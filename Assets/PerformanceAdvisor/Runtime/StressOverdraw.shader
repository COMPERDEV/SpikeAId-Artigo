// Shader de estresse de GPU para os casos do artigo (grupo G4_GPU).
// Quad transparente em tela cheia com um laço de cálculo por pixel: o custo é de
// preenchimento (GPU), não de submissão (CPU). Sem tag LightMode, a URP o desenha
// pelo passe SRPDefaultUnlit.
Shader "PerformanceAdvisor/StressOverdraw"
{
    Properties
    {
        _Iterations ("Iteracoes por pixel", Float) = 64
        _Color ("Cor", Color) = (1, 1, 1, 0.02)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Iterations;
            float4 _Color;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord.xy;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float2 p = i.uv;
                float a = 0;
                int n = (int)_Iterations;
                [loop]
                for (int k = 0; k < n; k++)
                {
                    p = float2(sin(p.x * 3.1 + p.y), cos(p.y * 2.7 - p.x));
                    a += p.x * p.y;
                }
                return float4(_Color.rgb * (0.5 + 0.5 * sin(a)), _Color.a);
            }
            ENDCG
        }
    }
}
