"""設営データのむぎゅっとの置き場所を，つながるノーツ用テープ（0.5m）が次のむぎゅっとに届くように直す．
テープが届いていない所（むぎゅっと k のテープの先が k+1 から 10cm 以上離れている）は，k+1 から後ろを，リングの中心を軸に
（リングを回る順を崩さない向きに）回して近づける．高さと中心からの距離は変えない．そのあとテープを k の 4cm 先から k+1 の 4cm 手前まで引き直す．
使い方（3d-rhythmgame で）: py ../tools/BeatTool/connect_modules.py（kodaisai-1101.json を書き換える）
"""
import io, json, math, re
p = 'Assets/Rhythm/Data/Hardware/setups/kodaisai-1101.json'
text = io.open(p, encoding='utf-8').read()
d = json.loads(text)
M = {m['lane']: m for m in d['modules']}
lanes = sorted(M)
D, GAP = 0.58, 0.04

def rot(pt, th):
    x, y, z = pt
    c, s = math.cos(th), math.sin(th)
    return [x * c + z * s, y, -x * s + z * c]

def dist(a, b): return math.dist(a, b)

def tape_end_gap(k):
    return dist(M[k]['connectTo'], M[k + 1]['mugyu'])

breaks = [k for k in lanes[:-1] if tape_end_gap(k) > 0.1]
print('届いていない:', [(k, k + 1, round(tape_end_gap(k), 2)) for k in breaks])
for k in breaks:
    a = M[k]['mugyu']
    # k+1 を k の方へ（リングを回る順を崩さない向きにだけ）回す．k+1 の角度が k の角度を越えない範囲で探す
    ang = lambda pt: math.degrees(math.atan2(pt[0], pt[2]))
    gap = (ang(M[k + 1]['mugyu']) - ang(M[k]['mugyu'])) % 360
    best = None
    for i in range(0, int(gap * 10) + 1):
        th = -math.radians(i / 10)   # 角度を減らす向き（k の方へ）
        dd = abs(dist(a, rot(M[k + 1]['mugyu'], th)) - D)
        if best is None or dd < best[0] - 1e-9 or (abs(dd - best[0]) < 1e-9 and abs(th) < abs(best[1])):
            best = (dd, th)
    th = best[1]
    for l in lanes:
        if l > k:
            for key in ('mugyu', 'connectFrom', 'connectTo'):
                M[l][key] = rot(M[l][key], th)
    print(f'  {k + 1}〜{lanes[-1]} を {math.degrees(th):+.1f}° 回した')
# テープを引き直す（最後のレーンは向きをそのまま）
for k in lanes[:-1]:
    a, b = M[k]['mugyu'], M[k + 1]['mugyu']
    u = [(b[i] - a[i]) / dist(a, b) for i in range(3)]
    M[k]['connectFrom'] = [a[i] + u[i] * GAP for i in range(3)]
    M[k]['connectTo'] = [b[i] - u[i] * GAP for i in range(3)]
for k in lanes:
    a = M[k]['mugyu']
    print(k, '角度 %4.0f°' % math.degrees(math.atan2(a[0], a[2])), '高さ %.2f' % a[1],
          ' 次まで %.2f m' % dist(a, M[k + 1]['mugyu']) if k + 1 in M else '', ' テープ %.2f m' % dist(M[k]['connectFrom'], M[k]['connectTo']))
close = min((dist(M[i]['mugyu'], M[j]['mugyu']), i, j) for i in lanes for j in lanes if j > i + 1)
print('隣でないもの同士の一番近い距離 %.2f m（レーン %d と %d）' % close)
def fmt(v): return '[' + ', '.join(f'{x:.4f}'.rstrip('0').rstrip('.') if abs(x) > 1e-9 else '0' for x in v) + ']'
lines = ',\n'.join(f'    {{ "lane": {k}, "mugyu": {fmt(M[k]["mugyu"])}, "connectFrom": {fmt(M[k]["connectFrom"])}, "connectTo": {fmt(M[k]["connectTo"])} }}' for k in lanes)
text = re.sub(r'  "modules": \[\n.*?\n  \],', '  "modules": [\n' + lines + '\n  ],', text, count=1, flags=re.S)
note = '2026-11-03: テープ（0.5m）が次のむぎゅっとに届くよう，届いていなかった所（1→2，6→7，7→8）の後ろ側をリングの中心を軸に回して近づけ，テープを引き直した（隣どうしは 0.58m）．'
text = text.replace('"modulesNote": "', '"modulesNote": "' + note, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(text)
json.loads(text)
print('書いた')
