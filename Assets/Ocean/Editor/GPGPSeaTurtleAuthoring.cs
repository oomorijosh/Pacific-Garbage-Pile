#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Prepares the swimming hawksbill turtle and places it independently of the debris bands.</summary>
public static class GPGPSeaTurtleAuthoring
{
    const string AssetRoot = "Assets/Wildlife/SeaTurtle";
    const string SourceModelPath = "Assets/Debris/model_50a_-_hawksbill_sea_turtle.glb";
    const string PreparedRoot = AssetRoot + "/Prepared";
    const string GeneratedRootName = "WaterCube - one continuous volume";
    public const string TurtleObjectName = "Sea Turtle";
    public const string PreparedPrefabPath = PreparedRoot + "/SeaTurtle.prefab";
    const string SwimControllerPath = PreparedRoot + "/SeaTurtle.controller";

    [MenuItem("Tools/Pacific Exhibit/Add or Refresh Sea Turtle")]
    public static void AddOrRefreshCurrentExhibit()
    {
        Scene scene = SceneManager.GetActiveScene();
        var exhibit = UnityEngine.Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene);
        if (!exhibit) throw new InvalidOperationException("The current scene has no plastic-type water cube.");
        Selection.activeGameObject = Ensure(exhibit);
    }

    [MenuItem("Tools/Pacific Exhibit/Add or Refresh Sea Turtle", true)]
    static bool CanAddOrRefresh() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    public static GameObject Ensure(GPGPLayeredCubeVisualization exhibit)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before adding the sea turtle.");
        if (!exhibit) throw new ArgumentNullException(nameof(exhibit));
        Transform generated = exhibit.transform.Find(GeneratedRootName);
        var water = generated ? generated.GetComponentInChildren<GPGPWaterCompartment>() : null;
        if (!generated || !water)
            throw new InvalidOperationException("Bake the water cube before adding the sea turtle.");

        // Prepare assets before modifying the scene. The wildlife branch stays
        // outside the generated debris hierarchy, so debris rebakes preserve it.
        GameObject prefab = PreparePrefab();
        Vector3 size = water.size;
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            throw new InvalidOperationException("The water volume must have positive dimensions.");
        float length = Mathf.Min(1.6f, Mathf.Min(size.x, size.z) * .3f, size.y * .4f);
        Quaternion rotation = Quaternion.Euler(-4f, 180f, -8f);
        if (!TryGetBounds(prefab.transform, out Bounds prefabBounds))
            throw new InvalidOperationException("The prepared sea turtle has no mesh bounds.");
        Vector3 extent = RotatedExtent(prefabBounds.extents, rotation) * length;
        Vector3 half = size * .5f;
        float padding = Mathf.Min(.04f, size.y * .01f);
        // Mega begins above the cumulative Micro/Meso/Macro mass-share bands.
        const float gap = .025f;
        float megaBottom = -half.y + size.y * (.46f * (1f - gap * 3f) + gap * 3f);
        Vector3 position = new Vector3(-size.x * .08f, size.y * .19f, -size.z * .25f);
        position.x = Mathf.Clamp(position.x, -half.x + extent.x + padding, half.x - extent.x - padding);
        position.y = Mathf.Clamp(position.y, megaBottom + extent.y + padding, half.y - extent.y - padding);
        position.z = Mathf.Clamp(position.z, -half.z + extent.z + padding, half.z - extent.z - padding);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add or refresh sea turtle");
        Transform previous = generated.Find(TurtleObjectName);
        if (previous) Undo.DestroyObjectImmediate(previous.gameObject);
        // Expanded scene objects match the exhibit's baked debris workflow.
        GameObject turtle = UnityEngine.Object.Instantiate(prefab, generated, false);
        turtle.name = TurtleObjectName;
        turtle.transform.localPosition = position;
        turtle.transform.localRotation = rotation;
        turtle.transform.localScale = Vector3.one * length;
        Undo.RegisterCreatedObjectUndo(turtle, "Create sea turtle");
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(exhibit.gameObject.scene);
        Debug.Log("GPGP_SEA_TURTLE: Added a swimming hawksbill turtle in the upper water band; asset notes are in " +
            AssetRoot + "/ATTRIBUTION.txt.", turtle);
        return turtle;
    }

    static GameObject PreparePrefab()
    {
        AssetImporter importer = AssetImporter.GetAtPath(SourceModelPath);
        if (!importer) throw new InvalidOperationException("The hawksbill turtle GLB is missing: " + SourceModelPath);
        bool reimport;
        using (var settings = new SerializedObject(importer))
        {
            SerializedProperty animationMethod = settings.FindProperty("importSettings.animationMethod");
            if (animationMethod == null)
                throw new InvalidOperationException("The hawksbill turtle GLB requires the Unity glTFast importer with Mecanim animation support.");
            reimport = animationMethod.intValue != (int)GLTFast.AnimationMethod.Mecanim;
            if (reimport)
            {
                animationMethod.intValue = (int)GLTFast.AnimationMethod.Mecanim;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        if (reimport) importer.SaveAndReimport();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModelPath);
        if (!source) throw new InvalidOperationException("Import the hawksbill turtle GLB before preparing the sea turtle: " + SourceModelPath);
        EnsureFolder(PreparedRoot);
        AnimationClip swimClip = AssetDatabase.LoadAllAssetsAtPath(SourceModelPath).OfType<AnimationClip>()
            .FirstOrDefault(clip => clip.name == "Swim Cycle");
        if (!swimClip) throw new InvalidOperationException("The imported hawksbill turtle has no Swim Cycle animation clip.");
        if (swimClip.legacy || !swimClip.isLooping)
            throw new InvalidOperationException("The hawksbill turtle Swim Cycle must import as a looping Mecanim clip. Reimport the GLB with Animation Method set to Mecanim.");
        AnimatorController controller = PrepareSwimController(swimClip);

        var prepared = new GameObject(TurtleObjectName);
        try
        {
            GameObject model = UnityEngine.Object.Instantiate(source, prepared.transform, false);
            model.name = "Turtle model";
            // The source faces -Z after its Z-up conversion. Use Unity's +Z forward
            // on the wrapper, leaving the imported joints and animation paths intact.
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                // Keep the imported textured materials and bone bindings intact.
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            Animator[] animators = model.GetComponentsInChildren<Animator>(true);
            if (animators.Length == 0) throw new InvalidOperationException("The imported hawksbill turtle has no Animator.");
            foreach (Animator animator in animators)
            {
                // glTFast binds curves relative to this imported Animator's object.
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.enabled = true;
            }
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            if (!TryGetBounds(prepared.transform, out Bounds bounds))
                throw new InvalidOperationException("The hawksbill turtle GLB has no mesh geometry.");
            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (largest <= .0001f) throw new InvalidOperationException("The sea turtle mesh has invalid dimensions.");
            model.transform.localPosition = (model.transform.localPosition - bounds.center) / largest;
            model.transform.localScale /= largest;
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(prepared, PreparedPrefabPath);
            if (!prefab) throw new InvalidOperationException("The prepared sea turtle prefab could not be saved.");
            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(prepared);
        }
    }

    static AnimatorController PrepareSwimController(AnimationClip swimClip)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(SwimControllerPath);
        if (!controller) controller = AnimatorController.CreateAnimatorControllerAtPath(SwimControllerPath);
        if (controller.layers.Length == 0) controller.AddLayer("Base Layer");
        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState swim = machine.states.Select(child => child.state).FirstOrDefault(state => state.name == "Swim Cycle");
        if (!swim) swim = machine.AddState("Swim Cycle");
        swim.motion = swimClip;
        machine.defaultState = swim;
        EditorUtility.SetDirty(swim);
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        return controller;
    }

    static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh) continue;
            EncapsulateBounds(filter.sharedMesh.bounds, filter.transform, root, ref bounds, ref found);
        }
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!renderer.sharedMesh) continue;
            EncapsulateBounds(renderer.localBounds, renderer.transform, root, ref bounds, ref found);
        }
        return found;
    }

    static void EncapsulateBounds(Bounds local, Transform source, Transform root, ref Bounds bounds, ref bool found)
    {
        Matrix4x4 toRoot = root.worldToLocalMatrix * source.localToWorldMatrix;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 offset = new Vector3(
                (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                (corner & 4) == 0 ? -local.extents.z : local.extents.z);
            Vector3 point = toRoot.MultiplyPoint3x4(local.center + offset);
            if (found) bounds.Encapsulate(point);
            else { bounds = new Bounds(point, Vector3.zero); found = true; }
        }
    }

    static Vector3 RotatedExtent(Vector3 extent, Quaternion rotation)
    {
        Vector3 x = rotation * Vector3.right;
        Vector3 y = rotation * Vector3.up;
        Vector3 z = rotation * Vector3.forward;
        return new Vector3(
            Mathf.Abs(x.x) * extent.x + Mathf.Abs(y.x) * extent.y + Mathf.Abs(z.x) * extent.z,
            Mathf.Abs(x.y) * extent.x + Mathf.Abs(y.y) * extent.y + Mathf.Abs(z.y) * extent.z,
            Mathf.Abs(x.z) * extent.x + Mathf.Abs(y.z) * extent.y + Mathf.Abs(z.z) * extent.z);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
}
#endif
