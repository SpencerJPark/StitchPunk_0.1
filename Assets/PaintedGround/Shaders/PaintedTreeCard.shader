// Painted/TreeCard  -  upright, two-sided, alpha-clipped tree cards baked into one mesh per tile (TerrainCardBatchMeshBuilder).
// Vertex data: uv = atlas sprite uv, uv2 = (0/1 across, 0 base / 1 top), uv3 = card (width, height) metres, colour.r = per-card tint 0..1.
// Cards billboard to the camera yaw in the vertex shader from _TerrainCardFacingRight; never pitch (CameraWorldLean leans them).

Shader "Painted/TreeCard"
{
    Properties
    {
        [MainTexture] _BaseMap ("Card atlas", 2D) = "white" {}
        _TintA ("Tint at 0", Color) = (0.85, 0.9, 0.8, 1)
        _TintB ("Tint at 1", Color) = (1.05, 1.0, 0.85, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _BaseDarken ("Darken toward the base", Range(0, 1)) = 0.25

        [Header(Lighting)]
        _AmbientTint     ("Ambient tint", Color) = (1, 1, 1, 1)
        _ShadowStrength  ("Main light shadow strength", Range(0, 1)) = 0.6
        _NdotLInfluence  ("Main light N.L influence", Range(0, 1)) = 0.1
        _PointLightBoost ("Point light boost", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _TintA;
            float4 _TintB;
            float4 _AmbientTint;
            half   _Cutoff;
            half   _BaseDarken;
            half   _ShadowStrength;
            half   _NdotLInfluence;
            half   _PointLightBoost;
        CBUFFER_END

        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _TintA)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _TintA UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _TintA)
        #endif

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        // Global set by the camera yaw driver: xyz = flat camera right in world, w = 1 when valid.
        float4 _TerrainCardFacingRight;

        // Yaw-only billboard around the card's base centre; CameraWorldLean owns pitch.
        void BillboardCardToCameraYaw(float3 positionOS, float3 normalOS, float2 cardCorner01, float2 cardSizeMetres,
                                      out float3 positionWS, out float3 normalWS)
        {
            float3 upWS = float3(0, 1, 0);
            positionWS = TransformObjectToWorld(positionOS);
            float3 builtNormalWS = normalize(TransformObjectToWorldDir(normalOS));
            float3 builtRightWS = normalize(cross(upWS, -builtNormalWS));
            // A part hidden with a tiny scale must stay tiny after billboarding.
            float scaleAcross = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
            float scaleUp = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
            float acrossMetres = (cardCorner01.x - 0.5) * cardSizeMetres.x * scaleAcross;
            float upMetres = cardCorner01.y * cardSizeMetres.y * scaleUp;
            float3 baseCentreWS = positionWS - builtRightWS * acrossMetres - upWS * upMetres;

            // No camera facing set (Scene view, edit mode): face -Z like the fixed game camera; the built axis carries a random scatter yaw.
            float3 facingRightWS = float3(1, 0, 0);
            if (_TerrainCardFacingRight.w >= 0.5 && length(_TerrainCardFacingRight.xyz) > 1e-4)
                facingRightWS = normalize(_TerrainCardFacingRight.xyz);

            positionWS = baseCentreWS + facingRightWS * acrossMetres + upWS * upMetres;
            // right = +X gives -Z, toward a camera looking along +Z.
            normalWS = cross(upWS, facingRightWS);
        }

        half SampleCardAlpha(float2 uv)
        {
            return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
        }
        ENDHLSL

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD2;
                float2 uv3        : TEXCOORD3;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  heightFraction : TEXCOORD3;
                float  tint01     : TEXCOORD4;
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
                BillboardCardToCameraYaw(IN.positionOS.xyz, IN.normalOS, IN.uv2, IN.uv3, OUT.positionWS, OUT.normalWS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.heightFraction = IN.uv2.y;
                OUT.tint01     = IN.color.r;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(texel.a - _Cutoff);

                // A back face is the same card seen from behind; light it as if its normal faced the viewer.
                float3 normalWS = normalize(IN.normalWS) * (isFrontFace ? 1 : -1);

                float3 albedo = texel.rgb * lerp(_TintA.rgb, _TintB.rgb, IN.tint01);
                albedo *= lerp(1 - _BaseDarken, 1, saturate(IN.heightFraction * 2));

                float3 lighting;
                PaintedLighting_float(IN.positionWS, normalWS, GetNormalizedScreenSpaceUV(IN.positionCS), _AmbientTint.rgb,
                                      _ShadowStrength, _NdotLInfluence, _PointLightBoost, lighting);

                float3 colour = MixFog(albedo * lighting, IN.fogFactor);
                return half4((half3)colour, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------
        // Two-sided on purpose: the fixed sun travels south toward the camera, so it sees the cards' back faces.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD2;
                float2 uv3        : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                float3 positionWS;
                float3 normalWS;
                BillboardCardToCameraYaw(IN.positionOS.xyz, IN.normalOS, IN.uv2, IN.uv3, positionWS, normalWS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                // Face the normal toward the light so the normal bias pushes away from it on either side of the card.
                normalWS *= sign(dot(normalWS, lightDirectionWS) + 1e-5);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                clip(SampleCardAlpha(IN.uv) - _Cutoff);
                return 0;
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
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD2;
                float2 uv3        : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 billboardPositionWS;
                float3 billboardNormalWS;
                BillboardCardToCameraYaw(IN.positionOS.xyz, IN.normalOS, IN.uv2, IN.uv3, billboardPositionWS, billboardNormalWS);
                OUT.positionCS = TransformWorldToHClip(billboardPositionWS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                clip(SampleCardAlpha(IN.uv) - _Cutoff);
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
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD2;
                float2 uv3        : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 billboardPositionWS;
                float3 billboardNormalWS;
                BillboardCardToCameraYaw(IN.positionOS.xyz, IN.normalOS, IN.uv2, IN.uv3, billboardPositionWS, billboardNormalWS);
                OUT.positionCS = TransformWorldToHClip(billboardPositionWS);
                OUT.normalWS   = billboardNormalWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                clip(SampleCardAlpha(IN.uv) - _Cutoff);
                return half4(normalize(IN.normalWS) * (isFrontFace ? 1 : -1), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
