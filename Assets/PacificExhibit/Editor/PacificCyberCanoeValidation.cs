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

/// <summary>Captures actual CyberCanoe cameras in an isolated batch validation project.</summary>
public static class PacificCyberCanoeValidation
{
    const string ScenePath = "Assets/Scenes/PacificExhibit_CyberCanoe.unity";
    static int frames;
    static double deadline;

    public static void RepairAndCapture()
    {
        try
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            Camera center = FindCamera("CenterCamera");
            Vector3 position = center.transform.position;
            Quaternion rotation = center.transform.rotation;
            Transform screen = Object.FindAnyObjectByType<CC_CAMERAOFFSET>().projectionScreen.transform;
            Vector3 screenPosition = screen.position;
            Vector3 screenScale = screen.lossyScale;
            PacificExhibitIntegration.MatchAppearance();
            if (center.transform.position != position || center.transform.rotation != rotation ||
                screen.position != screenPosition || screen.lossyScale != screenScale)
                throw new InvalidOperationException("Appearance repair changed the calibrated rig.");
            ValidateSavedScene();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            // Validation needs no connected headset. These changes are not saved to the scene.
            foreach (var simulator in Object.FindObjectsByType<CyberCanoeHeadsetSimulator>(FindObjectsInactive.Include))
                simulator.enabled = false;
            if (XRGeneralSettings.Instance) XRGeneralSettings.Instance.InitManagerOnStart = false;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            frames = 0;
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update += CaptureUpdate;
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void CaptureUpdate()
    {
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Debug.LogError("PACIFIC_CC: Preview timed out.");
            Finish(1);
            return;
        }
        if (!EditorApplication.isPlaying || ++frames < 35) return;
        EditorApplication.update -= CaptureUpdate;
        try
        {
            var appearance = Object.FindAnyObjectByType<PacificCyberCanoeAppearance>();
            if (!appearance.stereoOutput.enabled)
                throw new InvalidOperationException("Stereo output did not enable on entering Play mode.");
            bool canvasEnabled = appearance.stereoOutput.enabled;
            appearance.stereoOutput.enabled = false;
            try
            {
                Capture(FindCamera("CenterCamera"), "PacificExhibit-CyberCanoe-preview.png");
                Capture(FindCamera("CC_LEFT_CAMERA"), "PacificExhibit-CyberCanoe-left.png");
                Capture(FindCamera("CC_RIGHT_CAMERA"), "PacificExhibit-CyberCanoe-right.png");
            }
            finally { appearance.stereoOutput.enabled = canvasEnabled; }
            ValidateShaders();
            Debug.Log("PACIFIC_CC: Passed. Rig preserved; center/left/right HDR, bloom, reflections captured; canvas transitions verified.");
            Finish(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    static void ValidateSavedScene()
    {
        var appearance = Object.FindAnyObjectByType<PacificCyberCanoeAppearance>();
        if (!appearance || !appearance.stereoOutput || appearance.stereoOutput.enabled)
            throw new InvalidOperationException("Stereo placeholders must be hidden in the editor.");
        foreach (string name in new[] { "CenterCamera", "CC_LEFT_CAMERA", "CC_RIGHT_CAMERA" })
        {
            Camera camera = FindCamera(name);
            if (!camera.allowHDR || !camera.GetUniversalAdditionalCameraData().renderPostProcessing)
                throw new InvalidOperationException(name + " is missing HDR or post-processing.");
            if (name != "CenterCamera" && (!camera.targetTexture || camera.targetTexture.format != RenderTextureFormat.ARGBHalf))
                throw new InvalidOperationException(name + " needs an HDR eye texture.");
        }
        Light sun = GameObject.Find("Directional Light").GetComponent<Light>();
        if (sun.enabled) throw new InvalidOperationException("The legacy sun must not wash out the room.");
        var reflection = Object.FindAnyObjectByType<PacificPlanarReflection>();
        if (!reflection || reflection.additionalSourceCameras.Length != 2)
            throw new InvalidOperationException("Both eyes require floor reflection sources.");
    }

    static void Capture(Camera camera, string filename)
    {
        var target = new RenderTexture(1600, 600, 24, RenderTextureFormat.DefaultHDR);
        target.Create();
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        Texture2D linear = null, encoded = null;
        try
        {
            camera.targetTexture = target;
            var reflection = Object.FindAnyObjectByType<PacificPlanarReflection>();
            reflection.RenderReflectionFor(camera);
            var request = new RenderPipeline.StandardRequest { destination = target };
            RenderPipeline.SubmitRenderRequest(camera, request);
            var floorProperties = new MaterialPropertyBlock();
            reflection.floorRenderer.GetPropertyBlock(floorProperties);
            if (floorProperties.GetFloat("_ReflectionEnabled") < 0.5f)
                throw new InvalidOperationException(camera.name + " did not bind its floor reflection.");
            if (VolumeManager.instance.stack.GetComponent<Bloom>().intensity.value < 0.5f)
                throw new InvalidOperationException(camera.name + " did not apply the exhibit bloom volume.");
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
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            target.Release();
            Object.DestroyImmediate(target);
            if (linear) Object.DestroyImmediate(linear);
            if (encoded) Object.DestroyImmediate(encoded);
        }
    }

    static Camera FindCamera(string name)
    {
        foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            if (camera.name == name) return camera;
        throw new InvalidOperationException("Missing CyberCanoe camera: " + name);
    }

    static void ValidateShaders()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/PacificExhibit/Shaders" }))
            foreach (var message in ShaderUtil.GetShaderMessages(AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid))))
                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                    throw new InvalidOperationException(message.message);
    }

    static void Finish(int code)
    {
        EditorApplication.update -= CaptureUpdate;
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(code);
    }
}
