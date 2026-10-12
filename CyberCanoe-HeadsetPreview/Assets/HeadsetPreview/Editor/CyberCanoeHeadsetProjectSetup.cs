using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Object = UnityEngine.Object;

/// <summary>Enables PC headset preview in the student's current Cyber Canoe scene.</summary>
public static class CyberCanoeHeadsetProjectSetup
{
    const string MenuPath = "Tools/Cyber Canoe/Set Up Headset Preview for Current Scene";
    const string SettingsFolder = "Assets/HeadsetPreview/ProjectSettings";

    [MenuItem(MenuPath)]
    public static void SetupCurrentScene()
    {
        if (!CanSetupCurrentScene())
            throw new InvalidOperationException("Stop Play mode, close Prefab mode, and open a scene before setting up headset preview.");

        Scene scene = SceneManager.GetActiveScene();
        var pipeline = QualitySettings.renderPipeline ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
        if (!(pipeline is UniversalRenderPipelineAsset))
            throw new InvalidOperationException("Headset preview needs a Universal Render Pipeline project. Assign your project's URP asset in Graphics or the current Quality level before running setup.");
        RequireCyberCanoeRig(scene);

        // The scene builder preserves an existing simulator and its calibration.
        CyberCanoeHeadsetSetup.AddSimulator();
        var simulators = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<CyberCanoeHeadsetSimulator>(true)).ToArray();
        if (simulators.Length != 1)
            throw new InvalidOperationException("The current scene must contain exactly one Cyber Canoe headset simulator. Resolve duplicate simulators before running setup again.");
        CyberCanoeHeadsetSimulator simulator = simulators[0];
        ValidateSimulator(simulator, scene);

        XRGeneralSettingsPerBuildTarget settings = FindOrCreateXRSettings();
        if (!settings.HasSettingsForBuildTarget(BuildTargetGroup.Standalone))
            settings.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (!settings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
            settings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        XRGeneralSettings general = settings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        XRManagerSettings manager = general ? general.Manager : null;
        if (!general || !manager)
            throw new InvalidOperationException("Unity could not create desktop XR settings. Check the Console and XR Plug-in Management before retrying.");

        var previousLoaders = manager.activeLoaders.ToList();
        if (previousLoaders.Any(loader => !loader))
            throw new InvalidOperationException("The desktop XR loader list contains a missing asset. Repair it in XR Plug-in Management before running headset setup.");
        if (!previousLoaders.Any(loader => loader is OpenXRLoader))
        {
            try
            {
                if (!XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Standalone))
                    throw new InvalidOperationException("Unity could not assign OpenXR. Confirm the OpenXR package is installed and check XR Plug-in Management validation.");
            }
            catch
            {
                manager.TrySetLoaders(previousLoaders);
                EditorUtility.SetDirty(manager);
                AssetDatabase.SaveAssets();
                throw;
            }
        }
        OpenXRLoader openXRLoader = manager.activeLoaders.OfType<OpenXRLoader>().FirstOrDefault();
        if (!openXRLoader)
            throw new InvalidOperationException("OpenXR assignment did not produce a desktop loader. Check XR Plug-in Management before retrying.");

        // AssignLoader can reorder or filter the list using package metadata.
        // Retain the student's existing providers and order, appending OpenXR.
        var desiredLoaders = new List<XRLoader>(previousLoaders);
        if (!desiredLoaders.Contains(openXRLoader)) desiredLoaders.Add(openXRLoader);
        if (!manager.TrySetLoaders(desiredLoaders) || !manager.activeLoaders.SequenceEqual(desiredLoaders))
            throw new InvalidOperationException("Unity could not preserve the existing desktop XR providers while adding OpenXR. Check XR Plug-in Management before retrying.");

        EnsureOpenXRSettings();
        XRGeneralSettings.Instance = general;
        general.InitManagerOnStart = false;
        manager.automaticLoading = false;
        manager.automaticRunning = false;
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(settings);

