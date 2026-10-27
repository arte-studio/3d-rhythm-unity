# 評価した案どうしの数値を並べる: py EffectEval~/compare.py baseline v6_A v6_B v6_C（Recordings/eval/<名前>_eval/metrics.json）
import json, sys
names = sys.argv[1:]
keys = ['flash_whole_per_s','flash_quarter_per_s','mean_brightness','max_brightness','attack_count','attack_on_beat','attack_on_onset','strong_accent_hit','beat_concentration','xcorr_peak','energy_rho','boundary_change_ratio','section_contrast','mean_activity','spatial_variation','static_ratio']
M = {n: json.load(open(f'Recordings/eval/{n}_eval/metrics.json', encoding='utf-8')) for n in names}
print('%-22s' % '', ''.join('%10s' % n for n in names))
for k in keys: print('%-22s' % k, ''.join('%10.3f' % M[n][k] for n in names))
for n in names:
    print(n, ' | '.join(f"{s['name'][:3]} b{s['bright']:.2f} a{s['activity']*1000:.0f} {s['color']}" for s in M[n]['sections']))
