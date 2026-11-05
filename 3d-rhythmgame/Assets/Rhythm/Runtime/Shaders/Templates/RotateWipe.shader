// 回りながら色が変わる: クリップの始まりから終わりにかけて，決めた向きに一周回りながら新しい色に塗り替わっていく．
// 重ね方「普通」で前の色の上に重ねる（まだ塗っていないところは下が見える）．
Shader "Arte/LED/RotateWipe"
{
    Properties
    {
        _Color ("新しい色", Color) = (0.05, 0.1, 0.4, 1)
        [Enum(Clockwise,1,CounterClockwise,0)] _Clockwise ("回る向き（上から見て）", Float) = 0
        _Start ("始まる角度（一周を 1）", Range(0, 1)) = 0
        _Soft ("塗り替わりのぼかし（一周に対する割合）", Range(0.01, 1)) = 0.15
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
            float _Clockwise, _Start, _Soft;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float a = ArteAngle01(p.xyz);
                if (_Clockwise < 0.5) a = 1 - a;
                a = frac(a - _Start);
                float k = saturate((_ArteClipProgress * (1 + _Soft) - a) / _Soft);
                return float4(_Color.rgb, k * p.w);
            }
            ENDCG
        }
    }
}
