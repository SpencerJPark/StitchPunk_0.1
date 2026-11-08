// Zoomed-out miniature look: a screen-Y band stays sharp, everything above and below blurs, plus a warm, saturated lift.
// A FullScreenPassRendererFeature runs it only while StrategicRigDriver has switched the feature on; the driver writes the globals.
Shader "Painted/StrategicTiltShift"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "StrategicTiltShift"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _TiltShiftStrength;
            float _TiltShiftCenterY;
            float _TiltShiftBandHalfHeight;
            float _TiltShiftFalloff;
            float _TiltShiftMaxPixels;
            #define TILT_SHIFT_TAPS 28

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 centreColour = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                half3 colour = centreColour;

                float distanceFromBand = abs(uv.y - _TiltShiftCenterY) - _TiltShiftBandHalfHeight;
                float blurMask = smoothstep(0.0, 1.0, saturate(distanceFromBand / max(_TiltShiftFalloff, 0.001)));
                float radiusPixels = blurMask * _TiltShiftMaxPixels * _TiltShiftStrength * (_ScreenParams.y / 1080.0);
                if (radiusPixels > 0.5)
                {
                    float2 texel = 1.0 / _ScreenParams.xy;
                    float rotation = frac(52.9829189 * frac(dot(floor(uv * _ScreenParams.xy), float2(0.06711056, 0.00583715)))) * 6.2831853;
                    half3 accumulated = centreColour;
                    for (int tapIndex = 1; tapIndex <= TILT_SHIFT_TAPS; tapIndex++)
                    {
                        float tapRadius = sqrt((float)tapIndex / TILT_SHIFT_TAPS) * radiusPixels;
                        float tapAngle = tapIndex * 2.39996323 + rotation;
                        float2 tapUv = uv + float2(cos(tapAngle), sin(tapAngle)) * tapRadius * texel;
                        accumulated += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, tapUv, 0).rgb;
                    }
                    colour = accumulated / (TILT_SHIFT_TAPS + 1);
                }

                half grey = Luminance(colour);
                colour = lerp(grey.xxx, colour, 1.0 + 0.45 * _TiltShiftStrength);
                // Gamma lift first so the dark ground opens up without blowing out the already bright props.
                colour = pow(max(colour, 0.0001h), lerp(1.0h, 0.78h, _TiltShiftStrength));
                colour *= lerp(half3(1, 1, 1), half3(1.35h, 1.25h, 1.0h), _TiltShiftStrength);
                return half4(colour, 1);
            }
            ENDHLSL
        }
    }
}
