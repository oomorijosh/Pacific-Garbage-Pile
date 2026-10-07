using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds four deterministic, exhibit-style plastic size bands inside a water volume.</summary>
[ExecuteAlways]
public sealed class GPGPDebrisBands : MonoBehaviour
{
    [Range(0,1)] public float fill = 1;
    [Range(.5f,3f)] public float microCountMultiplier = 1;
    [Range(.5f,3f)] public float mesoCountMultiplier = 1;
    [Tooltip("Overall size multiplier applied to every generated debris prop.")]
    [Range(.1f,1f)] public float debrisSizeMultiplier = .5f;
    [Tooltip("Empty vertical space between categories so each debris layer reads clearly.")]
    [Range(0f,.06f)] public float bandGap = .025f;
    public int seed = 485;
    public Material[] materials;
    public Material impostorMaterial;
    public GPGPLayeredCubeVisualization.DebrisModelGroup[] modelGroups;
    [Header("Clutter and upward mixing")]
    [Tooltip("Chance of placing a piece in a seeded accumulation cluster instead of the wider cloud.")]
    [Range(0,1)] public float clusterStrength = .72f;
    [Range(2,10)] public int clusterCount = 6;
    [Tooltip("Additional Meso pieces placed in the Macro section, as a fraction of the Meso count.")]
    [Range(0,1)] public float mesoIntoMacro = .35f;
    [Tooltip("Additional Meso pieces placed in the Mega section, as a fraction of the Meso count.")]
    [Range(0,1)] public float mesoIntoMega = .55f;
    [Tooltip("Additional Macro pieces placed in the Mega section, as a fraction of the Macro count.")]
    [Range(0,1)] public float macroIntoMega = .65f;
    [Header("Motion")]
    [Range(0,.2f)] public float bobAmplitude = .065f;
    [Range(0,.25f)] public float horizontalDrift = .075f;
    [Range(0,20)] public float rotationDegreesPerSecond = 6f;

    static readonly string[] Labels =
    {
        "MICRO  < 5 mm", "MESO  5 mm - 5 cm", "MACRO  5 - 50 cm", "MEGA  > 50 cm"
    };
    // Counts favor legibility on a stereoscopic wall; the four band heights,
    // not object counts, carry the mass-share data.
    static readonly int[] Counts = { 520, 180, 100, 70 };
    static readonly string[] SourceLabels = { "MICRO", "MESO", "MACRO", "MEGA" };
    // Visual height shares from the exhibit brief. They encode estimated mass,
    // not ocean depth or physical debris volume.
    static readonly float[] Shares = { .08f, .13f, .25f, .54f };
    const float MegaLift = .12f;
    Transform generatedRoot;
    System.Random random;
    [SerializeField, HideInInspector] Vector3 volumeSize;
    // Placement coordinates remain in the dimensions used when this branch
    // was built. Parent scaling follows later water edits without changing
    // the bounds/radii used to keep those placements inside their sections.
    [SerializeField, HideInInspector] Vector3 generatedVolumeSize;
    [SerializeField, HideInInspector] Material waterMaterial;
    [SerializeField, HideInInspector] bool generatedWithSizeMultiplier;
    [SerializeField, HideInInspector] List<PiecePlacement> placements = new List<PiecePlacement>();
    public IReadOnlyList<PiecePlacement> Placements => placements;
    [Serializable]
    public sealed class PiecePlacement
    {
        public Transform transform;
        public int sourceBand, destinationBand;
        public Vector3 basePosition;
        public Quaternion baseRotation;
        public float radius, phase, spin;
        public Vector3 tumbleAxis;
    }
    readonly List<Floater> floaters=new List<Floater>();
    sealed class Floater
    {
        public Transform transform;
        public PiecePlacement placement;
    }

