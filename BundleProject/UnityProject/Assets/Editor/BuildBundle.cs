using System.IO;
using UnityEditor;
using UnityEngine;

public static class RenderScaleBuildBundle
{
    [MenuItem("RenderScale/Build AssetBundle")]
    public static void Build()
    {
        var outDir = Path.Combine(Application.dataPath, "../../Build");
        Directory.CreateDirectory(outDir);

        var manifest = BuildPipeline.BuildAssetBundles(
            outDir,
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows);

        if (manifest == null)
        {
            Debug.LogError("BuildAssetBundles returned null manifest");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        Debug.Log("AssetBundle written to " + outDir);
        foreach (var name in manifest.GetAllAssetBundles())
            Debug.Log("  bundle: " + name);

        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}
