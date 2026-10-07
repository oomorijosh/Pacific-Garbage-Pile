using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Equal-mass markers for the historical GPGP estimate; not literal debris.</summary>
public sealed class WhiteboxTrash : MonoBehaviour
{
    public Material blockMaterial;
    [Range(4, 1000)] public int markerCount = 400;
    [Range(1, 60)] public float revealSpeed = 12;
    public const double TotalTonnes = 78909;
    // Table 1 rounded category rows sum to 78908; normalize shares by that sum.
    public static readonly double[] CategoryTonnes = { 42362, 20195, 9971, 6380 };
    readonly string[] names = { "Megaplastic", "Macroplastic", "Mesoplastic", "Microplastic" };
    readonly string[] sizes = { ">50 cm", "5-50 cm", "5 mm-5 cm", "0.5-5 mm" };
    readonly Color[] colors = { new Color(.85f,.28f,.12f), new Color(.65f,.40f,.75f), new Color(.90f,.65f,.10f), new Color(.12f,.58f,.42f) };
    readonly List<Marker> markers = new List<Marker>();
    Transform root, floor;
    Mesh mesh;
    int[] allocation, categoryOrder;
    int count, landed;
    float budget;
    bool paused;
    GUIStyle title, number, text, small, button;
    sealed class Marker { public Transform transform; public float speed; public bool landed; }

    public static int[] Allocate(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        double total = 0;
        foreach (double mass in CategoryTonnes) total += mass;
        var result = new int[4];
        var remainder = new double[4];
        int assigned = 0;
        for (int i = 0; i < 4; i++)
        {
            double exact = count * CategoryTonnes[i] / total;
            result[i] = (int)Math.Floor(exact);
            remainder[i] = exact - result[i];
            assigned += result[i];
        }
        while (assigned++ < count)
        {
            int best = 0;
            for (int i = 1; i < 4; i++) if (remainder[i] > remainder[best]) best = i;
            result[best]++;
            remainder[best] = -1;
        }
        return result;
    }

    void Start()
    {
        if (!blockMaterial) { Debug.LogError("Assign the whitebox material.", this); enabled = false; return; }
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
        primitive.SetActive(false);
        Destroy(primitive);
        floor = MakeBlock("Whitebox Floor", transform, new Color(.72f,.74f,.76f));
        ResetVisualization();
    }

    Transform MakeBlock(string label, Transform parent, Color color)
    {
        var go = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = blockMaterial;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(properties);
        return go.transform;
    }

    public void ResetVisualization()
    {
        if (!floor) return;
        if (root) { root.gameObject.SetActive(false); Destroy(root.gameObject); }
        root = new GameObject("Mass markers").transform;
        root.SetParent(transform, false);
        markers.Clear(); landed = 0; budget = 0; paused = false;
        count = Mathf.Clamp(markerCount,4,1000);
        allocation = Allocate(count);
        categoryOrder = new int[count];
        int next = 0;
        for (int category = 0; category < 4; category++)
            for (int i = 0; i < allocation[category]; i++) categoryOrder[next++] = category;
        var random = new System.Random(485);
        for (int i = count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int temp = categoryOrder[i]; categoryOrder[i] = categoryOrder[j]; categoryOrder[j] = temp;
        }
        float side = Mathf.CeilToInt(Mathf.Sqrt(count)) * 1.15f + 3;
        floor.localScale = new Vector3(side,.18f,side);
        floor.localPosition = new Vector3(0,-.09f,0);
    }

    void Update()
    {
        if (!floor) return;
        if (paused) return;
        budget += Time.deltaTime * Mathf.Clamp(revealSpeed,1,60);
        while (budget >= 1 && markers.Count < count) { Spawn(); budget--; }
        if (markers.Count == count) budget = 0;
        foreach (var marker in markers)
        {
            if (marker.landed) continue;
            marker.speed += 9.81f * Time.deltaTime;
            Vector3 position = marker.transform.localPosition;
            position.y = Mathf.Max(.225f, position.y - marker.speed * Time.deltaTime);
            marker.transform.localPosition = position;
            if (position.y <= .225f) { marker.landed = true; landed++; }
        }
    }