    void Start()
    {
        SyncToWaterVolume();
        // Baked scene objects survive domain reload, while this runtime list
        // does not. Re-register the larger direct children for gentle motion.
        floaters.RemoveAll(item => !item.transform);
        if (floaters.Count>0) return;
        if (!generatedRoot) generatedRoot=transform.Find("Debris size exhibit");
        if (!generatedRoot) return;
        if (placements.Count>0)
        {
            foreach (PiecePlacement placement in placements)
                if (placement.transform && placement.sourceBand>0) RegisterFloater(placement);
            return;
        }
        random=new System.Random(seed);
        for (int band=1;band<generatedRoot.childCount;band++)
        {
            Transform bandRoot=generatedRoot.GetChild(band);
            for (int i=0;i<bandRoot.childCount;i++)
            {
                Transform item=bandRoot.GetChild(i);
                // Older baked scenes were authored before the size multiplier
                // existed. Scale their generated roots once when they load.
                if (!generatedWithSizeMultiplier)
                    item.localScale *= Mathf.Clamp(debrisSizeMultiplier,.1f,1f);
                RegisterFloater(item,Mathf.Max(.004f,item.localScale.y*.04f),item.localPosition.y);
            }
        }
        generatedWithSizeMultiplier=true;
    }

    void SyncToWaterVolume()
    {
        if (!generatedRoot) generatedRoot=transform.Find("Debris size exhibit");
        if (generatedRoot && !generatedWithSizeMultiplier)
        {
            for (int band=0; band<generatedRoot.childCount; band++)
            {
                Transform bandRoot=generatedRoot.GetChild(band);
                for (int i=0; i<bandRoot.childCount; i++)
                    bandRoot.GetChild(i).localScale *= Mathf.Clamp(debrisSizeMultiplier,.1f,1f);
            }
            generatedWithSizeMultiplier=true;
        }
        Transform owner = transform.parent;
        if (!owner) return;
        GPGPWaterCompartment compartment = owner.GetComponentInChildren<GPGPWaterCompartment>();
        if (!compartment) return;

        Vector3 currentSize = new Vector3(Mathf.Max(.001f, compartment.size.x),
            Mathf.Max(.001f, compartment.size.y), Mathf.Max(.001f, compartment.size.z));
        if (generatedVolumeSize.x <= .001f || generatedVolumeSize.y <= .001f || generatedVolumeSize.z <= .001f)
        {
            // Older baked scenes contain only the last synchronized size.
            generatedVolumeSize = volumeSize.x > .001f && volumeSize.y > .001f && volumeSize.z > .001f
                ? volumeSize : currentSize;
        }
        if (volumeSize.x <= .001f || volumeSize.y <= .001f || volumeSize.z <= .001f)
        {
            volumeSize = currentSize;
            return;
        }
        if ((currentSize-volumeSize).sqrMagnitude < .000001f) return;

        // The water and debris are siblings. Scale this branch from the last
        // generated dimensions so positions, meshes, and band heights remain
        // proportional when the cube changes after baking.
        Vector3 ratio = new Vector3(currentSize.x/volumeSize.x,
            currentSize.y/volumeSize.y, currentSize.z/volumeSize.z);
        transform.localScale = Vector3.Scale(transform.localScale, ratio);
        volumeSize = currentSize;
    }

