using System.Collections.Generic;
using UnityEngine;

/// <summary>Moves the scene turtle through the water, favouring the open part of the calibrated stereo display.</summary>
[DisallowMultipleComponent]
public sealed class GPGPSeaTurtleSwimmer : MonoBehaviour
{
    public GPGPWaterCompartment water;
    public CC_CAMERA_SETUP cameraSetup;
    public Transform projectionScreen;
    public GPGPUI.GPGPExhibitUI exhibitUI;
    [Tooltip("Normal swimming speed in world metres per second.")]
    [Min(.05f)] public float cruiseSpeed = .65f;
    [Tooltip("Maximum speed while swimming back into view after navigation.")]
    [Min(.05f)] public float retargetSpeed = 1.4f;
    [Min(.05f)] public float acceleration = .7f;
    [Tooltip("Maximum change of heading in degrees per second.")]
    [Range(5f, 90f)] public float turnSpeed = 25f;
    [Tooltip("Preferred horizontal and depth radii in world metres. Routes narrow when necessary.")]
    public Vector2 orbitRadii = new Vector2(3f, 1.8f);
    [Min(0f)] public float depthVariation = .25f;
    [Tooltip("Extra clearance around the animated turtle in world metres.")]
    [Min(0f)] public float bodyClearance = .12f;
    [Tooltip("Open part of the display, in normalized screen coordinates: x, y, width, height.")]
    public Rect visibleRegion = new Rect(.30f, .27f, .40f, .63f);

    public bool Initialized { get; private set; }
    public bool HasVisibleRoute { get; private set; }
    public Vector3 WorldVelocity => velocity;
    public Vector3 RouteCenterWorld => water ? water.transform.TransformPoint(routeCenter) : transform.position;
    public Bounds ModelLocalBounds => modelBounds;

    const float MaximumPitch = 6f;
    const float PlanInterval = .75f;
    readonly List<Plane> constraints = new List<Plane>(18);
    Bounds modelBounds;
    [SerializeField, HideInInspector] Bounds animatedModelBounds;
    [SerializeField, HideInInspector] bool hasAnimatedModelBounds;
    Vector3 velocity, centerVelocity;
    Vector3 routeCenter, desiredCenter, routeRadii, desiredRadii;
    float horizontalBodyRadius, verticalBodyRadius;
    float phase, planTimer;

    void Start() => Initialize();
    void Update() => Step(Time.deltaTime);

    /// <summary>Stores the full sampled swim envelope prepared on a temporary editor clone.</summary>
    public void SetAnimatedModelBounds(Bounds bounds)
    {
        animatedModelBounds = bounds;
        hasAnimatedModelBounds = bounds.size.sqrMagnitude > .000001f;
        Initialized = false;
    }

    /// <summary>Reads mesh bounds without changing the model, Animator, camera, or authored pose.</summary>
    public bool Initialize()
    {
        if (!water || !TryReadModelBounds(out modelBounds))
        {
            Initialized = false;
            return false;
        }
        Vector3 scale = transform.lossyScale;
        float maximumScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 extent = modelBounds.extents;
        // A horizontal disc remains safe through every heading, without the
        // excessive vertical padding of a bounding sphere around a flat turtle.
        float horizontal = new Vector2(extent.x, extent.z).magnitude * maximumScale;
        float vertical = extent.y * maximumScale;
        float pitch = MaximumPitch * Mathf.Deg2Rad;
        horizontalBodyRadius = horizontal + vertical * Mathf.Sin(pitch) + bodyClearance;
        verticalBodyRadius = vertical + horizontal * Mathf.Sin(pitch) + bodyClearance;
        routeCenter = desiredCenter = water.transform.InverseTransformPoint(transform.TransformPoint(modelBounds.center));
        routeRadii = desiredRadii = Vector3.zero;
        velocity = centerVelocity = Vector3.zero;
        phase = planTimer = 0f;
        Initialized = true;
        PlanRoute();
        return true;
    }

    /// <summary>Deterministic simulation hook, also used by the runtime Update.</summary>
    public void Step(float deltaTime)
    {
        if (!(deltaTime > 0f) || float.IsInfinity(deltaTime) || float.IsNaN(deltaTime)) return;
        if (!Initialized && !Initialize()) return;
        if (!water) { Initialized = false; return; }
        // Substeps retain the same acceleration and containment on a long frame.
        int steps = Mathf.CeilToInt(Mathf.Min(deltaTime, 1f) / .05f);
        float dt = Mathf.Min(deltaTime, 1f) / steps;
        for (int i = 0; i < steps; i++) Advance(dt);
    }

