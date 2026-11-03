using System.Linq;
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

        static NoteLedStyle Blink() => new NoteLedStyle { approach = NoteApproach.Blink, connectSweep = true };   // 旧版の見え方

        // むぎゅっと（8×8）の行 r・列 c
        byte[] MuguAt(int device, int module, int r, int c) => Pixel(device, notes.MuguLedIndex(module, 0, 0) + r * notes.MuguColumns + c);

        [Test]
        public void BeatsApproachShrinksSquareAndRunsComet()
        {
            // テンポなし（120BPM，1 拍 0.5 秒）なので 3 拍前 = 1.5 秒前から
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, new NoteLedStyle { approachSquare = true });
            r.Render(chart, judge, 8.49, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 0, 0), "3 拍前より前は消えている");
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 3, 3));

            // 3 拍前: 外周（角）が明るく，中心は薄く，テープは遠い端に粒
            r.Render(chart, judge, 8.5, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 0, 0)[2], 200, "外周の角");
            Assert.AreEqual(170, MuguAt(3, 1, 0, 0)[0], "3 拍前は紫");
            Assert.That(MuguAt(3, 1, 3, 3)[2], Is.InRange(50, 80), "中心の 2×2 は薄く（25%）");
            Assert.Greater(Enumerable.Range(0, notes.ConnectLeds).Max(i => Con(3, 1, i)[2]), 200, "テープには道しるべ");

            // ノーツ時刻の直前: 四角は中心に閉じ，最後の 1 拍は水色，粒はむぎゅっとの側の端に
            r.Render(chart, judge, 9.999, 0, 0.016f, false, frame);
            var center = MuguAt(3, 1, 2, 2);
            Assert.Greater(center[1], 80, "最後の 1 拍は内側の段（中心から 1.5）で水色（緑の成分がある）");
            Assert.Greater(center[2], 100);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 0, 0), "外周は消えた");
        }

        // テープ（レーン lane のむぎゅっとから次へ伸びる）の一番明るい LED の番号（0 = lane の側）．消えていれば −1
        int Peak(int lane)
        {
            notes.TryGetModule(lane, out var m);
            int best = -1, max = 0;
            for (int i = 0; i < notes.ConnectLeds; i++)
                if (Con(m.device, m.module, i)[2] > max) { max = Con(m.device, m.module, i)[2]; best = i; }
            return best;
        }

        [Test]
        public void GuideFlowsIntoTheNextNoteFromBothSidesFromJustBeforeThePreviousNote()
        {
            // 120BPM（1 拍 0.5 秒）．流れ: レーン 3（9.5 秒）→ レーン 4（10 秒）
            var (chart, judge) = Make(new ChartNote(9.5, 3, NoteType.Touch), new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes);
            // 8.9 秒: まだレーン 3 への道しるべだけ（テープ 2 とテープ 3 からレーン 3 へ）
            r.Render(chart, judge, 8.9, 0, 0.016f, false, frame);
            Assert.GreaterOrEqual(Peak(2), 0, "テープ 2（2 → 3）");
            Assert.That(Peak(3), Is.InRange(0, 14), "テープ 3 はレーン 3 の側へ流れている");
            Assert.AreEqual(-1, Peak(4));
            // 9.1 秒（前のノーツの 1 拍前を過ぎた）: レーン 4 への道しるべも始まる（レーン 3 への道しるべも判定まで続く）
            r.Render(chart, judge, 9.1, 0, 0.016f, false, frame);
            Assert.GreaterOrEqual(Peak(2), 0, "レーン 3 への道しるべは判定まで続く");
            Assert.GreaterOrEqual(Peak(4), 0, "テープ 4（5 の側から 4 へ）");
            // レーン 3 を取ったら，テープ 3 と テープ 4 の両側からレーン 4 へ
            var events = new List<JudgeEvent>();
            judge.Touch(3, 9.5, events);
            r.Render(chart, judge, 9.6, 0, 0.016f, false, frame);
            Assert.AreEqual(-1, Peak(2), "レーン 3 への道しるべは止まった");
            int a3 = Peak(3), a4 = Peak(4);
            r.Render(chart, judge, 9.7, 0, 0.016f, false, frame);
            Assert.Greater(Peak(3), a3, "テープ 3 は 3 の側からレーン 4 の側（最後の LED）へ");
            Assert.Less(Peak(4), a4, "テープ 4 は遠い端（5 の側）からレーン 4 の側（0 番目）へ");
            // 流れ続ける（1 拍ごとに繰り返す．9.7 秒の 1 拍前も同じ所）
            int b4 = Peak(4);
            r.Render(chart, judge, 9.2, 0, 0.016f, false, frame);
            Assert.AreEqual(b4, Peak(4), "1 拍ごとに繰り返す");
            // 判定したら止まる
            judge.Touch(4, 10, events);
            r.Render(chart, judge, 10.3, 0, 0.016f, false, frame);
            Assert.AreEqual(-1, Peak(3));
            Assert.AreEqual(-1, Peak(4));
        }

        [Test]
        public void ApproachFillBrightensTowardNoteWithoutSquare()
        {
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, new NoteLedStyle { approachSquare = false });
            r.Render(chart, judge, 8.5, 0, 0.016f, false, frame);
            int start = Mugu(3, 1, 0)[2];
            CollectionAssert.AreEqual(Mugu(3, 1, 0), Mugu(3, 1, 27), "むぎゅっと全体が同じ色");
            r.Render(chart, judge, 9.5, 0, 0.016f, false, frame);  // あと 1 拍（拍の頭）
            Assert.Greater(Mugu(3, 1, 0)[1] + Mugu(3, 1, 0)[2], start, "ノーツ時刻に近いほど明るい（最後の 1 拍は水色）");
        }

        [Test]
        public void CometUsesOnlyTheSameFlowAndWrapsAroundTheRing()
        {
            // 流れ 1: レーン 14 → レーン 0（輪で隣）．流れ 0 のレーン 1 は同じ時刻の前にあっても使わない
            int last = 0;
            foreach (int l in notes.Lanes) last = System.Math.Max(last, l);
            var (chart, judge) = Make(new ChartNote(9.5, 1, NoteType.Touch, 0), new ChartNote(9.5, last, NoteType.Touch, 1),
                                      new ChartNote(10, 0, NoteType.Touch, 1));
            var r = new NoteLedRenderer(notes);
            var events = new List<JudgeEvent>();
            judge.Touch(1, 9.5, events);
            judge.Touch(last, 9.5, events);
            notes.TryGetModule(last, out var mLast);
            int n = notes.ConnectLeds;
            r.Render(chart, judge, 9.05, 0, 0.016f, false, frame);
            Assert.Greater(Con(mLast.device, mLast.module, 1)[2], Con(mLast.device, mLast.module, n - 2)[2],
                "レーン 0 の案内は，最後のレーンのテープ（最後 → 0）を最後のレーンの側から流れる");
        }

        [Test]
        public void ShortGapStartsTwoBeatsBeforeAndNoteTimeLightsTheWholeModule()
        {
            // 120BPM（1 拍 0.5 秒）．前のノーツ（同じ流れ）から 1 秒（2 拍．3 拍より短い）なので，2 拍前（9.0 秒）から案内する
            var (chart, judge) = Make(new ChartNote(9, 3, NoteType.Touch), new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, new NoteLedStyle { approachSquare = true });
            var events = new List<JudgeEvent>();
            judge.Touch(3, 9, events);
            r.Render(chart, judge, 8.9, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 0, 0), "2 拍より前はまだ");
            r.Render(chart, judge, 9.05, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 1, 1)[2], 100, "2 拍前から四角（真ん中の段）");
            // ノーツ時刻ちょうどは，むぎゅっと全体（四角の外の角も）が最後の 1 拍の色で光る
            r.Render(chart, judge, 10.0, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 0, 0)[1], 150, "ノーツ時刻はむぎゅっと全体が光る");
            r.Render(chart, judge, 10.2, 0, 0.016f, false, frame);
            Assert.Less(MuguAt(3, 1, 0, 0)[1], 50, "すぐ戻る");
        }

        [Test]
        public void EachBeatHasItsOwnColorAndTheCometKeepsItsSpeedWhenTheGapIsShort()
        {
            // 120BPM（1 拍 0.5 秒）．3 拍前は紫，2 拍前は青，最後の 1 拍は水色
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, new NoteLedStyle { approachSquare = true });
            r.Render(chart, judge, 8.55, 0, 0.016f, false, frame);
            var c3 = MuguAt(3, 1, 0, 0);
            Assert.Greater(c3[0], 100, "3 拍前は紫（赤の成分がある）");
            r.Render(chart, judge, 9.05, 0, 0.016f, false, frame);
            int outer = MuguAt(3, 1, 1, 1)[0] + MuguAt(3, 1, 1, 1)[1];
            var full = new byte[notes.ConnectLeds];
            for (int i = 0; i < notes.ConnectLeds; i++) full[i] = Con(3, 1, i)[2];

            // 前のノーツ（同じ流れ）から 1 秒（2 拍）: むぎゅっとは 2 拍前から，テープの光の粒は同じ 3 拍前から同じ速さ
            var (chart2, judge2) = Make(new ChartNote(9, 3, NoteType.Touch), new ChartNote(10, 4, NoteType.Touch));
            var r2 = new NoteLedRenderer(notes, new NoteLedStyle { approachSquare = true });
            var events = new List<JudgeEvent>();
            judge2.Touch(3, 9, events);
            r2.Render(chart2, judge2, 8.55, 0, 0.016f, false, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, MuguAt(3, 1, 0, 0), "むぎゅっとはまだ");
            r2.Render(chart2, judge2, 9.05, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 1, 1)[2], 100, "2 拍前から（青）");
            Assert.AreEqual(0, MuguAt(3, 1, 1, 1)[0] + MuguAt(3, 1, 1, 1)[1], "2 拍前は青（赤も緑も無い）");
            Assert.AreEqual(0, outer, "前のノーツが無くても 2 拍前は青");
            notes.TryGetModule(3, out var m3);
            // 光の粒はレーン 3 のテープ（3 → 4）を走るが，速さ（ノーツ時刻までの残りに対する位置）は同じ
            int peakA = System.Array.IndexOf(full, full.Max());
            var tape = new byte[notes.ConnectLeds];
            for (int i = 0; i < notes.ConnectLeds; i++) tape[i] = Con(m3.device, m3.module, i)[2];
            int peakB = System.Array.IndexOf(tape, tape.Max());
            Assert.AreEqual(notes.ConnectLeds - 1 - peakA, peakB, "同じ速さ（向きだけ逆）");
        }

        [Test]
        public void SquareStepsOncePerBeatAndGuideTakesTheBeatColor()
        {
            // 120BPM（1 拍 0.5 秒）．ノーツ 10 秒（拍の頭）．3 拍前（8.5〜9.0）は外周，2 拍前（9.0〜9.5）は真ん中，最後の 1 拍は内側
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, new NoteLedStyle { approachSquare = true });
            foreach (double t in new[] { 8.55, 8.95 })
            {
                r.Render(chart, judge, t, 0, 0.016f, false, frame);
                Assert.Greater(MuguAt(3, 1, 0, 0)[2], 100, $"{t} 秒: 外周（拍の中では動かない）");
                Assert.AreEqual(0, MuguAt(3, 1, 1, 1)[2], $"{t} 秒: 真ん中はまだ");
            }
            r.Render(chart, judge, 9.2, 0, 0.016f, false, frame);
            Assert.AreEqual(0, MuguAt(3, 1, 0, 0)[2], "外周は消えた");
            Assert.Greater(MuguAt(3, 1, 1, 1)[2], 100, "真ん中の段");
            r.Render(chart, judge, 9.7, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 2, 2)[1], 80, "内側の段（水色）");
            // 道しるべの粒もむぎゅっとと同じ拍の色（最後の 1 拍は水色．テープは R と G を入れ替えて書く）
            notes.TryGetModule(3, out var m3);
            int g = 0;
            for (int i = 0; i < notes.ConnectLeds; i++) g = System.Math.Max(g, Con(m3.device, m3.module, i)[0]);
            Assert.Greater(g, 100, "テープ 3 の粒は水色");
        }

        [Test]
        public void OffBeatNoteCountsMusicBeats()
        {
            // ノーツが拍の途中（10.2 秒．拍の頭は 10.0）: 拍の頭 10.0 からノーツまでが最後の 1 拍（水色），その前の拍（9.5〜10.0）が 2 拍前（青）
            var (chart, judge) = Make(new ChartNote(10.2, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, new NoteLedStyle { approachSquare = true });
            r.Render(chart, judge, 9.8, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 1, 1)[2], 100, "2 拍前（青）は真ん中の段");
            Assert.AreEqual(0, MuguAt(3, 1, 1, 1)[1]);
            r.Render(chart, judge, 10.1, 0, 0.016f, false, frame);
            Assert.Greater(MuguAt(3, 1, 2, 2)[1], 80, "拍の頭を過ぎたら最後の 1 拍（水色）");
        }

        [Test]
        public void BeatsApproachFollowsTempo()
        {
            // 60BPM（1 拍 1 秒）なら 3 秒前から
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes, new NoteLedStyle { approachSquare = true }) { Tempo = new TempoMap(new[] { new TempoPoint { TimeSec = 0, Bpm = 60, BeatsPerBar = 4 } }) };
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
