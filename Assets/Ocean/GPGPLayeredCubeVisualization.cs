using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the editable GPGP CyberCANOE data sculpture at runtime: one water
/// volume containing four plastic-size bands whose heights encode mass share.
/// </summary>
public sealed class GPGPLayeredCubeVisualization : MonoBehaviour
{
    [System.Serializable]
    public sealed class DebrisModelGroup
    {
        public GameObject[] models;
    }
    [Header("Exhibit dimensions")]
    public Vector3 cubeSize = new Vector3(4f, 5f, 3.5f);
    [Range(0.05f, 1f)] public float debrisDensity = 0.72f;
    [Range(.5f, 3f)] public float smallDebrisEmphasis = 1.5f;
    public int randomSeed = 485;
    public bool lowQualityMode;

    [Header("Water (Micro, Meso, Macro, Mega)")]
    public Material[] waterMaterials;
    public Material waterSideMaterial;
    [Tooltip("Use the underwater depth-and-caustics material on the cube walls and bottom.")]
    public bool useTexturedOceanSides = true;

    [Header("Debris")]
    public Material[] debrisMaterials;
    [Tooltip("Overall multiplier for baked debris meshes. 0.5 makes the props read as smaller pieces inside the exhibit.")]
    [Range(.1f, 1f)] public float debrisSizeMultiplier = .5f;
    [Tooltip("Optional FBX models for Micro, Meso, Macro and Mega debris. Each category can contain multiple models; one is chosen randomly per piece.")]
    public DebrisModelGroup[] debrisModelGroups = new DebrisModelGroup[4];
    public Material separatorMaterial;

    [Header("Presentation")]
    [Tooltip("Screen-space labels are optional because the CyberCANOE output is side-by-side stereo.")]
    public bool showLabels;
    [HideInInspector] public bool createScaleHuman = true;
    [HideInInspector] public bool createFloor = true;
    [HideInInspector] public bool createGalleryEnvironment = true;

    [HideInInspector] public bool bakedIntoScene;
    [HideInInspector] public Material bakedWaterMaterial;

    static readonly float[] Shares = { .08f, .13f, .25f, .54f };
    const float BandGap = .025f;
    const string GeneratedRootName = "WaterCube - one continuous volume";
    static readonly string[] GeneratedCubeChildNames =
    {
        "OuterVolume and single WaterSurface",
        "Four mass-share debris bands",
        "Mass-share boundary lines"
    };
    static readonly string[] CategoryLabels =
    {
        "MICROPLASTICS   0.5-5 mm   6,400 t   ~8%",
        "MESOPLASTICS   5-50 mm   10,000 t   ~13%",
        "MACROPLASTICS   5-50 cm   20,000 t   ~25%",
        "MEGAPLASTICS   >50 cm   42,000 t   ~53%"
    };

    Transform generatedRoot;
    Material continuousWaterMaterial;
    bool ownsWaterMaterial;
    GUIStyle titleStyle;
    GUIStyle labelStyle;
    GUIStyle noteStyle;

    void OnValidate()
    {
        if (debrisModelGroups == null || debrisModelGroups.Length != 4)
            System.Array.Resize(ref debrisModelGroups, 4);
        for (int i = 0; i < debrisModelGroups.Length; i++)
            if (debrisModelGroups[i] == null) debrisModelGroups[i] = new DebrisModelGroup();
    }

    void Start()
    {
        if (!bakedIntoScene) Build();
    }

    public void BakeIntoScene(Material persistentWaterMaterial)
    {
        ClearBakedExhibit();
        bakedWaterMaterial = persistentWaterMaterial;
        bakedIntoScene = true;
        Build();
    }

    public void ClearBakedExhibit()
    {
        generatedRoot = generatedRoot ? generatedRoot : transform.Find(GeneratedRootName);
        if (generatedRoot)
        {
            // Only remove content owned by the cube/debris generator. Anything
            // else under this root was authored in the scene and must survive.
            for (int i = generatedRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = generatedRoot.GetChild(i);
                if (IsGeneratedCubeChild(child.name)) Release(child.gameObject);
            }
        }
        continuousWaterMaterial = null;
        ownsWaterMaterial = false;
        bakedIntoScene = false;

        var sourceRenderer = GetComponent<MeshRenderer>();
        if (sourceRenderer) sourceRenderer.enabled = true;
        var sourceCollider = GetComponent<Collider>();
        if (sourceCollider) sourceCollider.enabled = true;
    }

