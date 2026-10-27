// LedPreview 用: LED 1 個をカメラに向いた小さな円で，光量（リニア，HDR）のまま足し合わせて描く．
// LED は正面から見ると一番明るく，斜めからは cos で暗く，裏からは見えない（WS2812B の視野角 120°）．
// 頂点 6 個 = LED 1 個．位置と色は LedPreview が GraphicsBuffer で渡す．
Shader "Arte/LedPreview/Point"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float3> _Positions;
            StructuredBuffer<float4> _Colors;
            StructuredBuffer<float3> _Normals;
            float4x4 _LocalToWorld;
            float _LedSize;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 corner : TEXCOORD0;
                float3 color : TEXCOORD1;
            };

            static const float2 kCorners[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

            v2f vert(uint vid : SV_VertexID)
            {
                uint led = vid / 6;
                float2 c = kCorners[vid % 6];
                float3 world = mul(_LocalToWorld, float4(_Positions[led], 1)).xyz;
                float3 n = normalize(mul((float3x3)_LocalToWorld, _Normals[led]));
                float facing = saturate(dot(n, normalize(_WorldSpaceCameraPos - world)));
                // カメラの右と上の向き（ビュー行列の 1・2 行目）
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                world += (right * c.x + up * c.y) * (_LedSize * 0.5);
                v2f o;
                o.pos = TransformWorldToHClip(world);
                o.corner = c;
                o.color = _Colors[led].rgb * facing;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // 中心が強く，縁で消える円
                float r2 = dot(i.corner, i.corner);
                float a = saturate(1 - r2);
                return half4(i.color * a * a, 0);
            }
            ENDHLSL
        }
    }
}
