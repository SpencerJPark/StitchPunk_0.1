// Painted/Cliff  -  terrace wall, grass lip and dark foot fringe in one shader (Entities Graphics compatible).
// _Mode 0 Wall: uv = float4(arc metres down from top, metres along, 10 + arc below top-fillet end (<1 = no grass), 10 + arc above foot-fillet start); rock + fillet grass.
// _Mode 1 Lip : uv = (along / fringe size, 0 outer .. 1 inner); alpha-clipped against _SurfaceTex alpha, grass tinted.
// _Mode 2 Foot: same uv; black, alpha from vertex colour alpha. Set _SrcBlend/_DstBlend/_ZWrite/_Cull per material.

Shader "Painted/Cliff"
{
    Properties
    {
        [Enum(Wall, 0, Lip, 1, Foot, 2)] _Mode ("Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0

        [NoScaleOffset] _SurfaceTex ("Surface texture (T_Cliff / T_EdgeFringe)", 2D) = "white" {}
        _MetresPerTile ("Surface metres per tile", Float) = 4

        [Header(Wall)]
        _TopColor ("Top colour", Color) = (0.22, 0.24, 0.30, 1)
        _BottomColor ("Bottom colour", Color) = (0.08, 0.08, 0.11, 1)
        _GradientMetres ("Gradient metres", Float) = 3
        _StriationStrength ("Striation strength", Range(0, 1)) = 0.6
        _StriationDensity ("Striations per metre", Range(0.5, 8)) = 2.5
        _StriationStretch ("Striation vertical stretch", Range(0.02, 1)) = 0.12
        _SurfaceStrength ("Surface texture tint strength", Range(0, 1)) = 0.45
        _GrassTuftMinMetres ("Grass tuft min metres", Float) = 0.15
        _GrassTuftMaxMetres ("Grass tuft max metres", Float) = 0.35
        _WallBladesPerMetre ("Wall grass blades per metre", Range(4, 40)) = 14
        _OverhangShadowStrength ("Shadow under tufts", Range(0, 1)) = 0.3
        _FootShadowMetres ("Foot shadow metres", Float) = 0.5
        _FootShadowStrength ("Foot shadow strength", Range(0, 1)) = 0.45
        _FootGrassStartMetres ("Foot grass start metres", Float) = 0.55

        [Header(Lip)]
        _LipColor ("Lip colour", Color) = (0.30, 0.38, 0.22, 1)
        _LipAlphaCutoff ("Lip alpha cutoff", Range(0, 1)) = 0.5
        _BladeDensity ("Grass blades per fringe tile", Range(4, 64)) = 24

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

        CBUFFER_START(UnityPerMaterial)
            float4 _AmbientTint;
            half4  _TopColor;
            half4  _BottomColor;
            half4  _LipColor;
            float  _Mode;
            float  _MetresPerTile;
            float  _GradientMetres;
            half   _StriationStrength;
            float  _StriationDensity;
            float  _StriationStretch;
            half   _SurfaceStrength;
            float  _GrassTuftMinMetres;
            float  _GrassTuftMaxMetres;
            float  _WallBladesPerMetre;
            half   _OverhangShadowStrength;
            float  _FootShadowMetres;
            half   _FootShadowStrength;
            float  _FootGrassStartMetres;
            half   _LipAlphaCutoff;
            float  _BladeDensity;
            half   _ShadowStrength;
            half   _NdotLInfluence;
            half   _PointLightBoost;
        CBUFFER_END

        // Entities Graphics / BatchRendererGroup: at least one DOTS instanced property is required.
        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _AmbientTint)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _AmbientTint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _AmbientTint)
        #endif

        TEXTURE2D(_SurfaceTex);  SAMPLER(sampler_SurfaceTex);

        float CliffHash(float2 cell)
        {
            return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
        }

        // Value noise; the caller stretches uv.x so grooves run top to bottom.
        float CliffValueNoise(float2 position)
        {
            float2 cell = floor(position);
            float2 blend = frac(position);
            blend = blend * blend * (3 - 2 * blend);
            float bottomLeft  = CliffHash(cell);
            float bottomRight = CliffHash(cell + float2(1, 0));
            float topLeft     = CliffHash(cell + float2(0, 1));
            float topRight    = CliffHash(cell + float2(1, 1));
            return lerp(lerp(bottomLeft, bottomRight, blend.x), lerp(topLeft, topRight, blend.x), blend.y);
        }

        // Wall uv.x = metres down, uv.y = metres along; texture u runs along the wall so striations stay vertical.
        float2 CliffSurfaceUV(float2 uv)
        {
            return _Mode < 0.5 ? float2(uv.y, uv.x) / _MetresPerTile : uv;
        }

        // Procedural grass blades: uv.y 1 = root (solid), 0 = tip (jagged); positive result = covered.
        float CliffBladeCoverage(float2 uv)
        {
            float scaledAlong = uv.x * _BladeDensity;
            float bladeHeight = 0.55 + 0.45 * CliffHash(float2(floor(scaledAlong), 3.7));
            float bladeTaper = 1 - 2 * abs(frac(scaledAlong) - 0.5);
            return bladeHeight * (0.25 + 0.75 * bladeTaper) - (1 - uv.y);
        }

        // Lip clips on procedural blades; Foot never writes depth/normals; Wall is always solid.
        float CliffCoverageClipValue(float2 uv, float4 vertexColor)
        {
            if (_Mode > 1.5)
                return -1;
            if (_Mode > 0.5)
                return CliffBladeCoverage(uv);
            return 1;
        }
        ENDHLSL

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 uv         : TEXCOORD2;
                float4 color      : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
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
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 normalWS = normalize(IN.normalWS);
                float4 surface = SAMPLE_TEXTURE2D(_SurfaceTex, sampler_SurfaceTex, CliffSurfaceUV(IN.uv.xy));
                float3 baseColour;
                float alpha = 1;

                if (_Mode < 0.5)
                {
                    float gradient = saturate(IN.uv.x / max(_GradientMetres, 0.001));
                    float3 slate = lerp(_TopColor.rgb, _BottomColor.rgb, gradient);
                    float2 grooveUV = float2(IN.uv.y * _StriationDensity, IN.uv.x * _StriationDensity * _StriationStretch);
                    float groove = lerp(CliffValueNoise(grooveUV), CliffValueNoise(grooveUV * float2(2.7, 1.3) + 17.3), 0.35);
                    float striation = lerp(1, 0.35 + 0.9 * groove, _StriationStrength);
                    float3 rockColour = slate * lerp(float3(1, 1, 1), surface.rgb, _SurfaceStrength) * striation;
                    baseColour = rockColour;

                    if (IN.uv.z > 1)
                    {
                        float onTopBevel = IN.uv.z - 10;
                        float aboveFootBevel = IN.uv.w - 10;

                        float3 wallColour = rockColour;
                        wallColour *= lerp(1 - _OverhangShadowStrength, 1, saturate(IN.uv.x / 0.35));
                        if (onTopBevel < 0)
                            wallColour *= lerp(1.12, 1, saturate((onTopBevel + 0.2) / 0.2));

                        wallColour *= lerp(1 - _FootShadowStrength, 1, saturate(abs(aboveFootBevel) / max(_FootShadowMetres, 0.001)));
                        baseColour = wallColour;
                    }
                }
                else if (_Mode < 1.5)
                {
                    clip(CliffBladeCoverage(IN.uv.xy));
                    baseColour = _LipColor.rgb * lerp(0.65, 1, saturate(1 - IN.uv.y));
                }
                else
                {
                    baseColour = 0;
                    alpha = IN.color.a;
                }

                float3 lighting;
                PaintedLighting_float(IN.positionWS, normalWS, GetNormalizedScreenSpaceUV(IN.positionCS), _AmbientTint.rgb,
                                      _ShadowStrength, _NdotLInfluence, _PointLightBoost, lighting);

                float3 col = _Mode > 1.5 ? baseColour : baseColour * lighting;
                col = MixFog(col, IN.fogFactor);
                return half4((half3)col, alpha);
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
            Cull [_Cull]
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

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
                clip(CliffCoverageClipValue(IN.uv, IN.color));
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
            Cull [_Cull]

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
                clip(CliffCoverageClipValue(IN.uv, IN.color));
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
