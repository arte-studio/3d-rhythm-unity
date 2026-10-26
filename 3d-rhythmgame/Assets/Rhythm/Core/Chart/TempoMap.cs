using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Arte.Rhythm.Charts
{
    /// <summary>テンポの変わり目．TimeSec 以降は Bpm・BeatsPerBar で進む．</summary>
    public sealed class TempoPoint
    {
        [JsonProperty("t")] public double TimeSec;
        [JsonProperty("bpm")] public double Bpm = 120;
        [JsonProperty("beatsPerBar")] public int BeatsPerBar = 4;
    }

    /// <summary>
    /// 秒と拍の変換．譜面のノーツ時刻は秒で持ち，編集画面のグリッド・スナップ・小節表示にだけ拍を使う．
    /// （秒で持つのは，テンポを測り直してもノーツが音とずれないようにするため．）
    ///
    /// 最初の点より前は最初の点のテンポで延長する．小節番号は最初の点を 1 小節目の頭とする．
    /// </summary>
    public sealed class TempoMap
    {
        readonly TempoPoint[] points;
        readonly double[] beatAt; // 各点の位置（最初の点からの拍数）
        readonly int[] barAt;     // 各点の位置（最初の点からの小節数．拍子の変わり目は小節の頭にあるとみなす）

        public IReadOnlyList<TempoPoint> Points => points;

        public TempoMap(IEnumerable<TempoPoint> tempo)
        {
            points = (tempo ?? Enumerable.Empty<TempoPoint>()).OrderBy(p => p.TimeSec).ToArray();
            if (points.Length == 0) points = new[] { new TempoPoint() };
            foreach (var p in points)
            {
                if (!(p.Bpm > 0)) throw new ArgumentException($"BPM が正でない: {p.Bpm}（{p.TimeSec}s）");
                if (p.BeatsPerBar < 1) throw new ArgumentException($"拍子が不正: {p.BeatsPerBar}（{p.TimeSec}s）");
            }
            beatAt = new double[points.Length];
            barAt = new int[points.Length];
            for (int i = 1; i < points.Length; i++)
            {
                double beats = (points[i].TimeSec - points[i - 1].TimeSec) * points[i - 1].Bpm / 60.0;
                beatAt[i] = beatAt[i - 1] + beats;
                barAt[i] = barAt[i - 1] + (int)Math.Ceiling(beats / points[i - 1].BeatsPerBar - 1e-6);
            }
        }

        int SegmentOfTime(double sec)
        {
            int i = points.Length - 1;
            while (i > 0 && sec < points[i].TimeSec) i--;
            return i;
        }

        int SegmentOfBeat(double beat)
        {
            int i = points.Length - 1;
            while (i > 0 && beat < beatAt[i]) i--;
            return i;
        }

        /// <summary>秒 → 拍（最初の点を 0 拍とする）．</summary>
        public double BeatOf(double sec)
        {
            int i = SegmentOfTime(sec);
            return beatAt[i] + (sec - points[i].TimeSec) * points[i].Bpm / 60.0;
        }

        /// <summary>拍 → 秒．</summary>
        public double TimeOf(double beat)
        {
            int i = SegmentOfBeat(beat);
            return points[i].TimeSec + (beat - beatAt[i]) * 60.0 / points[i].Bpm;
        }

        /// <summary>1拍を division 等分したグリッドのうち，最も近い点の秒を返す．</summary>
        public double Snap(double sec, int division)
        {
            if (division < 1) throw new ArgumentOutOfRangeException(nameof(division));
            int i = SegmentOfTime(sec);
            double local = (sec - points[i].TimeSec) * points[i].Bpm / 60.0 * division;
            double snapped = points[i].TimeSec + Math.Round(local) / division * 60.0 / points[i].Bpm;
            // 次のテンポの区間にはみ出したら，その区間の頭（グリッド上の点）にそろえる
            if (i + 1 < points.Length && snapped > points[i + 1].TimeSec) snapped = points[i + 1].TimeSec;
            return snapped;
        }

        /// <summary>画面表示用の「小節.拍」（どちらも1始まり）．</summary>
        public (int bar, double beat) BarBeatOf(double sec)
        {
            int i = SegmentOfTime(sec);
            double local = (sec - points[i].TimeSec) * points[i].Bpm / 60.0;
            int bpb = points[i].BeatsPerBar;
            int bar = (int)Math.Floor(local / bpb);
            return (barAt[i] + bar + 1, local - bar * bpb + 1);
        }

        /// <summary>[fromSec, toSec] にある拍（division 等分）の線を列挙する．isBarHead は小節の頭．</summary>
        public IEnumerable<(double sec, bool isBeat, bool isBarHead)> GridLines(double fromSec, double toSec, int division)
        {
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                double segStart = i == 0 ? double.NegativeInfinity : p.TimeSec;
                double segEnd = i + 1 < points.Length ? points[i + 1].TimeSec : double.PositiveInfinity;
                double a = Math.Max(fromSec, segStart), b = Math.Min(toSec, segEnd);
                if (a > b) continue;
                double step = 60.0 / p.Bpm / division;
                long k0 = (long)Math.Ceiling((a - p.TimeSec) / step - 1e-9);
                for (long k = k0; ; k++)
                {
                    double sec = p.TimeSec + k * step;
                    if (sec > b + 1e-9 || (i + 1 < points.Length && sec >= segEnd - 1e-9)) break;
                    bool isBeat = k % division == 0;
                    long beatIndex = k / division - (k < 0 && k % division != 0 ? 1 : 0);
                    bool isBarHead = isBeat && ((beatIndex % p.BeatsPerBar) + p.BeatsPerBar) % p.BeatsPerBar == 0;
                    yield return (sec, isBeat, isBarHead);
                }
            }
        }
    }
}
