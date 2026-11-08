// Painted/Strategic - the always-loaded coarse far layer under the detail tiles.
// Hidden until a driver sets the global _StrategicLayerFade; dither-clipped, never blended.
Shader "Painted/Strategic"
{
    Properties
    {
        [NoScaleOffset] _ColourTex ("Baked colour", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _ShadeStrength ("Light colour influence", Range(0, 1)) = 0.5
        // The baked texture has no fringes, ink or macro noise, so its tones sit closer together than the real ground's; measured ~1.4x in Play.
        _Contrast ("Contrast about the pivot", Range(1, 2)) = 1.4
        _ContrastPivot ("Contrast pivot (lit ground mean)", Color) = (0.215, 0.19, 0.108, 1)
        // Final per-channel mean match against the real ground (R 0.95, G 0.88, B 0.92 measured); _Tint is DOTS-instanced so it cannot carry this.
        _ColourGain ("Colour gain after contrast", Color) = (0.95, 0.88, 0.92, 1)
        _FadeStartMetres ("Distance fade start (m from focus)", Float) = 0
        _FadeEndMetres ("Distance fade end (m from focus), 0 = off", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            float _FadeStartMetres;
            float _FadeEndMetres;
            half  _ShadeStrength;
            half  _Contrast;
            half4 _ContrastPivot;
            half4 _ColourGain;
        CBUFFER_END

        // Globals stay outside the CBUFFER so a script can drive them for every material at once.
        float _StrategicLayerFade;
        float4 _TerrainFocusPosition;
        float4 _TerrainAmbientColor;   // rgb = ambient multiplier, a = blend amount (0 = off)

        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _Tint)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _Tint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Tint)
        #endif

        // The coarse 4 m strategic mesh pokes through the 1 m real ground on bumps, and a fixed depth Offset is useless at 1000 m. Sliding each
        // vertex away from the camera along its own view ray leaves it where it is on screen but puts it behind the real ground.
        static const float StrategicDepthPushFraction = 0.0025;

        float3 PushAwayFromCamera(float3 positionWS)
        {
            float3 toVertex = positionWS - _WorldSpaceCameraPos;
            return positionWS + toVertex * StrategicDepthPushFraction;
        }

        float StrategicVisibility(float3 positionWS)
        {
            float distanceFactor = 1.0;
            if (_FadeStartMetres != 0.0 || _FadeEndMetres != 0.0)
            {
                float focusDistance = distance(positionWS.xz, _TerrainFocusPosition.xz);
                // Hidden under the detail ring, fully shown beyond it; the old direction hid it exactly where the real tiles unload.
                distanceFactor = saturate((focusDistance - _FadeStartMetres) / max(_FadeEndMetres - _FadeStartMetres, 0.001));
            }
            // Must match StrategicHandoverSpeed in Painted/Ground, which discards exactly the pixels this one keeps.
            return saturate(_StrategicLayerFade * 1.6) * distanceFactor;
        }

        void StrategicDitherClip(float4 positionCS, float visibility)
        {
            float2 pixel = floor(positionCS.xy);
            float noise = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            clip(visibility - noise - 0.0001);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back
            Offset 3, 3

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_ColourTex);
            SAMPLER(sampler_linear_clamp);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionWS = PushAwayFromCamera(TransformObjectToWorld(IN.positionOS.xyz));
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                StrategicDitherClip(IN.positionCS, StrategicVisibility(IN.positionWS));

                half3 albedo = SAMPLE_TEXTURE2D(_ColourTex, sampler_linear_clamp, IN.uv).rgb * _Tint.rgb;
                half3 lightColour = min(GetMainLight().color, half3(1, 1, 1));
                half3 ambientTint = lerp(half3(1, 1, 1), _TerrainAmbientColor.rgb, _TerrainAmbientColor.a);
                half3 colour = albedo * lerp(half3(1, 1, 1), lightColour, _ShadeStrength) * ambientTint;
                colour = max(_ContrastPivot.rgb + (colour - _ContrastPivot.rgb) * _Contrast, 0) * _ColourGain.rgb;
                return half4(colour, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Offset 3, 3

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionWS = PushAwayFromCamera(TransformObjectToWorld(IN.positionOS.xyz));
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                StrategicDitherClip(IN.positionCS, StrategicVisibility(IN.positionWS));
                return IN.positionCS.z;
            }
            ENDHLSL
        }
    }
}
