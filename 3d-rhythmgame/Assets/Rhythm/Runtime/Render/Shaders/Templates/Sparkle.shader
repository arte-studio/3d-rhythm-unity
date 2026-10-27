// きらめき: ランダムな LED がちらちら光る．
Shader "Arte/LED/Sparkle"
{
    Properties
    {
        _Color ("色", Color) = (1, 1, 1, 1)
        _Density ("光る LED の割合", Range(0, 1)) = 0.05
        _Rate ("1 拍で入れ替わる回数", Float) = 4
    }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex ArteVert
            #pragma fragment frag
            #include "../ArteLed.cginc"

            float4 _Color;
            float _Density, _Rate;

            float4 frag(ArteV2f i) : SV_Target
            {
                float2 cell = floor(i.uv * _ArteCanvasSize.xy);
                float n = floor(_ArteBeat * _Rate);
                float phase = frac(_ArteBeat * _Rate);
                float on = ArteHash(cell + n * 17.13) < _Density ? 1 : 0;
                float k = on * (1 - phase);
                return float4(_Color.rgb * k, k);
            }
            ENDCG
        }
    }
}
