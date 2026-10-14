#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Runs regression checks and prepares evidence only in the isolated validation project.</summary>
public static class GPGPFactoidWaypointValidation
{
    const string ScenePath = "Assets/Scenes/Plastic_Type.unity";
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly StringBuilder Report = new StringBuilder();
    static GPGPExhibitUI ui;
    static CCaux_Waypointer actualWaypointer;
    static GameObject fixtureRoot;
    static Scene scene;
    static string project, reportPath;
    static Dictionary<Object, string> protectedRig;
    static Camera[] eyes;
    static Vector3[] eyePositions;
    static Quaternion[] eyeRotations;
    static Matrix4x4[] projections, views;
    static int warmup;
    static double deadline;

    public static void BuildAndCapture()
    {
        try
        {
            project = Directory.GetParent(Application.dataPath).FullName;
            Require(project.Replace('\\', '/').EndsWith("/.utmp/pacific-exhibit-validation", StringComparison.OrdinalIgnoreCase),
                "Run this scene-writing audit only in .utmp/pacific-exhibit-validation.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Validation must remain outside Play mode and XR.");
            Directory.CreateDirectory(Path.Combine(project, "Artifacts"));
            reportPath = Path.Combine(project, "Artifacts", "GPGP-factoid-waypoint-check.txt");
            Report.Clear();
            Line("Factoid and waypoint regression; Unity " + Application.unityVersion + "; isolated editor, no Play mode/XR.");
            string before = Hash(Path.Combine(project, ScenePath));
            scene = EditorSceneManager.OpenScene(ScenePath);
            ui = Object.FindObjectsByType<GPGPExhibitUI>(FindObjectsInactive.Include)
                .Single(value => value.gameObject.scene == scene);
            protectedRig = SnapshotRig();
            ui.BuildPreview();
            actualWaypointer = ui.waypointer;
            Require(actualWaypointer && actualWaypointer.gameObject.scene == scene,
                "The scene UI must resolve and retain its existing scene waypointer.");
            Require(actualWaypointer.previousButton == GamepadButton.West && actualWaypointer.nextButton == GamepadButton.East,
                "The existing X/West previous and B/East next bindings changed.");
            Require(ui.GetComponentsInChildren<UnityEngine.UI.Selectable>(true).Length == 0 &&
                ui.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(true).Length == 0,
                "The overlay must retain the existing gamepad/EventSystem ownership.");
            ValidateWaypoints();
            ValidateFactsAndLayout();
            var rebuilt = GPGPExhibitUIAuthoring.RebuildPreview(ui);
            Require(rebuilt.waypointer == actualWaypointer, "The editor authoring rebuild lost the explicit waypoint reference.");
            Undo.PerformUndo();
            ui = Object.FindObjectsByType<GPGPExhibitUI>(FindObjectsInactive.Include)
                .Single(value => value.gameObject.scene == scene);
            Require(ui.waypointer == actualWaypointer, "Undo did not restore the prior UI and its waypoint reference.");
            Line("Authoring: Rebuild Preview preserves its explicit waypointer reference; Undo restores the preceding UI hierarchy/reference.");
            ui.BuildPreview();
            Require(ui.waypointer == actualWaypointer, "Rebuilding lost the scene's waypointer reference.");
            Require(!ui.IsUIHidden && ui.CurrentView == 0, "The authored preview must start with its visible Overview.");
            AssertProtectedRig("After all interactions");
            Require(Hash(Path.Combine(project, ScenePath)) == before, "The regression checks unexpectedly saved the scene.");
            EditorUtility.SetDirty(ui);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save the isolated generated preview.");
            Line("Scene: regenerated only the owned UI; retained its resolved waypointer; all original rig objects and camera inputs/poses unchanged before saving the isolated preview.");
            PrepareEyes();
            warmup = 0;
            deadline = EditorApplication.timeSinceStartup + 90;
            EditorApplication.update += CaptureAfterWarmup;
            EditorApplication.QueuePlayerLoopUpdate();
            WriteReport();
        }
        catch (Exception exception) { Fail(exception); }
    }

