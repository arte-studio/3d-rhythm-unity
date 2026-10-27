"""LED 演出を，LED に送った絵（キャンバス）と曲の音声から評価する．

使い方（3d-rhythmgame で）:
    Builds/Play/Play.exe -record Recordings/eval/A.mp4 -record-length 89 -record-canvas -effects 演出の名前 ...
    py EffectEval~/evaluate.py Recordings/eval/A --audio Assets/GameMusic.wav --song Assets/Rhythm/Songs/kodaisai-2025/song.json

出すもの（--out，既定は <名前>_eval/）:
    report.md     数値の評価（AI と人が読む）
    metrics.json  数値だけ（案を比べるとき用）
    score.png     曲全体を 1 枚にした「光の譜面」（横 = 時刻．上段 = 高さ方向の平均，中段 = 一周方向の平均，下段 = 音量・明るさ・動き）
    sheet.png     動画から 2 小節ごとに 1 コマ（mp4 があれば）

評価の観点は docs/effects/ai-proposals.md を参照．数値は「人の好み」ではなく，好みを判断する材料
（音と光が合っているか，曲の盛り上がりと光の強さが合っているか，区切りで変わるか，単調でないか，安全か）．
必要なもの: numpy，Pillow，ffmpeg．
"""
import argparse, json, os, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFont

SR = 22050


# ---------- 読み込み ----------

def load_canvas(base):
    meta = json.load(open(base + '.canvas.json', encoding='utf-8'))
    w, h = meta['width'], meta['height']
    raw = np.fromfile(base + '.canvas.rgba', dtype=np.uint8)
    n = raw.size // (w * h * 4)
    fr = raw[:n * w * h * 4].reshape(n, h, w, 4)[..., :3]   # 行 0 = 下端
    return fr, meta


def load_audio(path):
    raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-ac', '1', '-ar', str(SR), '-f', 'f32le', '-'],
                         capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def srgb_to_linear(c):
    c = c / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def tempo_of(song):
    t = song['tempo'][0]
    return t['t'], 60.0 / t['bpm'], t.get('beatsPerBar', 4)


# ---------- 音 ----------

def audio_features(x, fps, n, start_offset):
    """フレームごとの音量（0〜1）と立ち上がりの強さ．"""
    hop = SR / fps
    loud = np.zeros(n)
    for i in range(n):
        a = int((i * hop) + start_offset * SR)
        seg = x[max(0, a - int(hop)):max(0, a + int(hop))]
        loud[i] = np.sqrt((seg ** 2).mean()) if len(seg) else 0
    db = 20 * np.log10(loud + 1e-6)
    top = np.percentile(db, 95)
    level = np.clip((db - (top - 30)) / 30, 0, 1)
    # 立ち上がり（スペクトルフラックス，フレームの時刻に合わせる）
    nfft, h2 = 1024, 256
    win = np.hanning(nfft)
    m = 1 + (len(x) - nfft) // h2
    idx = np.arange(nfft)[None, :] + h2 * np.arange(m)[:, None]
    S = np.log1p(100 * np.abs(np.fft.rfft(x[idx] * win, axis=1)))
    fl = np.r_[0, np.maximum(0, np.diff(S, axis=0)).sum(1)]
    tt = (np.arange(m) * h2 + nfft / 2) / SR - start_offset
    onset = np.zeros(n)
    fi = np.clip(np.round(tt * fps).astype(int), 0, n - 1)
    np.maximum.at(onset, fi, fl)
    onset = np.maximum(0, onset - np.convolve(onset, np.ones(15) / 15, 'same'))
    return level, onset


def peaks(sig, min_gap, thresh):
    p = []
    for i in range(1, len(sig) - 1):
        if sig[i] >= thresh and sig[i] >= sig[i - 1] and sig[i] > sig[i + 1]:
            if p and i - p[-1] < min_gap:
                if sig[i] > sig[p[-1]]:
                    p[-1] = i
            else:
                p.append(i)
    return np.array(p, dtype=int)


# ---------- 光 ----------

