Shader "Custom/ViewSpaceNormalsCapture"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "ViewSpaceNormalsCapture"
            
            ZWrite Off        // Don't write to depth - we're using the scene's depth
            ZTest LEqual      // Only render if we pass depth test against scene
            Cull Back
            
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalVS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalVS = TransformWorldToViewNormal(TransformObjectToWorldNormal(input.normalOS));
                
                return output;
            }
            
            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                float3 normalVS = normalize(input.normalVS);
                return float4(normalVS * 0.5 + 0.5, 1.0);
            }
            ENDHLSL
        }
    }
}