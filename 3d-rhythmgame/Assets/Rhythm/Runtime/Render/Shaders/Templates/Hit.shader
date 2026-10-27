// ヒット: クリップの始まりで 1 回だけ光って消える（音のアクセントに置く）．形は 全体 / 下から上 / 上から下 / 左右に広がる / 波紋．
// クリップを短く（1〜2 拍）置いて使う．光り始めはクリップの始まりちょうど．
Shader "Arte/LED/Hit"
{
    Properties
    {
        _Color ("色", Color) = (1, 1, 1, 1)
        [Enum(Whole,0,Rise,1,Fall,2,Spread,3,Ripple,4)] _Shape ("形（0 全体，1 下から上，2 上から下，3 左右に広がる，4 波紋）", Float) = 0
        _Decay ("消えるまでの秒", Float) = 0.4
        _Speed ("進む速さ（形 1〜3 は 1 秒で全体の何倍，形 4 は m/秒）", Float) = 3
        _Width ("帯の太さ（全体に対する割合．形 4 は m）", Float) = 0.25
        _Start ("始まる角度（形 3．一周を 1）", Range(0, 1)) = 0
        _Center ("波紋の中心（形 4．m）", Vector) = (0, 1, 0, 0)
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

            float4 _Color, _Center;
            float _Shape, _Decay, _Speed, _Width, _Start;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float t = _ArteClipTime;
                float fade = exp(-3.0 * t / max(_Decay, 1e-3));
                float k;
                if (_Shape < 0.5)
                {
                    k = 1;
                }
                else if (_Shape < 3.5)
                {
                    float x;
                    if (_Shape < 1.5) x = i.uv.y;
                    else if (_Shape < 2.5) x = 1 - i.uv.y;
                    else
                    {
                        float a = abs(frac(ArteAngle01(p.xyz) - _Start + 0.5) - 0.5) * 2;   // 始まりの角度からの距離（0〜1）
                        x = a;
                    }
                    float front = t * _Speed;
                    // 先頭は太さ _Width で明るく，通ったあとは残光
                    float head = saturate(1 - abs(x - front) / max(_Width, 1e-3));
                    float trail = x < front ? 0.35 : 0;
                    k = max(head, trail);
                }
                else
                {
                    float r = t * _Speed;
                    float d = abs(distance(p.xyz, _Center.xyz) - r);
                    k = saturate(1 - d / max(_Width, 1e-3));
                }
                k *= fade * p.w;
                return float4(_Color.rgb * k, k);
            }
            ENDCG
        }
    }
}
