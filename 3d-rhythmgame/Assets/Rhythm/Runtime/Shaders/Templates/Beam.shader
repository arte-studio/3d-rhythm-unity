// 回る光: 光の帯が子どもの周りを回る．帯の数，太さ，速さ，ねじれ（高さで角度をずらすと螺旋になる）．
Shader "Arte/LED/Beam"
{
    Properties
    {
        _Color ("色", Color) = (1, 1, 1, 1)
        _Count ("帯の数", Float) = 2
        _Width ("帯の太さ（帯どうしの間隔に対する割合）", Range(0.02, 1)) = 0.2
        _Speed ("回る速さ（1 拍で一周の何割．マイナスで逆回り）", Float) = 0.125
        _Twist ("ねじれ（下から上で一周の何割ずれるか）", Float) = 0
        _Trail ("後ろに残る光の長さ（帯どうしの間隔に対する割合）", Range(0, 1)) = 0.3
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
            float _Count, _Width, _Speed, _Twist, _Trail;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float a = ArteAngle01(p.xyz) + _Twist * i.uv.y;
                float n = max(1, floor(_Count));
                // 帯の先頭からの後ろ向きの距離（0〜1．帯どうしの間隔を 1）
                float x = frac((a - _ArteBeat * _Speed) * n);
                if (_Speed < 0) x = 1 - x;
                float head = saturate(1 - x / max(_Width, 1e-3));
                float tail = 1 - x;                              // 後ろに残る光
                float trail = _Trail > 0 ? pow(saturate((tail - (1 - _Trail)) / _Trail), 2) * 0.5 : 0;
                float k = max(head, trail) * p.w;
                return float4(_Color.rgb * k, k);
            }
            ENDCG
        }
    }
}