    void Spawn()
    {
        int i = markers.Count;
        int width = Mathf.CeilToInt(Mathf.Sqrt(count));
        // A coprime step fills the grid across its area without repeated cells.
        int step = Mathf.Max(1, Mathf.RoundToInt(count * .618f));
        while (Gcd(step,count) != 1) step++;
        int cell = (i * step + count / 2) % count;
        int category = categoryOrder[i];
        Transform piece = MakeBlock(names[category] + " mass marker",root,colors[category]);
        piece.localScale = new Vector3(.85f,.45f,.85f);
        piece.localPosition = new Vector3((cell % width - (width-1)*.5f)*1.15f,10,(cell / width - (width-1)*.5f)*1.15f);
        markers.Add(new Marker { transform = piece });
    }
    static int Gcd(int a, int b) { while (b != 0) { int r = a % b; a = b; b = r; } return a; }

    void OnGUI()
    {
        if (!floor) return;
        if (title == null)
        {
            title = Style(24,FontStyle.Bold); number = Style(32,FontStyle.Bold);
            text = Style(17,FontStyle.Normal); small = Style(14,FontStyle.Normal);
            button = new GUIStyle(GUI.skin.button) { fontSize = 17 };
        }
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        float scale = Mathf.Min(Screen.width/1280f,Screen.height/800f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1));
        GUI.color = new Color(.96f,.97f,.98f);
        GUI.DrawTexture(new Rect(0,0,Screen.width/scale*.36f,Screen.height/scale),Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(24,22,408,760));
        GUILayout.Label("GREAT PACIFIC\nGARBAGE PATCH",title);
        GUILayout.Label("MASS / WHITEBOX STUDY",small);
        GUILayout.Space(12);
        GUILayout.Label((TotalTonnes*landed/count).ToString("N0") + " tonnes",number);
        GUILayout.Label("of 78,909 estimated metric tonnes",text);
        GUILayout.Label(landed + " / " + count + " blocks settled  |  " + (100f*landed/count).ToString("0") + "%",small);
        GUILayout.Label("1 block = approximately " + (TotalTonnes/count).ToString("N2") + " tonnes",text);
        GUILayout.Space(16);
        GUILayout.Label("COLOR SHOWS SIZE CATEGORY",text);
        for (int i = 0; i < 4; i++)
        {
            GUILayout.BeginHorizontal();
            Rect swatch = GUILayoutUtility.GetRect(16,38,GUILayout.Width(16));
            GUI.color = colors[i]; GUI.DrawTexture(new Rect(swatch.x,swatch.y+5,12,12),Texture2D.whiteTexture); GUI.color = Color.white;
            GUILayout.Label(names[i]+"  |  "+sizes[i]+"\n"+(100*CategoryTonnes[i]/78908).ToString("0.0")+"% mass  /  "+allocation[i]+" blocks",small);
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("Equal-mass blocks; counts rounded to preserve mass shares. Block dimensions are symbolic.",small);
        GUILayout.Space(12);
        GUILayout.Label("Reveal speed: "+revealSpeed.ToString("0")+" blocks/sec",text);
        revealSpeed = GUILayout.HorizontalSlider(revealSpeed,1,60);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(paused?"Resume":"Pause",button,GUILayout.Height(36))) paused=!paused;
        if (GUILayout.Button("Restart",button,GUILayout.Height(36))) ResetVisualization();
        GUILayout.EndHorizontal();
        GUILayout.Space(12);
        GUILayout.Label("2015-2016 surveys; published 2018.\nEstimated range: 45,163-128,665 tonnes.",small);
        GUILayout.Label("Whitebox visualization of estimated mass. Block dimensions, falling motion and floor coverage are symbolic, not physical scale or a dumping rate.",small);
        GUILayout.Label("Lebreton et al. (2018), Table 1\ndoi:10.1038/s41598-018-22939-w",small);
        GUILayout.EndArea();
        GUI.color=oldColor; GUI.matrix=oldMatrix;
    }
    static GUIStyle Style(int size,FontStyle weight)
    {
        var style=new GUIStyle(GUI.skin.label) { fontSize=size,fontStyle=weight,wordWrap=true };
        style.normal.textColor=new Color(.12f,.18f,.23f);
        return style;
    }
}
