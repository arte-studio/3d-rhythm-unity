// 拍で光る: 決めた拍ごとにぱっと光って，すっと消える．
Shader "Arte/LED/BeatPulse"
{
    Properties
    {
        _Color ("色", Color) = (1, 1, 1, 1)
        _Every ("何拍ごとに光るか", Float) = 1
        _Decay ("消える速さ（大きいほど早く消える）", Range(0.5, 20)) = 5
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
            float _Every, _Decay;

            float4 frag(ArteV2f i) : SV_Target
            {
                float k = exp(-ArteBeatPhase(_Every) * _Decay);
                return float4(_Color.rgb * k, k);
            }
            ENDCG
        }
    }
}
