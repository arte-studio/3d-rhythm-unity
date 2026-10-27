"""演出を組み立てるための曲の特徴（アクセント，音量の流れ，盛り上がり）を JSON に書き出す．

使い方（3d-rhythmgame で）:
    py EffectEval~/music_features.py Assets/GameMusic.wav --song Assets/Rhythm/Songs/kodaisai-2025/song.json \
        --out Assets/Rhythm/Effects/kodaisai-2025-ai/music.json

書き出すもの:
    beatsPerBar, secPerBeat, bar1Sec
    beats:    拍ごとの音量（0〜1）と，低音・中音・高音の立ち上がりの強さ
    accents:  強い立ち上がり（アクセント）．時刻は近い 8 分音符の位置に寄せる（ずれが 40ms 以内のとき）．
              strength（0〜1），band（low / mid / high．どの高さの音が強いか），bar・beat（1 始まり，拍は小数）
    sections: 区切りごとの音量の平均と，音量の上がり下がり（rise: 区切りの終わりに向けて大きくなるか）
    builds:   音量が 1 小節以上かけて大きくなっていくところ（盛り上げの候補）
アクセントのどれを光らせるか，どの演出を使うかは，これを読んで決める（演出を組み立てる側の仕事）．
"""
import argparse, json, subprocess
import numpy as np

