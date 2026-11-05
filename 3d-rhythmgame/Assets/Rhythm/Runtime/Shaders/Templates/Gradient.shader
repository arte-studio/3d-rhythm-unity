// グラデーション: 2 色の間を，高さ方向か一周方向に変える．流す速さを 0 以外にすると動く．
Shader "Arte/LED/Gradient"
{
    Properties
    {
        _ColorA ("色 1（下 / 始まり）", Color) = (0, 0.2, 1, 1)
        _ColorB ("色 2（上 / 終わり）", Color) = (1, 0, 0.6, 1)
        [Enum(Height,0,Around,1)] _Direction ("向き（0 = 高さ，1 = 一周）", Float) = 0
        _Scroll ("流す速さ（1 拍で進む割合．0 = 止める）", Float) = 0
        [Toggle] _Mirror ("往復させる（端で色がつながる）", Float) = 1
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
            float _Direction, _Scroll, _Mirror;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float t = _Direction < 0.5 ? i.uv.y : ArteAngle01(p.xyz);
                t = frac(t + _ArteBeat * _Scroll);
                if (_Mirror > 0.5) t = 1 - abs(t * 2 - 1);
                return float4(lerp(_ColorA.rgb, _ColorB.rgb, t), 1);
            }
            ENDCG
        }
    }
}
