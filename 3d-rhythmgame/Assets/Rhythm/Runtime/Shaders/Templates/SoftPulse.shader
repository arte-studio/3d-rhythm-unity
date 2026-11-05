// 柔らかい点滅: 拍に合わせて全体の明るさを少しだけ上げ下げする．重ね方「掛ける（Multiply）」で使う．
// 拍の瞬間が一番明るく，深さの分だけ暗くなる（深さを小さくすると明るさの差があまりつかない）．
Shader "Arte/LED/SoftPulse"
{
    Properties
    {
        _Every ("周期（拍）", Float) = 1
        _Depth ("暗くなる深さ（0〜1）", Range(0, 1)) = 0.2
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

            float _Every, _Depth;

            float4 frag(ArteV2f i) : SV_Target
            {
                float k = 1 - _Depth * (0.5 - 0.5 * cos(6.2831853 * ArteBeatPhase(_Every)));
                return float4(k, k, k, 1);
            }
            ENDCG
        }
    }
}