def light_features(fr):
    lin = srgb_to_linear(fr.astype(np.float32))
    Y = lin @ np.array([0.2126, 0.7152, 0.0722], np.float32)          # 光の量（リニア）
    L = fr.astype(np.float32).mean(-1) / 255                            # 見た目の明るさ（画面の値）
    bright = Y.mean((1, 2))
    d = np.diff(L, axis=0)
    activity = np.r_[0, np.abs(d).mean((1, 2))]                         # 動きの量
    vonset = np.r_[0, np.maximum(0, d).mean((1, 2))]                    # 明るくなる変化（光の「アタック」）
    # 場所の使い方: 高さの重心と，一周方向の偏り
    hy = Y.mean(2)
    hsum = hy.sum(1) + 1e-6
    ycent = (hy * np.arange(Y.shape[1])[None]).sum(1) / hsum / Y.shape[1]
    ang = Y.mean(1)
    th = np.linspace(0, 2 * np.pi, Y.shape[2], endpoint=False)
    aniso = np.abs((ang * np.exp(1j * th)).sum(1)) / (ang.sum(1) + 1e-6)
    spatial_var = Y.reshape(len(Y), -1).std(1) / (Y.reshape(len(Y), -1).mean(1) + 1e-3)
    return dict(lin=lin, Y=Y, bright=bright, activity=activity, vonset=vonset, ycent=ycent, aniso=aniso, svar=spatial_var)


def hue_sat(rgb):
    r, g, b = [rgb[..., i] for i in range(3)]
    mx, mn = rgb.max(-1), rgb.min(-1)
    c = mx - mn
    h = np.zeros_like(mx)
    m = c > 1e-6
    rr = (mx == r) & m; gg = (mx == g) & m & ~rr; bb = m & ~rr & ~gg
    h[rr] = ((g - b)[rr] / c[rr]) % 6
    h[gg] = (b - r)[gg] / c[gg] + 2
    h[bb] = (r - g)[bb] / c[bb] + 4
    return h / 6, np.where(mx > 0, c / (mx + 1e-6), 0), mx


def flashes(series, fps):
    """WCAG 2.3.1 に近い数え方: 明るさ（リニア）が 10% 以上（最大に対し）下がって上がる組を 1 回．1 秒の窓で最大何回か．"""
    s = series / max(series.max(), 1e-6)
    ext = [0]
    for i in range(1, len(s) - 1):
        if (s[i] - s[i - 1]) * (s[i + 1] - s[i]) < 0:
            ext.append(i)
    ext.append(len(s) - 1)
    trans = []
    for a, b in zip(ext[:-1], ext[1:]):
        if abs(s[b] - s[a]) >= 0.1 and min(s[a], s[b]) < 0.8:
            trans.append(b)
    trans = np.array(trans)
    worst = 0
    for t in trans:
        worst = max(worst, ((trans >= t) & (trans < t + fps)).sum() / 2)
    return worst


# ---------- 図 ----------

