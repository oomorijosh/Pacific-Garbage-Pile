using System;
using System.Collections.Generic;
using System.IO;
using PacificExhibit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Validates and captures Plastic_Type without starting XR or saving preview changes.</summary>
public static class PlasticTypePresentationPreview
{
    const string ScenePath = "Assets/Scenes/Plastic_Type.unity";
    const string PresentationName = "Pacific Exhibit Presentation";
    static readonly string[] ContentCameraNames = { "CenterCamera", "CC_LEFT_CAMERA", "CC_RIGHT_CAMERA" };
    static readonly List<Canvas> DisabledCanvases = new List<Canvas>();
    static PacificPlanarReflection reflection;
    static Camera[] originalSources;
    static Camera previewCamera;
    static Camera[] captureCameras;
    static Transform leftEye;
    static Transform rightEye;
    static Vector3 originalLeftEyePosition;
    static Vector3 originalRightEyePosition;
    static int warmupFrames;
    static int captureIndex;
    static double deadline;
    static bool running;
    static bool sourcesAugmented;

    [MenuItem("Tools/Pacific Exhibit/Capture Plastic Type Presentation")]
    public static void Capture()
    {
        if (running) throw new InvalidOperationException("A Plastic Type preview is already running.");
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before capturing Plastic Type.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }

