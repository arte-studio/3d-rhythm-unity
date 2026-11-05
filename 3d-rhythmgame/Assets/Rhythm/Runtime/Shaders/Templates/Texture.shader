// 画像・動画を貼る: テクスチャ（画像や動画の RenderTexture）をキャンバスに貼る．
Shader "Arte/LED/Texture"
{
    Properties
    {
        _MainTex ("画像", 2D) = "white" {}
        [Enum(Canvas,0,Around,1)] _Mapping ("貼り方（0 = キャンバスそのまま，1 = 一周に巻き付け）", Float) = 1
        _Tint ("色を掛ける", Color) = (1, 1, 1, 1)
        _ScrollX ("横に流す速さ（1 拍で進む割合）", Float) = 0
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Tint;
            float _Mapping, _ScrollX;

            float4 frag(ArteV2f i) : SV_Target
            {
                float2 uv = i.uv;
                if (_Mapping > 0.5) uv = float2(ArteAngle01(ArtePosition(i.uv).xyz), i.uv.y);
                uv.x += _ArteBeat * _ScrollX;
                return float4(tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb * _Tint.rgb, 1);
            }
            ENDCG
        }
    }
}
