// Place this file at: <UnityProject>/Assets/Editor/BuildBundle.cs
// It registers a menu item: "RenderScale > Build AssetBundle"
//
// One-time Unity setup required to build the bundle:
//   1. Install Unity Hub and Unity 2021.3.5f1 (matches DREDGE).
//   2. Create a new project (Universal Render Pipeline template).
//   3. Drop AreaDownsample.shader into Assets/Shaders/.
//   4. Drop this file into Assets/Editor/.
//   5. Select AreaDownsample.shader in the Project window. In the Inspector
//      footer, set "AssetBundle" to "supersampling" (and Variant blank).
//   6. Menu: RenderScale > Build AssetBundle.
//   7. Copy the produced "supersampling" file into the mod's Assets/ folder.

using System.IO;
using UnityEditor;
using UnityEngine;

public static class RenderScaleBuildBundle
{
    [MenuItem("RenderScale/Build AssetBundle")]
    public static void Build()
    {
        var outDir = Path.Combine(Application.dataPath, "../BuiltBundles");
        Directory.CreateDirectory(outDir);

        BuildPipeline.BuildAssetBundles(
            outDir,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        Debug.Log("AssetBundle written to " + outDir);
        EditorUtility.RevealInFinder(outDir);
    }
}
