using System.IO;
using System.Linq;
using Arte.Rhythm.Charts;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    public class SongDataTests
    {
        static TempoMap TwoTempos() => new TempoMap(new[]
        {
            new TempoPoint { TimeSec = 1.0, Bpm = 120, BeatsPerBar = 4 },  // 1拍 0.5秒
            new TempoPoint { TimeSec = 5.0, Bpm = 60, BeatsPerBar = 3 },   // 8拍後（2小節後）から 1拍 1秒
        });

        [Test]
        public void TempoMap_BeatAndTimeRoundTrip()
        {
            var map = TwoTempos();
            Assert.That(map.BeatOf(1.0), Is.EqualTo(0).Within(1e-9));
            Assert.That(map.BeatOf(3.0), Is.EqualTo(4).Within(1e-9));
            Assert.That(map.BeatOf(7.0), Is.EqualTo(10).Within(1e-9));
            Assert.That(map.BeatOf(0.0), Is.EqualTo(-2).Within(1e-9), "最初の点より前は延長");
            foreach (double t in new[] { -1.0, 0.3, 2.2, 5.0, 9.7 })
                Assert.That(map.TimeOf(map.BeatOf(t)), Is.EqualTo(t).Within(1e-9));
        }

        [Test]
        public void TempoMap_SnapAndBarBeat()
        {
            var map = TwoTempos();
            Assert.That(map.Snap(1.13, 4), Is.EqualTo(1.125).Within(1e-9), "16分（0.125秒）グリッド");
            Assert.That(map.Snap(5.4, 1), Is.EqualTo(5.0).Within(1e-9));
            Assert.That(map.Snap(4.99, 1), Is.EqualTo(5.0).Within(1e-9));
            Assert.That(map.BarBeatOf(1.0), Is.EqualTo((1, 1.0)));
            Assert.That(map.BarBeatOf(3.5), Is.EqualTo((2, 2.0)));
            Assert.That(map.BarBeatOf(5.0), Is.EqualTo((3, 1.0)), "テンポの変わり目は新しい小節");
            Assert.That(map.BarBeatOf(8.0), Is.EqualTo((4, 1.0)), "3拍子");
        }

        [Test]
        public void TempoMap_GridLines()
        {
            var lines = TwoTempos().GridLines(4.0, 7.0, 1).ToList();
            Assert.That(lines.Select(l => l.sec), Is.EqualTo(new[] { 4.0, 4.5, 5.0, 6.0, 7.0 }));
            Assert.That(lines.Select(l => l.isBarHead), Is.EqualTo(new[] { false, false, true, false, false }));
        }

        [Test]
        public void SongJson_RoundTripsLegacyChart()
        {
            string legacy = File.ReadAllText(Path.Combine(TestData.LegacyResourcesDir(), "Kodaisai_NoteData_1101.json"));
            var song = SongJson.FromLegacy(legacy, "古代祭");
            string json = SongJson.Save(song);
            var loaded = SongJson.Load(json);
            var a = LegacyChartJson.Parse(legacy).Notes;
            var b = loaded.ToChart().Notes;
            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Time, Is.EqualTo(a[i].Time).Within(1e-9));
                Assert.That(b[i].Lane, Is.EqualTo(a[i].Lane));
            }
            Assert.That(json, Does.Contain("\n    {\"t\":2.26,\"lane\":2,\"type\":\"touch\"},\n"), "ノーツは1行1個");
            Assert.That(SongJson.Save(loaded), Is.EqualTo(json), "保存し直しても変わらない");
        }

        [Test]
        public void TimeOfBarBeat_IsInverseOfBarBeatOf()
        {
            var map = new TempoMap(new[]
            {
                new TempoPoint { TimeSec = 1.759, Bpm = 151.96, BeatsPerBar = 4 },
                new TempoPoint { TimeSec = 40.0, Bpm = 120, BeatsPerBar = 3 },
            });
            foreach (double t in new[] { 0.3, 1.759, 10.0, 39.9, 40.0, 41.234, 80.0 })
            {
                var (bar, beat) = map.BarBeatOf(t);
                Assert.AreEqual(t, map.TimeOfBarBeat(bar, beat), 1e-9, $"{t} 秒 = {bar} 小節 {beat} 拍");
            }
            Assert.AreEqual(1.759, map.TimeOfBarBeat(1, 1), 1e-9);
        }

        [Test]
        public void Retime_KeepsBarAndBeat()
        {
            var oldMap = new TempoMap(new[] { new TempoPoint { TimeSec = 1.759, Bpm = 151.96, BeatsPerBar = 4 } });
            var newMap = new TempoMap(new[] { new TempoPoint { TimeSec = 2.154, Bpm = 152.5, BeatsPerBar = 4 } });
            double t = oldMap.TimeOfBarBeat(12, 3);
            double moved = TempoMap.Retime(t, oldMap, newMap);
            var (bar, beat) = newMap.BarBeatOf(moved);
            Assert.AreEqual(12, bar);
            Assert.AreEqual(3, beat, 1e-9);
            Assert.IsTrue(oldMap.SameAs(new TempoMap(new[] { new TempoPoint { TimeSec = 1.759, Bpm = 151.96, BeatsPerBar = 4 } })));
            Assert.IsFalse(oldMap.SameAs(newMap));
        }

        [Test]
        public void LoudnessEnvelope_FollowsVolume()
        {
            // 1 秒の静かな音のあと 1 秒の大きな音，ステレオ 8000Hz
            int rate = 8000;
            var x = new float[rate * 2 * 2];
            for (int f = 0; f < rate * 2; f++)
            {
                float amp = f < rate ? 0.01f : 0.5f;
                float v = amp * (float)System.Math.Sin(2 * System.Math.PI * 440 * f / rate);
                x[f * 2] = v;
                x[f * 2 + 1] = v;
            }
            var env = LoudnessEnvelope.FromSamples(x, 2, rate);
            Assert.AreEqual(2.0, env.LengthSec, 0.05);
            Assert.Less(env.At(0.5), 0.1);
            Assert.Greater(env.At(1.5), 0.9);
            Assert.AreEqual(0, env.At(-1));
            // 曲時刻 0 を音声の 1 秒目にすると，0.5 秒はもう大きい
            Assert.Greater(LoudnessEnvelope.FromSamples(x, 2, rate, 1.0).At(0.5), 0.9);
        }

        [Test]
        public void Sections_RoundTripAndLookup()
        {
            var song = new SongData();
            song.Sections.Add(new SongSection { Time = 0, Name = "前奏" });
            song.Sections.Add(new SongSection { Time = 14.39, Name = "Aメロ" });
            var back = SongJson.Load(SongJson.Save(song));
            Assert.AreEqual(2, back.Sections.Count);
            Assert.AreEqual("Aメロ", back.Sections[1].Name);
            Assert.AreEqual("前奏", back.SectionAt(14.0));
            Assert.AreEqual("Aメロ", back.SectionAt(14.39));
            Assert.AreEqual("", new SongData().SectionAt(3));
        }

        [Test]
        public void KodaisaiSongHasTempoAndSections()
        {
            var song = SongJson.Load(System.IO.File.ReadAllText(System.IO.Path.Combine(TestData.SongsDir(), "kodaisai-2025", "song.json")));
            Assert.AreEqual(151.96, song.Tempo[0].Bpm, 0.01);
            Assert.AreEqual("前奏", song.SectionAt(1));
            Assert.AreEqual("最後", song.SectionAt(85));
        }

        [Test]
        public void SongJson_RejectsUnknownKeys()
        {
            Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() =>
                SongJson.Load("{\"format\":\"arte-rhythm-song/1\",\"bmp\":120}"));
        }
    }
}
