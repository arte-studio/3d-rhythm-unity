"""案どうしを比べる動画: LED の絵を一周ひらいたもの（横 = 一周 8.8m，縦 = 高さ 2m）を縦に並べ，曲の音を付ける．
使い方: py EffectEval~/compare_video.py out.mp4 名前1:表示名1 名前2:表示名2 ... --audio Assets/GameMusic.wav
"""
import argparse, json, subprocess, sys, os
import numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(__file__))
import evaluate as E

ap = argparse.ArgumentParser()
ap.add_argument('out')
ap.add_argument('items', nargs='+')
ap.add_argument('--audio', required=True)
ap.add_argument('--song', default='Assets/Rhythm/Songs/kodaisai-2025/song.json')
a = ap.parse_args()
song = json.load(open(a.song, encoding='utf-8'))
secs = song.get('sections', [])
t0, P, bpb = E.tempo_of(song)
items = [it.split(':', 1) if ':' in it else (it, it) for it in a.items]
data = [E.load_canvas('Recordings/eval/' + n) for n, _ in items]
fps = data[0][1]['fps']
n = min(len(fr) for fr, _ in data)
PW, PH, LH = 880, 200, 24
W, H = PW, 30 + len(items) * (PH + LH)
f, fb = E.font(16), E.font(20)
ff = subprocess.Popen(['ffmpeg', '-y', '-loglevel', 'error', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{W}x{H}', '-r', str(fps), '-i', '-',
                       '-i', a.audio, '-c:a', 'aac', '-b:a', '192k', '-shortest', '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '18', a.out],
                      stdin=subprocess.PIPE)
for k in range(n):
    t = k / fps
    img = Image.new('RGB', (W, H), (10, 10, 12))
    d = ImageDraw.Draw(img)
    sec = [s['name'] for s in secs if s['t'] <= t + 1e-3]
    beat = (t - t0) / P
    bar = int(np.floor(beat / bpb)) + 1
    d.text((8, 4), f'{t:5.1f} 秒   {bar} 小節 {int(np.floor(beat % bpb)) + 1} 拍   {sec[-1] if sec else ""}', fill=(255, 220, 120), font=fb)
    for i, ((name, label), (fr, meta)) in enumerate(zip(items, data)):
        y = 30 + i * (PH + LH)
        d.text((8, y + 2), label, fill=(230, 230, 230), font=f)
        img.paste(Image.fromarray(fr[k][::-1]).resize((PW, PH), Image.NEAREST), (0, y + LH))
    ff.stdin.write(img.tobytes())
ff.stdin.close()
ff.wait()
print(a.out)
