Shader "LayerScreenSpace/SimpleOutlineTest"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 0, 1)
        _OutlineWidth ("Outline Width", Float) = 2.0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "Outline"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Global texture set by the render feature
            TEXTURE2D_X(_LayerTexture);
            SAMPLER(sampler_LayerTexture);

            float4 _OutlineColor;
            float _OutlineWidth;

            float SampleLayerAlpha(float2 uv, float2 offset, float2 texelSize)
            {
                float2 sampleUV = uv + offset * texelSize * _OutlineWidth;
                return SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LayerTexture, sampleUV).a;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // Camera/source provided by Blitter
                half4 camera = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Texel size (1 pixel in UV space)
                float2 texelSize = 1.0 / _ScreenParams.xy;

                float centerAlpha = SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LayerTexture, uv).a;

                float maxAlpha = centerAlpha;
                maxAlpha = max(maxAlpha, SampleLayerAlpha(uv, float2(-1, -1), texelSize));
                maxAlpha = max(maxAlpha, SampleLayerAlpha(uv, float2( 0, -1), texelSize));
                maxAlpha = max(maxAlpha, SampleLayerAlpha(uv, float2( 1, -1), texelSize));
                maxAlpha = max(maxAlpha, SampleLayerAlpha(uv, float2(-1,  0), texelSize));
                maxAlpha = max(maxAlpha, SampleLayerAlpha(uv, float2( 1,  0), texelSize));
                maxAlpha = max(maxAlpha, SampleLayerAlpha(uv, float2(-1,  1), texelSize));
                maxAlpha = max(maxAlpha, SampleLayerAlpha(uv, float2( 0,  1), texelSize));
                maxAlpha = max(maxAlpha, SampleLayerAlpha(uv, float2( 1,  1), texelSize));

                float outline = saturate(maxAlpha - centerAlpha);
                half3 finalColor = lerp(camera.rgb, _OutlineColor.rgb, outline);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
