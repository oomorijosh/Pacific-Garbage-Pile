#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GPGPUI
{
    /// <summary>Adds the exhibit UI without changing the stereo rig or its existing controls.</summary>
    public static class GPGPExhibitUIAuthoring
    {
        public const string RootName = "GPGP Stereo UI";
        const string MenuPath = "Tools/Pacific Exhibit/Add GPGP Stereo UI";
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        const string TurtlePath = "Assets/Wildlife/SeaTurtle/Prepared/SeaTurtle.prefab";

        [MenuItem(MenuPath)]
        public static void AddToCurrentScene() => AddOrSelect(FindExhibit());

        [MenuItem(MenuPath, true)]
        static bool CanAdd() => !EditorApplication.isPlayingOrWillChangePlaymode &&
            PrefabStageUtility.GetCurrentPrefabStage() == null && SceneManager.GetActiveScene().IsValid() &&
            SceneManager.GetActiveScene().isLoaded;

        public static GPGPExhibitUI AddOrSelect(GPGPLayeredCubeVisualization exhibit)
        {
            ValidateExhibit(exhibit);
            FindRig(exhibit.gameObject.scene, out CC_CAMERA_SETUP setup, out Transform hub, out Transform screen);
            var existing = hub.GetComponentsInChildren<GPGPExhibitUI>(true);
            if (existing.Length > 1)
                throw new InvalidOperationException("The stereo hub contains more than one GPGP UI. Choose which one to keep before adding another.");
            if (existing.Length == 1)
            {
                ValidateOwnedRoot(existing[0]);
                if (existing[0].exhibit != exhibit)
                    throw new InvalidOperationException("The existing GPGP UI belongs to another exhibit. Its content has been preserved.");
                Selection.activeGameObject = existing[0].gameObject;
                EditorGUIUtility.PingObject(existing[0]);
                Debug.Log("GPGP_UI: Selected the existing UI. Use Rebuild Preview in its Inspector to refresh its own content.", existing[0]);
                return existing[0];
            }
            if (hub.Find(RootName))
                throw new InvalidOperationException("A different object already uses the GPGP Stereo UI name. It has been preserved.");
            return Create(exhibit, setup, hub, screen, FindTurtle(exhibit),
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath), .4f, 25f, null);
        }

        public static GPGPExhibitUI RebuildPreview(GPGPExhibitUI ui)
        {
            ValidateOwnedRoot(ui);
            ValidateExhibit(ui.exhibit);
            ValidateRig(ui.cameraSetup, ui.ccHub, ui.sourceScreen, ui.gameObject.scene);
            if (ui.GetComponents<Component>().Any(component => component != ui && component != ui.transform))
                throw new InvalidOperationException("The UI root contains another component. Move custom components outside this generated root before rebuilding.");
            bool foundCanvas = false, foundStage = false;
            foreach (Transform child in ui.transform)
            {
                if (child.name == "GPGP UI Canvas" && !foundCanvas) foundCanvas = true;
                else if (child.name == "Featured Debris Stage" && !foundStage) foundStage = true;
                else throw new InvalidOperationException("The UI root contains an unrecognized or duplicate child. Keep custom scene objects outside its generated hierarchy.");
            }
            if (float.IsNaN(ui.foregroundDepthMeters) || float.IsInfinity(ui.foregroundDepthMeters) || ui.foregroundDepthMeters <= 0f ||
                float.IsNaN(ui.overviewIntervalSeconds) || float.IsInfinity(ui.overviewIntervalSeconds) || ui.overviewIntervalSeconds <= 0f)
                throw new InvalidOperationException("Use positive, finite foreground depth and Overview interval values.");
            if (ui.turtleModel && !EditorUtility.IsPersistent(ui.turtleModel) &&
                (ui.turtleModel.scene != ui.gameObject.scene || ui.turtleModel.transform.IsChildOf(ui.transform)))
                throw new InvalidOperationException("Use a turtle prefab or an existing turtle outside the UI's owned hierarchy.");
            return Create(ui.exhibit, ui.cameraSetup, ui.ccHub, ui.sourceScreen, ui.turtleModel,
                ui.font, ui.foregroundDepthMeters, ui.overviewIntervalSeconds, ui);
        }

        static GPGPExhibitUI Create(GPGPLayeredCubeVisualization exhibit, CC_CAMERA_SETUP setup,
            Transform hub, Transform screen, GameObject turtle, TMP_FontAsset font,
            float foregroundDepth, float overviewInterval, GPGPExhibitUI previous)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            string action = previous ? "Rebuild GPGP UI preview" : "Add GPGP stereo UI";
            Undo.SetCurrentGroupName(action);
            GameObject root = null;
            bool registered = false;
            try
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, exhibit.gameObject.scene);
                root.transform.SetParent(hub, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
                root.transform.localScale = Vector3.one;
                var ui = root.AddComponent<GPGPExhibitUI>();
                ui.exhibit = exhibit;
                ui.cameraSetup = setup;
                ui.ccHub = hub;
                ui.sourceScreen = screen;
                ui.turtleModel = turtle;
                ui.font = font;
                ui.foregroundDepthMeters = foregroundDepth;
                ui.overviewIntervalSeconds = overviewInterval;
                ui.BuildPreview();
                if (previous)
                {
                    ui.enabled = previous.enabled;
                    root.SetActive(previous.gameObject.activeSelf);
                }
                // Register the complete new hierarchy so Undo/Redo includes its generated children.
                Undo.RegisterCreatedObjectUndo(root, action);
                registered = true;
                if (previous) Undo.DestroyObjectImmediate(previous.gameObject);
                EditorUtility.SetDirty(ui);
                EditorSceneManager.MarkSceneDirty(exhibit.gameObject.scene);
                Undo.CollapseUndoOperations(group);
                Selection.activeGameObject = root;
                Debug.Log("GPGP_UI: Preview added to both Cyber Canoe eyes. Save the scene to keep it; enter Play mode for D-pad/Start controls and Select/Tab to hide or restore the UI.", ui);
                return ui;
            }
            catch
            {
                if (root && !registered) Object.DestroyImmediate(root);
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        static void ValidateExhibit(GPGPLayeredCubeVisualization exhibit)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before authoring the GPGP UI.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Close Prefab mode before authoring the scene UI.");
            if (!exhibit) throw new InvalidOperationException("Select a plastic-type exhibit, or keep exactly one in the current scene.");
            Scene scene = exhibit.gameObject.scene;
            if (EditorUtility.IsPersistent(exhibit) || !scene.IsValid() || !scene.isLoaded || scene != SceneManager.GetActiveScene())
                throw new InvalidOperationException("Choose an exhibit in the current active scene.");
            if (!exhibit.bakedIntoScene || exhibit.debrisModelGroups == null || exhibit.debrisModelGroups.Length != 4 ||
                exhibit.debrisModelGroups.Any(pool => pool?.models == null || !pool.models.Any(model => model)))
                throw new InvalidOperationException("Bake the exhibit and apply updated debris pools before adding its UI.");
        }

        static GPGPLayeredCubeVisualization FindExhibit()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject selected = Selection.activeGameObject;
            if (selected && selected.scene == scene)
            {
                var exhibit = selected.GetComponentInParent<GPGPLayeredCubeVisualization>(true);
                if (exhibit) return exhibit;
                var ui = selected.GetComponentInParent<GPGPExhibitUI>(true);
                if (ui && ui.exhibit && ui.exhibit.gameObject.scene == scene) return ui.exhibit;
            }
            var exhibits = Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include)
                .Where(candidate => candidate.gameObject.scene == scene).ToArray();
            return exhibits.Length == 1 ? exhibits[0] : null;
        }

        static void FindRig(Scene scene, out CC_CAMERA_SETUP setup, out Transform hub, out Transform screen)
        {
            setup = null;
            hub = null;
            screen = null;
            foreach (var candidate in Object.FindObjectsByType<CC_CAMERA_SETUP>(FindObjectsInactive.Include))
            {
                if (candidate.gameObject.scene != scene || !candidate.isActiveAndEnabled || !candidate.CCSettings ||
                    !candidate.leftCamera || !candidate.rightCamera || candidate.leftCamera == candidate.rightCamera) continue;
                var left = candidate.leftCamera.GetComponent<CC_CAMERAOFFSET>();
                var right = candidate.rightCamera.GetComponent<CC_CAMERAOFFSET>();
                if (!candidate.leftCamera.GetComponent<Camera>() || !candidate.rightCamera.GetComponent<Camera>() ||
                    !left || !right || !left.projectionScreen || left.projectionScreen != right.projectionScreen) continue;
                Transform ancestor = candidate.transform.parent;
                while (ancestor && ancestor.name != "CC_HUB") ancestor = ancestor.parent;
                if (!ancestor) continue;
                if (setup) throw new InvalidOperationException("The current scene contains multiple active Cyber Canoe stereo rigs. Keep one active before adding the UI.");
                setup = candidate;
                hub = ancestor;
                screen = left.projectionScreen.transform;
            }
            ValidateRig(setup, hub, screen, scene);
        }

        static void ValidateRig(CC_CAMERA_SETUP setup, Transform hub, Transform screen, Scene scene)
        {
            if (!setup || !hub || !screen || !setup.CCSettings || !setup.isActiveAndEnabled ||
                setup.gameObject.scene != scene || hub.gameObject.scene != scene || screen.gameObject.scene != scene ||
                !setup.transform.IsChildOf(hub) || !screen.IsChildOf(hub) ||
                !setup.leftCamera || !setup.rightCamera || setup.leftCamera == setup.rightCamera)
                throw new InvalidOperationException("Use an active CC_HEAD stereo setup with its projection screen and CC_HUB in the exhibit's scene.");
            foreach (GameObject eye in new[] { setup.leftCamera, setup.rightCamera })
            {
                var camera = eye.GetComponent<Camera>();
                var offset = eye.GetComponent<CC_CAMERAOFFSET>();
                if (eye.scene != scene || !eye.transform.IsChildOf(setup.transform) || !camera || !offset ||
                    !offset.projectionScreen || offset.projectionScreen.transform != screen || (camera.cullingMask & 1) == 0)
                    throw new InvalidOperationException("Both content cameras must project onto the same source screen and render the Default layer used by the UI.");
            }
            Vector3 rightEdge = screen.TransformVector(Vector3.right);
            Vector3 upEdge = screen.TransformVector(Vector3.up);
            if ((hub.lossyScale - Vector3.one).sqrMagnitude > .000001f || rightEdge.magnitude < .001f ||
                upEdge.magnitude < .001f || Mathf.Abs(Vector3.Dot(rightEdge.normalized, upEdge.normalized)) > .001f)
                throw new InvalidOperationException("Use a unit-scale CC_HUB and a nonzero rectangular projection screen for metre-based UI depth.");
        }

        static void ValidateOwnedRoot(GPGPExhibitUI ui)
        {
            if (!ui || EditorUtility.IsPersistent(ui) || ui.gameObject.name != RootName || !ui.ccHub ||
                ui.transform.parent != ui.ccHub || !ui.gameObject.scene.IsValid() || !ui.gameObject.scene.isLoaded ||
                ui.gameObject.scene != SceneManager.GetActiveScene())
                throw new InvalidOperationException("Rebuild only the tool's GPGP Stereo UI root directly beneath its assigned CC_HUB.");
        }

        static GameObject FindTurtle(GPGPLayeredCubeVisualization exhibit)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TurtlePath);
            if (prefab) return prefab;
            Transform turtle = exhibit.transform.Find("WaterCube - one continuous volume/Sea Turtle");
            return turtle && turtle.GetComponentInChildren<Renderer>(true) ? turtle.gameObject : null;
        }
    }

    [CustomEditor(typeof(GPGPExhibitUI))]
    public sealed class GPGPExhibitUIEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("D-pad moves focus; Start activates it. Select/Back/View or Tab slides the panels away or restores them. Hide UI is also in the header; Start restores the hidden UI. Your selection is kept, and the debris box and cameras stay in place. Rebuild Preview replaces this UI's generated content. Save the scene afterward.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode ||
                PrefabStageUtility.GetCurrentPrefabStage() != null || EditorUtility.IsPersistent(target)))
                if (GUILayout.Button("Rebuild Preview", GUILayout.Height(30)))
                    GPGPExhibitUIAuthoring.RebuildPreview((GPGPExhibitUI)target);
        }
    }
}
#endif
