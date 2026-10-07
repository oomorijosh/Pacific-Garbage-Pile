using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

/// <summary>Builds an independent, real-headset viewing room for the campus wall.</summary>
public static class CyberCanoeHeadsetSetup
{
    const string MenuPath = "Tools/Cyber Canoe/Add Headset Screen Simulator";
    const string CalibrationMenuPath = "Tools/Cyber Canoe/Apply Campus Calibration (4080 x 1152, 20 ft)";
    const int CampusEyeWidth = 2040;
    const int CampusEyeHeight = 1152;
    const float CampusViewingDistance = 6.096f;
    const string RoomLayerName = "CC_HeadsetRoom";
    const string MaterialPath = "Assets/HeadsetPreview/Materials/CyberCanoeStereoScreen.mat";
    const string RoomMaterialPath = "Assets/HeadsetPreview/Materials/CyberCanoeRoom.mat";

    [MenuItem(MenuPath)]
    public static void AddSimulator()
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (var existing in UnityEngine.Object.FindObjectsByType<CyberCanoeHeadsetSimulator>(
                     FindObjectsInactive.Include))
        {
            if (existing.gameObject.scene != scene) continue;
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            return;
        }

        if (!FindSource(scene, out CC_CAMERA_SETUP source, out Transform hub, out Transform screen,
                out Camera leftCamera, out Camera rightCamera))
        {
            Debug.LogError("Cyber Canoe headset setup needs CC_HEAD with CC_CAMERA_SETUP, two cameras " +
                           "using the same CC_CAMERAOFFSET projection screen, and a CC_HUB ancestor.");
            return;
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var roomMaterial = AssetDatabase.LoadAssetAtPath<Material>(RoomMaterialPath);
        if (!material || !roomMaterial)
        {
            Debug.LogError("Cyber Canoe headset materials are missing. Reimport Assets/HeadsetPreview.");
            return;
        }

        Vector3 lowerLeft = hub.InverseTransformPoint(screen.TransformPoint(new Vector3(-0.5f, -0.5f, 0f)));
        Vector3 lowerRight = hub.InverseTransformPoint(screen.TransformPoint(new Vector3(0.5f, -0.5f, 0f)));
        Vector3 upperLeft = hub.InverseTransformPoint(screen.TransformPoint(new Vector3(-0.5f, 0.5f, 0f)));
        Vector3 right = lowerRight - lowerLeft;
        Vector3 up = upperLeft - lowerLeft;
        if (right.magnitude < 0.001f || up.magnitude < 0.001f ||
            Mathf.Abs(Vector3.Dot(right.normalized, up.normalized)) > 0.001f)
        {
            Debug.LogError("The Cyber Canoe projection screen must be a nonzero rectangular plane.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Cyber Canoe headset simulator");
        try
        {
            int roomLayer = ReserveRoomLayer();
            if (roomLayer < 0)
            {
                Debug.LogError("Cyber Canoe headset setup needs an unused user layer for CC_HeadsetRoom.");
                Undo.RevertAllDownToGroup(undoGroup);
                return;
            }

            GameObject root = CreateObject("CyberCanoe_HeadsetSimulator", null, roomLayer, scene);
            root.SetActive(false);
            GameObject room = CreateObject("Headset_Room", root.transform, roomLayer, scene);
            room.SetActive(false);
            Transform virtualScreen = CreatePrimitive("VirtualCyberCanoeScreen", PrimitiveType.Quad,
                room.transform, roomLayer, material).transform;
            virtualScreen.localPosition = lowerLeft + (right + up) * 0.5f;
            virtualScreen.localRotation = Quaternion.LookRotation(Vector3.Cross(right, up).normalized, up.normalized);
            virtualScreen.localScale = new Vector3(right.magnitude, up.magnitude, 1f);

            Vector3 viewerPosition = room.transform.InverseTransformPoint(
                CyberCanoeHeadsetSimulator.CalculateCenteredViewer(virtualScreen, CampusViewingDistance));
            Quaternion viewerRotation = virtualScreen.localRotation;
            CreateRoom(room.transform, virtualScreen, viewerPosition, roomLayer, roomMaterial, scene);

            Transform trackingSpace = CreateObject("TrackingSpace", room.transform, roomLayer, scene).transform;
            trackingSpace.localPosition = viewerPosition;
            trackingSpace.localRotation = viewerRotation;
            GameObject cameraObject = CreateObject("HeadsetCamera", trackingSpace, roomLayer, scene);
            Camera headsetCamera = Undo.AddComponent<Camera>(cameraObject);
            headsetCamera.cullingMask = 1 << roomLayer;
            headsetCamera.clearFlags = CameraClearFlags.SolidColor;
            headsetCamera.backgroundColor = new Color(0.012f, 0.014f, 0.02f, 1f);
            headsetCamera.nearClipPlane = 0.03f;
            headsetCamera.farClipPlane = 50f;
            headsetCamera.depth = 100f;
            headsetCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            if (!HasActiveMainCamera()) cameraObject.tag = "MainCamera";
            var cameraData = Undo.AddComponent<UniversalAdditionalCameraData>(cameraObject);
            cameraData.renderType = CameraRenderType.Base;
            cameraData.allowXRRendering = true;
            cameraData.renderPostProcessing = false;
            cameraData.renderShadows = false;

            var poseDriver = Undo.AddComponent<TrackedPoseDriver>(cameraObject);
            poseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            poseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            poseDriver.ignoreTrackingState = false;
            poseDriver.positionInput = new InputActionProperty(new InputAction("Headset Position",
                InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            poseDriver.rotationInput = new InputActionProperty(new InputAction("Headset Rotation",
                InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            poseDriver.trackingStateInput = new InputActionProperty(new InputAction("Headset Tracking State",
                InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

            var simulator = Undo.AddComponent<CyberCanoeHeadsetSimulator>(root);
            simulator.ccHub = hub;
            simulator.ccHead = source.transform;
            simulator.sourceScreen = screen;
            simulator.roomRoot = room.transform;
            simulator.virtualScreen = virtualScreen;
            simulator.trackingSpace = trackingSpace;
            simulator.leftContentCamera = leftCamera;
            simulator.rightContentCamera = rightCamera;
            simulator.headsetCamera = headsetCamera;
            simulator.virtualScreenRenderer = virtualScreen.GetComponent<MeshRenderer>();
            simulator.screenMaterialTemplate = material;
            simulator.outputLayout = hub.GetComponentInChildren<CC_CANVAS_SETUP>(true);
            if (simulator.outputLayout)
                simulator.outputCanvas = simulator.outputLayout.GetComponent<Canvas>();
            simulator.textureWidth = CampusEyeWidth;
            simulator.textureHeight = CampusEyeHeight;
            simulator.viewingDistanceMeters = CampusViewingDistance;
            simulator.initializeXR = true;
            root.SetActive(true);
            EditorUtility.SetDirty(simulator);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Undo.CollapseUndoOperations(undoGroup);

            if (!HasStandaloneOpenXR())
                Debug.LogWarning("Cyber Canoe headset room added. To use your Steam Frame, assign OpenXR " +
                                 "under Project Settings > XR Plug-in Management > Windows, Mac, Linux, " +
                                 "and set SteamVR as the OpenXR runtime. Then start SteamVR and enter Play mode.", root);
            else
                Debug.Log("Cyber Canoe headset room added. Start SteamVR, wear the headset, and enter Play mode. " +
                          "The viewer starts centered, 20 feet from the wall, with 2040 x 1152 pixels per eye.", root);
        }
        catch (Exception exception)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogException(exception);
        }
    }

    [MenuItem(MenuPath, true)]
    static bool CanAddSimulator() => !EditorApplication.isPlayingOrWillChangePlaymode &&
                                    PrefabStageUtility.GetCurrentPrefabStage() == null &&
                                    SceneManager.GetActiveScene().IsValid();

    [MenuItem(CalibrationMenuPath)]
    static void ApplyCampusCalibrationToScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (var simulator in UnityEngine.Object.FindObjectsByType<CyberCanoeHeadsetSimulator>(
                     FindObjectsInactive.Include))
        {
            if (simulator.gameObject.scene != scene) continue;
            ApplyCampusCalibration(simulator);
            Selection.activeGameObject = simulator.gameObject;
            return;
        }
        AddSimulator();
    }

    [MenuItem(CalibrationMenuPath, true)]
    static bool CanApplyCampusCalibration() => CanAddSimulator();

    public static void ApplyCampusCalibration(CyberCanoeHeadsetSimulator simulator)
    {
        if (!simulator || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Undo.RecordObject(simulator, "Calibrate Cyber Canoe headset simulator");
        simulator.textureWidth = CampusEyeWidth;
        simulator.textureHeight = CampusEyeHeight;
        simulator.viewingDistanceMeters = CampusViewingDistance;
        simulator.viewerOffsetMeters = Vector2.zero;
        if (simulator.virtualScreen && simulator.trackingSpace)
        {
            Undo.RecordObject(simulator.trackingSpace, "Calibrate Cyber Canoe headset viewer");
            simulator.trackingSpace.SetPositionAndRotation(
                CyberCanoeHeadsetSimulator.CalculateCenteredViewer(simulator.virtualScreen, CampusViewingDistance),
                Quaternion.LookRotation(simulator.virtualScreen.forward, simulator.virtualScreen.up));
            EditorUtility.SetDirty(simulator.trackingSpace);
        }
        EditorUtility.SetDirty(simulator);
        EditorSceneManager.MarkSceneDirty(simulator.gameObject.scene);
    }

    static bool FindSource(Scene scene, out CC_CAMERA_SETUP source, out Transform hub,
        out Transform screen, out Camera left, out Camera right)
    {
        source = null;
        hub = null;
        screen = null;
        left = null;
        right = null;
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<CC_CAMERA_SETUP>(FindObjectsInactive.Include))
        {
            if (candidate.gameObject.scene != scene || !candidate.gameObject.activeInHierarchy || !candidate.enabled ||
                !candidate.CCSettings || !candidate.leftCamera || !candidate.rightCamera ||
                candidate.leftCamera == candidate.rightCamera) continue;
            Camera candidateLeft = candidate.leftCamera.GetComponent<Camera>();
            Camera candidateRight = candidate.rightCamera.GetComponent<Camera>();
            CC_CAMERAOFFSET leftOffset = candidate.leftCamera.GetComponent<CC_CAMERAOFFSET>();
            CC_CAMERAOFFSET rightOffset = candidate.rightCamera.GetComponent<CC_CAMERAOFFSET>();
            if (!candidateLeft || !candidateRight || !leftOffset || !rightOffset || !leftOffset.projectionScreen ||
                leftOffset.projectionScreen != rightOffset.projectionScreen) continue;
            Transform candidateHub = candidate.transform.parent;
            while (candidateHub && candidateHub.name != "CC_HUB") candidateHub = candidateHub.parent;
            if (!candidateHub) continue;
            source = candidate;
            hub = candidateHub;
            screen = leftOffset.projectionScreen.transform;
            left = candidateLeft;
            right = candidateRight;
            return true;
        }
        return false;
    }

    static int ReserveRoomLayer()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets.Length == 0) return -1;
        var tags = new SerializedObject(assets[0]);
        var layers = tags.FindProperty("layers");
        if (layers == null) return -1;
        for (int i = 8; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == RoomLayerName) return i;

        var sceneTransforms = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        for (int i = Math.Min(31, layers.arraySize - 1); i >= 8; i--)
        {
            if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue)) continue;
            bool occupied = false;
            foreach (Transform candidate in sceneTransforms)
                if (candidate.gameObject.scene.IsValid() && candidate.gameObject.layer == i) { occupied = true; break; }
            if (occupied) continue;
            Undo.RecordObject(assets[0], "Reserve Cyber Canoe headset room layer");
            layers.GetArrayElementAtIndex(i).stringValue = RoomLayerName;
            tags.ApplyModifiedProperties();
            return i;
        }
        return -1;
    }

    static GameObject CreateObject(string name, Transform parent, int layer, Scene scene)
    {
        var result = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(result, "Add Cyber Canoe headset simulator");
        if (parent) result.transform.SetParent(parent, false);
        else SceneManager.MoveGameObjectToScene(result, scene);
        result.layer = layer;
        return result;
    }

    static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, int layer, Material material)
    {
        GameObject result = GameObject.CreatePrimitive(type);
        result.name = name;
        Undo.RegisterCreatedObjectUndo(result, "Add Cyber Canoe headset simulator");
        result.transform.SetParent(parent, false);
        result.layer = layer;
        if (result.TryGetComponent<Collider>(out var collider)) Undo.DestroyObjectImmediate(collider);
        var renderer = result.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return result;
    }