    void Build()
    {
        if (waterMaterials == null || waterMaterials.Length != 4 ||
            System.Array.Exists(waterMaterials, material => !material))
        {
            Debug.LogError("Assign Micro, Meso, Macro and Mega water materials in that order.", this);
            enabled = false;
            return;
        }
        if (debrisMaterials == null || debrisMaterials.Length == 0 ||
            System.Array.Exists(debrisMaterials, material => !material))
        {
            Debug.LogError("Assign at least one complete debris material palette.", this);
            enabled = false;
            return;
        }

        var sourceRenderer = GetComponent<MeshRenderer>();
        if (sourceRenderer) sourceRenderer.enabled = false;
        var sourceCollider = GetComponent<Collider>();
        if (sourceCollider) sourceCollider.enabled = false;

        cubeSize = new Vector3(Mathf.Max(.5f, cubeSize.x), Mathf.Max(.5f, cubeSize.y), Mathf.Max(.5f, cubeSize.z));
        generatedRoot = transform.Find(GeneratedRootName);
        if (!generatedRoot)
        {
            generatedRoot = new GameObject(GeneratedRootName).transform;
            generatedRoot.SetParent(transform, false);
        }

        ownsWaterMaterial = !bakedWaterMaterial;
        continuousWaterMaterial = bakedWaterMaterial
            ? bakedWaterMaterial
            : new Material(waterMaterials[3]) { name = "GPGP continuous mass-share water" };
        ConfigureWaterMaterial();

        var water = new GameObject("OuterVolume and single WaterSurface").transform;
        water.SetParent(generatedRoot, false);
        var compartment = water.gameObject.AddComponent<GPGPWaterCompartment>();
        compartment.waterMaterial = continuousWaterMaterial;
        compartment.sideMaterial = useTexturedOceanSides && waterSideMaterial
            ? waterSideMaterial
            : continuousWaterMaterial;
        compartment.size = cubeSize;
        compartment.subdivisions = lowQualityMode ? 24 : 64;
        compartment.includeBottom = true;

        var bands = new GameObject("Four mass-share debris bands").AddComponent<GPGPDebrisBands>();
        bands.transform.SetParent(generatedRoot, false);
        bands.seed = randomSeed;
        bands.fill = Mathf.Clamp01(debrisDensity) * (lowQualityMode ? .42f : 1f);
        bands.microCountMultiplier = smallDebrisEmphasis;
        bands.mesoCountMultiplier = Mathf.Lerp(1f, smallDebrisEmphasis, .45f);
        bands.debrisSizeMultiplier = debrisSizeMultiplier;
        // Use only scale-controlled low-poly placeholders. Photo-atlas cards
        // are intentionally disabled because their pictured objects do not
        // preserve a meaningful relative scale between categories.
        bands.Build(cubeSize, debrisMaterials, null, continuousWaterMaterial, debrisModelGroups);

        CreateBandSeparators();
    }

    static bool IsGeneratedCubeChild(string objectName)
    {
        for (int i = 0; i < GeneratedCubeChildNames.Length; i++)
            if (objectName == GeneratedCubeChildNames[i]) return true;
        return false;
    }

    void ConfigureWaterMaterial()
    {
        continuousWaterMaterial.SetFloat("_LayeredTint", 1f);
        continuousWaterMaterial.SetFloat("_LayerBlendWidth", .012f);
        continuousWaterMaterial.SetFloat("_MicroBoundary", .08f);
        continuousWaterMaterial.SetFloat("_MesoBoundary", .21f);
        continuousWaterMaterial.SetFloat("_MacroBoundary", .46f);
        continuousWaterMaterial.SetFloat("_BoundaryEmissionStrength", .28f);
        continuousWaterMaterial.SetFloat("_WaveAmplitude", .055f);
        continuousWaterMaterial.SetFloat("_WaveAmplitude2", .014f);
        continuousWaterMaterial.SetFloat("_SpecularStrength", .1f);
        continuousWaterMaterial.SetFloat("_SurfaceOpacity", .13f);
        continuousWaterMaterial.SetFloat("_WaterOpacity", .13f);
        continuousWaterMaterial.SetFloat("_VolumeEmissionStrength", .18f);

        string[] properties = { "_MicroLayerColor", "_MesoLayerColor", "_MacroLayerColor", "_MegaLayerColor" };
        for (int i = 0; i < properties.Length; i++)
            continuousWaterMaterial.SetColor(properties[i], waterMaterials[i].GetColor("_WaterShallowColor"));
    }

