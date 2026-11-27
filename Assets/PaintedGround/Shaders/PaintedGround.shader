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
        [NoScaleOffset] _TerraceCornerLevels ("Terrace corner levels (R8, level+3)", 2D) = "black" {}
        _TerraceCornerOrigin ("Terrace corner origin xz, cell metres, corners per side", Vector) = (0,0,2,0)

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

        [Header(Far view)]
        // 1 on the coarse mesh that stands in for unloaded tiles when zoomed out: same material, so it looks exactly like the real ground.
        [Toggle] _StrategicProxy ("Far-view stand-in for the real tiles", Float) = 0

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

        // Global, set by StrategicRigDriver; deliberately outside the material CBUFFER.
        float _StrategicLayerFade;

        // One CBUFFER shared by every pass keeps the shader SRP Batcher compatible.
        CBUFFER_START(UnityPerMaterial)
            float4 _TileSizes;
            float4 _SplatBounds;
            float4 _TerraceCornerOrigin;
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
            float  _StrategicProxy;
        CBUFFER_END

        // Zoomed out, the far-view stand-in (same material on a coarse mesh) replaces the real tiles completely, so streaming never swaps the look.
        // Same noise on both, so the stand-in keeps exactly the pixels the real ground discards.
        static const float StrategicHandoverSpeed = 1.6;
        static const float StrategicProxyDepthPushFraction = 0.0025;

        // Globals, set by TerrainTileResidencyShaderSystem: one texel per tile, 1 = the real tile is drawn; grid zw = 0 means nothing published.
        TEXTURE2D(_TerrainTileResidency);
        float4 _TerrainTileResidencyGrid;
        static const float TerrainTileSizeMetres = 100.0;

        // A tile that is still loading, or outside the load radius, has no real ground: the stand-in fills it at any zoom instead of a hole.
        float StrategicStandInFillsTile(float2 positionXZ)
        {
            if (_TerrainTileResidencyGrid.z < 0.5)
                return 0.0;
            int2 localTile = (int2)floor(positionXZ / TerrainTileSizeMetres + 0.5) - (int2)_TerrainTileResidencyGrid.xy;
            if (any(localTile < 0) || any(localTile >= (int2)_TerrainTileResidencyGrid.zw))
                return 0.0;
            return 1.0 - LOAD_TEXTURE2D(_TerrainTileResidency, localTile).r;
        }

        void StrategicHandoverClip(float4 positionCS, float2 positionXZ)
        {
            float2 pixel = floor(positionCS.xy);
            float noise = frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            float standInVisibility = saturate(_StrategicLayerFade * StrategicHandoverSpeed);
            if (_StrategicProxy > 0.5)
                standInVisibility = max(standInVisibility, StrategicStandInFillsTile(positionXZ));
            clip(_StrategicProxy > 0.5 ? standInVisibility - noise - 0.0001 : noise - standInVisibility + 0.0001);
        }

        // The coarse stand-in pokes through the 1 m real ground on bumps; sliding each vertex away from the camera along its view ray keeps
        // it where it is on screen but puts it behind the real ground.
        float3 StrategicProxyPushAway(float3 positionWS)
        {
            return _StrategicProxy > 0.5 ? positionWS + (positionWS - _WorldSpaceCameraPos) * StrategicProxyDepthPushFraction : positionWS;
        }

        // The 4 m stand-in mesh sags below the 1 m real ground on bumps and would read the real ground's own shadow. The lookup is
        // lifted toward the sun: along the light ray it lands on the same shadow-map texel, so shadow edges stay exactly where the real
        // ground has them (a lift along the normal slid them metres sideways under a low sun and left dither grain on every edge).
        static const float StrategicProxyShadowLiftMetres = 0.5;

        float3 StrategicProxyShadingPosition(float3 positionWS)
        {
            return _StrategicProxy > 0.5 ? positionWS + _MainLightPosition.xyz * StrategicProxyShadowLiftMetres : positionWS;
        }

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
            // Global, set by TerrainLightingBridge: ink detail and Don't Starve tile edges on every ground material at once.
            #pragma multi_compile_fragment _ STITCH_INK_LOOK
            // Global, enabled by a C# bridge with Shader.EnableKeyword: 16-case edge tiles on the normal ground look.
            #pragma multi_compile_fragment _ _GROUND_EDGE_TILES

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
            TEXTURE2D(_TerraceCornerLevels);
            // Ink look globals (TerrainLightingBridge), deliberately outside the material CBUFFER.
            TEXTURE2D(_StitchInkDetailBaseTex);
            TEXTURE2D(_StitchInkDetailRTex);
            TEXTURE2D(_StitchInkDetailGTex);
            TEXTURE2D(_StitchInkDetailBTex);
            TEXTURE2D(_StitchTransitionAtlas);
            // Globals outside the material CBUFFER; R = grass over dirt, G = forest, B = stone; set by TerrainLightingBridge.
            TEXTURE2D(_GroundEdgeTileAtlasR);
            TEXTURE2D(_GroundEdgeTileAtlasG);
            TEXTURE2D(_GroundEdgeTileAtlasB);
            float  _GroundEdgeTileCellMetres;
            // Terrace cliff rim/foot sets (slices 0-3 rim Dirt/Grass/Forest/Stone, 4-7 foot); globals outside the CBUFFER, set by the terrace bridge.
            TEXTURE2D_ARRAY(_TerraceCliffEdges);
            float4 _TerraceRimRock;
            float4 _TerraceFootRock;
            float  _TerraceCliffEdgesReady;      // 1 when the array is bound
            float4 _StitchInkDetailTileMetres;   // world metres per detail tile: Base, R, G, B
            float4 _StitchInkDetailParams;       // x = line, y = tone, z = scratch strength, w = flatten painted texture
            float  _StitchTransitionCellMetres;
            SAMPLER(sampler_trilinear_repeat);   // trilinear: bilinear mips switch in hard lines that slide down the screen while zooming
            SAMPLER(sampler_linear_clamp);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float4 uv0        : TEXCOORD0;   // z = terrace level of this vertex
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
                float  terraceLevel : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Only the depth is pushed: texturing and lighting at the pushed spot shifted the paint and put the stand-in under the
                // real ground's shadow, so mid-zoom the two layers did not match.
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(StrategicProxyPushAway(OUT.positionWS));
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color      = IN.color;
                OUT.terraceLevel = IN.uv0.z;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            float TerraceCornerLevel(int2 cornerIndex)
            {
                int cornersPerSide = (int)(_TerraceCornerOrigin.w + 0.5);
                int2 clamped = clamp(cornerIndex, int2(0, 0), int2(cornersPerSide - 1, cornersPerSide - 1));
                return round(_TerraceCornerLevels.Load(int3(clamped, 0)).r * 255.0) - 3.0;
            }

            // Ink only, from the same atlas, case bits and integer variant hash as the biome tiles (cell index is global: origin is a multiple of the cell size).
            float TerraceEdgeTileInk(float2 wxz, float levelThreshold, float3 colourPlaceholder)
            {
                float  cellMetres = _TerraceCornerOrigin.z;
                int2   cellIndex = (int2)floor((wxz - _TerraceCornerOrigin.xy) / cellMetres);
                float4 cornerInside = float4(
                    TerraceCornerLevel(cellIndex + int2(0, 1)) >= levelThreshold ? 1.0 : 0.0,
                    TerraceCornerLevel(cellIndex + int2(1, 1)) >= levelThreshold ? 1.0 : 0.0,
                    TerraceCornerLevel(cellIndex + int2(1, 0)) >= levelThreshold ? 1.0 : 0.0,
                    TerraceCornerLevel(cellIndex)              >= levelThreshold ? 1.0 : 0.0);
                float2 globalCellCoordinate = wxz / cellMetres;
                float3 discardedColour; float inkOut; float discardedRim;
                PaintedTransitionTile_float(_GroundEdgeTileAtlasR, sampler_linear_clamp, cornerInside, globalCellCoordinate, 47.0,
                                            colourPlaceholder, colourPlaceholder, 0.0, 0.0, discardedColour, inkOut, discardedRim);
                return inkOut;
            }

            // Rim or foot of one cliff side: each layer's set is tapped by its final paint weight, so the cliff reads the biome per pixel.
            // The anti-aliasing width is taken outside any branch (masks instead of early-outs) so fwidth always sees valid neighbours.
            void TerraceCliffEdgeSide(float caseIndex, float firstSlice, float seedOffset, float4 layerWeights, float2 globalCellCoordinate,
                                      float3 rockColour, inout float3 col, inout float ink, inout float rim)
            {
                const float4 groundSeeds = float4(53.0, 0.0, 17.0, 31.0);
                float sideActive = (caseIndex > 0.5 && caseIndex < 14.5) ? 1.0 : 0.0;
                float coverage = 0.0;
                float weightedInk = 0.0;
                [unroll] for (int layerIndex = 0; layerIndex < 4; layerIndex++)
                {
                    float layerWeight = layerWeights[layerIndex];
                    float tapCase = (layerWeight > 0.02) ? caseIndex : 0.0;
                    float3 tap = CliffEdgeTileSample(_TerraceCliffEdges, sampler_linear_clamp, firstSlice + layerIndex, tapCase,
                                                     globalCellCoordinate, groundSeeds[layerIndex] + seedOffset);
                    float appliedWeight = layerWeight * sideActive;
                    coverage += appliedWeight * tap.r;
                    weightedInk += appliedWeight * tap.b;
                }
                float coverageAntiAlias = max(fwidth(coverage), 1e-3);
                float cut = smoothstep(0.5 - coverageAntiAlias, 0.5 + coverageAntiAlias, coverage);
                float cliffCut = lerp(1.0, cut, sideActive);
                col = lerp(rockColour, col, cliffCut);
                ink = max(ink * cliffCut, weightedInk);
                rim *= cliffCut;
            }

            void TerraceCliffEdge(float2 wxz, float myLevel, float4 layerWeights, inout float3 col, inout float ink, inout float rim, out float edgeOwned)
            {
                float  cellMetres = _TerraceCornerOrigin.z;
                int2   cellIndex = (int2)floor((wxz - _TerraceCornerOrigin.xy) / cellMetres);
                float2 globalCellCoordinate = wxz / cellMetres;
                float4 cornerLevels = float4(
                    TerraceCornerLevel(cellIndex + int2(0, 1)),
                    TerraceCornerLevel(cellIndex + int2(1, 1)),
                    TerraceCornerLevel(cellIndex + int2(1, 0)),
                    TerraceCornerLevel(cellIndex));
                float rimCase = dot(step(myLevel, cornerLevels), float4(1, 2, 4, 8));
                float footCase = dot(step(myLevel + 1.0, cornerLevels), float4(1, 2, 4, 8));
                float footSideCase = 15.0 - footCase;
                bool rimOwned = rimCase > 0.5 && rimCase < 14.5;
                bool footOwned = footSideCase > 0.5 && footSideCase < 14.5;
                edgeOwned = (rimOwned || footOwned) ? 1.0 : 0.0;
                TerraceCliffEdgeSide(rimCase, 0.0, 100.0, layerWeights, globalCellCoordinate, _TerraceRimRock.rgb, col, ink, rim);
                TerraceCliffEdgeSide(footSideCase, 4.0, 200.0, layerWeights, globalCellCoordinate, _TerraceFootRock.rgb, col, ink, rim);
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

            // Lines are inked into the layer colour rather than the shared ink channel, so they are covered exactly as far as
            // the layer is (fringe or tile coverage), and drawn with the same pen as the edges.
            half3 InkedLayer(TEXTURE2D_PARAM(tex, samp), TEXTURE2D_PARAM(detailTex, detailSamp), float2 wxz, float tileSize,
                             float detailTileMetres, half patchMask)
            {
                half3 layerColour = Layer(TEXTURE2D_ARGS(tex, samp), wxz, tileSize, patchMask);
            #if defined(STITCH_INK_LOOK)
                // Flatten: the reference is one flat local colour per surface with all detail in ink. The smallest mip is the
                // painted texture's own average, so the palette is kept without picking colours by hand.
                half3 flatLocalColour = SAMPLE_TEXTURE2D_LOD(tex, samp, float2(0.5, 0.5), 14).rgb;
                layerColour = lerp(layerColour, flatLocalColour, (half)_StitchInkDetailParams.w);
                half3 detail = Layer(TEXTURE2D_ARGS(detailTex, detailSamp), wxz, detailTileMetres, patchMask);
                layerColour *= lerp(1.0h, detail.g * 2.0h, (half)_StitchInkDetailParams.y);
                layerColour *= 1.0h - detail.b * (half)_StitchInkDetailParams.z * 0.25h;
                layerColour = lerp(layerColour, layerColour * (half3)_StitchInkColor.rgb, saturate(detail.r * (half)_StitchInkDetailParams.x));
            #endif
                return layerColour;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                StrategicHandoverClip(IN.positionCS, IN.positionWS.xz);

                float3 posWS = IN.positionWS;
                float2 wxz   = posWS.xz;
                half3  n     = normalize(IN.normalWS);

                // ---- noises -------------------------------------------------------------
                half  macroNoise = SAMPLE_TEXTURE2D(_MacroTex, sampler_trilinear_repeat, wxz / _MacroSize).r;
                half  patchNoise = SAMPLE_TEXTURE2D(_MacroTex, sampler_trilinear_repeat, wxz / _PatchSize + 0.37).g;
                half3 brush = SAMPLE_TEXTURE2D(_BlendNoiseTex, sampler_trilinear_repeat, wxz / _BlendNoiseSize).rgb;
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
                float3 col = InkedLayer(TEXTURE2D_ARGS(_BaseTex, sampler_BaseTex), TEXTURE2D_ARGS(_StitchInkDetailBaseTex, sampler_BaseTex), wxz, _TileSizes.x, _StitchInkDetailTileMetres.x, patchMask);
                float3 layerRColour = InkedLayer(TEXTURE2D_ARGS(_LayerRTex, sampler_BaseTex), TEXTURE2D_ARGS(_StitchInkDetailRTex, sampler_BaseTex), wxz, _TileSizes.y, _StitchInkDetailTileMetres.y, patchMask);
                float3 layerGColour = InkedLayer(TEXTURE2D_ARGS(_LayerGTex, sampler_BaseTex), TEXTURE2D_ARGS(_StitchInkDetailGTex, sampler_BaseTex), wxz, _TileSizes.z, _StitchInkDetailTileMetres.z, patchMask);
                float3 layerBColour = InkedLayer(TEXTURE2D_ARGS(_LayerBTex, sampler_BaseTex), TEXTURE2D_ARGS(_StitchInkDetailBTex, sampler_BaseTex), wxz, _TileSizes.w, _StitchInkDetailTileMetres.w, patchMask);
                float  ink = 0, rim = 0;
                float4 layerWeights = float4(1, 0, 0, 0);   // final paint weight of Dirt, Grass, Forest, Stone (sums to 1)
                float  layerCoverage = 0;
                float  terraceEdgeOwned = 0.0;   // 1 where the terrace cliff-edge pass owns this pixel's cell
            #if defined(_GROUND_EDGE_TILES) && defined(_SPLAT_WORLDTEXTURE)
                // Same corner-tap scheme as the ink look below, on the edge-tile atlas; ink goes through PaintedFinish with the material's _InkColor.
                float  edgeTileCellMetres = max(_GroundEdgeTileCellMetres, 0.1);
                float2 edgeCellCoordinate = wxz / edgeTileCellMetres;
                float2 edgeCellOrigin = floor(edgeCellCoordinate) * edgeTileCellMetres;
                float2 edgeSplatScale = 1.0 / max(_SplatBounds.zw, 0.001);
                float3 edgeCornerTopLeft     = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_linear_clamp, (edgeCellOrigin + float2(0, edgeTileCellMetres) - _SplatBounds.xy) * edgeSplatScale, 0).rgb;
                float3 edgeCornerTopRight    = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_linear_clamp, (edgeCellOrigin + edgeTileCellMetres - _SplatBounds.xy) * edgeSplatScale, 0).rgb;
                float3 edgeCornerBottomRight = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_linear_clamp, (edgeCellOrigin + float2(edgeTileCellMetres, 0) - _SplatBounds.xy) * edgeSplatScale, 0).rgb;
                float3 edgeCornerBottomLeft  = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_linear_clamp, (edgeCellOrigin - _SplatBounds.xy) * edgeSplatScale, 0).rgb;
                PaintedTransitionTileCoverage_float(_GroundEdgeTileAtlasR, sampler_linear_clamp, float4(edgeCornerTopLeft.r, edgeCornerTopRight.r, edgeCornerBottomRight.r, edgeCornerBottomLeft.r),
                                            edgeCellCoordinate, 0.0, layerRColour, col, ink, rim, col, ink, rim, layerCoverage);
                layerWeights *= 1.0 - layerCoverage; layerWeights.y = layerCoverage;
                PaintedTransitionTileCoverage_float(_GroundEdgeTileAtlasG, sampler_linear_clamp, float4(edgeCornerTopLeft.g, edgeCornerTopRight.g, edgeCornerBottomRight.g, edgeCornerBottomLeft.g),
                                            edgeCellCoordinate, 17.0, layerGColour, col, ink, rim, col, ink, rim, layerCoverage);
                layerWeights *= 1.0 - layerCoverage; layerWeights.z = layerCoverage;
                PaintedTransitionTileCoverage_float(_GroundEdgeTileAtlasB, sampler_linear_clamp, float4(edgeCornerTopLeft.b, edgeCornerTopRight.b, edgeCornerBottomRight.b, edgeCornerBottomLeft.b),
                                            edgeCellCoordinate, 31.0, layerBColour, col, ink, rim, col, ink, rim, layerCoverage);
                layerWeights *= 1.0 - layerCoverage; layerWeights.w = layerCoverage;
                if (_TerraceCornerOrigin.w > 0.5)
                {
                    float terraceMyLevel = round(IN.terraceLevel);
                    // Rim: the top surface of this level; foot: the lower ground, which receives the plateau's outward flicks.
                    if (_TerraceCliffEdgesReady > 0.5)
                    {
                        TerraceCliffEdge(wxz, terraceMyLevel, layerWeights, col, ink, rim, terraceEdgeOwned);
                    }
                    else
                    {
                        ink = max(ink, TerraceEdgeTileInk(wxz, terraceMyLevel, col));
                        ink = max(ink, TerraceEdgeTileInk(wxz, terraceMyLevel + 1.0, col));
                    }
                }
            #elif defined(STITCH_INK_LOOK) && defined(_SPLAT_WORLDTEXTURE)
                // Four corner taps shared by all three channels; a corner's world position gives the same value in every cell that owns it.
                float  transitionCellMetres = max(_StitchTransitionCellMetres, 0.1);
                float2 cellCoordinate = wxz / transitionCellMetres;
                float2 cellOrigin = floor(cellCoordinate) * transitionCellMetres;
                float2 splatScale = 1.0 / max(_SplatBounds.zw, 0.001);
                float3 cornerTopLeft     = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_linear_clamp, (cellOrigin + float2(0, transitionCellMetres) - _SplatBounds.xy) * splatScale, 0).rgb;
                float3 cornerTopRight    = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_linear_clamp, (cellOrigin + transitionCellMetres - _SplatBounds.xy) * splatScale, 0).rgb;
                float3 cornerBottomRight = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_linear_clamp, (cellOrigin + float2(transitionCellMetres, 0) - _SplatBounds.xy) * splatScale, 0).rgb;
                float3 cornerBottomLeft  = SAMPLE_TEXTURE2D_LOD(_SplatMap, sampler_linear_clamp, (cellOrigin - _SplatBounds.xy) * splatScale, 0).rgb;
                PaintedTransitionTile_float(_StitchTransitionAtlas, sampler_linear_clamp, float4(cornerTopLeft.r, cornerTopRight.r, cornerBottomRight.r, cornerBottomLeft.r),
                                            cellCoordinate, 0.0, layerRColour, col, ink, rim, col, ink, rim);
                PaintedTransitionTile_float(_StitchTransitionAtlas, sampler_linear_clamp, float4(cornerTopLeft.g, cornerTopRight.g, cornerBottomRight.g, cornerBottomLeft.g),
                                            cellCoordinate, 17.0, layerGColour, col, ink, rim, col, ink, rim);
                PaintedTransitionTile_float(_StitchTransitionAtlas, sampler_linear_clamp, float4(cornerTopLeft.b, cornerTopRight.b, cornerBottomRight.b, cornerBottomLeft.b),
                                            cellCoordinate, 31.0, layerBColour, col, ink, rim, col, ink, rim);
            #else
                PaintedFringe_float(_FringeTex, sampler_BaseTex, w.r, gR, wxz, 0, FRINGE_ROWS, fringeWid, fringeLen, brush.r, _BlendNoiseAmount,
                                    layerRColour, col, ink, rim, col, ink, rim);
                PaintedFringe_float(_FringeTex, sampler_BaseTex, w.g, gG, wxz, 1, FRINGE_ROWS, fringeWid, fringeLen, brush.g, _BlendNoiseAmount,
                                    layerGColour, col, ink, rim, col, ink, rim);
                PaintedFringe_float(_FringeTex, sampler_BaseTex, w.b, gB, wxz, 2, FRINGE_ROWS, fringeWid, fringeLen, brush.b, _BlendNoiseAmount,
                                    layerBColour, col, ink, rim, col, ink, rim);
            #endif

            #if defined(_CLIFFS)
                half2 cw = pow(abs(n.xz), 4.0h);
                cw /= max(cw.x + cw.y, 0.0001h);
                float3 cliffCol =
                    SAMPLE_TEXTURE2D(_CliffTex, sampler_BaseTex, posWS.zy / _CliffTileSize).rgb * cw.x +
                    SAMPLE_TEXTURE2D(_CliffTex, sampler_BaseTex, posWS.xy / _CliffTileSize).rgb * cw.y;
                // The cliff-edge pass owns its cells' bevels; the slope texture is only for un-tiled steep ground.
                float steep = (1.0 - smoothstep(_CliffStart, _CliffEnd, n.y)) * (1.0 - terraceEdgeOwned);
                float2 gC; DerivativeGradient_float(steep, wxz, gC);
                PaintedFringe_float(_FringeTex, sampler_BaseTex, steep, gC, wxz, 3, FRINGE_ROWS, fringeWid, fringeLen, brush.r, _BlendNoiseAmount,
                                    cliffCol, col, ink, rim, col, ink, rim);
            #endif

                // ---- macro variation, rim, ink --------------------------------------------
                // Ink stays at full strength zoomed out too: fading it made the painted marks vanish when zooming.
            #if defined(STITCH_INK_LOOK)
                // One pen for edges and detail; the macro tint mostly goes, the reference is flat local colour.
                half4 inkColorForView = half4((half3)_StitchInkColor.rgb, 1.0h);
                half  macroStrengthForView = _MacroStrength * (1.0h - 0.75h * (half)_StitchInkDetailParams.w);
            #else
                half4 inkColorForView = _InkColor;
                half  macroStrengthForView = _MacroStrength;
            #endif
                PaintedFinish_float(col, macroNoise, _MacroDark.rgb, _MacroLight.rgb, macroStrengthForView, rim, _RimColor, ink, inkColorForView, col);

                // ---- lighting -------------------------------------------------------------
                float3 lighting;
                PaintedLighting_float(StrategicProxyShadingPosition(posWS), n, GetNormalizedScreenSpaceUV(IN.positionCS), _AmbientTint.rgb,
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
                // The stand-in casts the hill shadows only once zooming starts (real tiles cover ground level); collapsed vertices draw nothing.
                if (_StrategicProxy > 0.5 && _StrategicLayerFade <= 0.0001)
                    positionCS = float4(0.0, 0.0, -2.0, 1.0);
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
                float2 positionXZ : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(StrategicProxyPushAway(positionWS));
                OUT.positionXZ = positionWS.xz;
                return OUT;
            }

            half frag(Varyings IN) : SV_Target
            {
                StrategicHandoverClip(IN.positionCS, IN.positionXZ);
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
                float2 positionXZ : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(StrategicProxyPushAway(positionWS));
                OUT.positionXZ = positionWS.xz;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                StrategicHandoverClip(IN.positionCS, IN.positionXZ);
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
