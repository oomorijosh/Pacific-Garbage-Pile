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
    static readonly int[] Counts = { 520, 150, 30, 10 };
    // Visual height shares from the exhibit brief. They encode estimated mass,
    // not ocean depth or physical debris volume.
    static readonly float[] Shares = { .08f, .13f, .25f, .54f };
    const float MacroLift = .22f;
    const float MegaLift = .12f;
    Transform generatedRoot;
    System.Random random;
    [SerializeField, HideInInspector] Vector3 volumeSize;
    [SerializeField, HideInInspector] Material waterMaterial;
    [SerializeField, HideInInspector] bool generatedWithSizeMultiplier;
    readonly List<Floater> floaters=new List<Floater>();
    sealed class Floater
    {
        public Transform transform;
        public float baseHeight;
        public float draft, yaw, roll, phase, spin;
        public Vector2 position;
    }

    void Start()
    {
        SyncToWaterVolume();
        // Baked scene objects survive domain reload, while this runtime list
        // does not. Re-register the larger direct children for gentle motion.
        if (floaters.Count>0) return;
        if (!generatedRoot) generatedRoot=transform.Find("Debris size exhibit");
        if (!generatedRoot) return;
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
        volumeSize=size;
        materials=palette;
        impostorMaterial=atlasMaterial;
        waterMaterial=surfaceMaterial;
        modelGroups=modelPalette;
        debrisSizeMultiplier=Mathf.Clamp(debrisSizeMultiplier,.1f,1f);
        generatedWithSizeMultiplier=true;
        if (materials==null || materials.Length==0 || Array.Exists(materials,m=>!m))
        {
            Debug.LogWarning("Debris bands need a complete material palette.",this);
            return;
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
        float normalizedBottom=0;
        for (int i=0;i<band;i++) normalizedBottom+=Shares[i];
        float usableHeight=1f-bandGap*3f;
        float bandHeight=size.y*Shares[band]*usableHeight;
        float bottom=-size.y*.5f+size.y*(normalizedBottom*usableHeight+band*bandGap);
        float categoryMultiplier=band==0?microCountMultiplier:band==1?mesoCountMultiplier:1;
        int count=Mathf.RoundToInt(Counts[band]*Mathf.Clamp01(fill)*categoryMultiplier);
        float cellSize=Mathf.Pow(size.x*bandHeight*size.z/Mathf.Max(1,count),1f/3f);
        int columns=Mathf.Max(1,Mathf.CeilToInt(size.x/cellSize));
        int verticalCells=Mathf.Max(1,Mathf.CeilToInt(bandHeight/cellSize));
        int rows=Mathf.Max(1,Mathf.CeilToInt(size.z/cellSize));
        int capacity=columns*verticalCells*rows;
        int step=Mathf.Max(1,Mathf.RoundToInt(capacity*.6180339f));
        while (GreatestCommonDivisor(step,capacity)!=1) step++;
        for (int i=0;i<count;i++)
        {
            float baseScale=ScaleForBand(band);
            float horizontalMargin=Mathf.Min(Mathf.Min(size.x,size.z)*.32f,baseScale*1.1f);
            // Suspend debris throughout its category band. Margin is based on
            // prop size so larger pieces remain inside the shared cube.
            float margin=Mathf.Min(bandHeight*.28f,baseScale*1.05f);
            int cell=(i*step+capacity/2)%capacity;
            int xCell=cell%columns;
            int zCell=(cell/columns)%rows;
            int yCell=cell/(columns*rows);
            float x=Mathf.Lerp(-size.x*.5f+horizontalMargin,size.x*.5f-horizontalMargin,(xCell+.5f)/columns);
            float z=Mathf.Lerp(-size.z*.5f+horizontalMargin,size.z*.5f-horizontalMargin,(zCell+.5f)/rows);
            float lift=band==2?bandHeight*MacroLift:band==3?bandHeight*MegaLift:0;
            float y=Mathf.Lerp(bottom+margin,bottom+bandHeight-margin,(yCell+.5f)/verticalCells)+lift;
            float jitterX=size.x/columns*.28f;
            float jitterY=bandHeight/verticalCells*.28f;
            float jitterZ=size.z/rows*.28f;
            x=Mathf.Clamp(x+Range(-jitterX,jitterX),-size.x*.5f+horizontalMargin,size.x*.5f-horizontalMargin);
            y=Mathf.Clamp(y+Range(-jitterY,jitterY),bottom+margin,bottom+bandHeight-margin);
            z=Mathf.Clamp(z+Range(-jitterZ,jitterZ),-size.z*.5f+horizontalMargin,size.z*.5f-horizontalMargin);
            Transform piece=CreatePiece(root,band,i,new Vector3(x,y,z),baseScale);
            if (band>0) RegisterFloater(piece,baseScale*.08f,y);
        }
        CreateImpostors(root,band,size,bottom,bandHeight);
        if (band==3 && count>0)
        {
            Transform tire=CreateTire(root,new Vector3(size.x*.12f,bottom+bandHeight*.46f+bandHeight*MegaLift,-size.z*.18f),bandHeight*.18f*debrisSizeMultiplier);
            Transform net=CreateNetCluster(root,new Vector3(-size.x*.13f,bottom+bandHeight*.58f+bandHeight*MegaLift,0),bandHeight*.27f*debrisSizeMultiplier);
            RegisterFloater(tire,.04f,bottom+bandHeight*.46f);
            RegisterFloater(net,.06f,bottom+bandHeight*.58f);
        }
    }

    float ScaleForBand(int band)
    {
        switch (band)
        {
            case 0: return Range(.012f,.025f)*debrisSizeMultiplier;
            case 1: return Range(.028f,.065f)*debrisSizeMultiplier;
            case 2: return Range(.08f,.2f)*debrisSizeMultiplier;
            default: return Range(.28f,.46f)*debrisSizeMultiplier;
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
            model = choices[random.Next(choices.Length)];
        }
        Transform piece;
        Material material = materials[random.Next(materials.Length)];
        if (model)
        {
            GameObject instance = Instantiate(model, parent);
            instance.name = PieceName(band, shape) + " " + index.ToString("000");
            piece = instance.transform;
            piece.localPosition = position;
            piece.localRotation = RandomRotation();
            NormalizeModel(instance, scale, material);
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
        if (band>=1 && type==PrimitiveType.Capsule && shape%3==0)
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

    void NormalizeModel(GameObject instance, float targetSize, Material material)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            instance.transform.localScale = Vector3.one * targetSize;
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        instance.transform.localScale = Vector3.one * (largest > .0001f ? targetSize / largest : targetSize);
        foreach (Renderer renderer in renderers)
        {
            if (renderer.sharedMaterial == null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
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
        float yaw=Range(0,360), roll=Range(-7,7);
        item.localRotation=Quaternion.Euler(0,yaw,roll);
        floaters.Add(new Floater {
            transform=item, baseHeight=baseHeight, draft=draft, yaw=yaw, roll=roll,
            phase=Range(0,6.283f), spin=Range(-1,1),
            position=new Vector2(item.localPosition.x,item.localPosition.z)
        });
    }

    void Update()
    {
        SyncToWaterVolume();
        if (!Application.isPlaying) return;
        const float delta=.035f;
        foreach (Floater item in floaters)
        {
            if (!item.transform) continue;
            float wave=0, dx=0, dz=0;
            if (waterMaterial)
            {
                wave=SampleWave(item.position.x,item.position.y);
                dx=(SampleWave(item.position.x+delta,item.position.y)-SampleWave(item.position.x-delta,item.position.y))/(2*delta);
                dz=(SampleWave(item.position.x,item.position.y+delta)-SampleWave(item.position.x,item.position.y-delta))/(2*delta);
            }
            Vector3 position=item.transform.localPosition;
            float scaleY=Mathf.Max(.001f,Mathf.Abs(transform.lossyScale.y));
            float time=Time.time;
            position.x=item.position.x+Mathf.Sin(time*.31f+item.phase)*horizontalDrift;
            position.z=item.position.y+Mathf.Cos(time*.23f+item.phase*1.37f)*horizontalDrift*.72f;
            position.y=item.baseHeight-item.draft+wave/scaleY+
                Mathf.Sin(time*.72f+item.phase)*bobAmplitude;
            item.transform.localPosition=position;
            Vector3 normal=new Vector3(-dx,1,-dz).normalized;
            float yaw=item.yaw+time*rotationDegreesPerSecond*item.spin;
            float roll=item.roll+Mathf.Sin(time*.43f+item.phase)*4f;
            Quaternion target=Quaternion.FromToRotation(Vector3.up,normal)*Quaternion.Euler(0,yaw,roll);
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
        float u=Mathf.Clamp01(localX/volumeSize.x+.5f), v=Mathf.Clamp01(localZ/volumeSize.z+.5f);
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
