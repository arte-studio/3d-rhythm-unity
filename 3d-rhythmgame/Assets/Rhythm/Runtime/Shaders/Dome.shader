// むぎゅっとの半透明の半球（ModuleDomes）．中の LED の光が半球全体にぼんやり広がって見えるのを，LED の平均の色（_Glow，光量）で光らせる．
// 縁（視線に対して斜めになる所）を少し明るくして丸い形が分かるようにする．光はそのまま足す（暗い部屋では半球そのものはほぼ見えない）．
Shader "Arte/Viewer/Dome"
{
    Properties
    {
        _Base ("半球そのものの明るさ（暗い部屋でうっすら見える分）", Color) = (0.02, 0.02, 0.025, 1)
        _Glow ("中の LED の光（光量．ModuleDomes が入れる）", Color) = (0, 0, 0, 1)
        _GlowGain ("中の LED の光の広がり方の強さ", Float) = 0.6
        _Rim ("縁の明るさ", Float) = 0.6
    }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _Base, _Glow;
            float _GlowGain, _Rim;
            CBUFFER_END

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 view : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 world = TransformObjectToWorld(v.vertex.xyz);
                o.pos = TransformWorldToHClip(world);
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.view = GetWorldSpaceViewDir(world);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float facing = abs(dot(normalize(i.normal), normalize(i.view)));   // 両面を描くので裏も表と同じに
                float rim = pow(1 - facing, 3);
                // 中の光は正面ほど強く透け，縁は半球の中を斜めに通るのでぼんやり明るい
                float3 glow = _Glow.rgb * _GlowGain * (0.5 + 0.5 * facing + rim);
                float3 c = _Base.rgb * (0.5 + _Rim * 4 * rim) + glow;
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
}
