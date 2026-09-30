// Painted/Water  -  flat, painted-looking water for URP (Unity 6.1+)
//
// What it does
//   - Reads the camera depth texture to find where the ground meets the water plane and
//     draws (a) a thin bright shoreline stroke, (b) a ragged foam band behind it, (c) a
//     shallow -> deep colour ramp. This is what gives the white "wet edge" along beaches.
//   - Painted ripple: two scrolling samples of a brushy noise, thresholded into light
//     streaks, instead of normal-map refraction. Reads as ink and gouache, not shader water.
//   - Optional distortion of the shoreline by the same noise so the edge wobbles.
//   - Fog + main light colour only. No specular, no N.L.
//
// Setup
//   - URP asset (or the camera): Depth Texture = ON. Without it the whole surface is "deep".
//   - Material queue is Transparent so it renders after the ground's DepthOnly pass.
//   - Assign PaintedGround_Brush.png as Ripple noise (sRGB off, Repeat).
//   - Put it on a flat quad/plane at the water height, scaled to cover the map. World-space
//     UVs are used so the plane's scale and UVs do not matter.

Shader "Painted/Water"
{
    Properties
    {
        [Header(Colour)]
        _ShallowColor ("Shallow colour", Color) = (0.42, 0.55, 0.58, 0.55)
        _DeepColor    ("Deep colour",    Color) = (0.10, 0.17, 0.24, 0.92)
        _DepthRamp    ("Shallow to deep distance (m)", Range(0.05, 10)) = 2.5

        [Header(Shoreline)]
        _ShoreColor ("Shoreline stroke colour", Color) = (0.92, 0.95, 0.93, 1)
        _ShoreWidth ("Stroke width (m)", Range(0.0, 1.0)) = 0.10
        _FoamColor  ("Foam colour", Color) = (0.80, 0.86, 0.84, 1)
        _FoamWidth  ("Foam band width (m)", Range(0.0, 3.0)) = 0.7
        _FoamRaggedness ("Foam raggedness", Range(0, 1)) = 0.6
        _EdgeWobble ("Shoreline wobble (m)", Range(0, 0.5)) = 0.12

        [Header(Ripple)]
        [NoScaleOffset] _RippleTex ("Ripple noise (tileable)", 2D) = "gray" {}
        _RippleSize   ("Ripple size (m)", Float) = 9
        _RippleSpeed  ("Ripple speed", Range(0, 1)) = 0.06
        _RippleCut    ("Ripple threshold", Range(0.3, 0.9)) = 0.62
        _RippleColor  ("Ripple colour (A = strength)", Color) = (0.75, 0.82, 0.85, 0.35)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                half4 _DeepColor;
                half4 _ShoreColor;
                half4 _FoamColor;
                half4 _RippleColor;
                float _DepthRamp;
                float _ShoreWidth;
                float _FoamWidth;
                half  _FoamRaggedness;
                float _EdgeWobble;
                float _RippleSize;
                float _RippleSpeed;
                half  _RippleCut;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                UNITY_DOTS_INSTANCED_PROP(float4, _ShallowColor)
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            #define _ShallowColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _ShallowColor)
            #endif

            TEXTURE2D(_RippleTex);
            SAMPLER(sampler_RippleTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half   fogFactor  : TEXCOORD1;
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
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            // Linear eye depth for both perspective and orthographic cameras
            // (top-down games are often orthographic, where LinearEyeDepth alone is wrong).
            float DepthToEye(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                float ortho01 = 1.0 - rawDepth;
            #else
                float ortho01 = rawDepth;
            #endif
                float orthoEye = lerp(_ProjectionParams.y, _ProjectionParams.z, ortho01);
                float perspEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                return lerp(perspEye, orthoEye, unity_OrthoParams.w);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 wxz = IN.positionWS.xz;
                float  t   = _Time.y * _RippleSpeed;

                // ---- painted ripple: two drifting samples, thresholded --------------------
                half n1 = SAMPLE_TEXTURE2D(_RippleTex, sampler_RippleTex, wxz / _RippleSize + float2(t, t * 0.6)).r;
                half n2 = SAMPLE_TEXTURE2D(_RippleTex, sampler_RippleTex, wxz / (_RippleSize * 1.7) - float2(t * 0.7, t * 0.3) + 0.5).g;
                half ripple = smoothstep(_RippleCut - 0.04h, _RippleCut + 0.04h, (n1 + n2) * 0.5h);
                half wobble = (n1 - 0.5h) * 2.0h;

                // ---- depth of what is under this pixel ------------------------------------
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float  rawDepth = SampleSceneDepth(screenUV);
                float  sceneEye = DepthToEye(rawDepth);
                float  waterEye = DepthToEye(IN.positionCS.z);
                float  eyeDiff  = max(sceneEye - waterEye, 0.0);

                // Convert the view-depth difference to a vertical (world) depth so the shoreline
                // width stays constant however the camera is tilted.
                float3 viewDir = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                float  vertical = eyeDiff * max(abs(viewDir.y), 0.05);

                float d = vertical + wobble * _EdgeWobble;

                // ---- bands ----------------------------------------------------------------
                half shore = 1.0h - smoothstep(0.0, max(_ShoreWidth, 0.001), d);
                half foamEdge = _FoamWidth * lerp(1.0h, n2 * 1.6h, _FoamRaggedness);
                half foam  = (1.0h - smoothstep(_ShoreWidth, _ShoreWidth + max(foamEdge, 0.01), d)) * (1.0h - shore);
                half deep  = smoothstep(0.0, _DepthRamp, vertical);

                half4 col = lerp((half4)_ShallowColor, _DeepColor, deep);
                col.rgb = lerp(col.rgb, col.rgb + _RippleColor.rgb * _RippleColor.a, ripple * (1.0h - foam));
                col.rgb = lerp(col.rgb, _FoamColor.rgb, foam * 0.85h);
                col.a   = lerp(col.a, 1.0h, foam * 0.6h);
                col.rgb = lerp(col.rgb, _ShoreColor.rgb, shore);
                col.a   = lerp(col.a, 1.0h, shore);

                // main light colour + ambient only, so it sits with the ground's flat lighting
                Light mainLight = GetMainLight();
                half3 lighting = SampleSH(half3(0, 1, 0)) + mainLight.color;
                col.rgb *= lighting;

                col.rgb = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
