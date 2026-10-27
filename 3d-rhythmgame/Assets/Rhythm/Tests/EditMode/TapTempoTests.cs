using System;
using System.Collections.Generic;
using Arte.Rhythm.Charts;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>叩いた拍からテンポと小節の頭を求める（譜面エディタの B / N キー）．</summary>
    public class TapTempoTests
    {
        // bpm・小節の頭 headSec の曲に合わせて，firstBeat 拍目から count 回叩いた（ばらつき jitter 秒）
        static List<TapTempo.Tap> Taps(double bpm, double headSec, int firstBeat, int count, double jitter, int seed, bool markDownbeats = true)
        {
            var rng = new Random(seed);
            var list = new List<TapTempo.Tap>();
            double p = 60 / bpm;
            for (int i = 0; i < count; i++)
            {
                int beat = firstBeat + i;
                double t = headSec + beat * p + (rng.NextDouble() * 2 - 1) * jitter;
                list.Add(new TapTempo.Tap(t, markDownbeats && beat % 4 == 0));
            }
            return list;
        }

        [Test]
        public void KeepsTempoAndMovesBarHeadByOneBeat()
        {
            // 本当の小節の頭は 2.154 秒（今の 1.759 秒より 1 拍後ろ）．±25ms でばらついて 24 回
            var current = new TempoPoint { TimeSec = 1.759, Bpm = 151.96, BeatsPerBar = 4 };
            var r = TapTempo.Fit(Taps(151.96, 2.154, 10, 24, 0.025, 1), current).Value;
            Assert.IsTrue(r.KeptTempo);
            Assert.AreEqual(151.96, r.Bpm, 1e-9);
            Assert.AreEqual(2.154, r.FirstBarSec, 0.015, "小節の頭は 1.759 に一番近いもの");
            Assert.AreEqual(0.395, r.ShiftSec, 0.015);
        }

        [Test]
        public void FitsTempoWhenFarFromCurrent()
        {
            var r = TapTempo.Fit(Taps(128, 0.5, 0, 32, 0.02, 2), null).Value;
            Assert.IsFalse(r.KeptTempo);
            Assert.AreEqual(128, r.Bpm, 0.3);
            Assert.AreEqual(0.5, r.FirstBarSec, 0.02);
        }

        [Test]
        public void WithoutDownbeatsKeepsCurrentBarPhase()
        {
            // 拍の位置だけ 20ms ずれていた（1 拍目は叩かない）→ 小節の頭は今のものの近くのまま
            var current = new TempoPoint { TimeSec = 1.759, Bpm = 151.96, BeatsPerBar = 4 };
            var r = TapTempo.Fit(Taps(151.96, 1.779, 5, 20, 0.0, 3, markDownbeats: false), current).Value;
            Assert.AreEqual(1.779, r.FirstBarSec, 0.002);
        }

        [Test]
        public void SkippedBeatIsCountedAsOneBeatGap()
        {
            var taps = Taps(151.96, 1.759, 0, 12, 0.0, 4);
            taps.RemoveAt(5);   // 1 回叩き損ねた
            var r = TapTempo.Fit(taps, null).Value;
            Assert.AreEqual(151.96, r.Bpm, 0.01);
            // 今のテンポが無いときは曲頭以降で最初の小節の頭（1.759 − 1 小節 = 0.180）
            Assert.AreEqual(1.759 - 4 * 60 / 151.96, r.FirstBarSec, 0.002);
        }

        [Test]
        public void WholeSongTapsWithFixedBaseFollowTheMusic()
        {
            // 曲は 151.96 BPM．1 曲分（200 拍）叩く．基準は叩き始める前のテンポのまま（譜面エディタと同じ使い方）
            var base0 = new TempoPoint { TimeSec = 1.759, Bpm = 151.96, BeatsPerBar = 4 };
            var all = Taps(151.96, 2.154, 4, 200, 0.03, 6);
            TapTempo.Result last = default;
            for (int n = TapTempo.MinTaps; n <= all.Count; n++) last = TapTempo.Fit(all.GetRange(0, n), base0).Value;
            Assert.IsTrue(last.KeptTempo);
            Assert.AreEqual(151.96, last.FittedBpm, 0.05, "叩いた拍だけでもほぼ同じテンポになる");
            Assert.AreEqual(2.154, last.FirstBarSec, 0.005);
        }

        [Test]
        public void SecondPassFromSongStartDoesNotBreakTempo()
        {
            // 1 周目の最後の続きに，曲の頭から 2 周目を叩いた（時刻が戻る）
            var base0 = new TempoPoint { TimeSec = 1.759, Bpm = 151.96, BeatsPerBar = 4 };
            var taps = Taps(151.96, 1.759, 180, 20, 0.02, 7);
            taps.AddRange(Taps(151.96, 1.759, 0, 20, 0.02, 8));
            var r = TapTempo.Fit(taps, base0).Value;
            Assert.AreEqual(151.96, r.Bpm, 1e-9);
            Assert.AreEqual(151.96, r.FittedBpm, 1.0);
            Assert.AreEqual(1.759, r.FirstBarSec, 0.02);
        }

        [Test]
        public void NeedsFourTaps()
        {
            Assert.IsNull(TapTempo.Fit(Taps(120, 0, 0, 3, 0, 5), null));
        }
    }
}
