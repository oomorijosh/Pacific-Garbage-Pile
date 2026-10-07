#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Populates the baked debris bands from the project's category model folders.</summary>
public static class GPGPDebrisModelAuthoring
{
    static readonly string[] Categories = { "Microplastic", "Mesoplastic", "Macroplastic", "Megaplastic" };
    const string GeneratedRootName = "WaterCube - one continuous volume";
    const string BandsName = "Four mass-share debris bands";

    [MenuItem("Tools/Pacific Exhibit/Refresh Debris Pools + Rebuild")]
    public static void PopulateCurrentExhibit()
    {
        var scene = SceneManager.GetActiveScene();
        var exhibit = UnityEngine.Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene);
        if (!exhibit) throw new InvalidOperationException("The current scene has no plastic-type water cube.");
        Populate(exhibit);
        Selection.activeGameObject = exhibit.gameObject;
    }

    [MenuItem("Tools/Pacific Exhibit/Refresh Debris Pools + Rebuild", true)]
    static bool CanPopulate() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        PrefabStageUtility.GetCurrentPrefabStage() == null;

    public static void Populate(GPGPLayeredCubeVisualization exhibit)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before adding debris models.");
        Dictionary<GameObject, GameObject> prepared = GPGPDebrisTextureLibrary.PrepareModels();
        var groups = new GPGPLayeredCubeVisualization.DebrisModelGroup[Categories.Length];
        for (int category = 0; category < Categories.Length; category++)
        {
            var models = new List<GameObject>();
            var unique = new HashSet<GameObject>();
            Action<GameObject> add = model =>
            {
                if (!model) return;
                if (prepared.TryGetValue(model, out GameObject textured)) model = textured;
                if (unique.Add(model)) models.Add(model);
            };
            // Keep manually assigned prefabs, replacing known raw models with
            // their textured versions instead of adding duplicate pool entries.
            if (exhibit.debrisModelGroups != null && category < exhibit.debrisModelGroups.Length)
                foreach (GameObject model in exhibit.debrisModelGroups[category]?.models ?? Array.Empty<GameObject>()) add(model);
            string folder = "Assets/Debris/" + Categories[category];
            foreach (string path in AssetDatabase.FindAssets("t:GameObject", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal))
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model && (model.GetComponentInChildren<MeshFilter>(true) ||
                    model.GetComponentInChildren<SkinnedMeshRenderer>(true))) add(model);
            }
            foreach (GameObject model in GPGPDebrisTextureLibrary.AdditionalModelsForCategory(category)) add(model);
            if (models.Count == 0) throw new InvalidOperationException("No debris models found for " + Categories[category] + ".");
            groups[category] = new GPGPLayeredCubeVisualization.DebrisModelGroup { models = models.ToArray() };
        }
        Rebuild(exhibit, groups);
    }

    [MenuItem("Tools/Pacific Exhibit/Rebuild Debris From Current Pools")]
    public static void RebuildCurrentExhibit()
    {
        var scene = SceneManager.GetActiveScene();
        var exhibit = UnityEngine.Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene);
        if (!exhibit) throw new InvalidOperationException("The current scene has no plastic-type water cube.");
        RebuildAssigned(exhibit);
        Selection.activeGameObject = exhibit.gameObject;
    }

    [MenuItem("Tools/Pacific Exhibit/Rebuild Debris From Current Pools", true)]
    static bool CanRebuild() => CanPopulate();

    public static void RebuildAssigned(GPGPLayeredCubeVisualization exhibit)
    {
        Rebuild(exhibit, exhibit.debrisModelGroups);
    }

    static void Rebuild(GPGPLayeredCubeVisualization exhibit, GPGPLayeredCubeVisualization.DebrisModelGroup[] groups)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before rebuilding debris.");
        if (groups == null || groups.Length != Categories.Length)
            throw new InvalidOperationException("The exhibit needs Micro, Meso, Macro and Mega model pools in that order.");
        Transform generated = exhibit.transform.Find(GeneratedRootName);
        Transform bandTransform = generated ? generated.Find(BandsName) : null;
        var bands = bandTransform ? bandTransform.GetComponent<GPGPDebrisBands>() : null;
        var water = generated ? generated.GetComponentInChildren<GPGPWaterCompartment>() : null;
        if (!exhibit.bakedIntoScene || !bands || !water)
            throw new InvalidOperationException("Bake the water cube before adding models to its debris bands.");
        if (exhibit.debrisMaterials == null || exhibit.debrisMaterials.Length == 0 ||
            Array.Exists(exhibit.debrisMaterials, material => !material))
            throw new InvalidOperationException("Assign the exhibit's debris material palette first.");

        // Keep authored textures/colors while using shared matte copies for any
        // imported URP materials that still have emission or shiny highlights.
        // This also applies when rebuilding manually edited pools.
        groups = groups.Select((group, category) => new GPGPLayeredCubeVisualization.DebrisModelGroup
        {
            models = (group?.models ?? Array.Empty<GameObject>())
                .Select(model => GPGPDebrisTextureLibrary.PrepareMatteModel(model, category))
                .Distinct().ToArray()
        }).ToArray();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add debris models to water sections");
        Undo.RegisterCompleteObjectUndo(exhibit, "Assign category debris models");
        // Placements are rebuilt while their old transforms are destroyed.
        // Capture them immediately; a deferred property diff can retain the
        // new list after Undo restores the old hierarchy.
        Undo.RegisterCompleteObjectUndo(bands, "Rebuild category debris models");
        Undo.RecordObject(bands.transform, "Reset debris branch scale");
        Transform oldDebris = bands.transform.Find("Debris size exhibit");
        if (oldDebris) Undo.DestroyObjectImmediate(oldDebris.gameObject);
        exhibit.debrisModelGroups = groups;
        bands.debrisSizeMultiplier = exhibit.debrisSizeMultiplier;
        bands.Build(water.size, exhibit.debrisMaterials, bands.impostorMaterial,
            exhibit.bakedWaterMaterial, groups);
        Transform newDebris = bands.transform.Find("Debris size exhibit");
        if (!newDebris) throw new InvalidOperationException("The debris model hierarchy was not created.");
        Undo.RegisterCreatedObjectUndo(newDebris.gameObject, "Create category debris models");
        Undo.CollapseUndoOperations(undoGroup);
        EditorUtility.SetDirty(exhibit);
        EditorUtility.SetDirty(bands);
        EditorSceneManager.MarkSceneDirty(exhibit.gameObject.scene);
        for (int category = 0; category < Categories.Length; category++)
            Debug.Log("GPGP_DEBRIS_MODELS: " + Categories[category] + " uses " + (groups[category]?.models?.Length ?? 0) +
                " model assets; " + newDebris.GetChild(category).childCount + " pieces placed.", exhibit);
    }
}
#endif
