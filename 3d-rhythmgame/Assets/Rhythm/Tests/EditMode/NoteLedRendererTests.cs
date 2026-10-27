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

        [Test]
        public void OpenNoteIsYellowFromGoodWindow()
        {
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes);
            r.Render(chart, judge, 6.99, 0, 0.016f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Mugu(3, 1));
            r.Render(chart, judge, 7.0, 0, 0.016f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 255, 235, 4 }, Mugu(3, 1));
            CollectionAssert.AreEqual(new byte[] { 255, 235, 4 }, Mugu(3, 1, 63), "64 個全部");
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Mugu(3, 0), "ほかのレーンは消えたまま");
        }

        [Test]
        public void JudgedColorStaysAndTrailFlows()
        {
            var (chart, judge) = Make(new ChartNote(10, 4, NoteType.Touch), new ChartNote(20, 4, NoteType.Touch));
            var r = new NoteLedRenderer(notes);
            var events = new List<JudgeEvent>();
            judge.Touch(4, 9.5, events);
            Assert.AreEqual(JudgeResult.Perfect, events[0].Result);
            r.OnJudged(events[0], 100.0);

            // 0.5 秒後: 光の先頭は 9 個目，強さは 1 − 0.5/3．magenta × 0.8333 = 212.5 → 212（偶数への丸め）
            r.Render(chart, judge, 10.0, 100.5, 0.016f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 255, 0, 255 }, Mugu(3, 1), "Perfect は magenta");
            CollectionAssert.AreEqual(new byte[] { 0, 212, 212 }, Con(3, 1, 9), "R と G を入れ替えて書く");
            Assert.Less(Con(3, 1, 0)[2], Con(3, 1, 5)[2], "先頭から離れるほど暗い");

            // 3 秒で光は消える．むぎゅっとは次のノーツが黄色にするまで magenta のまま
            r.Render(chart, judge, 16.9, 103.0, 0.016f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(3, 1, 9));
            CollectionAssert.AreEqual(new byte[] { 255, 0, 255 }, Mugu(3, 1));
            r.Render(chart, judge, 17.0, 103.1, 0.016f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 255, 235, 4 }, Mugu(3, 1));
        }

        [Test]
        public void MissIsCyan()
        {
            var (chart, judge) = Make(new ChartNote(10, 9, NoteType.Touch));
            var r = new NoteLedRenderer(notes);
            var events = new List<JudgeEvent>();
            judge.Advance(10.6, events);
            r.OnJudged(events[0], 0);
            r.Render(chart, judge, 10.6, 0, 0.016f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 0, 255, 255 }, Mugu(7, 0), "レーン 9 = デバイス 7 のモジュール 0");
        }

        [Test]
        public void Lane0ConnectSweepLikeLegacy()
        {
            var r = new NoteLedRenderer(notes);
            // 0.1 秒: 色相 210/360 + 0.05 = 0.6333 → HSV(0.6333, 1, 1) = (0, 0.2, 1) → (0, 51, 255)．R と G を入れ替えて (51, 0, 255)
            r.Render(null, null, 0, 0, 0.1f, true, frame);
            CollectionAssert.AreEqual(new byte[] { 51, 0, 255 }, Con(0, 0, 0));
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(0, 0, 1), "1 フレームに 1 個ずつ");
            r.Render(null, null, 0, 0, 0.1f, true, frame);
            Assert.AreNotEqual(0, Con(0, 0, 1)[2]);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0 }, Con(0, 1, 0), "レーン 0 だけ");

            // 再生していなければ進まない．切ることもできる
            var off = new NoteLedRenderer(notes, new NoteLedStyle { legacyLane0ConnectSweep = false });
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
            // レーン 12 は kodaisai-1101 に無い（旧版でも LED に出なかった）
            var (chart, judge) = Make(new ChartNote(10, 12, NoteType.Touch));
            var r = new NoteLedRenderer(notes);
            r.Render(chart, judge, 9.0, 0, 0.016f, false, frame);
            for (int d = 0; d < 8; d++)
                foreach (byte v in frame.Pin(d, notes.NoteOutputPin).ToArray()) Assert.AreEqual(0, v);
        }
    }
}
