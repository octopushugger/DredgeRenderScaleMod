using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Winch.Core;

namespace Supersampling
{
	public class Supersampling : MonoBehaviour
	{
		internal const string MOD_GUID = "OctopusHugger.Supersampling";
		internal const string BUNDLE_NAME = "supersampling";
		internal const string SHADER_NAME = "Hidden/Supersampling/AreaDownsample";

		internal static float RenderScale = 2.0f;
		internal static string Algorithm = "Catrom";
		internal static bool DisablePostProcessing = false;
		internal static Material DownsampleMaterial;

		private Harmony _harmony;
		private AssetBundle _bundle;

		public void Awake()
		{
			LoadConfig();
			LoadShaderBundle();
			TryPatchFinalBlit();
			DownsamplePatch.Initialize();
			ApplyRenderScale();
			WinchCore.Log.Info($"Supersampling loaded — scale={RenderScale}x, algorithm={Algorithm}, shader={(DownsampleMaterial != null ? "OK" : "MISSING (bilinear fallback)")}");
		}

		private void LoadConfig()
		{
			try
			{
				var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				var configPath = Path.Combine(modDir, "config.json");
				if (File.Exists(configPath))
				{
					var json = File.ReadAllText(configPath);
					var cfg = JsonConvert.DeserializeObject<Config>(json);
					if (cfg != null)
					{
						RenderScale = cfg.RenderScale;
						if (!string.IsNullOrEmpty(cfg.Algorithm)) Algorithm = cfg.Algorithm;
						DisablePostProcessing = cfg.DisablePostProcessing;
					}
				}
				else
				{
					File.WriteAllText(configPath, DefaultConfigText());
				}
			}
			catch (Exception e)
			{
				WinchCore.Log.Error($"Failed to load config: {e}");
			}

			RenderScale = Mathf.Clamp(RenderScale, 0.25f, 8.0f);

			// Hard limit: GPU max texture size. D3D11 = 16384.
			// Past this, RT allocation silently fails → black screen.
			int maxTex = SystemInfo.maxTextureSize;
			int largestAxis = Mathf.Max(Screen.width, Screen.height);
			float maxScale = (float)maxTex / largestAxis;
			// Leave a small margin (some RT formats round up).
			maxScale = Mathf.Floor(maxScale * 100f) / 100f;
			if (RenderScale > maxScale)
			{
				WinchCore.Log.Warn($"Requested RenderScale={RenderScale}x exceeds GPU texture cap (maxTex={maxTex}, screen={Screen.width}x{Screen.height}). Clamping to {maxScale}x.");
				RenderScale = maxScale;
			}
		}

		private void LoadShaderBundle()
		{
			try
			{
				var modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
				var bundlePath = Path.Combine(modDir, "Assets", BUNDLE_NAME);
				if (!File.Exists(bundlePath))
				{
					WinchCore.Log.Warn($"Shader bundle not found at {bundlePath} — using bilinear fallback");
					return;
				}

				_bundle = AssetBundle.LoadFromFile(bundlePath);
				if (_bundle == null)
				{
					WinchCore.Log.Error("AssetBundle.LoadFromFile returned null");
					return;
				}

				var shader = _bundle.LoadAsset<Shader>("AreaDownsample");
				if (shader == null)
				{
					// Try to find by full shader name as a fallback.
					foreach (var s in _bundle.LoadAllAssets<Shader>())
					{
						if (s.name == SHADER_NAME || s.name.EndsWith("AreaDownsample"))
						{
							shader = s;
							break;
						}
					}
				}

				if (shader == null)
				{
					WinchCore.Log.Error("Shader not found inside bundle");
					return;
				}

				DownsampleMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			}
			catch (Exception e)
			{
				WinchCore.Log.Error($"Failed to load shader bundle: {e}");
			}
		}