        Undo.RecordObjects(new Object[] { simulator, simulator.gameObject }, "Enable Cyber Canoe headset preview");
        simulator.gameObject.SetActive(true);
        simulator.enabled = true;
        simulator.initializeXR = true;
        EditorUtility.SetDirty(simulator);
        if (PrefabUtility.IsPartOfPrefabInstance(simulator))
            PrefabUtility.RecordPrefabInstancePropertyModifications(simulator);
        if (PrefabUtility.IsPartOfPrefabInstance(simulator.gameObject))
            PrefabUtility.RecordPrefabInstancePropertyModifications(simulator.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget registered) ||
            registered != settings || registered.SettingsForBuildTarget(BuildTargetGroup.Standalone) != general ||
            general.InitManagerOnStart || manager.automaticLoading || manager.automaticRunning ||
            !manager.activeLoaders.OfType<OpenXRLoader>().Any() ||
            !OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone))
            throw new InvalidOperationException("Desktop OpenXR configuration did not validate. Check the Console and run headset setup again.");

        Selection.activeGameObject = simulator.gameObject;
        EditorGUIUtility.PingObject(simulator);
        if (!UsesNewInputSystem())
            Debug.LogWarning("Before pressing Play, choose Input System Package (New) or Both under Project Settings > Player > Other Settings > Active Input Handling, then restart Unity when prompted.", simulator);
        Debug.Log("CYBER_CANOE_HEADSET_SETUP: Headset preview is ready in the current scene. Save the scene, connect your headset through Meta Link or SteamVR, select its OpenXR runtime, then press Play. The simulator controls XR startup; uncheck its component before presenting on the physical wall.", simulator);
    }

    [MenuItem(MenuPath, true)]
    static bool CanSetupCurrentScene() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer &&
        PrefabStageUtility.GetCurrentPrefabStage() == null &&
        SceneManager.GetActiveScene().IsValid() && SceneManager.GetActiveScene().isLoaded;

    static void RequireCyberCanoeRig(Scene scene)
    {
        foreach (CC_CAMERA_SETUP setup in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CC_CAMERA_SETUP>(true)))
        {
            if (!setup.CCSettings || !setup.leftCamera || !setup.rightCamera || setup.leftCamera == setup.rightCamera) continue;
            var left = setup.leftCamera.GetComponent<Camera>();
            var right = setup.rightCamera.GetComponent<Camera>();
            var leftOffset = setup.leftCamera.GetComponent<CC_CAMERAOFFSET>();
            var rightOffset = setup.rightCamera.GetComponent<CC_CAMERAOFFSET>();
            if (!left || !right || !leftOffset || !rightOffset || !leftOffset.projectionScreen ||
                leftOffset.projectionScreen != rightOffset.projectionScreen) continue;
            Transform hub = setup.transform.parent;
            while (hub && hub.name != "CC_HUB") hub = hub.parent;
            if (hub) return;
        }
        throw new InvalidOperationException("Open your Cyber Canoe scene containing CC_HUB, CC_HEAD with CC_CAMERA_SETUP, and two cameras using the same CC_CAMERAOFFSET projection screen before running headset setup.");
    }

    static XRGeneralSettingsPerBuildTarget FindOrCreateXRSettings()
    {
        if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget settings) && settings)
            return settings;
        var existing = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
            .Select(guid => AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(asset => asset).ToArray();
        if (existing.Length > 1)
            throw new InvalidOperationException("The project contains multiple unregistered XR settings assets. Select the intended configuration in XR Plug-in Management before running headset setup.");
        if (existing.Length == 1) settings = existing[0];
        else
        {
            EnsureSettingsFolder();
            settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            settings.name = "Cyber Canoe XR Settings";
            AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(SettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset"));
        }
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, settings, true);
        return settings;
    }

    static void EnsureOpenXRSettings()
    {
        var package = new OpenXRManagementSettings();
        string settingsType = package.metadata.settingsType;
        if (!EditorBuildSettings.TryGetConfigObject(Constants.k_SettingsKey, out ScriptableObject packageSettings) || !packageSettings)
        {
            var existing = AssetDatabase.FindAssets("t:" + settingsType)
                .Select(guid => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as ScriptableObject)
                .Where(asset => asset && asset.GetType().FullName == settingsType).ToArray();
            if (existing.Length > 1)
                throw new InvalidOperationException("The project contains multiple unregistered OpenXR settings assets. Register the intended one in XR Plug-in Management before running headset setup.");
            if (existing.Length == 1)
            {
                packageSettings = existing[0];
                EditorBuildSettings.AddConfigObject(Constants.k_SettingsKey, packageSettings, true);
            }
            else
            {
                EnsureSettingsFolder();
                packageSettings = ScriptableObject.CreateInstance(settingsType);
                if (!packageSettings)
                    throw new InvalidOperationException("Unity could not create OpenXR package settings. Confirm the OpenXR package is installed.");
                AssetDatabase.CreateAsset(packageSettings,
                    AssetDatabase.GenerateUniqueAssetPath(SettingsFolder + "/OpenXR Package Settings.asset"));
                // The OpenXR package moves its own settings to its standard path and registers them.
                if (!package.PopulateNewSettingsInstance(packageSettings))
                    throw new InvalidOperationException("The OpenXR package could not register its settings. Check the Console before retrying.");
            }
        }
        if (packageSettings.GetType().FullName != settingsType ||
            !OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone))
            throw new InvalidOperationException("Registered OpenXR settings do not expose a desktop configuration. Check XR Plug-in Management before retrying.");
        EditorUtility.SetDirty(packageSettings);
    }

    static void EnsureSettingsFolder()
    {
        if (!AssetDatabase.IsValidFolder(SettingsFolder))
            AssetDatabase.CreateFolder("Assets/HeadsetPreview", "ProjectSettings");
    }

    static void ValidateSimulator(CyberCanoeHeadsetSimulator simulator, Scene scene)
    {
        if (!simulator.ccHub || !simulator.ccHead || !simulator.sourceScreen ||
            !simulator.leftContentCamera || !simulator.rightContentCamera || !simulator.roomRoot ||
            !simulator.virtualScreen || !simulator.trackingSpace || !simulator.headsetCamera ||
            !simulator.virtualScreenRenderer || !simulator.screenMaterialTemplate)
            throw new InvalidOperationException("The headset simulator is incomplete. Check its rig references before running setup again.");
        if (simulator.leftContentCamera == simulator.rightContentCamera ||
            simulator.leftContentCamera == simulator.headsetCamera || simulator.rightContentCamera == simulator.headsetCamera ||
            simulator.roomRoot == simulator.transform || simulator.transform.IsChildOf(simulator.roomRoot) ||
            simulator.roomRoot.IsChildOf(simulator.ccHub) ||
            !simulator.virtualScreen.IsChildOf(simulator.roomRoot) || !simulator.trackingSpace.IsChildOf(simulator.roomRoot) ||
            simulator.headsetCamera.transform.parent != simulator.trackingSpace)
            throw new InvalidOperationException("Keep the headset room outside CC_HUB with its own tracked camera and two separate Cyber Canoe content cameras.");
        int roomLayer = simulator.headsetCamera.gameObject.layer;
        if (roomLayer < 8 || simulator.virtualScreenRenderer.gameObject.layer != roomLayer)
            throw new InvalidOperationException("Put the headset camera and virtual screen on the same dedicated user layer before running setup.");
        if (!simulator.screenMaterialTemplate.shader ||
            !simulator.screenMaterialTemplate.HasProperty("_LeftTex") || !simulator.screenMaterialTemplate.HasProperty("_RightTex"))
            throw new InvalidOperationException("Assign the Cyber Canoe stereo-screen material with its left and right texture properties before running setup.");
        if (float.IsNaN(simulator.viewingDistanceMeters) || float.IsInfinity(simulator.viewingDistanceMeters) ||
            simulator.viewingDistanceMeters < 0.05f)
            throw new InvalidOperationException("The headset viewer distance must be at least 0.05 metres.");
        foreach (Transform screen in new[] { simulator.sourceScreen, simulator.virtualScreen })
            if (screen.TransformVector(Vector3.right).magnitude < 0.01f || screen.TransformVector(Vector3.up).magnitude < 0.01f)
                throw new InvalidOperationException("The source and virtual screens must have positive physical width and height.");
        foreach (Transform target in new[] { simulator.ccHub, simulator.ccHead, simulator.trackingSpace,
            simulator.headsetCamera.transform, simulator.virtualScreen.parent })
            if (target && (target.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                throw new InvalidOperationException("Keep CC_HUB and the headset rig parents at scale (1, 1, 1) so the screen measurements represent metres.");
        var source = simulator.ccHead.GetComponent<CC_CAMERA_SETUP>();
        if (!source || !source.CCSettings || source.leftCamera != simulator.leftContentCamera.gameObject ||
            source.rightCamera != simulator.rightContentCamera.gameObject)
            throw new InvalidOperationException("The simulator's content cameras must match CC_HEAD's CC_CAMERA_SETUP references.");
        foreach (Camera camera in new[] { simulator.leftContentCamera, simulator.rightContentCamera })
        {
            var offset = camera.GetComponent<CC_CAMERAOFFSET>();
            if (!offset || !offset.projectionScreen || offset.projectionScreen.transform != simulator.sourceScreen)
                throw new InvalidOperationException("Both content cameras must use the simulator's source screen for their CC_CAMERAOFFSET projection.");
        }
        foreach (Transform target in new[] { simulator.ccHub, simulator.ccHead, simulator.sourceScreen,
            simulator.roomRoot, simulator.virtualScreen, simulator.trackingSpace, simulator.headsetCamera.transform,
            simulator.leftContentCamera.transform, simulator.rightContentCamera.transform })
            if (target.gameObject.scene != scene)
                throw new InvalidOperationException("All headset simulator references must belong to the current scene.");
    }

    static bool UsesNewInputSystem()
    {
        var player = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
        if (!player) return false;
        SerializedProperty input = new SerializedObject(player).FindProperty("activeInputHandler");
        return input != null && (input.intValue == 1 || input.intValue == 2);
    }
}
