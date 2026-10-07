#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Refreshes the exhibit's debris, wildlife and existing presentation lighting together.</summary>
public static class GPGPExhibitContentAuthoring
{
    const string ActionName = "Apply Updated Debris + Turtle";
    const string MenuPath = "Tools/Pacific Exhibit/" + ActionName;
    const string PresentationName = "Pacific Exhibit Presentation";

    [MenuItem(MenuPath)]
    public static void ApplyToCurrentScene()
    {
        GPGPLayeredCubeVisualization exhibit = FindCurrentExhibit();
        if (!exhibit)
            throw new InvalidOperationException("Select a baked plastic-type water cube in the current scene, or keep exactly one in that scene.");
        Apply(exhibit);
    }

    [MenuItem(MenuPath, true)]
    static bool CanApplyToCurrentScene() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        PrefabStageUtility.GetCurrentPrefabStage() == null && FindCurrentExhibit();

    static GPGPLayeredCubeVisualization FindCurrentExhibit()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return null;
        GameObject selected = Selection.activeGameObject;
        if (selected && selected.scene == scene)
        {
            var selectedExhibit = selected.GetComponentInParent<GPGPLayeredCubeVisualization>(true);
            if (selectedExhibit && selectedExhibit.bakedIntoScene) return selectedExhibit;
        }
        var exhibits = UnityEngine.Object.FindObjectsByType<GPGPLayeredCubeVisualization>(FindObjectsInactive.Include)
            .Where(candidate => candidate.gameObject.scene == scene).ToArray();
        return exhibits.Length == 1 && exhibits[0].bakedIntoScene ? exhibits[0] : null;
    }

    public static void Apply(GPGPLayeredCubeVisualization exhibit)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before updating the exhibit.");
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Close Prefab mode before updating the scene exhibit.");
        if (!exhibit) throw new ArgumentNullException(nameof(exhibit));
        Scene scene = exhibit.gameObject.scene;
        if (EditorUtility.IsPersistent(exhibit) || !scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("Choose a water cube in a loaded scene.");
        if (!exhibit.bakedIntoScene)
            throw new InvalidOperationException("Bake the water cube before applying updated debris and the turtle.");

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(ActionName);
        Undo.RecordObject(exhibit, ActionName);
        try
        {
            GPGPDebrisModelAuthoring.Populate(exhibit);
            GPGPSeaTurtleAuthoring.Ensure(exhibit);
            if (exhibit.transform.Find(PresentationName)) PlasticTypePresentation.BalanceDebrisLighting(exhibit);
            EditorUtility.SetDirty(exhibit);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = exhibit.gameObject;
            Debug.Log("GPGP_EXHIBIT_CONTENT: Updated debris and turtle, and balanced existing presentation lighting. Save the scene to keep these changes.", exhibit);
        }
        finally
        {
            Undo.SetCurrentGroupName(ActionName);
            Undo.CollapseUndoOperations(undoGroup);
        }
    }
}
#endif
