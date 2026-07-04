Shader "Custom/ViewSpaceNormalsSimple"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "ViewSpaceNormals"
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalVS : TEXCOORD0;
            };
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Transform position to clip space
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                
                // Transform normal to view space
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                float3 normalWS = normalInput.normalWS;
                output.normalVS = TransformWorldToViewDir(normalWS);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Normalize the view-space normal
                float3 normalVS = normalize(input.normalVS);
                
                // Encode to 0-1 range for storage
                half3 encoded = normalVS * 0.5 + 0.5;
                
                return half4(encoded, 1.0);
            }
            ENDHLSL
        }
    }
}
