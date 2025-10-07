Shader "Rive/UnlitArray"
{
    Properties {
        _MainTexArray ("Texture Array", 2DArray) = "" {}
        _Slice ("Slice", Float) = 0
    }
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D_ARRAY(_MainTexArray);
            SAMPLER(sampler_MainTexArray);
            float _Slice;

            struct appdata { float4 vertex: POSITION; float2 uv: TEXCOORD0; };
            struct v2f     { float4 pos: SV_POSITION; float2 uv: TEXCOORD0; };

            v2f vert(appdata v) {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target {
                float3 uvw = float3(i.uv, _Slice);
                return SAMPLE_TEXTURE2D_ARRAY(_MainTexArray, sampler_MainTexArray, uvw);
            }
            ENDHLSL
        }
    }
}
