using System;
using System.IO;
using PacificExhibit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using Object = UnityEngine.Object;

/// <summary>Applies Keller 102's measured wall geometry and captures its headset presentation.</summary>
public static class PacificKeller102Preview
{
    const string ScenePath = "Assets/Scenes/PacificExhibit_CyberCanoe.unity";
    const string CalibrationMenuPath = "Tools/Cyber Canoe/Apply LavaLab LED Wall Calibration (Keller 102)";
    const string LegacyCalibrationMenuPath = "Tools/Pacific Exhibit/Apply Keller 102 Headset Calibration";
    const float WallWidth = 6.047f;
    const float WallHeight = 2.042f;
    const float ViewingDistance = 4.282f;
    const int EyeWidth = 4800;
    const int EyeHeight = 1620;
    static readonly Vector3 WallCenter = new Vector3(0f, 1.75f, 2.282f);
    static readonly Vector3 HeadCenter = new Vector3(0f, 1.76f, -2f);
    static int frames;
    static double deadline;

    [MenuItem(LegacyCalibrationMenuPath)]
    public static void ApplyCalibration()
    {
        ApplyCalibration(FindSimulator());
    }

    [MenuItem(CalibrationMenuPath)]
    static void ApplyLavaLabCalibration() => ApplyCalibration();

    [MenuItem(CalibrationMenuPath, true)]
    [MenuItem(LegacyCalibrationMenuPath, true)]
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
        Undo.SetCurrentGroupName("Calibrate Pacific exhibit for Keller 102");
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
            Debug.Log("PACIFIC_KELLER102: Calibrated 6.047 x 2.042 m wall; head (0, 1.76, -2); 4800 x 1620 pixels per eye.");
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

    /// <summary>Batch entry point. Captures a calibrated stereo wall without connecting or starting XR.</summary>
    public static void GeneratePreview()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            ApplyCalibration();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath || !EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException("Could not save the calibrated exhibit scene.");

            // Preview-only changes follow the save and never alter the live headset scene.
            var simulator = FindSimulator();
            simulator.enabled = false;
            simulator.roomRoot.gameObject.SetActive(false);
            if (simulator.outputLayout) simulator.outputLayout.enabled = false;
            if (simulator.outputCanvas) simulator.outputCanvas.enabled = false;
            foreach (var behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (behaviour.gameObject.scene != scene) continue;
                string type = behaviour.GetType().Name;
                if (type == "CCaux_Waypointer" || type == "SpaceMouseFlyAround" ||
                    type == "ModelSwitcher" || type == "Exit") behaviour.enabled = false;
            }
            if (XRGeneralSettings.Instance) XRGeneralSettings.Instance.InitManagerOnStart = false;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            frames = 0;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += CaptureUpdate;
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    static void CaptureUpdate()
    {
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Debug.LogError("PACIFIC_KELLER102: Preview timed out.");
            Finish(1);
            return;
        }
        if (!EditorApplication.isPlaying || ++frames < 35) return;
        EditorApplication.update -= CaptureUpdate;
        try
        {
            CapturePresentation();
            Debug.Log("PACIFIC_KELLER102: Passed. Calibrated stereo content and headset room previews saved in Artifacts.");
            Finish(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    static void CapturePresentation()
    {
        var simulator = FindSimulator();
        var reflection = Object.FindAnyObjectByType<PacificPlanarReflection>();
        if (!reflection || !reflection.floorRenderer) throw new InvalidOperationException("The exhibit floor reflection is missing.");
        int roomMask = 1 << simulator.headsetCamera.gameObject.layer;
        if (simulator.outputCanvas) simulator.outputCanvas.enabled = false;
        simulator.leftContentCamera.cullingMask &= ~roomMask;
        simulator.rightContentCamera.cullingMask &= ~roomMask;
        Vector3 viewer = CyberCanoeHeadsetSimulator.CalculateViewerPosition(simulator.sourceScreen,
            simulator.viewingDistanceMeters, simulator.viewerOffsetMeters);
        if ((viewer - simulator.ccHub.TransformPoint(HeadCenter)).sqrMagnitude > 0.000001f)
            throw new InvalidOperationException("The simulated viewer does not match the XML head position.");
        Vector3 eyeSeparation = simulator.rightContentCamera.transform.position - simulator.leftContentCamera.transform.position;
        if ((eyeSeparation - simulator.sourceScreen.right.normalized * 0.061f).sqrMagnitude > 0.00000001f)
            throw new InvalidOperationException("The live content cameras do not have the XML's 61 mm eye separation.");
        ValidateWallProjection(simulator.leftContentCamera, simulator.sourceScreen);
        ValidateWallProjection(simulator.rightContentCamera, simulator.sourceScreen);
        if (SystemInfo.maxTextureSize < EyeWidth)
            throw new InvalidOperationException("The preview GPU cannot render the 4800-pixel Keller 102 eye images.");

        RenderTexture left = NewTarget("Keller 102 left eye", EyeWidth, EyeHeight);
        RenderTexture right = NewTarget("Keller 102 right eye", EyeWidth, EyeHeight);
        RenderTexture content = NewTarget("Keller 102 content preview", 1600, 540);
        RenderTexture room = NewTarget("Keller 102 headset room preview", 1600, 1000);
        Material screenMaterial = null;
        GameObject cameraObject = null;
        Material[] originalMaterials = simulator.virtualScreenRenderer.sharedMaterials;
        try
        {
            RenderContent(simulator.leftContentCamera, left, reflection);
            RenderContent(simulator.rightContentCamera, right, reflection);
            Graphics.Blit(left, content);
            SavePng(content, "PacificExhibit-Keller102-content-preview.png");

            screenMaterial = new Material(simulator.screenMaterialTemplate) { name = "Keller 102 preview screen" };
            screenMaterial.SetTexture("_LeftTex", left);
            screenMaterial.SetTexture("_RightTex", right);
            simulator.virtualScreenRenderer.sharedMaterial = screenMaterial;
            simulator.virtualScreenRenderer.enabled = true;
            simulator.roomRoot.gameObject.SetActive(true);
            cameraObject = new GameObject("Keller 102 headset preview camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(viewer,
                Quaternion.LookRotation(simulator.sourceScreen.forward, simulator.sourceScreen.up));
            camera.cullingMask = roomMask;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.012f, 0.014f, 0.02f);
            camera.fieldOfView = 65f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 50f;
            camera.allowHDR = true;
            camera.targetTexture = room;
            camera.aspect = 1.6f;
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.allowXRRendering = false;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            Submit(camera, room);
            SavePng(room, "PacificExhibit-Keller102-headset-preview.png");
        }
        finally
        {
            simulator.virtualScreenRenderer.sharedMaterials = originalMaterials;
            simulator.roomRoot.gameObject.SetActive(false);
            if (cameraObject) Object.DestroyImmediate(cameraObject);
            if (screenMaterial) Object.DestroyImmediate(screenMaterial);
            Release(left);
            Release(right);
            Release(content);
            Release(room);
        }
    }

    static void RenderContent(Camera camera, RenderTexture target, PacificPlanarReflection reflection)
    {
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            reflection.RenderReflectionFor(camera);
            Submit(camera, target);
            var properties = new MaterialPropertyBlock();
            reflection.floorRenderer.GetPropertyBlock(properties);
            if (properties.GetFloat("_ReflectionEnabled") < 0.5f)
                throw new InvalidOperationException(camera.name + " did not bind its floor reflection.");
            if (!camera.GetUniversalAdditionalCameraData().renderPostProcessing ||
                VolumeManager.instance.stack.GetComponent<Bloom>().intensity.value < 0.5f)
                throw new InvalidOperationException(camera.name + " did not apply exhibit bloom.");
        }
        finally { camera.targetTexture = previousTarget; }
    }

