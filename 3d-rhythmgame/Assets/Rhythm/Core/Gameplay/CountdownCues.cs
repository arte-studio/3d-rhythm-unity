using System;
using System.Collections.Generic;
using System.Linq;
using Arte.Rhythm.Charts;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// 3，2，1，はい の音を鳴らす時刻（2026-11-04 ユーザー: 3，2，1 に音を付けて．はいでも鳴るように）．
    /// むぎゅっとの案内（NoteLedRenderer の Beats）と同じく曲の拍の頭で数える: ノーツの拍 B に対して B−3，B−2，B−1 が 3，2，1，
    /// ノーツ時刻が はい（Count = 0）．同じ時刻の同じ音は 1 回にまとめる（2〜3 人で案内が重なっても 1 回）．
    /// </summary>
    public static class CountdownCues
    {
        public readonly struct Cue
        {
            public readonly double Time;
            /// <summary>3，2，1，または 0（はい）．</summary>
            public readonly int Count;
            public Cue(double time, int count) { Time = time; Count = count; }
            public override string ToString() => $"{Count}@{Time:F3}";
        }

        public static List<Cue> From(Chart chart, TempoMap tempo, int beats = 3)
        {
            var map = tempo ?? new TempoMap(null);
            var cues = new List<Cue>();
            foreach (var n in chart.Notes)
            {
                if (n.Type != NoteType.Touch) continue;
                int head = (int)Math.Ceiling(map.BeatOf(n.Time) - 1e-6);
                for (int k = beats; k >= 1; k--) cues.Add(new Cue(map.TimeOf(head - k), k));
                cues.Add(new Cue(n.Time, 0));
            }
            var result = new List<Cue>();
            foreach (var c in cues.OrderBy(c => c.Time).ThenBy(c => c.Count))
            {
                // 時刻順なので，同じ時刻のものは後ろの方にだけある
                bool dup = false;
                for (int i = result.Count - 1; i >= 0 && c.Time - result[i].Time < 1e-3; i--)
                    if (result[i].Count == c.Count) dup = true;
                if (!dup) result.Add(c);
            }
            return result;
        }
    }
}
