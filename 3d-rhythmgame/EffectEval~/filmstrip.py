"""切り替わりの前後を細かく並べた帯（フィルムストリップ）．動きやタイミングを静止画で確かめる．
使い方: py EffectEval~/filmstrip.py out.png 名前1 名前2 ... --at 小節.拍 [--span 拍] [--frames 12]
"""
import argparse, json, sys, os
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(__file__))
import evaluate as E

ap = argparse.ArgumentParser()
ap.add_argument('out')
ap.add_argument('names', nargs='+')
ap.add_argument('--at', type=float, nargs='+', required=True, help='小節.拍（例 26.3 = 26 小節 3 拍目）')
ap.add_argument('--span', type=float, default=2, help='前後何拍か')
ap.add_argument('--frames', type=int, default=10)
ap.add_argument('--song', default='Assets/Rhythm/Songs/kodaisai-2025/song.json')
a = ap.parse_args()
song = json.load(open(a.song, encoding='utf-8'))
t0, P, bpb = E.tempo_of(song)
f = E.font(12)
cols = []
for at in a.at:
    bar, beat = int(at), round((at - int(at)) * 10, 3) or 1
    tc = t0 + (bar - 1) * bpb * P + (beat - 1) * P
    times = np.linspace(tc - a.span * P, tc + a.span * P, a.frames)
    rows = []
    for n in a.names:
        fr, meta = E.load_canvas('Recordings/eval/' + n)
        ims = [Image.fromarray(fr[min(len(fr) - 1, int(round(t * meta['fps'])))][::-1]).resize((176, 40), Image.NEAREST) for t in times]
        rows.append(ims)
    W, H = 176 + 2, 40 + 2
    img = Image.new('RGB', (70 + len(times) * W, 20 + len(a.names) * H), (12, 12, 14))
    d = ImageDraw.Draw(img)
    for j, t in enumerate(times):
        rel = (t - tc) / P
        d.text((70 + j * W, 3), f'{rel:+.1f}拍', fill=(255, 220, 120) if abs(rel) < 1e-6 else (180, 180, 180), font=f)
    for i, n in enumerate(a.names):
        d.text((2, 20 + i * H + 12), n, fill=(220, 220, 220), font=f)
        for j, im in enumerate(rows[i]):
            img.paste(im, (70 + j * W, 20 + i * H))
    d.text((2, 3), f'{bar}小節{beat:g}拍', fill=(255, 220, 120), font=f)
    cols.append(img)
out = Image.new('RGB', (max(c.width for c in cols), sum(c.height + 6 for c in cols)), (0, 0, 0))
y = 0
for c in cols:
    out.paste(c, (0, y)); y += c.height + 6
out.save(a.out)