    static void ValidateWallProjection(Camera camera, Transform screen)
    {
        Matrix4x4 viewProjection = camera.projectionMatrix * camera.worldToCameraMatrix;
        foreach (Vector2 corner in new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f),
                     new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f) })
        {
            Vector3 world = screen.TransformPoint(new Vector3(corner.x, corner.y, 0f));
            Vector4 clip = viewProjection * new Vector4(world.x, world.y, world.z, 1f);
            if (clip.w <= 0f || Mathf.Abs(clip.x / clip.w - corner.x * 2f) > 0.001f ||
                Mathf.Abs(clip.y / clip.w - corner.y * 2f) > 0.001f)
                throw new InvalidOperationException(camera.name + " does not project the full physical wall edge to edge.");
        }
    }

    static void Submit(Camera camera, RenderTexture target)
    {
        var request = new RenderPipeline.StandardRequest { destination = target };
        if (!RenderPipeline.SupportsRenderRequest(camera, request))
            throw new InvalidOperationException("URP cannot render the Keller 102 preview camera.");
        RenderPipeline.SubmitRenderRequest(camera, request);
    }

    static RenderTexture NewTarget(string name, int width, int height)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        if (!target.Create()) throw new InvalidOperationException("Could not allocate " + name + ".");
        return target;
    }

    static void SavePng(RenderTexture target, string filename)
    {
        RenderTexture previousActive = RenderTexture.active;
        Texture2D linear = null, encoded = null;
        try
        {
            RenderTexture.active = target;
            linear = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            linear.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            linear.Apply();
            Color[] pixels = linear.GetPixels();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
            encoded = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            encoded.SetPixels(pixels);
            encoded.Apply();
            Directory.CreateDirectory("Artifacts");
            File.WriteAllBytes("Artifacts/" + filename, encoded.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (linear) Object.DestroyImmediate(linear);
            if (encoded) Object.DestroyImmediate(encoded);
        }
    }

    static void Release(RenderTexture target)
    {
        if (!target) return;
        target.Release();
        Object.DestroyImmediate(target);
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

    static void Finish(int code)
    {
        EditorApplication.update -= CaptureUpdate;
        EditorApplication.isPlaying = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
