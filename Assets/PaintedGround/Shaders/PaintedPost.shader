// Painted/Post  -  the "paper" pass: grain, vignette and a biome colour grade.
//
// Runs through URP's built-in Full Screen Pass Renderer Feature (no custom render pass to
// maintain). Tools > Painted Ground > Add Painted Post Effect to Renderer sets it up, or add
// the feature by hand: Renderer Data > Add Renderer Feature > Full Screen Pass Renderer
// Feature, Pass Material = M_PaintedPost, Injection Point = After Rendering Post Processing,
// Fetch Color Buffer = on.
//
// What it does, in order
//   1. Biome grade : lift / gamma / gain (shadows / mids / highlights tint) + saturation.
//                    Drive these from a script per biome and season.
//   2. Optional LUT: a 2D strip LUT (e.g. 1024 x 32, the URP/Photoshop layout) blended in.
//   3. Paper grain : a tileable grain texture in screen space, multiplied in softly. Screen
//                    space is the point: it sits on top of 2D sprites and 3D ground alike,
//                    which is what glues them together.
//   4. Vignette    : soft oval darkening with a slightly ragged edge.

Shader "Painted/Post"
{
    Properties
    {
        [Header(Grade)]
        _Lift  ("Lift (shadows)",    Color) = (0.50, 0.50, 0.50, 1)
        _Gamma ("Gamma (midtones)",  Color) = (0.50, 0.50, 0.50, 1)
        _Gain  ("Gain (highlights)", Color) = (0.50, 0.50, 0.50, 1)
        _Saturation ("Saturation", Range(0, 2)) = 0.92
        _Contrast   ("Contrast",   Range(0.5, 1.5)) = 1.05

        [Header(LUT)]
        [Toggle(_LUT)] _UseLut ("Use LUT strip", Float) = 0
        [NoScaleOffset] _Lut ("LUT strip (width = height*height)", 2D) = "white" {}
        _LutSize ("LUT size", Float) = 32
        _LutBlend ("LUT blend", Range(0, 1)) = 1

        [Header(Paper grain)]
        [NoScaleOffset] _GrainTex ("Grain (tileable, linear)", 2D) = "gray" {}
        _GrainScale    ("Grain tile size (px)", Float) = 512
        _GrainStrength ("Grain strength", Range(0, 1)) = 0.16
        _GrainShadowBias ("More grain in shadows", Range(0, 1)) = 0.6

        [Header(Vignette)]
        _VignetteColor ("Vignette colour", Color) = (0.02, 0.02, 0.03, 1)
        _VignetteRadius ("Radius", Range(0.3, 1.5)) = 0.95
        _VignetteSoftness ("Softness", Range(0.05, 1)) = 0.55
        _VignetteRagged ("Ragged edge", Range(0, 0.3)) = 0.08
        _VignetteStrength ("Strength", Range(0, 1)) = 0.75
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PaintedPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _LUT

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Lift, _Gamma, _Gain;
                half  _Saturation, _Contrast;
                float _LutSize;
                half  _LutBlend;
                float _GrainScale;
                half  _GrainStrength, _GrainShadowBias;
                half4 _VignetteColor;
                half  _VignetteRadius, _VignetteSoftness, _VignetteRagged, _VignetteStrength;
            CBUFFER_END

            TEXTURE2D(_GrainTex);  SAMPLER(sampler_GrainTex);
            TEXTURE2D(_Lut);       SAMPLER(sampler_Lut);

            // Strip LUT lookup: width = size*size, height = size, blue selects the slice.
            half3 ApplyStripLut(half3 c, float size)
            {
                float2 texel = float2(1.0 / (size * size), 1.0 / size);
                float slice = c.b * (size - 1.0);
                float s0 = floor(slice), f = slice - s0;
                float2 uv0 = float2((s0 * size + c.r * (size - 1.0) + 0.5) * texel.x, (c.g * (size - 1.0) + 0.5) * texel.y);
                float2 uv1 = uv0 + float2(size * texel.x, 0);
                half3 a = SAMPLE_TEXTURE2D(_Lut, sampler_Lut, uv0).rgb;
                half3 b = SAMPLE_TEXTURE2D(_Lut, sampler_Lut, uv1).rgb;
                return lerp(a, b, f);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // ---- 1. grade -------------------------------------------------------------
                // 0.5 = neutral for each colour; asymmetric so it reads like a painter's tint
                half3 lift  = (_Lift.rgb  - 0.5h) * 0.6h;
                half3 gamma = (_Gamma.rgb - 0.5h) * 1.2h;
                half3 gain  = _Gain.rgb * 2.0h;
                col = saturate(col);
                col = pow(max(col * gain + lift * (1.0h - col), 1e-4h), 1.0h / max(1.0h + gamma, 0.05h));
                half lum = Luminance(col);
                col = lerp(lum.xxx, col, _Saturation);
                col = lerp(half3(0.5h, 0.5h, 0.5h), col, _Contrast);

                // ---- 2. LUT ---------------------------------------------------------------
            #if defined(_LUT)
                col = lerp(col, ApplyStripLut(saturate(col), _LutSize), _LutBlend);
            #endif

                // ---- 3. paper grain -------------------------------------------------------
                float2 px = uv * _ScreenParams.xy;
                half g = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, px / max(_GrainScale, 1.0)).r;
                half g2 = SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, px / max(_GrainScale * 0.37, 1.0) + 0.5).g;
                half grain = (g * 0.7h + g2 * 0.3h) - 0.5h;               // -0.5 .. 0.5
                half shadowWeight = lerp(1.0h, 1.0h - lum, _GrainShadowBias);
                col *= 1.0h + grain * _GrainStrength * 2.0h * shadowWeight;

                // ---- 4. vignette ----------------------------------------------------------
                float2 v = (uv - 0.5) * float2(1.0, 0.85) * 2.0;
                float r = length(v);
                half rag = (g2 - 0.5h) * _VignetteRagged;
                half vig = smoothstep(_VignetteRadius - _VignetteSoftness, _VignetteRadius + rag, r);
                col = lerp(col, _VignetteColor.rgb, vig * _VignetteStrength);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
