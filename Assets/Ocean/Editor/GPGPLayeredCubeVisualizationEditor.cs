#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(GPGPLayeredCubeVisualization))]
public sealed class GPGPLayeredCubeVisualizationEditor : Editor
{
    const string BakedMaterialPath = "Assets/Ocean/Materials/MAT_GPGP_Water_Continuous_Baked.mat";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var exhibit = (GPGPLayeredCubeVisualization)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Scene Authoring", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(exhibit.bakedIntoScene
            ? "The exhibit is baked into the scene and can be expanded and edited in the Hierarchy."
            : "Bake once to create visible, editable scene objects without entering Play Mode.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            string bakeLabel = exhibit.bakedIntoScene ? "Rebuild Baked Exhibit" : "Bake Exhibit Into Scene";
            if (GUILayout.Button(bakeLabel, GUILayout.Height(34))) Bake(exhibit);

            using (new EditorGUI.DisabledScope(!exhibit.bakedIntoScene))
            {
                if (GUILayout.Button("Clear Baked Exhibit", GUILayout.Height(26))) Clear(exhibit);
            }
        }

        if (Application.isPlaying)
            EditorGUILayout.HelpBox("Stop Play Mode before baking or clearing the exhibit.", MessageType.Warning);
    }

    static void Bake(GPGPLayeredCubeVisualization exhibit)
    {
        if (exhibit.waterMaterials == null || exhibit.waterMaterials.Length != 4 || !exhibit.waterMaterials[3])
        {
            EditorUtility.DisplayDialog("Cannot bake GPGP exhibit",
                "Assign all four water materials first.", "OK");
            return;
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(BakedMaterialPath);
        if (!material)
        {
            material = new Material(exhibit.waterMaterials[3])
            {
                name = "MAT_GPGP_Water_Continuous_Baked"
            };
            AssetDatabase.CreateAsset(material, BakedMaterialPath);
        }

        Undo.RegisterFullObjectHierarchyUndo(exhibit.gameObject, "Bake GPGP exhibit");
        exhibit.BakeIntoScene(material);
        EditorUtility.SetDirty(exhibit);
        EditorUtility.SetDirty(material);
        EditorSceneManager.MarkSceneDirty(exhibit.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = exhibit.gameObject;
    }

    static void Clear(GPGPLayeredCubeVisualization exhibit)
    {
        Undo.RegisterFullObjectHierarchyUndo(exhibit.gameObject, "Clear baked GPGP exhibit");
        exhibit.ClearBakedExhibit();
        EditorUtility.SetDirty(exhibit);
        EditorSceneManager.MarkSceneDirty(exhibit.gameObject.scene);
    }
}
#endif
