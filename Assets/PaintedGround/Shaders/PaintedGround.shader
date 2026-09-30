// Painted/Ground  -  hand-painted, non-repeating terrain for URP (Unity 6.1+, written against 6.3-6.5 docs)
// Entities Graphics compatible (DOTS_INSTANCING_ON variant, SRP Batcher layout).
//
// What it does
//   1. World-space UVs      : textures are projected top-down from world XZ, so they flow across
//                             every terrain chunk with no seams and no per-mesh UV work.
//   2. Second sample        : each layer is sampled again, rotated + rescaled, and swapped in by a
//                             patchy noise mask. Breaks the tile grid without washing out contrast.
//   3. Macro variation      : a very large, soft noise tints value/hue across tens of metres.
//   4. Painted edge fringes : each turf border is drawn from a hand-paintable strip texture
//                             sampled across and along the edge, so grass borders are made of
//                             blades, forest borders of leafy scallops, stone borders of chips,
//                             each with its own highlight rim and ink line. This is what makes
//                             the "grass" in the reference: it is the edge, not an object.
//   5. Cliffs               : steep faces get a side-projected texture (vertical streaks).
//   6. Near-unlit lighting  : flat ambient + main light shadows + point lights with no N.L,
//                             so a campfire makes a soft pool of light on painted ground.
//
// Setup
//   - Splat weights come from vertex colour RGB (Base shows where RGB = 0), or switch
//     "Splat source" to World Texture and paint one PNG for the whole map.
//   - Assign PaintedGround_Macro.png, PaintedGround_Brush.png and T_EdgeFringe.png (sRGB OFF,
//     Wrap = Repeat, no compression or high quality).
//   - Layer textures: Wrap = Repeat, Aniso 4-8 on the Base texture (its sampler is shared).
//   - Unity 6.0 only: rename _CLUSTER_LIGHT_LOOP -> _FORWARD_PLUS and
//     USE_CLUSTER_LIGHT_LOOP -> USE_FORWARD_PLUS.

