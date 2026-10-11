using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Exports only the reusable headset preview add-on, without a user's scene or project settings.</summary>
public static class CyberCanoeHeadsetPackageExport
{
    const string Root = "Assets/HeadsetPreview";

    [MenuItem("Tools/Cyber Canoe/Export Headset Preview Add-on")]
    public static void ExportFromMenu()
    {
        Export();
        EditorUtility.RevealInFinder(Path.Combine(ProjectRoot, "Artifacts", "CyberCanoe-HeadsetPreview-Unity.zip"));
    }

    [MenuItem("Tools/Cyber Canoe/Export Headset Preview Add-on", true)]
    static bool CanExport() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

    static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

    public static void Export()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before exporting headset preview.");
        string[] assets = AssetDatabase.GetAllAssetPaths()
            .Where(path => path.StartsWith(Root + "/", StringComparison.Ordinal) &&
                !path.StartsWith(Root + "/ProjectSettings/", StringComparison.Ordinal) &&
                path != Root + "/ProjectSettings")
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (!assets.Contains(Root + "/CyberCanoeHeadsetSimulator.cs") ||
            !assets.Contains(Root + "/Editor/CyberCanoeHeadsetProjectSetup.cs"))
            throw new InvalidOperationException("The reusable headset preview scripts are missing.");

        string folder = Path.Combine(ProjectRoot, "Artifacts", "CyberCanoe-HeadsetPreview-Share");
        Directory.CreateDirectory(folder);
        string package = Path.Combine(folder, "CyberCanoe-HeadsetPreview.unitypackage");
        // Explicit selection prevents importing a user's exhibit or replacing the class's CC scripts.
        AssetDatabase.ExportPackage(assets, package, ExportPackageOptions.Default);
        File.Copy(Path.Combine(ProjectRoot, Root, "README.txt"), Path.Combine(folder, "README-FIRST.txt"), true);
        File.Copy(Path.Combine(ProjectRoot, "ProjectSettings/ProjectVersion.txt"), Path.Combine(folder, "Unity-Version.txt"), true);
        File.WriteAllLines(Path.Combine(folder, "EXPORT-CONTENTS.txt"), assets, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(folder, "REQUIRED-PACKAGES.json"),
            "{\n  \"dependencies\": {\n" +
            "    \"com.unity.inputsystem\": \"1.20.0\",\n" +
            "    \"com.unity.render-pipelines.universal\": \"17.6.0\",\n" +
            "    \"com.unity.ugui\": \"2.6.0\",\n" +
            "    \"com.unity.xr.management\": \"4.7.0\",\n" +
            "    \"com.unity.xr.openxr\": \"1.18.0\"\n" +
            "  }\n}\n", new UTF8Encoding(false));

        string archive = Path.Combine(ProjectRoot, "Artifacts", "CyberCanoe-HeadsetPreview-Unity.zip");
        if (File.Exists(archive)) File.Delete(archive);
        using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            foreach (string name in new[] { "CyberCanoe-HeadsetPreview.unitypackage", "README-FIRST.txt",
                "Unity-Version.txt", "EXPORT-CONTENTS.txt", "REQUIRED-PACKAGES.json" })
                zip.CreateEntryFromFile(Path.Combine(folder, name), name, System.IO.Compression.CompressionLevel.Fastest);
        Debug.Log("CYBERCANOE_HEADSET_ADDON: Exported only " + assets.Length +
            " headset preview assets. No exhibit, scene, CC core scripts, or saved project settings: " + archive);
    }
}
