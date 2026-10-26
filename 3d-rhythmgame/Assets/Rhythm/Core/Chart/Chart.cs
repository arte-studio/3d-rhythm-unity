using System;
using System.Collections.Generic;
using System.Linq;

namespace Arte.Rhythm.Charts
{
    /// <summary>ノーツの種類．Connect は判定未実装（旧版も判定していない）で，将来の実装に備えて型だけ持つ．</summary>
    public enum NoteType
    {
        Touch,
        Connect,
    }

    /// <summary>
    /// 譜面上の1ノーツ．Time は曲頭からの秒．Lane は0始まり（設営プロファイルのレーン番号と同じ）．
    /// 譜面制作の画面では1始まりで見せてよい（<see cref="Chart.LaneLabel"/>）．
    /// </summary>
    public readonly struct ChartNote
    {
        public readonly double Time;
        public readonly int Lane;
        public readonly NoteType Type;

        public ChartNote(double time, int lane, NoteType type)
        {
            Time = time;
            Lane = lane;
            Type = type;
        }

        public override string ToString() => $"{Type} lane {Lane} @ {Time:F3}s";
    }

    /// <summary>
    /// 譜面．ノーツは時刻順に並べて持つ（同時刻は元の並び順を保つ）．
    /// ノーツの番号（インデックス）は判定結果やリプレイで使うので，読み込み後は変えない．
    /// </summary>
    public sealed class Chart
    {
        public IReadOnlyList<ChartNote> Notes { get; }

        /// <summary>読み込み時に見つかった問題（並び替えたなど）．エディタやツールで表示する．</summary>
        public IReadOnlyList<string> Warnings { get; }

        public Chart(IEnumerable<ChartNote> notes, IEnumerable<string> warnings = null)
        {
            var list = notes.ToList();
            var w = warnings?.ToList() ?? new List<string>();
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].Time < list[i - 1].Time)
                {
                    w.Add($"ノーツが時刻順に並んでいなかったので並び替えた（{i} 番目: {list[i]}）");
                    break;
                }
            }
            // OrderBy は安定ソートなので，同時刻のノーツは元の並び順のまま
            Notes = list.Select((n, i) => (n, i)).OrderBy(p => p.n.Time).ThenBy(p => p.i).Select(p => p.n).ToArray();
            Warnings = w;
            foreach (var n in Notes)
            {
                if (double.IsNaN(n.Time) || double.IsInfinity(n.Time)) throw new ArgumentException($"ノーツの時刻が不正: {n}");
                if (n.Lane < 0) throw new ArgumentException($"レーン番号が負: {n}");
            }
        }

        /// <summary>画面に出すレーン番号（1始まり）．内部の番号は常に0始まりで扱う．</summary>
        public static int LaneLabel(int lane) => lane + 1;

        /// <summary>使われているレーン番号（昇順）．</summary>
        public IEnumerable<int> Lanes => Notes.Select(n => n.Lane).Distinct().OrderBy(l => l);
    }
}
