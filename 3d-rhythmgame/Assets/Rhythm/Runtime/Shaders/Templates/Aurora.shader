// オーロラ: 揺れる光のカーテンが一周を流れる．下の色から上の色へ．曲の音量で明るさを少し変えられる．
Shader "Arte/LED/Aurora"
{
    Properties
    {
        _ColorA ("下の色", Color) = (0.1, 1, 0.6, 1)
        _ColorB ("上の色", Color) = (0.5, 0.2, 1, 1)
        _Low ("カーテンの下端（高さの割合）", Range(0, 1)) = 0.2
        _High ("カーテンの上端（高さの割合）", Range(0, 1)) = 0.95
        _Folds ("一周のひだの数（整数）", Float) = 5
        _Speed ("流れる速さ（1 拍で一周の何割）", Float) = 0.01
        _Brightness ("明るさ", Range(0, 2)) = 1
        _Level ("曲の音量で明るさを変える量（0〜1）", Range(0, 1)) = 0.3
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
            float _Low, _High, _Folds, _Speed, _Brightness, _Level;

            // 一周でつながる 1 次元のノイズ（period 個の格子で繰り返す．つなぎ目で段差を作らない）
            float noise1(float x, float period, float salt)
            {
                float i = floor(x), f = frac(x);
                float a = ArteHash(float2(fmod(fmod(i, period) + period, period), salt));
                float b = ArteHash(float2(fmod(fmod(i + 1, period) + period, period), salt));
                return lerp(a, b, f * f * (3 - 2 * f));
            }

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float a = ArteAngle01(p.xyz);
                float y = i.uv.y;
                float flow = _ArteBeat * _Speed;
                // ひだ（2 つの速さのうねりを重ねる）．上下で少しずれてカーテンらしくする
                float n = max(1, round(_Folds));
                float u = (a + flow) * n + 0.6 * sin(6.2831853 * (y * 0.7 + _ArteBeat * 0.03));
                float u2 = (a - flow * 0.5) * n * 2 + 0.4 * sin(6.2831853 * (y * 0.5 - _ArteBeat * 0.02));
                float folds = 0.55 * noise1(u, n, 3.1) + 0.45 * noise1(u2, n * 2, 8.7);
                folds = pow(saturate(folds), 1.6) * 1.6;
                // 縦の形: 下端ははっきり，上はぼんやり消える
                float h = saturate((y - _Low) / max(_High - _Low, 1e-3));
                float bottom = smoothstep(0.0, 0.08, h);
                float top = 1 - smoothstep(0.55, 1.0, h);
                float k = folds * bottom * top * _Brightness * (1 - _Level + _Level * _ArteLevel) * p.w;
                float3 c = lerp(_ColorA.rgb, _ColorB.rgb, smoothstep(0.1, 0.8, h));
                k = saturate(k);
                return float4(c * k, k);
            }
            ENDCG
        }
    }
}
