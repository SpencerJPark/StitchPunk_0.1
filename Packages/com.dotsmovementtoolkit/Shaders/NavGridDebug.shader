// Vertex-coloured, unlit, double-sided transparent geometry for NavGridDebugRenderSystem.
// Deliberately untagged by render pipeline: an unlit pass with no LightMode tag is drawn by
// both the built-in pipeline and URP/HDRP's default unlit pass, so the package needs no
// pipeline-specific variant. In a player build this shader is only reachable if it is added to
// Project Settings > Graphics > Always Included Shaders — see the README's debug view section.
Shader "Hidden/DotsMovementToolkit/NavGridDebug"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex VertexStage
            #pragma fragment FragmentStage
            #include "UnityCG.cginc"

            struct VertexInput
            {
                float4 vertex : POSITION;
                fixed4 color  : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FragmentInput
            {
                float4 position : SV_POSITION;
                fixed4 color    : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            FragmentInput VertexStage(VertexInput input)
            {
                FragmentInput output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                return output;
            }

            fixed4 FragmentStage(FragmentInput input) : SV_Target
            {
                return input.color;
            }
            ENDCG
        }
    }

    Fallback Off
}
