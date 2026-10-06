using System;
using System.Collections.Generic;
using System.IO;
using PacificExhibit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Creates editable scene assets, with no external art or water package.</summary>
public static class PacificExhibitBuilder
{
    const string AssetRoot = "Assets/PacificExhibit";
    const string ScenePath = "Assets/Scenes/PacificExhibit.unity";
    const float Width = 3.8f, Height = 4.8f, Depth = 2.4f, Base = 0.18f;
    static Material frame, metal, wall, floor, glass, ceiling;
    static Mesh surfaceMesh;
    static readonly List<Renderer> FrameBars = new List<Renderer>();
    static readonly List<Renderer> GlassPanels = new List<Renderer>();
    static int previewFrames;
    static double previewDeadline;
    static bool batchPreview;

    [MenuItem("Tools/Pacific Exhibit/Open Standalone Scene")]
    public static void OpenScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Tools/Pacific Exhibit/Rebuild Generated Scenes")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildAssets();
    }

    // Called with Unity -batchmode -executeMethod PacificExhibitBuilder.BuildAll.
    public static void BuildAll()
    {
        try
        {
            BuildAssets();
            batchPreview = true;
            StartPreview();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void BuildAssets()
    {
        Directory.CreateDirectory(AssetRoot + "/Materials");
        Directory.CreateDirectory(AssetRoot + "/Meshes");
        Directory.CreateDirectory(AssetRoot + "/Prefabs");
        AssetDatabase.Refresh();
        frame = MaterialAsset("Frame_Cyan", "PacificExhibit/Glow");
        frame.SetColor("_BaseColor", new Color(0.14f, 0.75f, 1f) * 4f);
        glass = MaterialAsset("Glass_ClearBlue", "PacificExhibit/Glass");
        glass.SetColor("_BaseColor", new Color(0.015f, 0.14f, 0.23f));
        glass.SetFloat("_Opacity", 0.035f);
        glass.SetFloat("_EdgeGlow", 0.18f);
        glass.SetFloat("_FresnelPower", 4f);
        metal = LitMaterial("Plinth_Graphite", new Color(0.014f, 0.025f, 0.033f), 0.65f, 0.8f);
        wall = LitMaterial("Room_Charcoal", new Color(0.023f, 0.03f, 0.04f), 0.15f, 0.38f);
        floor = MaterialAsset("Floor_WetSlate", "PacificExhibit/Floor");
        floor.SetFloat("_ReflectionEnabled", 0f);
        floor.SetFloat("_ReflectionStrength", 0.6f);
        floor.SetFloat("_TileScale", 0.8f);
        floor.SetFloat("_RippleStrength", 0.0015f);
        floor.SetFloat("_ReflectionFlipY", 1f);
        ceiling = MaterialAsset("Room_LightStrips", "PacificExhibit/Glow");
        ceiling.SetColor("_BaseColor", new Color(0.03f, 0.07f, 0.09f));
        surfaceMesh = CreateWaterMesh();
        VolumeProfile profile = CreateVolumeProfile();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        ConfigureEnvironment();
        GameObject root = new GameObject("PacificExhibit");
        CreateRoom(root.transform);
        CreateBox(root.transform);
        var volume = Child("Atmosphere", root.transform).AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 20f;
        volume.sharedProfile = profile;
        PrefabUtility.SaveAsPrefabAsset(root, AssetRoot + "/Prefabs/PacificExhibit.prefab");

        GameObject cameraObject = new GameObject("Exhibit Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.003f, 0.008f, 0.015f);
        camera.fieldOfView = 43f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 80f;
        camera.allowHDR = true;
        var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = true;
        cameraData.allowXRRendering = false;
        cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        cameraObject.AddComponent<AudioListener>();
        var orbit = cameraObject.AddComponent<PacificExhibitOrbit>();
        orbit.distance = 10.3f;
        orbit.pitch = 12f;
        orbit.yaw = 15f;
        orbit.target = new Vector3(0f, 2.05f, 0f);
        orbit.ApplyPose();
        var reflection = cameraObject.AddComponent<PacificPlanarReflection>();
        reflection.sourceCamera = camera;
        reflection.floorRenderer = root.transform.Find("Room/Floor").GetComponent<Renderer>();
        reflection.reflectionStrength = 0.62f;
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        PacificExhibitIntegration.CreateCyberCanoeScene();
        EditorSceneManager.OpenScene(ScenePath);
        Selection.activeGameObject = GameObject.Find("PacificExhibit");
        Debug.Log("PACIFIC_EXHIBIT: Generated standalone scene, Cyber Canoe scene, and editable room/box prefab.");
    }

    static void CreateRoom(Transform parent)
    {
        Transform room = Child("Room", parent).transform;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Floor";
        ground.transform.SetParent(room, false);
        ground.transform.localScale = new Vector3(2.4f, 1f, 3.2f);
        ground.GetComponent<Renderer>().sharedMaterial = floor;
        Cube("Back Wall", room, new Vector3(0f, 4.5f, 9f), new Vector3(24f, 9f, 0.15f), wall, true);
        Cube("Left Wall", room, new Vector3(-12f, 4.5f, -3.5f), new Vector3(0.15f, 9f, 25f), wall, true);
        Cube("Right Wall", room, new Vector3(12f, 4.5f, -3.5f), new Vector3(0.15f, 9f, 25f), wall, true);
        Cube("Ceiling", room, new Vector3(0f, 9f, -3.5f), new Vector3(24f, 0.15f, 25f), wall, true);
        for (int i = -4; i <= 4; i++)
            Cube("Back Wall Seam " + (i + 5), room, new Vector3(i * 2.4f, 4.5f, 8.91f),
                new Vector3(0.028f, 9f, 0.022f), metal);
        Cube("Back Skirting", room, new Vector3(0f, 0.12f, 8.88f), new Vector3(24f, 0.24f, 0.12f), metal);
        for (int i = -1; i <= 1; i++)
            Cube("Ceiling Strip " + (i + 2), room, new Vector3(i * 5f, 8.9f, -2f),
                new Vector3(0.035f, 0.016f, 15f), ceiling);
        LightObject("Cool Key", room, LightType.Spot, new Vector3(-3.5f, 7.8f, -4f),
            Quaternion.Euler(53f, 22f, 0f), new Color(0.42f, 0.69f, 1f), 16f, 20f);
        LightObject("Blue Rim", room, LightType.Point, new Vector3(3f, 5.5f, 3f),
            Quaternion.identity, new Color(0.08f, 0.48f, 1f), 5f, 12f);
        LightObject("Soft Room Fill", room, LightType.Point, new Vector3(0f, 6f, -5f),
            Quaternion.identity, new Color(0.25f, 0.42f, 0.65f), 2f, 18f);
    }

    static void CreateBox(Transform parent)
    {
        FrameBars.Clear();
        GlassPanels.Clear();
        Transform box = Child("Four Layer Box", parent).transform;
        box.localRotation = Quaternion.Euler(0f, -18f, 0f);
        Transform frameRoot = Child("Glowing Frame", box).transform;
        Transform panelRoot = Child("Glass Panels", box).transform;
        Transform waterRoot = Child("Water Separators", box).transform;
        Transform bays = Child("Empty Layers - Add Content Here", box).transform;
        Cube("Plinth", box, new Vector3(0f, Base * 0.5f, 0f), new Vector3(Width + 0.14f, Base, Depth + 0.14f), metal, true);
        Cube("Base Glass", panelRoot, new Vector3(0f, Base + 0.005f, 0f), new Vector3(Width, 0.012f, Depth), glass);
        GlassPanels.Add(Cube("Front Glass", panelRoot, new Vector3(0f, Base + Height * 0.5f, -Depth * 0.5f),
            new Vector3(Width - 0.02f, Height, 0.006f), glass));
        GlassPanels.Add(Cube("Back Glass", panelRoot, new Vector3(0f, Base + Height * 0.5f, Depth * 0.5f),
            new Vector3(Width - 0.02f, Height, 0.006f), glass));
        GlassPanels.Add(Cube("Left Glass", panelRoot, new Vector3(-Width * 0.5f, Base + Height * 0.5f, 0f),
            new Vector3(0.006f, Height, Depth - 0.02f), glass));
        GlassPanels.Add(Cube("Right Glass", panelRoot, new Vector3(Width * 0.5f, Base + Height * 0.5f, 0f),
            new Vector3(0.006f, Height, Depth - 0.02f), glass));
        const float rail = 0.015f;
        for (int x = -1; x <= 1; x += 2)
            for (int z = -1; z <= 1; z += 2)
                FrameBars.Add(Cube("Corner " + x + " " + z, frameRoot,
                    new Vector3(x * Width * 0.5f, Base + Height * 0.5f, z * Depth * 0.5f),
                    new Vector3(rail, Height + rail, rail), frame));
        var waterRenderers = new List<Renderer>();
        for (int tier = 0; tier <= 4; tier++)
        {
            float y = Base + Height * tier * 0.25f;
            Transform ring = Child(tier == 0 ? "Bottom Rim" : tier == 4 ? "Top Rim" : "Divider Rim " + tier, frameRoot).transform;
            for (int side = -1; side <= 1; side += 2)
            {
                FrameBars.Add(Cube("Width Rail " + side, ring, new Vector3(0f, y, side * Depth * 0.5f),
                    new Vector3(Width + rail, rail, rail), frame));
                FrameBars.Add(Cube("Depth Rail " + side, ring, new Vector3(side * Width * 0.5f, y, 0f),
                    new Vector3(rail, rail, Depth + rail), frame));
            }
            if (tier == 0) continue;
            Material water = MaterialAsset("Water_Layer_" + tier, "PacificExhibit/RippleWater");
            water.SetFloat("_Phase", tier * 1.73f);
            water.SetFloat("_EmissionStrength", 2.6f);
            water.SetFloat("_Opacity", 0.22f);
            water.SetFloat("_CellScale", 3.8f);
            var sheet = Child(tier == 4 ? "Top Water Surface" : "Water Divider " + tier, waterRoot);
            sheet.transform.localPosition = new Vector3(0f, y, 0f);
            sheet.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
            var renderer = sheet.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = water;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            waterRenderers.Add(renderer);
            Transform bay = Child("Layer " + tier.ToString("00") + (tier == 1 ? " - Bottom" : tier == 4 ? " - Top" : ""), bays).transform;
            bay.localPosition = new Vector3(0f, y - Height * 0.125f, 0f);
        }
        var settings = box.gameObject.AddComponent<PacificExhibitSettings>();
        settings.frameBars = FrameBars.ToArray();
        settings.glassPanels = GlassPanels.ToArray();
        settings.waterSurfaces = waterRenderers.ToArray();
        settings.Apply();
    }

    static Mesh CreateWaterMesh()
    {
        const int nx = 72, nz = 48;
        var vertices = new Vector3[(nx + 1) * (nz + 1)];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[nx * nz * 6];
        int t = 0;
        for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
            {
                int index = z * (nx + 1) + x;
                uv[index] = new Vector2((float)x / nx, (float)z / nz);
                vertices[index] = new Vector3((uv[index].x - 0.5f) * Width, 0f, (uv[index].y - 0.5f) * Depth);
                normals[index] = Vector3.up;
                if (x == nx || z == nz) continue;
                triangles[t++] = index;
                triangles[t++] = index + nx + 1;
                triangles[t++] = index + 1;
                triangles[t++] = index + 1;
                triangles[t++] = index + nx + 1;
                triangles[t++] = index + nx + 2;
            }
        var mesh = new Mesh { name = "Subdivided Water Sheet", vertices = vertices, normals = normals, uv = uv, triangles = triangles };
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(Width, 0.4f, Depth));
        string path = AssetRoot + "/Meshes/WaterSheet.asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static VolumeProfile CreateVolumeProfile()
    {
        string path = AssetRoot + "/Materials/ExhibitAtmosphere.asset";
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet<Bloom>(out var bloom))
        {
            bloom = profile.Add<Bloom>(true);
            AssetDatabase.AddObjectToAsset(bloom, profile);
        }
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.62f);
        bloom.scatter.Override(0.64f);
        bloom.highQualityFiltering.Override(true);
        if (!profile.TryGet<Tonemapping>(out var tone))
        {
            tone = profile.Add<Tonemapping>(true);
            AssetDatabase.AddObjectToAsset(tone, profile);
        }
        tone.mode.Override(TonemappingMode.ACES);
        if (!profile.TryGet<Vignette>(out var vignette))
        {
            vignette = profile.Add<Vignette>(true);
            AssetDatabase.AddObjectToAsset(vignette, profile);
        }
        vignette.intensity.Override(0.18f);
        vignette.smoothness.Override(0.5f);
        EditorUtility.SetDirty(profile);
        return profile;
    }

    static void ConfigureEnvironment()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.022f, 0.034f, 0.052f);
        RenderSettings.fog = false;
    }

    static GameObject Child(string name, Transform parent)
    {
        var result = new GameObject(name);
        result.transform.SetParent(parent, false);
        return result;
    }

    static Renderer Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collision = false)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = position;
        cube.transform.localScale = scale;
        if (!collision) Object.DestroyImmediate(cube.GetComponent<Collider>());
        var renderer = cube.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        if (material.shader.name.StartsWith("PacificExhibit/", StringComparison.Ordinal))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        return renderer;
    }

    static void LightObject(string name, Transform parent, LightType type, Vector3 position, Quaternion rotation,
        Color color, float intensity, float range)
    {
        GameObject node = Child(name, parent);
        node.transform.SetLocalPositionAndRotation(position, rotation);
        var light = node.AddComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.spotAngle = 70f;
        light.innerSpotAngle = 45f;
        light.shadows = LightShadows.None;
    }

    static Material MaterialAsset(string name, string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (!shader) throw new InvalidOperationException("Missing shader: " + shaderName);
        string path = AssetRoot + "/Materials/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        EditorUtility.SetDirty(material);
        return material;
    }

    static Material LitMaterial(string name, Color color, float metallic, float smoothness)
    {
        Material material = MaterialAsset(name, "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        return material;
    }

    [MenuItem("Tools/Pacific Exhibit/Capture Preview")]
    public static void CapturePreview()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) StartPreview();
    }

    static void StartPreview()
    {
        previewFrames = 0;
        previewDeadline = EditorApplication.timeSinceStartup + 90;
        EditorApplication.update += PreviewUpdate;
        EditorApplication.isPlaying = true;
    }

    static void PreviewUpdate()
    {
        if (EditorApplication.timeSinceStartup > previewDeadline)
        {
            Debug.LogError("PACIFIC_EXHIBIT: Timed out entering preview Play mode.");
            FinishPreview(1);
            return;
        }
        if (!EditorApplication.isPlaying || ++previewFrames < 45) return;
        EditorApplication.update -= PreviewUpdate;
        try
        {
            ValidateScene();
            RenderPreviewImage();
            Debug.Log("PACIFIC_EXHIBIT: Validation passed; preview saved to Artifacts/PacificExhibit-preview.png.");
            FinishPreview(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            FinishPreview(1);
        }
    }

    static void ValidateScene()
    {
        var settings = Object.FindAnyObjectByType<PacificExhibitSettings>();
        if (!settings || settings.waterSurfaces.Length != 4 || settings.glassPanels.Length != 4)
            throw new InvalidOperationException("The exhibit must contain four water surfaces and four glass walls.");
        for (int i = 0; i < 4; i++)
        {
            Renderer surface = settings.waterSurfaces[i];
            if (!surface || !surface.GetComponent<MeshFilter>().sharedMesh || !surface.sharedMaterial.shader.isSupported)
                throw new InvalidOperationException("Missing or unsupported water surface " + i);
        }
        foreach (string shaderGuid in AssetDatabase.FindAssets("t:Shader", new[] { AssetRoot + "/Shaders" }))
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(shaderGuid));
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new InvalidOperationException(shader.name + ": " + message.message);
        }
    }

    static void RenderPreviewImage()
    {
        Camera camera = Camera.main;
        if (!camera) throw new InvalidOperationException("Standalone exhibit camera is missing.");
        // Keep an HDR destination so URP retains emission above the bloom threshold.
        var target = new RenderTexture(1600, 1200, 24, RenderTextureFormat.DefaultHDR);
        target.Create();
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        Texture2D image = null;
        Texture2D encodedImage = null;
        try
        {
            camera.targetTexture = target;
            camera.aspect = 1600f / 1200f;
            camera.GetComponent<PacificPlanarReflection>().RenderReflection();
            // StandardRequest updates the volume stack, so the capture includes bloom and tonemapping.
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("URP single-camera render requests are unavailable.");
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = target;
            // Preserve dark HDR gradients until after the PNG's sRGB conversion.
            image = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            Color[] pixels = image.GetPixels();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
            encodedImage = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            encodedImage.SetPixels(pixels);
            encodedImage.Apply();
            Directory.CreateDirectory("Artifacts");
            File.WriteAllBytes("Artifacts/PacificExhibit-preview.png", encodedImage.EncodeToPNG());
            ValidateScene();
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.ResetAspect();
            RenderTexture.active = previousActive;
            target.Release();
            Object.DestroyImmediate(target);
            if (image) Object.DestroyImmediate(image);
            if (encodedImage) Object.DestroyImmediate(encodedImage);
        }
    }

    static void FinishPreview(int exitCode)
    {
        EditorApplication.update -= PreviewUpdate;
        EditorApplication.isPlaying = false;
        if (batchPreview) EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
    }
}
