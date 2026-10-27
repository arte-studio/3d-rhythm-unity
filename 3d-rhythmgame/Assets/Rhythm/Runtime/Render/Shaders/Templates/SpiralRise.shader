// 螺旋の上昇: クリップの始まりから終わりにかけて，下から上へ螺旋階段を上るように点いていく（前奏のグリッサンド）．
// 点いたところは「点いたばかりの色」から「最後の色」へ変わり，クリップの終わりで全部「最後の色」になる．
// 一周の回数を 0 にすると，螺旋でなくまっすぐ下から上へ．
Shader "Arte/LED/SpiralRise"
{
    Properties
    {
        _ColorA ("点いたばかりの色", Color) = (0.55, 0.55, 0.6, 1)
        _ColorB ("最後の色", Color) = (0.1, 0.25, 1, 1)
        _Turns ("下から上までに回る回数（0 = まっすぐ）", Float) = 2
        [Enum(Clockwise,1,CounterClockwise,0)] _Clockwise ("回る向き（上から見て）", Float) = 0
        _Change ("色が変わりきるまで（全体に対する割合）", Range(0.01, 1)) = 0.25
        _Edge ("先端のぼかし（全体に対する割合）", Range(0.001, 0.2)) = 0.02
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
            float _Turns, _Clockwise, _Change, _Edge;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float a = ArteAngle01(p.xyz);
                if (_Clockwise < 0.5) a = 1 - a;
                // 点く順番（0〜1）: 高さに，一周の中の位置を 1 段分足す
                float order = _Turns > 0.01 ? (i.uv.y * _Turns + a) / (_Turns + 1) : i.uv.y;
                float since = _ArteClipProgress - order;
                float lit = saturate(since / _Edge + 1);
                float3 c = lerp(_ColorA.rgb, _ColorB.rgb, saturate(since / _Change));
                if (_ArteClipProgress >= 0.999) c = _ColorB.rgb;
                return float4(c * lit, lit * p.w);
            }
            ENDCG
        }
    }
}
