using System;
using System.Collections.Generic;
using System.Linq;
using Arte.Rhythm.Gameplay;
using Newtonsoft.Json;

namespace Arte.Rhythm.Charts
{
    /// <summary>
    /// 遊ぶ譜面の作り方の決まり（song.json の "chartRules"．書かなければ既定）．判定の決まり（JudgeRules）とは別．
    /// </summary>
    public sealed class ChartRules
    {
        /// <summary>
        /// 同じむぎゅっとの前のノーツからこれだけ（拍）あかないタッチノーツは遊ぶ譜面から外す．0 で外さない．
        /// 無し（既定）なら 数える拍 + 1（3，2，1，はい なら 4 拍．前のノーツの はい と次のノーツの数え始めが重ならない．2026-11-05 ユーザー）．
        /// </summary>
        [JsonProperty("sameLaneMinGapBeats", NullValueHandling = NullValueHandling.Ignore)] public double? SameLaneMinGapBeats;

        /// <summary>同じ流れの前のノーツからこれだけ（拍）あかないタッチノーツも外す（同時のものは残す）．0 で外さない．無しなら 数える拍 + 1．</summary>
        [JsonProperty("sameFlowMinGapBeats", NullValueHandling = NullValueHandling.Ignore)] public double? SameFlowMinGapBeats;

        public double LaneGap(int countBeats) => SameLaneMinGapBeats ?? countBeats + 1;
        public double FlowGap(int countBeats) => SameFlowMinGapBeats ?? countBeats + 1;
    }

    /// <summary>
    /// 曲の譜面（song.json）から，遊ぶ譜面を作る．song.json のノーツはそのまま残し，遊ぶときだけ選ぶ．
    /// </summary>
    public static class PlayChart
    {
        /// <summary>
        /// 遊ぶ人数（1〜3）と数え方（CountIn）に合わせた譜面．
        /// - 流れ 0〜players−1 のノーツだけ（2026-11-03 ユーザー決定）
        /// - 同じむぎゅっと・同じ流れの前のノーツから ChartRules の拍数あかないタッチノーツは外す（同時のノーツはそのまま．
        ///   2026-11-03〜05 ユーザー決定．数え終わりの 1，はい をそろえる）
        /// - 1 人のときは，最初のノーツが正面のむぎゅっと（frontLane）に来るよう，流れ全体をむぎゅっとの輪（ring．並び順）に沿って回す
        ///   （隣どうしの動きはそのまま．2026-11-03 ユーザー: 1 人のとき，最初のノーツは正面にいてほしい）．ring が無ければ回さない
        /// </summary>
        public static Chart Build(SongData song, int players, int countBeats = CountIn.Normal, IReadOnlyList<int> ring = null, int frontLane = -1)
        {
            var chart = Filter(song, players, countBeats);
            return players > 1 || ring == null ? chart : RotateToFront(chart, ring, frontLane);
        }

        static Chart Filter(SongData song, int players, int countBeats)
        {
            var rules = song.EffectiveChartRules;
            double minGap = rules.LaneGap(countBeats), minFlowGap = rules.FlowGap(countBeats);
            var tempo = song.TempoMap();
            var last = new Dictionary<int, double>();
            var lastInFlow = new Dictionary<int, double>();
            return new Chart(song.ToChart().Notes.Where(n =>
            {
                if (n.Flow >= Math.Max(1, players)) return false;
                if (n.Type != NoteType.Touch) return true;
                double beat = tempo.BeatOf(n.Time);
                if (minGap > 0 && last.TryGetValue(n.Lane, out double prev) && beat - prev < minGap - 1e-3) return false;
                if (minFlowGap > 0 && lastInFlow.TryGetValue(n.Flow, out double prevFlow)
                    && beat - prevFlow > 1e-3 && beat - prevFlow < minFlowGap - 1e-3) return false;
                last[n.Lane] = beat;
                lastInFlow[n.Flow] = beat;
                return true;
            }));
        }

        static Chart RotateToFront(Chart chart, IReadOnlyList<int> ring, int frontLane)
        {
            if (chart.Notes.Count == 0) return chart;
            var order = ring.ToList();
            int from = order.IndexOf(chart.Notes[0].Lane), to = order.IndexOf(frontLane);
            if (from < 0 || to < 0 || from == to) return chart;
            int shift = to - from, n = order.Count;
            return new Chart(chart.Notes.Select(x =>
            {
                int i = order.IndexOf(x.Lane);
                return i < 0 ? x : new ChartNote(x.Time, order[((i + shift) % n + n) % n], x.Type, x.Flow);
            }));
        }

        /// <summary>
        /// テストプレイの譜面: むぎゅっと lane に，2 小節目から曲の終わり（untilSec）まで，小節の頭ごと（4 拍おき）にノーツを出し続ける．
        /// 3，2，1，はい のリズムを確かめる用（2026-11-04 ユーザー）．
        /// </summary>
        public static Chart Test(SongData song, int lane, double untilSec)
        {
            var tempo = song.TempoMap();
            var notes = new List<ChartNote>();
            for (int bar = 2; ; bar++)
            {
                double t = tempo.TimeOfBarBeat(bar, 1);
                if (t > untilSec - 1 || notes.Count > 10000) break;
                notes.Add(new ChartNote(t, lane, NoteType.Touch));
            }
            return new Chart(notes);
        }
    }
}
