// Area Sampling downscaler — port of Dolphin's AreaSampling
// (Source/Core/.../default_pre_post_process.glsl, by Sam Belliveau & Filippo Tarpini, public domain).
// Weighs each source pixel by the area it covers in the destination pixel box.
// Mathematically perfect downscale filter; gracefully degrades to bilinear at <2x.
//
// Compile in Unity Editor → put into AssetBundle "supersampling" → ship with mod.

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

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4    _SourceSize;   // (w, h, 1/w, 1/h)
            float4    _TargetSize;   // (w, h, 1/w, 1/h)

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            // Nearest-neighbor fetch by integer pixel coords.
            float4 SampleByPixel(float2 xy)
            {
                return tex2D(_MainTex, xy * _SourceSize.zw);
            }

            // Dolphin's AreaSampling, simplified: no gamma correction,
            // no luminance restoration. Linear-space weighted average of
            // source pixels by their area within the target pixel box.
            float4 frag(v2f i) : SV_Target
            {
                float2 source_size       = _SourceSize.xy;
                float2 target_size       = _TargetSize.xy;
                float2 inv_target_size   = _TargetSize.zw;

                // Target pixel box in [0,1].
                float2 t_beg = floor(i.uv * target_size);
                float2 t_end = t_beg + float2(1.0, 1.0);

                // Map to source-pixel-space box.
                float2 beg = t_beg * inv_target_size * source_size;
                float2 end = t_end * inv_target_size * source_size;

                float2 f_beg = floor(beg);
                float2 f_end = floor(end);

                // Edge coverage (how much of the first/last pixel is inside the box).
                float area_w = 1.0 - frac(beg.x);
                float area_n = 1.0 - frac(beg.y);
                float area_e = frac(end.x);
                float area_s = frac(end.y);

                // Corner areas.
                float area_nw = area_n * area_w;
                float area_ne = area_n * area_e;
                float area_sw = area_s * area_w;
                float area_se = area_s * area_e;

                float4 avg = float4(0, 0, 0, 0);
                float2 offset = float2(0.5, 0.5);

                // Corners.
                avg += area_nw * SampleByPixel(float2(f_beg.x, f_beg.y) + offset);
                avg += area_ne * SampleByPixel(float2(f_end.x, f_beg.y) + offset);
                avg += area_sw * SampleByPixel(float2(f_beg.x, f_end.y) + offset);
                avg += area_se * SampleByPixel(float2(f_end.x, f_end.y) + offset);

                int x_range = (int)(f_end.x - f_beg.x - 0.5);
                int y_range = (int)(f_end.y - f_beg.y - 0.5);

                // Match Dolphin's DX11/12 workaround — cap loop iterations.
                const int max_iterations = 16;
                x_range = min(x_range, max_iterations);
                y_range = min(y_range, max_iterations);

                // Top + bottom edges.
                [loop]
                for (int ix = 0; ix < max_iterations; ++ix)
                {
                    if (ix < x_range)
                    {
                        float x = f_beg.x + 1.0 + float(ix);
                        avg += area_n * SampleByPixel(float2(x, f_beg.y) + offset);
                        avg += area_s * SampleByPixel(float2(x, f_end.y) + offset);
                    }
                }

                // Left + right edges, plus the entire interior.
                [loop]
                for (int iy = 0; iy < max_iterations; ++iy)
                {
                    if (iy < y_range)
                    {
                        float y = f_beg.y + 1.0 + float(iy);
                        avg += area_w * SampleByPixel(float2(f_beg.x, y) + offset);
                        avg += area_e * SampleByPixel(float2(f_end.x, y) + offset);

                        [loop]
                        for (int jx = 0; jx < max_iterations; ++jx)
                        {
                            if (jx < x_range)
                            {
                                float x = f_beg.x + 1.0 + float(jx);
                                avg += SampleByPixel(float2(x, y) + offset);
                            }
                        }
                    }
                }

                float area_corners = area_nw + area_ne + area_sw + area_se;
                float area_edges   = float(x_range) * (area_n + area_s)
                                   + float(y_range) * (area_w + area_e);
                float area_center  = float(x_range) * float(y_range);

                return avg / (area_corners + area_edges + area_center);
            }
            ENDHLSL
        }
    }
}
