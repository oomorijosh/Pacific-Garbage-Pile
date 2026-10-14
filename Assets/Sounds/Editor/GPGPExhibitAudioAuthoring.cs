#if UNITY_EDITOR
using System;
using System.Linq;
using GPGPUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Adds an independent audio root without changing the camera rig, UI, or AudioListeners.</summary>
public static class GPGPExhibitAudioAuthoring
{
    public const string RootName = "GPGP Exhibit Audio";
    const string MenuPath = "Tools/Pacific Exhibit/Add or Update Exhibit Audio";
    const string MusicPath = "Assets/Sounds/Undertow.mp3";
    const string WavesPath = "Assets/Sounds/calm wave.mp3";

    [MenuItem(MenuPath)]
    public static void AddToCurrentScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        var ui = Object.FindObjectsByType<GPGPExhibitUI>(FindObjectsInactive.Include)
            .Where(value => value.gameObject.scene == scene).ToArray();
        var compartments = (ui.Length == 1 && ui[0].exhibit
                ? ui[0].exhibit.GetComponentsInChildren<GPGPWaterCompartment>(true)
                : Object.FindObjectsByType<GPGPWaterCompartment>(FindObjectsInactive.Include))
            .Where(value => value.gameObject.scene == scene).ToArray();
        var setups = Object.FindObjectsByType<CC_CAMERA_SETUP>(FindObjectsInactive.Include)
            .Where(value => value.gameObject.scene == scene).ToArray();
        Transform head = ui.Length == 1 && ui[0].cameraSetup
            ? ui[0].cameraSetup.transform : setups.Length == 1 ? setups[0].transform : null;
        if (compartments.Length != 1 || !head)
            throw new InvalidOperationException("Keep one continuous exhibit water compartment and one Cyber Canoe head in the scene, or call AddOrUpdate with explicit references.");
        var audio = AddOrUpdate(compartments[0], head);
        Selection.activeGameObject = audio.gameObject;
        Debug.Log("GPGP_AUDIO: Both tracks will loop in Play mode. Music stays quiet; waves follow the navigating head from silence at the bottom to maximum at the surface. Save the scene to keep this setup.", audio);
    }

    [MenuItem(MenuPath, true)]
    static bool CanAdd() => !EditorApplication.isPlayingOrWillChangePlaymode &&
        PrefabStageUtility.GetCurrentPrefabStage() == null && SceneManager.GetActiveScene().IsValid() &&
        SceneManager.GetActiveScene().isLoaded;

    public static GPGPExhibitAudio AddOrUpdate(GPGPWaterCompartment water, Transform ccHead)
    {
        if (!CanAdd() || !water || !ccHead || EditorUtility.IsPersistent(water) || EditorUtility.IsPersistent(ccHead))
            throw new InvalidOperationException("Assign a scene water compartment and Cyber Canoe head outside Play mode and Prefab mode.");
        Scene scene = SceneManager.GetActiveScene();
        if (water.gameObject.scene != scene || ccHead.gameObject.scene != scene)
            throw new InvalidOperationException("Both audio references must belong to the current active scene.");
        var music = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
        var waves = AssetDatabase.LoadAssetAtPath<AudioClip>(WavesPath);
        if (!music || !waves)
            throw new InvalidOperationException("Import Assets/Sounds/Undertow.mp3 and Assets/Sounds/calm wave.mp3 before adding exhibit audio.");
        var existing = Object.FindObjectsByType<GPGPExhibitAudio>(FindObjectsInactive.Include)
            .Where(value => value.gameObject.scene == scene).ToArray();
        if (existing.Length > 1)
            throw new InvalidOperationException("More than one GPGP exhibit audio root exists; choose one before updating.");
        GPGPExhibitAudio audio = existing.Length == 1 ? existing[0] : null;
        if (audio && (audio.name != RootName || audio.transform.parent))
            throw new InvalidOperationException("Keep the generated GPGP Exhibit Audio object at the scene root, outside the UI and navigation rig.");
        if (!audio && scene.GetRootGameObjects().Any(value => value.name == RootName))
            throw new InvalidOperationException("An unrelated scene object uses the exhibit audio root name and has been preserved.");

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        const string action = "Add or update GPGP exhibit audio";
        Undo.SetCurrentGroupName(action);
        GameObject newRoot = null;
        bool registered = false;
        Transform[] previousChildren = audio ? audio.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
        try
        {
            if (!audio)
            {
                newRoot = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(newRoot, scene);
                audio = newRoot.AddComponent<GPGPExhibitAudio>();
            }
            else
            {
                Undo.RegisterFullObjectHierarchyUndo(audio.gameObject, action);
            }
            audio.water = water;
            audio.ccHeadTransform = ccHead;
            audio.musicClip = music;
            audio.wavesClip = waves;
            audio.PrepareSources();
            if (!newRoot)
                foreach (Transform child in new[] { audio.MusicSource.transform, audio.WavesSource.transform })
                    if (!previousChildren.Contains(child)) Undo.RegisterCreatedObjectUndo(child.gameObject, action);
            if (newRoot)
            {
                Undo.RegisterCreatedObjectUndo(newRoot, action);
                registered = true;
            }
            EditorUtility.SetDirty(audio);
            EditorUtility.SetDirty(audio.MusicSource);
            EditorUtility.SetDirty(audio.WavesSource);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(group);
            return audio;
        }
        catch
        {
            if (newRoot && !registered) Object.DestroyImmediate(newRoot);
            Undo.RevertAllDownToGroup(group);
            throw;
        }
    }
}

[CustomEditor(typeof(GPGPExhibitAudio))]
public sealed class GPGPExhibitAudioEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var audio = (GPGPExhibitAudio)target;
        EditorGUILayout.HelpBox("The two stereo loops keep playing across X/B navigation and hidden UI. Waves use actual CC_HEAD height inside the water volume, independent of the headset room camera. The lowest Micro band stays silent; waves fade up to Waves Max Volume at the surface. Music Volume is constant after its initial fade.", MessageType.Info);
        if (Application.isPlaying)
            EditorGUILayout.LabelField("Wave mix", $"Height {audio.NormalizedHeight:P0} / Target volume {audio.TargetWaveVolume:F3}");
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode ||
            PrefabStageUtility.GetCurrentPrefabStage() != null || EditorUtility.IsPersistent(audio)))
            if (GUILayout.Button("Update owned audio sources"))
                GPGPExhibitAudioAuthoring.AddOrUpdate(audio.water, audio.ccHeadTransform);
    }
}
#endif
