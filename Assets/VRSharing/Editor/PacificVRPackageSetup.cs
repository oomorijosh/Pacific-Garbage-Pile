#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace PacificExhibit.Sharing
{
    /// <summary>Restores project settings that are not carried by a Unity asset package.</summary>
    public static class PacificVRPackageSetup
    {
        const string MenuPath = "Tools/Pacific Exhibit/Prepare Imported VR Demo";
        const string ScenePath = "Assets/Scenes/Plastic_Type.unity";
        const string PipelinePath = "Assets/Settings/PC_RPAsset.asset";
        const string GlobalSettingsPath = "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset";
        const string XRSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        const string OpenXRSettingsPath = "Assets/XR/Settings/OpenXR Package Settings.asset";
        const string LoaderPath = "Assets/XR/Loaders/OpenXRLoader.asset";
        const int RoomLayer = 31;
        const string RoomLayerName = "CC_HeadsetRoom";

        [MenuItem(MenuPath)]
        public static void PrepareImportedDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Stop Play mode and wait for any build to finish before preparing the imported VR demo.");

            // Check the imported assets and layer before changing the recipient's settings.
            Required<SceneAsset>(ScenePath);
            Required<UniversalRenderPipelineAsset>(PipelinePath);
            Required<RenderPipelineGlobalSettings>(GlobalSettingsPath);
            Required<XRGeneralSettingsPerBuildTarget>(XRSettingsPath);
            Required<ScriptableObject>(OpenXRSettingsPath);
            Required<OpenXRLoader>(LoaderPath);
            CheckedRoomLayerSettings();

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var simulators = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CyberCanoeHeadsetSimulator>(true)).ToArray();
            if (simulators.Length != 1)
                throw new InvalidOperationException("The imported demo must contain exactly one CyberCanoe headset simulator. Reimport the complete scene.");
            CyberCanoeHeadsetSimulator simulator = simulators[0];
            ValidateRig(simulator);

            // Opening a scene can unload assets held only by local C# references.
            // Load the settings after opening the demo so every assigned object remains valid.
            var xrSettings = Required<XRGeneralSettingsPerBuildTarget>(XRSettingsPath);
            var openXRPackage = Required<ScriptableObject>(OpenXRSettingsPath);
            var loader = Required<OpenXRLoader>(LoaderPath);
            var general = xrSettings.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!general || !general.Manager)
                throw new InvalidOperationException("The imported XR settings do not contain a desktop XR manager. Reimport the complete VR package.");
            var openXR = AssetDatabase.LoadAllAssetsAtPath(OpenXRSettingsPath)
                .OfType<OpenXRSettings>().SingleOrDefault(settings => settings.name == "Standalone");
            if (!openXR)
                throw new InvalidOperationException("The imported OpenXR settings do not contain desktop settings. Reimport the complete VR package.");
            SerializedObject tagSettings = CheckedRoomLayerSettings();
            tagSettings.FindProperty("layers").GetArrayElementAtIndex(RoomLayer).stringValue = RoomLayerName;
            tagSettings.ApplyModifiedProperties();

            // These registrations normally live in ProjectSettings/EditorBuildSettings.asset.
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, xrSettings, true);
            EditorBuildSettings.AddConfigObject(Constants.k_SettingsKey, openXRPackage, true);
            XRGeneralSettings.Instance = general;
            general.InitManagerOnStart = false;
            XRManagerSettings manager = general.Manager;
            manager.automaticLoading = false;
            manager.automaticRunning = false;
            if (!manager.TrySetLoaders(new List<XRLoader> { loader }))
                throw new InvalidOperationException("Could not assign the imported OpenXR loader to the desktop XR manager.");
            openXR.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(openXR);
            EditorUtility.SetDirty(xrSettings);

            ConfigureRendering(Required<UniversalRenderPipelineAsset>(PipelinePath),
                Required<RenderPipelineGlobalSettings>(GlobalSettingsPath));
            PlayerSettings.colorSpace = ColorSpace.Linear;

            Undo.RecordObjects(new UnityEngine.Object[] { simulator, simulator.gameObject }, "Prepare imported VR demo");
            simulator.gameObject.SetActive(true);
            simulator.enabled = true;
            simulator.initializeXR = true;
            EditorUtility.SetDirty(simulator);
            EditorSceneManager.MarkSceneDirty(scene);

            var scenes = EditorBuildSettings.scenes.ToList();
            int sceneIndex = scenes.FindIndex(item => item.path == ScenePath);
            if (sceneIndex >= 0) scenes[sceneIndex].enabled = true;
            else scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("The VR settings were prepared, but Unity could not save the imported demo scene. Save it before entering Play mode.");

            Selection.activeGameObject = simulator.gameObject;
            EditorGUIUtility.PingObject(simulator);
            if (!UsesNewInputSystem())
                Debug.LogWarning("Before pressing Play, open Edit > Project Settings > Player > Other Settings > Active Input Handling, " +
                    "select Input System Package (New) or Both, and restart Unity when prompted. The setup menu does not change this setting or restart the Editor.", simulator);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows)
                Debug.LogWarning("This demo is prepared for Windows PC VR. Select a Windows build profile before headset testing.", simulator);
            Debug.Log("PACIFIC_VR_PACKAGE: The imported demo is prepared and saved. Its camera rig and wall calibration are preserved. " +
                "Connect the headset and select Meta Link or SteamVR as the active OpenXR runtime, then press Play. " +
                "Use Recenter Viewer at Calibrated Position in the simulator Inspector to reset the starting view. " +
                "Keyboard/gamepad navigation is supported; headset controller navigation is not configured.", simulator);
        }

        [MenuItem(MenuPath, true)]
        static bool CanPrepareImportedDemo() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            !EditorApplication.isCompiling && !BuildPipeline.isBuildingPlayer &&
            PrefabStageUtility.GetCurrentPrefabStage() == null;

        static T Required<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset)
                throw new InvalidOperationException("Required VR package asset is missing: " + path + ". Reimport the complete package.");
            return asset;
        }

        static void ConfigureRendering(UniversalRenderPipelineAsset pipeline, RenderPipelineGlobalSettings globalSettings)
        {
            if (!pipeline || !globalSettings)
                throw new InvalidOperationException("The imported render pipeline settings are unavailable. Run Prepare Imported VR Demo again.");
            var graphics = ProjectSettingsObject("ProjectSettings/GraphicsSettings.asset");
            var quality = ProjectSettingsObject("ProjectSettings/QualitySettings.asset");
            SerializedProperty defaultPipeline = graphics.FindProperty("m_CustomRenderPipeline");
            SerializedProperty levels = quality.FindProperty("m_QualitySettings");
            int currentLevel = QualitySettings.GetQualityLevel();
            if (defaultPipeline == null || levels == null || currentLevel < 0 || currentLevel >= levels.arraySize)
                throw new InvalidOperationException("Unity's render pipeline project settings could not be loaded.");
            SerializedProperty qualityPipeline = levels.GetArrayElementAtIndex(currentLevel).FindPropertyRelative("customRenderPipeline");
            if (qualityPipeline == null)
                throw new InvalidOperationException("Unity's current Quality level does not expose a render pipeline setting.");

            // Write the serialized Editor settings as well as the current runtime values.
            defaultPipeline.objectReferenceValue = pipeline;
            graphics.ApplyModifiedPropertiesWithoutUndo();
            qualityPipeline.objectReferenceValue = pipeline;
            quality.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>(globalSettings);
            EditorUtility.SetDirty(graphics.targetObject);
            EditorUtility.SetDirty(quality.targetObject);
            if (GraphicsSettings.defaultRenderPipeline != pipeline || QualitySettings.renderPipeline != pipeline ||
                EditorGraphicsSettings.GetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>() != globalSettings)
                throw new InvalidOperationException("Unity could not assign the imported render pipeline and global settings. Run Prepare Imported VR Demo again before entering Play mode.");
        }

        static SerializedObject ProjectSettingsObject(string path)
        {
            UnityEngine.Object settings = AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault();
            if (!settings)
                throw new InvalidOperationException("Unity project settings could not be loaded: " + path);
            return new SerializedObject(settings);
        }

        static SerializedObject CheckedRoomLayerSettings()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets.Length == 0)
                throw new InvalidOperationException("Unity's layer settings could not be loaded.");
            var tags = new SerializedObject(assets[0]);
            SerializedProperty layers = tags.FindProperty("layers");
            if (layers == null || layers.arraySize <= RoomLayer)
                throw new InvalidOperationException("Unity's user layer 31 is unavailable.");
            string existingName = layers.GetArrayElementAtIndex(RoomLayer).stringValue;
            if (!string.IsNullOrEmpty(existingName) && existingName != RoomLayerName)
                throw new InvalidOperationException("Layer 31 is already named '" + existingName + "'. The imported VR rig uses this layer for its private headset room. " +
                    "Import into a fresh Universal 3D project so existing layers are preserved.");
            return tags;
        }

        static void ValidateRig(CyberCanoeHeadsetSimulator simulator)
        {
            if (!simulator.ccHub || !simulator.ccHead || !simulator.sourceScreen ||
                !simulator.leftContentCamera || !simulator.rightContentCamera ||
                !simulator.outputCanvas || !simulator.outputLayout || !simulator.roomRoot ||
                !simulator.virtualScreen || !simulator.trackingSpace || !simulator.headsetCamera ||
                !simulator.virtualScreenRenderer || !simulator.screenMaterialTemplate)
                throw new InvalidOperationException("The imported headset simulator is missing camera, screen, or room references. Reimport the complete VR scene.");
            if (simulator.leftContentCamera == simulator.rightContentCamera ||
                simulator.leftContentCamera == simulator.headsetCamera ||
                simulator.rightContentCamera == simulator.headsetCamera)
                throw new InvalidOperationException("The headset viewer and stereo content require three separate cameras.");
            if (simulator.roomRoot.IsChildOf(simulator.ccHub) || simulator.roomRoot == simulator.transform ||
                simulator.transform.IsChildOf(simulator.roomRoot) ||
                !simulator.virtualScreen.IsChildOf(simulator.roomRoot) ||
                !simulator.trackingSpace.IsChildOf(simulator.roomRoot) ||
                !simulator.virtualScreenRenderer.transform.IsChildOf(simulator.roomRoot) ||
                simulator.headsetCamera.transform.parent != simulator.trackingSpace)
                throw new InvalidOperationException("The headset room must stay separate from CC_HUB, with the headset camera underneath TrackingSpace. Reimport the complete rig.");
            if (simulator.headsetCamera.gameObject.layer != RoomLayer || simulator.virtualScreenRenderer.gameObject.layer != RoomLayer)
                throw new InvalidOperationException("The imported headset camera and virtual screen must use the package's private layer 31.");
            if (!UnitScale(simulator.ccHub.lossyScale) || !UnitScale(simulator.ccHead.lossyScale) ||
                !UnitScale(simulator.trackingSpace.lossyScale) || !UnitScale(simulator.headsetCamera.transform.lossyScale))
                throw new InvalidOperationException("Keep the imported camera rig and headset parent transforms at scale (1, 1, 1).");
            if (!simulator.screenMaterialTemplate.HasProperty("_LeftTex") ||
                !simulator.screenMaterialTemplate.HasProperty("_RightTex"))
                throw new InvalidOperationException("The virtual screen's stereo material is missing. Reimport the complete HeadsetPreview folder.");
        }

        static bool UnitScale(Vector3 scale) => (scale - Vector3.one).sqrMagnitude < 0.000001f;

        static bool UsesNewInputSystem()
        {
            UnityEngine.Object settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (!settings) return false;
            SerializedProperty handling = new SerializedObject(settings).FindProperty("activeInputHandler");
            return handling != null && (handling.intValue == 1 || handling.intValue == 2);
        }
    }
}
#endif