    static void CreateRoom(Transform root, Transform screen, Vector3 viewer, int layer, Material material, Scene scene)
    {
        float width = screen.localScale.x;
        float height = screen.localScale.y;
        // Keep the frame attached to screen size/pose edits while authoring its
        // bars in meter-sized coordinates at the initial dimensions.
        Transform frame = CreateObject("ScreenFrame", screen, layer, scene).transform;
        frame.localScale = new Vector3(1f / width, 1f / height, 1f);
        const float border = 0.06f;
        const float thickness = 0.04f;
        CreateBar("FrameBottom", new Vector3(0f, -(height + border) * 0.5f, thickness),
            new Vector3(width + border * 2f, border, thickness), frame, layer, material);
        CreateBar("FrameTop", new Vector3(0f, (height + border) * 0.5f, thickness),
            new Vector3(width + border * 2f, border, thickness), frame, layer, material);
        CreateBar("FrameLeft", new Vector3(-(width + border) * 0.5f, 0f, thickness),
            new Vector3(border, height, thickness), frame, layer, material);
        CreateBar("FrameRight", new Vector3((width + border) * 0.5f, 0f, thickness),
            new Vector3(border, height, thickness), frame, layer, material);
        float depth = Mathf.Max(5f, Mathf.Abs(screen.localPosition.z - viewer.z) + 4f);
        CreateBar("RoomFloor", new Vector3(screen.localPosition.x, -0.04f, screen.localPosition.z - depth * 0.5f),
            new Vector3(width + 2f, 0.08f, depth), root, layer, material);
    }

