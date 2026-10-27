// 波紋（3D）: 部屋の中の 1 点から，光の球が広がっていく．中心は m（リングの中心が原点，y が上）．
Shader "Arte/LED/Ripple3D"
{
    Properties
    {
        _Color ("色", Color) = (0, 0.8, 1, 1)
        _Center ("中心（m）", Vector) = (0, 1, 0, 0)
        _Every ("何拍ごとに出すか", Float) = 2
        _Speed ("広がる速さ（m / 拍）", Float) = 1.5
        _Width ("輪の太さ（m）", Float) = 0.25
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
            float _Every, _Speed, _Width;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float r = ArteBeatPhase(_Every) * _Every * _Speed;
                float d = abs(distance(p.xyz, _Center.xyz) - r);
                float k = saturate(1 - d / max(_Width, 1e-3)) * p.w;
                return float4(_Color.rgb * k, k);
            }
            ENDCG
        }
    }
}