    static void ValidateWaypoints()
    {
        // A separate navigation target lets the real waypointer implementation run
        // without moving any authored cameras, the canoe, or the exhibit.
        fixtureRoot = new GameObject("Factoid waypoint regression fixture");
        SceneManager.MoveGameObjectToScene(fixtureRoot, scene);
        var target = new GameObject("Fixture navigation target");
        target.transform.SetParent(fixtureRoot.transform, false);
        var fixture = fixtureRoot.AddComponent<CCaux_Waypointer>();
        fixture.mainNavigationNode = target;
        fixture.loadWaypointFileOnStart = false;
        fixture.useSplineSmoothMovement = false;
        Invoke(fixture, "Start");
        var positions = new LinkedList<Vector3>(Enumerable.Range(0, 5).Select(i => new Vector3(i, 0, 0)));
        var rotations = new LinkedList<Quaternion>(Enumerable.Range(0, 5).Select(_ => Quaternion.identity));
        Set(fixture, "navPositionList", positions);
        Set(fixture, "navRotationList", rotations);
        Set(fixture, "currentFramePos", positions.First);
        Set(fixture, "currentFrameRot", rotations.First);
        int notifications = 0;
        Action<int> counter = _ => notifications++;
        fixture.OnWaypointChanged += counter;
        Invoke(ui, "OnDisable");
        ui.waypointer = fixture;
        Invoke(ui, "OnEnable");
        ui.BuildPreview();
        Require(ListenerCount(fixture) == 1, "The UI must subscribe exactly once to the current waypointer.");
        AssertSelection(0);
        for (int i = 1; i <= 12; i++)
        {
            int previous = notifications;
            fixture.NextFrame();
            Require(notifications == previous + 1, "NextFrame must emit exactly one target notification.");
            AssertTarget(fixture, i % 5);
        }
        for (int i = 1; i <= 12; i++)
        {
            int previous = notifications;
            Invoke(fixture, "PrevFrame");
            Require(notifications == previous + 1, "PrevFrame must emit exactly one target notification.");
            AssertTarget(fixture, (2 - i % 5 + 5) % 5);
        }
        Line("Navigation: twelve next and twelve previous selections traverse Overview, Mega, Macro, Meso, Micro with both wrap directions; every real linked-list target matches its category and emits once.");

        // The actual linked-list target must remain authoritative even if an old
        // cached index was stale before a selection.
        Set(fixture, "<CurrentWaypointIndex>k__BackingField", 3);
        fixture.NextFrame();
        AssertTarget(fixture, 1);
        fixture.NextFrame();
        fixture.NextFrame();
        ui.BuildPreview();
        ui.BuildPreview();
        AssertSelection(3);
        Require(ListenerCount(fixture) == 1, "Repeated preview initialization duplicated the waypoint listener.");
        Invoke(ui, "OnEnable");
        Invoke(ui, "OnEnable");
        Require(ListenerCount(fixture) == 1, "Repeated enable duplicated the waypoint listener.");
        Invoke(ui, "OnDisable");
        Require(ListenerCount(fixture) == 0, "Disabled UI retained a waypoint listener.");
        fixture.NextFrame();
        AssertSelection(3);
        Invoke(ui, "OnEnable");
        AssertSelection(4);
        Require(ListenerCount(fixture) == 1, "Re-enabled UI did not restore exactly one listener.");
        Line("Lifecycle: initialization at a preselected Meso target, repeated rebuild/enable, unsubscribe while disabled, and immediate Micro synchronization on re-enable all verified.");
        ValidateGamepadInput(fixture);

        int old = notifications;
        Invoke(fixture, "PlayFromStart");
        Require(notifications == old + 1, "Starting a tour must emit its target exactly once.");
        AssertTarget(fixture, 0);
        for (int expected = 1; expected <= 5; expected++)
        {
            Set(fixture, "playingFrame", false);
            old = notifications;
            Invoke(fixture, "DoAnimation");
            Require(notifications == old + 1, "Tour advancement must emit exactly one target notification.");
            AssertTarget(fixture, expected % 5);
        }
        fixture.NextFrame();
        fixture.waypointFile = Path.Combine(project, "Artifacts", "GPGP-waypoint-regression-fixture.txt");
        File.WriteAllLines(fixture.waypointFile, Enumerable.Range(0, 5).Select(i => i + " 0 0 0 0 0 1"));
        old = notifications;
        Invoke(fixture, "ReadWayPoints");
        Require(notifications == old + 1, "Reloading a tour must emit its reset target exactly once.");
        AssertTarget(fixture, 0);
        Require(!(bool)Get(fixture, "playMode") && !(bool)Get(fixture, "playingFrame") && !(bool)Get(fixture, "nextPrevAnimation"),
            "Reloading must clear previous playback animation flags.");
        Line("Tour: PlayFromStart, automatic target advancement and loop, reload to Overview, and stale-index recovery verified against actual linked-list nodes.");
        Invoke(ui, "OnDisable");
        fixture.OnWaypointChanged -= counter;
        Require(ListenerCount(fixture) == 0, "Fixture cleanup retained a UI listener.");
        ui.waypointer = actualWaypointer;
        Invoke(ui, "OnEnable");
        Object.DestroyImmediate(fixtureRoot);
        fixtureRoot = null;
        ui.BuildPreview();
    }

