"""譜面のレーンを，設営にあるモジュールの数に合わせて振り分け直す（song.json の notes の lane だけを書き換える．書き方は崩さない）．

工大祭版の譜面は 15 レーン（0〜14）で，3 つのノーツが同時に来て，それぞれが 3 つのグループ（0〜5，6〜10，11〜14）の中を
1 つずつ隣へ動いていく（3 本の流れ）．今の設営（kodaisai-1101）はモジュールが 11 個なので，3 つのグループを
（0〜3，4〜7，8〜10）に入れ直す．流れの「隣へ 1 つ」は隣へ 1 つのまま（グループの端では折り返す），大きく飛ぶところは同じ割合の位置へ飛ぶ．
隣のレーンのむぎゅっとはテープでつながっているので，案内の光の粒が前のノーツから次のノーツへ流れる（NoteLedRenderer）．

使い方（3d-rhythmgame で）:
    py ../tools/BeatTool/remap_lanes.py Assets/Rhythm/Songs/kodaisai-2025/song.json --from 0-5,6-10,11-14 --to 0-3,4-7,8-10 [--dry-run]
"""
import argparse, io, re


def ranges(text):
    out = []
    for part in text.split(','):
        a, b = part.split('-')
        out.append((int(a), int(b)))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('song')
    ap.add_argument('--from', dest='src', default='0-5,6-10,11-14', help='元のグループ（レーンの範囲）')
    ap.add_argument('--to', dest='dst', default='0-3,4-7,8-10', help='入れ直す先のグループ')
    ap.add_argument('--dry-run', action='store_true')
    a = ap.parse_args()
    src, dst = ranges(a.src), ranges(a.dst)
    assert len(src) == len(dst)
    text = io.open(a.song, encoding='utf-8').read()
    pat = re.compile(r'(\{"t":([-\d.eE]+),"lane":)(\d+)(,"type":"(\w+)"\})')
    notes = [(float(m.group(2)), int(m.group(3))) for m in pat.finditer(text)]
    order = sorted(range(len(notes)), key=lambda i: notes[i][0])

    def group_of(lane):
        for g, (lo, hi) in enumerate(src):
            if lo <= lane <= hi:
                return g
        raise ValueError(f'レーン {lane} がどのグループにも入っていない')

    prev_old = [None] * len(src)
    prev_new = [None] * len(src)
    direction = [1] * len(src)          # 端で折り返したら向きを反転して，元の流れの向きとの対応を保つ
    new_lane = [None] * len(notes)
    for i in order:
        t, lane = notes[i]
        g = group_of(lane)
        (slo, shi), (dlo, dhi) = src[g], dst[g]
        if prev_old[g] is None:
            # 最初は同じ割合の位置
            nl = dlo + round((lane - slo) * (dhi - dlo) / max(1, shi - slo))
        else:
            step = lane - prev_old[g]
            if abs(step) <= 1:
                nl = prev_new[g] + step * direction[g]
                if nl < dlo or nl > dhi:            # 端で折り返す
                    direction[g] = -direction[g]
                    nl = prev_new[g] + step * direction[g]
            else:
                nl = dlo + round((lane - slo) * (dhi - dlo) / max(1, shi - slo))
                direction[g] = 1
        nl = max(dlo, min(dhi, nl))
        new_lane[i] = nl
        prev_old[g], prev_new[g] = lane, nl

    # 同じ時刻に同じレーンが重ならないか
    seen = {}
    for i, (t, _) in enumerate(notes):
        key = (round(t, 3), new_lane[i])
        assert key not in seen, f'{t} 秒にレーン {new_lane[i]} が重なる'
        seen[key] = i
    adjacent = 0
    for g in range(len(src)):
        seq = [new_lane[i] for i in order if group_of(notes[i][1]) == g]
        adjacent += sum(1 for x, y in zip(seq, seq[1:]) if abs(x - y) == 1)
    print(f'ノーツ {len(notes)}．流れの中で隣へ動く（テープで光がつながる）ところ {adjacent}')
    print('最初の 8 か所:', [(round(notes[i][0], 2), notes[i][1], '→', new_lane[i]) for i in order[:24]][::1][:24])
    if a.dry_run:
        return
    k = iter(new_lane)
    out = pat.sub(lambda m: m.group(1) + str(next(k)) + m.group(4), text)
    io.open(a.song, 'w', encoding='utf-8', newline='').write(out)
    print(f'書き換えた: {a.song}')


if __name__ == '__main__':
    main()
