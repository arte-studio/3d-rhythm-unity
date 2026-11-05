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
    /// <summary>
    /// 何拍前から数えるか（2026-11-05 ユーザー: 数え始めではなく数え終わりの合図をそろえる）．
    /// ふつうは 3，2，1，はい，むずかしいは 2，1，はい．どちらも終わりは 1，はい．
    /// 遊ぶ譜面からは，前のノーツから 数える拍 + 1 あかないノーツを外す（SongData.ToChart．前の はい と次の数え始めが重ならない）．
    /// </summary>
    public static class CountIn
    {
        public const int Normal = 3;
        public const int Hard = 2;

        public static string Name(int beats) => beats <= Hard ? "2，1，はい（むずかしい）" : "3，2，1，はい（ふつう）";
    }

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

        /// <summary>
        /// ノーツが拍の頭にあるとみなす幅（拍）．song.json の時刻は 0.1ms に丸めてあるので，拍の頭のノーツでも
        /// 拍よりごくわずか後ろになることがある．狭すぎると次の拍のノーツとして数えて 1 と はい が重なる（2026-11-05 ユーザー: 3，2，はい になる）．
        /// </summary>
        public const double OnBeatTolerance = 0.01;

        /// <summary>ノーツの拍の頭（ノーツが拍の途中ならその次の拍）．3，2，1 はこの 3，2，1 拍前．</summary>
        public static int HeadBeat(double noteBeat) => (int)Math.Ceiling(noteBeat - OnBeatTolerance);

        public static List<Cue> From(Chart chart, TempoMap tempo, int beats = 3)
        {
            var map = tempo ?? new TempoMap(null);
            var cues = new List<Cue>();
            foreach (var n in chart.Notes)
            {
                if (n.Type != NoteType.Touch) continue;
                int head = HeadBeat(map.BeatOf(n.Time));
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
