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

/// <summary>Fits the Pacific exhibit presentation to the existing plastic data sculpture.</summary>
public static class PlasticTypePresentation
{
    const string ScenePath = "Assets/Scenes/Plastic_Type.unity";
    const string AssetRoot = "Assets/PacificExhibit";
    const string PresentationName = "Pacific Exhibit Presentation";

    [MenuItem("Tools/Pacific Exhibit/Style Plastic Type in Current Scene")]
    public static void ApplyToCurrentScene()
    {
        var exhibit = FindExhibit(SceneManager.GetActiveScene());
        if (!exhibit) throw new InvalidOperationException("The current scene has no plastic-type water cube.");
        Apply(exhibit);
        Selection.activeGameObject = exhibit.transform.Find(PresentationName).gameObject;
    }

    [MenuItem("Tools/Pacific Exhibit/Style Plastic Type in Current Scene", true)]
    static bool CanApply() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    [MenuItem("Tools/Pacific Exhibit/Light Plastic Type Interior")]
    public static void LightInterior()
    {
        var exhibit = FindExhibit(SceneManager.GetActiveScene());
        if (!exhibit) throw new InvalidOperationException("The current scene has no plastic-type water cube.");
        LightInterior(exhibit);
    }

    public static void LightInterior(GPGPLayeredCubeVisualization exhibit)
    {
        if (!exhibit) throw new ArgumentNullException(nameof(exhibit));
        Transform presentation = exhibit.transform.Find(PresentationName);
        if (!presentation) throw new InvalidOperationException("Apply the Pacific exhibit presentation first.");
        Transform lights = Child(presentation, "Gallery Lighting");
        Undo.RegisterFullObjectHierarchyUndo(lights.gameObject, "Light plastic-type interior");
        float floorTop = -exhibit.cubeSize.y * .5f - .545f;
        ConfigureLighting(presentation, exhibit, floorTop);
        EditorSceneManager.MarkSceneDirty(exhibit.gameObject.scene);
        Selection.activeGameObject = lights.gameObject;
        Debug.Log("PLASTIC_LIGHTING: Front, lower-front, and overhead spotlights aimed through the water at the debris.");
    }

    [MenuItem("Tools/Pacific Exhibit/Light Plastic Type Interior", true)]
    static bool CanLightInterior() => CanApply();

    // Apply the content update without replacing the user's authored lighting.
    public static void BalanceDebrisLighting(GPGPLayeredCubeVisualization exhibit)
    {
        if (!exhibit) throw new ArgumentNullException(nameof(exhibit));
        Transform presentation = exhibit.transform.Find(PresentationName);
        Transform lights = presentation ? presentation.Find("Gallery Lighting") : null;
        if (!lights) return;
        float scale = Mathf.Abs(exhibit.transform.lossyScale.x);
        string[] names = { "Cool Key", "Soft Front Fill", "Top Interior Spotlight" };
        float[] maximums = { 96f, 64f, 48f };
        bool changed = false;
        for (int i = 0; i < names.Length; i++)
        {
            Transform lightTransform = lights.Find(names[i]);
            Light light = lightTransform ? lightTransform.GetComponent<Light>() : null;
            float maximum = maximums[i] * scale * scale;
            if (!light || light.intensity <= maximum) continue;
            Undo.RecordObject(light, "Balance debris lighting");
            light.intensity = maximum;
            EditorUtility.SetDirty(light);
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            changed = true;
        }
        if (changed) EditorSceneManager.MarkSceneDirty(exhibit.gameObject.scene);
    }

