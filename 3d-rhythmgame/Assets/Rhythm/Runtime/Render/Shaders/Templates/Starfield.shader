// 星空: ところどころの LED を星にして，柔らかくまたたかせる（白〜薄い黄色）．
// またたきの深さを小さくすると「明るさの差をあまりつけない」点滅になる．
Shader "Arte/LED/Starfield"
{
    Properties
    {
        _ColorA ("星の色 1", Color) = (1, 1, 1, 1)
        _ColorB ("星の色 2", Color) = (1, 0.92, 0.6, 1)
        _Density ("星になる LED の割合", Range(0, 1)) = 0.04
        _Brightness ("明るさ", Range(0, 2)) = 1
        _Every ("またたく周期（拍）", Float) = 2
        _Depth ("またたきの深さ（0 = 変わらない）", Range(0, 1)) = 0.3
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

            float4 _ColorA, _ColorB;
            float _Density, _Brightness, _Every, _Depth;

            float4 frag(ArteV2f i) : SV_Target
            {
                float2 cell = floor(i.uv * _ArteCanvasSize.xy);
                float h = ArteHash(cell);
                if (h >= _Density) return 0;
                float h2 = ArteHash(cell + 19.1);
                float tw = 1 - _Depth * (0.5 + 0.5 * sin(6.2831853 * (_ArteBeat / max(_Every, 1e-3) + h2)));
                float3 c = lerp(_ColorA.rgb, _ColorB.rgb, ArteHash(cell + 5.3)) * _Brightness * tw;
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
