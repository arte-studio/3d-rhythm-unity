// 単色: キャンバス全体を 1 色にする．フェードはクリップの不透明度で．
Shader "Arte/LED/Solid"
{
    Properties
    {
        _Color ("色", Color) = (1, 1, 1, 1)
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

            float4 _Color;

            float4 frag(ArteV2f i) : SV_Target
            {
                return float4(_Color.rgb, 1);
            }
            ENDCG
        }
    }
}
