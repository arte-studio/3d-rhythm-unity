// 面が通り抜ける（3D）: 光る板が部屋を一方向に横切る．向きは板の進む向き（例: (0,1,0) で下から上）．
Shader "Arte/LED/Plane3D"
{
    Properties
    {
        _Color ("色", Color) = (1, 0.5, 0, 1)
        _Normal ("進む向き", Vector) = (0, 1, 0, 0)
        _Every ("何拍ごとに通るか", Float) = 4
        _From ("出発する位置（m．向きに沿った距離）", Float) = -2
        _To ("着く位置（m）", Float) = 3
        _Width ("板の厚さ（m）", Float) = 0.3
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

            float4 _Color, _Normal;
            float _Every, _From, _To, _Width;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float s = lerp(_From, _To, ArteBeatPhase(_Every));
                float d = abs(dot(p.xyz, normalize(_Normal.xyz)) - s);
                float k = saturate(1 - d / max(_Width, 1e-3)) * p.w;
                return float4(_Color.rgb * k, k);
            }
            ENDCG
        }
    }
}
