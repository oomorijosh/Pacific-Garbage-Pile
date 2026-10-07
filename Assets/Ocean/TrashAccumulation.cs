using System.Collections.Generic;
using UnityEngine;

/// <summary>Visualizes a supplied total with weighted markers and an illustrative reveal.</summary>
public sealed class TrashAccumulation : MonoBehaviour
{
    public Material oceanMaterial;
    public Material debrisMaterial;
    [Header("Dataset - enter the source total before playback")]
    [Min(0)] public double totalAmount;
    public string unit = "items";
    public string datasetLabel = "";
    public string source = "";
    [Header("Visual presentation")]
    [Range(1, 1000)] public int targetCount = 400;
    [Range(1, 60)] public float itemsPerSecond = 12;
    [Min(1)] public float dropHeight = 12;
    readonly List<Piece> pieces = new List<Piece>();
    readonly Color[] colors = { new Color(.95f,.36f,.12f), new Color(.9f,.8f,.2f), new Color(.22f,.55f,.8f), new Color(.85f,.84f,.73f) };
    OceanSurface ocean;
    Transform debrisRoot;
    System.Random random;
    float spawnBudget;
    int landed;
    bool paused;
    int runTarget;
    Mesh cubeMesh;
    GUIStyle heading, body;

    sealed class Piece
    {
        public Transform transform;
        public Vector3 destination;
        public float velocity, offset;
        public bool landed;
    }

    void Start()
    {
        if (!oceanMaterial || !debrisMaterial)
        {
            Debug.LogError("Assign the ocean and debris materials to Trash Accumulation.", this);
            enabled = false;
            return;
        }
        var water = new GameObject("Visualization Ocean", typeof(MeshFilter), typeof(MeshRenderer));
        water.transform.SetParent(transform, false);
        water.transform.localScale = new Vector3(20, 1, 20);
        water.GetComponent<MeshRenderer>().sharedMaterial = oceanMaterial;
        ocean = water.AddComponent<OceanSurface>();
        ocean.circulationSpeed = 0;
        ocean.convergenceRate = 0;
        // Retrieve Unity's shared primitive mesh once, rather than creating colliders per piece.
        var temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cubeMesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        temporary.SetActive(false);
        Destroy(temporary);
        Restart();
    }

    public void Restart()
    {
        if (!ocean) return;
        if (debrisRoot) { debrisRoot.gameObject.SetActive(false); Destroy(debrisRoot.gameObject); }
        debrisRoot = new GameObject("Accumulating Debris").transform;
        debrisRoot.SetParent(transform, false);
        pieces.Clear();
        random = new System.Random(485);
        landed = 0;
        spawnBudget = 0;
        paused = false;
        runTarget = Mathf.Clamp(targetCount, 1, 1000);
    }

    void Update()
    {
        if (!ocean || paused || totalAmount <= 0 || double.IsNaN(totalAmount) || double.IsInfinity(totalAmount)) return;
        spawnBudget += Time.deltaTime * Mathf.Max(1, itemsPerSecond);
        while (spawnBudget >= 1 && pieces.Count < runTarget)
        {
            Spawn();
            spawnBudget -= 1;
        }
        if (pieces.Count == runTarget) spawnBudget = 0;
        foreach (var piece in pieces)
        {
            ocean.Sample(piece.destination, Time.time, out Vector3 surface, out Vector3 normal);
            surface += Vector3.up * piece.offset;
            if (!piece.landed)
            {
                piece.velocity += 9.81f * Time.deltaTime;
                Vector3 position = piece.transform.position;
                position.y -= piece.velocity * Time.deltaTime;
                if (position.y <= surface.y)
                {
                    piece.landed = true;
                    landed++;
                    position = surface;
                }
                piece.transform.position = position;
            }
            if (piece.landed)
            {
                piece.transform.position = surface;
                Quaternion rotation = piece.transform.rotation;
                piece.transform.rotation = Quaternion.Slerp(rotation,
                    Quaternion.FromToRotation(rotation * Vector3.up, normal) * rotation,
                    1 - Mathf.Exp(-4 * Time.deltaTime));
            }
        }
    }

