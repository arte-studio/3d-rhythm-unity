"""曲の拍と小節の頭を音声から求め，聴き比べ用のクリック音とレポートを書き出す．

使い方（3d-rhythm-unity で）:
    py 3d-rhythmgame/BeatTool~/beatgrid.py 3d-rhythmgame/Assets/GameMusic.wav --out Builds/beatgrid
    （--bpm 151.96 でテンポを固定，--excerpt 12 48 で聴き比べの範囲，--candidates 4 で書き出す候補の数）

出すもの（--out のフォルダ）:
    report.md       テンポ・拍の位置・1 拍目の候補・区間ごとのずれ・区切りの候補（AI と人が読む）
    click_N_*.wav   候補ごとに曲にクリックを重ねた音（高い音 = 1 拍目）
    result.json     数字だけ（song.json の tempo に入れる値など）

手順と考え方は docs/authoring/beat-finding.md．必要なもの: numpy，ffmpeg．
"""
import argparse, json, os, subprocess, sys, wave
import numpy as np

SR = 22050
HOP = 64          # 2.9ms
NFFT = 1024


def load(path):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32).copy()


def stft_mag(x):
    win = np.hanning(NFFT)
    n = 1 + (len(x) - NFFT) // HOP
    idx = np.arange(NFFT)[None, :] + HOP * np.arange(n)[:, None]
    out = np.empty((n, NFFT // 2 + 1), np.float32)
    for a in range(0, n, 4096):                      # メモリを使いすぎないように分けて
        out[a:a + 4096] = np.abs(np.fft.rfft(x[idx[a:a + 4096]] * win, axis=1))
    return out


def flux(L, mask):
    d = np.maximum(0, np.diff(L[:, mask], axis=0)).sum(1)
    return np.r_[0, d]


def frame_time(n):
    # フレームの中心ではなく「立ち上がりがフレームに入り始める」側に寄せる（左端に合わせる流儀）
    return (np.arange(n) * HOP + NFFT * 0.5) / SR


def concentration(t, w, period, t0=0.0):
    ph = ((t - t0) / period) % 1.0
    z = (w * np.exp(2j * np.pi * ph)).sum()
    return abs(z) / (w.sum() + 1e-12), (np.angle(z) / (2 * np.pi)) * period   # 集中度，平均の位置（秒，-P/2〜P/2）


def estimate_tempo(t, onset, lo=60, hi=200):
    # 自己相関で大まかに → 位相の集中度で 0.01 BPM まで詰める
    env = onset - np.convolve(onset, np.ones(64) / 64, 'same')
    env = np.maximum(env, 0)
    fps = SR / HOP
    ac = np.correlate(env, env, 'full')[len(env) - 1:]
    lags = np.arange(len(ac)) / fps
    m = (lags >= 60 / hi) & (lags <= 60 / lo)
    cands = []
    for i in np.argsort(-ac * m)[:2000]:
        b = 60 / lags[i]
        if m[i] and all(abs(b - c) > 2 for c in cands):
            cands.append(b)
        if len(cands) >= 4:
            break
    res = []
    for b0 in cands:
        best = max(((concentration(t, env, 60 / b)[0], b) for b in np.arange(b0 - 1.5, b0 + 1.5, 0.01)))
        res.append((best[1], best[0]))
    return sorted(res, key=lambda r: -r[1])


def chroma(S, f):
    pitch = np.full(len(f), -1)
    ok = (f > 55) & (f < 2000)
    pitch[ok] = (np.round(12 * np.log2(f[ok] / 440.0)) % 12).astype(int)
    C = np.zeros((S.shape[0], 12), np.float32)
    for k in range(12):
        C[:, k] = (S[:, pitch == k] ** 2).sum(1)
    return C / (np.linalg.norm(C, axis=1, keepdims=True) + 1e-9)


def click(freq, dur=0.03):
    tt = np.arange(int(dur * SR)) / SR
    return np.sin(2 * np.pi * freq * tt) * np.exp(-tt * 150)


def write_wav(path, y):
    y = np.clip(y, -1, 1)
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
        w.writeframes((y * 32767).astype('<i2').tobytes())


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('audio')
    ap.add_argument('--out', default='Builds/beatgrid')
    ap.add_argument('--bpm', type=float, help='テンポを固定する（分かっているとき）')
    ap.add_argument('--beats', type=int, default=4, help='拍子（1 小節の拍の数）')
    ap.add_argument('--excerpt', type=float, nargs=2, default=None, help='聴き比べの範囲（秒）．既定は曲の 1/6〜1/2')
    ap.add_argument('--candidates', type=int, default=4)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)

    x = load(a.audio)
    dur = len(x) / SR
    S = stft_mag(x)
    f = np.fft.rfftfreq(NFFT, 1 / SR)
    L = np.log1p(100 * S)
    t = frame_time(len(S))
    on_all = flux(L, f > 0)
    on_low = flux(L, f < 150)
    first_sound = np.argmax(np.abs(x) > 0.01) / SR
    rep = []

    # 1. テンポ
    tempos = estimate_tempo(t, on_all)
    bpm = a.bpm or tempos[0][0]
    P = 60 / bpm
    rep.append('# 拍の解析レポート\n')
    rep.append(f'- 音声: `{a.audio}`（{dur:.1f} 秒，最初の音 {first_sound:.3f} 秒）')
    rep.append('- テンポの候補（集中度が高いほど拍がはっきり）: ' + '，'.join(f'{b:.2f} BPM（{c:.3f}）' for b, c in tempos))
    rep.append(f'- 使うテンポ: **{bpm:.2f} BPM**' + ('（指定）' if a.bpm else '') +
               '．候補の半分・2 倍の値はどちらも「合って」見えるので，曲の感じ（速い曲か）で人が選ぶ\n')

    # 2. 拍の位置（半拍ごとの立ち上がりの平均．表拍か裏拍かは決まらない）
    _, half_pos = concentration(t, on_all, P / 2)
    beat0 = half_pos % (P / 2)                       # 半拍の格子の位置
    phases = [beat0, beat0 + P / 2]                  # 表拍と裏拍の 2 通り
    rep.append('## 拍の位置\n')
    rep.append(f'半拍ごとの音の立ち上がりは {beat0 * 1000:.0f} ms + k × {P / 2 * 1000:.1f} ms に揃う．'
               '拍はこの「1 つおき」なので候補は 2 通り（A と B）．どちらかは耳で決める\n')

    # 区間ごとのずれ（テンポが一定か）
    rep.append('| 区間（秒） | 半拍の格子からのずれ | 集中度 |\n| --- | --- | --- |')
    seg = 8 * a.beats * P
    drift = []
    s0 = max(first_sound, 0)
    while s0 + seg <= dur:
        m = (t >= s0) & (t < s0 + seg)
        c, pos = concentration(t[m], on_all[m], P / 2, beat0)
        drift.append(pos)
        rep.append(f'| {s0:.1f}〜{s0 + seg:.1f} | {pos * 1000:+.0f} ms | {c:.2f} |')
        s0 += seg
    spread = (max(drift) - min(drift)) * 1000 if drift else 0
    rep.append(f'\nずれの幅 {spread:.0f} ms．' + ('テンポは一定と見てよい' if spread < 30 else
               '**ずれていく**: テンポを少し変えるか，テンポ表で途中から直す') + '\n')

    # 3. 1 拍目（拍の格子 2 通り × 拍子の数）
    C = chroma(S, f)
    cands = []
    for pi, ph in enumerate(phases):
        beats = np.arange(ph, dur, P)
        bi = np.clip((beats * SR / HOP).astype(int), 0, len(S) - 1)
        # 拍ごとの和音（拍の間の平均）と低音の立ち上がり
        ch = np.array([C[bi[i]:bi[i + 1]].mean(0) if i + 1 < len(bi) else C[bi[i]] for i in range(len(bi))])
        ch /= np.linalg.norm(ch, axis=1, keepdims=True) + 1e-9
        change = np.r_[0, 1 - (ch[1:] * ch[:-1]).sum(1)]
        w = 3                                            # 立ち上がりを ±9ms で拾う
        low = np.array([on_low[max(0, i - w):i + w + 1].max() for i in bi])
        allo = np.array([on_all[max(0, i - w):i + w + 1].max() for i in bi])
        valid = beats > first_sound
        for k in range(a.beats):
            sel = valid & (np.arange(len(beats)) % a.beats == k)
            other = valid & ~sel
            sc_ch = change[sel].mean() / (change[other].mean() + 1e-9)
            sc_low = low[sel].mean() / (low[other].mean() + 1e-9)
            # 拍の格子の強さ（表拍は裏拍より強い音が多い）
            beat_strength = allo[valid].mean()
            t0 = beats[k]
            while t0 - a.beats * P > -1e-9 and t0 > first_sound:
                t0 -= a.beats * P
            if t0 < 0:
                t0 += a.beats * P
            # 1 小節目 = 最初の音より後で最初の小節の頭（前に弱起があれば 0 小節目）
            t1 = t0
            while t1 < first_sound - 0.05:
                t1 += a.beats * P
            cands.append(dict(grid='AB'[pi], downbeat=k, bar1=round(t1, 4), chord=sc_ch, low=sc_low, beat=beat_strength))
    bs = {g: np.mean([c['beat'] for c in cands if c['grid'] == g]) for g in 'AB'}
    for c in cands:
        c['beatRel'] = bs[c['grid']] / max(bs.values())
        c['score'] = 0.6 * c['chord'] + 0.2 * c['low'] + 0.2 * c['beatRel']
    cands.sort(key=lambda c: -c['score'])

    rep.append('## 1 拍目の候補（点の高い順）\n')
    rep.append('点 = 和音の変わりやすさ × 0.6 + 低音の強さ × 0.2 + 拍の格子の強さ × 0.2（1 拍目は和音が変わりやすく低音が強い）．'
               '**この点はあくまで目安．最後は聴き比べで決める**（工大祭版では 1 拍目に強い音が無く，点だけでは決まらなかった）\n')
    rep.append('| 順位 | 拍の格子 | 1 小節目（秒） | 点 | 和音の変化 | 低音 | 格子の強さ |\n| --- | --- | --- | --- | --- | --- | --- |')
    for i, c in enumerate(cands):
        rep.append(f"| {i + 1} | {c['grid']} | {c['bar1']:.3f} | {c['score']:.2f} | {c['chord']:.2f} | {c['low']:.2f} | {c['beatRel']:.2f} |")

    # 4. 区切りの候補（小節ごとの音色の変わり目．上位の候補の小節で数える）
    best = cands[0]
    bar = a.beats * P
    heads = np.arange(best['bar1'] - bar * np.floor(best['bar1'] / bar), dur, bar)
    feat = []
    for i in range(len(heads) - 1):
        m = (t >= heads[i]) & (t < heads[i + 1])
        feat.append(np.r_[np.log1p(S[m][:, :256].mean(0)), 4 * C[m].mean(0)])
    feat = np.array(feat)
    feat /= np.linalg.norm(feat, axis=1, keepdims=True) + 1e-9
    ssm = feat @ feat.T
    K = 4
    ker = np.kron(np.array([[1, -1], [-1, 1]]), np.ones((K, K)))
    nov = np.zeros(len(feat))
    for i in range(K, len(feat) - K):
        nov[i] = (ssm[i - K:i + K, i - K:i + K] * ker).sum()
    peaks = [i for i in range(1, len(nov) - 1) if nov[i] > nov[i - 1] and nov[i] >= nov[i + 1] and nov[i] > 0]
    peaks = sorted(sorted(peaks, key=lambda i: -nov[i])[:10])
    bar_no = lambda i: int(round((heads[i] - best['bar1']) / bar)) + 1
    rep.append('\n## 区切りの候補（1 位の候補の小節で．音色や和音がまとまって変わる小節）\n')
    rep.append('| 小節 | 秒 | 変わり方の強さ |\n| --- | --- | --- |')
    for i in peaks:
        rep.append(f'| {bar_no(i)} | {heads[i]:.3f} | {nov[i]:.1f} |')
    rep.append('\n区切りの名前（A メロなど）と正しい位置は人が聴いて決める．ここは「このあたり」の目安\n')

    # 5. 聴き比べ用のクリック音
    ex = a.excerpt or (dur / 6, dur / 2)
    rep.append('## 聴き比べ\n')
    rep.append(f'{ex[0]:.0f}〜{ex[1]:.0f} 秒にクリックを重ねた（高い音 = 1 拍目）．太鼓（ドン・タン）とクリックが重なって聞こえ，'
               '高い音で小節が始まって聞こえるものを選ぶ．どれも違えば，近いものを選んで「何拍ずれているか」を伝える\n')
    hi, lo = click(2000), click(1000)
    files = []
    for n, c in enumerate(cands[:a.candidates]):
        y = x * 0.6
        tb = c['bar1'] - bar * np.floor(c['bar1'] / bar)
        k = int(round((tb - c['bar1']) / P))
        tt = tb
        while tt < dur - 0.05:
            s = hi if k % a.beats == 0 else lo
            i = int(tt * SR); y[i:i + len(s)] += 0.5 * s[:len(y) - i]
            tt += P; k += 1
        name = f"click_{n + 1}_{c['grid']}_bar1_{c['bar1']:.3f}.wav"
        write_wav(os.path.join(a.out, name), y[int(ex[0] * SR):int(ex[1] * SR)])
        files.append(name)
        rep.append(f'- `{name}`')
    rep.append('\n決めたら譜面エディタの「1小節目(秒)」に入れて保存する（区切りと演出を動かすか聞かれる）．手順は docs/authoring/beat-finding.md')

    with open(os.path.join(a.out, 'report.md'), 'w', encoding='utf-8') as fp:
        fp.write('\n'.join(rep) + '\n')
    with open(os.path.join(a.out, 'result.json'), 'w', encoding='utf-8') as fp:
        json.dump(dict(bpm=round(float(bpm), 3), beatsPerBar=a.beats, firstSoundSec=round(float(first_sound), 3),
                       halfBeatGridSec=round(float(beat0), 4), driftMs=round(float(spread), 1),
                       candidates=[{k: (round(float(v), 4) if isinstance(v, (float, np.floating)) else v) for k, v in c.items()} for c in cands],
                       sectionBars=[dict(bar=bar_no(i), sec=round(float(heads[i]), 3)) for i in peaks], clicks=files),
                  fp, ensure_ascii=False, indent=1)
    print(open(os.path.join(a.out, 'report.md'), encoding='utf-8').read())


if __name__ == '__main__':
    main()