    public static void RelightAndCapture()
    {
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            LightInterior();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Plastic_Type lighting.");
            AssetDatabase.SaveAssets();
            PlasticTypePresentationPreview.Capture();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    // Run in an isolated copy of the project while the working editor stays open.
    public static void BuildAndCapture()
    {
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            ApplyToCurrentScene();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Plastic_Type.");
            AssetDatabase.SaveAssets();
            PlasticTypePresentationPreview.Capture();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    public static void Apply(GPGPLayeredCubeVisualization exhibit)
    {
        Transform generated = exhibit.transform.Find("WaterCube - one continuous volume");
        if (!generated) throw new InvalidOperationException("Bake the water cube before applying its presentation.");
        Transform floorTransform = generated.Find("Dark gallery floor");
        if (!floorTransform || !floorTransform.TryGetComponent<Renderer>(out var floorRenderer))
            throw new InvalidOperationException("The authored gallery floor is missing.");

        Material glow = Required<Material>(AssetRoot + "/Materials/Frame_Cyan.mat");
        Material graphite = Required<Material>(AssetRoot + "/Materials/Plinth_Graphite.mat");
        VolumeProfile atmosphere = Required<VolumeProfile>(AssetRoot + "/Materials/ExhibitAtmosphere.asset");
        Material floor = FloorMaterial();

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Style plastic-type exhibit");
        Undo.RegisterFullObjectHierarchyUndo(exhibit.gameObject, "Style plastic-type exhibit");
        try
        {
            Transform presentation = Child(exhibit.transform, PresentationName);
            Vector3 size = exhibit.cubeSize;
            float bottom = -size.y * .5f;
            const float plinthHeight = .52f;
            float floorTop = bottom - plinthHeight - .025f;

            Transform oldPlinth = generated.Find("Gallery lighting and backdrop/Low exhibit plinth");
            if (!oldPlinth) oldPlinth = generated.Find("Low exhibit plinth");
            if (oldPlinth && !presentation.Find("Plinth"))
            {
                Undo.SetTransformParent(oldPlinth, presentation, "Fit exhibit plinth");
                oldPlinth.name = "Plinth";
            }
            Bar(presentation, "Plinth", new Vector3(0f, bottom - plinthHeight * .5f - .005f, 0f),
                new Vector3(size.x + .42f, plinthHeight, size.z + .42f), graphite);

            // The original floor almost covered the original plinth. Ground it below the base.
            float oldFloorTop = floorRenderer.bounds.max.y;
            Vector3 floorPosition = floorTransform.localPosition;
            floorPosition.y = floorTop - floorTransform.localScale.y * .5f;
            floorTransform.localPosition = floorPosition;
            floorRenderer.sharedMaterial = floor;
            floorRenderer.SetPropertyBlock(null);
            floorRenderer.shadowCastingMode = ShadowCastingMode.Off;
            floorRenderer.receiveShadows = false;
            Transform human = generated.Find("ScaleHuman - display scale only");
            if (human) human.position += Vector3.up * (floorRenderer.bounds.max.y - oldFloorTop);

            Transform frame = Child(presentation, "Glowing Frame");
            const float rail = .032f;
            float halfX = size.x * .501f, halfZ = size.z * .501f;
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Bar(frame, "Corner " + x + " " + z, new Vector3(x * halfX, 0f, z * halfZ),
                        new Vector3(rail, size.y + rail, rail), glow);
            Ring(Child(frame, "Bottom Rim"), bottom, halfX, halfZ, rail, glow);
            Ring(Child(frame, "Top Rim"), -bottom, halfX, halfZ, rail, glow);

            // Use the authored mass-share heights; rebuilding the data is unnecessary.
            Transform boundaries = generated.Find("Mass-share boundary lines");
            if (boundaries)
                foreach (Renderer boundary in boundaries.GetComponentsInChildren<Renderer>(true))
                {
                    boundary.sharedMaterial = glow;
                    boundary.SetPropertyBlock(null);
                    boundary.shadowCastingMode = ShadowCastingMode.Off;
                    boundary.receiveShadows = false;
                    Vector3 scale = boundary.transform.localScale;
                    scale.y = rail;
                    if (scale.x < scale.z) scale.x = rail;
                    else scale.z = rail;
                    boundary.transform.localScale = scale;
                }
            exhibit.separatorMaterial = glow;

            Volume volume = Component<Volume>(Child(presentation, "Neon Bloom").gameObject);
            volume.isGlobal = true;
            volume.priority = 50f;
            volume.sharedProfile = atmosphere;

            ConfigureLighting(presentation, exhibit, floorTop);
            Transform oldLighting = generated.Find("Gallery lighting and backdrop");
            if (oldLighting)
                foreach (Light light in oldLighting.GetComponentsInChildren<Light>(true)) light.enabled = false;
            var reflection = Component<PacificPlanarReflection>(presentation.gameObject);
            reflection.floorRenderer = floorRenderer;
            reflection.surfaceOffset = floorRenderer.bounds.max.y - floorTransform.position.y;
            reflection.textureWidth = 1024;
            reflection.reflectionStrength = .78f;
            ConfigureCameras(exhibit.gameObject.scene, reflection, presentation);

            var settings = Unsupported.GetSerializedAssetInterfaceSingleton("RenderSettings");
            if (settings) Undo.RecordObject(settings, "Set dark exhibit backdrop");
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.065f, .085f, .11f);
            RenderSettings.fog = false;
            foreach (GameObject root in exhibit.gameObject.scene.GetRootGameObjects())
                if (root.name == "Directional Light" && root.TryGetComponent<Light>(out var sun))
                {
                    Undo.RecordObject(sun, "Use gallery lighting");
                    sun.enabled = false;
                }

            EditorUtility.SetDirty(exhibit);
            EditorSceneManager.MarkSceneDirty(exhibit.gameObject.scene);
            Undo.CollapseUndoOperations(group);
            Debug.Log("PLASTIC_PRESENTATION: Graphite plinth, cyan frame and band rails, bloom, and per-camera reflective floor applied.");
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    static void ConfigureLighting(Transform presentation, GPGPLayeredCubeVisualization exhibit, float floorTop)
    {
        Transform lights = Child(presentation, "Gallery Lighting");
        float scale = Mathf.Abs(exhibit.transform.lossyScale.x);
        Vector3 half = exhibit.cubeSize * .5f;
        // Broad neutral spots reveal plastic through the water without pushing
        // its diffuse colors into the HDR bloom used by the cyan frame.
        AddLight(lights, "Cool Key", new Vector3(-half.x - .8f, half.y * .72f, -half.z - 3.2f), LightType.Spot,
            new Color(.94f, .97f, 1f), 96f * scale * scale, 30f * scale, new Vector3(0f, 1f, 0f));
        AddLight(lights, "Blue Rim", new Vector3(6f, 5f, 6f), LightType.Point,
            new Color(.08f, .48f, 1f), 7f * scale * scale, 20f * scale, Vector3.zero);
        AddLight(lights, "Soft Front Fill", new Vector3(half.x + .8f, -half.y * .18f, -half.z - 3.2f), LightType.Spot,
            new Color(1f, .96f, .9f), 64f * scale * scale, 26f * scale, new Vector3(0f, -half.y * .6f, 0f));
        AddLight(lights, "Top Interior Spotlight", new Vector3(0f, half.y + 2.5f, 0f), LightType.Spot,
            new Color(.9f, .96f, 1f), 48f * scale * scale, 24f * scale, new Vector3(0f, -half.y * .55f, 0f));
        AddLight(lights, "Plinth Cyan Wash", new Vector3(0f, floorTop + .4f, -exhibit.cubeSize.z * .5f - .8f),
            LightType.Point, new Color(.04f, .65f, 1f), 1.5f * scale * scale, 8f * scale, Vector3.zero);
    }

    static void ConfigureCameras(Scene scene, PacificPlanarReflection reflection, Transform presentation)
    {
        var eyes = new List<Camera>();
        RenderTexture left = Required<RenderTexture>(AssetRoot + "/RenderTextures/Exhibit_Left_HDR.renderTexture");
        RenderTexture right = Required<RenderTexture>(AssetRoot + "/RenderTextures/Exhibit_Right_HDR.renderTexture");
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            if (camera.gameObject.scene != scene || (camera.name != "CenterCamera" &&
                camera.name != "CC_LEFT_CAMERA" && camera.name != "CC_RIGHT_CAMERA")) continue;
            Undo.RecordObject(camera, "Enable plastic exhibit HDR");
            camera.allowHDR = true;
            var data = Component<UniversalAdditionalCameraData>(camera.gameObject);
            Undo.RecordObject(data, "Enable plastic exhibit bloom");
            data.renderPostProcessing = true;
            data.allowXRRendering = false;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            if (camera.name == "CenterCamera") reflection.sourceCamera = camera;
            else
            {
                camera.targetTexture = camera.name == "CC_LEFT_CAMERA" ? left : right;
                eyes.Add(camera);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            PrefabUtility.RecordPrefabInstancePropertyModifications(data);
        }
        reflection.additionalSourceCameras = eyes.ToArray();
        foreach (var setup in Object.FindObjectsByType<CC_SETUP_RENDER_TEXTURES>(FindObjectsInactive.Include))
        {
            if (setup.gameObject.scene != scene) continue;
            Undo.RecordObject(setup, "Bind HDR stereo textures");
            setup.cameraLeftRT = left;
            setup.cameraRightRT = right;
            if (setup.leftCameraImage)
            {
                Undo.RecordObject(setup.leftCameraImage, "Bind left HDR output");
                setup.leftCameraImage.texture = left;
                PrefabUtility.RecordPrefabInstancePropertyModifications(setup.leftCameraImage);
            }
            if (setup.rightCameraImage)
            {
                Undo.RecordObject(setup.rightCameraImage, "Bind right HDR output");
                setup.rightCameraImage.texture = right;
                PrefabUtility.RecordPrefabInstancePropertyModifications(setup.rightCameraImage);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(setup);
        }
        foreach (var layout in Object.FindObjectsByType<CC_CANVAS_SETUP>(FindObjectsInactive.Include))
        {
            if (layout.gameObject.scene != scene) continue;
            Undo.RecordObject(layout, "Bind HDR stereo canvas");
            layout.leftRT = left;
            layout.rightRT = right;
            PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            var appearance = Component<PacificCyberCanoeAppearance>(presentation.gameObject);
            Undo.RecordObject(appearance, "Show exhibit while editing");
            appearance.stereoOutput = layout.GetComponent<Canvas>();
            if (appearance.stereoOutput) Undo.RecordObject(appearance.stereoOutput, "Hide stereo placeholders");
            appearance.RefreshPreview();
            if (appearance.stereoOutput) PrefabUtility.RecordPrefabInstancePropertyModifications(appearance.stereoOutput);
        }
        foreach (var simulator in Object.FindObjectsByType<CyberCanoeHeadsetSimulator>(FindObjectsInactive.Include))
        {
            if (simulator.gameObject.scene != scene) continue;
            Undo.RecordObject(simulator, "Keep headset content HDR");
            simulator.useHDRContentTextures = true;
            PrefabUtility.RecordPrefabInstancePropertyModifications(simulator);
        }
    }

    static Material FloorMaterial()
    {
        string path = AssetRoot + "/Materials/PlasticType_ReflectiveFloor.mat";
        Material floor = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!floor)
        {
            floor = new Material(Required<Material>(AssetRoot + "/Materials/Floor_WetSlate.mat"))
                { name = "PlasticType_ReflectiveFloor" };
            AssetDatabase.CreateAsset(floor, path);
        }
        floor.SetTexture("_BaseMap", Required<Texture2D>(AssetRoot + "/Textures/Museum_Dark_Tile_BaseColor.png"));
        floor.SetFloat("_UseBaseMap", 1f);
        floor.SetColor("_BaseColor", new Color(.7f, .74f, .78f));
        floor.SetFloat("_ReflectionStrength", .78f);
        floor.SetFloat("_TileScale", 1f);
        floor.SetFloat("_RippleStrength", 0f);
        EditorUtility.SetDirty(floor);
        return floor;
    }

    static void Ring(Transform root, float y, float halfX, float halfZ, float rail, Material material)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            Bar(root, "Width Rail " + side, new Vector3(0f, y, side * halfZ),
                new Vector3(halfX * 2f + rail, rail, rail), material);
            Bar(root, "Depth Rail " + side, new Vector3(side * halfX, y, 0f),
                new Vector3(rail, rail, halfZ * 2f + rail), material);
        }
    }

    static Renderer Bar(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        Transform existing = parent.Find(name);
        GameObject node;
        if (existing) node = existing.gameObject;
        else
        {
            node = GameObject.CreatePrimitive(PrimitiveType.Cube);
            node.name = name;
            node.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(node, "Create exhibit " + name);
        }
        if (node.TryGetComponent<Collider>(out var collider)) Undo.DestroyObjectImmediate(collider);
        node.transform.localPosition = position;
        node.transform.localRotation = Quaternion.identity;
        node.transform.localScale = size;
        Renderer renderer = node.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.SetPropertyBlock(null);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    static void AddLight(Transform parent, string name, Vector3 position, LightType type,
        Color color, float intensity, float range, Vector3 target)
    {
        Transform node = Child(parent, name);
        node.localPosition = position;
        if (type == LightType.Spot) node.localRotation = Quaternion.LookRotation(target - position);
        var light = Component<Light>(node.gameObject);
        light.type = type;
        light.enabled = true;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.spotAngle = 85f;
        light.innerSpotAngle = 55f;
        light.shadows = LightShadows.None;
    }

    static Transform Child(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child) return child;
        var node = new GameObject(name);
        node.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(node, "Create exhibit " + name);
        return node.transform;
    }

    static T Component<T>(GameObject node) where T : Component =>
        node.TryGetComponent<T>(out var component) ? component : Undo.AddComponent<T>(node);

    static T Required<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path)
        ?? throw new InvalidOperationException("Required exhibit asset is missing: " + path);

    static GPGPLayeredCubeVisualization FindExhibit(Scene scene)
    {
        foreach (var exhibit in Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include))
            if (exhibit.gameObject.scene == scene) return exhibit;
        return null;
    }
}
