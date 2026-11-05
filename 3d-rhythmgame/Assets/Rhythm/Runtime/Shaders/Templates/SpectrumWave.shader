// 周波数の波: 曲の周波数ごとの大きさで，下から光が伸びる（イコライザーのように）．一周を周波数に割り当て，真ん中が低い音・両端が高い音．
// 同じ並びを一周に何個か置く（どこを向いていても全体が見える）．高さは曲の音から毎フレーム変わる（_ArteSpectrum）．
Shader "Arte/LED/SpectrumWave"
{
    Properties
    {
        _ColorLow ("低い音の色", Color) = (0.1, 0.3, 1, 1)
        _ColorHigh ("高い音の色", Color) = (1, 0.3, 0.7, 1)
        _Copies ("一周に並べる数", Float) = 3
        _Base ("音が無いときの高さ（全体に対する割合）", Range(0, 1)) = 0.02
        _Height ("一番大きい音のときに足す高さ", Range(0, 1)) = 0.55
        _Gamma ("大きい音を強調する（1 = そのまま，2 = 大きい音だけ高く）", Range(0.5, 4)) = 1.6
        _Soft ("てっぺんのぼかし（全体に対する割合）", Range(0.005, 0.3)) = 0.06
        _Brightness ("明るさ", Range(0, 2)) = 1
        _Scroll ("回る速さ（1 拍で一周の何割）", Float) = 0
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

            float4 _ColorLow, _ColorHigh;
            float _Copies, _Base, _Height, _Gamma, _Soft, _Brightness, _Scroll;

            float4 frag(ArteV2f i) : SV_Target
            {
                float4 p = ArtePosition(i.uv);
                float a = ArteAngle01(p.xyz) + _ArteBeat * _Scroll;
                float x = frac(a * max(1, floor(_Copies)));
                float f = abs(x * 2 - 1);                     // 0 = 真ん中（低い音），1 = 両端（高い音）
                float level = ArteSpectrumAt(f);
                float h = _Base + _Height * pow(saturate(level), _Gamma);
                float y = i.uv.y;
                float body = saturate((h - y) / max(_Soft, 1e-3));          // 高さより下が光る
                float shade = 0.35 + 0.65 * saturate(y / max(h, 1e-3));      // 根元は暗く，先が明るい（炎のように）
                float k = saturate(body * shade * _Brightness) * p.w;
                float3 c = lerp(_ColorLow.rgb, _ColorHigh.rgb, f);
                return float4(c * k, k);
            }
            ENDCG
        }
    }
}
