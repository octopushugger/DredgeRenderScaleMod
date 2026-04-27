using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Winch.Core;

namespace Supersampling
{
	internal static class DownsamplePatch
	{
		// Shader pass indices in Hidden/Supersampling/AreaDownsample.
		private const int PassCatrom   = 0;
		private const int PassSpline64 = 1;

		private static readonly int SourceSizeId = Shader.PropertyToID("_SourceSize");
		private static readonly int TargetSizeId = Shader.PropertyToID("_TargetSize");

		private static bool _initialized;

		// FinalBlitPass reflection.
		private static FieldInfo _sourceField;

		// PostProcessPass reflection.
		private static FieldInfo _ppResolveField;
		private static FieldInfo _ppIsFinalField;
		private static FieldInfo _ppHasFinalField;
		private static FieldInfo _ppDestField;
		private static FieldInfo _ppUseSwapField;
		private static FieldInfo _ppSourceField;
		private static MethodInfo _rtHandleInitFromIdentifier;

		// Owned post-process destination RT (allocated to supersampled size).
		private static RenderTexture _ownedRT;

		[ThreadStatic] private static bool   _ppOverrideThisCall;
		[ThreadStatic] private static bool   _ppOriginalResolve;
		[ThreadStatic] private static bool   _ppOriginalUseSwap;
		[ThreadStatic] private static bool   _ppHadUseSwap;
		[ThreadStatic] private static object _ppOriginalDestination;

		internal static void Initialize() => _initialized = true;

		// Returns -1 if the algorithm is "Default" (or unknown) → don't override.
		private static int ResolvePassIndex()
		{
			var a = Supersampling.Algorithm;
			if (string.Equals(a, "Catrom",   StringComparison.OrdinalIgnoreCase)) return PassCatrom;
			if (string.Equals(a, "Spline64", StringComparison.OrdinalIgnoreCase)) return PassSpline64;
			return -1;
		}

		private static void EnsureOwnedRT(RenderTextureDescriptor desc)
		{
			desc.depthBufferBits = 0;
			if (_ownedRT != null
				&& _ownedRT.width == desc.width
				&& _ownedRT.height == desc.height
				&& _ownedRT.format == desc.colorFormat)
				return;

			if (_ownedRT != null) { _ownedRT.Release(); UnityEngine.Object.Destroy(_ownedRT); }
			_ownedRT = new RenderTexture(desc)
			{
				name = "SupersamplingOwnedPP",
				hideFlags = HideFlags.HideAndDontSave,
			};
			_ownedRT.Create();
			WinchCore.Log.Info($"Allocated owned PP RT: {_ownedRT.width}x{_ownedRT.height} fmt={_ownedRT.format}");
		}

		// Single shader blit. mat must already have _SourceSize/_TargetSize set.
		private static void BlitWithShader(
			ScriptableRenderContext context,
			RenderTargetIdentifier source,
			RenderTargetIdentifier finalTarget,
			Material mat,
			int passIndex,
			CameraData cameraData,
			string profilerName)
		{
			var cmd = CommandBufferPool.Get(profilerName);
			cmd.SetRenderTarget(finalTarget,
				RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
				RenderBufferLoadAction.DontCare, RenderBufferStoreAction.DontCare);
			cmd.Blit(source, finalTarget, mat, passIndex);
			context.ExecuteCommandBuffer(cmd);
			CommandBufferPool.Release(cmd);

			cameraData.renderer.ConfigureCameraTarget(finalTarget, finalTarget);
		}

		private static void ApplySizes(Material mat, int srcW, int srcH, int dstW, int dstH)
		{
			mat.SetVector(SourceSizeId, new Vector4(srcW, srcH, 1f / srcW, 1f / srcH));
			mat.SetVector(TargetSizeId, new Vector4(dstW, dstH, 1f / dstW, 1f / dstH));
		}

		// Prefix on PostProcessPass.Execute.
		// Take ownership of the destination RT so we know exactly where the
		// post-processed supersampled image lands. Return true to let PP run.
		// Return false to skip PP entirely (DisablePostProcessing path).
		private static int _ppDiagCount;

