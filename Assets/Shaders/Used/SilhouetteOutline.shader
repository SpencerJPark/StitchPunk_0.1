Shader "Hidden/SilhouetteOutline"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _SilhouetteTexture ("Silhouette", 2D) = "black" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineThickness ("Outline Thickness", Range(0.5, 10)) = 1.0
        _EdgeThreshold ("Edge Threshold", Range(0.01, 1)) = 0.1
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
            
            float4 _OutlineColor;
            float _OutlineThickness;
            float _EdgeThreshold;
            float _DebugSilhouette;
            float4 _SilhouetteTexture_TexelSize;
            
            float SampleAlpha(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_SilhouetteTexture, sampler_SilhouetteTexture, uv).a;
            }
            
            float DetectEdge(float2 uv, float2 texelSize)
            {
                float2 offset = texelSize * _OutlineThickness;
                
                float centerAlpha = SampleAlpha(uv);
                
                // 8-direction sampling
                float maxDiff = 0;
                
                // Unrolled for performance
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(-offset.x, -offset.y))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(0, -offset.y))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(offset.x, -offset.y))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(-offset.x, 0))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(offset.x, 0))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(-offset.x, offset.y))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(0, offset.y))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(offset.x, offset.y))));
                
                return maxDiff;
            }
            
            // Faster 4-sample version for lower quality
            float DetectEdgeFast(float2 uv, float2 texelSize)
            {
                float2 offset = texelSize * _OutlineThickness;
                
                float centerAlpha = SampleAlpha(uv);
                
                // 4-direction (cross pattern)
                float maxDiff = 0;
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(0, -offset.y))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(-offset.x, 0))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(offset.x, 0))));
                maxDiff = max(maxDiff, abs(centerAlpha - SampleAlpha(uv + float2(0, offset.y))));
                
                return maxDiff;
            }
            
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelSize = _SilhouetteTexture_TexelSize.xy;
                
                // Sample original scene
                float4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                
                // Debug mode
                if (_DebugSilhouette > 0.5)
                {
                    float alpha = SampleAlpha(uv);
                    return float4(alpha.xxx, 1);
                }
                
                // Early out - check center first
                float centerAlpha = SampleAlpha(uv);
                
                // Quick neighbor check for early out
                float2 offset = texelSize * _OutlineThickness;
                float neighborAlpha = SampleAlpha(uv + float2(offset.x, 0));
                neighborAlpha = max(neighborAlpha, SampleAlpha(uv + float2(0, offset.y)));
                
                // If center and neighbors are both empty or both full, likely no edge
                if (centerAlpha < 0.01 && neighborAlpha < 0.01)
                    return sceneColor;
                
                // Full edge detection
                float edge = DetectEdge(uv, texelSize);
                
                // Apply threshold
                edge = smoothstep(_EdgeThreshold, _EdgeThreshold + 0.1, edge);
                
                // Blend
                return lerp(sceneColor, _OutlineColor, edge * _OutlineColor.a);
            }
            ENDHLSL
        }
    }
}