    public void Build(Vector3 size, Material[] palette, Material atlasMaterial, Material surfaceMaterial,
        GPGPLayeredCubeVisualization.DebrisModelGroup[] modelPalette = null)
    {
        if (palette==null || palette.Length==0 || Array.Exists(palette,m=>!m))
        {
            Debug.LogWarning("Debris bands need a complete material palette.",this);
            return;
        }

        volumeSize=size;
        generatedVolumeSize=size;
        transform.localScale=Vector3.one;
        materials=palette;
        impostorMaterial=atlasMaterial;
        waterMaterial=surfaceMaterial;
        modelGroups=modelPalette;
        debrisSizeMultiplier=Mathf.Clamp(debrisSizeMultiplier,.1f,1f);
        generatedWithSizeMultiplier=true;
        // Rebuild only this component's generated branch. Water, boundary lines
        // and authored siblings remain untouched, including on repeated builds.
        floaters.Clear();
        placements.Clear();
        for (int i=transform.childCount-1; i>=0; i--)
        {
            Transform child=transform.GetChild(i);
            if (child.name!="Debris size exhibit") continue;
            child.gameObject.SetActive(false);
            Release(child.gameObject);
        }
        random=new System.Random(seed);
        generatedRoot=new GameObject("Debris size exhibit").transform;
        generatedRoot.SetParent(transform,false);
        for (int band=0; band<4; band++) BuildBand(band,volumeSize);
    }

    void BuildBand(int band, Vector3 size)
    {
        var root=new GameObject(Labels[band]).transform;
        root.SetParent(generatedRoot,false);
        Bounds destination=GetDestinationBounds(band);
        float bottom=destination.min.y, bandHeight=destination.size.y;
        var clusters=new Vector3[Mathf.Clamp(clusterCount,2,10)];
        for (int i=0; i<clusters.Length; i++)
            clusters[i]=new Vector3(Range(-size.x*.34f,size.x*.34f),
                Mathf.Lerp(bottom,bottom+bandHeight,band==0?Range(.1f,.4f):band==3?Range(.6f,.9f):Range(.2f,.85f)),
                Range(-size.z*.34f,size.z*.34f));
        int count=CountForBand(band);
        SpawnCloud(root,band,band,count,clusters);
        // Smaller sources may accumulate upward. Larger sources are never
        // selected for a lower destination, and Micro stays on the bottom.
        if (band==2) SpawnCloud(root,1,band,Mathf.RoundToInt(CountForBand(1)*mesoIntoMacro),clusters);
        if (band==3)
        {
            SpawnCloud(root,1,band,Mathf.RoundToInt(CountForBand(1)*mesoIntoMega),clusters);
            SpawnCloud(root,2,band,Mathf.RoundToInt(CountForBand(2)*macroIntoMega),clusters);
        }
        CreateImpostors(root,band,size,bottom,bandHeight);
        if (band==3 && count>0 && !HasModelsForBand(band))
        {
            Transform tire=CreateTire(root,new Vector3(size.x*.12f,bottom+bandHeight*.46f+bandHeight*MegaLift,-size.z*.18f),bandHeight*.18f*debrisSizeMultiplier);
            Transform net=CreateNetCluster(root,new Vector3(-size.x*.13f,bottom+bandHeight*.58f+bandHeight*MegaLift,0),bandHeight*.27f*debrisSizeMultiplier);
            RegisterFloater(tire,.04f,bottom+bandHeight*.46f);
            RegisterFloater(net,.06f,bottom+bandHeight*.58f);
        }
    }

    int CountForBand(int band)
    {
        float multiplier=band==0?microCountMultiplier:band==1?mesoCountMultiplier:1;
        int count=Mathf.RoundToInt(Counts[band]*Mathf.Clamp01(fill)*multiplier);
        if (fill>0 && HasModelsForBand(band)) count=Mathf.Max(count,modelGroups[band].models.Length);
        return count;
    }

    public Bounds GetDestinationBounds(int band)
    {
        band=Mathf.Clamp(band,0,3);
        Vector3 size = generatedVolumeSize.x > .001f && generatedVolumeSize.y > .001f && generatedVolumeSize.z > .001f
            ? generatedVolumeSize : volumeSize;
        float cumulative=0;
        for (int i=0; i<band; i++) cumulative+=Shares[i];
        float usable=1f-bandGap*3f;
        float height=size.y*Shares[band]*usable;
        float bottom=-size.y*.5f+size.y*(cumulative*usable+band*bandGap);
        return new Bounds(new Vector3(0,bottom+height*.5f,0),new Vector3(size.x,height,size.z));
    }

