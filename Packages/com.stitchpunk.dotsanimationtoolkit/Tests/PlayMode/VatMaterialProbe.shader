// Copyright (c) 2026 Stitch Punk. All rights reserved.
//
// Test-only shader. It exists so the baking acceptance suite can build a VAT material that is
// *correctly* configured — one that genuinely declares the _VatBoneTex and _VatPosTex slots
// RigTargetBaker looks for.
//
// Without it every fixture material short-circuits on "declares no such slot", which is a
// different branch from the section 4.4 mismatch the acceptance list actually specifies, and
// leaves the validator unconstrained against false positives: it could warn on every correctly
// set up part and the suite would stay green.
//
// It renders nothing meaningful on purpose. Nothing bakes or draws with it; only Material.
// HasProperty and Material.GetTexture are ever asked about it.
//
// URP, not the built-in pipeline, even though it is never rendered. Section 6 makes this package
// URP-only and M4's C5 acceptance is that every shader in it compiles for the URP target with
// warnings as errors — a sweep that would pick this file up wherever it sits. It also ships in the
// published tarball unless Tests/ is excluded, so a consumer project imports and variant-compiles
// it; a built-in-pipeline shader inside a URP-only package is a support ticket waiting to happen.
// The two texture properties are the only thing the tests read, and they are pipeline-agnostic.

Shader "Hidden/StitchPunk/AnimationToolkit/Tests/VatMaterialProbe"
{
    Properties
    {
        _VatBoneTex ("VAT Bone Matrix Texture", 2D) = "black" {}
        _VatPosTex ("VAT Vertex Position Texture", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                return half4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