    static void AssertTarget(CCaux_Waypointer fixture, int expected)
    {
        Require(fixture.CurrentWaypointIndex == expected, "Wrong waypoint index: expected " + expected + ", got " + fixture.CurrentWaypointIndex + ".");
        var node = (LinkedListNode<Vector3>)Get(fixture, "currentFramePos");
        Require(node != null && Mathf.Abs(node.Value.x - expected) < .001f, "The published index does not match the actual camera target node.");
        AssertSelection(expected);
    }

    static void AssertSelection(int waypoint)
    {
        bool selected = waypoint != 0;
        Require(ui.HasSelectedCategory == selected, "Overview and selected-band states are conflated at waypoint " + waypoint + ".");
        Require(ui.CurrentView == (selected ? 1 : 0), "Incorrect information view at waypoint " + waypoint + ".");
        if (selected)
        {
            Require(ui.SelectedCategory == 4 - waypoint, "Wrong source category at waypoint " + waypoint + ".");
            Require(!ui.AutoCycling, "Debris facts must stay pinned.");
        }
        for (int index = 0; index < 4; index++)
        {
            Transform control = Find("Control 0:" + index);
            TMP_Text marker = control.Find("Selected marker")?.GetComponent<TMP_Text>();
            Require(marker, "Missing explicit selected marker for category " + index + ".");
            bool marked = marker.gameObject.activeInHierarchy && marker.text.IndexOf("SELECTED", StringComparison.OrdinalIgnoreCase) >= 0;
            bool expected = selected && index == waypoint - 1;
            Require(marked == expected, "Selected marker does not match the camera's category at waypoint " + waypoint + ".");
            Transform stripe = control.Find("Selected stripe");
            Require(stripe && stripe.gameObject.activeInHierarchy == expected, "Selected stripe disagrees with its category marker.");
        }
    }

