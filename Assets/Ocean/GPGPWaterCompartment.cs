using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Stable open-top volume and a GPU-animated grid sharing one water material.</summary>
[ExecuteAlways]
public sealed class GPGPWaterCompartment : MonoBehaviour
{
    public Material waterMaterial;
    [Tooltip("Optional material for the four vertical faces and bottom.")]
    public Material sideMaterial;
    public Vector3 size = new Vector3(3, 1.2f, 3);
    [Range(8, 160)] public int subdivisions = 64;
    [Tooltip("Disable on upper layers of a continuous stack to avoid internal bottom faces.")]
    public bool includeBottom = true;
    readonly List<Mesh> meshes = new List<Mesh>();
    readonly List<GameObject> parts = new List<GameObject>();
    bool rebuild;
    float amplitude = -1;
    float verticalScale = -1;
    static readonly int WaveAmplitude = Shader.PropertyToID("_WaveAmplitude");
    static readonly int WaveAmplitude2 = Shader.PropertyToID("_WaveAmplitude2");

    void OnEnable() { rebuild = true; }
    void OnValidate() { rebuild = true; }
    void Update()
    {
        if (rebuild) { rebuild = false; Build(); }
        float current = waterMaterial ? Mathf.Max(0, waterMaterial.GetFloat(WaveAmplitude)) : 0;
        if (waterMaterial && waterMaterial.HasProperty(WaveAmplitude2))
            current += Mathf.Max(0, waterMaterial.GetFloat(WaveAmplitude2));
        float scale = Mathf.Max(.001f,Mathf.Abs(transform.lossyScale.y));
        if (Mathf.Approximately(amplitude, current) && Mathf.Approximately(verticalScale,scale)) return;
        amplitude = current;
        verticalScale = scale;
        foreach (Mesh mesh in meshes)
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(Mathf.Max(.01f,size.x),
                Mathf.Max(.01f,size.y) + 2*current/scale + .1f,
                Mathf.Max(.01f,size.z)));
    }

    void Build()
    {
        Clear();
        if (!waterMaterial) return;
        Vector3 half = new Vector3(Mathf.Max(.01f,size.x), Mathf.Max(.01f,size.y), Mathf.Max(.01f,size.z))*.5f;
        int n = Mathf.Clamp(subdivisions,8,160);
        var vertices = new Vector3[(n+1)*(n+1)];
        var colors = new Color[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var dimensions = new Vector2[vertices.Length];
        var normals = new Vector3[vertices.Length];
        var tangents = new Vector4[vertices.Length];
        var indices = new int[n*n*6];
        for (int z=0; z<=n; z++) for (int x=0; x<=n; x++)
        {
            int i=z*(n+1)+x;
            uv[i]=new Vector2((float)x/n,(float)z/n);
            dimensions[i]=new Vector2(half.x*2,half.z*2);
            vertices[i]=new Vector3((uv[i].x*2-1)*half.x,half.y,(uv[i].y*2-1)*half.z);
            colors[i]=new Color(1,1,0,1); // R: surface mask, G: normalized height.
            normals[i]=Vector3.up; tangents[i]=new Vector4(1,0,0,-1);
            if (x==n || z==n) continue;
            int t=(z*n+x)*6;
            indices[t]=i; indices[t+1]=i+n+1; indices[t+2]=i+1;
            indices[t+3]=i+1; indices[t+4]=i+n+1; indices[t+5]=i+n+2;
        }
        var surface = new Mesh { name="GPGP surface grid", hideFlags=HideFlags.HideAndDontSave };
        surface.vertices=vertices; surface.normals=normals; surface.tangents=tangents;
        surface.colors=colors; surface.uv=uv; surface.uv2=dimensions; surface.triangles=indices;
        AddPart(surface,"Water surface",waterMaterial);

        var sides = new List<Vector3>(); var sideColors = new List<Color>(); var faces = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start=sides.Count;
            foreach (Vector3 p in new[]{a,b,c,d})
            {
                sides.Add(Vector3.Scale(p,half));
                sideColors.Add(new Color(0,(p.y+1)*.5f,0,1));
            }
            faces.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
        }
        Quad(new Vector3(-1,-1,-1),new Vector3(-1,1,-1),new Vector3(1,1,-1),new Vector3(1,-1,-1));
        Quad(new Vector3(1,-1,1),new Vector3(1,1,1),new Vector3(-1,1,1),new Vector3(-1,-1,1));
        Quad(new Vector3(-1,-1,1),new Vector3(-1,1,1),new Vector3(-1,1,-1),new Vector3(-1,-1,-1));
        Quad(new Vector3(1,-1,-1),new Vector3(1,1,-1),new Vector3(1,1,1),new Vector3(1,-1,1));
        if (includeBottom)
            Quad(new Vector3(-1,-1,1),new Vector3(-1,-1,-1),new Vector3(1,-1,-1),new Vector3(1,-1,1));
        var volume = new Mesh { name="GPGP open-top volume", hideFlags=HideFlags.HideAndDontSave };
        volume.SetVertices(sides); volume.SetColors(sideColors); volume.SetTriangles(faces,0);
        volume.RecalculateNormals();
        var sideUV=new Vector2[sides.Count];
        for (int i=0; i<sides.Count; i+=4)
        {
            sideUV[i]=new Vector2(0,0); sideUV[i+1]=new Vector2(0,1);
            sideUV[i+2]=new Vector2(1,1); sideUV[i+3]=new Vector2(1,0);
        }
        volume.uv=sideUV;
        var sideTangents=new Vector4[sides.Count];
        var sideNormals=volume.normals;
        for (int i=0; i<sideTangents.Length; i++)
        {
            Vector3 tangent=Vector3.Cross(Mathf.Abs(sideNormals[i].y)>.9f?Vector3.forward:Vector3.up,sideNormals[i]);
            sideTangents[i]=new Vector4(tangent.x,tangent.y,tangent.z,1);
        }
        volume.tangents=sideTangents;
        AddPart(volume,"Water volume",sideMaterial ? sideMaterial : waterMaterial);
        amplitude=-1;
    }

    void AddPart(Mesh mesh, string label, Material material)
    {
        meshes.Add(mesh);
        var part = new GameObject(label,typeof(MeshFilter),typeof(MeshRenderer));
        part.hideFlags=HideFlags.HideAndDontSave;
        part.transform.SetParent(transform,false);
        part.GetComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
        parts.Add(part);
    }

    void OnDisable() { Clear(); }
    void Clear()
    {
        foreach (GameObject part in parts) if (part) { part.SetActive(false); Release(part); }
        foreach (Mesh mesh in meshes) if (mesh) Release(mesh);
        parts.Clear(); meshes.Clear();
    }
    static void Release(Object item)
    {
        if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
    }
}