    void SpawnCloud(Transform parent, int sourceBand, int destinationBand, int count, Vector3[] clusters)
    {
        Bounds destination=GetDestinationBounds(destinationBand);
        for (int i=0; i<count; i++)
        {
            float scale=ScaleForBand(sourceBand);
            float t=(float)random.NextDouble();
            if (destinationBand==0) t*=.45f;
            else if (destinationBand>=2) t=1f-Mathf.Pow(t,destinationBand==3?1.9f:1.35f);
            Vector3 position=new Vector3(Range(destination.min.x,destination.max.x),
                Mathf.Lerp(destination.min.y,destination.max.y,t),Range(destination.min.z,destination.max.z));
            float clustering=destinationBand==0?clusterStrength*.35f:clusterStrength;
            if (random.NextDouble()<clustering)
            {
                Vector3 center=clusters[random.Next(clusters.Length)];
                position=center+new Vector3(CloudJitter(destination.size.x*.13f),
                    CloudJitter(destination.size.y*.13f),CloudJitter(destination.size.z*.15f));
            }
            position=ClampToDestination(position,destinationBand,scale*.9f);
            Transform piece=CreatePiece(parent,sourceBand,i,position,scale);
            piece.name=SourceLabels[sourceBand]+" | "+piece.name;
            PiecePlacement placement=CreatePlacement(piece,sourceBand,destinationBand);
            if (sourceBand>0) RegisterFloater(placement);
        }
    }

    float CloudJitter(float spread) => (Range(-1,1)+Range(-1,1)+Range(-1,1))*spread;

    Vector3 ClampToDestination(Vector3 position, int band, float radius)
    {
        Bounds bounds=GetDestinationBounds(band);
        Vector3 margin=new Vector3(Mathf.Min(radius+.015f,bounds.extents.x*.98f),
            Mathf.Min(radius+.015f,bounds.extents.y*.98f),Mathf.Min(radius+.015f,bounds.extents.z*.98f));
        Vector3 minimum=bounds.min+margin, maximum=bounds.max-margin;
        return new Vector3(Mathf.Clamp(position.x,minimum.x,maximum.x),
            Mathf.Clamp(position.y,minimum.y,maximum.y),Mathf.Clamp(position.z,minimum.z,maximum.z));
    }

    float ScaleForBand(int band)
    {
        switch (band)
        {
            case 0: return Range(.035f,.075f)*debrisSizeMultiplier;
            case 1: return Range(.12f,.32f)*debrisSizeMultiplier;
            case 2: return Range(.45f,1.1f)*debrisSizeMultiplier;
            default: return Range(1.2f,2.2f)*debrisSizeMultiplier;
        }
    }

