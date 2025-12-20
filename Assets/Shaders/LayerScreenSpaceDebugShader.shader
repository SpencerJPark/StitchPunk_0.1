Shader "Hidden/LayerScreenSpace/Debug"
{
    Properties
    {
        _LayerTex ("Layer Texture", 2D) = "black" {}
        _CameraTex ("Camera Texture", 2D) = "white" {}
        _DebugMode ("Debug Mode", Int) = 0
    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Cull Off
        ZWrite Off
        ZTest Always
        
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            
            TEXTURE2D(_LayerTex);
            SAMPLER(sampler_LayerTex);
            
            TEXTURE2D(_CameraTex);
            SAMPLER(sampler_CameraTex);
            
            int _DebugMode;
            
            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                
                // Sample both textures
                half4 layerColor = SAMPLE_TEXTURE2D(_LayerTex, sampler_LayerTex, uv);
                half4 cameraColor = SAMPLE_TEXTURE2D(_CameraTex, sampler_CameraTex, uv);
                
                // Debug Mode 1: Show Layer Only
                if (_DebugMode == 1)
                {
                    return layerColor;
                }
                
                // Debug Mode 2: Show Camera Only
                if (_DebugMode == 2)
                {
                    return cameraColor;
                }
                
                // Debug Mode 3: Split Screen (left = layer, right = camera)
                if (_DebugMode == 3)
                {
                    if (uv.x < 0.5)
                    {
                        // Left half: Show layer texture
                        return layerColor;
                    }
                    else
                    {
                        // Right half: Show camera texture
                        return cameraColor;
                    }
                }
                
                // Fallback: Show camera
                return cameraColor;
            }
            ENDHLSL
        }
    }
}