		internal static bool PostProcessExecute_Prefix(
			object __instance,
			ScriptableRenderContext context,
			ref RenderingData renderingData)
		{
			_ppOverrideThisCall = false;
			if (!_initialized) return true;

			int passIndex = ResolvePassIndex();
			if (passIndex < 0) return true;     // Default: no override
			var mat = Supersampling.DownsampleMaterial;
			if (mat == null) return true;       // shader missing

			var t = __instance.GetType();
			if (_ppResolveField  == null) _ppResolveField  = t.GetField("m_ResolveToScreen", BindingFlags.NonPublic | BindingFlags.Instance);
			if (_ppIsFinalField  == null) _ppIsFinalField  = t.GetField("m_IsFinalPass",     BindingFlags.NonPublic | BindingFlags.Instance);
			if (_ppHasFinalField == null) _ppHasFinalField = t.GetField("m_HasFinalPass",    BindingFlags.NonPublic | BindingFlags.Instance);
			if (_ppDestField     == null) _ppDestField     = t.GetField("m_Destination",     BindingFlags.NonPublic | BindingFlags.Instance);
			if (_ppUseSwapField  == null) _ppUseSwapField  = t.GetField("m_UseSwapBuffer",   BindingFlags.NonPublic | BindingFlags.Instance);
			if (_ppSourceField   == null) _ppSourceField   = t.GetField("m_Source",          BindingFlags.NonPublic | BindingFlags.Instance);
			if (_ppResolveField  == null) return true;

			bool isFinalPass = _ppIsFinalField != null && (bool)_ppIsFinalField.GetValue(__instance);
			bool resolve     = (bool)_ppResolveField.GetValue(__instance);

			var cameraData = renderingData.cameraData;
			var desc = cameraData.cameraTargetDescriptor;
			int dstW = cameraData.targetTexture != null ? cameraData.targetTexture.width  : (int)cameraData.camera.pixelRect.width;
			int dstH = cameraData.targetTexture != null ? cameraData.targetTexture.height : (int)cameraData.camera.pixelRect.height;

			if (_ppDiagCount < 6)
			{
				WinchCore.Log.Info($"PP diag cam='{cameraData.camera?.name}' isFinal={isFinalPass} resolve={resolve} desc={desc.width}x{desc.height} dst={dstW}x{dstH}");
				_ppDiagCount++;
			}

			if (desc.width == dstW && desc.height == dstH) return true; // exact 1:1

			RenderTargetIdentifier finalTarget = (cameraData.targetTexture != null)
				? new RenderTargetIdentifier(cameraData.targetTexture)
				: new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);

			// FINAL PASS PATH (only fires at scale<1 in URP 12).
			// Uber pass already wrote to our owned RT; do the upscale to screen
			// with our shader and skip URP's own final upscale.
			if (isFinalPass)
			{
				if (_ownedRT != null)
				{
					ApplySizes(mat, _ownedRT.width, _ownedRT.height, dstW, dstH);
					BlitWithShader(context, new RenderTargetIdentifier(_ownedRT), finalTarget, mat, passIndex, cameraData, "SupersamplingPPFinal");
					return false; // skip URP's final pass
				}
				return true; // no owned RT yet — let URP run
			}

			// UBER PASS PATH.

			// DisablePostProcessing diagnostic path: skip uber + final entirely,
			// downsample raw scene to screen with our shader.
			if (Supersampling.DisablePostProcessing && _ppSourceField != null)
			{
				var msource = (RenderTargetIdentifier)_ppSourceField.GetValue(__instance);
				ApplySizes(mat, desc.width, desc.height, dstW, dstH);
				BlitWithShader(context, msource, finalTarget, mat, passIndex, cameraData, "SupersamplingPPDisabled");
				return false;
			}

			// Allocate (or reuse) our destination RT at the post-process resolution.
			EnsureOwnedRT(desc);

			// Override m_Destination so PP writes into our RT.
			if (_ppDestField != null)
			{
				if (_rtHandleInitFromIdentifier == null)
					_rtHandleInitFromIdentifier = _ppDestField.FieldType.GetMethod("Init", new[] { typeof(RenderTargetIdentifier) });

				if (_rtHandleInitFromIdentifier != null)
				{
					_ppOriginalDestination = _ppDestField.GetValue(__instance);
					object newDest = Activator.CreateInstance(_ppDestField.FieldType);
					_rtHandleInitFromIdentifier.Invoke(newDest, new object[] { new RenderTargetIdentifier(_ownedRT) });
					_ppDestField.SetValue(__instance, newDest);
				}
			}

			// Disable the swap-buffer path so PP honors m_Destination.
			if (_ppUseSwapField != null)
			{
				_ppOriginalUseSwap = (bool)_ppUseSwapField.GetValue(__instance);
				_ppHadUseSwap = true;
				_ppUseSwapField.SetValue(__instance, false);
			}

			_ppOriginalResolve = resolve;
			_ppOverrideThisCall = true;
			_ppResolveField.SetValue(__instance, false);
			return true;
		}