    void Advance(float dt)
    {
        planTimer -= dt;
        if (planTimer <= 0f) { PlanRoute(); planTimer = PlanInterval; }
        Transform volume = water.transform;
        Vector3 oldCenter = volume.TransformPoint(routeCenter);
        Vector3 newCenter = Vector3.SmoothDamp(oldCenter, volume.TransformPoint(desiredCenter),
            ref centerVelocity, 1.4f, Mathf.Max(cruiseSpeed, retargetSpeed), dt);
        routeCenter = volume.InverseTransformPoint(newCenter);
        routeRadii = Vector3.Lerp(routeRadii, desiredRadii, 1f - Mathf.Exp(-dt / 2f));
        Vector3 tangent = volume.TransformVector(new Vector3(-routeRadii.x * Mathf.Sin(phase),
            routeRadii.y * .7f * Mathf.Cos(phase * .7f), routeRadii.z * Mathf.Cos(phase)));
        float phaseSpeed = Mathf.Min(.35f, Mathf.Max(.05f, cruiseSpeed) * .7f / Mathf.Max(.2f, tangent.magnitude));
        phase = (phase + phaseSpeed * dt) % (Mathf.PI * 20f);
        Vector3 target = volume.TransformPoint(routeCenter + new Vector3(
            routeRadii.x * Mathf.Cos(phase), routeRadii.y * Mathf.Sin(phase * .7f),
            routeRadii.z * Mathf.Sin(phase)));
        tangent = volume.TransformVector(new Vector3(-routeRadii.x * Mathf.Sin(phase),
            routeRadii.y * .7f * Mathf.Cos(phase * .7f), routeRadii.z * Mathf.Cos(phase))) * phaseSpeed;
        // The geometric route describes the body's centre. Preserve the
        // imported wrapper's offset instead of padding that offset on both sides.
        Vector3 rootTarget = target - transform.TransformVector(modelBounds.center);
        Vector3 difference = rootTarget - transform.position;
        bool relocating = difference.sqrMagnitude > 3.24f || centerVelocity.sqrMagnitude > .0625f;
        float maximumSpeed = relocating ? Mathf.Max(cruiseSpeed, retargetSpeed) : cruiseSpeed;
        Vector3 desiredVelocity = Vector3.ClampMagnitude(tangent + centerVelocity + difference * .9f,
            Mathf.Max(.05f, maximumSpeed));
        velocity = Vector3.MoveTowards(velocity, desiredVelocity, Mathf.Max(.05f, acceleration) * dt);

        Vector3 up = volume.up;
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, up);
        if (horizontalVelocity.sqrMagnitude > .0001f)
        {
            float maximumSlope = Mathf.Tan(MaximumPitch * Mathf.Deg2Rad);
            float pitch = Mathf.Clamp(Vector3.Dot(velocity, up),
                -horizontalVelocity.magnitude * maximumSlope, horizontalVelocity.magnitude * maximumSlope);
            Quaternion heading = Quaternion.LookRotation(horizontalVelocity + up * pitch, up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, heading, Mathf.Max(1f, turnSpeed) * dt);
        }
        Vector3 previous = transform.position;
        transform.position += velocity * dt;
        ContainModel();
        velocity = (transform.position - previous) / dt;
    }

    void PlanRoute()
    {
        constraints.Clear();
        Vector3 half = Vector3.Max(water.size * .5f, Vector3.one * .01f);
        AddWaterPlane(Vector3.right, new Vector3(-half.x, 0f, 0f));
        AddWaterPlane(Vector3.left, new Vector3(half.x, 0f, 0f));
        AddWaterPlane(Vector3.up, new Vector3(0f, -half.y, 0f));
        AddWaterPlane(Vector3.down, new Vector3(0f, half.y, 0f));
        AddWaterPlane(Vector3.forward, new Vector3(0f, 0f, -half.z));
        AddWaterPlane(Vector3.back, new Vector3(0f, 0f, half.z));

        bool hasViews = cameraSetup && projectionScreen && cameraSetup.leftCamera && cameraSetup.rightCamera;
        Vector3 preferred = routeCenter;
        if (hasViews)
        {
            Rect region = visibleRegion;
            if (exhibitUI && exhibitUI.IsUIHidden) region = new Rect(.10f, .10f, .80f, .80f);
            AddEyeCone(cameraSetup.leftCamera.transform.position, region);
            AddEyeCone(cameraSetup.rightCamera.transform.position, region);
            Vector3 eye = (cameraSetup.leftCamera.transform.position + cameraSetup.rightCamera.transform.position) * .5f;
            Vector3 screenPoint = ScreenPoint(region.center.x, Mathf.Lerp(region.yMin, region.yMax, .25f));
            Vector3 start = water.transform.InverseTransformPoint(eye);
            Vector3 direction = water.transform.InverseTransformVector((screenPoint - eye).normalized);
            if (RayBox(start, direction, half, out float near, out float far))
                preferred = start + direction * Mathf.Lerp(Mathf.Max(0f, near), far, .15f);
        }

        Vector3 radii = new Vector3(
            Mathf.Max(.1f, orbitRadii.x) / Mathf.Max(.001f, water.transform.TransformVector(Vector3.right).magnitude),
            Mathf.Max(0f, depthVariation) / Mathf.Max(.001f, water.transform.TransformVector(Vector3.up).magnitude),
            Mathf.Max(.1f, orbitRadii.y) / Mathf.Max(.001f, water.transform.TransformVector(Vector3.forward).magnitude));
        HasVisibleRoute = SearchRoute(preferred, half, radii, out Vector3 center, out float fit);
        if (!HasVisibleRoute)
        {
            // Some external camera poses have no water in view. Continue a safe
            // water route, and try the view again after navigation changes.
            if (constraints.Count > 6) constraints.RemoveRange(6, constraints.Count - 6);
            SearchRoute(routeCenter, half, radii, out center, out fit);
        }
        HasVisibleRoute &= hasViews;
        desiredCenter = center;
        desiredRadii = radii * Mathf.Clamp01(fit);
    }

    bool SearchRoute(Vector3 preferred, Vector3 half, Vector3 radii, out Vector3 center, out float fit)
    {
        Vector3 best = Vector3.zero;
        float bestFit = 0f, bestScore = float.NegativeInfinity;
        Consider(preferred, preferred, radii, ref best, ref bestFit, ref bestScore);
        Consider(routeCenter, preferred, radii, ref best, ref bestFit, ref bestScore);
        // Projection catches a narrow feasible region between grid samples.
        Vector3 projected = preferred;
        for (int pass = 0; pass < 40; pass++)
            foreach (Plane plane in constraints)
            {
                float distance = plane.GetDistanceToPoint(projected);
                if (distance < 0f) projected -= plane.normal * distance;
            }
        Consider(projected, preferred, radii, ref best, ref bestFit, ref bestScore);
        // Also seed the closest broad loop, which can fall between coarse grid
        // rows. Project the whole oval rather than only its centre point.
        Vector3 fullOval = preferred;
        for (int pass = 0; pass < 40; pass++)
            foreach (Plane plane in constraints)
            {
                float distance = plane.GetDistanceToPoint(fullOval) - OvalSupport(plane.normal, radii);
                if (distance < 0f) fullOval -= plane.normal * distance;
            }
        Consider(fullOval, preferred, radii, ref best, ref bestFit, ref bestScore);
        for (int x = 0; x <= 8; x++)
            for (int y = 0; y <= 12; y++)
                for (int z = 0; z <= 12; z++)
                    Consider(new Vector3(Mathf.Lerp(-half.x, half.x, x / 8f),
                        Mathf.Lerp(-half.y, half.y, y / 12f), Mathf.Lerp(-half.z, half.z, z / 12f)),
                        preferred, radii, ref best, ref bestFit, ref bestScore);
        center = best;
        fit = bestFit;
        return !float.IsNegativeInfinity(bestScore);
    }

    void Consider(Vector3 candidate, Vector3 preferred, Vector3 radii,
        ref Vector3 best, ref float bestFit, ref float bestScore)
    {
        float fit = 1f;
        foreach (Plane plane in constraints)
        {
            float clearance = plane.GetDistanceToPoint(candidate);
            if (clearance < -.0001f) return;
            float support = OvalSupport(plane.normal, radii);
            if (support > .00001f) fit = Mathf.Min(fit, Mathf.Max(0f, clearance) / support);
        }
        float distance = water.transform.TransformVector(candidate - preferred).magnitude;
        float score = Mathf.Clamp01(fit) * 3f - distance * .035f;
        if (score <= bestScore) return;
        bestScore = score;
        bestFit = Mathf.Clamp01(fit) * .96f;
        best = candidate;
    }

    static float OvalSupport(Vector3 normal, Vector3 radii) =>
        new Vector2(normal.x * radii.x, normal.z * radii.z).magnitude + Mathf.Abs(normal.y) * radii.y;

    void AddWaterPlane(Vector3 localNormal, Vector3 localPoint)
    {
        Vector3 normal = water.transform.worldToLocalMatrix.transpose.MultiplyVector(localNormal).normalized;
        AddWorldPlane(normal, water.transform.TransformPoint(localPoint));
    }

    void AddEyeCone(Vector3 eye, Rect region)
    {
        Vector3 ll = ScreenPoint(region.xMin, region.yMin);
        Vector3 lr = ScreenPoint(region.xMax, region.yMin);
        Vector3 ul = ScreenPoint(region.xMin, region.yMax);
        Vector3 ur = ScreenPoint(region.xMax, region.yMax);
        Vector3 middle = ScreenPoint(region.center.x, region.center.y);
        AddRayPlane(eye, ll, ul, middle);
        AddRayPlane(eye, ur, lr, middle);
        AddRayPlane(eye, lr, ll, middle);
        AddRayPlane(eye, ul, ur, middle);
        Vector3 forward = Vector3.Cross(lr - ll, ul - ll).normalized;
        if (Vector3.Dot(forward, middle - eye) < 0f) forward = -forward;
        AddWorldPlane(forward, eye + forward * .1f);
    }

    void AddRayPlane(Vector3 eye, Vector3 a, Vector3 b, Vector3 inside)
    {
        Vector3 normal = Vector3.Cross(a - eye, b - eye).normalized;
        if (Vector3.Dot(normal, inside - eye) < 0f) normal = -normal;
        AddWorldPlane(normal, eye);
    }

    void AddWorldPlane(Vector3 worldNormal, Vector3 worldPoint)
    {
        float vertical = Mathf.Abs(Vector3.Dot(worldNormal, water.transform.up));
        float support = horizontalBodyRadius * Mathf.Sqrt(Mathf.Max(0f, 1f - vertical * vertical)) +
            verticalBodyRadius * vertical;
        Transform volume = water.transform;
        Vector3 localNormal = new Vector3(Vector3.Dot(worldNormal, volume.TransformVector(Vector3.right)),
            Vector3.Dot(worldNormal, volume.TransformVector(Vector3.up)),
            Vector3.Dot(worldNormal, volume.TransformVector(Vector3.forward)));
        float length = localNormal.magnitude;
        if (length < .00001f) return;
        float distance = (Vector3.Dot(worldNormal, volume.position - worldPoint) - support) / length;
        constraints.Add(new Plane(localNormal / length, distance));
    }

    Vector3 ScreenPoint(float x, float y) => projectionScreen.TransformPoint(new Vector3(x - .5f, y - .5f, 0f));

    static bool RayBox(Vector3 origin, Vector3 direction, Vector3 half, out float near, out float far)
    {
        near = float.NegativeInfinity;
        far = float.PositiveInfinity;
        for (int axis = 0; axis < 3; axis++)
        {
            if (Mathf.Abs(direction[axis]) < .00001f)
            {
                if (Mathf.Abs(origin[axis]) > half[axis]) return false;
                continue;
            }
            float a = (-half[axis] - origin[axis]) / direction[axis];
            float b = (half[axis] - origin[axis]) / direction[axis];
            near = Mathf.Max(near, Mathf.Min(a, b));
            far = Mathf.Min(far, Mathf.Max(a, b));
            if (near > far) return false;
        }
        return far > 0f;
    }

    void ContainModel()
    {
        Matrix4x4 toWater = water.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        Bounds bounds = new Bounds(toWater.MultiplyPoint3x4(modelBounds.center), Vector3.zero);
        for (int corner = 0; corner < 8; corner++)
            bounds.Encapsulate(toWater.MultiplyPoint3x4(Corner(modelBounds, corner)));
        Vector3 half = water.size * .5f;
        Vector3 correction = Vector3.zero;
        for (int axis = 0; axis < 3; axis++)
        {
            if (bounds.size[axis] > water.size[axis]) correction[axis] = -bounds.center[axis];
            else if (bounds.min[axis] < -half[axis]) correction[axis] = -half[axis] - bounds.min[axis];
            else if (bounds.max[axis] > half[axis]) correction[axis] = half[axis] - bounds.max[axis];
        }
        if (correction.sqrMagnitude > 0f) transform.position += water.transform.TransformVector(correction);
    }

    bool TryReadModelBounds(out Bounds bounds)
    {
        bounds = hasAnimatedModelBounds ? animatedModelBounds : new Bounds();
        bool found = hasAnimatedModelBounds;
        foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh) Include(filter.sharedMesh.bounds, filter.transform, ref bounds, ref found);
        foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (renderer.sharedMesh)
            {
                // Some glTF renderers have a dirty, zero localBounds before their
                // first skinning update. The mesh is a dependable rest fallback.
                Include(renderer.sharedMesh.bounds, renderer.transform, ref bounds, ref found);
                if (!hasAnimatedModelBounds && renderer.localBounds.size.sqrMagnitude > .000001f)
                    Include(renderer.localBounds, renderer.transform, ref bounds, ref found);
            }
        return found && bounds.size.sqrMagnitude > .000001f;
    }

    void Include(Bounds sourceBounds, Transform source, ref Bounds bounds, ref bool found)
    {
        Matrix4x4 matrix = transform.worldToLocalMatrix * source.localToWorldMatrix;
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 point = matrix.MultiplyPoint3x4(Corner(sourceBounds, corner));
            if (found) bounds.Encapsulate(point);
            else { bounds = new Bounds(point, Vector3.zero); found = true; }
        }
    }

    static Vector3 Corner(Bounds bounds, int index) => bounds.center + Vector3.Scale(bounds.extents,
        new Vector3((index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
}