		private void TryPatchFinalBlit()
		{
			var asm = typeof(UniversalRenderPipeline).Assembly;
			var finalBlitType = asm.GetType("UnityEngine.Rendering.Universal.Internal.FinalBlitPass");
			if (finalBlitType == null)
			{
				WinchCore.Log.Error("FinalBlitPass type not found — downsample disabled");
				return;
			}

			var executeMethod = finalBlitType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
			if (executeMethod == null)
			{
				WinchCore.Log.Error("FinalBlitPass.Execute not found — downsample disabled");
				return;
			}

			_harmony = new Harmony(MOD_GUID);
			_harmony.Patch(
				executeMethod,
				prefix: new HarmonyMethod(typeof(DownsamplePatch), nameof(DownsamplePatch.Execute_Prefix)));

			WinchCore.Log.Info("Patched FinalBlitPass.Execute");

			var postType = asm.GetType("UnityEngine.Rendering.Universal.Internal.PostProcessPass");
			var postExecute = postType?.GetMethod("Execute", BindingFlags.Public | BindingFlags.Instance);
			if (postExecute != null)
			{
				_harmony.Patch(
					postExecute,
					prefix:  new HarmonyMethod(typeof(DownsamplePatch), nameof(DownsamplePatch.PostProcessExecute_Prefix)),
					postfix: new HarmonyMethod(typeof(DownsamplePatch), nameof(DownsamplePatch.PostProcessExecute_Postfix)));
				WinchCore.Log.Info("Patched PostProcessPass.Execute");
			}
			else
			{
				WinchCore.Log.Warn("PostProcessPass.Execute not found");
			}
		}

		private void ApplyRenderScale()
		{
			var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			if (urpAsset == null)
			{
				WinchCore.Log.Error("UniversalRenderPipelineAsset not active — cannot apply render scale");
				return;
			}

			// URP clamps the public `renderScale` property to [0.1, 2.0] via
			// ValidateRenderScale. Bypass it by writing the backing field directly.
			var field = typeof(UniversalRenderPipelineAsset).GetField(
				"m_RenderScale", BindingFlags.NonPublic | BindingFlags.Instance);
			if (field != null)
			{
				field.SetValue(urpAsset, RenderScale);
				float actual = (float)field.GetValue(urpAsset);
				WinchCore.Log.Info($"Render scale = {actual}x ({actual * actual * 100f:F0}% pixel count) [direct field write]");
			}
			else
			{
				urpAsset.renderScale = RenderScale;
				WinchCore.Log.Warn($"m_RenderScale field not found — used clamped property setter, actual = {urpAsset.renderScale}x");
			}
		}

		public void OnDestroy()
		{
			_harmony?.UnpatchSelf();
			if (DownsampleMaterial != null) Destroy(DownsampleMaterial);
			if (_bundle != null) _bundle.Unload(true);
		}

		private class Config
		{
			public float RenderScale = 2.0f;
			public string Algorithm = "Catrom";
			public bool DisablePostProcessing = false;
		}

		private static string DefaultConfigText()
		{
			return
@"{
  // RenderScale: internal render multiplier (0.25 - 8.0).
  //   2.0 = renders at 4x pixel count, downsampled to your display resolution.
  //   0.5 = renders at 1/4 pixel count, upscaled to your display resolution.
  //   Anything above ~3.0 yields rapidly diminishing returns; mind your VRAM.
  ""RenderScale"": 2.0,

  // Algorithm: how the supersampled frame is downsampled to the display.
  //   ""Catrom""   - Catmull-Rom bicubic, 4x4 tap. Sharp, fast, mild ringing.
  //   ""Spline64"" - mpv's Spline64 kernel, 8x8 tap. Sharper than Catrom; ~4x cost.
  //   ""Default""  - do not override; let URP / Unity perform the final blit itself.
  ""Algorithm"": ""Catrom"",

  // DisablePostProcessing: when true, skip URP's post-processing (bloom, tonemap, etc.)
  //   entirely and downsample the raw rendered scene directly to the display.
  ""DisablePostProcessing"": false
}
";
		}
	}
}
