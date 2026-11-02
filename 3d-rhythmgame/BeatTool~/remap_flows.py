"""譜面のノーツを，3 本の流れごとに「同じむぎゅっとか隣」にだけ動くよう置き直す（時刻は変えない．lane と flow だけを書き換える）．

工大祭版の譜面は 3 つのノーツが同時に来て，それぞれが 3 つのグループ（元のレーン 0〜5，6〜10，11〜14）の中を動く 3 本の流れ．
元の流れはほとんど隣へ動くが，ところどころで大きく飛ぶ．ここでは:
- むぎゅっとは輪（最後の次は最初．--ring 個）として扱う（テープも輪につながっている）
- 流れの次のノーツは，元の流れが動いた向きへ 1 つ（元が同じなら同じ所，大きく飛ぶところも向きへ 1 つだけ）
- ほかの流れと同じむぎゅっとは使わない．できれば 2 つ以上離れる（だめなら同じ所，逆の向きの順に試す）
- どの流れかを flow（0〜2）に書く．遊ぶ人数（1〜3 人）に合わせて流れを選ぶ（GameSession.players）

使い方（3d-rhythmgame で）:
    py BeatTool~/remap_flows.py Assets/Rhythm/Songs/kodaisai-2025/song.json --groups 0-5,6-10,11-14 --ring 15 [--dry-run]
もう flow が書いてある譜面では，--groups の代わりに flow で流れを分ける（--by-flow）．
"""
import argparse, io, re


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('song')
    ap.add_argument('--groups', default='0-5,6-10,11-14', help='元の流れ（レーンの範囲）')
    ap.add_argument('--by-flow', action='store_true', help='flow で流れを分ける（もう置き直した譜面を直すとき）')
    ap.add_argument('--ring', type=int, default=15, help='むぎゅっとの数（輪）')
    ap.add_argument('--dry-run', action='store_true')
    a = ap.parse_args()
    N = a.ring
    groups = [tuple(int(x) for x in g.split('-')) for g in a.groups.split(',')]
    text = io.open(a.song, encoding='utf-8').read()
    pat = re.compile(r'\{"t":([-\d.eE]+),"lane":(\d+),"type":"(\w+)"(?:,"flow":(\d+))?\}')
    found = list(pat.finditer(text))
    notes = [(float(m.group(1)), int(m.group(2)), m.group(3), int(m.group(4) or 0)) for m in found]

    def flow_of(n):
        if a.by_flow:
            return n[3]
        for g, (lo, hi) in enumerate(groups):
            if lo <= n[1] <= hi:
                return g
        raise ValueError(f'レーン {n[1]} がどの流れにも入っていない')

    flows = len(groups) if not a.by_flow else max(n[3] for n in notes) + 1
    order = sorted(range(len(notes)), key=lambda i: (notes[i][0], flow_of(notes[i])))
    pos = [None] * flows          # 流れの今のむぎゅっと
    prev_old = [None] * flows
    new_lane = [None] * len(notes)
    ring = lambda x: x % N
    dist = lambda x, y: min((x - y) % N, (y - x) % N)
    for i in order:
        t, lane, _, _ = notes[i]
        f = flow_of(notes[i])
        if pos[f] is None:
            nl = ring(lane)
        else:
            s = lane - prev_old[f]
            d = 0 if s == 0 else (1 if s > 0 else -1)
            cands = [d, 0, -d] if d != 0 else [0, 1, -1]
            others = [pos[g] for g in range(flows) if g != f and pos[g] is not None]
            nl = None
            for need in (2, 1):                 # できれば 2 つ以上離れる．だめなら重ならないだけ
                for c in cands:
                    x = ring(pos[f] + c)
                    if all(dist(x, o) >= need for o in others):
                        nl = x
                        break
                if nl is not None:
                    break
            if nl is None:
                nl = pos[f]
        new_lane[i] = nl
        pos[f], prev_old[f] = nl, lane

    # 確かめる: 流れの中は同じか隣だけ，同じ時刻に同じむぎゅっとは無い
    steps = {'同じ': 0, '隣': 0, '飛ぶ': 0}
    last = {}
    for i in order:
        f = flow_of(notes[i])
        if f in last:
            k = dist(new_lane[i], last[f])
            steps['同じ' if k == 0 else '隣' if k == 1 else '飛ぶ'] += 1
        last[f] = new_lane[i]
    same_time = {}
    for i in order:
        key = (round(notes[i][0], 3), new_lane[i])
        assert key not in same_time, f'{notes[i][0]} 秒にむぎゅっと {new_lane[i]} が重なる'
        same_time[key] = i
    print(f'ノーツ {len(notes)}，流れ {flows} 本．流れの中の動き: {steps}')
    print('最初の 12 個:', [(round(notes[i][0], 2), flow_of(notes[i]), notes[i][1], '→', new_lane[i]) for i in order[:12]])
    if a.dry_run:
        return
    out, last_end = [], 0
    for m, i in zip(found, range(len(notes))):
        t, _, typ, _ = notes[i]
        f = flow_of(notes[i])
        flow = f',"flow":{f}' if f else ''
        out.append(text[last_end:m.start()] + f'{{"t":{m.group(1)},"lane":{new_lane[i]},"type":"{typ}"{flow}}}')
        last_end = m.end()
    out.append(text[last_end:])
    io.open(a.song, 'w', encoding='utf-8', newline='').write(''.join(out))
    print(f'書き換えた: {a.song}')


if __name__ == '__main__':
    main()
