Shader "Custom/ColorRampShader"
{
    Properties
    {
        // The CustomEditor script will hide this and auto-fill it with your gradient
        [HideInInspector] _RampTex ("Baked Ramp Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            Texture2D _RampTex;
            SamplerState sampler_RampTex;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                // Sampling using the horizontal UV coordinate (0 to 1)
                float factor = saturate(input.uv.x);
                return _RampTex.Sample(sampler_RampTex, float2(factor, 0.5));
            }
            ENDHLSL
        }
    }
    // Link the custom Inspector interface here
    CustomEditor "RampShaderGUI"
}