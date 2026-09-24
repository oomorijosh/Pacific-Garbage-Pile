using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Optional subtle separator sheets at the data-driven mass-share boundaries.</summary>
public sealed class GPGPLayerSurfaces : MonoBehaviour
{
    readonly List<Mesh> meshes=new List<Mesh>();
    readonly List<GameObject> parts=new List<GameObject>();

    public void Build(Vector3 size, Material material)
    {
        const int subdivisions=48;
        float[] boundaries={.08f,.21f,.46f};
        for (int layer=1;layer<=3;layer++)
        {
            float normalizedHeight=boundaries[layer-1];
            float y=-size.y*.5f+size.y*normalizedHeight;
            Mesh mesh=CreateGrid(size,y,normalizedHeight,subdivisions);
            var part=new GameObject("Layer wave "+layer,typeof(MeshFilter),typeof(MeshRenderer));
            part.transform.SetParent(transform,false);
            part.GetComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;
            parts.Add(part); meshes.Add(mesh);
        }
    }

    static Mesh CreateGrid(Vector3 size, float y, float height, int n)
    {
        int count=(n+1)*(n+1);
        var vertices=new Vector3[count]; var normals=new Vector3[count];
        var tangents=new Vector4[count]; var uv=new Vector2[count];
        var dimensions=new Vector2[count]; var colors=new Color[count];
        var triangles=new int[n*n*6];
        for (int z=0;z<=n;z++) for (int x=0;x<=n;x++)
        {
            int i=z*(n+1)+x;
            uv[i]=new Vector2((float)x/n,(float)z/n);
            dimensions[i]=new Vector2(size.x,size.z);
            vertices[i]=new Vector3((uv[i].x-.5f)*size.x,y,(uv[i].y-.5f)*size.z);
            colors[i]=new Color(1,height,0,1);
            normals[i]=Vector3.up; tangents[i]=new Vector4(1,0,0,-1);
            if (x==n || z==n) continue;
            int t=(z*n+x)*6;
            triangles[t]=i; triangles[t+1]=i+n+1; triangles[t+2]=i+1;
            triangles[t+3]=i+1; triangles[t+4]=i+n+1; triangles[t+5]=i+n+2;
        }
        var mesh=new Mesh { name="GPGP internal layer wave" };
        mesh.vertices=vertices; mesh.normals=normals; mesh.tangents=tangents;
        mesh.colors=colors; mesh.uv=uv; mesh.uv2=dimensions; mesh.triangles=triangles;
        mesh.bounds=new Bounds(new Vector3(0,y,0),new Vector3(size.x,size.y*.15f,size.z));
        return mesh;
    }

    void OnDestroy()
    {
        foreach (GameObject part in parts) if (part) Destroy(part);
        foreach (Mesh mesh in meshes) if (mesh) Destroy(mesh);
    }
}
