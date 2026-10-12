using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Applies the Keller 102 LAVA LED wall measurements to a Cyber Canoe scene and headset room.</summary>
public static class CyberCanoeLEDWallCalibration
{
    const string MenuPath = "Tools/Cyber Canoe/Apply LAVA LED Wall Calibration (Keller 102)";
    const float WallWidth = 6.047f;
    const float WallHeight = 2.042f;
    const float ViewingDistance = 4.282f;
    const int EyeWidth = 4800;
    const int EyeHeight = 1620;
    static readonly Vector3 WallCenter = new Vector3(0f, 1.75f, 2.282f);
    static readonly Vector3 HeadCenter = new Vector3(0f, 1.76f, -2f);

    [MenuItem(MenuPath)]
    public static void ApplyCalibration() => ApplyCalibration(FindSimulator());

    [MenuItem(MenuPath, true)]
    static bool CanApplyCalibration() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        PrefabStageUtility.GetCurrentPrefabStage() == null &&
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().IsValid() &&
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().isLoaded;

    public static void ApplyCalibration(CyberCanoeHeadsetSimulator simulator)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before applying the Keller 102 calibration.");
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Close Prefab mode before applying a scene wall calibration.");
        ValidateCalibrationSetup(simulator);
        var scene = simulator.gameObject.scene;
        var setup = simulator.ccHead.GetComponent<CC_CAMERA_SETUP>();
        if (!setup || !setup.CCSettings || Mathf.Abs(setup.CCSettings.eyeSeparation - 0.061f) > 0.00001f ||
            setup.CCSettings.invertStereo || setup.CCSettings.stereoMode != CC_StereoMode.sidebyside)
            throw new InvalidOperationException("Keller 102 requires the existing 61 mm, non-inverted side-by-side CC settings.");
        if ((simulator.ccHub.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException("Keller 102's metre measurements require a unit-scale CC_HUB.");

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Calibrate Cyber Canoe for Keller 102");
        try
        {
            SetPose(simulator.sourceScreen, simulator.ccHub.TransformPoint(WallCenter), simulator.ccHub.rotation,
                new Vector3(WallWidth, WallHeight, 1f));
            SetPose(simulator.ccHead, simulator.ccHub.TransformPoint(HeadCenter), simulator.ccHub.rotation,
                simulator.ccHead.localScale);
            SetPose(simulator.transform, simulator.ccHub.position, simulator.ccHub.rotation, Vector3.one);
            SetLocalPose(simulator.roomRoot, Vector3.zero, Vector3.one);
            SetLocalPose(simulator.virtualScreen, WallCenter, new Vector3(WallWidth, WallHeight, 1f));
            SetLocalPose(simulator.trackingSpace, HeadCenter, Vector3.one);

            Undo.RecordObject(simulator, "Apply Keller 102 headset parameters");
            simulator.viewingDistanceMeters = ViewingDistance;
            simulator.viewerOffsetMeters = new Vector2(0f, 0.01f);
            simulator.textureWidth = EyeWidth;
            simulator.textureHeight = EyeHeight;
            Record(simulator);
            ResizeRoom(simulator);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            Debug.Log("CYBER_CANOE_LED_WALL: Calibrated 6.047 x 2.042 m wall; head (0, 1.76, -2); 4800 x 1620 pixels per eye.");
        }
        catch
        {
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }

    static void ValidateCalibrationSetup(CyberCanoeHeadsetSimulator simulator)
    {
        if (!simulator) throw new ArgumentNullException(nameof(simulator));
        var scene = simulator.gameObject.scene;
        if (EditorUtility.IsPersistent(simulator) || !scene.IsValid() || !scene.isLoaded ||
            scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene())
            throw new InvalidOperationException("Choose a headset simulator in the current active scene.");
        if (!simulator.ccHub || !simulator.ccHead || !simulator.sourceScreen || !simulator.roomRoot ||
            !simulator.virtualScreen || !simulator.trackingSpace || !simulator.leftContentCamera ||
            !simulator.rightContentCamera || !simulator.headsetCamera || !simulator.virtualScreenRenderer)
            throw new InvalidOperationException("Assign the Cyber Canoe rig, content cameras and complete headset room before calibration.");
        if (simulator.ccHead == simulator.ccHub || simulator.sourceScreen == simulator.ccHub ||
            simulator.sourceScreen == simulator.ccHead || simulator.virtualScreen == simulator.trackingSpace ||
            simulator.leftContentCamera == simulator.rightContentCamera ||
            simulator.leftContentCamera == simulator.headsetCamera || simulator.rightContentCamera == simulator.headsetCamera)
            throw new InvalidOperationException("Use distinct hub, head, screens, tracking space and content/headset cameras.");
        foreach (Transform target in new[] { simulator.ccHub, simulator.ccHead, simulator.sourceScreen,
            simulator.roomRoot, simulator.virtualScreen, simulator.trackingSpace,
            simulator.leftContentCamera.transform, simulator.rightContentCamera.transform,
            simulator.headsetCamera.transform, simulator.virtualScreenRenderer.transform })
            if (target.gameObject.scene != scene)
                throw new InvalidOperationException("All calibrated rig and headset room references must belong to the simulator's scene.");
        if (simulator.roomRoot.parent != simulator.transform || simulator.virtualScreen.parent != simulator.roomRoot ||
            simulator.trackingSpace.parent != simulator.roomRoot || simulator.headsetCamera.transform.parent != simulator.trackingSpace ||
            simulator.virtualScreenRenderer.transform != simulator.virtualScreen || simulator.roomRoot.IsChildOf(simulator.ccHub) ||
            !simulator.ccHead.IsChildOf(simulator.ccHub) || !simulator.sourceScreen.IsChildOf(simulator.ccHub))
            throw new InvalidOperationException("Keep the calibrated CC head/screen under CC_HUB and the independent headset room under its simulator.");
        foreach (Transform target in new[] { simulator.ccHub, simulator.ccHead, simulator.sourceScreen.parent,
            simulator.ccHead.parent, simulator.transform.parent })
            if (target && (target.lossyScale - Vector3.one).sqrMagnitude > 0.000001f)
                throw new InvalidOperationException("Keller 102's metre measurements require unit-scale CC_HUB and rig parents.");
        var setup = simulator.ccHead.GetComponent<CC_CAMERA_SETUP>();
        if (!setup || setup.leftCamera != simulator.leftContentCamera.gameObject ||
            setup.rightCamera != simulator.rightContentCamera.gameObject ||
            !simulator.leftContentCamera.transform.IsChildOf(simulator.ccHead) ||
            !simulator.rightContentCamera.transform.IsChildOf(simulator.ccHead))
            throw new InvalidOperationException("Use the simulator's two content cameras beneath the configured CC_HEAD.");
        foreach (Camera camera in new[] { simulator.leftContentCamera, simulator.rightContentCamera })
        {
            var offset = camera.GetComponent<CC_CAMERAOFFSET>();
            if (!offset || !offset.projectionScreen || offset.projectionScreen.transform != simulator.sourceScreen)
                throw new InvalidOperationException("Both content cameras must project onto the calibrated Cyber Canoe source screen.");
        }
        Transform frame = RequiredChild(simulator.virtualScreen, "ScreenFrame");
        foreach (string name in new[] { "FrameBottom", "FrameTop", "FrameLeft", "FrameRight" }) RequiredChild(frame, name);
        Transform floor = RequiredChild(simulator.roomRoot, "RoomFloor");
        if (floor == simulator.virtualScreen || floor == simulator.trackingSpace)
            throw new InvalidOperationException("Keep the headset room floor separate from its screen and tracking space.");
    }

    static void SetPose(Transform target, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        Undo.RecordObject(target, "Apply Keller 102 room geometry");
        target.SetPositionAndRotation(position, rotation);
        target.localScale = scale;
        Record(target);
    }

    static void SetLocalPose(Transform target, Vector3 position, Vector3 scale)
    {
        Undo.RecordObject(target, "Apply Keller 102 headset room geometry");
        target.localPosition = position;
        target.localRotation = Quaternion.identity;
        target.localScale = scale;
        Record(target);
    }

    static void Record(Object target)
    {
        EditorUtility.SetDirty(target);
        if (PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    static void ResizeRoom(CyberCanoeHeadsetSimulator simulator)
    {
        Transform frame = simulator.virtualScreen.Find("ScreenFrame");
        if (!frame) throw new InvalidOperationException("The existing headset screen frame is missing.");
        SetLocalPose(frame, Vector3.zero, new Vector3(1f / WallWidth, 1f / WallHeight, 1f));
        const float border = 0.06f;
        const float thickness = 0.04f;
        SetLocalPose(RequiredChild(frame, "FrameBottom"), new Vector3(0f, -(WallHeight + border) * 0.5f, thickness),
            new Vector3(WallWidth + border * 2f, border, thickness));
        SetLocalPose(RequiredChild(frame, "FrameTop"), new Vector3(0f, (WallHeight + border) * 0.5f, thickness),
            new Vector3(WallWidth + border * 2f, border, thickness));
        SetLocalPose(RequiredChild(frame, "FrameLeft"), new Vector3(-(WallWidth + border) * 0.5f, 0f, thickness),
            new Vector3(border, WallHeight, thickness));
        SetLocalPose(RequiredChild(frame, "FrameRight"), new Vector3((WallWidth + border) * 0.5f, 0f, thickness),
            new Vector3(border, WallHeight, thickness));
        float depth = Mathf.Max(5f, ViewingDistance + 4f);
        SetLocalPose(RequiredChild(simulator.roomRoot, "RoomFloor"),
            new Vector3(0f, -0.04f, WallCenter.z - depth * 0.5f), new Vector3(WallWidth + 2f, 0.08f, depth));
    }

    static Transform RequiredChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (!child) throw new InvalidOperationException("The existing headset room is missing " + name + ".");
        return child;
    }

    static CyberCanoeHeadsetSimulator FindSimulator()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Open a scene containing the Cyber Canoe headset simulator first.");
        GameObject selected = Selection.activeGameObject;
        if (selected && selected.scene == scene)
        {
            var selectedSimulator = selected.GetComponentInParent<CyberCanoeHeadsetSimulator>(true);
            if (selectedSimulator) return selectedSimulator;
        }
        CyberCanoeHeadsetSimulator found = null;
        foreach (var simulator in Object.FindObjectsByType<CyberCanoeHeadsetSimulator>(FindObjectsInactive.Include))
        {
            if (simulator.gameObject.scene != scene) continue;
            if (found) throw new InvalidOperationException("Select the headset simulator to calibrate when the current scene contains more than one.");
            found = simulator;
        }
        if (found) return found;
        throw new InvalidOperationException("The current scene has no Cyber Canoe headset simulator. Add one from Tools > Cyber Canoe first.");
    }

}