    void Spawn()
    {
        int i = pieces.Count;
        int width = Mathf.CeilToInt(Mathf.Sqrt(runTarget));
        // A coprime step spreads the reveal across distinct cells.
        int step = Mathf.Max(1, Mathf.RoundToInt(runTarget * .618f));
        while (GreatestCommonDivisor(step, runTarget) != 1) step++;
        int cell = (i * step + runTarget / 2) % runTarget;
        float x = (cell % width - (width - 1) * .5f) * 1.25f;
        float z = (cell / width - (width - 1) * .5f) * 1.25f;
        x += ((float)random.NextDouble() - .5f) * .12f;
        z += ((float)random.NextDouble() - .5f) * .12f;
        float size = .35f + (float)random.NextDouble() * .42f;
        float thickness = .08f + (float)random.NextDouble() * .12f;
        var go = new GameObject("Debris " + (i + 1), typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(debrisRoot, false);
        go.transform.localScale = new Vector3(size, thickness, size);
        go.transform.rotation = Quaternion.Euler(0, (float)random.NextDouble() * 360, 0);
        go.GetComponent<MeshFilter>().sharedMesh = cubeMesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = debrisMaterial;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_BaseColor", colors[i % colors.Length]);
        renderer.SetPropertyBlock(properties);
        Vector3 destination = transform.position + new Vector3(x, 0, z);
        go.transform.position = destination + Vector3.up * Mathf.Max(1, dropHeight);
        pieces.Add(new Piece { transform = go.transform, destination = destination, offset = thickness * .25f });
    }

    static int GreatestCommonDivisor(int a, int b)
    {
        while (b != 0) { int remainder = a % b; a = b; b = remainder; }
        return a;
    }

    void OnGUI()
    {
        if (!ocean) return;
        if (heading == null)
        {
            heading = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, wordWrap = true };
            body = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
        }
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUILayout.BeginArea(new Rect(24,24,410,440), GUI.skin.box);
        GUILayout.Label("TRASH ACCUMULATION", heading);
        if (totalAmount <= 0 || double.IsNaN(totalAmount) || double.IsInfinity(totalAmount))
        {
            GUILayout.Label("Waiting for dataset", heading);
            GUILayout.Label("Enter the measured total, unit, location/time period and source on Trash Accumulation in the Inspector.", body);
            GUILayout.Label("No amount is assumed. The ocean is ready for your data.", body);
            GUILayout.EndArea();
            GUI.matrix = previous;
            return;
        }
        GUILayout.Label(datasetLabel, body);
        GUILayout.Label("Each block represents " + (totalAmount / runTarget).ToString("N2") + " " + unit, body);
        GUILayout.Space(12);
        GUILayout.Label((totalAmount * landed / runTarget).ToString("N2") + " / " + totalAmount.ToString("N2") + " " + unit, heading);
        GUILayout.Label((pieces.Count - landed) + " falling · " + Mathf.RoundToInt(100f * landed / runTarget) + "% deposited", body);
        GUILayout.Space(10);
        GUILayout.Label("Reveal speed: " + itemsPerSecond.ToString("0") + " blocks / second", body);
        itemsPerSecond = GUILayout.HorizontalSlider(itemsPerSecond,1,60);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(paused ? "Resume" : "Pause", GUILayout.Height(34))) paused = !paused;
        if (GUILayout.Button("Restart", GUILayout.Height(34))) Restart();
        GUILayout.EndHorizontal();
        GUILayout.Label("Animation speed and block sizes are illustrative. They do not represent physical dimensions or dumping rates.", body);
        GUILayout.Label("Source: " + source, body);
        GUILayout.EndArea();
        GUI.matrix = previous;
    }
}
