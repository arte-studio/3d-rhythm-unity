// Viewer の部屋（床・暗幕・テープ本体）: LedPreview が出す光源（近くの LED をまとめたもの）だけで照らす．
// 光源は数百個あるので，頂点ごとに計算して面の中は補間する（メッシュを細かくしてある）．画面の大きさによらず軽い．
// 光源は点光源として，面の向き（cos）と距離の 2 乗で弱まる．LED の向き（テープの正面）は考えない．
Shader "Arte/Viewer/LitByLeds"
{
    Properties
    {
        _BaseColor ("反射率（色）", Color) = (0.2, 0.2, 0.2, 1)
        [Toggle] _TwoSided ("両面から照らす", Float) = 0
        _Ambient ("最低限の明るさ（真っ暗にしない）", Float) = 0.0
        _NearM ("これより近い光源は弱める距離（m）", Float) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float4> _ArteLedLightPos;
            StructuredBuffer<float4> _ArteLedLightColor;
            int _ArteLedLightCount;
            float _ArteLedLightScale;

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _TwoSided;
            float _Ambient;
            float _NearM;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 light : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                float3 wpos = TransformObjectToWorld(v.vertex.xyz);
                float3 wn = normalize(TransformObjectToWorldNormal(v.normal));
                float3 sum = 0;
                for (int i = 0; i < _ArteLedLightCount; i++)
                {
                    float3 d = _ArteLedLightPos[i].xyz - wpos;
                    float r2 = dot(d, d) + _NearM * _NearM;   // _NearM より近づいても無限に明るくしない
                    float c = dot(wn, d) * rsqrt(r2);
                    c = _TwoSided > 0.5 ? abs(c) : saturate(c);
                    sum += _ArteLedLightColor[i].rgb * (c / r2);
                }
                v2f o;
                o.pos = TransformWorldToHClip(wpos);
                o.light = sum * _ArteLedLightScale + _Ambient;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return half4(_BaseColor.rgb * i.light, 1);
            }
            ENDHLSL
        }
    }
}
