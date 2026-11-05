using System.IO;
using System.Linq;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Gameplay;
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
            var song = SongJson.FromLegacy(legacy, "工大祭");
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

        [Test]
        public void SameLaneNotesLessThanThreeBeatsApartAreLeftOut()
        {
            var data = new SongData { Tempo = { new TempoPoint { TimeSec = 0, Bpm = 120 } } }; // 1 拍 0.5 秒
            foreach (var (t, lane) in new[] { (1.0, 0), (2.0, 0), (2.5, 0), (2.5, 1), (3.5, 0) })
                data.Notes.Add(new SongNote { Time = t, Lane = lane, Type = "touch" });
            CollectionAssert.AreEquivalent(new[] { (1.0, 0), (2.5, 0), (2.5, 1) },
                data.ToChart(3, CountIn.Hard).Notes.Select(n => (n.Time, n.Lane)), "2，1，はい: 2.0 は 0 番の 1.0 から 3 拍（1.5 秒）あかない．2.5 はちょうど 3 拍なので残る．3.5 は 2.5 から 2 拍");
            CollectionAssert.AreEquivalent(new[] { (1.0, 0), (3.5, 0) },
                data.ToChart(3).Notes.Select(n => (n.Time, n.Lane)), "3，2，1，はい（既定）: 4 拍あかないものを外す");
            data.Rules = new JudgeRules { SameLaneMinGapBeats = 0, SameFlowMinGapBeats = 0 };
            Assert.AreEqual(5, data.ToChart(3).Notes.Count, "0 なら外さない");
            Assert.AreEqual(5, data.ToChart().Notes.Count, "譜面そのものには残る");
        }

        [Test]
        public void SameFlowNotesLessThanThreeBeatsApartAreLeftOutButSimultaneousStay()
        {
            var data = new SongData { Tempo = { new TempoPoint { TimeSec = 0, Bpm = 120 } } }; // 1 拍 0.5 秒
            foreach (var (t, lane, flow) in new[] { (1.0, 0, 0), (1.0, 1, 0), (2.0, 2, 0), (2.5, 3, 0), (2.5, 4, 0), (3.0, 9, 0), (2.0, 7, 1) })
                data.Notes.Add(new SongNote { Time = t, Lane = lane, Type = "touch", Flow = flow });
            CollectionAssert.AreEquivalent(new[] { (1.0, 0), (1.0, 1), (2.5, 3), (2.5, 4), (2.0, 7) },
                data.ToChart(3, CountIn.Hard).Notes.Select(n => (n.Time, n.Lane)), "2，1，はい: 2.0 と 3.0 は流れ 0 の前から 3 拍あかない．同時の 2 つは残る．流れ 1 は別");
            CollectionAssert.AreEquivalent(new[] { (1.0, 0), (1.0, 1), (3.0, 9), (2.0, 7) },
                data.ToChart(3, CountIn.Normal).Notes.Select(n => (n.Time, n.Lane)), "3，2，1，はい: 2.5 は 3 拍しかあかない．3.0 はちょうど 4 拍");
        }

        [Test]
        public void KodaisaiChartNeverStartsTheCountdownLate([Values(1, 2, 3)] int players, [Values(CountIn.Normal, CountIn.Hard)] int count)
        {
            // 2026-11-05 ユーザー: 数え終わりをそろえる．前のノーツの はい のあと 1 拍あけて数え始める（同時は別）
            // 2026-11-04 ユーザー: 2 から始まるノーツは禁止．同じ流れの前のノーツから必ず 3 拍あく（同時は別）
            var song = SongJson.Load(File.ReadAllText(Path.Combine(TestData.SongsDir(), "kodaisai-2025", "song.json")));
            var tempo = song.TempoMap();
            double minGap = song.EffectiveRules.SameFlowMinGapBeats ?? count + 1;   // song.json の rules で変えられる
            foreach (var flow in song.ToChart(players, count).Notes.GroupBy(n => n.Flow))
            {
                var beats = flow.Select(n => tempo.BeatOf(n.Time)).ToList();
                for (int i = 1; i < beats.Count; i++)
                {
                    double gap = beats[i] - beats[i - 1];
                    Assert.That(gap < 1e-3 || gap > minGap - 1e-3, $"流れ {flow.Key} の {i} 番目: 前から {gap:F2} 拍");
                }
            }
        }

        [Test]
        public void TestChartPutsANoteOnEveryBarHeadAtOneModule()
        {
            var data = new SongData { Tempo = { new TempoPoint { TimeSec = 1, Bpm = 120, BeatsPerBar = 4 } } }; // 1 小節 2 秒
            var notes = data.TestChart(11, 10).Notes;
            CollectionAssert.AreEqual(new[] { 3.0, 5.0, 7.0, 9.0 }, notes.Select(n => n.Time), "2 小節目から，終わりの 1 秒前まで");
            Assert.That(notes.All(n => n.Lane == 11));
        }

        [Test]
        public void CountdownCuesAreThreeTwoOneAndHitOnBeatHeads()
        {
            var tempo = new TempoMap(new[] { new TempoPoint { TimeSec = 0, Bpm = 120 } }); // 1 拍 0.5 秒
            var chart = new Chart(new[] { new ChartNote(2.0, 0, NoteType.Touch), new ChartNote(2.0, 5, NoteType.Touch), new ChartNote(3.5, 1, NoteType.Touch) });
            var cues = CountdownCues.From(chart, tempo).Select(c => (c.Time, c.Count)).ToList();
            CollectionAssert.AreEqual(new[] { (0.5, 3), (1.0, 2), (1.5, 1), (2.0, 0), (2.0, 3), (2.5, 2), (3.0, 1), (3.5, 0) }, cues,
                "同時の 2 つは 1 回．3 拍あとのノーツの 3 は前の はい と一緒に鳴る");
        }

        [Test]
        public void KodaisaiCountdownAlwaysHasThreeTwoOneBeforeEachNote([Values(1, 2, 3)] int players, [Values(CountIn.Normal, CountIn.Hard)] int count)
        {
            // 2026-11-05 ユーザー: 通常プレイでは 3，2，はい になる．song.json の時刻が拍よりごくわずか後ろでも，1 は はい の 1 拍前
            var song = SongJson.Load(File.ReadAllText(Path.Combine(TestData.SongsDir(), "kodaisai-2025", "song.json")));
            var tempo = song.TempoMap();
            var cues = CountdownCues.From(song.ToChart(players, count), tempo, count);
            Assert.That(cues.All(c => c.Count <= count));
            foreach (var hit in cues.Where(c => c.Count == 0))
            {
                // 数え始めが前の はい と重ならない（1 人のとき．2〜3 人では別の流れの はい と重なることはある）
                Assert.That(players > 1 || (song.EffectiveRules.SameFlowMinGapBeats ?? count + 1) <= count || !cues.Any(c => c.Count == 0 && System.Math.Abs(c.Time - (hit.Time - count * 60 / 151.96)) < 2e-3), $"{hit.Time:F4} の数え始めが前の はい と重なる");
                for (int k = 1; k <= count; k++)
                {
                    double want = hit.Time - k * 60 / 151.96;
                    Assert.That(cues.Any(c => c.Count == k && System.Math.Abs(c.Time - want) < 2e-3), $"{hit.Time:F4} の {k} が {want:F4} に無い");
                }
            }
        }

        [Test]
        public void OnePlayerChartStartsAtTheFrontModule()
        {
            var data = new SongData();
            data.Notes.Add(new SongNote { Time = 1, Lane = 5, Type = "touch" });
            data.Notes.Add(new SongNote { Time = 3, Lane = 6, Type = "touch" });
            data.Notes.Add(new SongNote { Time = 5, Lane = 4, Type = "touch" });
            data.Notes.Add(new SongNote { Time = 7, Lane = 0, Type = "touch" });
            data.Notes.Add(new SongNote { Time = 1, Lane = 9, Type = "touch", Flow = 1 });
            int[] ring = { 0, 1, 2, 3, 4, 5, 6, 7 };
            var one = data.ToChart(1, ring, 7).Notes;
            CollectionAssert.AreEqual(new[] { 7, 0, 6, 2 }, one.Select(n => n.Lane), "最初が正面（7）．隣どうしの動きはそのまま輪に沿って回る");
            Assert.AreEqual(5, data.ToChart(2, ring, 7).Notes.Count, "2 人のときは回さない");
            Assert.AreEqual(5, data.ToChart(2, ring, 7).Notes[0].Lane);
        }
    }
}
