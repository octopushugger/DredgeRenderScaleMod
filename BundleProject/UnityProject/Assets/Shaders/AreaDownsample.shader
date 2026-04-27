// Supersampling downscaler — two passes:
//   Pass 0: Catmull-Rom bicubic (a = -0.5).        Native radius 2.
//   Pass 1: mpv's Spline64 (filter_kernels.c).     Native radius 4.
//
// Both passes scale their kernel support proportionally to the downsample
// ratio (scale = source_size / target_size, clamped >= 1). At 1:1 or
// upsample, the kernel runs at native radius. At Nx downsample, the
// effective radius becomes N*native_radius source pixels — the kernel
// then acts as both an anti-alias prefilter and a reconstruction filter.

Shader "Hidden/Supersampling/AreaDownsample"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _SourceSize ("Source size (w, h, 1/w, 1/h)", Vector) = (1, 1, 1, 1)
        _TargetSize ("Target size (w, h, 1/w, 1/h)", Vector) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        // Pass 0: Catmull-Rom bicubic. Native radius = 2.
        // MAX_R = 16 supports kernel scaling up to 8x downsample.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0

            #include "UnityCG.cginc"

            #define KERNEL_RADIUS 2.0
            #define MAX_R         16

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4    _SourceSize;
            float4    _TargetSize;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            float kernelW(float x)
            {
                x = abs(x);
                float x2 = x * x;
                float x3 = x2 * x;
                if (x < 1.0)  return  1.5 * x3 - 2.5 * x2 + 1.0;
                if (x < 2.0)  return -0.5 * x3 + 2.5 * x2 - 4.0 * x + 2.0;
                return 0.0;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 src_pos   = i.uv * _SourceSize.xy - 0.5;
                float2 src_floor = floor(src_pos);
                float2 f         = src_pos - src_floor;

                // Stretch kernel so its support covers `scale` source pixels
                // per kernel unit. For upscale (scale < 1) clamp to 1.
                float2 scale     = max(_SourceSize.xy / _TargetSize.xy, 1.0);
                float2 inv_scale = 1.0 / scale;

                float4 sum    = 0.0;
                float  totalW = 0.0;

                [loop]
                for (int dy = -MAX_R; dy <= MAX_R + 1; ++dy)
                {
                    float ky = (float(dy) - f.y) * inv_scale.y;
                    if (abs(ky) >= KERNEL_RADIUS) continue;
                    float wy = kernelW(ky);

                    [loop]
                    for (int dx = -MAX_R; dx <= MAX_R + 1; ++dx)
                    {
                        float kx = (float(dx) - f.x) * inv_scale.x;
                        if (abs(kx) >= KERNEL_RADIUS) continue;
                        float wx = kernelW(kx);
                        float w  = wx * wy;
                        float2 uv = (src_floor + float2(dx, dy) + 0.5) * _SourceSize.zw;
                        sum    += w * tex2Dlod(_MainTex, float4(uv, 0, 0));
                        totalW += w;
                    }
                }

                return sum / totalW;
            }
            ENDCG
        }

        // Pass 1: Spline64 (mpv). Native radius = 4.
        // MAX_R = 32 supports kernel scaling up to 8x downsample.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.0

            #include "UnityCG.cginc"

            #define KERNEL_RADIUS 4.0
            #define MAX_R         32

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 pos    : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4    _SourceSize;
            float4    _TargetSize;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            float kernelW(float x)
            {
                x = abs(x);
                if (x < 1.0)
                    return ((49.0/41.0 * x - 6387.0/2911.0) * x - 3.0/2911.0) * x + 1.0;
                if (x < 2.0)
                {
                    float t = x - 1.0;
                    return ((-24.0/41.0 * t + 4032.0/2911.0) * t - 2328.0/2911.0) * t;
                }
                if (x < 3.0)
                {
                    float t = x - 2.0;
                    return ((6.0/41.0 * t - 1008.0/2911.0) * t + 582.0/2911.0) * t;
                }
                if (x < 4.0)
                {
                    float t = x - 3.0;
                    return ((-1.0/41.0 * t + 168.0/2911.0) * t - 97.0/2911.0) * t;
                }
                return 0.0;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 src_pos   = i.uv * _SourceSize.xy - 0.5;
                float2 src_floor = floor(src_pos);
                float2 f         = src_pos - src_floor;

                float2 scale     = max(_SourceSize.xy / _TargetSize.xy, 1.0);
                float2 inv_scale = 1.0 / scale;

                float4 sum    = 0.0;
                float  totalW = 0.0;

                [loop]
                for (int dy = -MAX_R; dy <= MAX_R + 1; ++dy)
                {
                    float ky = (float(dy) - f.y) * inv_scale.y;
                    if (abs(ky) >= KERNEL_RADIUS) continue;
                    float wy = kernelW(ky);

                    [loop]
                    for (int dx = -MAX_R; dx <= MAX_R + 1; ++dx)
                    {
                        float kx = (float(dx) - f.x) * inv_scale.x;
                        if (abs(kx) >= KERNEL_RADIUS) continue;
                        float wx = kernelW(kx);
                        float w  = wx * wy;
                        float2 uv = (src_floor + float2(dx, dy) + 0.5) * _SourceSize.zw;
                        sum    += w * tex2Dlod(_MainTex, float4(uv, 0, 0));
                        totalW += w;
                    }
                }

                return sum / totalW;
            }
            ENDCG
        }
    }
}