    static void ValidateFactsAndLayout()
    {
        ui.SelectView(0);
        ui.SelectView(1);
        Require(ui.HasSelectedCategory, "Selecting Debris sizes from Overview must show the current category marker.");
        ui.SelectView(0);
        Require(GPGPUIContent.OverviewCards.Length >= 4, "Overview is missing its approved factoid cards.");
        string overview = string.Join("\n", GPGPUIContent.OverviewCards.Select(card => card.Heading + " " + card.Body + " " + card.Source + " " + card.Headline));
        Require(overview.Contains("1.6") && overview.Contains("1.8") && overview.Contains("79,000") && overview.Contains("2018"),
            "Approved area, piece-count, mass and 2018 estimate context must be present.");
        ui.SelectCategory(0);
        Transform comparison = Find("Piece count and mass comparison");
        bool foundComparison = false;
        int count = (int)Invoke(ui, "CardCount");
        for (int i = 0; i < count; i++)
        {
            if (comparison.gameObject.activeInHierarchy)
            {
                float primary = comparison.Find("Primary metric share").GetComponent<RectTransform>().sizeDelta.x / 1140f;
                float secondary = comparison.Find("Secondary metric share").GetComponent<RectTransform>().sizeDelta.x / 1140f;
                Require(Mathf.Abs(primary - .94f) < .0001f && Mathf.Abs(secondary - .08f) < .0001f,
                    "Microplastic comparison bars must encode 94% of pieces and 8% of mass against the same full width.");
                Require(comparison.Find("Primary metric label").GetComponent<TMP_Text>().text.Contains("94") &&
                    comparison.Find("Secondary metric label").GetComponent<TMP_Text>().text.Contains("8"),
                    "The comparison bars need visible percentages.");
                foundComparison = true;
            }
            Require(!ui.ShowsCountryContext, "Microplastic facts must not display the large rigid-plastic country sample.");
            AssertActiveTextFits();
            ui.NextCard(1);
        }
        Require(foundComparison, "No microplastic comparison fact was displayed.");
        ui.SelectCategory(3);
        ShowCountryEligibleSource();
        bool foundCountry = false;
        count = (int)Invoke(ui, "CardCount");
        for (int i = 0; i < count; i++)
        {
            if (ui.ShowsCountryContext)
            {
                Require(!comparison.gameObject.activeInHierarchy, "Country chart must occupy its own card.");
                Require(ui.CurrentCard != 0, "Country figures must be on their own fact card instead of being appended to the item introduction.");
                foundCountry = true;
            }
            AssertActiveTextFits();
            ui.NextCard(1);
        }
        Require(foundCountry, "The Mega category has no dedicated country-context card.");
        for (int view = 0; view < 4; view++)
        {
            ui.SelectView(view);
            if (view == 1)
            {
                for (int category = 0; category < 4; category++)
                {
                    ui.SelectCategory(category);
                    for (int card = 0, cards = (int)Invoke(ui, "CardCount"); card < cards; card++)
                    { AssertActiveTextFits(); ui.NextCard(1); }
                }
            }
            else
            {
                for (int card = 0, cards = (int)Invoke(ui, "CardCount"); card < cards; card++)
                { AssertActiveTextFits(); ui.NextCard(1); }
            }
        }
        ui.SelectCategory(2);
        Vector2 selectedSize = Find("Control 0:1").GetComponent<RectTransform>().sizeDelta;
        Color selectedFill = Find("Control 0:1").GetComponent<UnityEngine.UI.Image>().color;
        while (ui.FocusRow > 0) ui.Navigate(Vector2.down);
        while (ui.FocusIndex != 0) ui.Navigate(Vector2.left);
        ui.Navigate(Vector2.right);
        ui.Navigate(Vector2.right);
        Require(ui.SelectedCategory == 2 && ui.HasSelectedCategory, "Moving D-pad focus changed the selected category.");
        Require(Find("Control 0:1").GetComponent<UnityEngine.UI.Image>().color == selectedFill,
            "Moving focus removed the active category's filled highlight.");
        Require(Find("Control 0:1").GetComponent<RectTransform>().sizeDelta == selectedSize,
            "Selected styling changed footer layout geometry.");
        for (int index = 0; index < 4; index++)
        {
            foreach (TMP_Text text in Find("Control 0:" + index).GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate(true, true);
                Require(text.preferredHeight <= text.rectTransform.rect.height + 1 && text.preferredWidth <= text.rectTransform.rect.width + 1,
                    "Footer text exceeds its rectangle: " + text.text.Replace('\n', ' ') + "; preferred=" + text.preferredWidth + "x" + text.preferredHeight + "; rect=" + text.rectTransform.rect.size);
            }
        }
        string footer = Find("Gamepad instructions").GetComponent<TMP_Text>().text;
        Require(footer.Contains("X") && footer.Contains("B") && footer.IndexOf("band", StringComparison.OrdinalIgnoreCase) >= 0,
            "Footer needs the existing X/B band-navigation instructions.");
        Line("Facts/layout: approved 2018 Overview estimates, 94%/8% comparison geometry and labels, separate country sample card, selected-band fill/marker independent of D-pad focus, all visible card text and four category labels within their rectangles, and X/B footer instructions verified.");
    }

