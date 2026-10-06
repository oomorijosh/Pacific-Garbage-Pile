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

/// <summary>Adds the exhibit without changing Cyber Canoe's calibrated viewing rig.</summary>
public static class PacificExhibitIntegration
{
    const string MenuPath = "Tools/Pacific Exhibit/Add Room and Box to Current Scene";
    const string PrefabPath = "Assets/PacificExhibit/Prefabs/PacificExhibit.prefab";
    const string CyberCanoeSourceScene = "Assets/CC_ALL/CC_LAVA_LCD_WALL_SCENE.unity";
    const string CyberCanoeExhibitScene = "Assets/Scenes/PacificExhibit_CyberCanoe.unity";
    const string DemoPrefabGuid = "c8b81a1884780124a9d4dce2395c10f1";
    const string ExhibitName = "PacificExhibit";

    [MenuItem(MenuPath)]
    public static void AddToCurrentScene()
    {
        GameObject exhibit = Install(SceneManager.GetActiveScene());
        if (!exhibit) return;
        Selection.activeGameObject = exhibit;
        EditorGUIUtility.PingObject(exhibit);
    }

    [MenuItem("Tools/Pacific Exhibit/Match CyberCanoe Appearance")]
    public static void MatchAppearance() => AddToCurrentScene();

    [MenuItem(MenuPath, true)]
    static bool CanAddToCurrentScene() =>
        !EditorApplication.isPlayingOrWillChangePlaymode &&
        PrefabStageUtility.GetCurrentPrefabStage() == null &&
        SceneManager.GetActiveScene().IsValid();

    /// <summary>Creates a separate exhibit scene; the source scene and build list are untouched.</summary>
    public static void CreateCyberCanoeScene()
    {
        Scene scene = EditorSceneManager.OpenScene(CyberCanoeSourceScene, OpenSceneMode.Single);
        if (!Install(scene))
            throw new InvalidOperationException("The Pacific exhibit prefab could not be installed.");
        if (!EditorSceneManager.SaveScene(scene, CyberCanoeExhibitScene))
            throw new InvalidOperationException("Could not save " + CyberCanoeExhibitScene + ".");
    }

