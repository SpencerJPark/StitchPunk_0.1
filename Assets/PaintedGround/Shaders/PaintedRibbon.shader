// Painted/Ribbon  -  road ribbon mesh that lies just above Painted/Ground (Entities Graphics compatible).
// Vertex data: uv = (signed metres across, metres along), uv2 = (ford wetness, rut fade),
// colour = (R edge wear, G rut, B shoulder, A opacity). Coverage is dithered against the noise texture.

Shader "Painted/Ribbon"
{
    Properties
    {
        [NoScaleOffset] _SurfaceTex ("Surface texture", 2D) = "white" {}
        _SurfaceTint ("Surface tint", Color) = (1, 1, 1, 1)
        _NoiseTex ("Noise (tileable)", 2D) = "white" {}
        _NoiseMetres ("Noise size (m)", Float) = 6
        _MetresPerTile ("Surface metres per tile", Float) = 4

        [Header(Ruts)]
        _RutTint ("Rut tint", Color) = (0.35, 0.28, 0.2, 1)
        _RutDarkness ("Rut darkness", Range(0, 1)) = 0.5
        _RutSharpness ("Rut sharpness", Range(1, 8)) = 3
        _RidgeLighten ("Ridge lighten", Range(0, 0.5)) = 0.08

        [Header(Edge wear)]
        _EdgeWearColor ("Edge wear colour", Color) = (0.35, 0.4, 0.2, 1)
        _EdgeWearAmount ("Edge wear amount", Range(0, 1)) = 0.5
        _EdgeWearNoiseScale ("Edge wear noise", Range(0, 1)) = 0.35

        [Header(Ford)]
        _WetTint ("Wet tint", Color) = (0.55, 0.6, 0.7, 1)
        _WetDarkness ("Wet darkness", Range(0, 1)) = 0.5

        [Header(Coverage)]
        _EdgeDitherSoftness ("Edge dither softness", Range(0, 1)) = 0.6

        [Header(Lighting)]
        _AmbientTint     ("Ambient tint", Color) = (1, 1, 1, 1)
        _ShadowStrength  ("Main light shadow strength", Range(0, 1)) = 0.6
        _NdotLInfluence  ("Main light N.L influence", Range(0, 1)) = 0.15
        _PointLightBoost ("Point light boost", Range(0, 4)) = 1
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

        // One CBUFFER shared by every pass keeps the shader SRP Batcher compatible.
        CBUFFER_START(UnityPerMaterial)
            float4 _SurfaceTint;
            float4 _AmbientTint;
            half4  _RutTint;
            half4  _EdgeWearColor;
            half4  _WetTint;
            float  _NoiseMetres;
            float  _MetresPerTile;
            half   _RutDarkness;
            half   _RutSharpness;
            half   _RidgeLighten;
            half   _EdgeWearAmount;
            half   _EdgeWearNoiseScale;
            half   _WetDarkness;
            half   _EdgeDitherSoftness;
            half   _ShadowStrength;
            half   _NdotLInfluence;
            half   _PointLightBoost;
        CBUFFER_END

        // Entities Graphics / BatchRendererGroup: at least one DOTS instanced property is required.
        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _SurfaceTint)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _SurfaceTint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _SurfaceTint)
        #endif
        ENDHLSL

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Include/PaintedGroundCore.hlsl"

            TEXTURE2D(_SurfaceTex);  SAMPLER(sampler_SurfaceTex);
            TEXTURE2D(_NoiseTex);    SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float2 uv2        : TEXCOORD3;
                float4 color      : TEXCOORD4;
                half   fogFactor  : TEXCOORD5;
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
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = IN.uv;
                OUT.uv2        = IN.uv2;
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 posWS    = IN.positionWS;
                float3 normalWS = normalize(IN.normalWS);

                float2 surfaceUV = IN.uv / _MetresPerTile;
                float4 baseColour = SAMPLE_TEXTURE2D(_SurfaceTex, sampler_SurfaceTex, surfaceUV) * _SurfaceTint;
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, IN.uv / _NoiseMetres).r;

                float edgeWear   = IN.color.r;
                float rutWeight  = IN.color.g;
                float shoulder   = IN.color.b;
                float opacity    = IN.color.a;

                float rut = saturate(rutWeight * _RutSharpness) * IN.uv2.y;
                baseColour.rgb = lerp(baseColour.rgb, baseColour.rgb * _RutTint.rgb, rut * _RutDarkness);

                float ridge = (1 - rutWeight) * (1 - edgeWear);
                baseColour.rgb += _RidgeLighten * ridge;

                float wear = smoothstep(1 - _EdgeWearAmount, 1, edgeWear + (noise - 0.5) * _EdgeWearNoiseScale);
                baseColour.rgb = lerp(baseColour.rgb, _EdgeWearColor.rgb, wear);

                baseColour.rgb = lerp(baseColour.rgb, baseColour.rgb * _WetTint.rgb, IN.uv2.x * _WetDarkness);

                float coverage = opacity * (1 - shoulder);
                clip(coverage - lerp(0.5, noise, _EdgeDitherSoftness));

                float3 lighting;
                PaintedLighting_float(posWS, normalWS, GetNormalizedScreenSpaceUV(IN.positionCS), _AmbientTint.rgb,
                                      _ShadowStrength, _NdotLInfluence, _PointLightBoost, lighting);

                float3 col = baseColour.rgb * lighting;
                col = MixFog(col, IN.fogFactor);
                return half4((half3)col, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            TEXTURE2D(_NoiseTex);  SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv    = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, IN.uv / _NoiseMetres).r;
                float coverage = IN.color.a * (1 - IN.color.b);
                clip(coverage - lerp(0.5, noise, _EdgeDitherSoftness));
                return IN.positionCS.z;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            TEXTURE2D(_NoiseTex);  SAMPLER(sampler_NoiseTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float4 color      : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv    = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, IN.uv / _NoiseMetres).r;
                float coverage = IN.color.a * (1 - IN.color.b);
                clip(coverage - lerp(0.5, noise, _EdgeDitherSoftness));
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
