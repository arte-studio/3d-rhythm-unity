using System;
using Arte.Rhythm.Charts;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>曲の周波数ごとの大きさ（演出の「周波数ごとに下から波」用）．</summary>
    public class SpectrumEnvelopeTests
    {
        const int Rate = 44100;

        // 前半 1 秒は無音，後半 1 秒は freq Hz の正弦波（ステレオ）
        static float[] SilenceThenTone(double freq)
        {
            int n = Rate * 2;
            var s = new float[n * 2];
            for (int i = Rate; i < n; i++)
            {
                float v = (float)(0.5 * Math.Sin(2 * Math.PI * freq * i / Rate));
                s[i * 2] = v;
                s[i * 2 + 1] = v;
            }
            return s;
        }

        static int BandOf(double freq)
        {
            for (int b = 0; b < SpectrumEnvelope.BandCount; b++)
            {
                var (lo, hi) = SpectrumEnvelope.BandRange(b);
                if (freq >= lo && freq < hi) return b;
            }
            return -1;
        }

        // 帯の真ん中（対数で）の周波数の音．境目の音は隣の帯にも漏れる（低い帯は FFT の分解能 21.5Hz に対して狭い）
        [TestCase(2)]
        [TestCase(8)]
        [TestCase(14)]
        public void ToneLightsItsBandMost(int band)
        {
            var (lo, hi) = SpectrumEnvelope.BandRange(band);
            double freq = Math.Sqrt(lo * hi);
            var env = SpectrumEnvelope.FromSamples(SilenceThenTone(freq), 2, Rate);
            var v = new float[SpectrumEnvelope.BandCount];
            env.At(1.5, v);
            int expected = BandOf(freq);
            Assert.AreEqual(band, expected);
            int max = 0;
            for (int b = 1; b < v.Length; b++)
                if (v[b] > v[max]) max = b;
            Assert.AreEqual(expected, max, $"{freq}Hz は帯 {expected} が一番大きいはず（{string.Join(", ", v)}）");
            Assert.Greater(v[expected], 0.9f);
        }

        [Test]
        public void BandsAreLogSpacedAndCoverRange()
        {
            Assert.AreEqual(SpectrumEnvelope.MinHz, SpectrumEnvelope.BandRange(0).lo, 1e-9);
            Assert.AreEqual(SpectrumEnvelope.MaxHz, SpectrumEnvelope.BandRange(SpectrumEnvelope.BandCount - 1).hi, 1e-6);
            var (lo0, hi0) = SpectrumEnvelope.BandRange(0);
            var (lo1, hi1) = SpectrumEnvelope.BandRange(1);
            Assert.AreEqual(hi0 / lo0, hi1 / lo1, 1e-9, "どの帯も同じ比");
        }

        [Test]
        public void ReleasesSlowlyAfterSoundStops()
        {
            // 1 秒鳴って止まる
            var s = SilenceThenTone(1000);
            Array.Reverse(s);
            var env = SpectrumEnvelope.FromSamples(s, 2, Rate);
            var v = new float[SpectrumEnvelope.BandCount];
            int b = BandOf(1000);
            env.At(1.0 + 0.05, v);
            float soon = v[b];
            env.At(1.0 + 0.6, v);
            Assert.Greater(soon, 0.3f, "止まってすぐはまだ残る");
            Assert.Less(v[b], 0.1f, "0.6 秒後には消える");
        }

        [Test]
        public void OutsideIsZero()
        {
            var env = SpectrumEnvelope.FromSamples(SilenceThenTone(1000), 2, Rate);
            var v = new float[SpectrumEnvelope.BandCount];
            env.At(-1, v);
            Assert.That(v, Is.All.EqualTo(0f));
            env.At(100, v);
            Assert.That(v, Is.All.EqualTo(0f));
        }
    }
}
