Shader "StitchPunk/Glass"
{
    Properties
    {
        _GlassTint ("Glass Tint (rgb colour, a body opacity)", Color) = (0.55, 0.8, 0.75, 0.35)
        _EdgeOpacity ("Edge Opacity", Range(0, 1)) = 0.6
        _EdgePower ("Edge Power", Range(0.5, 8)) = 3
        [HDR] _GlintColor ("Glint Colour", Color) = (1, 1, 1, 1)
        _GlintSize ("Glint Size", Range(0, 1)) = 0.15
        _GlintHardness ("Glint Hardness", Range(0, 1)) = 0.9
        _AmbientTint ("Ambient Tint", Color) = (1, 1, 1, 1)
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.6
        _NdotLInfluence ("NdotL Influence", Range(0, 1)) = 0.15
        _PointLightBoost ("Point Light Boost", Range(0, 4)) = 1
        _ShadowTintStrength ("Coloured Shadow Strength", Range(0, 1)) = 0.8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // One CBUFFER shared by both passes keeps the shader SRP Batcher compatible.
        CBUFFER_START(UnityPerMaterial)
            float4 _GlassTint;
            float4 _GlintColor;
            float4 _AmbientTint;
            float  _EdgeOpacity;
            float  _EdgePower;
            float  _GlintSize;
            float  _GlintHardness;
            float  _ShadowStrength;
            float  _NdotLInfluence;
            float  _PointLightBoost;
            float  _ShadowTintStrength;
            float  _Cull;
        CBUFFER_END

        // Entities Graphics / BatchRendererGroup: at least one DOTS instanced property is required.
        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _GlassTint)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _GlassTint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _GlassTint)
        #endif
        ENDHLSL

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "GlassForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

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
            #include "Assets/PaintedGround/Shaders/Include/PaintedGroundCore.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                float3 normalWS = normalize(input.normalWS);
                // Cull Off draws back faces too; flip so the fresnel and glint see the camera-facing normal.
                normalWS = isFrontFace ? normalWS : -normalWS;
                float3 viewDirectionWS = normalize(GetWorldSpaceNormalizeViewDir(positionWS));

                float3 lighting;
                PaintedLighting_float(positionWS, normalWS, GetNormalizedScreenSpaceUV(input.positionCS), _AmbientTint.rgb,
                                      _ShadowStrength, _NdotLInfluence, _PointLightBoost, lighting);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS), positionWS, half4(1, 1, 1, 1));
                float3 halfVector = normalize(mainLight.direction + viewDirectionWS);
                float halfVectorAlignment = saturate(dot(normalWS, halfVector));
                float glintEdgeWidth = max(lerp(0.2, 0.001, _GlintHardness), 0.0001);
                float glintThreshold = 1.0 - _GlintSize;
                float glintMask = smoothstep(glintThreshold - glintEdgeWidth, glintThreshold + glintEdgeWidth, halfVectorAlignment)
                                  * step(0.0001, _GlintSize) * mainLight.shadowAttenuation;
                float3 glint = _GlintColor.rgb * glintMask;

                float fresnel = pow(1.0 - saturate(dot(normalWS, viewDirectionWS)), _EdgePower);
                float alpha = saturate(_GlassTint.a + fresnel * _EdgeOpacity + glintMask * _GlintColor.a);

                float3 colour = _GlassTint.rgb * lighting + glint;
                colour = MixFog(colour, input.fogFactor);
                return half4((half3)colour, (half)alpha);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------
        // Rendered by a render feature from the sun's point of view; contract names are fixed.
        Pass
        {
            Name "StitchTintedShadow"
            Tags { "LightMode" = "StitchTintedShadow" }
            ZTest Always
            ZWrite Off
            Cull Off
            Blend DstColor Zero, One One
            BlendOp Add, Min

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            float4x4 _StitchTintedShadowWorldToLight;

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
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float depth01 = saturate(mul(_StitchTintedShadowWorldToLight, float4(input.positionWS, 1.0)).z);
                float3 shadowColour = lerp(float3(1, 1, 1), _GlassTint.rgb, _ShadowTintStrength);
                return float4(shadowColour, depth01);
            }
            ENDHLSL
        }
    }
}