    Transform CreatePiece(Transform parent, int band, int index, Vector3 position, float scale)
    {
        PrimitiveType type;
        int shape=random.Next(100);
        if (band==0) type=shape<58?PrimitiveType.Sphere:PrimitiveType.Cube;
        else if (band==1) type=shape<38?PrimitiveType.Cube:shape<72?PrimitiveType.Sphere:PrimitiveType.Capsule;
        else type=shape<48?PrimitiveType.Cube:shape<74?PrimitiveType.Capsule:PrimitiveType.Cylinder;
        GameObject model = null;
        if (modelGroups != null && band < modelGroups.Length && modelGroups[band] != null &&
            modelGroups[band].models != null && modelGroups[band].models.Length > 0)
        {
            GameObject[] choices = modelGroups[band].models;
            // Show each assigned model before choosing random repetitions.
            model = choices[index < choices.Length ? index : random.Next(choices.Length)];
        }
        Transform piece;
        Material material = materials[random.Next(materials.Length)];
        if (model)
        {
            // A centred wrapper gives imported pivots and transforms a stable
            // origin for category placement, scale and floater motion.
            piece = new GameObject(model.name + " " + index.ToString("000")).transform;
            piece.SetParent(parent, false);
            piece.localPosition = position;
            piece.localRotation = RandomRotation();
            GameObject instance = Instantiate(model, piece);
            instance.name = model.name;
            NormalizeModel(piece, instance, scale, material);
            RemoveColliders(instance);
        }
        else
        {
            GameObject primitive=GameObject.CreatePrimitive(type);
            primitive.name=PieceName(band,shape)+" "+index.ToString("000");
            primitive.transform.SetParent(parent,false);
            primitive.transform.localPosition=position;
            primitive.transform.localRotation=RandomRotation();
            primitive.transform.localScale=PieceScale(type,band,scale);
            Configure(primitive,material);
            piece = primitive.transform;
        }

        // A short neck turns some capsules into recognizable bottle silhouettes.
        if (!model && band>=1 && type==PrimitiveType.Capsule && shape%3==0)
        {
            var neck=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            neck.name="Bottle neck";
            neck.transform.SetParent(piece.transform,false);
            neck.transform.localPosition=new Vector3(0,.62f,0);
            neck.transform.localScale=new Vector3(.28f,.18f,.28f);
            Configure(neck,piece.GetComponent<MeshRenderer>().sharedMaterial);
        }
        return piece;
    }

    bool HasModelsForBand(int band)
    {
        return modelGroups!=null && band<modelGroups.Length && modelGroups[band]!=null &&
            modelGroups[band].models!=null && Array.Exists(modelGroups[band].models,model=>model);
    }

