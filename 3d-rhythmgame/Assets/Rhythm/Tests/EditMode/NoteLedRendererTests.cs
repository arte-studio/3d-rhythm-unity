using System.Collections.Generic;
using System.IO;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>ノーツ用 LED の描画が旧版（GameManager / NoteLeds）の LED と同じになるか．</summary>
    public class NoteLedRendererTests
    {
        NoteLayout notes;
        PerfLayout perf;
        LedFrame frame;

        [SetUp]
        public void Load()
        {
            string dir = TestData.HardwareDir();
            var config = HardwareJson.LoadConfig(File.ReadAllText(Path.Combine(dir, "hardware.json")));
            var setup = HardwareJson.LoadSetup(File.ReadAllText(Path.Combine(dir, "setups", "kodaisai-1101.json")));
            notes = new NoteLayout(config, setup);
            perf = new PerfLayout(config, setup);
            frame = LedFrame.FromLayout(perf, notes, 8);
        }

        // レーン 4 = デバイス 3 のモジュール 1
        byte[] Mugu(int device, int module, int led = 0) => Pixel(device, notes.MuguLedIndex(module, 0, 0) + led);
        byte[] Con(int device, int module, int i) => Pixel(device, notes.ConnectLedIndex(module, i));

        byte[] Pixel(int device, int index)
        {
            var p = frame.Pin(device, notes.NoteOutputPin);
            return new[] { p[index * 3], p[index * 3 + 1], p[index * 3 + 2] };
        }

        static (Chart chart, TouchJudge judge) Make(params ChartNote[] n)
        {
            var chart = new Chart(n);
            return (chart, new TouchJudge(chart, new JudgeRules()));
        }

        static NoteLedStyle Blink() => new NoteLedStyle { approach = NoteApproach.Blink };

        // むぎゅっと（8×8）の行 r・列 c
        byte[] MuguAt(int device, int module, int r, int c) => Pixel(device, notes.MuguLedIndex(module, 0, 0) + r * notes.MuguColumns + c);

        [Test]
        public void BeatsApproachShrinksSquareAndRunsComet()
        {
            // テンポなし（120BPM，1 拍 0.5 秒）なので 3 拍前 = 1.5 秒前から
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes);
            r.Render(chart, judge, 8.49, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 0, 0), "3 拍前より前は消えている");
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 3, 3));

            // 3 拍前: 外周（角）が明るく，中心は薄く，テープは遠い端に粒
            r.Render(chart, judge, 8.5, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 0, 0)[2], 200, "外周の角");
            Assert.AreEqual(0, MuguAt(3, 1, 0, 0)[0]);
            Assert.That(MuguAt(3, 1, 3, 3)[2], Is.InRange(50, 80), "中心の 2×2 は薄く（25%）");
            Assert.Greater(Con(3, 1, notes.ConnectLeds - 1)[2], 200, "テープの遠い端");
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(3, 1, 0), "むぎゅっとの側の端はまだ");

            // ノーツ時刻の直前: 四角は中心に閉じ，最後の 1 拍は水色，粒はむぎゅっとの側の端に
            r.Render(chart, judge, 9.999, 0, 0.016f, false, frame);
            var center = MuguAt(3, 1, 3, 3);
            Assert.Greater(center[1], 80, "最後の 1 拍は水色（緑の成分がある）");
            Assert.Greater(center[2], 100);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 0, 0), "外周は消えた");
            // テープは R と G を入れ替えて書く
            Assert.Greater(Con(3, 1, 0)[0] + Con(3, 1, 0)[1], 100, "むぎゅっとの側の端に着いた");
        }

        [Test]
        public void BeatsApproachPulsesOnEachBeatCountedFromNote()
        {
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes);
            // 粒の頭（テープの何番目か = 29 × 残りの割合）の明るさで比べる．
            // 9.0 秒はノーツ時刻から 1 拍ずつ数えた拍の頭（あと 2 拍）なので一番明るい．9.3 秒は拍の途中なので暗い
            r.Render(chart, judge, 9.0, 0, 0.016f, false, frame);
            int onBeat = Con(3, 1, 19)[2];
            r.Render(chart, judge, 9.3, 0, 0.016f, false, frame);
            int midBeat = Con(3, 1, 14)[2];
            Assert.AreEqual(255, onBeat);
            Assert.That(midBeat, Is.InRange(130, 170), "拍の途中は 55〜60% くらいまで戻る");
        }

        [Test]
        public void CometFlowsFromThePreviousAdjacentModule()
        {
            // レーン 3 → レーン 4（隣）．レーン 4 の案内の光の粒は，レーン 3 のテープ（3 から 4 へ伸びる）を 3 の側から 4 の側へ流れる
            var (chart, judge) = Make(new ChartNote(9.5, 3, NoteType.Touch), new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes);
            notes.TryGetModule(3, out var m3);
            notes.TryGetModule(4, out var m4);
            int n = notes.ConnectLeds;
            var events = new List<JudgeEvent>();
            judge.Touch(3, 9.5, events);           // レーン 3 は取った（レーン 4 の案内だけが出る）
            r.Render(chart, judge, 8.6, 0, 0.016f, false, frame);   // あと 1.4 秒（ほぼ走り始め）
            int nearStart = Con(m3.device, m3.module, 1)[2], nearEnd = Con(m3.device, m3.module, n - 2)[2];
            Assert.Greater(nearStart, nearEnd, "走り始めはレーン 3 の側（0 番目の近く）");
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(m4.device, m4.module, n - 1), "レーン 4 のテープは使わない");
            r.Render(chart, judge, 9.999, 0, 0.016f, false, frame);
            Assert.Greater(Con(m3.device, m3.module, n - 1)[1] + Con(m3.device, m3.module, n - 1)[0], 100, "着くのはレーン 4 の側（テープの最後）");
        }

        [Test]
        public void BeatsApproachFollowsTempo()
        {
            // 60BPM（1 拍 1 秒）なら 3 秒前から
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes) { Tempo = new TempoMap(new[] { new TempoPoint { TimeSec = 0, Bpm = 60, BeatsPerBar = 4 } }) };
            r.Render(chart, judge, 6.9, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 0, 0));
            r.Render(chart, judge, 7.05, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 0, 0)[2], 100);
        }

        [Test]
        public void OpenNoteBlinksBlueFromSpawnLead()
        {
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, Blink());
            r.Render(chart, judge, 6.99, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Mugu(3, 1), "3 秒前より前は消えている");
            // ノーツ時刻にちょうど点く位相: 7.0 秒は 6 周期前なので点灯，7.3 秒は消灯
            r.Render(chart, judge, 7.0, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255 }, Mugu(3, 1));
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255 }, Mugu(3, 1, 63), "64 個全部");
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Mugu(3, 0), "ほかのレーンは消えたまま");
            r.Render(chart, judge, 7.3, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Mugu(3, 1));
            r.Render(chart, judge, 9.99, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Mugu(3, 1));
            r.Render(chart, judge, 10.0, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255 }, Mugu(3, 1));
        }

        [Test]
        public void PerfectShowsRainbowThenFadesGreen()
        {
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch), new ChartNote(20, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, Blink());
            var events = new List<JudgeEvent>();
            judge.Touch(4, 10.01, events);
            Assert.AreEqual(JudgeResult.Perfect, events[0].Result);
            r.OnJudged(events[0], 100.0);

            // 0.2 秒は虹色（LED ごとに色が違う）
            r.Render(chart, judge, 10.01, 100.0, 0.016f, false, frame);
            CollectionAssert.AreNotEqual(Mugu(3, 1, 0), Mugu(3, 1, 7));
            // そのあと緑が 0.5 秒で消える（0.25 秒で半分: 127.5 → 128）
            r.Render(chart, judge, 10.26, 100.25, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 128, 0 }, Mugu(3, 1));
            CollectionAssert.AreEqual(new byte[] { 0, 128, 0 }, Mugu(3, 1, 63));

            // 0.5 秒後: むぎゅっとは消え，光の先頭は 9 個目，強さは 1 − 0.5/3．緑 × 0.8333 = 212.5 → 212．R と G を入れ替えて書く
            r.Render(chart, judge, 10.5, 100.5, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Mugu(3, 1));
            CollectionAssert.AreEqual(new byte[] { 212, 0, 0 }, Con(3, 1, 9));
            Assert.Less(Con(3, 1, 0)[0], Con(3, 1, 5)[0], "先頭から離れるほど暗い");

            // 3 秒で光は消える．次のノーツの案内で青
            r.Render(chart, judge, 16.9, 103.0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(3, 1, 9));
            r.Render(chart, judge, 17.0, 103.1, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255 }, Mugu(3, 1));
        }

        [Test]
        public void GoodIsYellowAndMissIsRed()
        {
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch), new ChartNote(10, 9, NoteType.Touch));
            var r = new NoteLedRenderer(notes, Blink());
            var events = new List<JudgeEvent>();
            judge.Touch(4, 10.2, events);
            judge.Advance(10.3, events);
            Assert.AreEqual(JudgeResult.Good, events[0].Result);
            Assert.AreEqual(JudgeResult.Miss, events[1].Result);
            foreach (var e in events) r.OnJudged(e, 0);
            r.Render(chart, judge, 10.3, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 255, 255, 0 }, Mugu(3, 1));
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0 }, Mugu(7, 0), "レーン 9 = デバイス 7 のモジュール 0");
        }

        [Test]
        public void NextNoteOpeningWinsOverFade()
        {
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch), new ChartNote(12, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, Blink());
            var events = new List<JudgeEvent>();
            judge.Touch(4, 10.1, events);
            r.OnJudged(events[0], 0);
            r.Render(chart, judge, 10.0, 0.1, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255 }, Mugu(3, 1), "次のノーツ（12 秒）はもう案内中（10.0 秒は点灯の位相）");
        }

        [Test]
        public void ConnectSweepPaintsAllLanesByTime()
        {
            var r = new NoteLedRenderer(notes, Blink());
            // 0.1 秒: 色相 210/360 + 0.05 = 0.6333 → HSV(0.6333, 1, 1) = (0, 0.2, 1) → (0, 51, 255)．R と G を入れ替えて (51, 0, 255)
            // 60 個/秒なので 0.1 秒で 6 個
            r.Render(null, null, 0, 0, 0.1f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 51, 0, 255 }, Con(0, 0, 0));
            CollectionAssert.AreEqual(new byte[] { 51, 0, 255 }, Con(0, 0, 5));
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(0, 0, 6));
            CollectionAssert.AreEqual(new byte[] { 51, 0, 255 }, Con(3, 1, 0), "全レーン");
            CollectionAssert.AreEqual(new byte[] { 51, 0, 255 }, Con(7, 1, 5));

            // 曲が終わったら消える
            r.OnSongEnded();
            r.Render(null, null, 0, 0, 0.1f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(0, 0, 0));

            // 切ることもできる
            var off = new NoteLedRenderer(notes, new NoteLedStyle { connectSweep = false });
            off.Render(null, null, 0, 0, 0.1f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(0, 0, 0));
        }

        [Test]
        public void HsvMatchesUnity()
        {
            NoteLedRenderer.HsvToRgb(0f, 1f, 1f, out float r, out float g, out float b);
            Assert.AreEqual((1f, 0f, 0f), (r, g, b));
            NoteLedRenderer.HsvToRgb(0.5f, 1f, 1f, out r, out g, out b);
            Assert.AreEqual((0f, 1f, 1f), (r, g, b));
            NoteLedRenderer.HsvToRgb(0.25f, 0.5f, 0.8f, out r, out g, out b);
            Assert.AreEqual(0.6f, r, 1e-6);
            Assert.AreEqual(0.8f, g, 1e-6);
            Assert.AreEqual(0.4f, b, 1e-6);
        }

        [Test]
        public void UnwiredLanesAreIgnored()
        {
            // レーン 15 は kodaisai-1101 に無い
            var (chart, judge) = Make(new ChartNote(10, 15, NoteType.Touch));
            var r = new NoteLedRenderer(notes, Blink());
            r.Render(chart, judge, 9.0, 0, 0.016f, false, frame);
            for (int d = 0; d < 8; d++)
                foreach (byte v in frame.Pin(d, notes.NoteOutputPin).ToArray()) Assert.AreEqual(0, v);
        }
    }
}
