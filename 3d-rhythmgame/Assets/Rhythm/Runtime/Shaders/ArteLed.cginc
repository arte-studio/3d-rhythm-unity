// LED 演出のシェーダーで使う値と道具（LedCanvas が毎フレーム global に入れる）．
// 演出は「キャンバス全面を描くシェーダー」として書く．uv の x が列（ストリップ），y が高さ（0 = 下端）．
// 返す色はリニア（マテリアルの Color はそのまま使える．画面で見える値で計算したら ArteScreenToLinear で直す）．
#ifndef ARTE_LED_INCLUDED
#define ARTE_LED_INCLUDED

#include "UnityCG.cginc"

float _ArteTime;          // 曲の時刻（秒）
float _ArteBeat;          // 拍（曲のテンポから．テンポが無ければ 120BPM）
float _ArteBar;           // 小節（小数）
float4 _ArteCanvasSize;   // (列数, LED 数, 1/列数, 1/LED 数)
float _ArteLevel;         // 曲の音量（0〜1．曲の音声から求めた値．大きい音で 1 近く）
float _ArteSpectrum[16];  // 周波数ごとの大きさ（0〜1．0 番が低い音 40Hz，15 番が高い音 12kHz．対数で等分．帯ごとにそろえてある）
float _ArteBass;          // 低い音（帯 0〜3．約 40〜170Hz．バスドラム・ベース）の大きさ（0〜1）
float _ArteMid;           // 中くらいの音（帯 4〜9．約 170Hz〜1.4kHz．歌・ピアノ・スネアの胴）
float _ArteTreble;        // 高い音（帯 10〜15．約 1.4〜12kHz．シンバル・ハイハット・歌の子音）
sampler2D _ArtePositionTex;

// クリップの値（LedEffectClip が入れる．Timeline 以外で使うときは 0）
float _ArteClipTime;      // クリップが始まってからの秒
float _ArteClipProgress;  // クリップの中の位置（0〜1）

struct ArteV2f
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

ArteV2f ArteVert(appdata_img v)
{
    ArteV2f o;
    o.pos = UnityObjectToClipPos(v.vertex);
    o.uv = v.texcoord;
    return o;
}

// このピクセルの LED の 3D 位置（m．リングの中心が原点，y が上）．w = 付いていれば 1
float4 ArtePosition(float2 uv)
{
    return tex2Dlod(_ArtePositionTex, float4(uv, 0, 0));
}

// 中心から見た角度（0〜1．一周で 1）
float ArteAngle01(float3 p)
{
    return frac(atan2(p.x, p.z) / 6.2831853 + 1.0);
}

// 拍の中の位置（0〜1）．every 拍ごとに 0 に戻る
float ArteBeatPhase(float every)
{
    return frac(_ArteBeat / max(every, 1e-3));
}

float3 ArteHue(float h)
{
    return saturate(abs(frac(h + float3(0, 2.0 / 3, 1.0 / 3)) * 6 - 3) - 1);
}

// 0〜1 の乱数（同じ p なら同じ値．格子状の模様が出にくいもの）
float ArteHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// 周波数ごとの大きさを 0〜1 の位置で引く（0 = 一番低い音，1 = 一番高い音．帯の間はなめらかにつなぐ）
float ArteSpectrumAt(float x)
{
    float f = saturate(x) * 15;
    int a = (int)floor(f);
    int b = min(a + 1, 15);
    return lerp(_ArteSpectrum[a], _ArteSpectrum[b], f - a);
}

float3 ArteScreenToLinear(float3 c)
{
    return GammaToLinearSpace(saturate(c));
}

#endif