    void CreateBandSeparators()
    {
        var root = new GameObject("Mass-share boundary lines").transform;
        root.SetParent(generatedRoot, false);
        float cumulative = 0f;
        for (int i = 0; i < Shares.Length - 1; i++)
        {
            cumulative += Shares[i] * (1f - BandGap * 3f);
            float y = -cubeSize.y * .5f + cubeSize.y * (cumulative + BandGap * (i + .5f));
            CreateSeparatorEdge(root, "Boundary " + Mathf.RoundToInt(cumulative * 100f) + "% front",
                new Vector3(0, y, -cubeSize.z * .501f), new Vector3(cubeSize.x, .035f, .018f));
            CreateSeparatorEdge(root, "Boundary " + Mathf.RoundToInt(cumulative * 100f) + "% back",
                new Vector3(0, y, cubeSize.z * .501f), new Vector3(cubeSize.x, .035f, .018f));
            CreateSeparatorEdge(root, "Boundary " + Mathf.RoundToInt(cumulative * 100f) + "% left",
                new Vector3(-cubeSize.x * .501f, y, 0), new Vector3(.018f, .035f, cubeSize.z));
            CreateSeparatorEdge(root, "Boundary " + Mathf.RoundToInt(cumulative * 100f) + "% right",
                new Vector3(cubeSize.x * .501f, y, 0), new Vector3(.018f, .035f, cubeSize.z));
        }
    }

    void CreateSeparatorEdge(Transform parent, string label, Vector3 position, Vector3 scale)
    {
        var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        edge.name = label;
        edge.transform.SetParent(parent, false);
        edge.transform.localPosition = position;
        edge.transform.localScale = scale;
        Release(edge.GetComponent<Collider>());
        var renderer = edge.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = separatorMaterial ? separatorMaterial : debrisMaterials[0];
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", new Color(.06f, .48f, .68f, 1f));
        properties.SetColor("_Color", new Color(.06f, .48f, .68f, 1f));
        Color glow = new Color(.02f, .65f, 1f, 1f);
        properties.SetColor("_EmissionColor", glow);
        properties.SetColor("_Emission", glow);
        properties.SetFloat("_EmissionStrength", 3.5f);
        renderer.SetPropertyBlock(properties);
    }

    void CreateHuman()
    {
        var human = new GameObject("ScaleHuman - display scale only").transform;
        human.SetParent(generatedRoot, false);
        human.localPosition = new Vector3(-cubeSize.x * .5f - .55f, -cubeSize.y * .5f, 0);

        CreateHumanPart(human, "Body", PrimitiveType.Capsule, new Vector3(0, .9f, 0), new Vector3(.42f, .9f, .42f));
        CreateHumanPart(human, "Head", PrimitiveType.Sphere, new Vector3(0, 1.85f, 0), Vector3.one * .38f);
    }

