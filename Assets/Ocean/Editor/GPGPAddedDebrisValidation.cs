#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GPGPUI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Imports, checks and captures the added debris only in the isolated validation project.</summary>
public static class GPGPAddedDebrisValidation
{
    const string ScenePath = "Assets/Scenes/Plastic_Type.unity";
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] Keys = { "Plastic_Canister", "Plastic_Garden_Chair", "Truck_Tire", "Tire_Pile" };
    static readonly string[] Names = { "Plastic canister", "Plastic garden chair", "Truck tire", "Tire pile" };
    static readonly int[] Categories = { 2, 3, 3, 3 };
    static readonly StringBuilder Report = new StringBuilder();
    static string project, reportPath;
    static Scene scene;
    static GPGPLayeredCubeVisualization exhibit;
    static GPGPDebrisBands bands;
    static GPGPExhibitUI authoredUI, probe;
    static GameObject[] added;
    static Dictionary<Object, string> protectedState;
    static Camera[] eyes;
    static Vector3[] eyePositions;
    static Quaternion[] eyeRotations;
    static Matrix4x4[] projections, views;
    static bool authoredUIActive;
    static int warmup, captureIndex;
    static double deadline;
    static double waterLuminance = double.NaN;

    public static void BuildAndCapture()
    {
        try
        {
            project = Directory.GetParent(Application.dataPath).FullName;
            Require(project.Replace('\\', '/').EndsWith("/.utmp/pacific-exhibit-validation", StringComparison.OrdinalIgnoreCase),
                "Run this scene-writing audit only in .utmp/pacific-exhibit-validation.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Validation must stay outside Play mode and XR.");
            Directory.CreateDirectory(Path.Combine(project, "Artifacts"));
            reportPath = Path.Combine(project, "Artifacts", "Added-debris-check.txt");
            Report.Clear();
            Line("Added debris import, preservation and rendering validation; Unity " + Application.unityVersion + ".");
            string diskBefore = Hash(Path.Combine(project, ScenePath));
            scene = EditorSceneManager.OpenScene(ScenePath);
            exhibit = Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include)
                .Single(value => value.gameObject.scene == scene);
            bands = exhibit.GetComponentsInChildren<GPGPDebrisBands>(true).Single();
            authoredUI = Object.FindObjectsByType<GPGPExhibitUI>(FindObjectsInactive.Include)
                .Single(value => value.gameObject.scene == scene);
            protectedState = SnapshotProtected();
            string exhibitSettings = SettingsFingerprint(exhibit, "debrisModelGroups");
            string bandsSettings = SettingsFingerprint(bands, "modelGroups", "placements");
            GameObject[][] previousPools = exhibit.debrisModelGroups.Select(group => group.models.ToArray()).ToArray();
            Bounds[] previousBandBounds = Enumerable.Range(0, 4).Select(bands.GetDestinationBounds).ToArray();
            int[] previousCounts = Counts();
            string[][] populatedPools = null;
            for (int pass = 1; pass <= 2; pass++)
            {
                GPGPDebrisModelAuthoring.Populate(exhibit);
                Require(SettingsFingerprint(exhibit, "debrisModelGroups") == exhibitSettings, "Population changed exhibit dimensions, materials or settings.");
                Require(SettingsFingerprint(bands, "modelGroups", "placements") == bandsSettings, "Population changed density, mixing, seed, water or motion settings.");
                AssertProtected("Population pass " + pass);
                for (int category = 0; category < 4; category++)
                {
                    GameObject[] pool = exhibit.debrisModelGroups[category].models;
                    Require(pool.All(model => model) && pool.Length == pool.Distinct().Count(), "A category pool contains missing or duplicate references.");
                    Require(previousPools[category].All(pool.Contains), "An existing manually assigned pool model was discarded.");
                    Require(pool.SequenceEqual(bands.modelGroups[category].models), "Exhibit and placed-piece pools disagree.");
                    Require(bands.GetDestinationBounds(category) == previousBandBounds[category], "A mass-share band changed height or bounds.");
                }
                string[][] current = PoolPaths();
                if (populatedPools == null) populatedPools = current;
                else Require(Enumerable.Range(0, 4).All(category => current[category].SequenceEqual(populatedPools[category])),
                    "Repeated population changed or duplicated pool membership.");
            }
            Require(Counts().SequenceEqual(previousCounts), "Adding these models unexpectedly changed the existing band piece counts.");
            Line("Two population passes: old pool assignments retained, new entries deduplicated; density, seed, mixing, dimensions, motion and all four mass-share bounds unchanged.");
            Line("Piece counts: " + string.Join(",", Counts()) + "; pool sizes: " + string.Join(",", exhibit.debrisModelGroups.Select(group => group.models.Length)) + ".");
            added = Enumerable.Range(0, Keys.Length).Select(index => RequiredPrefab(index)).ToArray();
            ValidateAddedModels();
            ValidateUI();
            AssertProtected("After UI and prefab checks");
            Require(Hash(Path.Combine(project, ScenePath)) == diskBefore, "The authoring helper unexpectedly saved the scene.");
            Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save the isolated validated scene.");
            AssetDatabase.SaveAssets();
            scene = EditorSceneManager.OpenScene(ScenePath);
            exhibit = Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include).Single(value => value.gameObject.scene == scene);
            bands = exhibit.GetComponentsInChildren<GPGPDebrisBands>(true).Single();
            authoredUI = Object.FindObjectsByType<GPGPExhibitUI>(FindObjectsInactive.Include).Single(value => value.gameObject.scene == scene);
            Require(Enumerable.Range(0, 4).All(category => PoolPaths()[category].SequenceEqual(populatedPools[category])), "Saving/reopening changed model pool references.");
            ValidateAddedModels();
            Line("Save/reopen: persistent prefabs, meshes, materials and normal import settings retained. Only the isolated scene was saved.");
            protectedState = SnapshotProtected();
            PrepareCaptures();
            warmup = captureIndex = 0;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += CaptureAfterWarmup;
            EditorApplication.QueuePlayerLoopUpdate();
            WriteReport();
        }
        catch (Exception exception) { Fail(exception); }
    }

    /// <summary>Checks and recaptures an already validated scene without rebuilding or saving it.</summary>
    public static void CaptureExistingAndCheckUI()
    {
        try
        {
            project = Directory.GetParent(Application.dataPath).FullName;
            Require(project.Replace('\\', '/').EndsWith("/.utmp/pacific-exhibit-validation", StringComparison.OrdinalIgnoreCase),
                "Run the preview check only in the isolated validation project.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Preview checks must stay outside Play mode and XR.");
            Directory.CreateDirectory(Path.Combine(project, "Artifacts"));
            reportPath = Path.Combine(project, "Artifacts", "Added-debris-preview-check.txt");
            Report.Clear();
            Line("Saved-scene debris and preview validation; no rebuild, scene save, Play mode or XR.");
            string diskBefore = Hash(Path.Combine(project, ScenePath));
            scene = EditorSceneManager.OpenScene(ScenePath);
            exhibit = Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include).Single(value => value.gameObject.scene == scene);
            bands = exhibit.GetComponentsInChildren<GPGPDebrisBands>(true).Single();
            authoredUI = Object.FindObjectsByType<GPGPExhibitUI>(FindObjectsInactive.Include).Single(value => value.gameObject.scene == scene);
            added = Enumerable.Range(0, Keys.Length).Select(RequiredPrefab).ToArray();
            protectedState = SnapshotProtected();
            ValidateAddedModels();
            ValidateUI();
            AssertProtected("After saved-scene and UI checks");
            Require(Hash(Path.Combine(project, ScenePath)) == diskBefore, "Preview checks unexpectedly saved the scene.");
            PrepareCaptures();
            warmup = captureIndex = 0;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += CaptureAfterWarmup;
            EditorApplication.QueuePlayerLoopUpdate();
            WriteReport();
        }
        catch (Exception exception) { Fail(exception); }
    }

    static GameObject RequiredPrefab(int index)
    {
        string category = Categories[index] == 2 ? "Macroplastic" : "Megaplastic";
        string path = "Assets/Debris/Prepared/" + category + "/" + Keys[index] + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Require(prefab, "Missing prepared prefab: " + path);
        return prefab;
    }

    static void ValidateAddedModels()
    {
        for (int index = 0; index < added.Length; index++)
        {
            GameObject prefab = added[index];
            Require(exhibit.debrisModelGroups[Categories[index]].models.Contains(prefab), Keys[index] + " is absent from its source pool.");
            Require(!Enumerable.Range(0, 4).Where(category => category != Categories[index])
                .Any(category => exhibit.debrisModelGroups[category].models.Contains(prefab)), "A new model was assigned to two size categories.");
            Require(GPGPUIContent.FriendlyModelName(prefab.name) == Names[index], "The featured model name is incorrect: " + prefab.name);
            var meshes = new HashSet<Mesh>(prefab.GetComponentsInChildren<MeshFilter>(true).Select(filter => filter.sharedMesh));
            Require(meshes.Count > 0 && meshes.All(mesh => mesh && mesh.vertexCount > 0 && mesh.subMeshCount > 0 && Finite(mesh.bounds.center) && Finite(mesh.bounds.size)),
                Keys[index] + " has a missing, empty or invalid mesh.");
            Require(bands.Placements.Any(placement => placement.sourceBand == Categories[index] && placement.destinationBand == Categories[index] &&
                placement.transform && placement.transform.GetComponentsInChildren<MeshFilter>(true).Any(filter => meshes.Contains(filter.sharedMesh))),
                Keys[index] + " has no placed example in its own band.");
            long triangles = meshes.Sum(mesh => (long)mesh.triangles.Length / 3);
            var instance = Object.Instantiate(prefab);
            try
            {
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                Bounds bounds = RendererBounds(instance);
                Require(Finite(bounds.center) && Finite(bounds.size) && bounds.size.sqrMagnitude > .000001f, Keys[index] + " has invalid renderer bounds.");
                Require(bounds.center.magnitude <= Mathf.Max(.0001f, bounds.size.magnitude * .0001f), Keys[index] + " prefab is not centered around its visible bounds.");
                float[] expectedSize = index == 0 ? new[] { .294f, .339f, .410f } : index == 1 ? new[] { .592f, .594f, .831f } :
                    index == 2 ? new[] { .363f, .956f, .956f } : null;
                float[] actualSize = new[] { bounds.size.x, bounds.size.y, bounds.size.z }.OrderBy(value => value).ToArray();
                Require(actualSize.Last() >= .1f && actualSize.Last() <= 3f, Keys[index] + " imported physical dimensions are not in meters.");
                if (expectedSize != null) Require(actualSize.Zip(expectedSize, (actual, expected) => Mathf.Abs(actual - expected)).All(error => error < .005f),
                    Keys[index] + " imported dimensions disagree with the source FBX units.");
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Require(renderer.sharedMaterials.Length > 0, Keys[index] + " has an empty material slot list.");
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        Require(material && material.shader && material.shader.name == "Universal Render Pipeline/Lit" && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),
                            Keys[index] + " has a missing or unsupported material/shader.");
                        Require(material.GetTexture("_BaseMap") && material.GetTexture("_BumpMap"), Keys[index] + " is missing its color or normal texture.");
                        Require(material.GetTexture("_MetallicGlossMap"), Keys[index] + " is missing the packed PBR map.");
                        Require(!material.IsKeywordEnabled("_EMISSION") && material.GetFloat("_SpecularHighlights") == 0f && material.GetFloat("_EnvironmentReflections") == 0f,
                            Keys[index] + " lost the exhibit's matte material setup.");
                        foreach (string property in material.GetTexturePropertyNames())
                        {
                            Texture texture = material.GetTexture(property);
                            if (!texture) continue;
                            Require(texture.width <= 2048 && texture.height <= 2048, Keys[index] + " contains an unnecessarily large imported texture.");
                        }
                        var normal = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap")));
                        var color = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")));
                        Require(normal && normal.textureShape == TextureImporterShape.Texture2D && normal.textureType == TextureImporterType.NormalMap && !normal.sRGBTexture && !normal.convertToNormalmap && normal.flipGreenChannel == (index == 0),
                            Keys[index] + " normal map import convention is incorrect.");
                        Require(color && color.textureShape == TextureImporterShape.Texture2D && color.sRGBTexture, Keys[index] + " base color is not imported as a 2D sRGB texture.");
                    }
                }
                Line(Keys[index] + ": " + triangles + " triangles; centered bounds " + bounds.size.ToString("G6") + "; textured matte URP material; <=2048 textures; normal convention verified.");
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }

    static void ValidateUI()
    {
        var sourceMaterials = added.SelectMany(model => model.GetComponentsInChildren<Renderer>(true))
            .SelectMany(renderer => renderer.sharedMaterials).Distinct().ToDictionary(material => material, material => EditorJsonUtility.ToJson(material));
        probe = Object.Instantiate(authoredUI.gameObject).GetComponent<GPGPExhibitUI>();
        try
        {
            probe.BuildPreview();
            const int previewLayer = 30;
            int previewMask = 1 << previewLayer;
            Require(new[] { probe.cameraSetup.leftCamera.GetComponent<Camera>(), probe.cameraSetup.rightCamera.GetComponent<Camera>() }
                .All(camera => (camera.cullingMask & previewMask) != 0), "A content camera cannot render the isolated preview layer.");
            Require(exhibit.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.gameObject.layer != previewLayer),
                "An original exhibit object was moved onto the owned UI preview layer.");
            Require(exhibit.GetComponentsInChildren<GPGPWaterCompartment>(true).SelectMany(water => water.GetComponentsInChildren<Renderer>(true))
                .All(renderer => renderer.gameObject.layer == 0), "The authored water left its original Default layer.");
            for (int index = 0; index < added.Length; index++)
            {
                var pool = (List<GameObject>)Invoke(probe, "SourcePool", Categories[index]);
                Require(pool.Contains(added[index]), "The UI cannot select a newly added original-pool model.");
                ShowAddedModel(index);
                Require(probe.FeaturedObject && probe.FeaturedSource == added[index], "The UI failed to display a new model.");
                Require(((TMP_Text)Get(probe, "featureTitle")).text == Names[index], "The featured title lost its friendly name.");
                Require(probe.FeaturedObject.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.gameObject.layer == previewLayer),
                    "A featured model renderer is not isolated from the gallery's light layer.");
                Require(probe.transform.Find("Featured Debris Stage").GetComponentsInChildren<Light>(true).All(light => light.cullingMask == previewMask),
                    "An owned preview light can spill onto the original water or gallery.");
                var previewLights = probe.transform.Find("Featured Debris Stage").GetComponentsInChildren<Light>(true);
                Require(previewLights.Single(light => light.name == "Preview key").intensity == .35f &&
                    previewLights.Single(light => light.name == "Preview fill").intensity == .18f,
                    "Selecting rubber changed the preview lights instead of limiting its visibility adjustment to the featured clone.");
                Bounds bounds = RendererBounds(probe.FeaturedObject);
                Require(Finite(bounds.center) && Finite(bounds.size) && bounds.size.sqrMagnitude > .000001f, "A featured model has invalid bounds.");
                int selectedMarkers = probe.GetComponentsInChildren<TMP_Text>(true).Count(text => text.name == "Selected marker" && text.gameObject.activeInHierarchy);
                Require(probe.HasSelectedCategory && probe.SelectedCategory == Categories[index] && selectedMarkers == 1, "Featured-model category selection is not highlighted exactly once.");
                if (index >= 2)
                {
                    Require(GPGPUIContent.IsRubberDebris(added[index].name) && !GPGPUIContent.EligibleCountryContext(Categories[index], added[index].name),
                        "Rubber tires were treated as a hard-plastic sample.");
                    int cardCount = (int)Invoke(probe, "CardCount");
                    Require(cardCount == GPGPUIContent.DebrisCards(Categories[index], added[index].name).Length, "A tire received the plastic country-sample card.");
                    Require(((TMP_Text)Get(probe, "badge")).text.Contains("Rubber") && ((TMP_Text)Get(probe, "featureCaption")).text.Contains("Rubber"), "The tire's UI material label is missing.");
                    for (int card = 0; card < cardCount; card++)
                    {
                        Require(!probe.ShowsCountryContext, "A tire can enter the plastic country context.");
                        Require(!((TMP_Text)Get(probe, "body")).text.Contains("Fishing nets accounted"), "A tire shows the plastic net statistic as its own fact.");
                        probe.NextCard(1);
                    }
                }
            }
            Line("UI: all four new source-pool models feature correctly with friendly names and one category marker; both tire models use rubber cards and cannot display the plastic country chart.");
            Line("Preview isolation: all featured renderers and both owned lights use layer 30; both existing content-camera masks include it; original exhibit renderers remain off that layer and water remains on Default.");
            Require(sourceMaterials.All(pair => pair.Key && EditorJsonUtility.ToJson(pair.Key) == pair.Value),
                "The featured clone's visibility adjustment changed a shared source material.");
            Line("Rubber visibility: preview light intensities remain .35/.18 for every model; all four source models' shared materials remain unchanged.");
        }
        finally { DestroyProbe(); }
    }

    static void ShowAddedModel(int index)
    {
        probe.SelectCategory(Categories[index]);
        Invoke(probe, "ShowModel", added[index]);
        Invoke(probe, "Refresh", false);
    }

    static Dictionary<Object, string> SnapshotProtected()
    {
        var result = new Dictionary<Object, string>();
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            foreach (Transform transform in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                if (transform == bands.transform || transform.IsChildOf(bands.transform)) continue;
                result[transform.gameObject] = EditorJsonUtility.ToJson(transform.gameObject);
                foreach (Component component in transform.GetComponents<Component>())
                {
                    Require(component, "A protected scene object contains a missing script.");
                    if (component == exhibit) continue;
                    result[component] = EditorJsonUtility.ToJson(component);
                    var serialized = new SerializedObject(component);
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            Object asset = property.objectReferenceValue;
                            if (asset && EditorUtility.IsPersistent(asset) && (asset is Material || asset is ScriptableObject))
                                result[asset] = EditorJsonUtility.ToJson(asset);
                        }
                }
            }
        return result;
    }

    static void AssertProtected(string context)
    {
        foreach (var pair in protectedState)
            Require(pair.Key && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
                context + " changed protected scene state: " + (pair.Key ? pair.Key.name + " / " + pair.Key.GetType().Name : "deleted object"));
    }

    static string SettingsFingerprint(Object owner, params string[] excluded)
    {
        var result = new StringBuilder();
        var serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.GetIterator();
        while (property.Next(true))
        {
            if (excluded.Any(name => property.propertyPath == name || property.propertyPath.StartsWith(name + ".", StringComparison.Ordinal))) continue;
            string value;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer: value = property.longValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Boolean: value = property.boolValue.ToString(); break;
                case SerializedPropertyType.Float: value = property.doubleValue.ToString("R", CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.String: value = property.stringValue; break;
                case SerializedPropertyType.ObjectReference: value = property.objectReferenceEntityIdValue.ToString(); break;
                case SerializedPropertyType.Enum: value = property.enumValueIndex.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.ArraySize: value = property.intValue.ToString(CultureInfo.InvariantCulture); break;
                case SerializedPropertyType.Vector3: value = property.vector3Value.ToString("G9"); break;
                case SerializedPropertyType.Color: value = property.colorValue.ToString("G9"); break;
                case SerializedPropertyType.Generic: continue;
                default: value = property.type; break;
            }
            result.Append(property.propertyPath).Append('=').Append(value).Append('\n');
        }
        return result.ToString();
    }

    static void PrepareCaptures()
    {
        waterLuminance = double.NaN;
        authoredUIActive = authoredUI.gameObject.activeSelf;
        probe = Object.Instantiate(authoredUI.gameObject).GetComponent<GPGPExhibitUI>();
        probe.BuildPreview();
        authoredUI.gameObject.SetActive(false);
        eyes = new[] { probe.cameraSetup.leftCamera.GetComponent<Camera>(), probe.cameraSetup.rightCamera.GetComponent<Camera>() };
        eyePositions = eyes.Select(camera => camera.transform.localPosition).ToArray();
        eyeRotations = eyes.Select(camera => camera.transform.localRotation).ToArray();
        projections = eyes.Select(camera => camera.projectionMatrix).ToArray();
        views = eyes.Select(camera => camera.worldToCameraMatrix).ToArray();
        float half = probe.cameraSetup.CCSettings.eyeSeparation * .5f;
        eyes[0].transform.localPosition = eyePositions[0] - Vector3.right * half;
        eyes[1].transform.localPosition = eyePositions[1] + Vector3.right * half;
        foreach (Camera camera in eyes)
        {
            var offset = camera.GetComponent<CC_CAMERAOFFSET>();
            Require(offset, "Missing calibrated Cyber Canoe projection component.");
            Invoke(offset, "Start");
            Invoke(offset, "LateUpdate");
        }
    }

    static void CaptureAfterWarmup()
    {
        if (EditorApplication.timeSinceStartup > deadline) { Fail(new InvalidOperationException("Added-debris evidence capture timed out.")); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || ++warmup < 24)
        { EditorApplication.QueuePlayerLoopUpdate(); return; }
        try
        {
            if (captureIndex < added.Length)
            {
                ShowAddedModel(captureIndex);
                CaptureCamera(eyes[0], "Added-debris-" + Keys[captureIndex] + "-left-eye.png", 2400, 810, true);
                if (captureIndex == 2) CaptureCamera(eyes[1], "Added-debris-" + Keys[captureIndex] + "-right-eye.png", 2400, 810, true);
                captureIndex++;
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            EditorApplication.update -= CaptureAfterWarmup;
            RestoreCaptures();
            AssertProtected("After actual CC eye renders");
            CaptureContactSheet();
            RestoreRenderMaterialState();
            AssertProtected("After native contact-sheet render");
            Line("Evidence: four actual calibrated CC left-eye images, one right-eye tire image, and a native Unity-rendered four-model contact sheet; all camera/UI state restored; captures were not saved into the scene.");
            Line("RESULT PASS: new textured debris integrated into original pools and bands; scene geometry, water, turtle, lighting, stereo cameras, navigation and authored UI preserved.");
            WriteReport();
            Debug.Log("GPGP_ADDED_DEBRIS_VALIDATION: Passed.");
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
        catch (Exception exception) { Fail(exception); }
    }

    static void CaptureContactSheet()
    {
        Scene preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("Added debris contact sheet");
            SceneManager.MoveGameObjectToScene(root, preview);
            for (int index = 0; index < added.Length; index++)
            {
                Vector3 center = new Vector3(index % 2 == 0 ? -1.45f : 1.45f, index < 2 ? 1.38f : -1.32f, 0);
                var wrapper = new GameObject(Keys[index]).transform;
                wrapper.SetParent(root.transform, false);
                var item = Object.Instantiate(added[index], wrapper, false);
                Bounds bounds = RendererBounds(item);
                item.transform.localPosition -= bounds.center;
                wrapper.localScale = Vector3.one * (1.9f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
                wrapper.localRotation = Quaternion.Euler(0, -22, 0);
                wrapper.position = center;
                var labelObject = new GameObject("Label", typeof(TextMeshPro));
                labelObject.transform.SetParent(root.transform, false);
                labelObject.transform.position = center + new Vector3(0, -1.2f, -.05f);
                var label = labelObject.GetComponent<TextMeshPro>();
                label.font = authoredUI.font;
                label.text = Names[index] + "\n" + (Categories[index] == 2 ? "MACRO / PLASTIC" : index >= 2 ? "MEGA / RUBBER" : "MEGA / PLASTIC");
                label.fontSize = 1.2f;
                label.alignment = TextAlignmentOptions.Center;
                label.color = new Color(.75f, .92f, .94f);
                label.rectTransform.sizeDelta = new Vector2(2.8f, .55f);
            }
            var cameraObject = new GameObject("Contact sheet camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = preview;
            camera.transform.position = new Vector3(0, .3f, -12);
            camera.transform.rotation = Quaternion.identity;
            camera.orthographic = true;
            camera.orthographicSize = 3.2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.02f, .045f, .065f);
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 30f;
            camera.allowHDR = true;
            AddPreviewLight(preview, "Contact key", new Vector3(35, -35, 0), 2.2f);
            AddPreviewLight(preview, "Contact fill", new Vector3(-25, 125, 0), .7f);
            CaptureCamera(camera, "Added-debris-contact-sheet.png", 1800, 1500, false);
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static void AddPreviewLight(Scene preview, string name, Vector3 rotation, float intensity)
    {
        var lightObject = new GameObject(name, typeof(Light));
        SceneManager.MoveGameObjectToScene(lightObject, preview);
        var light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        lightObject.transform.rotation = Quaternion.Euler(rotation);
    }

    static void CaptureCamera(Camera camera, string filename, int width, int height, bool withReflection)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
        RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
        Rect oldRect = camera.rect;
        StereoTargetEyeMask oldEye = camera.stereoTargetEye;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        bool oldXR = data && data.allowXRRendering;
        Texture2D linear = null, image = null;
        try
        {
            Require(target.Create(), "Could not allocate evidence render target.");
            camera.targetTexture = target;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            if (data) data.allowXRRendering = false;
            var reflection = withReflection ? Object.FindObjectsByType<PacificExhibit.PacificPlanarReflection>(FindObjectsInactive.Include)
                .FirstOrDefault(value => value.gameObject.scene == scene) : null;
            var request = new RenderPipeline.StandardRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "URP cannot render the evidence camera.");
            for (int pass = 0; pass < 2; pass++)
            {
                if (reflection) reflection.RenderReflectionFor(camera);
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            RenderTexture.active = target;
            linear = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            linear.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            linear.Apply();
            Color[] pixels = linear.GetPixels();
            if (withReflection && filename.EndsWith("-left-eye.png", StringComparison.Ordinal))
            {
                double sum = 0;
                int count = 0;
                // The upper water beside the right panel is outside the UI
                // and contains no featured geometry. Rubber preview changes
                // must not brighten this authored part of the exhibit.
                for (int y = (int)(height * .60f); y < (int)(height * .80f); y++)
                    for (int x = (int)(width * .64f); x < (int)(width * .69f); x++)
                    {
                        Color pixel = pixels[y * width + x];
                        sum += pixel.r * .2126 + pixel.g * .7152 + pixel.b * .0722;
                        count++;
                    }
                double mean = sum / Math.Max(1, count);
                if (double.IsNaN(waterLuminance)) waterLuminance = mean;
                else Require(Math.Abs(mean - waterLuminance) < .006,
                    "Changing the featured model changed the water's lighting (preview light spill): " + mean.ToString("G6") + " versus " + waterLuminance.ToString("G6"));
                Line(filename + ": unchanged upper-water luminance " + mean.ToString("G6", CultureInfo.InvariantCulture) + ".");
            }
            int bright = 0, pink = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color value = pixels[index];
                Require(Finite(new Vector3(value.r, value.g, value.b)), "The evidence render contains nonfinite pixels.");
                if (value.maxColorComponent > .15f) bright++;
                if (value.r > .7f && value.b > .7f && value.g < .1f) pink++;
                pixels[index] = value.gamma;
            }
            Require(bright > pixels.Length / 1000, "The evidence render is unexpectedly empty.");
            Require(pink < pixels.Length / 1000, "The evidence render contains shader-error magenta.");
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.SetPixels(pixels);
            image.Apply();
            File.WriteAllBytes(Path.Combine(project, "Artifacts", filename), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = oldActive;
            camera.targetTexture = oldTarget;
            camera.rect = oldRect;
            camera.stereoTargetEye = oldEye;
            if (data) data.allowXRRendering = oldXR;
            if (linear) Object.DestroyImmediate(linear);
            if (image) Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    static void DestroyProbe()
    {
        if (probe) Object.DestroyImmediate(probe.gameObject);
        probe = null;
    }

    static void RestoreCaptures()
    {
        DestroyProbe();
        if (eyes == null || eyePositions == null) return;
        if (authoredUI) authoredUI.gameObject.SetActive(authoredUIActive);
        for (int index = 0; index < eyes.Length; index++)
        {
            if (!eyes[index]) continue;
            eyes[index].transform.localPosition = eyePositions[index];
            eyes[index].transform.localRotation = eyeRotations[index];
            eyes[index].projectionMatrix = projections[index];
            eyes[index].worldToCameraMatrix = views[index];
        }
        eyes = null;
        RestoreRenderMaterialState();
    }

    static void RestoreRenderMaterialState()
    {
        if (protectedState == null) return;
        foreach (var pair in protectedState.Where(value => value.Key is Material))
        {
            if (!pair.Key) continue;
            string after = EditorJsonUtility.ToJson(pair.Key);
            if (after == pair.Value) continue;
            string stem = "Capture-material-" + new string(pair.Key.name.Where(char.IsLetterOrDigit).ToArray());
            File.WriteAllText(Path.Combine(project, "Artifacts", stem + "-before.json"), pair.Value);
            File.WriteAllText(Path.Combine(project, "Artifacts", stem + "-after.json"), after);
            // URP's lazy material validation can initialize keywords when a
            // camera first renders. This is evidence-only work, after all
            // authoring checks and the isolated scene save have passed.
            EditorJsonUtility.FromJsonOverwrite(pair.Value, pair.Key);
            Require(EditorJsonUtility.ToJson(pair.Key) == pair.Value, "Could not restore a material warmed by the capture pipeline.");
            Line("Capture cleanup: restored transient URP material initialization for " + pair.Key.name + "; before/after JSON retained in Artifacts.");
        }
    }

    static Bounds RendererBounds(GameObject model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        Require(renderers.Length > 0, "A model has no renderers.");
        Bounds result = renderers[0].bounds;
        foreach (Renderer renderer in renderers.Skip(1)) result.Encapsulate(renderer.bounds);
        return result;
    }

    static int[] Counts() => bands.transform.Find("Debris size exhibit").Cast<Transform>().Select(value => value.childCount).ToArray();
    static string[][] PoolPaths() => exhibit.debrisModelGroups.Select(group => group.models.Select(AssetDatabase.GetAssetPath).ToArray()).ToArray();
    static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    static object Get(object owner, string name) => owner.GetType().GetField(name, Instance).GetValue(owner);
    static object Invoke(object owner, string name, params object[] arguments)
    {
        MethodInfo method = owner.GetType().GetMethod(name, Instance);
        Require(method != null, "Missing validation target method: " + name);
        return method.Invoke(owner, arguments);
    }
    static string Hash(string path)
    { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Line(string message) => Report.AppendLine(message);
    static void WriteReport() { if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, Report.ToString()); }
    static void Fail(Exception exception)
    {
        EditorApplication.update -= CaptureAfterWarmup;
        try { RestoreCaptures(); }
        catch (Exception cleanupException) { Line("Capture cleanup failure: " + cleanupException); }
        Line("RESULT FAIL: " + exception);
        WriteReport();
        Debug.LogException(exception);
        EditorApplication.Exit(1);
    }
}
#endif
