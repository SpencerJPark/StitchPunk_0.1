// Painted/Decal  -  screen-space projected decals for URP (Unity 6.1+)
//
// Put this on a unit cube. The cube's transform is the decal volume: X/Z = footprint,
// Y = how far above/below the ground the decal still lands. The shader reconstructs the
// world position of whatever is behind each pixel from the camera depth texture, converts it
// to the cube's local space, and samples the atlas with the local XZ. Nothing needs a renderer
// feature; it only needs Depth Texture ON in the URP asset (the water needs that anyway).
//
// Modes
//   Multiply : cracks, stains, scorch marks. Darkens what is underneath, keeps its lighting.
//   Normal   : leaf litter, pebbles, puddles, moss. Painted over the ground, lit with the same
//              flat ambient + main light + point light pool as Painted/Ground.
//
// Atlas: a grid of _AtlasGrid.x by _AtlasGrid.y cells, _Tile selects one (row-major from the
// top-left). Set _Tile through a MaterialPropertyBlock per decal (GameObjects) or the
// PaintedDecalTile component (Entities). Entities Graphics compatible.

Shader "Painted/Decal"
{
    Properties
    {
        [NoScaleOffset] _Atlas ("Decal atlas (RGBA)", 2D) = "white" {}
        _AtlasGrid ("Atlas grid (cols, rows)", Vector) = (4, 2, 0, 0)
        _Tile ("Tile index", Float) = 0
        _Tint ("Tint (A = opacity)", Color) = (1, 1, 1, 1)
        [KeywordEnum(Multiply, Normal)] _Mode ("Blend mode (also set Src/Dst below)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 2   // DstColor
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0   // Zero
        _EdgeFade ("Fade at volume edge", Range(0, 0.5)) = 0.1
        _SlopeFade ("Fade on slopes (normal.y below)", Range(0, 1)) = 0.6
        _FlipRandom ("Random flip by position", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"      // after opaques (ground), before water
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Decal"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]     // Multiply: DstColor Zero. Normal: SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _MODE_MULTIPLY _MODE_NORMAL
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _AtlasGrid;
                float4 _Tint;
                float  _Tile;
                half   _EdgeFade;
                half   _SlopeFade;
                half   _FlipRandom;
            CBUFFER_END

            // Entities Graphics: _Tile and _Tint come per entity from
            // PaintedDecalTile / PaintedDecalTint components (see PaintedDecalEntities.cs).
            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float,  _Tile)
                UNITY_DOTS_INSTANCED_PROP(float4, _Tint)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _Tile UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float,  _Tile)
            #define _Tint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _Tint)
            #endif

            TEXTURE2D(_Atlas);
            SAMPLER(sampler_Atlas);

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
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
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // ---- world position of the surface behind this pixel ------------------------
                // raw pixel UV for reconstruction (ComputeClipSpacePosition handles the Y flip itself);
                // GetNormalizedScreenSpaceUV is only for the lighting/shadow inputs further down
                float2 screenUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float  rawDepth = SampleSceneDepth(screenUV);
            #if !UNITY_REVERSED_Z
                rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
            #endif
                float3 posWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);

                // surface normal from depth derivatives (before any clip, derivatives need
                // all four lanes alive), used to drop decals off cliff faces
                float3 nWS = normalize(cross(ddy(posWS), ddx(posWS)));
                nWS *= sign(nWS.y + 1e-4);

                // ---- into the decal cube ------------------------------------------------------
                float3 posOS = TransformWorldToObject(posWS);          // unit cube: -0.5 .. 0.5
                clip(0.5 - abs(posOS));                                 // outside the volume

                float2 uv = posOS.xz + 0.5;
                if (_FlipRandom > 0.5)
                {
                    // deterministic per-object flip so scattered copies of one tile differ
                    float3 origin = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                    float h = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);
                    uv.x = h > 0.5 ? 1.0 - uv.x : uv.x;
                    uv.y = frac(h * 7.0) > 0.5 ? 1.0 - uv.y : uv.y;
                }

                float2 grid = max(_AtlasGrid.xy, 1.0);
                float  tile = floor(_Tile + 0.5);
                float2 cell = float2(fmod(tile, grid.x), grid.y - 1.0 - floor(tile / grid.x));
                float2 atlasUV = (cell + uv) / grid;
                half4 tex = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, atlasUV) * (half4)_Tint;

                // ---- fades ---------------------------------------------------------------------
                half edge = 1.0h;
                if (_EdgeFade > 0.0h)
                {
                    float3 e = (0.5 - abs(posOS)) / _EdgeFade;
                    edge = saturate(min(min(e.x, e.z), e.y * 2.0));
                }

                half slope = smoothstep(_SlopeFade - 0.15h, _SlopeFade, nWS.y);

                half alpha = tex.a * edge * slope;

                float fogFactor = ComputeFogFactor(TransformWorldToHClip(posWS).z);

            #if defined(_MODE_MULTIPLY)
                // Blend is DstColor Zero in this mode: output 1 = no change. Lerp toward white
                // by alpha so the decal fades out cleanly, and by fog so distant decals vanish.
            #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                alpha *= ComputeFogIntensity(fogFactor);
            #endif
                return half4(lerp(half3(1, 1, 1), tex.rgb, alpha), 1.0h);
            #else
                // ---- same flat lighting as Painted/Ground ------------------------------------
                InputData inputData = (InputData)0;
                inputData.positionWS = posWS;
                inputData.normalWS = nWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(posWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = float4(inputData.normalizedScreenSpaceUV, 0.0, 1.0);
            #else
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
            #endif
                Light mainLight = GetMainLight(shadowCoord);
                half3 lighting = SampleSH(half3(0, 1, 0)) + mainLight.color * lerp(1.0h, mainLight.shadowAttenuation, 0.6h);

            #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                #if USE_CLUSTER_LIGHT_LOOP
                UNITY_LOOP for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                {
                    Light dirLight = GetAdditionalLight(dirIndex, posWS, half4(1, 1, 1, 1));
                    lighting += dirLight.color * dirLight.shadowAttenuation;
                }
                #endif
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, posWS, half4(1, 1, 1, 1));
                    half atten = l.distanceAttenuation * l.shadowAttenuation;
                    atten = atten / (1.0h + atten);
                    lighting += l.color * atten;
                LIGHT_LOOP_END
            #endif

                half3 col = tex.rgb * lighting;
                col = MixFog(col, fogFactor);
                return half4(col, alpha);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
