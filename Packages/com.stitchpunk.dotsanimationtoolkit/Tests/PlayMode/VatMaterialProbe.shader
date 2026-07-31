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

Shader "Hidden/StitchPunk/AnimationToolkit/Tests/VatMaterialProbe"
{
    Properties
    {
        _VatBoneTex ("VAT Bone Matrix Texture", 2D) = "black" {}
        _VatPosTex ("VAT Vertex Position Texture", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
