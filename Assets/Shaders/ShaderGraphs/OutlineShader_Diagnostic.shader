Shader "Custom/OutlineShader_Diagnostic"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "OutlineDiagnostic"
            
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // This should be set globally by the normals pass
            TEXTURE2D(_SceneViewSpaceNormals);
            SAMPLER(sampler_SceneViewSpaceNormals);

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                
                // Sample the source color
                half4 sourceColor = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                
                // Sample the normals texture
                half4 normalsSample = SAMPLE_TEXTURE2D(_SceneViewSpaceNormals, sampler_SceneViewSpaceNormals, uv);
                
                // DIAGNOSTIC MODE - visualize what we're getting:
                // Comment/uncomment different return statements to diagnose
                
                // TEST 1: Pass through source color (should look normal)
                // return sourceColor;
                
                // TEST 2: Show normals as colors (should see colorful normals on objects)
                return half4(normalsSample.rgb, 1.0);
                
                // TEST 3: Show if normals texture is bound (red = no normals, green = has normals)
                // float hasNormals = step(0.01, length(normalsSample.rgb));
                // return half4(1.0 - hasNormals, hasNormals, 0, 1);
            }
            ENDHLSL
        }
    }
}