    static void CreateBar(string name, Vector3 position, Vector3 scale, Transform parent, int layer, Material material)
    {
        Transform result = CreatePrimitive(name, PrimitiveType.Cube, parent, layer, material).transform;
        result.localPosition = position;
        result.localScale = scale;
    }

    static bool HasActiveMainCamera()
    {
        foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>())
            if (camera.enabled && camera.gameObject.activeInHierarchy && camera.CompareTag("MainCamera")) return true;
        return false;
    }

    public static bool HasStandaloneOpenXR()
    {
        // Read settings without the static helper, which can register settings assets as a side effect.
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget settings);
        if (!settings)
            foreach (string guid in AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget"))
            {
                settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid));
                if (settings) break;
            }
        var manager = settings ? settings.SettingsForBuildTarget(BuildTargetGroup.Standalone)?.Manager : null;
        if (!manager) return false;
        foreach (XRLoader loader in manager.activeLoaders)
            if (loader is OpenXRLoader) return true;
        return false;
    }
}

[CustomEditor(typeof(CyberCanoeHeadsetSimulator))]
public sealed class CyberCanoeHeadsetSimulatorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.HelpBox("Wear your headset and enter Play mode with SteamVR running. The virtual wall matches " +
                                "the configured Cyber Canoe screen. Content uses the calibrated CC_HEAD viewpoint; " +
                                "headset motion changes where you view the wall from.", MessageType.Info);
        DrawDefaultInspector();
        var simulator = (CyberCanoeHeadsetSimulator)target;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode ||
            PrefabStageUtility.GetCurrentPrefabStage() != null || !simulator.gameObject.scene.IsValid() ||
            !simulator.gameObject.scene.isLoaded || EditorUtility.IsPersistent(simulator)))
            if (GUILayout.Button("Apply LavaLab LED Wall Calibration (Keller 102)", GUILayout.Height(30)))
                PacificKeller102Preview.ApplyCalibration(simulator);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            if (GUILayout.Button("Apply 4080 x 1152 SBS / 20 ft Center Calibration"))
                CyberCanoeHeadsetSetup.ApplyCampusCalibration(simulator);
        if (simulator.virtualScreen && GUILayout.Button("Select Virtual Screen to Adjust Size"))
            Selection.activeGameObject = simulator.virtualScreen.gameObject;
        if (Application.isPlaying)
            EditorGUILayout.LabelField("Status", simulator.Status);
        if (!CyberCanoeHeadsetSetup.HasStandaloneOpenXR())
            EditorGUILayout.HelpBox("Enable OpenXR for Windows, Mac, Linux in XR Plug-in Management, " +
                                    "then select SteamVR as the OpenXR runtime.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
            if (GUILayout.Button("Recenter Viewer at Calibrated Position"))
                ((CyberCanoeHeadsetSimulator)target).RecenterViewer();
    }

    public override bool RequiresConstantRepaint() => Application.isPlaying;
}
