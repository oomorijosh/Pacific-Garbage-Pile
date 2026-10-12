using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>Shows Cyber Canoe's calibrated stereo output on a physical-size screen in a real headset.</summary>
[DisallowMultipleComponent]
public sealed class CyberCanoeHeadsetSimulator : MonoBehaviour
{
    [Header("Cyber Canoe Scene")]
    public Transform ccHub;
    public Transform ccHead;
    public Transform sourceScreen;
    public Camera leftContentCamera;
    public Camera rightContentCamera;
    public Canvas outputCanvas;
    public CC_CANVAS_SETUP outputLayout;

    [Header("Headset Room")]
    public Transform roomRoot;
    public Transform virtualScreen;
    public Transform trackingSpace;
    public Camera headsetCamera;
    public MeshRenderer virtualScreenRenderer;
    public Material screenMaterialTemplate;

    [Header("Campus Calibration")]
    [Tooltip("Distance from the screen plane to the viewer, in metres. Twenty feet is 6.096 metres.")]
    [Min(0.05f)] public float viewingDistanceMeters = 6.096f;
    [Tooltip("Viewer offset from the screen centre in metres: horizontal and vertical. Used for a wall calibration whose head position is off-centre.")]
    public Vector2 viewerOffsetMeters;

    [Header("Runtime")]
    [InspectorName("Per-Eye Texture Width")]
    [Tooltip("Pixels in each eye image. A 4080 x 1152 side-by-side feed contains two 2040 x 1152 images, each covering the full physical wall.")]
    [Min(256)] public int textureWidth = 2040;
    [InspectorName("Per-Eye Texture Height")]
    [Tooltip("Pixels in each eye image, independent of the physical screen aspect ratio.")]
    [Min(1)] public int textureHeight = 1152;
    [Tooltip("Preserve HDR emission and bloom in the exhibit's stereo content cameras.")]
    public bool useHDRContentTextures;
    public bool initializeXR = true;

    public string Status { get; private set; } = "Not running.";
    public bool IsViewerCalibrated { get; private set; }

    const float ScaleTolerance = 0.001f;
    static readonly int LeftTextureId = Shader.PropertyToID("_LeftTex");
    static readonly int RightTextureId = Shader.PropertyToID("_RightTex");
    readonly List<CameraState> cameraStates = new List<CameraState>();
    readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();

    Coroutine startup;
    RenderTexture leftTexture;
    RenderTexture rightTexture;
    Material screenMaterial;
    Material[] originalScreenMaterials;
    Vector3 originalSourceScale;
    Vector3 originalHeadPosition;
    Quaternion originalHeadRotation;
    Vector3 originalLeftEyePosition;
    Quaternion originalLeftEyeRotation;
    Vector3 originalRightEyePosition;
    Quaternion originalRightEyeRotation;
    CC_CAMERA_SETUP sourceCameraSetup;
    bool originalCameraSetupEnabled;
    Vector3 originalTrackingPosition;
    Quaternion originalTrackingRotation;
    Vector3 viewerBaseline;
    bool originalCanvasEnabled;
    bool originalLayoutEnabled;
    bool originalScreenEnabled;
    bool sceneConfigured;
    bool ready;
    bool recenterPending;
    bool failed;
    XRManagerSettings xrManager;
    XRLoader sessionLoader;
    bool initializedXR;
    bool startedXR;

    sealed class CameraState
    {
        public Camera camera;
        public bool enabled;
        public RenderTexture target;
        public StereoTargetEyeMask targetEye;
        public int cullingMask;
        public float depth;
        public UniversalAdditionalCameraData data;
        public bool allowXR;
        public CameraRenderType renderType;
        public bool addedData;
        public bool restoreMatrices;
        public Matrix4x4 projection;
        public Matrix4x4 worldToCamera;

        public CameraState(Camera camera, bool restoreMatrices)
        {
            this.camera = camera;
            this.restoreMatrices = restoreMatrices;
            if (restoreMatrices)
            {
                projection = camera.projectionMatrix;
                worldToCamera = camera.worldToCameraMatrix;
            }
            enabled = camera.enabled;
            target = camera.targetTexture;
            targetEye = camera.stereoTargetEye;
            cullingMask = camera.cullingMask;
            depth = camera.depth;
            if (camera.TryGetComponent(out data))
            {
                allowXR = data.allowXRRendering;
                renderType = data.renderType;
            }
        }

        public UniversalAdditionalCameraData EnsureData()
        {
            if (!data)
            {
                data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                addedData = true;
            }
            return data;
        }