    void NormalizeModel(Transform wrapper, GameObject instance, float targetSize, Material material)
    {
        if (TryGetLocalMeshBounds(wrapper, instance, out Bounds bounds))
        {
            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float normalization = largest > .0001f ? 1f / largest : 1f;
            instance.transform.localPosition = (instance.transform.localPosition - bounds.center) * normalization;
            instance.transform.localScale *= normalization;
        }
        // Mesh bounds are now one unit across their largest local dimension.
        // Parent scaling belongs to the exhibit, not to model normalization.
        wrapper.localScale = Vector3.one * targetSize;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            // Keep authored URP materials and their textures. Fill only missing
            // or incompatible slots with the exhibit's existing palette.
            Material[] slots = renderer.sharedMaterials;
            bool changed = slots.Length == 0;
            if (slots.Length == 0) slots = new Material[1];
            for (int i = 0; i < slots.Length; i++)
            {
                if (IsCompatibleUrpMaterial(slots[i])) continue;
                slots[i] = material;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = slots;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    static bool IsCompatibleUrpMaterial(Material material)
    {
        if (!material || !material.shader || !material.shader.isSupported) return false;
        // Missing imported materials resolve to URP's glossy white default.
        // Use the exhibit palette for those slots, preserving authored textures.
        RenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline;
        if (pipelineAsset && material == pipelineAsset.defaultMaterial) return false;
        string pipeline = material.GetTag("RenderPipeline", false, string.Empty);
        return pipeline == "UniversalPipeline" ||
            (string.IsNullOrEmpty(pipeline) && material.shader.name.StartsWith(
                "Universal Render Pipeline/", StringComparison.Ordinal));
    }

    static bool TryGetLocalMeshBounds(Transform wrapper, GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        Matrix4x4 worldToWrapper = wrapper.worldToLocalMatrix;
        foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh) continue;
            EncapsulateMeshBounds(filter.sharedMesh.bounds, worldToWrapper * filter.transform.localToWorldMatrix,
                ref bounds, ref found);
        }
        foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!renderer.sharedMesh) continue;
            EncapsulateMeshBounds(renderer.localBounds, worldToWrapper * renderer.transform.localToWorldMatrix,
                ref bounds, ref found);
        }
        return found;
    }

    static void EncapsulateMeshBounds(Bounds meshBounds, Matrix4x4 toWrapper, ref Bounds bounds, ref bool found)
    {
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = new Vector3(
                (corner & 1) == 0 ? -meshBounds.extents.x : meshBounds.extents.x,
                (corner & 2) == 0 ? -meshBounds.extents.y : meshBounds.extents.y,
                (corner & 4) == 0 ? -meshBounds.extents.z : meshBounds.extents.z);
            Vector3 point = toWrapper.MultiplyPoint3x4(meshBounds.center + offset);
            if (found) bounds.Encapsulate(point);
            else
            {
                bounds = new Bounds(point, Vector3.zero);
                found = true;
            }
        }
    }

    static void RemoveColliders(GameObject instance)
    {
        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Release(collider);
    }

    static string PieceName(int band, int shape)
    {
        if (band==0) return "Plastic fleck";
        if (band==1) return shape<38?"Fragment":shape<72?"Pellet":"Small bottle";
        if (band==2) return shape<48?"Container":shape<74?"Bottle":"Float";
        return shape<48?"Crate":shape<74?"Large bottle":"Buoy";
    }

    Vector3 PieceScale(PrimitiveType type, int band, float s)
    {
        if (type==PrimitiveType.Cube)
            return new Vector3(s*Range(.65f,1.5f),s*Range(.35f,.85f),s*Range(.65f,1.35f));
        if (type==PrimitiveType.Cylinder)
            return new Vector3(s*Range(.5f,.85f),s*Range(.35f,.8f),s*Range(.5f,.85f));
        if (type==PrimitiveType.Capsule)
            return new Vector3(s*Range(.4f,.7f),s*Range(.65f,1.3f),s*Range(.4f,.7f));
        float flatten=band==0?Range(.35f,1):Range(.7f,1.2f);
        return new Vector3(s*Range(.7f,1.25f),s*flatten,s*Range(.7f,1.25f));
    }

    Transform CreateTire(Transform parent, Vector3 position, float radius)
    {
        var tire=new GameObject("MEGA tire").transform;
        tire.SetParent(parent,false); tire.localPosition=position;
        tire.localRotation=Quaternion.Euler(75,18,0);
        const int segments=14;
        for (int i=0;i<segments;i++)
        {
            float angle=i*Mathf.PI*2/segments;
            var segment=GameObject.CreatePrimitive(PrimitiveType.Capsule);
            segment.name="Tire segment"; segment.transform.SetParent(tire,false);
            segment.transform.localPosition=new Vector3(Mathf.Cos(angle)*radius,Mathf.Sin(angle)*radius,0);
            segment.transform.localRotation=Quaternion.Euler(0,0,angle*Mathf.Rad2Deg+90);
            segment.transform.localScale=new Vector3(radius*.28f,radius*.24f,radius*.28f);
            Configure(segment,materials[0]);
        }
        return tire;
    }

    Transform CreateNetCluster(Transform parent, Vector3 position, float width)
    {
        var net=new GameObject("MEGA rope and net cluster").transform;
        net.SetParent(parent,false); net.localPosition=position;
        for (int i=0;i<12;i++)
        {
            var strand=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            strand.name="Rope strand"; strand.transform.SetParent(net,false);
            strand.transform.localPosition=new Vector3(Range(-width,width),Range(-width*.35f,width*.35f),Range(-width*.5f,width*.5f));
            strand.transform.localRotation=Quaternion.Euler(Range(55,125),Range(0,180),Range(0,180));
            strand.transform.localScale=new Vector3(width*.035f,Range(width*.4f,width),width*.035f);
            Configure(strand,materials[(i+2)%materials.Length]);
        }
        return net;
    }

    void CreateImpostors(Transform parent, int band, Vector3 size, float bottom, float bandHeight)
    {
        if (!impostorMaterial || band==0) return;
        int baseCount=band==1?6:band==2?8:5;
        int count=Mathf.RoundToInt(baseCount*Mathf.Clamp01(fill));
        string[] names={"Bottle","Detergent jug","Crate","Buoy","Tire","Fishing net","Rope","Foam float",
            "Bucket","Fuel can","Broken container","Flip-flop","Plastic bag","Green bottle","Fishing trap","Plastic fragments"};
        for (int i=0;i<count;i++)
        {
            int cell=random.Next(16);
            var card=GameObject.CreatePrimitive(PrimitiveType.Quad);
            card.name="Atlas "+names[cell]; card.transform.SetParent(parent,false);
            float x=Range(-size.x*.41f,size.x*.41f), z=Range(-size.z*.37f,size.z*.37f);
            float cardSize=(band==1?Range(.018f,.035f):band==2?Range(.07f,.17f):Range(.24f,.4f))*debrisSizeMultiplier;
            float margin=Mathf.Min(bandHeight*.22f,cardSize*.6f);
            float y=Range(bottom+margin,bottom+bandHeight-margin);
            card.transform.localPosition=new Vector3(x,y,z);
            card.transform.localScale=new Vector3(cardSize,cardSize,cardSize);
            var collider=card.GetComponent<Collider>(); if (collider) Release(collider);
            var renderer=card.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=impostorMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            var properties=new MaterialPropertyBlock();
            properties.SetVector("_Cell",new Vector4(cell%4,3-cell/4,0,0));
            renderer.SetPropertyBlock(properties);
            RegisterFloater(card.transform,cardSize*.08f,y);
        }
    }

    void RegisterFloater(Transform item, float draft, float baseHeight)
    {
        // Compatibility for older baked hierarchies and optional atlas props.
        int destination=item.parent.GetSiblingIndex();
        RegisterFloater(CreatePlacement(item,destination,destination));
    }

    PiecePlacement CreatePlacement(Transform piece, int sourceBand, int destinationBand)
    {
        float radius=Mathf.Max(.004f,piece.localScale.magnitude*.5f);
        if (TryGetLocalMeshBounds(piece,piece.gameObject,out Bounds modelBounds))
            radius=Vector3.Scale(modelBounds.center,piece.localScale).magnitude+
                Vector3.Scale(modelBounds.extents,piece.localScale).magnitude;
        Bounds destination=GetDestinationBounds(destinationBand);
        float maximumRadius=Mathf.Max(.001f,Mathf.Min(destination.extents.x,
            Mathf.Min(destination.extents.y,destination.extents.z))-.015f);
        if (radius>maximumRadius)
        {
            // Extremely small edited water volumes still contain the whole
            // piece, including while it tumbles at runtime.
            piece.localScale*=maximumRadius/radius;
            radius=maximumRadius;
        }
        piece.localPosition=ClampToDestination(piece.localPosition,destinationBand,radius);
        Vector3 axis=new Vector3(Range(-1,1),Range(-1,1),Range(-1,1));
        if (axis.sqrMagnitude<.0001f) axis=Vector3.up;
        var placement=new PiecePlacement {
            transform=piece,sourceBand=sourceBand,destinationBand=destinationBand,
            basePosition=piece.localPosition,baseRotation=piece.localRotation,
            radius=radius,phase=Range(0,6.283f),spin=Range(-1,1),tumbleAxis=axis.normalized
        };
        placements.Add(placement);
        return placement;
    }

    void RegisterFloater(PiecePlacement placement)
    {
        if (placement.sourceBand==0) return;
        floaters.Add(new Floater { transform=placement.transform,placement=placement });
    }

    void Update()
    {
        SyncToWaterVolume();
        if (!Application.isPlaying) return;
        const float delta=.035f;
        foreach (Floater item in floaters)
        {
            if (!item.transform) continue;
            PiecePlacement placement=item.placement;
            float wave=0, dx=0, dz=0;
            if (waterMaterial)
            {
                float x=placement.basePosition.x, z=placement.basePosition.z;
                wave=SampleWave(x,z);
                dx=(SampleWave(x+delta,z)-SampleWave(x-delta,z))/(2*delta);
                dz=(SampleWave(x,z+delta)-SampleWave(x,z-delta))/(2*delta);
            }
            Vector3 position=placement.basePosition;
            float scaleY=Mathf.Max(.001f,Mathf.Abs(transform.lossyScale.y));
            float time=Time.time;
            position.x+=Mathf.Sin(time*.31f+placement.phase)*horizontalDrift;
            position.z+=Mathf.Cos(time*.23f+placement.phase*1.37f)*horizontalDrift*.72f;
            position.y+=wave/scaleY+Mathf.Sin(time*.72f+placement.phase)*bobAmplitude;
            item.transform.localPosition=ClampToDestination(position,placement.destinationBand,placement.radius);
            Vector3 normal=new Vector3(-dx,1,-dz).normalized;
            Quaternion tumble=Quaternion.AngleAxis(time*rotationDegreesPerSecond*placement.spin,placement.tumbleAxis);
            Quaternion rock=Quaternion.AngleAxis(Mathf.Sin(time*.43f+placement.phase)*4f,Vector3.forward);
            Quaternion target=Quaternion.FromToRotation(Vector3.up,normal)*rock*tumble*placement.baseRotation;
            item.transform.localRotation=Quaternion.Slerp(item.transform.localRotation,target,Time.deltaTime*2.5f);
        }
    }

    float SampleWave(float localX, float localZ)
    {
        Vector3 world=transform.TransformPoint(new Vector3(localX,0,localZ));
        Vector4 direction=waterMaterial.GetVector("_WaveDirection");
        Vector4 direction2=waterMaterial.GetVector("_WaveDirection2");
        Vector2 d=new Vector2(direction.x,direction.y).normalized;
        Vector2 d2=new Vector2(direction2.x,direction2.y).normalized;
        float phase=(world.x*d.x+world.z*d.y)*waterMaterial.GetFloat("_WaveFrequency")+Time.time*waterMaterial.GetFloat("_WaveSpeed");
        float phase2=(world.x*d2.x+world.z*d2.y)*waterMaterial.GetFloat("_WaveFrequency2")+Time.time*waterMaterial.GetFloat("_WaveSpeed2");
        Vector3 size = generatedVolumeSize.x > .001f && generatedVolumeSize.z > .001f ? generatedVolumeSize : volumeSize;
        float u=Mathf.Clamp01(localX/size.x+.5f), v=Mathf.Clamp01(localZ/size.z+.5f);
        float envelope=Mathf.Sin(u*Mathf.PI)*Mathf.Sin(v*Mathf.PI);
        return (waterMaterial.GetFloat("_WaveAmplitude")*Mathf.Sin(phase)+
            waterMaterial.GetFloat("_WaveAmplitude2")*Mathf.Sin(phase2))*envelope;
    }

    void Configure(GameObject piece, Material material)
    {
        var collider=piece.GetComponent<Collider>();
        if (collider) Release(collider);
        var renderer=piece.GetComponent<MeshRenderer>();
        renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.On;
        renderer.receiveShadows=true;
    }

    float Range(float min, float max) { return min+(float)random.NextDouble()*(max-min); }
    static int GreatestCommonDivisor(int a, int b)
    {
        while (b!=0) { int remainder=a%b; a=b; b=remainder; }
        return a;
    }
    Quaternion RandomRotation()
    {
        return Quaternion.Euler(Range(0,360),Range(0,360),Range(0,360));
    }

    void OnDestroy()
    {
        // In Edit Mode the generated children may be intentionally baked into
        // the scene. Their parent hierarchy owns their lifetime.
        if (Application.isPlaying && generatedRoot) Destroy(generatedRoot.gameObject);
    }

    static void Release(UnityEngine.Object item)
    {
        if (!item) return;
        if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
    }
}
