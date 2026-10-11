using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PacificExhibit.Sharing
{
    /// <summary>Exports the connected exhibit and the instructions needed before content import.</summary>
    public static class PacificVRPackageExport
    {
        const string ScenePath = "Assets/Scenes/Plastic_Type.unity";
        static readonly string[] Folders =
        {
            "Assets/CC_ALL", "Assets/Debris", "Assets/Ocean", "Assets/PacificExhibit",
            "Assets/GPGPUI", "Assets/HeadsetPreview", "Assets/Wildlife", "Assets/XR",
            "Assets/VRSharing", "Assets/TextMesh Pro/Resources", "Assets/TextMesh Pro/Shaders",
            "Assets/TextMesh Pro/Fonts"
        };
        static readonly string[] ExtraAssets =
        {
            ScenePath, "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/UniversalRenderPipelineGlobalSettings.asset"
        };
        static readonly string[] Dependencies =
        {
            "com.unity.cloud.gltfast\": \"6.19.0",
            "com.unity.inputsystem\": \"1.20.0",
            "com.unity.render-pipelines.universal\": \"17.6.0",
            "com.unity.ugui\": \"2.6.0",
            "com.unity.xr.management\": \"4.7.0",
            "com.unity.xr.openxr\": \"1.18.0"
        };

        [MenuItem("Tools/Pacific Exhibit/Export VR Unity Share Package")]
        public static void ExportFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Export();
            EditorUtility.RevealInFinder(Path.Combine(ProjectRoot, "Artifacts", "Pacific-Garbage-Pile-VR-Unity.zip"));
        }

        [MenuItem("Tools/Pacific Exhibit/Export VR Unity Share Package", true)]
        static bool CanExport() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        public static void Export()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play mode before exporting the VR Unity package.");
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                throw new FileNotFoundException("The complete exhibit scene is required.", ScenePath);

            string folder = Path.Combine(ProjectRoot, "Artifacts", "VR-Unity-Share");
            Directory.CreateDirectory(Path.Combine(folder, "Packages"));
            string[] selected = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) &&
                (Folders.Any(root => path == root || path.StartsWith(root + "/", StringComparison.Ordinal)) || ExtraAssets.Contains(path)))
                .Where(path => !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || path == ScenePath).ToArray();
            // C# references and authoring asset paths need explicit folders as well as serialized dependencies.
            string[] assets = AssetDatabase.GetDependencies(selected, true).Concat(selected)
                .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal))
                .Distinct().OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (!assets.Contains(ScenePath) || !assets.Contains("Assets/VRSharing/Editor/PacificVRPackageSetup.cs"))
                throw new InvalidOperationException("The scene or imported-project setup command is missing from the export.");

            string package = Path.Combine(folder, "Pacific-Garbage-Pile-VR.unitypackage");
            AssetDatabase.ExportPackage(assets, package, ExportPackageOptions.Default);
            File.Copy(Path.Combine(ProjectRoot, "Assets/VRSharing/README-FIRST.txt"), Path.Combine(folder, "README-FIRST.txt"), true);
            File.Copy(Path.Combine(ProjectRoot, "ProjectSettings/ProjectVersion.txt"), Path.Combine(folder, "Unity-Version.txt"), true);
            File.WriteAllLines(Path.Combine(folder, "EXPORT-CONTENTS.txt"), assets, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "Packages/manifest.json"), DependencyManifest(), new UTF8Encoding(false));

            string archive = Path.Combine(ProjectRoot, "Artifacts", "Pacific-Garbage-Pile-VR-Unity.zip");
            if (File.Exists(archive)) File.Delete(archive);
            ZipFile.CreateFromDirectory(folder, archive, System.IO.Compression.CompressionLevel.Fastest, false);
            Debug.Log("PACIFIC_VR_PACKAGE: Exported " + assets.Length + " assets, dependency manifest, import guide and connected camera rig to " + archive);
        }

        static string DependencyManifest()
        {
            var entries = Dependencies.Select(pair => "    \"" + pair + "\"").ToList();
            string source = File.ReadAllText(Path.Combine(ProjectRoot, "Packages/manifest.json"));
            foreach (Match module in Regex.Matches(source, "\"(com\\.unity\\.modules\\.[^\"]+)\"\\s*:\\s*\"([^\"]+)\""))
                entries.Add("    \"" + module.Groups[1].Value + "\": \"" + module.Groups[2].Value + "\"");
            return "{\n  \"dependencies\": {\n" + string.Join(",\n", entries) + "\n  }\n}\n";
        }
    }
}
