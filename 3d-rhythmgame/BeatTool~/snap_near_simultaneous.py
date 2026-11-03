"""同じ流れの中で，ほぼ同時（--within 秒より近い）に来る隣どうしのむぎゅっとのノーツを，厳密に同時（先のノーツの時刻）にそろえる．
1 人で遊ぶとき，0.1〜0.3 秒ずれて隣に来る 2 つは片方しか取れなかった（2026-11-03 ユーザー: 厳密に同時にしてしまう）．
0.3 秒以内に続くまとまりの中に隣どうしがあれば，まとまり全体を先の時刻にそろえる（同じむぎゅっとが重なる分は先のものだけ残す）．
同じむぎゅっとに続けて来るだけのもの（叩き直し）はそのまま．
使い方（3d-rhythmgame で）:
    py BeatTool~/snap_near_simultaneous.py Assets/Rhythm/Songs/kodaisai-2025/song.json [--within 0.3] [--ring 15] [--dry-run]
"""
import argparse, io, re


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('song')
    ap.add_argument('--within', type=float, default=0.3)
    ap.add_argument('--ring', type=int, default=15)
    ap.add_argument('--dry-run', action='store_true')
    a = ap.parse_args()
    N = a.ring
    ring = lambda x, y: min((x - y) % N, (y - x) % N)
    text = io.open(a.song, encoding='utf-8').read()
    pat = re.compile(r'\{"t":([-\d.eE]+),"lane":(\d+),"type":"(\w+)"(?:,"flow":(\d+))?\}')
    found = list(pat.finditer(text))
    notes = [[m.group(1), int(m.group(2)), int(m.group(4) or 0)] for m in found]
    snapped, drop = [], set()
    for f in sorted(set(n[2] for n in notes)):
        idx = sorted((i for i, n in enumerate(notes) if n[2] == f), key=lambda i: float(notes[i][0]))
        # 前のノーツから --within 秒より近く続くものを 1 つのまとまりに
        groups, cur = [], [idx[0]]
        for p, q in zip(idx, idx[1:]):
            if float(notes[q][0]) - float(notes[p][0]) < a.within: cur.append(q)
            else: groups.append(cur); cur = [q]
        groups.append(cur)
        for g in groups:
            times = sorted(set(float(notes[i][0]) for i in g))
            lanes = sorted(set(notes[i][1] for i in g))
            if len(times) < 2 or not any(ring(x, y) == 1 for x in lanes for y in lanes):
                continue                       # 同じ時刻だけ，または同じむぎゅっとの叩き直しだけ
            t0 = min(g, key=lambda i: float(notes[i][0]))
            kept = {}
            for i in sorted(g, key=lambda i: float(notes[i][0])):
                if notes[i][1] in kept: drop.add(i); continue     # 同じむぎゅっとが重なる分は先のものだけ
                kept[notes[i][1]] = i
                if notes[i][0] != notes[t0][0]:
                    snapped.append((f, notes[i][0], notes[t0][0], notes[i][1]))
                    notes[i][0] = notes[t0][0]
    seen = set()
    for i, (t, lane, _) in enumerate(notes):
        if i in drop: continue
        assert (float(t), lane) not in seen, f'{t} 秒にむぎゅっと {lane} が重なる'
        seen.add((float(t), lane))
    for i in sorted(drop):
        print(f'流れ {notes[i][2]}: 同じむぎゅっと {notes[i][1]} が重なるので {found[i].group(1)} 秒のノーツを消す')
    for s in snapped:
        print(f'流れ {s[0]}: むぎゅっと {s[3]} を {s[1]} 秒 → {s[2]} 秒')
    if a.dry_run or not snapped:
        return
    out, last = [], 0
    for i, (m, n) in enumerate(zip(found, notes)):
        if i in drop:
            # 消すノーツは前の区切り（,と改行）ごと除く
            pre = text[last:m.start()]
            cut = pre.rfind(',')
            out.append(pre[:cut] if cut >= 0 else pre)
        else:
            out.append(text[last:m.start()] + text[m.start():m.end()].replace(f'"t":{m.group(1)},', f'"t":{n[0]},', 1))
        last = m.end()
    out.append(text[last:])
    io.open(a.song, 'w', encoding='utf-8', newline='').write(''.join(out))
    print(f'書き換えた: {a.song}（そろえた {len(snapped)} 個，消した {len(drop)} 個）')


if __name__ == '__main__':
    main()
