Shader "Custom/RobertsCrossOutline"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineThickness ("Outline Thickness", Range(0.1, 10)) = 1.0
        _RobertsCrossMultiplier ("Edge Sensitivity", Range(0.1, 10)) = 1.0
        _DepthThreshold ("Depth Threshold", Range(0, 1)) = 0.1
        _NormalThreshold ("Normal Threshold", Range(0, 1)) = 0.3
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "RobertsCrossOutline"
            
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            
            TEXTURE2D(_NormalsTexture);
            SAMPLER(sampler_NormalsTexture);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                float4 _OutlineColor;
                float _OutlineThickness;
                float _RobertsCrossMultiplier;
                float _DepthThreshold;
                float _NormalThreshold;
            CBUFFER_END
            
            // Roberts Cross kernels
            // Kernel X:  | 1  0 |    Kernel Y:  | 0  1 |
            //            | 0 -1 |               |-1  0 |
            
            float SampleDepth(float2 uv)
            {
                return SampleSceneDepth(uv);
            }
            
            float3 SampleNormal(float2 uv)
            {
                float4 normalSample = SAMPLE_TEXTURE2D(_NormalsTexture, sampler_NormalsTexture, uv);
                // Decode from 0-1 range back to -1 to 1
                return normalSample.rgb * 2.0 - 1.0;
            }
            
            float GetNormalMask(float2 uv)
            {
                // Returns 1 if there's valid geometry at this pixel, 0 otherwise
                return SAMPLE_TEXTURE2D(_NormalsTexture, sampler_NormalsTexture, uv).a;
            }
            
            float RobertsCrossDepth(float2 uv, float2 texelSize)
            {
                float2 offset = texelSize * _OutlineThickness;
                
                // Sample depths at 2x2 kernel positions
                float d00 = SampleDepth(uv);
                float d11 = SampleDepth(uv + offset);
                float d01 = SampleDepth(uv + float2(0, offset.y));
                float d10 = SampleDepth(uv + float2(offset.x, 0));
                
                // Convert to linear depth for better edge detection
                float ld00 = Linear01Depth(d00, _ZBufferParams);
                float ld11 = Linear01Depth(d11, _ZBufferParams);
                float ld01 = Linear01Depth(d01, _ZBufferParams);
                float ld10 = Linear01Depth(d10, _ZBufferParams);
                
                // Roberts Cross operators
                float gx = ld00 - ld11;
                float gy = ld01 - ld10;
                
                return sqrt(gx * gx + gy * gy);
            }
            
            float RobertsCrossNormal(float2 uv, float2 texelSize)
            {
                float2 offset = texelSize * _OutlineThickness;
                
                // Sample normals at 2x2 kernel positions
                float3 n00 = SampleNormal(uv);
                float3 n11 = SampleNormal(uv + offset);
                float3 n01 = SampleNormal(uv + float2(0, offset.y));
                float3 n10 = SampleNormal(uv + float2(offset.x, 0));
                
                // Roberts Cross on normals (use dot product difference)
                float3 gx = n00 - n11;
                float3 gy = n01 - n10;
                
                return sqrt(dot(gx, gx) + dot(gy, gy));
            }
            
            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texelSize = _MainTex_TexelSize.xy;
                
                // Sample original scene color
                float4 sceneColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                
                // Check if we have valid geometry from our normal pass
                float mask = GetNormalMask(uv);
                
                // Also check neighboring pixels for edge detection at geometry boundaries
                float2 offset = texelSize * _OutlineThickness;
                float maskNeighbor = max(
                    max(GetNormalMask(uv + float2(offset.x, 0)), GetNormalMask(uv + float2(-offset.x, 0))),
                    max(GetNormalMask(uv + float2(0, offset.y)), GetNormalMask(uv + float2(0, -offset.y)))
                );
                
                // Compute Roberts Cross edge detection
                float depthEdge = RobertsCrossDepth(uv, texelSize);
                float normalEdge = RobertsCrossNormal(uv, texelSize);
                
                // Apply thresholds
                depthEdge = smoothstep(_DepthThreshold, _DepthThreshold + 0.01, depthEdge);
                normalEdge = smoothstep(_NormalThreshold, _NormalThreshold + 0.1, normalEdge);
                
                // Combine edges
                float edge = saturate((depthEdge + normalEdge) * _RobertsCrossMultiplier);
                
                // Only show outlines where we have geometry (or adjacent to geometry)
                edge *= max(mask, maskNeighbor);
                
                // Blend outline color with scene
                float4 finalColor = lerp(sceneColor, _OutlineColor, edge * _OutlineColor.a);
                
                return finalColor;
            }
            ENDHLSL
        }
    }
}