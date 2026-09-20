using UnityEngine;

/// <summary>Mesh and CPU sampling for Ocean_Shader's object-space sine wave.</summary>
[ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class OceanSurface : MonoBehaviour
{
    [Range(16, 250)] public int subdivisions = 128;
    [Min(0)] public float circulationSpeed = 0.4f;
    [Min(0)] public float convergenceRate = 0.01f;
    [Min(0.1f)] public float accumulationRadius = 12f;
    Mesh generatedMesh;
    Mesh originalMesh;
    int builtResolution;
    float boundedHeight;
    static readonly int WaveHeight = Shader.PropertyToID("_Wave_Height");

    float Amplitude
    {
        get
        {
            var material = GetComponent<MeshRenderer>().sharedMaterial;
            return material && material.HasProperty(WaveHeight) ? material.GetFloat(WaveHeight) : 0.2f;
        }
    }

    void OnEnable() { Rebuild(); }
    void Update()
    {
        if (!generatedMesh || builtResolution != Mathf.Clamp(subdivisions, 16, 250)) Rebuild();
        float height = Mathf.Abs(Amplitude);
        if (generatedMesh && !Mathf.Approximately(height, boundedHeight)) UpdateBounds(height);
    }

    void Rebuild()
    {
        var filter = GetComponent<MeshFilter>();
        if (!generatedMesh) originalMesh = filter.sharedMesh;
        else ReleaseMesh();
        int n = builtResolution = Mathf.Clamp(subdivisions, 16, 250);
        var vertices = new Vector3[(n + 1) * (n + 1)];
        var normals = new Vector3[vertices.Length];
        var tangents = new Vector4[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[n * n * 6];
        for (int z = 0; z <= n; z++)
        for (int x = 0; x <= n; x++)
        {
            int i = z * (n + 1) + x;
            uv[i] = new Vector2((float)x / n, (float)z / n);
            vertices[i] = new Vector3(uv[i].x * 10 - 5, 0, uv[i].y * 10 - 5);
            normals[i] = Vector3.up;
            tangents[i] = new Vector4(1, 0, 0, -1);
            if (x == n || z == n) continue;
            int t = (z * n + x) * 6;
            triangles[t] = i; triangles[t + 1] = i + n + 1; triangles[t + 2] = i + 1;
            triangles[t + 3] = i + 1; triangles[t + 4] = i + n + 1; triangles[t + 5] = i + n + 2;
        }
        generatedMesh = new Mesh { name = "Ocean grid (generated)", hideFlags = HideFlags.HideAndDontSave };
        generatedMesh.vertices = vertices;
        generatedMesh.normals = normals;
        generatedMesh.tangents = tangents;
        generatedMesh.uv = uv;
        generatedMesh.triangles = triangles;
        UpdateBounds(Mathf.Abs(Amplitude));
        filter.sharedMesh = generatedMesh;
    }

    void UpdateBounds(float height)
    {
        boundedHeight = height;
        generatedMesh.bounds = new Bounds(Vector3.zero, new Vector3(10, height * 2 + 0.1f, 10));
    }

    // Keep the ocean horizontal. Translation and nonuniform scale are supported.
    public void Sample(Vector3 worldPosition, float time, out Vector3 surface, out Vector3 normal)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        float phase = local.x + local.z + time;
        local.y = Amplitude * Mathf.Sin(phase);
        float slope = Amplitude * Mathf.Cos(phase);
        surface = transform.TransformPoint(local);
        normal = transform.worldToLocalMatrix.transpose.MultiplyVector(new Vector3(-slope, 1, -slope)).normalized;
    }

    public Vector3 Current(Vector3 position)
    {
        Vector3 radial = position - transform.position;
        radial.y = 0;
        float distance = radial.magnitude;
        if (distance < 0.001f) return Vector3.zero;
        Vector3 outward = radial / distance;
        // Clockwise circulation and gentle convergence toward a broad accumulation zone.
        return Vector3.Cross(Vector3.up, outward) * circulationSpeed * Mathf.Clamp01(distance / accumulationRadius)
            - outward * Mathf.Max(0, distance - accumulationRadius) * convergenceRate;
    }

    void OnDisable()
    {
        var filter = GetComponent<MeshFilter>();
        if (filter && filter.sharedMesh == generatedMesh) filter.sharedMesh = originalMesh;
        ReleaseMesh();
    }
    void ReleaseMesh()
    {
        if (!generatedMesh) return;
        if (Application.isPlaying) Destroy(generatedMesh); else DestroyImmediate(generatedMesh);
        generatedMesh = null;
    }
}
