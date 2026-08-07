// Copyright (c) 2026 Stitch Punk. All rights reserved.
//
// =====================================================================================
// DOTS Animation Toolkit — VAT crowd reference (architecture sections 6.1, 6.3, 6.4)
//
// Plays baked skinned animation with no skeleton, no SkinnedMeshRenderer, and no per-entity
// CPU work beyond writing three floats. This is the performance reference and the normative
// example of the hand-declared multi-pass pattern.
//
// THE MESH IS AN ORDINARY MESH. Bone influences travel in UV1 rather than through skinning
// semantics, because a plain MeshRenderer does not bind BLENDINDICES/BLENDWEIGHT — and the
// whole point of VAT is not to need a SkinnedMeshRenderer. VatTentacleBakeRunner packs them:
//
//   uv1 = (boneIndex0, boneIndex1, weight0, weight1)
//
// Two influences, which is the budget §12 R3 recommends for crowds on constrained hardware.
// =====================================================================================

Shader "DOTS Animation Toolkit/VAT Crowd Unlit"
{
    Properties
    {
        [MainTexture] _MainTex ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)

        [NoScaleOffset] _VatBoneTex ("VAT Bone Texture", 2D) = "black" {}

        // Material-level, never per-instance: a per-instance texture or layout would split the
        // BRG batch, which is exactly the cost this shader exists to avoid (§6.6).
        _VatTexelParams ("VAT Texel Params (w, h, rowsPerFrame, count)", Vector) = (16, 183, 3, 12)

        // Per-instance rows of §6.2. Material values are the fallback when no entity drives them.
        _VatFrameA ("VAT Frame A", Float) = 0
        _VatFrameB ("VAT Frame B", Float) = 0
        _VatBlend ("VAT Blend", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitInstancing.hlsl"
        #include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitVat.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _BaseColor;
            float4 _VatTexelParams;
            float _VatFrameA;
            float _VatFrameB;
            float _VatBlend;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_VatBoneTex);
        SAMPLER(sampler_VatBoneTex);

        struct VatAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 boneData : TEXCOORD1;   // (index0, index1, weight0, weight1)
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct VatVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // The one displacement, called by every pass below. Declaring it once is what makes the
        // §6.3 guarantee structural rather than a thing each pass has to remember.
        float3 ToolkitVatDisplace(float4 boneData, float3 positionOS)
        {
            float4 boneIndices = float4(boneData.x, boneData.y, 0.0, 0.0);
            float4 boneWeights = float4(boneData.z, boneData.w, 0.0, 0.0);

            return VatBoneSkin(
                _VatBoneTex, sampler_VatBoneTex,
                positionOS, boneIndices, boneWeights,
                TOOLKIT_VAT_FRAME_A, TOOLKIT_VAT_FRAME_B, TOOLKIT_VAT_BLEND,
                _VatTexelParams);
        }

        VatVaryings VatVertexCommon(VatAttributes input)
        {
            VatVaryings output = (VatVaryings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);

            float3 displacedOS = ToolkitVatDisplace(input.boneData, input.positionOS.xyz);
            output.positionCS = TransformObjectToHClip(displacedOS);

            // The normal is skinned by the same matrices as the position. Skipping it leaves a
            // deforming mesh lit as though it were still in its bind pose.
            float3 displacedNormalOS = ToolkitVatDisplace(input.boneData, input.positionOS.xyz + input.normalOS)
                - displacedOS;
            output.normalWS = TransformObjectToWorldNormal(normalize(displacedNormalOS));

            output.uv = TRANSFORM_TEX(input.uv, _MainTex);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex VatForwardVertex
            #pragma fragment VatForwardFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            VatVaryings VatForwardVertex(VatAttributes input) { return VatVertexCommon(input); }

            float4 VatForwardFragment(VatVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * TOOLKIT_BASE_COLOR;

                // Cheap wrap term so the deformation is legible without a lighting model. A crowd
                // reference that shipped flat-shaded would hide exactly the wobble it exists to show.
                float lambert = saturate(dot(normalize(input.normalWS), float3(0.3, 0.8, -0.5)) * 0.5 + 0.5);
                return float4(albedo.rgb * lambert, albedo.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex VatShadowVertex
            #pragma fragment VatShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            VatVaryings VatShadowVertex(VatAttributes input) { return VatVertexCommon(input); }

            float4 VatShadowFragment(VatVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex VatDepthVertex
            #pragma fragment VatDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            VatVaryings VatDepthVertex(VatAttributes input) { return VatVertexCommon(input); }

            float4 VatDepthFragment(VatVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex VatDepthNormalsVertex
            #pragma fragment VatDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            VatVaryings VatDepthNormalsVertex(VatAttributes input) { return VatVertexCommon(input); }

            float4 VatDepthNormalsFragment(VatVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return float4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            ZWrite On

            HLSLPROGRAM
            #pragma vertex VatMotionVertex
            #pragma fragment VatMotionFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5

            VatVaryings VatMotionVertex(VatAttributes input) { return VatVertexCommon(input); }

            // Displaced positions give correct DEPTH here. The velocity written is
            // transform-derived, not deformation-derived — §12 R6's documented v1 limitation,
            // behind _VAT_DEFORMATION_MV post-1.0.
            float4 VatMotionFragment(VatVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