        public void Restore()
        {
            if (!camera) return;
            camera.enabled = enabled;
            camera.targetTexture = target;
            camera.stereoTargetEye = targetEye;
            camera.cullingMask = cullingMask;
            camera.depth = depth;
            if (restoreMatrices)
            {
                camera.projectionMatrix = projection;
                camera.worldToCameraMatrix = worldToCamera;
            }
            if (!data) return;
            if (addedData)
                UnityEngine.Object.Destroy(data);
            else
            {
                data.allowXRRendering = allowXR;
                data.renderType = renderType;
            }
        }
    }

    void OnEnable()
    {
        if (!Application.isPlaying) return;
        failed = false;
        Status = "Starting headset screen simulation...";
        startup = StartCoroutine(StartSimulation());
    }

    IEnumerator StartSimulation()
    {
        // CC's Start methods apply its screen configuration and eye separation first.
        // XR Management also requires graphics initialization to have completed.
        yield return null;

        string error = ValidateSetup();
        if (error != null)
        {
            Fail(error);
            yield break;
        }

        try
        {
            ConfigureScene();
        }
        catch (Exception exception)
        {
            Fail("Could not prepare the virtual screen: " + exception.Message);
            yield break;
        }

        if (!TryStartXR(out error))
        {
            Fail(error);
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + 10f;
        while (!IsDisplayRunning() && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!IsDisplayRunning())
        {
            Fail("The XR display did not start. Connect the headset and start the active OpenXR runtime, then re-enable the simulator.");
            yield break;
        }

        headsetCamera.enabled = true;
        ready = true;
        recenterPending = true;
        Status = "Waiting for headset tracking...";
        startup = null;
    }

    string ValidateSetup()
    {
        if (!ccHub || !ccHead || !sourceScreen || !leftContentCamera || !rightContentCamera ||
            !roomRoot || !virtualScreen || !trackingSpace || !headsetCamera || !virtualScreenRenderer || !screenMaterialTemplate)
            return "Assign the Cyber Canoe cameras, screen, head, and headset-room references before entering Play mode.";
        if (leftContentCamera == rightContentCamera || leftContentCamera == headsetCamera || rightContentCamera == headsetCamera)
            return "The content cameras and headset camera must be three separate cameras.";
        if (headsetCamera.transform.parent != trackingSpace)
            return "The headset camera must be a direct child of Tracking Space.";
        if (roomRoot == transform || transform.IsChildOf(roomRoot))
            return "The simulator component must be outside its exclusively owned Room Root so it can restart after the room is hidden.";
        if (!virtualScreen.IsChildOf(roomRoot) || !trackingSpace.IsChildOf(roomRoot) ||
            !virtualScreenRenderer.transform.IsChildOf(roomRoot))
            return "Place the virtual screen, its renderer, and Tracking Space underneath Room Root.";
        if (roomRoot.IsChildOf(ccHub))
            return "The virtual room and Tracking Space must be outside CC_HUB so navigation cannot move the campus screen.";
        if (!IsUnitScale(ccHub.lossyScale) || !IsUnitScale(ccHead.lossyScale) ||
            !IsUnitScale(trackingSpace.lossyScale) || !IsUnitScale(headsetCamera.transform.lossyScale) ||
            (virtualScreen.parent && !IsUnitScale(virtualScreen.parent.lossyScale)))
            return "Use a scale of (1, 1, 1) on CC_HUB and the headset rig parents; one Unity unit must represent one metre.";
        if (headsetCamera.gameObject.layer < 8 || virtualScreenRenderer.gameObject.layer != headsetCamera.gameObject.layer)
            return "Put the headset camera and virtual screen on the same dedicated user layer (8-31).";
        if (!screenMaterialTemplate.shader || !screenMaterialTemplate.shader.isSupported ||
            !screenMaterialTemplate.HasProperty(LeftTextureId) || !screenMaterialTemplate.HasProperty(RightTextureId))
            return "Use the supported Cyber Canoe stereo-screen material with _LeftTex and _RightTex properties.";
        if (ScreenWidth(virtualScreen) < 0.01f || ScreenHeight(virtualScreen) < 0.01f ||
            ScreenWidth(sourceScreen) < 0.01f || ScreenHeight(sourceScreen) < 0.01f)
            return "Both screens must have positive physical width and height.";
        if (float.IsNaN(viewingDistanceMeters) || float.IsInfinity(viewingDistanceMeters) || viewingDistanceMeters < 0.05f)
            return "Set the viewing distance to at least 0.05 metres.";
        return null;
    }

    void ConfigureScene()
    {
        originalSourceScale = sourceScreen.localScale;
        originalHeadPosition = ccHead.localPosition;
        originalHeadRotation = ccHead.localRotation;
        originalLeftEyePosition = leftContentCamera.transform.localPosition;
        originalLeftEyeRotation = leftContentCamera.transform.localRotation;
        originalRightEyePosition = rightContentCamera.transform.localPosition;
        originalRightEyeRotation = rightContentCamera.transform.localRotation;
        sourceCameraSetup = ccHead.GetComponent<CC_CAMERA_SETUP>();
        if (sourceCameraSetup) originalCameraSetupEnabled = sourceCameraSetup.enabled;
        // Save the source matrices before changing the head or eye transforms.
        RememberCamera(leftContentCamera);
        RememberCamera(rightContentCamera);
        originalTrackingPosition = trackingSpace.localPosition;
        originalTrackingRotation = trackingSpace.localRotation;
        originalScreenMaterials = virtualScreenRenderer.sharedMaterials;
        originalScreenEnabled = virtualScreenRenderer.enabled;
        if (outputCanvas) originalCanvasEnabled = outputCanvas.enabled;
        if (outputLayout) originalLayoutEnabled = outputLayout.enabled;
        // Set before any allocations so failures restore the partially configured scene too.
        sceneConfigured = true;

        roomRoot.gameObject.SetActive(true);
        if (outputCanvas) outputCanvas.enabled = false;
        if (outputLayout) outputLayout.enabled = false;

        float width = ScreenWidth(virtualScreen);
        float height = ScreenHeight(virtualScreen);
        if (Mathf.Abs(width - ScreenWidth(sourceScreen)) > ScaleTolerance ||
            Mathf.Abs(height - ScreenHeight(sourceScreen)) > ScaleTolerance)
        {
            Vector3 scale = originalSourceScale;
            scale.x *= width / ScreenWidth(sourceScreen);
            scale.y *= height / ScreenHeight(sourceScreen);
            sourceScreen.localScale = scale;
        }

        CC_SETTINGS settings = sourceCameraSetup ? sourceCameraSetup.CCSettings : null;
        float separation = settings ? settings.eyeSeparation :
            Vector3.Distance(leftContentCamera.transform.position, rightContentCamera.transform.position);
        bool invertStereo = settings ? settings.invertStereo :
            Vector3.Dot(rightContentCamera.transform.position - leftContentCamera.transform.position, sourceScreen.right) < 0f;
        // Suspend the shared rig setup while the simulator owns the calibrated
        // eye positions so later setting changes cannot override this viewing point.
        if (sourceCameraSetup) sourceCameraSetup.enabled = false;
        Vector3 contentViewer = CalculateViewerPosition(sourceScreen, viewingDistanceMeters, viewerOffsetMeters);
        Quaternion facingScreen = Quaternion.LookRotation(sourceScreen.forward, sourceScreen.up);
        ccHead.SetPositionAndRotation(contentViewer, facingScreen);
        Vector3 eyeOffset = sourceScreen.right.normalized * (separation * 0.5f);
        if (invertStereo) eyeOffset = -eyeOffset;
        leftContentCamera.transform.SetPositionAndRotation(contentViewer - eyeOffset, facingScreen);
        rightContentCamera.transform.SetPositionAndRotation(contentViewer + eyeOffset, facingScreen);
        viewerBaseline = ccHub.InverseTransformPoint(ccHead.position);
        trackingSpace.localPosition = viewerBaseline;

        int pixelWidth = Mathf.Clamp(textureWidth, 256, Mathf.Max(256, SystemInfo.maxTextureSize));
        int pixelHeight = Mathf.Clamp(textureHeight, 1, Mathf.Max(1, SystemInfo.maxTextureSize));
        leftTexture = CreateTexture("Cyber Canoe Simulator Left", pixelWidth, pixelHeight, useHDRContentTextures);
        rightTexture = CreateTexture("Cyber Canoe Simulator Right", pixelWidth, pixelHeight, useHDRContentTextures);
        screenMaterial = new Material(screenMaterialTemplate) { name = "Cyber Canoe Simulator Screen (Runtime)" };
        screenMaterial.SetTexture(LeftTextureId, leftTexture);
        screenMaterial.SetTexture(RightTextureId, rightTexture);
        virtualScreenRenderer.sharedMaterial = screenMaterial;
        virtualScreenRenderer.enabled = true;

        foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
        {
            if (camera == leftContentCamera || camera == rightContentCamera || camera == headsetCamera) continue;
            if (camera.gameObject.scene != ccHub.gameObject.scene && camera.gameObject.scene != headsetCamera.gameObject.scene) continue;
            // Preserve texture-producing cameras, such as reflection cameras.
            if (camera.targetTexture) continue;
            RememberCamera(camera);
            camera.enabled = false;
        }

        int roomMask = 1 << headsetCamera.gameObject.layer;
        ConfigureContentCamera(leftContentCamera, leftTexture, roomMask, -2f);
        ConfigureContentCamera(rightContentCamera, rightTexture, roomMask, -1f);
        CameraState hmdState = RememberCamera(headsetCamera);
        headsetCamera.enabled = false;
        headsetCamera.targetTexture = null;
        headsetCamera.stereoTargetEye = StereoTargetEyeMask.Both;
        headsetCamera.cullingMask = roomMask;
        headsetCamera.depth = 0f;
        UniversalAdditionalCameraData hmdData = hmdState.EnsureData();
        hmdData.allowXRRendering = true;
        hmdData.renderType = CameraRenderType.Base;
    }

    void ConfigureContentCamera(Camera camera, RenderTexture target, int roomMask, float depth)
    {
        CameraState state = RememberCamera(camera);
        camera.targetTexture = target;
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.cullingMask &= ~roomMask;
        camera.depth = depth;
        UniversalAdditionalCameraData data = state.EnsureData();
        data.allowXRRendering = false;
        data.renderType = CameraRenderType.Base;
        camera.enabled = true;
        // CC_CAMERAOFFSET remains enabled and recomputes the full-wall off-axis
        // projection from the calibrated eye positions in each LateUpdate.
    }

    CameraState RememberCamera(Camera camera)
    {
        foreach (CameraState state in cameraStates)
            if (state.camera == camera) return state;
        CameraState result = new CameraState(camera, camera == leftContentCamera || camera == rightContentCamera);
        cameraStates.Add(result);
        return result;
    }

    static RenderTexture CreateTexture(string name, int width, int height, bool hdr = false)
    {
        RenderTexture texture = new RenderTexture(width, height, 24,
            hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
            antiAliasing = 1
        };
        if (texture.Create()) return texture;
        Destroy(texture);
        throw new InvalidOperationException("Could not allocate a stereo render texture.");
    }

    bool TryStartXR(out string error)
    {
        error = null;
        if (IsDisplayRunning()) return true;
        if (!initializeXR)
        {
            error = "No XR display is running. Enable Initialize XR or start XR from your application's existing XR setup.";
            return false;
        }

        XRGeneralSettings settings = XRGeneralSettings.Instance;
        xrManager = settings ? settings.Manager : null;
        if (!xrManager)
        {
            error = "No XR manager is configured. Enable the OpenXR provider for the desktop build target in XR Plug-in Management.";
            return false;
        }

        try
        {
            if (!xrManager.activeLoader)
            {
                xrManager.InitializeLoaderSync();
                initializedXR = xrManager.activeLoader != null;
            }
            sessionLoader = xrManager.activeLoader;
            if (!sessionLoader)
            {
                error = "OpenXR could not initialize. Connect the headset, start its OpenXR runtime, and check XR Plug-in Management validation.";
                return false;
            }
            if (!IsDisplayRunning())
            {
                startedXR = true;
                xrManager.StartSubsystems();
            }
            return true;
        }
        catch (Exception exception)
        {
            sessionLoader = xrManager.activeLoader;
            error = "XR startup failed: " + exception.Message;
            return false;
        }
    }

    bool IsDisplayRunning()
    {
        displays.Clear();
        SubsystemManager.GetSubsystems(displays);
        foreach (XRDisplaySubsystem display in displays)
            if (display.running) return true;
        return false;
    }

    void LateUpdate()
    {
        if (!ready || !recenterPending) return;
        InputDevice headset = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
        if (!headset.isValid || !headset.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked) return;
        // Native centre-eye values match the tracked-pose driver's bindings and
        // remain current even when its Transform has not updated this frame yet.
        if (!headset.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 initialPosition) &&
            !headset.TryGetFeatureValue(CommonUsages.devicePosition, out initialPosition)) return;
        if (!headset.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion initialRotation) &&
            !headset.TryGetFeatureValue(CommonUsages.deviceRotation, out initialRotation)) return;
        Vector3 forward = Vector3.ProjectOnPlane(initialRotation * Vector3.forward, Vector3.up);
        Quaternion initialYaw = forward.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(forward, Vector3.up)
            : Quaternion.Euler(0f, initialRotation.eulerAngles.y, 0f);
        Quaternion inverseYaw = Quaternion.Inverse(initialYaw);
        trackingSpace.localRotation = originalTrackingRotation * inverseYaw;
        trackingSpace.localPosition = viewerBaseline - trackingSpace.localRotation * initialPosition;
        recenterPending = false;
        IsViewerCalibrated = true;
        Status = "Running: headset viewer at the calibrated Cyber Canoe screen.";
    }

    /// <summary>Returns the centred viewing point in world coordinates, on the front side of the screen.</summary>
    public static Vector3 CalculateCenteredViewer(Transform screen, float distanceMeters) =>
        screen.position - screen.forward.normalized * Mathf.Max(0.05f, distanceMeters);

    /// <summary>Returns the calibrated viewer position, including offsets along the screen axes.</summary>
    public static Vector3 CalculateViewerPosition(Transform screen, float distanceMeters, Vector2 offsetMeters) =>
        CalculateCenteredViewer(screen, distanceMeters) +
        screen.right.normalized * offsetMeters.x + screen.up.normalized * offsetMeters.y;

    /// <summary>Places the current headset pose at the calibrated viewing point, facing the screen.</summary>
    public void RecenterViewer()
    {
        if (!Application.isPlaying || !ready) return;
        recenterPending = true;
        IsViewerCalibrated = false;
        Status = "Waiting for headset tracking to recenter...";
    }

    void Fail(string message)
    {
        failed = true;
        Status = message;
        Debug.LogError("Cyber Canoe headset simulator: " + message, this);
        enabled = false;
    }

    void OnDisable()
    {
        if (startup != null)
        {
            StopCoroutine(startup);
            startup = null;
        }
        ready = false;
        recenterPending = false;
        IsViewerCalibrated = false;
        if (!failed) Status = "Not running.";

        if (sceneConfigured)
        {
            if (outputCanvas) outputCanvas.enabled = originalCanvasEnabled;
            if (outputLayout) outputLayout.enabled = originalLayoutEnabled;
            if (sourceScreen) sourceScreen.localScale = originalSourceScale;
            if (ccHead)
            {
                ccHead.localPosition = originalHeadPosition;
                ccHead.localRotation = originalHeadRotation;
            }
            if (leftContentCamera)
            {
                leftContentCamera.transform.localPosition = originalLeftEyePosition;
                leftContentCamera.transform.localRotation = originalLeftEyeRotation;
            }
            if (rightContentCamera)
            {
                rightContentCamera.transform.localPosition = originalRightEyePosition;
                rightContentCamera.transform.localRotation = originalRightEyeRotation;
            }
            if (trackingSpace)
            {
                trackingSpace.localPosition = originalTrackingPosition;
                trackingSpace.localRotation = originalTrackingRotation;
            }
            if (virtualScreenRenderer)
            {
                virtualScreenRenderer.sharedMaterials = originalScreenMaterials;
                virtualScreenRenderer.enabled = originalScreenEnabled;
            }
        }
        foreach (CameraState state in cameraStates) state.Restore();
        cameraStates.Clear();
        if (sceneConfigured && sourceCameraSetup) sourceCameraSetup.enabled = originalCameraSetupEnabled;
        sourceCameraSetup = null;
        sceneConfigured = false;
        ReleaseTexture(ref leftTexture);
        ReleaseTexture(ref rightTexture);
        if (screenMaterial) Destroy(screenMaterial);
        screenMaterial = null;

        // This presentation belongs exclusively to the simulator. Hide it after
        // restoring source-camera masks so the room cannot enter normal CC output.
        if (roomRoot && roomRoot != transform && !transform.IsChildOf(roomRoot))
            roomRoot.gameObject.SetActive(false);

        // Do not shut down an XR session owned by another scene or subsystem.
        if (xrManager && xrManager.activeLoader == sessionLoader && sessionLoader)
        {
            if (initializedXR) xrManager.DeinitializeLoader();
            else if (startedXR) xrManager.StopSubsystems();
        }
        xrManager = null;
        sessionLoader = null;
        initializedXR = false;
        startedXR = false;
    }

    static void ReleaseTexture(ref RenderTexture texture)
    {
        if (!texture) return;
        texture.Release();
        Destroy(texture);
        texture = null;
    }

    static bool IsUnitScale(Vector3 scale) => (scale - Vector3.one).sqrMagnitude < ScaleTolerance * ScaleTolerance;
    static float ScreenWidth(Transform screen) => screen.TransformVector(Vector3.right).magnitude;
    static float ScreenHeight(Transform screen) => screen.TransformVector(Vector3.up).magnitude;
}
