using System;
using UnityEditor;
using UnityEngine;

public static class GPGPWaterValidation
{
    [MenuItem("Tools/GPGP/Capture Water Test Screenshot")]
    public static void Capture()
    {
        if (!Application.isPlaying || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="GPGP_WaterTest")
            throw new InvalidOperationException("Open GPGP_WaterTest and enter Play mode before capturing.");
        System.IO.Directory.CreateDirectory("Logs");
        ScreenCapture.CaptureScreenshot("Logs/GPGP_WaterTest.png",2);
        Debug.Log("Water test screenshot requested: Logs/GPGP_WaterTest.png (saved at the end of the rendered frame).");
    }

    [MenuItem("Tools/GPGP/Validate Water Shader")]
    public static void Validate()
    {
        const string path="Assets/Ocean/SG_GPGP_Water.shadergraph";
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (!shader) throw new Exception("Water graph did not import as a shader.");
        foreach (string preset in new[]{"Micro","Meso","Macro","Mega"})
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Ocean/Materials/MAT_GPGP_Water_"+preset+".mat");
            if (!material || material.shader!=shader) throw new Exception("Invalid water material: "+preset);
            for (int pass=0; pass<material.passCount; pass++) ShaderUtil.CompilePass(material,pass,true);
        }
        bool errors=false;
        foreach (var message in ShaderUtil.GetShaderMessages(shader))
        {
            Debug.Log("GPGP shader: "+message.severity+" "+message.message);
            errors |= message.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error;
        }
        if (errors || ShaderUtil.ShaderHasError(shader)) throw new Exception("Water shader compilation failed.");
        Debug.Log("GPGP water: graph imported, four materials resolved, synchronous shader pass compilation completed. Visual and stereo checks still require Play mode.");
    }
}
