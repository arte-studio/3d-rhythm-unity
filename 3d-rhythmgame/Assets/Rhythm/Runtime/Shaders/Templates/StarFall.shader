// 流れ星: 星が縦に落ちていく（しだれ桜のように．テープは縦に並んでいるので縦方向）．
// 先頭は「先頭の色」，尾は「尾の色」へ変わりながら消える．列ごとに落ちる速さとタイミングがばらばら．
Shader "Arte/LED/StarFall"
{
    Properties
    {
        _ColorHead ("先頭の色", Color) = (1, 1, 1, 1)
        _ColorTail ("尾の色", Color) = (1, 0.9, 0.55, 1)
        _Density ("同時に落ちている列の割合", Range(0, 1)) = 0.3
        _Speed ("落ちる速さ（1 拍で上から下までの何割）", Float) = 0.25
        _Tail ("尾の長さ（高さに対する割合）", Range(0.02, 1)) = 0.25
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

            float4 _ColorHead, _ColorTail;
            float _Density, _Speed, _Tail;

            float4 frag(ArteV2f i) : SV_Target
            {
                float col = floor(i.uv.x * _ArteCanvasSize.x);
                float rate = _Speed * (0.6 + 0.8 * ArteHash(float2(col, 3.7)));
                float t = _ArteBeat * rate + ArteHash(float2(col, 11.3));
                float cycle = floor(t);
                // この回に落ちるかは列と回ごとに決める
                if (ArteHash(float2(col, cycle * 7.13)) >= _Density) return 0;
                float head = lerp(1.02, -_Tail, frac(t));
                float d = i.uv.y - head;                // 先頭より上が尾
                if (d < -0.01 || d > _Tail) return 0;
                float k = d < 0 ? 1 : exp(-3 * d / _Tail);
                float3 c = lerp(_ColorHead.rgb, _ColorTail.rgb, saturate(d / _Tail));
                return float4(c * k, k);
            }
            ENDCG
        }
    }
}