def font(size):
    for p in ['C:/Windows/Fonts/meiryo.ttc', 'C:/Windows/Fonts/msgothic.ttc']:
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def score_image(fr, F, level, onset, fps, sections, t0, P, bpb, path, title):
    n = len(fr)
    step = max(1, n // 1800)
    cols = np.arange(0, n, step)
    W = len(cols)
    hstrip = fr[cols].astype(np.float32)
    # 上段: 高さ方向（列を平均）．光の量で平均してから画面の値に戻す
    lin = F['lin'][cols]
    def to8(l):
        return (np.where(l <= 0.0031308, 12.92 * l, 1.055 * np.power(np.maximum(l, 0), 1 / 2.4) - 0.055) * 255).clip(0, 255).astype(np.uint8)
    top = to8(lin.mean(2))[:, ::-1].transpose(1, 0, 2)                   # (H, W, 3) 上が上端
    mid = to8(lin.mean(1)).transpose(1, 0, 2)                            # (列, W, 3)
    top = np.repeat(top, 2, 0)
    mid = np.repeat(mid, 2, 0)
    gh = 160
    pad = 40
    Himg = pad + top.shape[0] + 10 + mid.shape[0] + 10 + gh + 30
    img = Image.new('RGB', (W + 120, Himg), (16, 16, 20))
    img.paste(Image.fromarray(top), (110, pad))
    img.paste(Image.fromarray(mid), (110, pad + top.shape[0] + 10))
    d = ImageDraw.Draw(img)
    f12, f14 = font(12), font(16)
    d.text((10, 8), title, fill=(230, 230, 230), font=f14)
    d.text((10, pad + 40), '高さ\n(上が上)', fill=(200, 200, 200), font=f12)
    d.text((10, pad + top.shape[0] + 60), '一周\n(列)', fill=(200, 200, 200), font=f12)
    gy = pad + top.shape[0] + 10 + mid.shape[0] + 10
    def line(sig, color, scale=None):
        v = sig[cols]
        v = v / (scale or (np.percentile(v, 99) + 1e-9))
        pts = [(110 + i, gy + gh - min(1, max(0, y)) * gh) for i, y in enumerate(v)]
        d.line(pts, fill=color, width=1)
    line(level, (90, 160, 255), 1.0)
    line(F['bright'], (255, 230, 120))
    line(np.convolve(F['activity'], np.ones(5) / 5, 'same'), (255, 120, 160))
    d.text((10, gy), '音量', fill=(90, 160, 255), font=f12)
    d.text((10, gy + 16), '明るさ', fill=(255, 230, 120), font=f12)
    d.text((10, gy + 32), '動き', fill=(255, 120, 160), font=f12)
    # 小節と区切り
    tt = cols / fps
    x_of = lambda t: 110 + np.searchsorted(tt, t)
    bar = P * bpb
    b = t0
    k = 1
    while b - bar > 0:
        b -= bar; k -= 1
    while b < tt[-1]:
        x = x_of(b)
        d.line([(x, gy + gh), (x, gy + gh + 4)], fill=(120, 120, 120))
        if k % 4 == 1:
            d.text((x - 4, gy + gh + 6), str(k), fill=(150, 150, 150), font=f12)
        b += bar; k += 1
    for s in sections:
        x = x_of(s['t'])
        d.line([(x, pad - 6), (x, Himg - 30)], fill=(255, 210, 90))
        d.text((x + 3, pad - 22), s['name'][:6], fill=(255, 210, 90), font=f12)
    img.save(path)


def contact_sheet(mp4, times, labels, path):
    if not os.path.exists(mp4):
        return False
    tiles = []
    for t in times:
        out = subprocess.run(['ffmpeg', '-v', 'error', '-ss', f'{t:.3f}', '-i', mp4, '-frames:v', '1', '-vf', 'scale=384:216',
                              '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-'], capture_output=True).stdout
        if len(out) == 384 * 216 * 3:
            tiles.append(Image.fromarray(np.frombuffer(out, np.uint8).reshape(216, 384, 3)))
        else:
            tiles.append(Image.new('RGB', (384, 216)))
    cols = 6
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * 386, rows * 236), (10, 10, 12))
    d = ImageDraw.Draw(sheet)
    f = font(13)
    for i, (im, lb) in enumerate(zip(tiles, labels)):
        x, y = (i % cols) * 386, (i // cols) * 236
        sheet.paste(im, (x, y + 18))
        d.text((x + 2, y + 1), lb, fill=(230, 230, 230), font=f)
    sheet.save(path)
    return True


# ---------- 本体 ----------

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('base', help='録画の名前（.canvas.rgba / .canvas.json / .mp4 の前まで）')
    ap.add_argument('--audio', required=True)
    ap.add_argument('--song', required=True)
    ap.add_argument('--out')
    ap.add_argument('--title')
    a = ap.parse_args()
    out = a.out or a.base + '_eval'
    os.makedirs(out, exist_ok=True)
    fr, meta = load_canvas(a.base)
    fps = meta['fps']
    n = len(fr)
    song = json.load(open(a.song, encoding='utf-8'))
    t0, P, bpb = tempo_of(song)
    sections = song.get('sections', [])
    x = load_audio(a.audio)
    level, onset = audio_features(x, fps, n, song.get('audioStartSec', 0) + meta.get('from', 0))
    F = light_features(fr)
    tt = np.arange(n) / fps + meta.get('from', 0)
    R = {}
    rep = [f"# 演出の評価: {a.title or meta.get('effects') or os.path.basename(a.base)}\n"]

    # 1. 安全（光の点滅）
    whole = flashes(F['bright'], fps)
    H, W = F['Y'].shape[1:]
    regions = [F['Y'][:, :, q * W // 4:(q + 1) * W // 4].mean((1, 2)) for q in range(4)]
    region = max(flashes(r, fps) for r in regions)
    lin = F['lin']
    redness = (lin[..., 0] / (lin.sum(-1) + 1e-6) >= 0.8) * lin[..., 0]
    red = flashes(redness.mean((1, 2)) + 1e-4, fps) if redness.max() > 0.02 else 0
    R['flash_whole_per_s'] = whole
    R['flash_quarter_per_s'] = region
    R['flash_red_per_s'] = red
    R['mean_brightness'] = float(F['bright'].mean())
    R['max_brightness'] = float(F['bright'].max())
    safe = whole <= 3 and region <= 3 and red <= 3
    rep.append('## 1. 安全（光の点滅）\n')
    rep.append(f"- 全体の点滅: 1 秒に最大 {whole:.1f} 回，1/4 の範囲: {region:.1f} 回，赤の点滅: {red:.1f} 回 → "
               + ('**OK**（3 回以下）' if safe else '**NG: 3 回を超える**（光過敏性発作の目安．子どもが見るので必ず直す）'))
    rep.append(f"- 光の量の平均 {R['mean_brightness']:.3f}，最大 {R['max_brightness']:.3f}（全部の LED が白で 1）\n")

    # 2. 音と光の同期
    vo = F['vonset']
    beats = np.arange(t0 - P * np.floor(t0 / P), tt[-1], P)
    ph = ((tt - t0) / P) % 1
    z = (vo * np.exp(2j * np.pi * ph)).sum()
    beat_conc = abs(z) / (vo.sum() + 1e-9)
    beat_off = np.angle(z) / (2 * np.pi) * P * 1000
    # 光の「アタック」（はっきり明るくなった瞬間）と音の立ち上がりの一致
    vthr = np.percentile(vo, 97) if vo.max() > 0 else 1
    vpk = peaks(vo, int(fps * 0.15), max(vthr, 1e-4))
    athr = np.percentile(onset, 90)
    apk = peaks(onset, int(fps * 0.15), athr)
    strong = peaks(onset, int(fps * 0.3), np.percentile(onset, 98))
    tol = 2   # ±2 フレーム（±67ms）
    def hit(src, dst):
        if len(src) == 0:
            return 0.0
        return float(np.mean([np.min(np.abs(dst - s)) <= tol if len(dst) else False for s in src]))
    beat_frames = np.round((beats - meta.get('from', 0)) * fps).astype(int)
    prec_beat = hit(vpk, beat_frames)
    prec_onset = hit(vpk, apk)
    recall_strong = hit(strong, vpk)
    # 相互相関でずれ（光が音より何 ms 早い / 遅い）
    lag_range = int(fps * 0.3)
    def norm(v):
        return (v - v.mean()) / (v.std() + 1e-9)
    xc = [np.mean(norm(vo)[max(0, k):n + min(0, k)] * norm(onset)[max(0, -k):n - max(0, k)]) for k in range(-lag_range, lag_range + 1)]
    lag = (np.argmax(xc) - lag_range) / fps * 1000
    R.update(beat_concentration=float(beat_conc), beat_offset_ms=float(beat_off), attack_count=int(len(vpk)),
             attack_on_beat=prec_beat, attack_on_onset=prec_onset, strong_accent_hit=recall_strong,
             xcorr_peak=float(max(xc)), xcorr_lag_ms=float(lag))
    rep.append('## 2. 音と光の同期\n')
    rep.append(f"- 光のアタック（はっきり明るくなる瞬間）{len(vpk)} 回．そのうち拍の上 {prec_beat * 100:.0f}%，音の立ち上がりの上 {prec_onset * 100:.0f}%（±67ms）")
    rep.append(f"- 曲の強いアクセント {len(strong)} 個のうち，光もアタックしたもの {recall_strong * 100:.0f}%")
    rep.append(f"- 光の変化の拍への集中度 {beat_conc:.2f}（0 = 拍と無関係，1 = 拍ちょうど），平均の位置は拍から {beat_off:+.0f} ms")
    rep.append(f"- 光の変化と音の立ち上がりの相互相関 {max(xc):.2f}，ずれ {lag:+.0f} ms（＋ = 光が遅い）\n")

    # 3. 盛り上がり（音量と光の強さ）
    bar = P * bpb
    first_bar = t0 - bar * np.floor(t0 / bar)
    bar_edges = np.arange(first_bar, tt[-1], bar)
    def per_bar(sig):
        v = []
        for b0 in bar_edges:
            m = (tt >= b0) & (tt < b0 + bar)
            v.append(sig[m].mean() if m.any() else 0)
        return np.array(v)
    bl, bb, ba = per_bar(level), per_bar(F['bright']), per_bar(F['activity'])
    intensity = bb / (bb.max() + 1e-9) * 0.6 + ba / (ba.max() + 1e-9) * 0.4
    def spearman(u, v):
        ru, rv = np.argsort(np.argsort(u)), np.argsort(np.argsort(v))
        return float(np.corrcoef(ru, rv)[0, 1])
    rho = spearman(bl, intensity)
    rho_b = spearman(bl, bb)
    R.update(energy_rho=rho, energy_rho_brightness=rho_b)
    rep.append('## 3. 盛り上がり（小節ごとの音量と光の強さ）\n')
    rep.append(f"- 順位相関 {rho:.2f}（光の強さ = 明るさ 0.6 + 動き 0.4），明るさだけ {rho_b:.2f}．"
               "1 に近いほど曲の盛り上がりに光が付いていく（ただし全部が音量どおりだと単調になるので 0.5〜0.8 くらいがよい）\n")

    # 4. 区切りと場面の違い
    def sec_mask(i):
        a0 = sections[i]['t']
        a1 = sections[i + 1]['t'] if i + 1 < len(sections) else tt[-1] + 1
        return (tt >= a0) & (tt < a1)
    hsv_h, hsv_s, hsv_v = hue_sat(lin.mean((1, 2)))
    rep.append('## 4. 場面（区切りごと）\n')
    rep.append('| 区切り | 音量 | 光の量 | 動き | 高さの重心 | 一周の偏り | ムラ | 色（平均） | 彩度 |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
    secv = []
    for i, s in enumerate(sections):
        m = sec_mask(i)
        if not m.any():
            continue
        mc = lin[m].mean((0, 1, 2))
        mc8 = (np.where(mc <= 0.0031308, 12.92 * mc, 1.055 * mc ** (1 / 2.4) - 0.055) * 255).clip(0, 255).astype(int)
        h_, s_, _ = hue_sat(mc[None])
        v = dict(name=s['name'], level=float(level[m].mean()), bright=float(F['bright'][m].mean()), activity=float(F['activity'][m].mean()),
                 ycent=float(F['ycent'][m].mean()), aniso=float(F['aniso'][m].mean()), svar=float(F['svar'][m].mean()),
                 color='#%02x%02x%02x' % tuple(mc8), sat=float(s_[0]), hue=float(h_[0]))
        secv.append(v)
        rep.append(f"| {s['name'][:12]} | {v['level']:.2f} | {v['bright']:.3f} | {v['activity'] * 1000:.1f} | {v['ycent']:.2f} | {v['aniso']:.2f} | {v['svar']:.2f} | {v['color']} | {v['sat']:.2f} |")
    R['sections'] = secv
    # 区切りで見た目が変わるか: 区切りの前後 1 小節の差 / ふつうの小節の前後の差
    def look(t_a, t_b):
        m = (tt >= t_a) & (tt < t_b)
        if not m.any():
            return None
        return np.r_[lin[m].mean((0, 1)).ravel() * 4, F['activity'][m].mean() * 20, F['ycent'][m].mean()]
    def change_at(t):
        u, v = look(t - bar, t), look(t, t + bar)
        return None if u is None or v is None else float(np.linalg.norm(u - v))
    at_sec = [c for c in (change_at(s['t']) for s in sections[1:]) if c is not None]
    at_bar = [c for c in (change_at(b) for b in bar_edges[1:-1]) if c is not None]
    boundary = float(np.mean(at_sec) / (np.median(at_bar) + 1e-6)) if at_sec and at_bar else 0
    # 場面どうしの違い（隣どうし）
    feats = [np.r_[np.array(int(v['color'][1:3], 16), float) / 255, int(v['color'][3:5], 16) / 255, int(v['color'][5:7], 16) / 255,
                   v['activity'] * 50, v['ycent'], v['svar'] / 3] for v in secv]
    contrast = float(np.mean([np.linalg.norm(feats[i + 1] - feats[i]) for i in range(len(feats) - 1)])) if len(feats) > 1 else 0
    R.update(boundary_change_ratio=boundary, section_contrast=contrast)
    rep.append(f"\n- 区切りでの見た目の変わり方: ふつうの小節の境目の **{boundary:.1f} 倍**（大きいほど区切りがはっきり分かる．2 倍以上を目安）")
    rep.append(f"- 隣り合う場面の違い（色・動き・高さ・ムラ）: {contrast:.2f}\n")

    # 5. 単調さと変化
    # 2 小節ごとの「見た目」をまとめ，同じ場面の中で変化があるか（ずっと同じ絵 / ずっとバラバラ を避ける）
    looks = []
    for b0 in bar_edges[::2]:
        u = look(b0, b0 + 2 * bar)
        if u is not None:
            looks.append(u)
    looks = np.array(looks)
    steps = np.linalg.norm(np.diff(looks, axis=0), axis=1) if len(looks) > 1 else np.zeros(1)
    still = float(np.mean(steps < 0.02))
    # 違う見た目の数（ざっくり: 似たものをまとめていく）
    reps_ = []
    for u in looks:
        if all(np.linalg.norm(u - r) > 0.08 for r in reps_):
            reps_.append(u)
    R.update(static_ratio=still, distinct_looks=len(reps_), mean_activity=float(F['activity'].mean()),
             spatial_variation=float(F['svar'].mean()))
    rep.append('## 5. 単調さと変化\n')
    rep.append(f"- 2 小節ごとに見た目がほとんど変わらない割合 {still * 100:.0f}%（多いと単調）")
    rep.append(f"- 違う見た目の数（ざっくり）{len(reps_)}")
    rep.append(f"- 場所によるムラ（明るさのばらつき）平均 {F['svar'].mean():.2f}（0 = 全体が同じ色．大きいほど模様や動きがある）\n")

    # 図
    title = f"{a.title or meta.get('effects')}"
    score_image(fr, F, level, onset, fps, sections, t0, P, bpb, os.path.join(out, 'score.png'), title)
    times = [b + bar * 0.5 for b in bar_edges[::2] if b + bar * 0.5 < tt[-1]]
    labels = []
    for t in times:
        bn = int(round((t - bar * 0.5 - t0) / bar)) + 1
        sec = [s['name'] for s in sections if s['t'] <= t + 1e-3]
        labels.append(f"{bn} 小節 {t:.1f}s {sec[-1][:8] if sec else ''}")
    has_sheet = contact_sheet(a.base + '.mp4', [t - meta.get('from', 0) for t in times], labels, os.path.join(out, 'sheet.png'))
    rep.append('## 図\n')
    rep.append('- `score.png`: 光の譜面（上段 = 高さ方向，中段 = 一周方向，下段 = 音量・明るさ・動き．黄色の線 = 区切り）')
    if has_sheet:
        rep.append('- `sheet.png`: 2 小節ごとのコマ（リングの中心・子どもの目の高さ）')
    with open(os.path.join(out, 'report.md'), 'w', encoding='utf-8') as fp:
        fp.write('\n'.join(rep) + '\n')
    with open(os.path.join(out, 'metrics.json'), 'w', encoding='utf-8') as fp:
        json.dump(R, fp, ensure_ascii=False, indent=1, default=float)
    print('\n'.join(rep))


if __name__ == '__main__':
    main()
