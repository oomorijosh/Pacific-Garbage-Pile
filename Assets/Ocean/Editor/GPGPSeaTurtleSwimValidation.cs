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
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Authors and exercises turtle movement only in the isolated validation project.</summary>
public static class GPGPSeaTurtleSwimValidation
{
    const string ScenePath = "Assets/Scenes/Plastic_Type.unity";
    const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly string[] PoseNames = { "Overview", "Mega", "Macro", "Meso", "Micro" };
    static readonly StringBuilder Report = new StringBuilder();
    static string project, reportPath, savedSceneHash;
    static Scene scene;
    static GPGPSeaTurtleSwimmer swimmer;
    static GPGPExhibitUI authoredUI, probe;
    static Transform navigation;
    static Vector3 navigationPosition, turtlePosition;
    static Quaternion navigationRotation, turtleRotation;
    static Dictionary<Object, string> protectedState;
    static Dictionary<Transform, Pose> bonePoses;
    static Dictionary<Transform, Vector3> boneScales;
    static Dictionary<Transform, string> boneJson;
    static Vector3[] simulationEyePositions;
    static Camera[] eyes;
    static Vector3[] eyePositions;
    static Quaternion[] eyeRotations;
    static Matrix4x4[] projections, views;
    static float[][] poses;
    static Vector3[] animatedRootCorners;
    static bool authoredUIActive;
    static int warmup, captureStage, captureElapsed;
    static bool capturePrepared;
    static readonly int[] CaptureIndices = { 0, 1, 1, 1, 1, 4 };
    static readonly int[] CaptureSeconds = { 110, 30, 60, 90, 110, 110 };
    static double deadline;

    public static void BuildAndValidate()
    {
        try
        {
            project = Directory.GetParent(Application.dataPath).FullName;
            Require(project.Replace('\\', '/').EndsWith("/.utmp/pacific-exhibit-validation", StringComparison.OrdinalIgnoreCase),
                "Run this scene-writing audit only in .utmp/pacific-exhibit-validation.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Turtle audit stays outside Play mode and XR.");
            Directory.CreateDirectory(Path.Combine(project, "Artifacts"));
            reportPath = Path.Combine(project, "Artifacts", "Sea-turtle-swim-check.txt");
            Report.Clear();
            Line("Camera-aware turtle movement validation; Unity " + Application.unityVersion + "; isolated project, no Play mode/XR.");
            Line("Visibility scope: complete animated-envelope projection in both calibrated eyes and clearance from UI panels; occlusion by individual debris pieces is not measured.");
            Line("Runtime code SHA256 " + Hash(Path.Combine(project, "Assets/Ocean/GPGPSeaTurtleSwimmer.cs")) +
                "; authoring code SHA256 " + Hash(Path.Combine(project, "Assets/Ocean/Editor/GPGPSeaTurtleSwimAuthoring.cs")) + ".");
            scene = EditorSceneManager.OpenScene(ScenePath);
            string diskBefore = Hash(Path.Combine(project, ScenePath));
            var exhibit = Find<GPGPLayeredCubeVisualization>();
            authoredUI = Find<GPGPExhibitUI>();
            Require(authoredUI.cameraSetup && authoredUI.sourceScreen && authoredUI.waypointer && authoredUI.waypointer.mainNavigationNode,
                "Original calibrated screen, head and saved navigation references are required.");
            var turtle = exhibit.GetComponentsInChildren<Transform>(true).Single(value => value.name == "Sea Turtle");
            Require(!turtle.GetComponent<GPGPSeaTurtleSwimmer>(), "Expected the unmodified original turtle for attachment/preservation audit.");
            int originalObjects = SceneObjects().Count();
            int originalComponents = SceneObjects().Sum(value => value.GetComponents<Component>().Length);
            var originalTurtleComponents = turtle.GetComponents<Component>();
            protectedState = SnapshotScene(turtle.gameObject, null);
            Vector3 scaleBefore = turtle.localScale;
            swimmer = GPGPSeaTurtleSwimAuthoring.AddOrUpdate(exhibit, authoredUI.cameraSetup, authoredUI.sourceScreen, authoredUI);
            Require(swimmer && swimmer.transform == turtle, "Movement must attach to the existing Sea Turtle root.");
            Require(SceneObjects().Count() == originalObjects && SceneObjects().Sum(value => value.GetComponents<Component>().Length) == originalComponents + 1,
                "Authoring created extra scene objects or changed more than one component.");
            Require(turtle.GetComponents<Component>().Where(value => value != swimmer).SequenceEqual(originalTurtleComponents),
                "Authoring replaced or reordered the turtle's original components.");
            Require(turtle.localScale == scaleBefore && swimmer.water && swimmer.cameraSetup == authoredUI.cameraSetup &&
                swimmer.projectionScreen == authoredUI.sourceScreen, "Movement lost the original turtle scale, water or calibrated head/screen references.");
            AssertProtected("First attachment");
            string settings = EditorJsonUtility.ToJson(swimmer);
            var second = GPGPSeaTurtleSwimAuthoring.AddOrUpdate(exhibit, authoredUI.cameraSetup, authoredUI.sourceScreen, authoredUI);
            Require(second == swimmer && EditorJsonUtility.ToJson(swimmer) == settings && turtle.GetComponents<GPGPSeaTurtleSwimmer>().Length == 1,
                "Repeated authoring duplicated or changed the swimmer.");
            AssertProtected("Second attachment");
            ValidateAnimation();
            Require(Hash(Path.Combine(project, ScenePath)) == diskBefore, "Authoring unexpectedly saved before preservation checks.");
            Require(EditorSceneManager.SaveScene(scene, ScenePath), "Could not save the isolated turtle scene.");
            scene = EditorSceneManager.OpenScene(ScenePath);
            savedSceneHash = Hash(Path.Combine(project, ScenePath));
            swimmer = Find<GPGPSeaTurtleSwimmer>();
            authoredUI = Find<GPGPExhibitUI>();
            Require(swimmer.water && swimmer.cameraSetup && swimmer.projectionScreen && swimmer.exhibitUI == authoredUI,
                "Save/reopen lost the original exhibit references.");
            ValidateAnimation();
            Line("Two authoring passes and save/reopen: exactly one new component on the original turtle; original hierarchy, bones, scale, meshes, materials, animation, navigation, cameras, water, debris, UI and audio preserved.");
            PrepareSimulation();
            ValidateMotion();
            RestoreMotion();
            AssertProtected("After long deterministic simulation and restoration");
            Require(Hash(Path.Combine(project, ScenePath)) != diskBefore, "Saved scene did not contain the new component.");
            PrepareCaptures();
            warmup = 0;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += CaptureAfterWarmup;
            EditorApplication.QueuePlayerLoopUpdate();
            WriteReport();
        }
        catch (Exception exception) { Fail(exception); }
    }

