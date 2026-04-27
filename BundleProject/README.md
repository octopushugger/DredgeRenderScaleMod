# Building the RenderScale AssetBundle

The mod ships an HLSL shader (`AreaDownsample.shader`) that has to be precompiled
by Unity Editor into an AssetBundle, since Unity standalone cannot compile shaders
at runtime.

## One-time setup

1. Install **Unity Hub** + **Unity 2021.3.5f1** (the version DREDGE was built with).
2. Create a new Unity project from the **Universal Render Pipeline** template.
3. Copy `AreaDownsample.shader` into `Assets/Shaders/`.
4. Copy `Editor/BuildBundle.cs` into `Assets/Editor/`.
5. In the Project window, select `AreaDownsample.shader`. In the Inspector at
   the bottom there's an "AssetBundle" dropdown — set it to `supersampling`
   (leave Variant blank).
6. Menu bar: **RenderScale → Build AssetBundle**.
7. The bundle file `supersampling` will appear in `BuiltBundles/`.

## Installing into the mod

Copy the produced `supersampling` file into the mod's
`Supersampling/Assets/` folder. The csproj already copies anything under
`Assets/**` into the mod output, so the next build will deploy it alongside
the DLL.

## When to rebuild

Only when you change the shader. The compiled bundle does not depend on the
mod's C# code.
