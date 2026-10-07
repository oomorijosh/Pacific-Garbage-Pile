#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Prepares the licensed sea turtle and places it independently of the debris bands.</summary>
public static class GPGPSeaTurtleAuthoring
{
    const string AssetRoot = "Assets/Wildlife/SeaTurtle";
    const string PreparedRoot = AssetRoot + "/Prepared";
    const string GeneratedRootName = "WaterCube - one continuous volume";
    public const string TurtleObjectName = "Sea Turtle";
    public const string PreparedPrefabPath = PreparedRoot + "/SeaTurtle.prefab";

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
        Debug.Log("GPGP_SEA_TURTLE: Added a matte sea turtle in the upper water band; attribution is in " +
            AssetRoot + "/ATTRIBUTION.txt.", turtle);
        return turtle;
    }

    static GameObject PreparePrefab()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/SeaTurtle.obj");
        if (!source) throw new InvalidOperationException("The licensed sea turtle OBJ is missing.");
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("The URP Lit shader is unavailable.");
        EnsureFolder(PreparedRoot);
        EnsureFolder(PreparedRoot + "/Materials");
        // glTF baseColorFactor values are linear. Unity material colors are
        // authored in sRGB, so preserve the source colors with a gamma conversion.
        var materials = new Dictionary<string, Material>(StringComparer.Ordinal)
        {
            { "Mat", MatteMaterial("Eyes", new Color(.06979f, .14f, .0686f).gamma, shader) },
            { "Brown", MatteMaterial("Shell", new Color(.58f, .386667f, 0f).gamma, shader) },
            { "WhiteShell", MatteMaterial("Pale", new Color(.73458f, .77f, .5929f).gamma, shader) },
            { "GreenSkin", MatteMaterial("Skin", new Color(.43741f, .62f, .0434f).gamma, shader) }
        };

        var prepared = new GameObject(TurtleObjectName);
        try
        {
            GameObject model = UnityEngine.Object.Instantiate(source, prepared.transform, false);
            model.name = "Turtle model";
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int slot = 0; slot < slots.Length; slot++)
                {
                    string sourceName = slots[slot] ? slots[slot].name : string.Empty;
                    if (!materials.TryGetValue(sourceName, out Material replacement))
                        throw new InvalidOperationException("Unexpected turtle material slot: " + sourceName + ".");
                    slots[slot] = replacement;
                }
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            if (!TryGetBounds(prepared.transform, out Bounds bounds))
                throw new InvalidOperationException("The sea turtle OBJ has no mesh geometry.");
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

    static Material MatteMaterial(string label, Color color, Shader shader)
    {
        string path = PreparedRoot + "/Materials/SeaTurtle_" + label + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(shader) { name = "SeaTurtle_" + label };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", .12f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_EnvironmentReflections", 0f);
        material.SetColor("_EmissionColor", Color.black);
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.DisableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh) continue;
            Bounds local = filter.sharedMesh.bounds;
            Matrix4x4 toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
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
        return found;
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
