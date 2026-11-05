// 星座: 星をひとつずつ線でつないで星座を描く（クリップの長さで描き終わる）．描き終わったら全体が少し明るく光る．
// 同じ星座を一周に何個か並べられる（どこを向いていても見えるように）．星の並びは「種」で変わる．
Shader "Arte/LED/Constellation"
{
    Properties
    {
        _StarColor ("星の色", Color) = (1, 0.95, 0.75, 1)
        _LineColor ("線の色", Color) = (0.55, 0.7, 1, 1)
        _Seed ("種（星の並び）", Float) = 1
        _Stars ("星の数（2〜8）", Range(2, 8)) = 6
        _Copies ("一周に並べる数", Float) = 3
        _Size ("星座の大きさ（一周に対する割合）", Range(0.05, 0.5)) = 0.22
        _LineWidth ("線の太さ（m）", Float) = 0.05
        _Draw ("描き終わるまで（クリップの長さに対する割合）", Range(0.05, 1)) = 0.7
        _Brightness ("明るさ", Range(0, 2)) = 1
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

            float4 _StarColor, _LineColor;
            float _Seed, _Stars, _Copies, _Size, _LineWidth, _Draw, _Brightness;

            // リングの面の上の座標（m）: 横 = 一周の長さ（半径 1.4m として），縦 = 高さ
            static const float Circ = 8.8;

            float2 starPos(int k)
            {
                float2 h = float2(ArteHash(float2(k * 1.7 + _Seed, 2.3)), ArteHash(float2(k * 3.1 + _Seed, 7.9)));
                // ジグザグに進む並び（星座らしく），高さは 0.35〜1.85m
                float x = ((float)k / max(_Stars - 1, 1) - 0.5) * _Size * Circ + (h.x - 0.5) * 0.25 * _Size * Circ;
                float y = lerp(0.35, 1.85, h.y);
                return float2(x, y);
            }

            float segDist(float2 p, float2 a, float2 b, out float along)
            {
                float2 ab = b - a;
                along = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-6));
                return length(p - (a + ab * along));
            }

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float a = ArteAngle01(p.xyz);
                int n = (int)clamp(_Stars, 2, 8);
                // 一番近い星座のコピーの中心からの横の距離（m）
                float copies = max(1, floor(_Copies));
                float local = (frac(a * copies + 0.5) - 0.5) / copies * Circ;
                float2 q = float2(local, p.y);
                float prog = saturate(_ArteClipProgress / _Draw) * (n - 1);    // 何本目の線まで描いたか
                float done = saturate((_ArteClipProgress - _Draw) / max(1 - _Draw, 1e-3));
                float3 c = 0;
                float alpha = 0;
                for (int k = 0; k < 8; k++)
                {
                    if (k >= n) break;
                    float2 s = starPos(k);
                    // 星: 線が届いたら点く（最初の星は始めから）．またたき
                    float lit = k == 0 ? 1 : saturate(prog - (k - 1));
                    float tw = 0.75 + 0.25 * sin(_ArteTime * (2.0 + k) + k * 1.3);
                    float ds = length(q - s);
                    float star = saturate(1 - ds / 0.09) * lit * tw * (1 + 0.6 * done);
                    c += _StarColor.rgb * star;
                    alpha = max(alpha, star);
                    if (k < n - 1)
                    {
                        float along;
                        float dl = segDist(q, s, starPos(k + 1), along);
                        float drawn = saturate(prog - k);                    // この線をどこまで描いたか（0〜1）
                        float on = along <= drawn ? 1 : 0;
                        float head = drawn < 1 ? saturate(1 - abs(along - drawn) * 8) * 0.8 : 0;
                        float seg = saturate(1 - dl / max(_LineWidth, 1e-3)) * on * (0.45 + head + 0.3 * done);
                        c += _LineColor.rgb * seg;
                        alpha = max(alpha, seg);
                    }
                }
                c *= _Brightness * p.w;
                alpha = saturate(alpha) * p.w;
                return float4(c, alpha);
            }
            ENDCG
        }
    }
}
