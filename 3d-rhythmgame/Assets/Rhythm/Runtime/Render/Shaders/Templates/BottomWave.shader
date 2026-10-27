// 下の波: リングの下の方に波打つ光．波の高さが曲の音量に合わせて大きくなる（強いピンク〜青）．
// 音量は曲の音声から求めた値（_ArteLevel，0〜1）．
Shader "Arte/LED/BottomWave"
{
    Properties
    {
        _ColorA ("色 1", Color) = (1, 0.1, 0.55, 1)
        _ColorB ("色 2", Color) = (0.1, 0.3, 1, 1)
        _Base ("静かなときの高さ（全体に対する割合）", Range(0, 1)) = 0.05
        _Amp ("音量が最大のときに足す高さ", Range(0, 1)) = 0.3
        _Waves ("一周の波の数", Float) = 6
        _Speed ("波が回る速さ（1 拍で一周の何割）", Float) = 0.05
        _ColorSpeed ("色が回る速さ（1 拍で一周の何割）", Float) = 0.02
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

            float4 _ColorA, _ColorB;
            float _Base, _Amp, _Waves, _Speed, _ColorSpeed;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float a = ArteAngle01(p.xyz);
                float wave = 0.5 + 0.5 * sin(6.2831853 * (a * _Waves - _ArteBeat * _Speed));
                float h = _Base + _Amp * _ArteLevel * (0.4 + 0.6 * wave);
                float k = saturate((h - i.uv.y) / max(h * 0.5, 1e-3)) * p.w;
                float3 c = lerp(_ColorA.rgb, _ColorB.rgb, 0.5 + 0.5 * sin(6.2831853 * (a + _ArteBeat * _ColorSpeed)));
                return float4(c * k, k);
            }
            ENDCG
        }
    }
}
