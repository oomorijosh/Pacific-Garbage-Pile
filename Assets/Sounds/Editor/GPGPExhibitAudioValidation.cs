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
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks and authors exhibit audio only in the isolated validation project.</summary>
public static class GPGPExhibitAudioValidation
{
    const string ScenePath = "Assets/Scenes/Plastic_Type.unity";
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly StringBuilder Report = new StringBuilder();
    static string project, reportPath;
    static GameObject fixtureRoot;
    static Dictionary<Object, string> protectedState;

    public static void BuildAndValidate()
    {
        try
        {
            project = Directory.GetParent(Application.dataPath).FullName;
            Require(project.Replace('\\', '/').EndsWith("/.utmp/pacific-exhibit-validation", StringComparison.OrdinalIgnoreCase),
                "Run this scene-writing validation only in .utmp/pacific-exhibit-validation.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Audio validation stays outside Play mode and XR.");
            Directory.CreateDirectory(Path.Combine(project, "Artifacts"));
            reportPath = Path.Combine(project, "Artifacts", "Exhibit-audio-check.txt");
            Report.Clear();
            Line("Exhibit audio validation; Unity " + Application.unityVersion + "; isolated editor, no Play mode/XR.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            string diskBefore = Hash(Path.Combine(project, ScenePath));
            var water = Object.FindObjectsByType<GPGPWaterCompartment>(FindObjectsInactive.Include)
                .Single(value => value.gameObject.scene == scene);
            var ui = Object.FindObjectsByType<GPGPExhibitUI>(FindObjectsInactive.Include)
                .Single(value => value.gameObject.scene == scene);
            Require(ui.cameraSetup, "The existing Cyber Canoe head must be referenced by the authored UI.");
            var previous = Object.FindObjectsByType<GPGPExhibitAudio>(FindObjectsInactive.Include)
                .SingleOrDefault(value => value.gameObject.scene == scene);
            protectedState = SnapshotProtected(scene, previous ? previous.transform : null);
            int listenerCount = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<AudioListener>(true).Length);
            int cameraCount = scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Camera>(true).Length);
            var audio = GPGPExhibitAudioAuthoring.AddOrUpdate(water, ui.cameraSetup.transform);
            Require(audio, "Audio authoring returned no component.");
            Require(audio.water == water && audio.ccHeadTransform == ui.cameraSetup.transform,
                "The mix must use the original water volume and Cyber Canoe head.");
            Require(!audio.transform.IsChildOf(ui.transform), "Exhibit sound must survive UI rebuilds and model/category changes.");
            ValidateClip(audio.musicClip, "Assets/Sounds/Undertow.mp3", 249.9f);
            ValidateClip(audio.wavesClip, "Assets/Sounds/calm wave.mp3", 179.7f);
            ValidateSources(audio);
            AudioSource music = audio.MusicSource, waves = audio.WavesSource;
            string settings = EditorJsonUtility.ToJson(audio);
            var second = GPGPExhibitAudioAuthoring.AddOrUpdate(water, ui.cameraSetup.transform);
            Require(second == audio && second.MusicSource == music && second.WavesSource == waves,
                "Repeated authoring duplicated the audio root or its sources.");
            Require(EditorJsonUtility.ToJson(audio) == settings, "Repeated authoring changed audio settings or references.");
            Require(Object.FindObjectsByType<GPGPExhibitAudio>(FindObjectsInactive.Include).Count(value => value.gameObject.scene == scene) == 1,
                "The exhibit has duplicate audio controllers.");
            Require(scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<AudioListener>(true).Length) == listenerCount &&
                scene.GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Camera>(true).Length) == cameraCount,
                "Audio authoring changed the original listener or camera count.");
            AssertProtected("After two authoring passes");
            Line("Authoring: one independent audio root, two reused looping stereo sources; existing rig, cameras, listener, water, debris and UI preserved across two passes.");
            ValidateMixFixture(scene, audio, ui);
            AssertProtected("After mix fixture cleanup");
            Require(Hash(Path.Combine(project, ScenePath)) == diskBefore, "Audio validation unexpectedly saved the scene before preservation checks.");
            Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save the isolated authored audio scene.");
            AssetDatabase.SaveAssets();
            scene = EditorSceneManager.OpenScene(ScenePath);
            audio = Object.FindObjectsByType<GPGPExhibitAudio>(FindObjectsInactive.Include).Single(value => value.gameObject.scene == scene);
            ValidateSources(audio);
            Require(audio.water && audio.ccHeadTransform && audio.ccHeadTransform.GetComponent<CC_CAMERA_SETUP>(),
                "Saved audio lost the real water or Cyber Canoe head reference.");
            Line("Save/reopen: MP3 references, two sources, volume tuning and original CC_HEAD/water references retained.");
            Line("RESULT PASS: both clips loop; waves rise continuously with actual head height, silence at the bottom, constant music gain, preserved source/playhead identity and no rig mutations.");
            WriteReport();
            Debug.Log("GPGP_EXHIBIT_AUDIO_VALIDATION: Passed.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            if (fixtureRoot) Object.DestroyImmediate(fixtureRoot);
            Line("RESULT FAIL: " + exception);
            WriteReport();
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    static void ValidateClip(AudioClip clip, string path, float length)
    {
        Require(clip && AssetDatabase.GetAssetPath(clip) == path, "Wrong or missing soundtrack clip: " + path);
        Require(clip.channels == 2 && Mathf.Abs(clip.length - length) < 1, "Unexpected clip channels or duration: " + path);
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        Require(importer && !importer.forceToMono && importer.loadInBackground &&
            importer.defaultSampleSettings.loadType == AudioClipLoadType.Streaming,
            "Long stereo clips must stream in the background: " + path);
        Line(path + ": " + clip.length.ToString("F3", CultureInfo.InvariantCulture) + " s, stereo, " + clip.frequency + " Hz; background streaming.");
    }

    static void ValidateSources(GPGPExhibitAudio audio)
    {
        AudioSource music = audio.MusicSource, waves = audio.WavesSource;
        Require(music && waves && music != waves && audio.GetComponentsInChildren<AudioSource>(true).Length == 2,
            "Exhibit needs exactly two separate audio sources.");
        Require(music.clip == audio.musicClip && waves.clip == audio.wavesClip, "A source has the wrong MP3.");
        foreach (AudioSource source in new[] { music, waves })
            Require(source.loop && !source.playOnAwake && source.spatialBlend == 0 && source.pitch == 1 &&
                source.transform.IsChildOf(audio.transform), "Invalid authored looping source: " + source.name);
        Require(audio.GetComponentsInChildren<AudioListener>(true).Length == 0 && audio.GetComponentsInChildren<Camera>(true).Length == 0,
            "Exhibit audio must use the existing rig listener.");
    }

    static void ValidateMixFixture(Scene scene, GPGPExhibitAudio authored, GPGPExhibitUI ui)
    {
        fixtureRoot = new GameObject("Temporary exhibit audio validation fixture");
        SceneManager.MoveGameObjectToScene(fixtureRoot, scene);
        var waterObject = new GameObject("Fixture water");
        waterObject.transform.SetParent(fixtureRoot.transform, false);
        var water = waterObject.AddComponent<GPGPWaterCompartment>();
        water.size = new Vector3(4, 10, 4);
        water.transform.position = new Vector3(7, 20, -3);
        water.transform.rotation = Quaternion.Euler(0, 22, 0);
        water.transform.localScale = new Vector3(2, .8f, 1);
        var head = new GameObject("Fixture CC head").transform;
        head.SetParent(fixtureRoot.transform, false);
        var audioObject = new GameObject("Fixture exhibit audio");
        audioObject.transform.SetParent(fixtureRoot.transform, false);
        var audio = audioObject.AddComponent<GPGPExhibitAudio>();
        audio.water = water;
        audio.ccHeadTransform = head;
        audio.musicClip = authored.musicClip;
        audio.wavesClip = authored.wavesClip;
        audio.musicVolume = authored.musicVolume;
        audio.wavesMaxVolume = authored.wavesMaxVolume;
        audio.mixSmoothSeconds = authored.mixSmoothSeconds;
        audio.silentBottomFraction = authored.silentBottomFraction;
        PlaceHead(head, water, 0);
        Invoke(audio, "Start");
        ValidateSources(audio);
        var music = audio.MusicSource;
        var waves = audio.WavesSource;
        Require(music.volume == 0 && waves.volume == 0, "Both loops must begin from silence.");
        audio.UpdateMix(.02f);
        Require(music.volume > 0 && music.volume < audio.musicVolume && waves.volume == 0,
            "Music needs a quiet startup fade while bottom waves remain silent.");
        Line("Startup: both loops start at zero; music rises smoothly while the bottom wave mix stays silent.");
        float previous = -1;
        foreach (float height in new[] { 0f, .04f, .08f, .25f, .5f, .75f, 1f })
        {
            PlaceHead(head, water, height);
            Require(Mathf.Abs(audio.NormalizedHeight - height) < .00001f, "Wave height did not use the actual head/water transform.");
            float expected = GPGPExhibitAudio.EvaluateWaveVolume(height, audio.wavesMaxVolume, audio.silentBottomFraction);
            Require(Mathf.Abs(audio.TargetWaveVolume - expected) < .00001f, "The actual target differs from the head-height mix.");
            Require(expected >= previous, "Waves grew quieter while rising.");
            float first = waves.volume;
            audio.UpdateMix(.02f);
            if (height > 0 && expected > first + .001f)
                Require(waves.volume > first && waves.volume < expected, "Height changes must fade smoothly instead of jumping.");
            for (int step = 0; step < 600; step++) audio.UpdateMix(.02f);
            Require(Mathf.Abs(waves.volume - expected) < .0002f, "Wave volume did not settle at the height target.");
            Require(Mathf.Abs(music.volume - audio.musicVolume) < .0002f, "Rising through debris changed the settled music gain.");
            Line("Actual head height " + height.ToString("P0", CultureInfo.InvariantCulture) + ": music=" + music.volume.ToString("F4", CultureInfo.InvariantCulture) +
                "; waves=" + waves.volume.ToString("F4", CultureInfo.InvariantCulture) + "; target=" + expected.ToString("F4", CultureInfo.InvariantCulture) + ".");
            previous = expected;
        }
        Require(GPGPExhibitAudio.EvaluateWaveVolume(.08f, audio.wavesMaxVolume, audio.silentBottomFraction) == 0,
            "The bottom Micro band must remain silent.");
        PlaceHead(head, water, -1);
        for (int step = 0; step < 600; step++) audio.UpdateMix(.02f);
        Require(audio.NormalizedHeight == 0 && audio.TargetWaveVolume == 0 && waves.volume == 0,
            "Waves must become exactly silent at and below the bottom.");
        PlaceHead(head, water, 2);
        Require(audio.NormalizedHeight == 1 && audio.TargetWaveVolume == audio.wavesMaxVolume,
            "Wave gain must stay capped above the surface.");
        // A head-height change must not replace sources, toggle looping or seek.
        // Native playback can be unavailable in an editor outside Play mode.
        int musicSample = music.timeSamples, waveSample = waves.timeSamples;
        for (int step = 0; step < 50; step++)
        {
            PlaceHead(head, water, step % 2 == 0 ? 0 : 1);
            audio.UpdateMix(.02f);
        }
        Require(audio.MusicSource == music && audio.WavesSource == waves && music.loop && waves.loop,
            "Moving through the volume replaced or stopped looping sources.");
        Require(music.timeSamples >= musicSample && waves.timeSamples >= waveSample,
            "A height transition restarted an audio playhead.");
        Line("Movement: 50 head-height changes retain both source identities, looping flags and nondecreasing sample positions.");
        ValidateActualWaypoints(audio, authored, ui);
        bool nativePlayback = music.isPlaying && waves.isPlaying;
        Invoke(audio, "OnDisable");
        int pausedMusic = music.timeSamples, pausedWaves = waves.timeSamples;
        Invoke(audio, "OnEnable");
        Require(audio.MusicSource == music && audio.WavesSource == waves && music.timeSamples >= pausedMusic && waves.timeSamples >= pausedWaves,
            "Disable/re-enable replaced sources or restarted their playheads.");
        // MonoBehaviour lifecycle callbacks are invoked explicitly in this editor audit,
        // while native source activation is exercised by actual component/root toggles.
        Invoke(audio, "OnDisable");
        audio.enabled = false;
        Require(audio.gameObject.activeInHierarchy && music.gameObject.activeInHierarchy && waves.gameObject.activeInHierarchy,
            "Disabling the mix component must not deactivate its source children.");
        pausedMusic = music.timeSamples;
        pausedWaves = waves.timeSamples;
        audio.enabled = true;
        Invoke(audio, "OnEnable");
        Require(audio.MusicSource == music && audio.WavesSource == waves && music.timeSamples >= pausedMusic && waves.timeSamples >= pausedWaves,
            "Component disable/re-enable replaced a source or restarted its playhead.");
        Invoke(audio, "OnDisable");
        audio.gameObject.SetActive(false);
        Require(!music.gameObject.activeInHierarchy && !waves.gameObject.activeInHierarchy,
            "Root deactivation must exercise native source deactivation.");
        audio.gameObject.SetActive(true);
        Invoke(audio, "OnEnable");
        Require(audio.MusicSource == music && audio.WavesSource == waves &&
            (!nativePlayback || music.isPlaying && waves.isPlaying),
            "Root reactivation failed to retain and resume both audio sources.");
        Line("Lifecycle: explicit editor lifecycle callbacks plus actual component.enabled and root.SetActive toggles retain source identity; native source resumption asserted when editor playback is available.");
        Line(nativePlayback ? "Native editor audio reported both sources playing; no headset or hardware listening test performed." :
            "Native AudioSource playback is unavailable outside Play mode; loop configuration, gain behavior and source/sample preservation verified, hardware listening remains manual.");
        Invoke(audio, "OnDisable");
        Object.DestroyImmediate(fixtureRoot);
        fixtureRoot = null;
    }

    static void ValidateActualWaypoints(GPGPExhibitAudio fixture, GPGPExhibitAudio authored, GPGPExhibitUI ui)
    {
        Require(ui.waypointer && ui.waypointer.mainNavigationNode,
            "The existing UI must retain the scene navigation reference.");
        Transform originalNavigation = ui.waypointer.mainNavigationNode.transform;
        Require(authored.ccHeadTransform.IsChildOf(originalNavigation), "The audio head must move with the original navigation node.");
        string path = ui.waypointer.waypointFile;
        if (!Path.IsPathRooted(path)) path = Path.Combine(project, path);
        Require(File.Exists(path), "The existing waypoint file is missing: " + path);
        float[][] rows = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => float.Parse(value, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        Require(rows.Length == 5 && rows.All(row => row.Length == 7), "Expected the existing five exhibit poses.");
        var navigation = new GameObject("Fixture original waypoint navigation").transform;
        navigation.SetParent(fixtureRoot.transform, false);
        navigation.position = originalNavigation.position;
        navigation.rotation = originalNavigation.rotation;
        navigation.localScale = originalNavigation.lossyScale;
        var head = new GameObject("Fixture original CC_HEAD offset").transform;
        head.SetParent(navigation, false);
        head.localPosition = originalNavigation.InverseTransformPoint(authored.ccHeadTransform.position);
        head.localRotation = Quaternion.Inverse(originalNavigation.rotation) * authored.ccHeadTransform.rotation;
        var waypointer = navigation.gameObject.AddComponent<CCaux_Waypointer>();
        waypointer.mainNavigationNode = navigation.gameObject;
        waypointer.loadWaypointFileOnStart = false;
        waypointer.useSplineSmoothMovement = false;
        Invoke(waypointer, "Start");
        var positions = new LinkedList<Vector3>(rows.Select(row => new Vector3(row[0], row[1], row[2])));
        var rotations = new LinkedList<Quaternion>(rows.Select(row => new Quaternion(row[3], row[4], row[5], row[6])));
        Set(waypointer, "navPositionList", positions);
        Set(waypointer, "navRotationList", rotations);
        Set(waypointer, "currentFramePos", positions.First);
        Set(waypointer, "currentFrameRot", rotations.First);
        fixture.water = authored.water;
        fixture.ccHeadTransform = head;
        AudioSource music = fixture.MusicSource, waves = fixture.WavesSource;
        int musicSample = music.timeSamples, waveSample = waves.timeSamples;
        float megaHeight = -1, microHeight = -1, microVolume = -1;
        string[] names = { "Overview", "Mega", "Macro", "Meso", "Micro" };
        for (int step = 1; step <= 5; step++)
        {
            waypointer.NextFrame();
            Set(waypointer, "startTime", Time.time - 1f);
            Invoke(waypointer, "DoAnimation");
            int index = step % 5;
            Require(waypointer.CurrentWaypointIndex == index && Vector3.Distance(navigation.position, positions.ElementAt(index)) < .0001f,
                "The real next-band implementation did not reach the saved pose.");
            for (int frame = 0; frame < 600; frame++) fixture.UpdateMix(.02f);
            Require(Mathf.Abs(music.volume - authored.musicVolume) < .0002f, "A saved category pose changed the music gain.");
            Line("Saved " + names[index] + " pose: head world Y=" + head.position.y.ToString("F4", CultureInfo.InvariantCulture) +
                "; normalized water height=" + fixture.NormalizedHeight.ToString("F4", CultureInfo.InvariantCulture) +
                "; music=" + music.volume.ToString("F4", CultureInfo.InvariantCulture) +
                "; waves=" + waves.volume.ToString("F4", CultureInfo.InvariantCulture) + ".");
            if (index == 1) megaHeight = fixture.NormalizedHeight;
            if (index == 4) { microHeight = fixture.NormalizedHeight; microVolume = waves.volume; }
        }
        Require(megaHeight > microHeight && microVolume == 0, "The saved Micro viewpoint must be silent beneath the louder Mega viewpoint.");
        for (int step = 1; step <= 5; step++)
        {
            Invoke(waypointer, "PrevFrame");
            Set(waypointer, "startTime", Time.time - 1f);
            Invoke(waypointer, "DoAnimation");
            fixture.UpdateMix(.02f);
        }
        Require(fixture.MusicSource == music && fixture.WavesSource == waves &&
            music.timeSamples >= musicSample && waves.timeSamples >= waveSample,
            "Next/previous band navigation replaced or sought an audio source.");
        Line("Navigation: real NextFrame/PrevFrame traversal through all five saved poses retains source identities and nondecreasing playheads; original CC rig was never moved.");
    }

    static void PlaceHead(Transform head, GPGPWaterCompartment water, float height) =>
        head.position = water.transform.TransformPoint(new Vector3(0, (height - .5f) * water.size.y, 0));

    static Dictionary<Object, string> SnapshotProtected(Scene scene, Transform ownedAudio)
    {
        var result = new Dictionary<Object, string>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (ownedAudio && (transform == ownedAudio || transform.IsChildOf(ownedAudio))) continue;
                result[transform.gameObject] = EditorJsonUtility.ToJson(transform.gameObject);
                foreach (Component component in transform.GetComponents<Component>())
                {
                    Require(component, "An original scene object contains a missing script.");
                    result[component] = EditorJsonUtility.ToJson(component);
                }
            }
        return result;
    }

    static void AssertProtected(string context)
    {
        foreach (var pair in protectedState)
            Require(pair.Key && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
                context + " changed an existing object: " + (pair.Key ? pair.Key.name + " / " + pair.Key.GetType().Name : "deleted object"));
    }

    static void Invoke(object target, string name)
    {
        var method = target.GetType().GetMethod(name, Instance);
        Require(method != null, "Missing lifecycle method " + name);
        method.Invoke(target, null);
    }
    static void Set(object target, string name, object value)
    {
        var field = target.GetType().GetField(name, Instance);
        Require(field != null, "Missing navigation validation field " + name);
        field.SetValue(target, value);
    }

    static string Hash(string path)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Line(string message) => Report.AppendLine(message);
    static void WriteReport() { if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, Report.ToString()); }
}
#endif