SR = 22050


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('audio')
    ap.add_argument('--song', required=True)
    ap.add_argument('--out', required=True)
    a = ap.parse_args()
    song = json.load(open(a.song, encoding='utf-8'))
    tp = song['tempo'][0]
    t0, P, bpb = tp['t'], 60.0 / tp['bpm'], tp.get('beatsPerBar', 4)
    start = song.get('audioStartSec', 0)
    x = np.frombuffer(subprocess.run(['ffmpeg', '-v', 'error', '-i', a.audio, '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'],
                                     capture_output=True, check=True).stdout, dtype=np.float32)
    dur = len(x) / SR - start
    nfft, hop = 1024, 128
    win = np.hanning(nfft)
    m = 1 + (len(x) - nfft) // hop
    idx = np.arange(nfft)[None, :] + hop * np.arange(m)[:, None]
    S = np.empty((m, nfft // 2 + 1), np.float32)
    for i in range(0, m, 4096):
        S[i:i + 4096] = np.abs(np.fft.rfft(x[idx[i:i + 4096]] * win, axis=1))
    f = np.fft.rfftfreq(nfft, 1 / SR)
    t = (np.arange(m) * hop + nfft / 2) / SR - start
    L = np.log1p(100 * S)
    def flux(mask):
        d = np.r_[0, np.maximum(0, np.diff(L[:, mask], axis=0)).sum(1)]
        base = np.convolve(d, np.ones(int(0.5 * SR / hop)) / int(0.5 * SR / hop), 'same')
        return np.maximum(0, d - base)
    bands = dict(low=flux(f < 200), mid=flux((f >= 200) & (f < 3000)), high=flux(f >= 3000))
    total = sum(v / (np.percentile(v, 99) + 1e-9) for v in bands.values())
    rms = np.sqrt((S ** 2).mean(1))
    db = 20 * np.log10(rms + 1e-9)
    top = np.percentile(db, 95)
    level = np.clip((db - (top - 30)) / 30, 0, 1)

    # 拍ごと
    first = t0 - P * np.floor(t0 / P)
    beats = []
    k = int(round((first - t0) / P))
    bt = first
    while bt < dur:
        msk = (t >= bt) & (t < bt + P)
        bar = int(np.floor(k / bpb)) + 1
        beats.append(dict(t=round(bt, 4), bar=bar, beat=int(k % bpb) + 1, level=round(float(level[msk].mean()) if msk.any() else 0, 3),
                          **{b: round(float(v[msk].max() / (np.percentile(v, 99) + 1e-9)) if msk.any() else 0, 3) for b, v in bands.items()}))
        bt += P
        k += 1

    # アクセント: 強い立ち上がり（0.25 秒以内に 1 つ）
    thr = np.percentile(total, 97.5)
    gap = int(0.25 * SR / hop)
    pk = []
    for i in range(1, len(total) - 1):
        if total[i] >= thr and total[i] >= total[i - 1] and total[i] > total[i + 1] and t[i] >= 0:
            if pk and i - pk[-1] < gap:
                if total[i] > total[pk[-1]]:
                    pk[-1] = i
            else:
                pk.append(i)
    smax = max(total[i] for i in pk) if pk else 1
    accents = []
    for i in pk:
        tt = t[i]
        q = (tt - t0) / (P / 2)
        snapped = t0 + round(q) * P / 2
        if abs(snapped - tt) <= 0.04:
            tt = snapped
        pos = round((tt - t0) / P, 2)
        band = max(bands, key=lambda b: bands[b][i] / (np.percentile(bands[b], 99) + 1e-9))
        accents.append(dict(t=round(float(tt), 4), strength=round(float(total[i] / smax), 3), band=band,
                            bar=int(np.floor(pos / bpb)) + 1, beat=round(float(pos % bpb) + 1, 2)))
    # 際立ち（salience）: 同じ区切りの中の，同じ拍の位置（8 分音符）の立ち上がりの強さの中央値に対する比．
    # 2・4 拍目のスネアのように毎小節鳴る音は 1 くらい，そこだけ強い音は大きくなる
    secs0 = song.get('sections', [])
    def sec_of(tt):
        k = 0
        for j, s_ in enumerate(secs0):
            if s_['t'] <= tt + 1e-3:
                k = j
        return k
    grid = {}
    for e8 in np.arange(-2 * bpb * 2, (dur - t0) / (P / 2) + 1):
        ts = t0 + e8 * P / 2
        if ts < 0 or ts >= dur:
            continue
        j = np.searchsorted(t, ts)
        v = total[max(0, j - 3):j + 4].max() if j < len(total) else 0
        grid.setdefault((sec_of(ts), int(e8) % (2 * bpb)), []).append(v)
    for ac in accents:
        e8 = int(round((ac['t'] - t0) / (P / 2)))
        med = np.median(grid.get((sec_of(ac['t']), e8 % (2 * bpb)), [1])) + 1e-9
        ac['salience'] = round(float(ac['strength'] * smax / med), 2)

    # 区切り
    secs = song.get('sections', [])
    sections = []
    for i, s in enumerate(secs):
        a0 = s['t']
        a1 = secs[i + 1]['t'] if i + 1 < len(secs) else dur
        msk = (t >= a0) & (t < a1)
        half = (t >= a0) & (t < (a0 + a1) / 2)
        last = (t >= a1 - 2 * P * bpb / 2) & (t < a1)
        sections.append(dict(name=s['name'], t=a0, end=round(float(a1), 3), level=round(float(level[msk].mean()), 3),
                             rise=round(float(level[last].mean() - level[half].mean()), 3),
                             accents=sum(1 for ac in accents if a0 <= ac['t'] < a1)))

    # 盛り上げ: 小節ごとの音量が 2 小節続けて上がる
    bars = {}
    for b in beats:
        bars.setdefault(b['bar'], []).append(b['level'])
    bl = {k: float(np.mean(v)) for k, v in bars.items()}
    ks = sorted(bl)
    builds = [dict(bar=k, fromLevel=round(bl[k], 3), toLevel=round(bl[k + 2], 3)) for k in ks
              if k + 2 in bl and bl[k + 1] - bl[k] > 0.03 and bl[k + 2] - bl[k + 1] > 0.03]

    out = dict(bar1Sec=t0, secPerBeat=P, beatsPerBar=bpb, lengthSec=round(dur, 3), beats=beats, accents=accents, sections=sections, builds=builds)
    with open(a.out, 'w', encoding='utf-8') as fp:
        json.dump(out, fp, ensure_ascii=False, indent=1)
    print(f"拍 {len(beats)}，アクセント {len(accents)}，盛り上げ {len(builds)}")
    for s in sections:
        print(f"  {s['name'][:10]:10s} 音量 {s['level']:.2f} 上がり {s['rise']:+.2f} アクセント {s['accents']}")
    print('  際立つアクセント（上位 16）:', ', '.join(f"{a_['bar']}.{a_['beat']:.1f}({a_['band']},{a_['salience']:.1f})"
                                            for a_ in sorted(accents, key=lambda a_: -a_['salience'])[:16]))


if __name__ == '__main__':
    main()