    void CreateHumanPart(Transform parent, string label, PrimitiveType type, Vector3 position, Vector3 scale)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = label;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Release(part.GetComponent<Collider>());
        part.GetComponent<MeshRenderer>().sharedMaterial = debrisMaterials[debrisMaterials.Length - 1];
    }

    void CreateGalleryFloor()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Dark gallery floor";
        floor.transform.SetParent(generatedRoot, false);
        floor.transform.localPosition = new Vector3(0, -cubeSize.y * .5f - .08f, 0);
        floor.transform.localScale = new Vector3(cubeSize.x * 2.8f, .12f, cubeSize.z * 2.8f);
        Release(floor.GetComponent<Collider>());
        var renderer = floor.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = debrisMaterials[0];
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", new Color(.012f, .018f, .025f, 1f));
        properties.SetColor("_Color", new Color(.012f, .018f, .025f, 1f));
        properties.SetFloat("_Smoothness", .52f);
        renderer.SetPropertyBlock(properties);
    }

    void CreateGalleryEnvironment()
    {
        // Solid-color clearing is important for the side-by-side stereo output;
        // the default procedural sky makes transparent water disappear.
        foreach (Camera camera in Camera.allCameras)
        {
            if (!camera.name.Contains("CC_") && camera.name != "CenterCamera") continue;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.004f, .008f, .015f, 1f);
        }

        var environment = new GameObject("Gallery lighting and backdrop").transform;
        environment.SetParent(generatedRoot, false);

        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backdrop.name = "Deep navy backdrop";
        backdrop.transform.SetParent(environment, false);
        backdrop.transform.localPosition = new Vector3(0, .4f, cubeSize.z * .5f + 2.2f);
        backdrop.transform.localScale = new Vector3(cubeSize.x * 3.2f, cubeSize.y * 2.1f, .12f);
        Release(backdrop.GetComponent<Collider>());
        var backdropRenderer = backdrop.GetComponent<MeshRenderer>();
        backdropRenderer.sharedMaterial = debrisMaterials[0];
        var backdropProperties = new MaterialPropertyBlock();
        backdropProperties.SetColor("_BaseColor", new Color(.006f, .014f, .026f, 1f));
        backdropProperties.SetColor("_Color", new Color(.006f, .014f, .026f, 1f));
        backdropProperties.SetFloat("_Smoothness", .08f);
        backdropRenderer.SetPropertyBlock(backdropProperties);

        var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plinth.name = "Low exhibit plinth";
        plinth.transform.SetParent(environment, false);
        plinth.transform.localPosition = new Vector3(0, -cubeSize.y * .5f - .16f, 0);
        plinth.transform.localScale = new Vector3(cubeSize.x * 1.12f, .2f, cubeSize.z * 1.12f);
        Release(plinth.GetComponent<Collider>());
        var plinthRenderer = plinth.GetComponent<MeshRenderer>();
        plinthRenderer.sharedMaterial = debrisMaterials[0];
        var plinthProperties = new MaterialPropertyBlock();
        plinthProperties.SetColor("_BaseColor", new Color(.018f, .035f, .05f, 1f));
        plinthProperties.SetColor("_Color", new Color(.018f, .035f, .05f, 1f));
        plinthProperties.SetFloat("_Smoothness", .68f);
        plinthRenderer.SetPropertyBlock(plinthProperties);

        CreateExhibitLight(environment, "Cool water key", new Color(.2f, .72f, 1f), 7f,
            new Vector3(-3.8f, 4.8f, -4.5f), 48f, true);
        CreateExhibitLight(environment, "Warm debris fill", new Color(1f, .48f, .2f), 3.2f,
            new Vector3(4.2f, 2.5f, -2.5f), 58f, false);
        CreateExhibitLight(environment, "Water rim", new Color(.05f, .65f, 1f), 4.5f,
            new Vector3(0, 4.2f, 3.2f), 62f, false);
    }

    void CreateExhibitLight(Transform parent, string label, Color color, float intensity,
        Vector3 position, float angle, bool shadows)
    {
        var lightObject = new GameObject(label, typeof(Light));
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localPosition = position;
        lightObject.transform.localRotation = Quaternion.LookRotation(-position.normalized, Vector3.up);
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Spot;
        light.color = color;
        light.intensity = intensity;
        light.range = 16f;
        light.spotAngle = angle;
        light.innerSpotAngle = angle * .55f;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
    }

    void OnGUI()
    {
        if (!showLabels || !enabled) return;
        EnsureStyles();
        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), .65f, 1.5f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUILayout.BeginArea(new Rect(24, 24, 470, 310), GUI.skin.box);
        GUILayout.Label("GREAT PACIFIC GARBAGE PATCH", titleStyle);
        GUILayout.Label("Floating plastic mass-share data sculpture", noteStyle);
        for (int i = CategoryLabels.Length - 1; i >= 0; i--)
            GUILayout.Label(CategoryLabels[i], labelStyle);
        GUILayout.Space(8);
        GUILayout.Label("Illustrative mass-share visualization; not an actual ocean-depth cross-section.", noteStyle);
        GUILayout.Label("Source: Lebreton et al., Scientific Reports (2018). Heights use 8/13/25/54 for a complete visual cube; the measured mega share is shown as ~53%.", noteStyle);
        GUILayout.EndArea();
        GUI.matrix = previous;
    }

    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
        labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
        noteStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
    }

    void OnDestroy()
    {
        if (ownsWaterMaterial && continuousWaterMaterial) Release(continuousWaterMaterial);
    }

    static void Release(Object item)
    {
        if (!item) return;
        if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
    }
}
