using System;
using System.Collections.Generic;
using System.Linq;

namespace Arte.Rhythm.Charts
{
    /// <summary>
    /// 曲に合わせて叩いた拍から，テンポと小節の頭を求める（譜面エディタの B / N キー）．
    ///
    /// - 叩いた時刻に拍番号を振り（間隔のおおよそから），拍番号に対する時刻の最小二乗直線で拍の間隔と位置を出す．
    ///   人の叩くばらつき（1 回 20〜30ms）は回数が増えるほど打ち消し合う
    /// - 今のテンポとの差が 1% 以内なら，テンポは今の値のまま（解析などで正確に分かっているため）拍の位置だけ合わせる．
    ///   current には叩き始める前のテンポを渡す（叩くたびに結果で置き換えると，最初の数回のテンポから動かなくなる）
    /// - 1 拍目として叩いた（downbeat）拍の拍番号を拍子で割った余りの多数決で，どの拍が小節の頭かを決める．
    ///   1 拍目を叩いていなければ，今の小節の頭に一番近いものにする
    /// </summary>
    public static class TapTempo
    {
        public readonly struct Tap
        {
            public readonly double Time;
            public readonly bool Downbeat;

            public Tap(double time, bool downbeat)
            {
                Time = time;
                Downbeat = downbeat;
            }
        }

        public readonly struct Result
        {
            public readonly double Bpm;
            /// <summary>小節の頭（TempoPoint.TimeSec に入れる）．今のテンポがあればその小節の頭に一番近いもの．</summary>
            public readonly double FirstBarSec;
            /// <summary>今のテンポをそのまま使った．</summary>
            public readonly bool KeptTempo;
            /// <summary>小節の頭を今からどれだけ動かしたか（秒．−小節の半分〜＋小節の半分）．</summary>
            public readonly double ShiftSec;
            /// <summary>叩いた時刻と，求めた拍の差の大きさ（秒，二乗平均）．</summary>
            public readonly double ErrorSec;
            /// <summary>叩いた拍だけで求めたテンポ（KeptTempo でも参考に出す）．</summary>
            public readonly double FittedBpm;

            public Result(double bpm, double firstBarSec, bool keptTempo, double shiftSec, double errorSec, double fittedBpm)
            {
                FittedBpm = fittedBpm;
                Bpm = bpm;
                FirstBarSec = firstBarSec;
                KeptTempo = keptTempo;
                ShiftSec = shiftSec;
                ErrorSec = errorSec;
            }
        }

        public const int MinTaps = 4;

        /// <summary>current は今のテンポ（無ければ null）．求められなければ null．</summary>
        public static Result? Fit(IReadOnlyList<Tap> taps, TempoPoint current, int beatsPerBar = 4)
        {
            // 時刻が戻っていたら（曲の頭からもう一度叩いた），戻ったあとの分だけを使う
            int from = 0;
            for (int i = 1; i < taps.Count; i++)
                if (taps[i].Time <= taps[i - 1].Time) from = i;
            if (from > 0) taps = taps.Skip(from).ToList();
            if (taps.Count < MinTaps) return null;
            var t = taps.Select(x => x.Time).ToArray();
            // 拍の間隔のおおよそ: 隣どうしの間隔の中央値（叩き損ねて 1 拍飛んでも引きずられにくい）
            var diffs = t.Zip(t.Skip(1), (a, b) => b - a).Where(d => d > 0).OrderBy(d => d).ToArray();
            if (diffs.Length == 0) return null;
            double period = diffs[diffs.Length / 2];
            if (current != null && Math.Abs(60 / current.Bpm - period) / period < 0.25) period = 60 / current.Bpm;
            if (period < 0.15 || period > 2) return null;

            var k = new int[t.Length];
            for (int i = 1; i < t.Length; i++) k[i] = k[i - 1] + Math.Max(1, (int)Math.Round((t[i] - t[i - 1]) / period));

            // 最小二乗: t = a + p k
            double mk = k.Average(), mt = t.Average(), skt = 0, skk = 0;
            for (int i = 0; i < t.Length; i++)
            {
                skt += (k[i] - mk) * (t[i] - mt);
                skk += (k[i] - mk) * (k[i] - mk);
            }
            if (skk <= 0) return null;
            double p = skt / skk;
            double fitted = 60 / p;
            bool kept = false;
            if (current != null && Math.Abs(60 / p - current.Bpm) / current.Bpm < 0.01)
            {
                p = 60 / current.Bpm;
                kept = true;
            }
            double a = mt - p * mk;
            double err = Math.Sqrt(Enumerable.Range(0, t.Length).Select(i => Math.Pow(t[i] - (a + p * k[i]), 2)).Average());

            double bar = p * beatsPerBar;
            double head;
            var down = Enumerable.Range(0, t.Length).Where(i => taps[i].Downbeat).ToArray();
            if (down.Length > 0)
            {
                int m = down.GroupBy(i => ((k[i] % beatsPerBar) + beatsPerBar) % beatsPerBar).OrderByDescending(g => g.Count()).First().Key;
                head = a + p * m;
            }
            else
            {
                // 1 拍目を叩いていなければ，今の小節の頭に一番近い拍を小節の頭にする
                double old = current?.TimeSec ?? a;
                head = a + p * Math.Round((old - a) / p);
            }
            // 小節の番号が大きく変わらないよう，今の小節の頭に一番近いものにする（テンポが無ければ曲頭以降で最初のもの）
            double first = current != null ? head + Math.Round((current.TimeSec - head) / bar) * bar : head - Math.Floor(head / bar) * bar;
            double shift = current != null ? first - current.TimeSec : 0;
            return new Result(60 / p, first, kept, shift, err, fitted);
        }
    }
}
