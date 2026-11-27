// Painted/CliffEdgeRibbon  -  one ribbon along a terrace lip or foot that carries the hand-drawn straight-edge art (NC-D15).
// Vertex contract: uv0.x = arc metres / 2, uv0.y = 0.225 (wall side) .. 1.0 (far side), uv1.x = chain startArcPhase,
// colour.r = set index / 2 (0 grass-over-dirt, 0.5 forest-over-grass, 1 stone-over-dirt), colour.g = next set / 2,
// colour.b = blend toward next set, colour.a = fade (dithered; 0 draws nothing).
// Per 2 m segment one of 16 pieces (4 straight cases x 2 variants x mirror) is picked by hash; where the atlas coverage
// is above 0.5 the ground shows through (clip), elsewhere rock. _RibbonSide: 0 lip (_TerraceRimRock), 1 foot (_TerraceFootRock).

Shader "Painted/CliffEdgeRibbon"
{
    Properties
    {
        [Enum(Lip, 0, Foot, 1)] _RibbonSide ("Ribbon side", Float) = 0

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
            float  _RibbonSide;
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

        // Globals pushed by TerrainLightingBridge / the terrace bridge, deliberately outside the material CBUFFER.
        TEXTURE2D(_GroundEdgeTileAtlasR);
        TEXTURE2D(_GroundEdgeTileAtlasG);
        TEXTURE2D(_GroundEdgeTileAtlasB);
        float4 _TerraceRimRock;
        float4 _TerraceFootRock;
        SAMPLER(sampler_linear_clamp);

        static const float RibbonBayer[16] =
        {
             0.0,  8.0,  2.0, 10.0,
            12.0,  4.0, 14.0,  6.0,
             3.0, 11.0,  1.0,  9.0,
            15.0,  7.0, 13.0,  5.0
        };

        // Rotates / mirrors an in-cell vector from the common orientation (covered side +v) into the source piece.
        // rotation 0 = case 3, 1 = case 6 (90 CCW), 2 = case 12 (180), 3 = case 9 (270 CCW).
        float2 RibbonOrientVector(float2 vectorInCommonOrientation, uint rotation, bool mirror)
        {
            float2 oriented = vectorInCommonOrientation;
            oriented.x = mirror ? -oriented.x : oriented.x;
            if (rotation == 1u)
                return float2(oriented.y, -oriented.x);
            if (rotation == 2u)
                return -oriented;
            if (rotation == 3u)
                return float2(-oriented.y, oriented.x);
            return oriented;
        }

        float4 RibbonSampleAtlas(uint setIndex, float2 atlasUV, float2 atlasDdx, float2 atlasDdy)
        {
            if (setIndex == 1u)
                return SAMPLE_TEXTURE2D_GRAD(_GroundEdgeTileAtlasG, sampler_linear_clamp, atlasUV, atlasDdx, atlasDdy);
            if (setIndex == 2u)
                return SAMPLE_TEXTURE2D_GRAD(_GroundEdgeTileAtlasB, sampler_linear_clamp, atlasUV, atlasDdx, atlasDdy);
            return SAMPLE_TEXTURE2D_GRAD(_GroundEdgeTileAtlasR, sampler_linear_clamp, atlasUV, atlasDdx, atlasDdy);
        }

        // Returns (coverage, rim light, ink).
        float3 RibbonTile(float4 uv, float4 vertexColor)
        {
            int segmentIndex = (int)floor(uv.x);
            uint hash = (uint)segmentIndex * 747796405u + asuint(uv.z) * 2891336453u + 2891336453u;
            hash = ((hash >> ((hash >> 28u) + 4u)) ^ hash) * 277803737u;
            hash = hash * 747796405u + 2891336453u;
            hash = ((hash >> ((hash >> 28u) + 4u)) ^ hash) * 277803737u;
            hash ^= hash >> 22u;
            uint pieceIndex = hash % 16u;
            uint rotation = pieceIndex & 3u;
            uint variant = (pieceIndex >> 2u) & 1u;
            bool mirror = ((pieceIndex >> 3u) & 1u) == 1u;
            // rotation -> case: 0 = 3, 1 = 6, 2 = 12, 3 = 9
            float caseIndex = rotation == 0u ? 3.0 : (rotation == 1u ? 6.0 : (rotation == 2u ? 12.0 : 9.0));

            float2 cellCoordinate = float2(uv.x, uv.y);
            float2 cellLocal = float2(frac(uv.x), saturate(uv.y));
            float2 sourceLocal = RibbonOrientVector(cellLocal - 0.5, rotation, mirror) + 0.5;

            // Gradients from the continuous coordinate (the frac jump would pick the smallest mip); the same linear map as the position.
            float2 sourceDdx = RibbonOrientVector(ddx(cellCoordinate), rotation, mirror) / float2(8.0, 4.0);
            float2 sourceDdy = RibbonOrientVector(ddy(cellCoordinate), rotation, mirror) / float2(8.0, 4.0);
            // Half a texel of the sampled mip (128 px cells at mip 0) inside the cell, so filtering never reads the neighbour.
            float texelsPerPixel = max(length(ddx(cellCoordinate)), length(ddy(cellCoordinate))) * 128.0;
            float insetInCell = min(0.5 * max(texelsPerPixel, 1.0) / 128.0, 0.25);
            sourceLocal = clamp(sourceLocal, insetInCell, 1.0 - insetInCell);

            float atlasColumn = fmod(caseIndex, 8.0);
            float atlasRow = floor(caseIndex / 8.0) + 2.0 * (float)variant;
            float2 atlasUV = float2((atlasColumn + sourceLocal.x) / 8.0, 1.0 - (atlasRow + 1.0 - sourceLocal.y) / 4.0);

            uint setIndex = (uint)round(vertexColor.r * 2.0);
            uint nextSetIndex = (uint)round(vertexColor.g * 2.0);
            float3 tile = RibbonSampleAtlas(setIndex, atlasUV, sourceDdx, sourceDdy).rgb;
            if (nextSetIndex != setIndex && vertexColor.b > 0.001)
                tile = lerp(tile, RibbonSampleAtlas(nextSetIndex, atlasUV, sourceDdx, sourceDdy).rgb, vertexColor.b);
            return tile;
        }

        // Shared by every pass so depth, normals and colour discard the same pixels. Returns the tile for colour use.
        float3 RibbonClip(float4 uv, float4 vertexColor, float4 positionCS)
        {
            float3 tile = RibbonTile(uv, vertexColor);
            clip(0.5 - tile.r);
            uint2 pixel = (uint2)positionCS.xy & 3u;
            float dither = (RibbonBayer[pixel.y * 4u + pixel.x] + 0.5) / 16.0;
            clip(vertexColor.a - dither);
            return tile;
        }
        ENDHLSL

        // ------------------------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One Zero
            ZWrite On
            Cull Off
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
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
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
                OUT.uv         = float4(IN.uv, IN.uv1);
                OUT.color      = IN.color;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 tile = RibbonClip(IN.uv, IN.color, IN.positionCS);
                float3 normalWS = normalize(IN.normalWS);

                float3 rockColour = _RibbonSide < 0.5 ? _TerraceRimRock.rgb : _TerraceFootRock.rgb;
                float3 baseColour = lerp(rockColour, float3(1, 1, 1), saturate(tile.g * 0.35));

                float3 lighting;
                PaintedLighting_float(IN.positionWS, normalWS, GetNormalizedScreenSpaceUV(IN.positionCS), _AmbientTint.rgb,
                                      _ShadowStrength, _NdotLInfluence, _PointLightBoost, lighting);

                float3 col = baseColour * lighting;
                col = lerp(col, _StitchInkColor.rgb, saturate(tile.b));
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
            Cull Off
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
                float2 uv1        : TEXCOORD1;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv         : TEXCOORD0;
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
                OUT.uv    = float4(IN.uv, IN.uv1);
                OUT.color = IN.color;
                return OUT;
            }

            half frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                RibbonClip(IN.uv, IN.color, IN.positionCS);
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
                float2 uv1        : TEXCOORD1;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3  normalWS   : TEXCOORD0;
                float4 uv         : TEXCOORD1;
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
                OUT.uv    = float4(IN.uv, IN.uv1);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                RibbonClip(IN.uv, IN.color, IN.positionCS);
                return half4(normalize(IN.normalWS), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
