using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>
    /// 通信プロトコル v2．バイト列の期待値はファームのテスト（tape_led_module_s3/test/test_link）と同じ．
    /// </summary>
    public class ProtocolTests
    {
        static byte[] Hex(string s)
        {
            s = s.Replace(" ", "");
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(i * 2, 2), 16);
            return b;
        }

        // ---- ファームと共通のバイト列 ----

        [Test]
        public void PixelsMatchesFirmware()
        {
            var expected = Hex("41520210 0301 3412 0F00 0000 0200 010203040506");
            var buf = new byte[64];
            int n = ArteProtocol.WritePixelsHeader(buf, new PixelsHeader(3, 1, 0x1234, 0x0F, 0, 2));
            new byte[] { 1, 2, 3, 4, 5, 6 }.CopyTo(buf, n);
            CollectionAssert.AreEqual(expected, buf.AsSpan(0, n + 6).ToArray());

            Assert.IsTrue(ArteProtocol.TryReadPixels(expected, out var h, out var rgb));
            Assert.AreEqual(0x1234, h.Frame);
            Assert.AreEqual(6, rgb.Length);
            Assert.IsFalse(ArteProtocol.TryReadPixels(expected.AsSpan(0, expected.Length - 1), out _, out _));
        }

        [Test]
        public void TouchMatchesFirmware()
        {
            var expected = Hex("41520290 05030200 0807060504030201 1000 0101 40420F0000000000 1100 0000 8877665544332211");
            var events = new TouchEvent[8];
            Assert.IsTrue(ArteProtocol.TryReadTouch(expected, out var h, events));
            Assert.AreEqual(5, h.Device);
            Assert.AreEqual(3, h.State);
            Assert.AreEqual(2, h.Count);
            Assert.AreEqual(0x0102030405060708UL, h.DeviceTimeUs);
            Assert.AreEqual(0x10, events[0].Seq);
            Assert.AreEqual(1, events[0].Sensor);
            Assert.IsTrue(events[0].Down);
            Assert.AreEqual(1000000UL, events[0].TimeUs);
            Assert.IsFalse(events[1].Down);
            Assert.AreEqual(0x1122334455667788UL, events[1].TimeUs);

            var buf = new byte[ArteProtocol.MaxPacket];
            int n = ArteProtocol.WriteTouch(buf, h, events.AsSpan(0, 2));
            CollectionAssert.AreEqual(expected, buf.AsSpan(0, n).ToArray());
        }

        [Test]
        public void PongMatchesFirmware()
        {
            var expected = Hex("41520282 0200 07000000 0010000000000000 0020000000000000 CDAB 0030000000000000");
            Assert.IsTrue(ArteProtocol.TryReadPong(expected, out var m));
            Assert.AreEqual(2, m.Device);
            Assert.AreEqual(7u, m.Seq);
            Assert.AreEqual(0x1000UL, m.HostTimeUs);
            Assert.AreEqual(0x2000UL, m.DeviceTimeUs);
            Assert.AreEqual(0xABCD, m.LastFrame);
            Assert.AreEqual(0x3000UL, m.LastShowUs);
            var buf = new byte[64];
            CollectionAssert.AreEqual(expected, buf.AsSpan(0, ArteProtocol.WritePong(buf, m)).ToArray());
        }

        [Test]
        public void ConfigMatchesFirmware()
        {
            var expected = Hex("41520220 FF 32 32 01 03 32 3200");
            var c = new DeviceConfig { Target = 0xFF, BrightnessPerf = 50, BrightnessNote = 50, Flags = ArteProtocol.FlagTouchFeedback, DebounceMs = 3, HeartbeatMs = 50, HoldMs = 50 };
            var buf = new byte[32];
            CollectionAssert.AreEqual(expected, buf.AsSpan(0, ArteProtocol.WriteConfig(buf, c)).ToArray());
        }

        [Test]
        public void HelloRoundTrip()
        {
            var mac = MacAddress.From(new byte[] { 0x34, 0x85, 0x18, 0xAA, 0xBB, 0xCC });
            var h = new Hello { Device = 4, Firmware = 0x0200, Mac = mac, Pins = 4, LedsPerPin = 480, TouchCount = 5, Session = 99 };
            var buf = new byte[64];
            Assert.AreEqual(ArteProtocol.HelloSize, ArteProtocol.WriteHello(buf, h));
            Assert.IsTrue(ArteProtocol.TryReadHello(buf, out var r));
            Assert.AreEqual(mac, r.Mac);
            Assert.AreEqual("34:85:18:AA:BB:CC", r.Mac.ToString());
            Assert.AreEqual(480, r.LedsPerPin);
            Assert.AreEqual(99u, r.Session);
        }

        [Test]
        public void LegacyPacketsAreNotV2()
        {
            Assert.AreEqual((PacketType)0, ArteProtocol.TypeOf(new byte[] { 255, 100, 50, 50 }));
            Assert.AreEqual((PacketType)0, ArteProtocol.TypeOf(new byte[] { 255, 3 }));
        }

        // ---- 時刻合わせ ----

        [Test]
        public void ClockSyncUsesFastestSample()
        {
            var clock = new ClockSync(windowSize: 10);
            const long offset = 5_000_000;
            var rnd = new Random(1);
            ulong t = 1_000_000;
            for (int i = 0; i < 10; i++)
            {
                // 行き 1〜30ms，帰り 1〜30ms の揺れ（偏りあり）
                ulong up = (ulong)rnd.Next(1000, 30000), down = (ulong)rnd.Next(1000, 30000);
                if (i == 6) { up = 900; down = 1100; }
                clock.AddSample(t, (ulong)((long)(t + up) + offset), t + up + down);
                t += 250_000;
            }
            Assert.AreEqual(2000, clock.RttUs);
            Assert.That(clock.OffsetUs, Is.EqualTo(offset).Within(200));
            Assert.That((long)clock.ToHost((ulong)(9_000_000 + offset)), Is.EqualTo(9_000_000).Within(200));
        }

        // ---- タッチの重複除去 ----

        [Test]
        public void TouchTrackerDropsResentEvents()
        {
            var tracker = new TouchEventTracker();
            var fresh = new TouchEvent[8];
            var h = new TouchHeader { DeviceTimeUs = 10_000_000 };
            var e1 = new TouchEvent(5, 0, true, 9_990_000);
            var e2 = new TouchEvent(6, 0, false, 9_995_000);
            Assert.AreEqual(1, tracker.Accept(h, new[] { e1 }, fresh));
            Assert.AreEqual(1, tracker.Accept(h, new[] { e1, e2 }, fresh));
            Assert.AreEqual(6, fresh[0].Seq);
            Assert.AreEqual(0, tracker.Accept(h, new[] { e1, e2 }, fresh));
        }

        [Test]
        public void TouchTrackerIgnoresOldEventsAfterReconnect()
        {
            var tracker = new TouchEventTracker();
            var fresh = new TouchEvent[8];
            var h = new TouchHeader { DeviceTimeUs = 10_000_000 };
            var old = new TouchEvent(40, 1, true, 3_000_000);
            var recent = new TouchEvent(41, 1, false, 9_800_000);
            Assert.AreEqual(1, tracker.Accept(h, new[] { old, recent }, fresh));
            Assert.AreEqual(41, fresh[0].Seq);
        }

        [Test]
        public void TouchTrackerHandlesSeqWrap()
        {
            var tracker = new TouchEventTracker();
            var fresh = new TouchEvent[8];
            var h = new TouchHeader { DeviceTimeUs = 1_000 };
            tracker.Accept(h, new[] { new TouchEvent(65535, 0, true, 1_000) }, fresh);
            Assert.AreEqual(1, tracker.Accept(h, new[] { new TouchEvent(65535, 0, true, 1_000), new TouchEvent(0, 0, false, 1_000) }, fresh));
            Assert.AreEqual(0, fresh[0].Seq);
        }

        // ---- 送るデータの形 ----

        [Test]
        public void LedFrameFollowsSetup()
        {
            string dir = TestData.HardwareDir();
            var config = HardwareJson.LoadConfig(File.ReadAllText(Path.Combine(dir, "hardware.json")));
            var setup = HardwareJson.LoadSetup(File.ReadAllText(Path.Combine(dir, "setups", "kodaisai-1101.json")));
            var frame = LedFrame.FromLayout(new PerfLayout(config, setup), new NoteLayout(config, setup), config.devices.count);

            Assert.AreEqual(0x0F, frame.PinMask(0));
            Assert.AreEqual(480, frame.LedCount(0, 0));
            Assert.AreEqual(480, frame.LedCount(0, 3));
            // デバイス 7: ピン 0 に 2 本（240 個），ピン 1・2 は入口のため外している
            Assert.AreEqual(0x09, frame.PinMask(7));
            Assert.AreEqual(240, frame.LedCount(7, 0));
            Assert.AreEqual(0, frame.LedCount(7, 1));

            var buf = new byte[ArteProtocol.MaxPacket];
            Assert.AreEqual(0, frame.WritePacket(buf, 7, 1, 1));
            Assert.AreEqual(ArteProtocol.PixelsHeaderSize + 480 * 3, frame.WritePacket(buf, 0, 3, 1));
        }

        [Test]
        public void FrameAssemblerMatchesFirmwareRules()
        {
            var fa = new FrameAssembler();
            Assert.AreEqual(FrameAssembler.Result.Accept, fa.OnPart(1, 0, 0x09, 0));
            Assert.AreEqual(FrameAssembler.Result.Complete, fa.OnPart(1, 3, 0x09, 0));
            fa.MarkShown(false);
            fa.OnPart(2, 0, 0x0F, 16_000);
            Assert.AreEqual(FrameAssembler.Result.Accept, fa.OnPart(3, 0, 0x0F, 32_000));
            Assert.AreEqual(1u, fa.FramesDropped);
            Assert.AreEqual(FrameAssembler.Result.Ignore, fa.OnPart(2, 1, 0x0F, 33_000));
            Assert.IsTrue(fa.Overdue(82_000, 50));
            fa.OnPart(30000, 0, 0x01, 0);
            fa.MarkShown(false);
            Assert.AreEqual(FrameAssembler.Result.Complete, fa.OnPart(0, 0, 0x01, 0), "PC 再起動でフレーム番号が戻った");
        }

        // ---- PC とデバイスを localhost でつなぐ ----

        [Test]
        public void LoopbackDiscoverySyncFrameAndTouch()
        {
            var watch = Stopwatch.StartNew();
            ulong HostUs() => (ulong)(watch.Elapsed.TotalMilliseconds * 1000) + 1_000_000;
            const ulong deviceOffset = 777_000_000;   // デバイスの時計は PC より 777 秒進んでいる
            ulong DeviceUs() => HostUs() + deviceOffset;

            var options = new LinkOptions { HostPort = 0, DeviceCount = 8, PingIntervalSec = 0.02 };
            options.Config.HoldMs = 50;
            using var host = new LinkHost(options, HostUs);
            host.Start();
            var touches = new ConcurrentQueue<DeviceTouch>();
            host.Touched += touches.Enqueue;

            var target = new IPEndPoint(IPAddress.Loopback, host.LocalPort);
            using var dev3 = new VirtualDevice(3, MacAddress.From(new byte[] { 1, 2, 3, 4, 5, 3 }), target);
            using var dev7 = new VirtualDevice(7, MacAddress.From(new byte[] { 1, 2, 3, 4, 5, 7 }), target, ledsPerPin: 480);
            using var stray = new VirtualDevice(ArteProtocol.UnassignedDevice, MacAddress.From(new byte[] { 9, 9, 9, 9, 9, 9 }), target);

            bool Until(Func<bool> cond, int ms = 3000)
            {
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < ms)
                {
                    host.Update();
                    dev3.Poll(DeviceUs());
                    dev7.Poll(DeviceUs());
                    stray.Poll(DeviceUs());
                    if (cond()) return true;
                    Thread.Sleep(2);
                }
                return false;
            }

            dev3.SendHello();
            dev7.SendHello();
            stray.SendHello();
            Assert.IsTrue(Until(() => dev3.Registered && dev7.Registered), "WELCOME が届かない");
            Assert.AreEqual(host.Session, dev3.Session);
            Assert.IsFalse(stray.Registered, "番号未設定の基板は登録しない");
            Assert.IsTrue(Until(() => dev3.Config.BrightnessPerf == 50), "CONFIG が届かない");

            // 時刻合わせ
            Assert.IsTrue(Until(() => host.Snapshot().Exists(d => d.Id == 3 && d.Synced)));
            Thread.Sleep(100);
            Until(() => false, 200);
            var info3 = host.Snapshot().Find(d => d.Id == 3);
            Assert.That(info3.ClockOffsetUs, Is.EqualTo((long)deviceOffset).Within(Math.Max(2000, info3.RttUs)));
            Assert.IsTrue(host.Snapshot().Exists(d => d.Mac.ToString() == "09:09:09:09:09:09" && d.Id == 255));

            // フレーム: デバイス 7 はピン 0 とノーツだけで揃う
            var counts = new ushort[8 * 4];
            for (int d = 0; d < 8; d++) { counts[d * 4] = 480; counts[d * 4 + 1] = 480; counts[d * 4 + 2] = 480; counts[d * 4 + 3] = 480; }
            counts[7 * 4] = 240; counts[7 * 4 + 1] = 0; counts[7 * 4 + 2] = 0;
            var frame = new LedFrame(8, counts, 3);
            frame.Pin(3, 2)[30] = 200;
            frame.Pin(7, 3)[3] = 77;
            host.SendFrame(frame);
            Assert.IsTrue(Until(() => dev3.Frames.FramesShown == 1 && dev7.Frames.FramesShown == 1), "フレームが表示されない");
            // PC が明るさを掛けて送る
            var c = host.Options.Config;
            Assert.AreEqual(LedOutputModel.ApplyBrightness(200, c.BrightnessPerf), dev3.ShownPin(2)[30]);
            Assert.AreEqual(LedOutputModel.ApplyBrightness(77, c.BrightnessNote), dev7.ShownPin(3)[3]);
            Assert.AreEqual(0u, dev7.Frames.FramesForced);

            // 表示までの遅れが測れる（PONG に最後の表示フレームが載る）
            Assert.IsTrue(Until(() => !double.IsNaN(host.Snapshot().Find(d => d.Id == 7).DisplayLatencyMs)));

            // タッチ: デバイス時刻で 30ms 前に触れた → PC の時刻に直して届く．再送は 1 回にまとまる
            ulong touchedHostUs = HostUs() - 30_000;
            dev3.Touch(2, true, touchedHostUs + deviceOffset, DeviceUs());
            dev3.SendTouch(DeviceUs());
            dev3.SendTouch(DeviceUs());
            Assert.IsTrue(Until(() => touches.Count >= 1));
            Until(() => false, 100);
            Assert.AreEqual(1, touches.Count);
            Assert.IsTrue(touches.TryDequeue(out var t));
            Assert.AreEqual(3, t.Device);
            Assert.AreEqual(2, t.Sensor);
            Assert.IsTrue(t.Down);
            Assert.IsTrue(t.Synced);
            Assert.That((long)t.HostTimeUs, Is.EqualTo((long)touchedHostUs).Within(Math.Max(2000, info3.RttUs)));
        }
    }
}
