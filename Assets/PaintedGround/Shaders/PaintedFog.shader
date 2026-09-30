// Painted/Fog  -  drifting ground fog on flat horizontal cards. Soft-particle fade against
// the depth texture so cards never show a hard line where they cut the ground, plus a
// painted, thresholded body so it reads as brushed mist rather than a smooth gradient.
// Entities Graphics compatible.

Shader "Painted/Fog"
{
    Properties
    {
        [NoScaleOffset] _NoiseTex ("Noise (tileable)", 2D) = "gray" {}
        _Color ("Fog colour (A = max opacity)", Color) = (0.62, 0.66, 0.70, 0.45)
        _NoiseSize ("Noise size (m)", Float) = 6
        _Speed ("Drift speed", Range(0, 0.5)) = 0.05
        _Threshold ("Body threshold", Range(0.2, 0.8)) = 0.48
        _Softness ("Body softness", Range(0.02, 0.5)) = 0.18
        _EdgeFade ("Card edge fade", Range(0.05, 0.5)) = 0.35
        _DepthFade ("Depth fade (m)", Range(0.05, 3)) = 0.8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"     // after water
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Fog"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _NoiseSize;
                half   _Speed;
                half   _Threshold;
                half   _Softness;
                half   _EdgeFade;
                float  _DepthFade;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _Color)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Color UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Color)
            #endif

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            float DepthToEye(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                float ortho01 = 1.0 - rawDepth;
            #else
                float ortho01 = rawDepth;
            #endif
                float orthoEye = lerp(_ProjectionParams.y, _ProjectionParams.z, ortho01);
                float perspEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                return lerp(perspEye, orthoEye, unity_OrthoParams.w);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 wxz = IN.positionWS.xz;
                float  t   = _Time.y * _Speed;
                half n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, wxz / _NoiseSize + float2(t, t * 0.35)).r;
                half n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, wxz / (_NoiseSize * 2.3) - float2(t * 0.5, t * 0.8) + 0.3).b;
                half body = smoothstep(_Threshold - _Softness, _Threshold + _Softness, n1 * 0.6h + n2 * 0.4h);

                // oval fade at the card edge so cards never show their rectangle
                float2 e = abs(IN.uv - 0.5) * 2.0;
                half edge = 1.0h - smoothstep(1.0h - _EdgeFade, 1.0h, length(e));

                // soft-particle fade where the card intersects geometry
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float sceneEye = DepthToEye(SampleSceneDepth(screenUV));
                float cardEye  = DepthToEye(IN.positionCS.z);
                half depthFade = saturate((sceneEye - cardEye) / _DepthFade);

                half4 c = (half4)_Color;
                half alpha = c.a * body * edge * depthFade;

                // lit by ambient + main light so it takes the scene's mood
                Light mainLight = GetMainLight();
                half3 col = c.rgb * (SampleSH(half3(0, 1, 0)) + mainLight.color * 0.6h);
                col = MixFog(col, IN.fogFactor);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