		// Postfix on PostProcessPass.Execute.
		// Restore PP state. If no final pass is queued (downscale case), blit
		// our owned RT to screen with the shader. If a final pass IS queued
		// (upscale case in URP 12), defer — the final-pass prefix will do it.
		internal static void PostProcessExecute_Postfix(
			object __instance,
			ScriptableRenderContext context,
			ref RenderingData renderingData)
		{
			if (!_ppOverrideThisCall) return;
			_ppOverrideThisCall = false;

			if (_ppResolveField != null) _ppResolveField.SetValue(__instance, _ppOriginalResolve);
			if (_ppHadUseSwap && _ppUseSwapField != null) { _ppUseSwapField.SetValue(__instance, _ppOriginalUseSwap); _ppHadUseSwap = false; }
			if (_ppOriginalDestination != null && _ppDestField != null) { _ppDestField.SetValue(__instance, _ppOriginalDestination); _ppOriginalDestination = null; }

			int passIndex = ResolvePassIndex();
			if (passIndex < 0 || _ownedRT == null) return;
			var mat = Supersampling.DownsampleMaterial;
			if (mat == null) return;

			// If a final FXAA/upscale pass is going to follow, leave the screen
			// blit to its prefix — _ownedRT already holds the post-processed image.
			bool hasFinalPass = _ppHasFinalField != null && (bool)_ppHasFinalField.GetValue(__instance);
			if (hasFinalPass) return;

			var cameraData = renderingData.cameraData;
			RenderTargetIdentifier finalTarget = (cameraData.targetTexture != null)
				? new RenderTargetIdentifier(cameraData.targetTexture)
				: new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);

			var desc = cameraData.cameraTargetDescriptor;
			int dstW = cameraData.targetTexture != null ? cameraData.targetTexture.width  : (int)cameraData.camera.pixelRect.width;
			int dstH = cameraData.targetTexture != null ? cameraData.targetTexture.height : (int)cameraData.camera.pixelRect.height;

			ApplySizes(mat, desc.width, desc.height, dstW, dstH);
			BlitWithShader(context, new RenderTargetIdentifier(_ownedRT), finalTarget, mat, passIndex, cameraData, "SupersamplingPP");
		}

		// Prefix on FinalBlitPass.Execute.
		// Used for cameras that bypass PostProcessPass (none in DREDGE main, but
		// we keep it for cases where post-processing is fully disabled per-camera).
		// Reflection cameras (cameraData.targetTexture != null) are left alone.
		private static int _fbDiagCount;

		internal static bool Execute_Prefix(
			object __instance,
			ScriptableRenderContext context,
			ref RenderingData renderingData)
		{
			if (!_initialized) return true;

			int passIndex = ResolvePassIndex();
			if (passIndex < 0) return true;
			var mat = Supersampling.DownsampleMaterial;
			if (mat == null) return true;

			var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			float scale = urpAsset?.renderScale ?? 1.0f;

			if (_fbDiagCount < 4)
			{
				var c = renderingData.cameraData.camera;
				WinchCore.Log.Info($"FinalBlit diag cam='{c?.name}' scale={scale} targetTex={renderingData.cameraData.targetTexture != null}");
				_fbDiagCount++;
			}

			if (Mathf.Abs(scale - 1.0f) < 0.001f) return true; // exact 1:1; nothing to resample

			var cameraData = renderingData.cameraData;
			if (cameraData.targetTexture != null) return true; // reflection / RT cameras

			if (_sourceField == null)
			{
				_sourceField = __instance.GetType().GetField("m_Source", BindingFlags.NonPublic | BindingFlags.Instance);
				if (_sourceField == null)
				{
					WinchCore.Log.Error("FinalBlitPass.m_Source not found — falling back to default blit");
					_initialized = false;
					return true;
				}
			}

			var sourceObj = _sourceField.GetValue(__instance);
			RenderTargetIdentifier sourceId = ResolveSource(sourceObj);

			var desc = cameraData.cameraTargetDescriptor;
			int srcW = desc.width, srcH = desc.height;
			int dstW = Mathf.RoundToInt(srcW / scale);
			int dstH = Mathf.RoundToInt(srcH / scale);

			RenderTargetIdentifier finalTarget = new RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget);

			ApplySizes(mat, srcW, srcH, dstW, dstH);
			BlitWithShader(context, sourceId, finalTarget, mat, passIndex, cameraData, "SupersamplingFinalBlit");
			return false;
		}

		private static RenderTargetIdentifier ResolveSource(object value)
		{
			if (value is RenderTargetIdentifier rti) return rti;

			var t = value.GetType();
			var op = t.GetMethod("op_Implicit", new[] { t });
			if (op != null)
			{
				var converted = op.Invoke(null, new[] { value });
				if (converted is RenderTargetIdentifier id) return id;
			}

			var idMethod = t.GetMethod("Identifier", BindingFlags.Public | BindingFlags.Instance);
			if (idMethod != null)
			{
				var id = idMethod.Invoke(value, null);
				if (id is RenderTargetIdentifier rid) return rid;
			}

			var nameIdField = t.GetField("nameID", BindingFlags.Public | BindingFlags.Instance);
			if (nameIdField != null)
			{
				var nameId = (int)nameIdField.GetValue(value);
				return new RenderTargetIdentifier(nameId);
			}

			WinchCore.Log.Error($"Unknown m_Source type: {t.FullName}");
			return BuiltinRenderTextureType.CameraTarget;
		}
	}
}
