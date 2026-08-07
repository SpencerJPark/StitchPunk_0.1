// Copyright (c) 2026 Stitch Punk. All rights reserved.
//
// =====================================================================================
// DOTS Animation Toolkit — hand-written flipbook + billboard reference (section 6.1)
//
// This shader exists to be READ AND COPIED. It is the normative example of three things
// that are easy to get subtly wrong and that no ShaderGraph shows you:
//
//   1. The explicit DOTS-instancing macro block (via ToolkitInstancing.hlsl), so per-entity
//      values reach a hand-written shader.
//   2. EVERY PASS DECLARED, each calling the SAME vertex displacement. A billboard that
//      displaces only in the colour pass casts the shadow of an undisplaced quad — the
//      defect section 6.3 exists to prevent.
//   3. Slice and atlas flipbook addressing side by side, selected by a shader feature so
//      neither costs the other anything.
//
// To reuse the pieces in your own shader, include the three files from Shaders/Includes/.
// They declare no properties and read no globals of their own, so they carry none of this
// package with them.
// =====================================================================================

Shader "DOTS Animation Toolkit/Sprite Unlit"
{
    Properties
    {
        [MainTexture] _MainTex ("Texture (atlas mode)", 2D) = "white" {}
        _MainTexArray ("Texture Array (slice mode)", 2DArray) = "" {}
        [MainColor] _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)

        // Per-instance rows of the section 6.2 table. Their material values are the
        // fallback for entities that carry no matching component.
        _ImageIndex ("Image Index", Float) = 0
        _AtlasFrame ("Atlas Frame (scale.xy, offset.zw)", Vector) = (1, 1, 0, 0)
        _BillboardParams ("Billboard (mode, frozenYaw, _, _)", Vector) = (0, 0, 0, 0)

        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Toggle(_TOOLKIT_SLICE_MODE)] _SliceMode ("Slice Mode (Texture2DArray)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        // -----------------------------------------------------------------------------
        // Shared by every pass. Declaring the displacement once and calling it from each
        // pass's vertex function is the whole point of this file.
        // -----------------------------------------------------------------------------
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitInstancing.hlsl"
        #include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitBillboard.hlsl"
        #include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitFlipbook.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _BaseColor;
            float _ImageIndex;
            float4 _AtlasFrame;
            float4 _BillboardParams;
            float _Cutoff;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D_ARRAY(_MainTexArray);
        SAMPLER(sampler_MainTexArray);

        // The one displacement, called identically by all four passes.
        float3 ToolkitSpriteDisplace(float3 positionOS)
        {
            return BillboardTransform(
                positionOS,
                float3(0.0, 0.0, 0.0),
                TOOLKIT_BILLBOARD_PARAMS,
                _WorldSpaceCameraPos,
                TOOLKIT_CAMERA_FORWARD,
                UNITY_MATRIX_M,
                UNITY_MATRIX_I_M);
        }

        // The one texture fetch, so slice and atlas cannot drift between passes.
        float4 ToolkitSpriteSample(float2 uv)
        {
        #if defined(_TOOLKIT_SLICE_MODE)
            float3 sliceCoordinate = SliceUV(uv, TOOLKIT_IMAGE_INDEX);
            return SAMPLE_TEXTURE2D_ARRAY(_MainTexArray, sampler_MainTexArray, sliceCoordinate.xy, sliceCoordinate.z);
        #else
            float2 atlasUv = AtlasFrameUV(uv, TOOLKIT_ATLAS_FRAME);
            return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, atlasUv);
        #endif
        }
        ENDHLSL

        // -----------------------------------------------------------------------------
        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex SpriteForwardVertex
            #pragma fragment SpriteForwardFragment
            #pragma shader_feature_local _TOOLKIT_SLICE_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings SpriteForwardVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 displacedOS = ToolkitSpriteDisplace(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(displacedOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 SpriteForwardFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float4 sampled = ToolkitSpriteSample(input.uv);
                float4 tinted = sampled * TOOLKIT_BASE_COLOR;
                clip(tinted.a - _Cutoff);
                return tinted;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------------------
        // The pass that makes the displacement contract real: the same billboard rotation
        // runs here, so the shadow is cast by the geometry the camera actually sees.
        // -----------------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex SpriteShadowVertex
            #pragma fragment SpriteShadowFragment
            #pragma shader_feature_local _TOOLKIT_SLICE_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings SpriteShadowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 displacedOS = ToolkitSpriteDisplace(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(displacedOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 SpriteShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(ToolkitSpriteSample(input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex SpriteDepthVertex
            #pragma fragment SpriteDepthFragment
            #pragma shader_feature_local _TOOLKIT_SLICE_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings SpriteDepthVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 displacedOS = ToolkitSpriteDisplace(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(displacedOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 SpriteDepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(ToolkitSpriteSample(input.uv).a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex SpriteDepthNormalsVertex
            #pragma fragment SpriteDepthNormalsFragment
            #pragma shader_feature_local _TOOLKIT_SLICE_MODE
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings SpriteDepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 displacedOS = ToolkitSpriteDisplace(input.positionOS.xyz);
                output.positionCS = TransformObjectToHClip(displacedOS);

                // The normal is displaced by the same rotation as the position. Skipping
                // this leaves a billboarded quad reporting the normal of its unrotated
                // pose, which corrupts anything reading the normals buffer — SSAO first.
                float3 displacedNormalOS = ToolkitSpriteDisplace(input.normalOS) - ToolkitSpriteDisplace(float3(0.0, 0.0, 0.0));
                output.normalWS = TransformObjectToWorldNormal(displacedNormalOS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            float4 SpriteDepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(ToolkitSpriteSample(input.uv).a - _Cutoff);
                return float4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
