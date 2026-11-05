"""譜面のノーツの時刻を，一番近い拍の頭（song.json の tempo から数える）に移す（2026-11-03 ユーザー: ノーツの時刻も近くの拍に移動）．
むぎゅっとの案内は曲の拍で光るので，ノーツが拍の途中にあると数えた拍と叩く時刻がずれて分かりにくかった．
同じ時刻・同じむぎゅっとに重なるものは先のものだけ残す．テンポが 1 つだけの曲を前提にする．
使い方（3d-rhythmgame で）:
    py ../tools/BeatTool/snap_to_beats.py Assets/Rhythm/Songs/kodaisai-2025/song.json [--dry-run]
"""
import argparse, io, json, re


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('song')
    ap.add_argument('--dry-run', action='store_true')
    a = ap.parse_args()
    text = io.open(a.song, encoding='utf-8').read()
    tempo = json.loads(text)['tempo']
    assert len(tempo) == 1, 'テンポが 1 つの曲だけ'
    t0, beat = tempo[0]['t'], 60.0 / tempo[0]['bpm']
    seen, moved, drop = set(), [], 0
    out, last = [], 0
    items = list(re.finditer(r'(,?\s*)(\{"t":([-\d.eE]+),"lane":(\d+),[^}]*\})', text))
    for m in items:
        t, lane = float(m.group(3)), int(m.group(4))
        nt = round(t0 + round((t - t0) / beat) * beat, 4)
        out.append(text[last:m.start()])
        last = m.end()
        if (nt, lane) in seen:
            drop += 1
            continue
        seen.add((nt, lane))
        moved.append(abs(nt - t))
        out.append(m.group(1) + m.group(2).replace(f'"t":{m.group(3)},', f'"t":{nt},', 1))
    out.append(text[last:])
    print(f'ノーツ {len(items)}．動かした量 最大 {max(moved):.3f} 秒，平均 {sum(moved) / len(moved):.3f} 秒．重なって消した {drop}')
    if a.dry_run:
        return
    new = ''.join(out)
    json.loads(new)
    io.open(a.song, 'w', encoding='utf-8', newline='').write(new)
    print(f'書き換えた: {a.song}')


if __name__ == '__main__':
    main()
