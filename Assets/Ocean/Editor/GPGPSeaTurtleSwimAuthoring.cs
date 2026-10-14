#if UNITY_EDITOR
using System;
using System.Linq;
using GPGPUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Attaches route movement only to the existing scene turtle, leaving its prepared UI prefab alone.</summary>
public static class GPGPSeaTurtleSwimAuthoring
{
    const string MenuPath = "Tools/Pacific Exhibit/Add or Update Turtle Swim Route";
    const string GeneratedRootName = "WaterCube - one continuous volume";

    [MenuItem(MenuPath)]
    public static void AddToCurrentExhibit()
    {
        Scene scene = SceneManager.GetActiveScene();
        var exhibit = UnityEngine.Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene);
        if (!exhibit) throw new InvalidOperationException("The current scene has no plastic-type exhibit.");
        GPGPSeaTurtleSwimmer swimmer = TryAddOrUpdate(exhibit);
        if (!swimmer) throw new InvalidOperationException("Assign a Cyber Canoe stereo rig and projection screen before adding the turtle route.");
        Selection.activeGameObject = swimmer.gameObject;
    }

    [MenuItem(MenuPath, true)]
    static bool CanAdd() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    /// <summary>Convenience for the existing turtle authoring flow; scenes without a rig remain supported.</summary>
    public static GPGPSeaTurtleSwimmer TryAddOrUpdate(GPGPLayeredCubeVisualization exhibit)
    {
        if (!exhibit) return null;
        Scene scene = exhibit.gameObject.scene;
        var ui = UnityEngine.Object.FindObjectsByType<GPGPExhibitUI>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene && candidate.exhibit == exhibit);
        CC_CAMERA_SETUP setup = ui ? ui.cameraSetup : null;
        if (!setup) setup = UnityEngine.Object.FindObjectsByType<CC_CAMERA_SETUP>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene);
        Transform screen = ui ? ui.sourceScreen : null;
        if (!screen && setup && setup.leftCamera)
        {
            var offset = setup.leftCamera.GetComponent<CC_CAMERAOFFSET>();
            if (offset && offset.projectionScreen) screen = offset.projectionScreen.transform;
        }
        if (!setup || !screen || !setup.leftCamera || !setup.rightCamera) return null;
        return AddOrUpdate(exhibit, setup, screen, ui);
    }

    public static GPGPSeaTurtleSwimmer AddOrUpdate(GPGPLayeredCubeVisualization exhibit,
        CC_CAMERA_SETUP cameraSetup, Transform projectionScreen, GPGPExhibitUI exhibitUI = null)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Stop Play mode and close Prefab mode before authoring the turtle route.");
        if (!exhibit || !cameraSetup || !projectionScreen || !cameraSetup.leftCamera || !cameraSetup.rightCamera)
            throw new ArgumentException("Assign the exhibit, both stereo eyes, and their projection screen.");
        Scene scene = exhibit.gameObject.scene;
        if (EditorUtility.IsPersistent(exhibit) || !scene.IsValid() || !scene.isLoaded ||
            cameraSetup.gameObject.scene != scene || projectionScreen.gameObject.scene != scene)
            throw new InvalidOperationException("The exhibit and camera references must belong to the same loaded scene.");
        Transform generated = exhibit.transform.Find(GeneratedRootName);
        Transform turtle = generated ? generated.Find(GPGPSeaTurtleAuthoring.TurtleObjectName) : null;
        var water = generated ? generated.GetComponentInChildren<GPGPWaterCompartment>(true) : null;
        if (!turtle || !water)
            throw new InvalidOperationException("Add the sea turtle to the baked water cube before adding its swimming route.");
        var swimmer = turtle.GetComponent<GPGPSeaTurtleSwimmer>();
        if (!swimmer) swimmer = Undo.AddComponent<GPGPSeaTurtleSwimmer>(turtle.gameObject);
        Undo.RecordObject(swimmer, "Update turtle swim route");
        swimmer.water = water;
        swimmer.cameraSetup = cameraSetup;
        swimmer.projectionScreen = projectionScreen;
        swimmer.exhibitUI = exhibitUI;
        swimmer.SetAnimatedModelBounds(ReadAnimatedBounds(turtle));
        EditorUtility.SetDirty(swimmer);
        EditorSceneManager.MarkSceneDirty(scene);
        return swimmer;
    }

    static Bounds ReadAnimatedBounds(Transform original)
    {
        // Sampling a disposable clone preserves every original bone transform,
        // Animator state, renderer bound, material and prepared prefab byte.
        GameObject clone = UnityEngine.Object.Instantiate(original.gameObject, original.parent, false);
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.transform.localPosition = original.localPosition;
        clone.transform.localRotation = original.localRotation;
        clone.transform.localScale = original.localScale;
        var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var animators = clone.GetComponentsInChildren<Animator>(true);
            foreach (Animator animator in animators) animator.enabled = false;
            foreach (GPGPSeaTurtleSwimmer movement in clone.GetComponentsInChildren<GPGPSeaTurtleSwimmer>(true))
                movement.enabled = false;
            var renderers = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Bounds bounds = new Bounds();
            bool found = false;
            for (int frame = 0; frame < 32; frame++)
            {
                foreach (Animator animator in animators)
                {
                    if (!animator.runtimeAnimatorController) continue;
                    AnimationClip clip = animator.runtimeAnimatorController.animationClips
                        .FirstOrDefault(value => value.name == "Swim Cycle");
                    if (clip) clip.SampleAnimation(animator.gameObject, clip.length * frame / 32f);
                }
                foreach (SkinnedMeshRenderer renderer in renderers)
                {
                    if (!renderer.sharedMesh) continue;
                    baked.Clear();
                    // For this imported hierarchy, true agrees with independent
                    // bone/bind-pose skinning; false duplicates ancestor scale.
                    renderer.BakeMesh(baked, true);
                    Matrix4x4 toRoot = clone.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in baked.vertices)
                    {
                        Vector3 point = toRoot.MultiplyPoint3x4(vertex);
                        if (found) bounds.Encapsulate(point);
                        else { bounds = new Bounds(point, Vector3.zero); found = true; }
                    }
                }
                foreach (MeshFilter filter in clone.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh) IncludeBounds(filter.sharedMesh.bounds, filter.transform, clone.transform,
                        ref bounds, ref found);
            }
            if (!found || bounds.size.sqrMagnitude < .000001f)
                throw new InvalidOperationException("The turtle has no valid animated geometry bounds.");
            // Reserve a small interval between sampled phases as flippers turn.
            bounds.Expand(.04f);
            return bounds;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(baked);
            UnityEngine.Object.DestroyImmediate(clone);
        }
    }

    static void IncludeBounds(Bounds source, Transform sourceTransform, Transform root,
        ref Bounds bounds, ref bool found)
    {
        Matrix4x4 matrix = root.worldToLocalMatrix * sourceTransform.localToWorldMatrix;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = Vector3.Scale(source.extents, new Vector3((corner & 1) == 0 ? -1f : 1f,
                (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
            Vector3 point = matrix.MultiplyPoint3x4(source.center + offset);
            if (found) bounds.Encapsulate(point);
            else { bounds = new Bounds(point, Vector3.zero); found = true; }
        }
    }
}
#endif
