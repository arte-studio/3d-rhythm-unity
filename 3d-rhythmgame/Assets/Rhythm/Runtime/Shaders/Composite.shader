// LedCanvas.Composite 用: 演出 1 つ分（_MainTex）を今のキャンバス（_Dst）に重ねる．色はどちらもリニア．
// _Mode: 0 = 普通（不透明度で混ぜる．演出の a も使う），1 = 足す，2 = 掛ける，3 = 明るい方
Shader "Arte/LedCanvas/Composite"
{
    Properties
    {
        _MainTex ("演出", 2D) = "black" {}
    }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _Dst;
            float _Mode;
            float _Opacity;

            float4 frag(v2f_img i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                float3 dst = tex2D(_Dst, i.uv).rgb;
                float k = _Opacity;
                float3 o;
                if (_Mode < 0.5) o = lerp(dst, src.rgb, k * src.a);
                else if (_Mode < 1.5) o = dst + src.rgb * src.a * k;
                else if (_Mode < 2.5) o = lerp(dst, dst * src.rgb, k * src.a);
                else o = lerp(dst, max(dst, src.rgb), k * src.a);
                return float4(o, 1);
            }
            ENDCG
        }
    }
}
