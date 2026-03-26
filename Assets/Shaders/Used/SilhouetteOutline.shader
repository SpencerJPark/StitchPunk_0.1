Shader "Hidden/SilhouetteOutline"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _SilhouetteTexture ("Silhouette", 2D) = "black" {}
        _SceneDepth ("Scene Depth", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineThickness ("Outline Thickness", Range(0.5, 10)) = 1.0
        _EdgeThreshold ("Edge Threshold", Range(0.01, 1)) = 0.1
        _InnerLineWidth ("Inner Line Width", Range(0.1, 0.9)) = 0.35
        _OuterGlowOpacity ("Outer Glow Opacity", Range(0, 1)) = 0.6
        _DebugSilhouette ("Debug Silhouette", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SilhouetteOutline"

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_SilhouetteTexture);
            SAMPLER(sampler_SilhouetteTexture);

            TEXTURE2D(_SceneDepth);
            SAMPLER(sampler_SceneDepth);

            float4 _OutlineColor;
            float _OutlineThickness;
            float _EdgeThreshold;
            float _InnerLineWidth;
            float _OuterGlowOpacity;
            float _DebugSilhouette;
            float4 _SilhouetteTexture_TexelSize;

            float SampleAlpha(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_SilhouetteTexture, sampler_SilhouetteTexture, uv).a;
            }

            float SampleRawDepth(float2 uv)
            {
                return SAMPLE_DEPTH_TEXTURE(_SceneDepth, sampler_SceneDepth, uv);
            }

            // Returns 1 if any of the 8 neighbours at 'offset' is inside the silhouette.
            // Since we only call this on outside pixels (centerAlpha < 0.5), a hit means
            // the character is within that radius in some direction.
            float MaxNeighborAlpha(float2 uv, float2 offset)
            {
                float m = 0;
                m = max(m, SampleAlpha(uv + float2(-offset.x, -offset.y)));
                m = max(m, SampleAlpha(uv + float2( 0,        -offset.y)));
                m = max(m, SampleAlpha(uv + float2( offset.x, -offset.y)));
                m = max(m, SampleAlpha(uv + float2(-offset.x,  0       )));
                m = max(m, SampleAlpha(uv + float2( offset.x,  0       )));
                m = max(m, SampleAlpha(uv + float2(-offset.x,  offset.y)));
                m = max(m, SampleAlpha(uv + float2( 0,         offset.y)));
                m = max(m, SampleAlpha(uv + float2( offset.x,  offset.y)));
                return m;
            }

            // Returns the scene depth of the silhouette neighbour with the highest alpha
            // at 'offset'. Used to compare against the outline pixel's own depth so we
            // can suppress outline pixels that sit on an occluder in front of the character.
            float NearestCharDepth(float2 uv, float2 offset)
            {
                float bestA = 0;
                float bestD = 0;
                float a; float2 n;

                n = uv + float2(-offset.x, -offset.y); a = SampleAlpha(n); if (a > bestA) { bestA = a; bestD = SampleRawDepth(n); }
                n = uv + float2( 0,        -offset.y); a = SampleAlpha(n); if (a > bestA) { bestA = a; bestD = SampleRawDepth(n); }
                n = uv + float2( offset.x, -offset.y); a = SampleAlpha(n); if (a > bestA) { bestA = a; bestD = SampleRawDepth(n); }
                n = uv + float2(-offset.x,  0       ); a = SampleAlpha(n); if (a > bestA) { bestA = a; bestD = SampleRawDepth(n); }
                n = uv + float2( offset.x,  0       ); a = SampleAlpha(n); if (a > bestA) { bestA = a; bestD = SampleRawDepth(n); }
                n = uv + float2(-offset.x,  offset.y); a = SampleAlpha(n); if (a > bestA) { bestA = a; bestD = SampleRawDepth(n); }
                n = uv + float2( 0,         offset.y); a = SampleAlpha(n); if (a > bestA) { bestA = a; bestD = SampleRawDepth(n); }
                n = uv + float2( offset.x,  offset.y); a = SampleAlpha(n); if (a > bestA) { bestA = a; bestD = SampleRawDepth(n); }

                return bestD;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelSize = _SilhouetteTexture_TexelSize.xy;

                float4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);

                if (_DebugSilhouette > 0.5)
                {
                    return float4(SampleAlpha(uv).xxx, 1);
                }

                float centerAlpha = SampleAlpha(uv);

                // Outline draws only outside the silhouette — never tint the character itself.
                if (centerAlpha > 0.5)
                    return sceneColor;

                float2 outerOffset = texelSize * _OutlineThickness;

                // Early out — character not reachable within full thickness, nothing to draw.
                if (MaxNeighborAlpha(uv, outerOffset) < 0.5)
                    return sceneColor;

                // Occlusion suppression — if this pixel's geometry is in front of the
                // nearest character pixel, an occluder is covering that part of the outline.
                float centerDepth = SampleRawDepth(uv);
                float charDepth   = NearestCharDepth(uv, outerOffset);
                #if UNITY_REVERSED_Z
                if (centerDepth > charDepth + 0.001)
                #else
                if (centerDepth < charDepth - 0.001)
                #endif
                    return sceneColor;

                // --- Hard inner line ---
                // Fires for pixels within _InnerLineWidth fraction of the full thickness.
                float2 innerOffset = outerOffset * _InnerLineWidth;
                float hardHit = MaxNeighborAlpha(uv, innerOffset);
                float hardLine = smoothstep(0.3, 0.9, hardHit);

                // --- Soft outer gradient ---
                // Sample at 4 radii between inner and outer with linearly decreasing weights.
                // MaxNeighborAlpha at radius r = 1 when the pixel is within r of the character.
                // Closer pixels satisfy more of the four steps → higher weighted sum → brighter glow.
                // Further pixels satisfy only the large-radius steps → lower weighted sum → dimmer.
                //
                //  t     radius         weight
                //  0.25  inner→25% out  0.75  (brightest contribution)
                //  0.50  inner→50% out  0.50
                //  0.75  inner→75% out  0.25
                //  1.00  outer          0.10  (keep a faint hint at the far edge)
                //
                const float wSum = 0.75 + 0.50 + 0.25 + 0.10; // = 1.60

                float glow = 0;
                glow += MaxNeighborAlpha(uv, lerp(innerOffset, outerOffset, 0.25)) * 0.75;
                glow += MaxNeighborAlpha(uv, lerp(innerOffset, outerOffset, 0.50)) * 0.50;
                glow += MaxNeighborAlpha(uv, lerp(innerOffset, outerOffset, 0.75)) * 0.25;
                glow += MaxNeighborAlpha(uv, outerOffset)                          * 0.10;
                glow /= wSum;                        // normalise to [0, 1]
                glow *= (1.0 - hardLine);            // don't overlap the hard line
                glow *= _OuterGlowOpacity;

                float finalEdge = saturate(hardLine + glow);
                return lerp(sceneColor, _OutlineColor, finalEdge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}
