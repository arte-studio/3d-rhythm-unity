// キャンバス用の例: 下から上へ流れる色の帯と，周方向に回る明るい筋．
// 色の値は画面で見える値（sRGB）として書く．LED に送るときに曲線と明るさを掛ける．
Shader "Arte/LedCanvas/Sweep"
{
    Properties
    {
        _Speed ("帯が流れる速さ（1/秒）", Float) = 0.25
        _SpinSpeed ("筋が回る速さ（周/秒）", Float) = 0.1
        _Saturation ("鮮やかさ", Range(0, 1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _ArteTime;
            float4 _ArteCanvasSize;
            float _Speed, _SpinSpeed, _Saturation;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            float3 hue(float h)
            {
                float3 k = saturate(abs(frac(h + float3(0, 2.0 / 3, 1.0 / 3)) * 6 - 3) - 1);
                return lerp(1, k, _Saturation);
            }

            float4 frag(v2f i) : SV_Target
            {
                float band = frac(i.uv.y * 0.5 - _ArteTime * _Speed);
                float3 c = hue(band) * smoothstep(0, 0.15, band);
                float spin = frac(i.uv.x - _ArteTime * _SpinSpeed);
                c += exp(-spin * 40) * 0.8;
                // キャンバスは sRGB の RenderTexture なので，書いた値が画面で見える値になるよう光量に直して返す
                return float4(GammaToLinearSpace(saturate(c)), 1);
            }
            ENDCG
        }
    }
}