Shader "Painted/Ground"
{
    Properties
    {
        [Header(Layers)]
        [NoScaleOffset] _BaseTex   ("Base layer (dirt)",   2D) = "gray" {}
        [NoScaleOffset] _LayerRTex ("R layer (grass)",     2D) = "gray" {}
        [NoScaleOffset] _LayerGTex ("G layer (forest)",    2D) = "gray" {}
        [NoScaleOffset] _LayerBTex ("B layer (stone)",     2D) = "gray" {}
        _TileSizes ("Tile size in metres (Base, R, G, B)", Vector) = (12, 10, 14, 12)

        [Header(Splat source)]
        [KeywordEnum(VertexColor, WorldTexture)] _Splat ("Splat source", Float) = 0
        [NoScaleOffset] _SplatMap ("World splat map (RGB)", 2D) = "black" {}
        _SplatBounds ("World rect: minX, minZ, sizeX, sizeZ", Vector) = (-128, -128, 256, 256)
        _SplatGradStep ("Edge direction sample step (m)", Range(0.05, 2)) = 0.35

        [Header(Anti tiling)]
        [Toggle(_SECOND_SAMPLE)] _UseSecond ("Second rotated sample", Float) = 1
        _SecondScale ("Second sample scale", Range(0.2, 1.5)) = 0.61
        _SecondBlend ("Second sample amount", Range(0, 1)) = 1
        _PatchSize   ("Patch size (m)", Float) = 35

        [Header(Macro variation)]
        [NoScaleOffset] _MacroTex ("Macro noise (tileable)", 2D) = "gray" {}
        _MacroSize     ("Macro size (m)", Float) = 140
        _MacroStrength ("Macro strength", Range(0, 1)) = 0.6
        _MacroDark  ("Macro dark tint (0.5 = neutral)",  Color) = (0.36, 0.39, 0.45, 1)
        _MacroLight ("Macro light tint (0.5 = neutral)", Color) = (0.58, 0.55, 0.48, 1)

        [Header(Painted edges)]
        // The fringe strip is the painted border of each turf: R = coverage silhouette
        // (blades, scallops, chips), G = highlight rim, B = ink line. One row per layer,
        // top row = R layer, then G, B, cliff. Along the strip = along the border.
        [NoScaleOffset] _FringeTex ("Edge fringe strip (4 rows)", 2D) = "white" {}
        // Scales the strip like a tiling texture: one repeat of the strip along the border is
        // _FringeSize metres, and it reaches _FringeSize / 4 metres across (the rows are 4:1),
        // so the painted proportions are kept. Stretch changes only the across reach.
        _FringeSize    ("Fringe size (m along the edge)", Range(0.5, 14)) = 3.0
        _FringeStretch ("Fringe stretch (across)", Range(0.5, 2)) = 1.0
        [NoScaleOffset] _BlendNoiseTex ("Brush noise (tileable)", 2D) = "gray" {}
        _BlendNoiseSize   ("Brush noise size (m)", Float) = 7
        _BlendNoiseAmount ("Edge wobble", Range(0, 1)) = 0.25
        _RimColor ("Highlight rim (A = strength)", Color) = (0.95, 0.92, 0.80, 0.55)
        _InkColor ("Ink line (A = strength)", Color) = (0.12, 0.10, 0.12, 0.75)

        [Header(Cliffs)]
        [Toggle(_CLIFFS)] _UseCliffs ("Cliff texture on steep faces", Float) = 1
        [NoScaleOffset] _CliffTex ("Cliff texture", 2D) = "gray" {}
        _CliffTileSize ("Cliff tile size (m)", Float) = 6
        _CliffStart ("Cliff fully on below normal.y", Range(0, 1)) = 0.45
        _CliffEnd   ("Cliff fully off above normal.y", Range(0, 1)) = 0.70

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
            float4 _TileSizes;
            float4 _SplatBounds;
            half4  _MacroDark;
            half4  _MacroLight;
            half4  _InkColor;
            half4  _RimColor;
            float4 _AmbientTint;
            float  _FringeSize;
            float  _FringeStretch;
            float  _SplatGradStep;
            float  _SecondScale;
            float  _PatchSize;
            float  _MacroSize;
            float  _BlendNoiseSize;
            float  _CliffTileSize;
            half   _SecondBlend;
            half   _MacroStrength;
            half   _BlendNoiseAmount;
            half   _CliffStart;
            half   _CliffEnd;
            half   _ShadowStrength;
            half   _NdotLInfluence;
            half   _PointLightBoost;
        CBUFFER_END

        // Entities Graphics / BatchRendererGroup: the shader must declare at least one DOTS
        // instanced property. The ground has no per-instance overrides, so this just
        // mirrors one material value and keeps the DOTS_INSTANCING_ON variant valid.
        #ifdef UNITY_DOTS_INSTANCING_ENABLED
        UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
            UNITY_DOTS_INSTANCED_PROP(float4, _AmbientTint)
        UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
        #define _AmbientTint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _AmbientTint)
        #endif
        ENDHLSL

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _SPLAT_VERTEXCOLOR _SPLAT_WORLDTEXTURE
            #pragma shader_feature_local_fragment _SECOND_SAMPLE
            #pragma shader_feature_local_fragment _CLIFFS

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

            TEXTURE2D(_BaseTex);        SAMPLER(sampler_BaseTex);   // shared by all colour layers
            TEXTURE2D(_LayerRTex);
            TEXTURE2D(_LayerGTex);
            TEXTURE2D(_LayerBTex);
            TEXTURE2D(_CliffTex);
            TEXTURE2D(_MacroTex);
            TEXTURE2D(_BlendNoiseTex);
            TEXTURE2D(_FringeTex);
            TEXTURE2D(_SplatMap);
            SAMPLER(sampler_linear_repeat);
            SAMPLER(sampler_linear_clamp);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
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
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            #define FRINGE_ROWS 4.0
            #define FRINGE_ASPECT 4.0      // strip width / row height (1024 / 256)

            half3 Layer(TEXTURE2D_PARAM(tex, samp), float2 wxz, float tileSize, half patchMask)
            {
                float3 c;
            #if defined(_SECOND_SAMPLE)
                PaintedLayer_float(tex, samp, wxz, tileSize, _SecondScale, patchMask, c);
            #else
                c = SAMPLE_TEXTURE2D(tex, samp, wxz / tileSize).rgb;
            #endif
                return (half3)c;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 posWS = IN.positionWS;
                float2 wxz   = posWS.xz;
                half3  n     = normalize(IN.normalWS);

                // ---- noises -------------------------------------------------------------
                half  macroNoise = SAMPLE_TEXTURE2D(_MacroTex, sampler_linear_repeat, wxz / _MacroSize).r;
                half  patchNoise = SAMPLE_TEXTURE2D(_MacroTex, sampler_linear_repeat, wxz / _PatchSize + 0.37).g;
                half3 brush = SAMPLE_TEXTURE2D(_BlendNoiseTex, sampler_linear_repeat, wxz / _BlendNoiseSize).rgb;
                half  patchMask = smoothstep(0.4h, 0.6h, patchNoise) * _SecondBlend;

                // ---- splat weights and border directions --------------------------------
                float3 w; float2 gR, gG, gB;
            #if defined(_SPLAT_WORLDTEXTURE)
                // smooth: from the baked world splat map (Tools > Painted Ground > Bake Splat Map)
                SplatGradient_float(_SplatMap, sampler_linear_clamp, wxz, _SplatBounds, _SplatGradStep, w, gR, gG, gB);
            #else
                // per-triangle fallback from vertex colours: works, but borders show seams
                // along mesh triangles. Bake a splat map for production.
                w = IN.color.rgb;
                DerivativeGradient_float(w.r, wxz, gR);
                DerivativeGradient_float(w.g, wxz, gG);
                DerivativeGradient_float(w.b, wxz, gB);
            #endif

                // ---- layers, painted back to front with their fringes --------------------
                float fringeLen = _FringeSize;
                float fringeWid = _FringeSize / FRINGE_ASPECT * _FringeStretch;
                float3 col = Layer(TEXTURE2D_ARGS(_BaseTex, sampler_BaseTex), wxz, _TileSizes.x, patchMask);
                float  ink = 0, rim = 0;
                PaintedFringe_float(_FringeTex, sampler_linear_repeat, w.r, gR, wxz, 0, FRINGE_ROWS, fringeWid, fringeLen, brush.r, _BlendNoiseAmount,
                                    Layer(TEXTURE2D_ARGS(_LayerRTex, sampler_BaseTex), wxz, _TileSizes.y, patchMask), col, ink, rim, col, ink, rim);
                PaintedFringe_float(_FringeTex, sampler_linear_repeat, w.g, gG, wxz, 1, FRINGE_ROWS, fringeWid, fringeLen, brush.g, _BlendNoiseAmount,
                                    Layer(TEXTURE2D_ARGS(_LayerGTex, sampler_BaseTex), wxz, _TileSizes.z, patchMask), col, ink, rim, col, ink, rim);
                PaintedFringe_float(_FringeTex, sampler_linear_repeat, w.b, gB, wxz, 2, FRINGE_ROWS, fringeWid, fringeLen, brush.b, _BlendNoiseAmount,
                                    Layer(TEXTURE2D_ARGS(_LayerBTex, sampler_BaseTex), wxz, _TileSizes.w, patchMask), col, ink, rim, col, ink, rim);

            #if defined(_CLIFFS)
                half2 cw = pow(abs(n.xz), 4.0h);
                cw /= max(cw.x + cw.y, 0.0001h);
                float3 cliffCol =
                    SAMPLE_TEXTURE2D(_CliffTex, sampler_BaseTex, posWS.zy / _CliffTileSize).rgb * cw.x +
                    SAMPLE_TEXTURE2D(_CliffTex, sampler_BaseTex, posWS.xy / _CliffTileSize).rgb * cw.y;
                float steep = 1.0 - smoothstep(_CliffStart, _CliffEnd, n.y);
                float2 gC; DerivativeGradient_float(steep, wxz, gC);
                PaintedFringe_float(_FringeTex, sampler_linear_repeat, steep, gC, wxz, 3, FRINGE_ROWS, fringeWid, fringeLen, brush.r, _BlendNoiseAmount,
                                    cliffCol, col, ink, rim, col, ink, rim);
            #endif

                // ---- macro variation, rim, ink --------------------------------------------
                PaintedFinish_float(col, macroNoise, _MacroDark.rgb, _MacroLight.rgb, _MacroStrength, rim, _RimColor, ink, _InkColor, col);

                // ---- lighting -------------------------------------------------------------
                float3 lighting;
                PaintedLighting_float(posWS, n, GetNormalizedScreenSpaceUV(IN.positionCS), _AmbientTint.rgb,
                                      _ShadowStrength, _NdotLInfluence, _PointLightBoost, lighting);

                col *= lighting;
                col = MixFog(col, IN.fogFactor);
                return half4((half3)col, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);

            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                OUT.positionCS = positionCS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------------------------
        // Needed so the ground shows up in the camera depth texture (water shoreline fade, soft particles).
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half frag(Varyings IN) : SV_Target
            {
                return IN.positionCS.z;
            }
            ENDHLSL
        }
        // ------------------------------------------------------------------------------------
        // Used when SSAO (or anything else) requests the depth-normals prepass.
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