    static T Find<T>() where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include).Single(value => value.gameObject.scene == scene);
    static IEnumerable<GameObject> SceneObjects() => scene.GetRootGameObjects().SelectMany(value => value.GetComponentsInChildren<Transform>(true)).Select(value => value.gameObject);

    static void ValidateAnimation()
    {
        Animator[] animators = swimmer.GetComponentsInChildren<Animator>(true);
        Require(animators.Length > 0 && animators.All(value => value.enabled && !value.applyRootMotion && value.runtimeAnimatorController),
            "The turtle must retain its enabled in-place swim animator.");
        foreach (Animator animator in animators)
        {
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            Require(clips.Any(value => value.name == "Swim Cycle" && value.isLooping && !value.legacy),
                "The original looping Swim Cycle is missing.");
        }
        Require(swimmer.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(value => value.sharedMesh && value.bones.Length > 0 && value.bones.All(bone => bone) && value.sharedMaterials.All(material => material)),
            "The turtle lost its skinned mesh, bone bindings or materials.");
    }

    static void PrepareSimulation()
    {
        navigation = authoredUI.waypointer.mainNavigationNode.transform;
        navigationPosition = navigation.position;
        navigationRotation = navigation.rotation;
        turtlePosition = swimmer.transform.position;
        turtleRotation = swimmer.transform.rotation;
        string path = authoredUI.waypointer.waypointFile;
        if (!Path.IsPathRooted(path)) path = Path.Combine(project, path);
        poses = File.ReadAllLines(path).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value =>
            value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Select(number => float.Parse(number, CultureInfo.InvariantCulture)).ToArray()).ToArray();
        Require(poses.Length == 5 && poses.All(value => value.Length == 7), "Expected the original five saved navigation poses.");
        protectedState = SnapshotScene(null, swimmer.transform);
        CacheAnimatedEnvelope();
        AssertProtected("Independent animated mesh envelope sampling and bone restoration");
        simulationEyePositions = new[] { swimmer.cameraSetup.leftCamera.transform.localPosition, swimmer.cameraSetup.rightCamera.transform.localPosition };
        Invoke(swimmer.cameraSetup, "Start");
        swimmer.Initialize();
        Require(swimmer.Initialized && Vector3.Distance(turtlePosition, swimmer.transform.position) < .00001f &&
            Quaternion.Angle(turtleRotation, swimmer.transform.rotation) < .0001f, "Initialization teleported or turned the authored turtle.");
        foreach (Vector3 point in animatedRootCorners)
        {
            Bounds bounds = swimmer.ModelLocalBounds;
            bounds.Expand(.001f);
            Require(bounds.Contains(point), "Runtime body bounds omit independently skinned flipper geometry.");
        }
        Line("Runtime conservative root-local envelope: " + swimmer.ModelLocalBounds.ToString("G6") + ".");
    }

    static void ValidateMotion()
    {
        const float dt = 1f / 30f;
        float largestStep = 0, largestTurn = 0;
        int totalSteps = 0;
        // The second reverse pass exercises recovery from the deepest view.
        foreach (int index in new[] { 0, 1, 2, 3, 4, 3, 2, 1, 0 })
        {
            SetPose(index);
            var unchanged = SnapshotScene(null, swimmer.transform);
            Vector3 previous = swimmer.transform.position;
            Quaternion rotation = swimmer.transform.rotation;
            var visited = new Bounds(previous, Vector3.zero);
            float distance = 0;
            int visible = 0, safe = 0, measured = 0, routes = 0, lastNotCentral = -1;
            float lowestY = float.PositiveInfinity, highestY = float.NegativeInfinity;
            for (int step = 0; step < 3600; step++)
            {
                swimmer.Step(dt);
                Vector3 current = swimmer.transform.position;
                float move = Vector3.Distance(previous, current);
                float turn = Quaternion.Angle(rotation, swimmer.transform.rotation);
                Require(Finite(current) && Finite(swimmer.transform.forward), "Movement produced a nonfinite transform.");
                Require(move < 2.5f * dt + .0001f, "A movement step teleported or exceeded a gentle 2.5 m/s ceiling.");
                Require(turn < 100f * dt + .002f, "A movement step snapped orientation beyond 100 degrees/s.");
                largestStep = Mathf.Max(largestStep, move);
                largestTurn = Mathf.Max(largestTurn, turn);
                distance += move;
                visited.Encapsulate(current);
                lowestY = Mathf.Min(lowestY, current.y);
                highestY = Mathf.Max(highestY, current.y);
                Vector3[] corners = GeometryCorners();
                Vector3 half = swimmer.water.size * .5f;
                foreach (Vector3 corner in corners)
                {
                    Vector3 local = swimmer.water.transform.InverseTransformPoint(corner);
                    Require(Mathf.Abs(local.x) <= half.x + .001f && Mathf.Abs(local.y) <= half.y + .001f && Mathf.Abs(local.z) <= half.z + .001f,
                        "The turtle's whole visible bounds escaped the tank at " + PoseNames[index] + ": " + local.ToString("G6"));
                }
                bool inFrame = true, central = true;
                foreach (Vector3 eye in StereoEyes()) foreach (Vector3 corner in corners)
                {
                    Vector3 uv = ScreenViewport(corner, eye);
                    inFrame &= uv.z > 0 && uv.x >= .02f && uv.x <= .98f && uv.y >= .02f && uv.y <= .98f;
                    central &= uv.z > 0 && uv.x >= .285f && uv.x <= .715f && uv.y >= .27f && uv.y <= .895f;
                }
                if (!central) lastNotCentral = step;
                if (step >= 1800)
                {
                    measured++;
                    if (inFrame) visible++;
                    if (central) safe++;
                    if (swimmer.HasVisibleRoute) routes++;
                }
                previous = current;
                rotation = swimmer.transform.rotation;
                totalSteps++;
            }
            Line(PoseNames[index] + ": 120 s; travel " + distance.ToString("F2", CultureInfo.InvariantCulture) + " m; world Y " + lowestY.ToString("F2", CultureInfo.InvariantCulture) +
                ".." + highestY.ToString("F2", CultureInfo.InvariantCulture) + "; settled full-body stereo framing " + (100f * visible / measured).ToString("F1", CultureInfo.InvariantCulture) +
                "%; clear central UI opening " + (100f * safe / measured).ToString("F1", CultureInfo.InvariantCulture) +
                "%; visible route planned " + (100f * routes / measured).ToString("F1", CultureInfo.InvariantCulture) +
                "%; continuously clear after " + ((lastNotCentral + 1) * dt).ToString("F2", CultureInfo.InvariantCulture) + " s; entire body inside water at every step.");
            Line(PoseNames[index] + " final turtle=" + swimmer.transform.position.ToString("G6") + "; route centre=" + swimmer.RouteCenterWorld.ToString("G6") + "; HasVisibleRoute=" + swimmer.HasVisibleRoute + ".");
            Require(distance >= 3f && visited.size.magnitude >= .75f, "The turtle did not swim a meaningful route at " + PoseNames[index]);
            // These thresholds assess the complete animated mesh envelope in both eyes,
            // rather than only testing whether the turtle's centre is visible.
            Require(visible >= measured * .90f, "The turtle did not remain mostly visible after settling at " + PoseNames[index] + ": " + visible + "/" + measured);
            Require(safe >= measured * .75f, "The turtle spent too much settled time under the UI at " + PoseNames[index] + ": " + safe + "/" + measured);
            AssertSnapshot(unchanged, "Swimmer Step at " + PoseNames[index]);
        }
        Line(totalSteps + " deterministic steps; peak speed " + (largestStep / dt).ToString("F3", CultureInfo.InvariantCulture) + " m/s; peak turn " +
            (largestTurn / dt).ToString("F2", CultureInfo.InvariantCulture) + " deg/s; no rig, camera, UI, audio, material, animator or skeleton mutations.");
        ValidateNextPreviousTransitions(dt);
    }

    static void ValidateNextPreviousTransitions(float dt)
    {
        // Exercise the original NextFrame/PrevFrame methods on a navigation
        // fixture. Only the authored navigation transform is copied from it.
        var fixture = new GameObject("Temporary turtle waypoint transition fixture");
        SceneManager.MoveGameObjectToScene(fixture, scene);
        try
        {
            fixture.transform.position = new Vector3(poses[0][0], poses[0][1], poses[0][2]);
            fixture.transform.rotation = PoseRotation(0);
            var waypointer = fixture.AddComponent<CCaux_Waypointer>();
            waypointer.mainNavigationNode = fixture;
            waypointer.loadWaypointFileOnStart = false;
            waypointer.useSplineSmoothMovement = false;
            Invoke(waypointer, "Start");
            var positions = new LinkedList<Vector3>(poses.Select(value => new Vector3(value[0], value[1], value[2])));
            var rotations = new LinkedList<Quaternion>(Enumerable.Range(0, poses.Length).Select(PoseRotation));
            Set(waypointer, "navPositionList", positions);
            Set(waypointer, "navRotationList", rotations);
            Set(waypointer, "currentFramePos", positions.First);
            Set(waypointer, "currentFrameRot", rotations.First);
            for (int direction = 0; direction < 2; direction++) for (int index = 0; index < 5; index++)
            {
                Invoke(waypointer, direction == 0 ? "NextFrame" : "PrevFrame");
                for (int frame = 0; frame <= 16; frame++)
                {
                    Set(waypointer, "startTime", Time.time - frame * dt);
                    Invoke(waypointer, "DoAnimation");
                    navigation.SetPositionAndRotation(fixture.transform.position, fixture.transform.rotation);
                    Vector3 previous = swimmer.transform.position;
                    Quaternion rotation = swimmer.transform.rotation;
                    swimmer.Step(dt);
                    Require(Vector3.Distance(previous, swimmer.transform.position) <= 2.5f * dt + .0001f &&
                        Quaternion.Angle(rotation, swimmer.transform.rotation) <= 100f * dt + .002f,
                        "Actual next/previous navigation caused turtle teleportation or a snapped turn.");
                }
            }
            Line("Actual NextFrame/PrevFrame transitions: all five poses in both directions; camera travelling between frames does not teleport or snap the turtle.");
        }
        finally { Object.DestroyImmediate(fixture); }
    }

    static void SetPose(int index) => navigation.SetPositionAndRotation(new Vector3(poses[index][0], poses[index][1], poses[index][2]), PoseRotation(index));
    static Quaternion PoseRotation(int index) => new Quaternion(poses[index][3], poses[index][4], poses[index][5], poses[index][6]);

    static Vector3[] GeometryCorners()
    {
        Require(animatedRootCorners != null && animatedRootCorners.Length == 8, "The turtle has no sampled animated envelope.");
        return animatedRootCorners.Select(swimmer.transform.TransformPoint).ToArray();
    }

    static void CacheAnimatedEnvelope()
    {
        Transform root = swimmer.transform;
        var transforms = root.GetComponentsInChildren<Transform>(true);
        var originalPoses = transforms.ToDictionary(value => value, value => new Pose(value.localPosition, value.localRotation));
        var originalScales = transforms.ToDictionary(value => value, value => value.localScale);
        var originalJson = transforms.ToDictionary(value => value, value => EditorJsonUtility.ToJson(value));
        var allCorners = new List<Vector3>();
        var mesh = new Mesh { name = "Temporary sampled turtle envelope" };
        try
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh) AddCorners(allCorners, filter.sharedMesh.bounds, filter.transform);
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            SkinnedMeshRenderer[] skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int phase = 0; phase < 12; phase++)
            {
                foreach (Animator animator in animators)
                {
                    AnimationClip clip = animator.runtimeAnimatorController.animationClips.First(value => value.name == "Swim Cycle");
                    clip.SampleAnimation(animator.gameObject, clip.length * phase / 12f);
                }
                foreach (SkinnedMeshRenderer renderer in skins)
                {
                    Vector3[] vertices = renderer.sharedMesh.vertices;
                    BoneWeight[] weights = renderer.sharedMesh.boneWeights;
                    Matrix4x4[] bind = renderer.sharedMesh.bindposes;
                    Transform[] bones = renderer.bones;
                    Require(vertices.Length > 0 && weights.Length == vertices.Length && bind.Length == bones.Length, "The turtle skin has invalid readable vertices, weights or bind poses.");
                    Matrix4x4[] matrices = bones.Select((bone, index) => bone.localToWorldMatrix * bind[index]).ToArray();
                    var localBounds = new Bounds();
                    for (int vertex = 0; vertex < vertices.Length; vertex++)
                    {
                        BoneWeight weight = weights[vertex];
                        Vector3 point = matrices[weight.boneIndex0].MultiplyPoint3x4(vertices[vertex]) * weight.weight0 +
                            matrices[weight.boneIndex1].MultiplyPoint3x4(vertices[vertex]) * weight.weight1 +
                            matrices[weight.boneIndex2].MultiplyPoint3x4(vertices[vertex]) * weight.weight2 +
                            matrices[weight.boneIndex3].MultiplyPoint3x4(vertices[vertex]) * weight.weight3;
                        allCorners.Add(point);
                        Vector3 local = renderer.transform.InverseTransformPoint(point);
                        if (vertex == 0) localBounds = new Bounds(local, Vector3.zero); else localBounds.Encapsulate(local);
                    }
                    if (phase == 0 || phase == 6)
                    {
                        mesh.Clear();
                        renderer.BakeMesh(mesh, false);
                        mesh.RecalculateBounds();
                        string noScale = mesh.bounds.ToString("G6");
                        mesh.Clear();
                        renderer.BakeMesh(mesh, true);
                        mesh.RecalculateBounds();
                        Require(Vector3.Distance(mesh.bounds.center, localBounds.center) < .0001f && Vector3.Distance(mesh.bounds.size, localBounds.size) < .0001f,
                            "Unity's scale-compensated baked skin disagrees with independent boneWorld*bindpose*vertex geometry.");
                        Line("Skin phase " + phase + ": " + renderer.name + "; scale=" + renderer.transform.lossyScale.ToString("G6") +
                            "; shared=" + renderer.sharedMesh.bounds.ToString("G6") + "; local=" + renderer.localBounds.ToString("G6") + "; baked(false)=" + noScale +
                            "; baked(true)=" + mesh.bounds.ToString("G6") + "; manual boneWorld*bindpose*vertex=" + localBounds.ToString("G6"));
                    }
                }
            }
            Require(allCorners.Count > 0, "The turtle has no visible geometry.");
            Bounds bounds = new Bounds(root.InverseTransformPoint(allCorners[0]), Vector3.zero);
            foreach (Vector3 point in allCorners) bounds.Encapsulate(root.InverseTransformPoint(point));
            Require(bounds.size.magnitude * root.lossyScale.magnitude > 1f, "The sampled turtle envelope is implausibly small.");
            animatedRootCorners = Enumerable.Range(0, 8).Select(corner => bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))).ToArray();
            Line("Independent body envelope: original Swim Cycle sampled at 12 phases; every vertex skinned independently with boneWorld*bindpose*vertex; unioned root-local bounds " + bounds.ToString("G6") + "; all bone poses restored.");
        }
        finally
        {
            foreach (var pair in originalPoses)
            {
                EditorJsonUtility.FromJsonOverwrite(originalJson[pair.Key], pair.Key);
            }
            Object.DestroyImmediate(mesh);
        }
    }

    static void AddCorners(List<Vector3> target, Bounds bounds, Transform transform)
    {
        for (int corner = 0; corner < 8; corner++) target.Add(transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1))));
    }

    static IEnumerable<Vector3> StereoEyes()
    {
        yield return swimmer.cameraSetup.leftCamera.transform.position;
        yield return swimmer.cameraSetup.rightCamera.transform.position;
    }

    static Vector3 ScreenViewport(Vector3 point, Vector3 eye)
    {
        Transform screen = swimmer.projectionScreen;
        Vector3 right = screen.TransformVector(Vector3.right), up = screen.TransformVector(Vector3.up);
        Vector3 normal = Vector3.Cross(right, up).normalized;
        if (Vector3.Dot(screen.position - eye, normal) < 0) normal = -normal;
        Vector3 ray = point - eye;
        float depth = Vector3.Dot(ray, normal);
        if (depth <= .00001f) return new Vector3(-100, -100, -1);
        float fraction = Vector3.Dot(screen.position - eye, normal) / depth;
        Vector3 local = screen.InverseTransformPoint(eye + ray * fraction);
        return new Vector3(local.x + .5f, local.y + .5f, depth);
    }

    static void RestoreMotion()
    {
        if (navigation) navigation.SetPositionAndRotation(navigationPosition, navigationRotation);
        if (swimmer) swimmer.transform.SetPositionAndRotation(turtlePosition, turtleRotation);
        if (simulationEyePositions != null && swimmer && swimmer.cameraSetup)
        {
            swimmer.cameraSetup.leftCamera.transform.localPosition = simulationEyePositions[0];
            swimmer.cameraSetup.rightCamera.transform.localPosition = simulationEyePositions[1];
        }
    }

    static void PrepareCaptures()
    {
        captureStage = captureElapsed = 0;
        capturePrepared = false;
        authoredUIActive = authoredUI.gameObject.activeSelf;
        probe = Object.Instantiate(authoredUI.gameObject).GetComponent<GPGPExhibitUI>();
        probe.BuildPreview();
        authoredUI.gameObject.SetActive(false);
        bonePoses = swimmer.GetComponentsInChildren<Transform>(true).Where(value => value != swimmer.transform)
            .ToDictionary(value => value, value => new Pose(value.localPosition, value.localRotation));
        boneScales = bonePoses.Keys.ToDictionary(value => value, value => value.localScale);
        boneJson = bonePoses.Keys.ToDictionary(value => value, value => EditorJsonUtility.ToJson(value));
        eyes = new[] { swimmer.cameraSetup.leftCamera.GetComponent<Camera>(), swimmer.cameraSetup.rightCamera.GetComponent<Camera>() };
        eyePositions = eyes.Select(value => value.transform.localPosition).ToArray();
        eyeRotations = eyes.Select(value => value.transform.localRotation).ToArray();
        projections = eyes.Select(value => value.projectionMatrix).ToArray();
        views = eyes.Select(value => value.worldToCameraMatrix).ToArray();
        float half = swimmer.cameraSetup.CCSettings.eyeSeparation * .5f;
        if (swimmer.cameraSetup.CCSettings.invertStereo) half = -half;
        eyes[0].transform.localPosition -= Vector3.right * half;
        eyes[1].transform.localPosition += Vector3.right * half;
        foreach (Camera camera in eyes) Invoke(camera.GetComponent<CC_CAMERAOFFSET>(), "Start");
    }

    static void CaptureAfterWarmup()
    {
        if (EditorApplication.timeSinceStartup > deadline) { Fail(new InvalidOperationException("Turtle captures timed out.")); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || ++warmup < 24)
        { EditorApplication.QueuePlayerLoopUpdate(); return; }
        try
        {
            if (captureStage < CaptureIndices.Length)
            {
                int index = CaptureIndices[captureStage];
                if (!capturePrepared)
                {
                    if (captureStage == 0 || CaptureIndices[captureStage - 1] != index)
                    {
                        SetPose(index);
                        probe.SyncToWaypoint(index);
                        Invoke(probe, "LateUpdate");
                        captureElapsed = 0;
                    }
                    capturePrepared = true;
                    warmup = 20;
                    EditorApplication.QueuePlayerLoopUpdate();
                    return;
                }
                int seconds = CaptureSeconds[captureStage];
                for (int step = 0; step < (seconds - captureElapsed) * 30; step++) swimmer.Step(1f / 30f);
                CapturePose(index, seconds == 110 ? "" : "-" + seconds.ToString("D3", CultureInfo.InvariantCulture) + "s", seconds);
                captureElapsed = seconds;
                captureStage++;
                capturePrepared = false;
                warmup = 20;
                EditorApplication.QueuePlayerLoopUpdate();
                return;
            }
            EditorApplication.update -= CaptureAfterWarmup;
            RestoreCaptures();
            AssertProtected("After calibrated stereo evidence capture and restoration");
            Require(Hash(Path.Combine(project, ScenePath)) == savedSceneHash, "Simulation or evidence capture unexpectedly saved scene changes.");
            Line("Evidence: Overview, Mega and Micro from both calibrated Cyber Canoe eyes, plus Mega after 30, 60 and 90 seconds for debris-occlusion review; original UI shown and original swim clip sampled; temporary camera/UI/navigation/animation state restored and never saved.");
            Line("RESULT PASS: slow, continuous, camera-aware turtle route; body inside water and framed between panels, gradual recovery across navigation, unchanged exhibit and original swimming animation.");
            WriteReport();
            Debug.Log("GPGP_SEA_TURTLE_SWIM_VALIDATION: Passed.");
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }
        catch (Exception exception) { Fail(exception); }
    }

    static void CapturePose(int index, string suffix, float seconds)
    {
        foreach (Animator animator in swimmer.GetComponentsInChildren<Animator>(true))
        {
            AnimationClip clip = animator.runtimeAnimatorController.animationClips.First(value => value.name == "Swim Cycle");
            clip.SampleAnimation(animator.gameObject, Mathf.Repeat(.35f + seconds * .7f, clip.length));
        }
        for (int eye = 0; eye < eyes.Length; eye++)
        {
            // Several evidence renders share one editor tick. Rebuild the
            // temporary UI's text geometry before each eye so TMP's native
            // buffers remain valid without waiting for a normal player frame.
            foreach (TMPro.TMP_Text label in probe.GetComponentsInChildren<TMPro.TMP_Text>(true)) label.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
            Invoke(eyes[eye].GetComponent<CC_CAMERAOFFSET>(), "LateUpdate");
            CaptureCamera(eyes[eye], "Sea-turtle-swim-" + PoseNames[index] + suffix + "-" + (eye == 0 ? "left" : "right") + "-eye.png");
        }
    }

    static void CaptureCamera(Camera camera, string filename)
    {
        const int width = 2400, height = 810;
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR);
        var oldTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        Rect oldRect = camera.rect;
        var oldStereo = camera.stereoTargetEye;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        bool oldXR = data && data.allowXRRendering;
        Texture2D linear = null, image = null;
        try
        {
            Require(target.Create(), "Could not create turtle evidence render target.");
            camera.targetTexture = target;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            if (data) data.allowXRRendering = false;
            var reflection = Object.FindObjectsByType<PacificExhibit.PacificPlanarReflection>(FindObjectsInactive.Include).FirstOrDefault(value => value.gameObject.scene == scene);
            var request = new RenderPipeline.StandardRequest { destination = target };
            Require(RenderPipeline.SupportsRenderRequest(camera, request), "URP cannot capture the actual eye.");
            for (int pass = 0; pass < 2; pass++)
            {
                if (reflection) reflection.RenderReflectionFor(camera);
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            RenderTexture.active = target;
            linear = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            linear.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            linear.Apply();
            Color[] pixels = linear.GetPixels();
            int bright = 0, pink = 0;
            for (int pixel = 0; pixel < pixels.Length; pixel++)
            {
                Color color = pixels[pixel];
                Require(Finite(new Vector3(color.r, color.g, color.b)), "A capture contains nonfinite pixels.");
                if (color.maxColorComponent > .15f) bright++;
                if (color.r > .7f && color.b > .7f && color.g < .1f) pink++;
                pixels[pixel] = color.gamma;
            }
            Require(bright > pixels.Length / 1000 && pink < pixels.Length / 1000, "A capture is empty or has shader-error magenta.");
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.SetPixels(pixels);
            image.Apply();
            File.WriteAllBytes(Path.Combine(project, "Artifacts", filename), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = oldActive;
            camera.targetTexture = oldTarget;
            camera.rect = oldRect;
            camera.stereoTargetEye = oldStereo;
            if (data) data.allowXRRendering = oldXR;
            if (linear) Object.DestroyImmediate(linear);
            if (image) Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    static void RestoreCaptures()
    {
        if (probe) Object.DestroyImmediate(probe.gameObject);
        probe = null;
        if (authoredUI) authoredUI.gameObject.SetActive(authoredUIActive);
        if (eyes != null && eyePositions != null) for (int index = 0; index < eyes.Length; index++)
        {
            if (!eyes[index]) continue;
            eyes[index].transform.localPosition = eyePositions[index];
            eyes[index].transform.localRotation = eyeRotations[index];
            eyes[index].projectionMatrix = projections[index];
            eyes[index].worldToCameraMatrix = views[index];
        }
        eyes = null;
        if (bonePoses != null) foreach (var pair in bonePoses)
        {
            if (!pair.Key) continue;
            EditorJsonUtility.FromJsonOverwrite(boneJson[pair.Key], pair.Key);
        }
        RestoreMotion();
        // URP may initialize material keywords lazily on the first capture.
        if (protectedState != null) foreach (var pair in protectedState.Where(value => value.Key is Material))
            if (pair.Key && EditorJsonUtility.ToJson(pair.Key) != pair.Value) EditorJsonUtility.FromJsonOverwrite(pair.Value, pair.Key);
    }

    static Dictionary<Object, string> SnapshotScene(GameObject exceptObject, Transform exceptTransform)
    {
        var result = new Dictionary<Object, string>();
        foreach (GameObject item in SceneObjects())
        {
            if (item != exceptObject) result[item] = EditorJsonUtility.ToJson(item);
            foreach (Component component in item.GetComponents<Component>())
            {
                Require(component, "An original scene object contains a missing component.");
                if (component != exceptTransform) result[component] = EditorJsonUtility.ToJson(component);
                var serialized = new SerializedObject(component);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        Object asset = property.objectReferenceValue;
                        if (asset && EditorUtility.IsPersistent(asset) && (asset is Material || asset is ScriptableObject))
                            result[asset] = EditorJsonUtility.ToJson(asset);
                    }
            }
        }
        foreach (Material material in SceneObjects().SelectMany(value => value.GetComponents<Renderer>()).SelectMany(value => value.sharedMaterials).Where(value => value).Distinct())
            result[material] = EditorJsonUtility.ToJson(material);
        return result;
    }

    static void AssertProtected(string context) => AssertSnapshot(protectedState, context);
    static void AssertSnapshot(Dictionary<Object, string> snapshot, string context)
    {
        foreach (var pair in snapshot) Require(pair.Key && EditorJsonUtility.ToJson(pair.Key) == pair.Value,
            context + " changed protected state: " + (pair.Key ? pair.Key.name + " / " + pair.Key.GetType().Name : "deleted object"));
    }
    static void Invoke(object target, string name)
    {
        Require(target != null, "Missing target for " + name);
        var method = target.GetType().GetMethod(name, Instance);
        Require(method != null, "Missing method " + name);
        method.Invoke(target, null);
    }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Instance).SetValue(target, value);
    static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Line(string message) => Report.AppendLine(message);
    static void WriteReport() { if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, Report.ToString()); }
    static void Fail(Exception exception)
    {
        EditorApplication.update -= CaptureAfterWarmup;
        try { RestoreCaptures(); } catch (Exception cleanup) { Line("Capture cleanup failure: " + cleanup); }
        Line("RESULT FAIL: " + exception);
        WriteReport();
        Debug.LogException(exception);
        EditorApplication.Exit(1);
    }
}
#endif