            var exhibit = FindExhibit();
            Transform presentation = RequiredChild(exhibit.transform, PresentationName);
            ValidateScene(exhibit, presentation);
            ValidateShaders(presentation);
            PrepareStereoEyes();
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                if (canvas.gameObject.scene != scene || !canvas.enabled || canvas.renderMode == RenderMode.WorldSpace) continue;
                DisabledCanvases.Add(canvas);
                canvas.enabled = false;
            }

            previewCamera = CreatePreviewCamera(exhibit, presentation);
            originalSources = reflection.additionalSourceCameras;
            var sources = new List<Camera>(originalSources ?? Array.Empty<Camera>()) { previewCamera };
            reflection.additionalSourceCameras = sources.ToArray();
            sourcesAugmented = true;
            captureCameras = new[] { previewCamera, FindCamera(ContentCameraNames[0]),
                FindCamera(ContentCameraNames[1]), FindCamera(ContentCameraNames[2]) };
            warmupFrames = 0;
            captureIndex = 0;
            deadline = EditorApplication.timeSinceStartup + 180;
            running = true;
            EditorApplication.update += CaptureUpdate;
            EditorApplication.QueuePlayerLoopUpdate();
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
            Debug.LogError("PLASTIC_PRESENTATION: Capture timed out.");
            Finish(1);
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || ++warmupFrames < 24)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            return;
        }
        try
        {
            Camera camera = captureCameras[captureIndex];
            string filename = captureIndex == 0 ? "Plastic_Type-presentation-preview.png" :
                "Plastic_Type-" + new[] { "center", "left", "right" }[captureIndex - 1] + ".png";
            CaptureCamera(camera, filename, captureIndex == 0);
            captureIndex++;
            if (captureIndex == captureCameras.Length)
            {
                ValidateShaders(RequiredChild(FindExhibit().transform, PresentationName));
                Debug.Log("PLASTIC_PRESENTATION: Passed. Plinth, cyan glow, HDR bloom, and nonempty per-camera floor reflections validated. Four PNGs saved in Artifacts; scene was not saved.");
                Finish(0);
            }
            else EditorApplication.QueuePlayerLoopUpdate();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    static void ValidateScene(GPGPLayeredCubeVisualization exhibit, Transform presentation)
    {
        if (!exhibit.bakedIntoScene || !exhibit.transform.Find("WaterCube - one continuous volume"))
            throw new InvalidOperationException("Plastic Type must contain its baked water and debris sculpture.");
        RequiredChild(presentation, "Plinth");
        RequiredChild(presentation, "Glowing Frame");
        var volume = presentation.GetComponentInChildren<Volume>(true);
        if (!volume || !volume.enabled || !volume.isGlobal || !volume.sharedProfile ||
            !volume.sharedProfile.TryGet(out Bloom bloom) || !bloom.active || bloom.intensity.value < 0.5f)
            throw new InvalidOperationException("The presentation needs an active global bloom volume.");
        reflection = presentation.GetComponentInChildren<PacificPlanarReflection>(true);
        if (!reflection || !reflection.isActiveAndEnabled || !reflection.floorRenderer ||
            !reflection.floorRenderer.enabled || reflection.reflectionStrength <= 0f)
            throw new InvalidOperationException("The presentation needs an enabled reflective floor.");
        if (reflection.sourceCamera != FindCamera(ContentCameraNames[0]))
            throw new InvalidOperationException("The floor reflection source must be CenterCamera.");
        for (int i = 0; i < ContentCameraNames.Length; i++)
        {
            Camera camera = FindCamera(ContentCameraNames[i]);
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            if (!camera.allowHDR || !data || !data.renderPostProcessing)
                throw new InvalidOperationException(camera.name + " needs HDR and URP post-processing.");
            if (i > 0 && (reflection.additionalSourceCameras == null ||
                Array.IndexOf(reflection.additionalSourceCameras, camera) < 0))
                throw new InvalidOperationException(camera.name + " is not registered for its floor reflection.");
        }
    }

    static Camera CreatePreviewCamera(GPGPLayeredCubeVisualization exhibit, Transform presentation)
    {
        Vector3 half = exhibit.cubeSize * 0.5f;
        var bounds = new Bounds(exhibit.transform.position, Vector3.zero);
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    bounds.Encapsulate(exhibit.transform.TransformPoint(Vector3.Scale(half, new Vector3(x, y, z))));
        foreach (Renderer renderer in RequiredChild(presentation, "Plinth").GetComponentsInChildren<Renderer>())
            bounds.Encapsulate(renderer.bounds);

        var previewObject = new GameObject("Plastic Type presentation preview") { hideFlags = HideFlags.HideAndDontSave };
        Camera camera = previewObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.allowHDR = true;
        camera.allowMSAA = false;
        camera.useOcclusionCulling = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.006f, 0.014f, 0.022f, 1f);
        camera.cullingMask = FindCamera(ContentCameraNames[0]).cullingMask;
        camera.fieldOfView = 45f;
        camera.aspect = 1.28f;
        float height = Mathf.Max(1f, bounds.size.y);
        // Keep the earlier, unobstructed viewing position and widen the lens
        // to include the top rim, rather than backing through the gallery wall.
        float distance = Mathf.Max(bounds.extents.magnitude * 2.6f,
            height / (2f * Mathf.Tan(38f * 0.5f * Mathf.Deg2Rad)) * 1.5f);
        Vector3 target = bounds.center - Vector3.up * height * 0.12f;
        Vector3 direction = new Vector3(0.30f, 0.20f, -1f).normalized;
        camera.transform.position = target + direction * distance;
        camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
        camera.nearClipPlane = Mathf.Max(0.05f, height * 0.003f);
        camera.farClipPlane = distance + bounds.extents.magnitude * 8f;
        var data = camera.GetUniversalAdditionalCameraData();
        var centerData = FindCamera(ContentCameraNames[0]).GetUniversalAdditionalCameraData();
        data.allowXRRendering = false;
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = true;
        data.renderShadows = true;
        data.volumeLayerMask = centerData.volumeLayerMask;
        data.volumeTrigger = camera.transform;
        return camera;
    }

    static void PrepareStereoEyes()
    {
        var setup = FindCamera("CC_LEFT_CAMERA").GetComponentInParent<CC_CAMERA_SETUP>();
        if (!setup || !setup.CCSettings || !setup.leftCamera || !setup.rightCamera)
            throw new InvalidOperationException("The Cyber Canoe rig needs its stereo camera setup and settings.");
        leftEye = setup.leftCamera.transform;
        rightEye = setup.rightCamera.transform;
        originalLeftEyePosition = leftEye.localPosition;
        originalRightEyePosition = rightEye.localPosition;
        float halfSeparation = setup.CCSettings.eyeSeparation * 0.5f;
        if (setup.CCSettings.invertStereo) halfSeparation = -halfSeparation;
        Vector3 offset = new Vector3(halfSeparation, 0f, 0f);
        // Mirror CC_CAMERA_SETUP.Start's authored-position offsets without
        // entering Play mode or initializing its private runtime state.
        leftEye.localPosition = originalLeftEyePosition - offset;
        rightEye.localPosition = originalRightEyePosition + offset;
        Debug.Log("PLASTIC_PRESENTATION: Temporary stereo eye positions " + leftEye.position.ToString("F4") +
            " and " + rightEye.position.ToString("F4") + "; separation " +
            Vector3.Distance(leftEye.position, rightEye.position).ToString("F4") + " m.");
    }

    static void CaptureCamera(Camera camera, string filename, bool isolatedPreview)
    {
        int width = isolatedPreview ? 1280 : 1600;
        float aspect = camera.targetTexture ? (float)camera.targetTexture.width / camera.targetTexture.height : camera.aspect;
        int height = isolatedPreview ? 1000 : Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(0.1f, aspect)), 400, 1200);
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
        {
            name = "Plastic Type preview HDR",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        RenderTexture previousTarget = camera.targetTexture;
        Rect previousRect = camera.rect;
        Matrix4x4 previousProjection = camera.projectionMatrix;
        Matrix4x4 previousView = camera.worldToCameraMatrix;
        bool projectionRecomputed = false;
        try
        {
            if (!target.Create()) throw new InvalidOperationException("Could not allocate the capture texture.");
            camera.targetTexture = target;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            if (!isolatedPreview) projectionRecomputed = ApplyCalibratedProjection(camera);
            LogCameraDiagnostics(camera);
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                throw new InvalidOperationException("URP cannot render the preview camera.");
            // The first request warms camera-dependent shader and post-processing resources.
            for (int pass = 0; pass < 2; pass++)
            {
                reflection.RenderReflectionFor(camera);
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            SavePng(target, filename);
            Debug.Log("PLASTIC_PRESENTATION: Captured " + filename + " (" + width + " x " + height + ").");
            ValidateRenderedReflection(camera);
            Bloom bloom = VolumeManager.instance.stack.GetComponent<Bloom>();
            if (bloom == null || !bloom.active || bloom.intensity.value < 0.5f)
                throw new InvalidOperationException(camera.name + " did not apply the presentation bloom volume.");
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.rect = previousRect;
            if (projectionRecomputed)
            {
                camera.projectionMatrix = previousProjection;
                camera.worldToCameraMatrix = previousView;
            }
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    static bool ApplyCalibratedProjection(Camera camera)
    {
        // CC_CAMERAOFFSET normally prepares this in editor LateUpdate. Batch captures
        // must also work before that private lifecycle has initialized its Camera field.
        var offset = camera.GetComponent<CC_CAMERAOFFSET>();
        if (!offset || !offset.GetProjectionScreen()) return false;
        Transform screen = offset.GetProjectionScreen().transform;
        Vector3 lowerLeft = screen.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 lowerRight = screen.TransformPoint(new Vector3(0.5f, -0.5f, 0f));
        Vector3 upperLeft = screen.TransformPoint(new Vector3(-0.5f, 0.5f, 0f));
        Vector3 right = (lowerRight - lowerLeft).normalized;
        Vector3 up = (upperLeft - lowerLeft).normalized;
        Vector3 normal = -Vector3.Cross(right, up).normalized;
        Vector3 eye = camera.transform.position;
        Vector3 eyeToLowerLeft = lowerLeft - eye;
        float distance = -Vector3.Dot(eyeToLowerLeft, normal);
        if (distance <= 0.0001f)
            throw new InvalidOperationException(camera.name + " is not in front of its calibrated projection screen.");
        float near = camera.nearClipPlane;
        float left = Vector3.Dot(right, eyeToLowerLeft) * near / distance;
        float rightEdge = Vector3.Dot(right, lowerRight - eye) * near / distance;
        float bottom = Vector3.Dot(up, eyeToLowerLeft) * near / distance;
        float top = Vector3.Dot(up, upperLeft - eye) * near / distance;
        camera.projectionMatrix = Matrix4x4.Frustum(left, rightEdge, bottom, top, near, camera.farClipPlane);
        Matrix4x4 view = Matrix4x4.identity;
        view.SetRow(0, new Vector4(right.x, right.y, right.z, -Vector3.Dot(right, eye)));
        view.SetRow(1, new Vector4(up.x, up.y, up.z, -Vector3.Dot(up, eye)));
        view.SetRow(2, new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, eye)));
        camera.worldToCameraMatrix = view;
        Debug.Log("PLASTIC_PRESENTATION: " + camera.name + " calibrated screen position " + screen.position.ToString("F3") +
            "; scale " + screen.lossyScale.ToString("F3") + "; viewing distance " + distance.ToString("F3") + ".");
        return true;
    }

    static void LogCameraDiagnostics(Camera camera)
    {
        Matrix4x4 projection = camera.projectionMatrix;
        Renderer floor = reflection.floorRenderer;
        bool inFrustum = GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), floor.bounds);
        Debug.Log("PLASTIC_PRESENTATION: " + camera.name + " position " + camera.transform.position.ToString("F3") +
            "; rotation " + camera.transform.eulerAngles.ToString("F3") + "; FOV " + camera.fieldOfView.ToString("F3") +
            "; aspect " + camera.aspect.ToString("F3") + "; projection (m00, m11, m02, m12) " +
            new Vector4(projection.m00, projection.m11, projection.m02, projection.m12).ToString("F3") +
            "; floor enabled " + floor.enabled + "; floor in frustum " + inFrustum +
            "; floor layer visible " + ((camera.cullingMask & (1 << floor.gameObject.layer)) != 0) +
            "; floor bounds center " + floor.bounds.center.ToString("F3") + "; size " + floor.bounds.size.ToString("F3") + ".");
    }

    static void ValidateRenderedReflection(Camera source)
    {
        var properties = new MaterialPropertyBlock();
        reflection.floorRenderer.GetPropertyBlock(properties);
        var texture = properties.GetTexture("_ReflectionTex") as RenderTexture;
        if (properties.GetFloat("_ReflectionEnabled") < 0.5f || !texture || !texture.IsCreated())
            throw new InvalidOperationException(source.name + " did not bind a valid floor reflection.");
        Camera helper = null;
        foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            if (camera.name == "Pacific floor reflection - " + source.name) { helper = camera; break; }
        if (!helper || helper.targetTexture != texture)
            throw new InvalidOperationException(source.name + " did not bind its own reflection helper texture.");
        SavePng(texture, "Plastic_Type-reflection-" + source.name + ".png");

        RenderTexture previousActive = RenderTexture.active;
        Texture2D pixels = null;
        var sample = new RenderTexture(128, 128, 0, RenderTextureFormat.DefaultHDR) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            sample.Create();
            Graphics.Blit(texture, sample);
            RenderTexture.active = sample;
            pixels = new Texture2D(sample.width, sample.height, TextureFormat.RGBAFloat, false, true);
            pixels.ReadPixels(new Rect(0, 0, sample.width, sample.height), 0, 0);
            pixels.Apply();
            float maximum = 0f;
            int lit = 0;
            foreach (Color pixel in pixels.GetPixels())
            {
                float brightness = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                maximum = Mathf.Max(maximum, brightness);
                if (brightness > 0.025f) lit++;
            }
            if (maximum < 0.025f || lit < 8)
                throw new InvalidOperationException(source.name + " produced an empty reflection (maximum " + maximum + ", lit pixels " + lit + ").");
            Debug.Log("PLASTIC_PRESENTATION: " + source.name + " reflection maximum " + maximum.ToString("F3") + "; lit samples " + lit + ".");
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (pixels) Object.DestroyImmediate(pixels);
            sample.Release();
            Object.DestroyImmediate(sample);
        }
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

    static void ValidateShaders(Transform presentation)
    {
        var compiledMaterials = new HashSet<Material>();
        foreach (Renderer renderer in presentation.GetComponentsInChildren<Renderer>(true))
            foreach (Material material in renderer.sharedMaterials)
            {
                if (!material || !material.shader || !material.shader.name.StartsWith("PacificExhibit/", StringComparison.Ordinal) ||
                    !compiledMaterials.Add(material)) continue;
                for (int pass = 0; pass < material.passCount; pass++) ShaderUtil.CompilePass(material, pass, true);
            }
        foreach (string path in new[] { "Assets/PacificExhibit/Shaders/Glow.shader", "Assets/PacificExhibit/Shaders/Floor.shader" })
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (!shader) throw new InvalidOperationException("Missing presentation shader: " + path);
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new InvalidOperationException(path + ": " + message.message);
            if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException(path + " failed to compile.");
        }
    }

    static GPGPLayeredCubeVisualization FindExhibit()
    {
        foreach (var exhibit in Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include))
            if (exhibit.gameObject.scene.path == ScenePath) return exhibit;
        throw new InvalidOperationException("Plastic_Type is missing its layered cube sculpture.");
    }

    static Camera FindCamera(string name)
    {
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            if (camera.gameObject.scene.path == ScenePath && camera.name == name) return camera;
        throw new InvalidOperationException("Plastic_Type is missing " + name + ".");
    }

    static Transform RequiredChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (!child) throw new InvalidOperationException(parent.name + " is missing " + name + ".");
        return child;
    }

    static void Finish(int code)
    {
        EditorApplication.update -= CaptureUpdate;
        foreach (Canvas canvas in DisabledCanvases)
            if (canvas) canvas.enabled = true;
        DisabledCanvases.Clear();
        if (leftEye) leftEye.localPosition = originalLeftEyePosition;
        if (rightEye) rightEye.localPosition = originalRightEyePosition;
        leftEye = null;
        rightEye = null;
        if (reflection && sourcesAugmented) reflection.additionalSourceCameras = originalSources;
        if (previewCamera) Object.DestroyImmediate(previewCamera.gameObject);
        previewCamera = null;
        reflection = null;
        originalSources = null;
        captureCameras = null;
        running = false;
        sourcesAugmented = false;
        if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