    static GameObject Install(Scene scene)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab)
        {
            Debug.LogError("Build the Pacific exhibit first. Its prefab is missing at " + PrefabPath + ".");
            return null;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Pacific exhibit");
        try
        {
            var renderSettings = Unsupported.GetSerializedAssetInterfaceSingleton("RenderSettings");
            if (renderSettings) Undo.RecordObject(renderSettings, "Set Pacific exhibit room lighting");
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.022f, 0.034f, 0.052f);
            RenderSettings.fog = false;

            GameObject exhibit = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == ExhibitName) { exhibit = root; break; }

            if (!exhibit)
            {
                exhibit = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                Undo.RegisterCreatedObjectUndo(exhibit, "Add Pacific exhibit");
                exhibit.name = ExhibitName;
                exhibit.transform.SetPositionAndRotation(new Vector3(0f, 0f, 11f), Quaternion.identity);
                exhibit.transform.localScale = Vector3.one * 0.7f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(exhibit.transform);
            }

            foreach (Transform child in exhibit.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.layer == 0) continue;
                Undo.RecordObject(child.gameObject, "Set Pacific exhibit content layer");
                child.gameObject.layer = 0;
                PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
            }

            Camera centerCamera = null;
            var eyes = new List<Camera>();
            RenderTexture leftTarget = CreateEyeTarget("Exhibit_Left_HDR");
            RenderTexture rightTarget = CreateEyeTarget("Exhibit_Right_HDR");
            foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                if (camera.gameObject.scene != scene ||
                    (camera.name != "CC_LEFT_CAMERA" && camera.name != "CC_RIGHT_CAMERA" &&
                     camera.name != "CenterCamera")) continue;
                Undo.RecordObject(camera, "Enable exhibit camera HDR");
                camera.allowHDR = true;
                if (camera.name == "CenterCamera") centerCamera = camera;
                else
                {
                    eyes.Add(camera);
                    camera.targetTexture = camera.name == "CC_LEFT_CAMERA" ? leftTarget : rightTarget;
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
                var data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (!data) data = Undo.AddComponent<UniversalAdditionalCameraData>(camera.gameObject);
                Undo.RecordObject(data, "Enable exhibit camera bloom");
                data.renderPostProcessing = true;
                data.allowXRRendering = false;
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                EditorUtility.SetDirty(camera);
                EditorUtility.SetDirty(data);
            }

            foreach (var setup in UnityEngine.Object.FindObjectsByType<CC_SETUP_RENDER_TEXTURES>(FindObjectsInactive.Include))
            {
                if (setup.gameObject.scene != scene) continue;
                Undo.RecordObject(setup, "Assign exhibit HDR eye textures");
                setup.cameraLeftRT = leftTarget;
                setup.cameraRightRT = rightTarget;
                if (setup.leftCameraImage)
                {
                    Undo.RecordObject(setup.leftCameraImage, "Assign left eye preview texture");
                    setup.leftCameraImage.texture = leftTarget;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(setup.leftCameraImage);
                }
                if (setup.rightCameraImage)
                {
                    Undo.RecordObject(setup.rightCameraImage, "Assign right eye preview texture");
                    setup.rightCameraImage.texture = rightTarget;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(setup.rightCameraImage);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(setup);
            }
            Canvas outputCanvas = null;
            foreach (var layout in UnityEngine.Object.FindObjectsByType<CC_CANVAS_SETUP>(FindObjectsInactive.Include))
            {
                if (layout.gameObject.scene != scene) continue;
                Undo.RecordObject(layout, "Assign exhibit stereo output textures");
                layout.leftRT = leftTarget;
                layout.rightRT = rightTarget;
                outputCanvas = layout.GetComponent<Canvas>();
                PrefabUtility.RecordPrefabInstancePropertyModifications(layout);
            }
            foreach (var simulator in UnityEngine.Object.FindObjectsByType<CyberCanoeHeadsetSimulator>(FindObjectsInactive.Include))
            {
                if (simulator.gameObject.scene != scene) continue;
                Undo.RecordObject(simulator, "Keep exhibit HDR in headset content");
                simulator.useHDRContentTextures = true;
                PrefabUtility.RecordPrefabInstancePropertyModifications(simulator);
            }

            var appearance = exhibit.GetComponent<PacificCyberCanoeAppearance>();
            if (!appearance) appearance = Undo.AddComponent<PacificCyberCanoeAppearance>(exhibit);
            Undo.RecordObject(appearance, "Configure CyberCanoe editor preview");
            appearance.stereoOutput = outputCanvas;
            if (outputCanvas) Undo.RecordObject(outputCanvas, "Hide stereo output placeholders while editing");
            appearance.RefreshPreview();
            if (outputCanvas) PrefabUtility.RecordPrefabInstancePropertyModifications(outputCanvas);

            var reflection = exhibit.GetComponent<PacificPlanarReflection>();
            if (!reflection) reflection = Undo.AddComponent<PacificPlanarReflection>(exhibit);
            Undo.RecordObject(reflection, "Add per-camera floor reflections");
            reflection.sourceCamera = centerCamera;
            reflection.additionalSourceCameras = eyes.ToArray();
            reflection.floorRenderer = exhibit.transform.Find("Room/Floor").GetComponent<Renderer>();
            reflection.textureWidth = 768;
            reflection.reflectionStrength = 0.62f;
            reflection.enabled = true;

            // Preserve the same illumination when the exhibit is scaled for the wall.
            float scale = exhibit.transform.lossyScale.x;
            foreach (Light light in exhibit.GetComponentsInChildren<Light>(true))
            {
                Light template = PrefabUtility.GetCorrespondingObjectFromSource(light);
                if (!template) continue;
                Undo.RecordObject(light, "Match scaled exhibit lighting");
                light.intensity = template.intensity * scale * scale;
                light.range = template.range * scale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Directional Light" && root.TryGetComponent<Light>(out var originalSun))
                {
                    Undo.RecordObject(originalSun, "Use exhibit room lighting");
                    originalSun.enabled = false;
                }
                if (root.name != "Cube" || !PrefabUtility.IsPartOfPrefabInstance(root)) continue;
                string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (AssetDatabase.AssetPathToGUID(sourcePath) != DemoPrefabGuid) continue;
                Undo.RecordObject(root, "Hide Cyber Canoe demonstration cube");
                root.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            }

            EditorUtility.SetDirty(exhibit);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);
            return exhibit;
        }
        catch
        {
            Undo.RevertAllDownToGroup(undoGroup);
            throw;
        }
    }

    static RenderTexture CreateEyeTarget(string name)
    {
        const string directory = "Assets/PacificExhibit/RenderTextures";
        Directory.CreateDirectory(directory);
        AssetDatabase.Refresh();
        string path = directory + "/" + name + ".renderTexture";
        RenderTexture texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (!texture)
        {
            texture = new RenderTexture(2040, 1152, 24, RenderTextureFormat.DefaultHDR)
            {
                name = name,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            AssetDatabase.CreateAsset(texture, path);
        }
        return texture;
    }
}