    static void ValidateGamepadInput(CCaux_Waypointer fixture)
    {
        Gamepad previousPad = Gamepad.current;
        Keyboard previousKeyboard = Keyboard.current;
        PropertyInfo keyboardCurrent = typeof(Keyboard).GetProperty("current", BindingFlags.Static | BindingFlags.Public);
        object manager = typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        PropertyInfo playerUpdates = manager.GetType().GetProperty("runPlayerUpdatesInEditMode", BindingFlags.Instance | BindingFlags.Public);
        bool oldUpdates = (bool)playerUpdates.GetValue(manager);
        Gamepad pad = null;
        try
        {
            playerUpdates.SetValue(manager, true);
            pad = InputSystem.AddDevice<Gamepad>();
            pad.MakeCurrent();
            foreach (GamepadButton button in new[] { GamepadButton.East, GamepadButton.West })
            {
                InputSystem.QueueStateEvent(pad, new GamepadState());
                UpdateRuntimeInput();
                pad.MakeCurrent();
                keyboardCurrent.SetValue(null, null);
                Invoke(fixture, "Update");
                int before = fixture.CurrentWaypointIndex;
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(button));
                UpdateRuntimeInput();
                pad.MakeCurrent();
                keyboardCurrent.SetValue(null, null);
                Invoke(fixture, "Update");
                int expected = (before + (button == GamepadButton.East ? 1 : 4)) % 5;
                AssertTarget(fixture, expected);
            }
            Line("Input: virtual B/East and X/West press edges drive the real waypointer Update with Keyboard.current null; the selected footer/category follows both bindings. Temporary input state/devices restored.");
        }
        finally
        {
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            playerUpdates.SetValue(manager, oldUpdates);
            keyboardCurrent.SetValue(null, previousKeyboard);
            if (previousPad != null && previousPad.added) previousPad.MakeCurrent();
        }
    }

    static void UpdateRuntimeInput()
    {
        MethodInfo update = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(InputUpdateType) }, null);
        Require(update != null, "Missing explicit dynamic input update.");
        update.Invoke(null, new object[] { InputUpdateType.Dynamic });
    }

    static void ShowCountryEligibleSource()
    {
        GameObject eligible = ui.exhibit.debrisModelGroups[3].models.First(model => model && GPGPUIContent.EligibleCountryContext(3, model.name));
        Invoke(ui, "ShowModel", eligible);
        Invoke(ui, "Refresh", false);
    }

    static void AssertActiveTextFits()
    {
        foreach (TMP_Text text in ui.GetComponentsInChildren<TMP_Text>(true).Where(item => item.gameObject.activeInHierarchy && !string.IsNullOrEmpty(item.text)))
        {
            text.ForceMeshUpdate(true, true);
            // Exclude invisible wrap spaces: TMP retains their advance beyond the
            // rectangle although the actual line glyphs fit within it.
            Vector2 rendered = text.GetRenderedValues(true);
            Require(rendered.x <= text.rectTransform.rect.width + 2 && rendered.y <= text.rectTransform.rect.height + 2,
                "Visible text exceeds its rectangle in view " + ui.CurrentView + ", card " + ui.CurrentCard + ": " + text.name + " = " + text.text.Replace('\n', ' ') +
                "; rendered=" + rendered + "; rect=" + text.rectTransform.rect.size + "; font=" + text.fontSize + ".");
        }
    }

    static Dictionary<Object, string> SnapshotRig()
    {
        var result = new Dictionary<Object, string>();
        foreach (Transform item in ui.ccHub.GetComponentsInChildren<Transform>(true))
        {
            if (item == ui.transform || item.IsChildOf(ui.transform)) continue;
            result[item.gameObject] = EditorJsonUtility.ToJson(item.gameObject);
            foreach (Component component in item.GetComponents<Component>())
                if (component) result[component] = EditorJsonUtility.ToJson(component);
        }
        return result;
    }

    static void AssertProtectedRig(string context)
    {
        foreach (var pair in protectedRig)
            Require(pair.Key && EditorJsonUtility.ToJson(pair.Key) == pair.Value, context + " changed an original rig object: " + (pair.Key ? pair.Key.name : "destroyed object") + ".");
    }

    static void PrepareEyes()
    {
        eyes = new[] { ui.cameraSetup.leftCamera.GetComponent<Camera>(), ui.cameraSetup.rightCamera.GetComponent<Camera>() };
        eyePositions = eyes.Select(camera => camera.transform.localPosition).ToArray();
        eyeRotations = eyes.Select(camera => camera.transform.localRotation).ToArray();
        projections = eyes.Select(camera => camera.projectionMatrix).ToArray();
        views = eyes.Select(camera => camera.worldToCameraMatrix).ToArray();
        float half = ui.cameraSetup.CCSettings.eyeSeparation * .5f;
        eyes[0].transform.localPosition = eyePositions[0] - Vector3.right * half;
        eyes[1].transform.localPosition = eyePositions[1] + Vector3.right * half;
        foreach (Camera camera in eyes)
        {
            var offset = camera.GetComponent<CC_CAMERAOFFSET>();
            Invoke(offset, "Start");
            Invoke(offset, "LateUpdate");
        }
    }

    static void CaptureAfterWarmup()
    {
        if (EditorApplication.timeSinceStartup > deadline) { Fail(new InvalidOperationException("Eye capture warmup timed out.")); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || ++warmup < 24)
        { EditorApplication.QueuePlayerLoopUpdate(); return; }
        EditorApplication.update -= CaptureAfterWarmup;
        try
        {
            ui.SelectView(0);
            ui.NextCard(1);
            CaptureEye(eyes[0], "GPGP-factoids-overview-left-eye.png");
            ui.SelectCategory(0);
            ui.NextCard(1);
            CaptureEye(eyes[0], "GPGP-factoids-micro-left-eye.png");
            CaptureEye(eyes[1], "GPGP-factoids-micro-right-eye.png");
            ui.SelectCategory(3);
            ShowCountryEligibleSource();
            for (int i = 0; i < (int)Invoke(ui, "CardCount") && !ui.ShowsCountryContext; i++) ui.NextCard(1);
            CaptureEye(eyes[0], "GPGP-factoids-country-left-eye.png");
            RestoreEyes();
            AssertProtectedRig("After captures");
            Line("Evidence: Overview, selected Micro (both actual CC eyes), and dedicated country-card PNGs captured at 2400x810; all camera state restored; captures were not saved into the scene.");
            Line("RESULT PASS: target/category synchronization, lifecycle, fact cards, chart geometry, independent footer selection/focus and original rig protection.");
            WriteReport();
            Debug.Log("GPGP_FACTOID_WAYPOINT_VALIDATION: Passed.");
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
        catch (Exception exception) { Fail(exception); }
    }

    static void CaptureEye(Camera camera, string filename)
    {
        var target = new RenderTexture(2400, 810, 24, RenderTextureFormat.DefaultHDR);
        RenderTexture oldTarget = camera.targetTexture, oldActive = RenderTexture.active;
        Rect oldRect = camera.rect;
        StereoTargetEyeMask oldEye = camera.stereoTargetEye;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        bool oldXR = data && data.allowXRRendering;
        Texture2D linear = null, image = null;
        try
        {
            Require(target.Create(), "Could not allocate eye capture target.");
            camera.targetTexture = target;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            if (data) data.allowXRRendering = false;
            var reflection = Object.FindObjectsByType<PacificExhibit.PacificPlanarReflection>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.gameObject.scene == scene);
            var request = new RenderPipeline.StandardRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "URP cannot render the actual CC content eye.");
            for (int pass = 0; pass < 2; pass++)
            {
                if (reflection) reflection.RenderReflectionFor(camera);
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            RenderTexture.active = target;
            linear = new Texture2D(target.width, target.height, TextureFormat.RGBAFloat, false, true);
            linear.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            linear.Apply();
            Color[] pixels = linear.GetPixels();
            for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
            image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
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

    static void RestoreEyes()
    {
        if (eyes == null || eyePositions == null) return;
        for (int i = 0; i < eyes.Length; i++)
        {
            if (!eyes[i]) continue;
            eyes[i].transform.localPosition = eyePositions[i];
            eyes[i].transform.localRotation = eyeRotations[i];
            eyes[i].projectionMatrix = projections[i];
            eyes[i].worldToCameraMatrix = views[i];
        }
        eyes = null;
    }

    static int ListenerCount(CCaux_Waypointer owner) => owner.OnWaypointChanged?.GetInvocationList().Count(value => value.Target == ui) ?? 0;
    static Transform Find(string name) => ui.GetComponentsInChildren<Transform>(true).Single(item => item.name == name);
    static object Get(object owner, string name) => owner.GetType().GetField(name, Instance).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Instance).SetValue(owner, value);
    static object Invoke(object owner, string name, params object[] args)
    {
        MethodInfo method = owner.GetType().GetMethod(name, Instance);
        Require(method != null, "Missing method " + owner.GetType().Name + "." + name + ".");
        return method.Invoke(owner, args);
    }
    static string Hash(string path)
    { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Line(string message) => Report.AppendLine(message);
    static void WriteReport() { if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, Report.ToString()); }
    static void Fail(Exception exception)
    {
        EditorApplication.update -= CaptureAfterWarmup;
        RestoreEyes();
        if (fixtureRoot) Object.DestroyImmediate(fixtureRoot);
        Line("RESULT FAIL: " + exception);
        WriteReport();
        Debug.LogException(exception);
        EditorApplication.Exit(1);
    }
}
#endif
