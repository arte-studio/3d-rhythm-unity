using System;
using System.IO;
using Arte.Rhythm.Hardware;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>
    /// ハード構成データから計算した配置・配線が，旧コード（legacy-v1）と同じ結果になることを確かめる．
    /// 旧コードの式はこのファイル内の Legacy* に写してある．新しい送信系に切り替えても実機の見え方が
    /// 変わらないことの保証になる．
    /// </summary>
    public class HardwareLayoutTests
    {
        HardwareConfig config;
        SetupProfile setup;
        PerfLayout perf;
        NoteLayout notes;

        [SetUp]
        public void Load()
        {
            string dir = TestData.HardwareDir();
            config = HardwareJson.LoadConfig(File.ReadAllText(Path.Combine(dir, "hardware.json")));
            setup = HardwareJson.LoadSetup(File.ReadAllText(Path.Combine(dir, "setups", "kodaisai-1101.json")));
            perf = new PerfLayout(config, setup);
            notes = new NoteLayout(config, setup);
        }

        // ---- 旧コードの式（Assets/Script/UdpController.cs, RenderTextureSampler.cs, NoteLeds.cs） ----

        static int LegacyConvertPerfLedsID(int i)
        {
            int n = i % 30 * 3;
            if (i / 30 == 1) n += 2;
            else if (i / 30 == 2) n += 1;
            return n;
        }

        /// <summary>RenderTextureSampler: キャンバス (column, row) → perfLedData 内の LED 番号．</summary>
        static int LegacySamplerLedIndex(int column, int row) => column % 2 == 0 ? 119 - row : row;

        /// <summary>UdpController.SendAllLedData で 1 パケット分を組み立てる処理（ヘッダ 2 バイトを除く）．</summary>
        static byte[] LegacyPerfPacket(byte[] perfLedData, int deviceId, int packetType)
        {
            if (deviceId == 7 && packetType > 0) return null;
            int strips = deviceId == 7 ? 2 : 4;
            var dest = new byte[strips * 360];
            int begin = deviceId * 4 + 30 * packetType;
            for (int j = 0; j < strips; j++)
            {
                int phys = LegacyConvertPerfLedsID(begin + j);
                Array.Copy(perfLedData, phys * 360, dest, j * 360, 360);
            }
            return dest;
        }

        static (int device, int module) LegacyLaneToHard(int lane)
        {
            switch (lane)
            {
                case 0: return (0, 0);
                case 1: return (0, 1);
                case 2: return (0, 2);
                case 3: return (3, 0);
                case 4: return (3, 1);
                case 5: return (3, 2);
                case 6: return (3, 3);
                case 7: return (3, 4);
                case 8: return (5, 1);
                case 9: return (7, 0);
                case 10: return (7, 1);
                default: return (-1, -1);
            }
        }

        // ---- テスト ----

        [Test]
        public void ColumnCountMatchesLegacyCanvas()
        {
            Assert.AreEqual(90, perf.ColumnCount);
            Assert.AreEqual(120, perf.LedsPerStrip);
            Assert.AreEqual(2.0 / 120, perf.LedPitchM, 1e-6);
        }

        [Test]
        public void EveryColumnHasUniqueAddress()
        {
            var seen = new System.Collections.Generic.HashSet<StripAddress>();
            for (int c = 0; c < perf.ColumnCount; c++)
            {
                StripAddress a = perf.GetStrip(c).Address;
                Assert.IsTrue(seen.Add(a), $"列 {c} の配線位置 {a} が重複");
                Assert.AreEqual(c, perf.ColumnOf(a));
            }
        }

        [Test]
        public void WiringMatchesLegacyConvertPerfLedsID()
        {
            for (int noteId = 0; noteId < 90; noteId++)
            {
                int pin = noteId / 30;
                int angle = noteId % 30;
                var address = new StripAddress(angle / 4, pin, angle % 4);
                Assert.AreEqual(LegacyConvertPerfLedsID(noteId), perf.ColumnOf(address), $"noteId {noteId}");
            }
        }

        [Test]
        public void ZigzagMatchesLegacySampler()
        {
            for (int c = 0; c < perf.ColumnCount; c++)
                for (int row = 0; row < 120; row++)
                    Assert.AreEqual(LegacySamplerLedIndex(c, row), perf.WireIndexOf(c, row), $"列 {c} 行 {row}");
        }

        [Test]
        public void PackedPacketsMatchLegacyPipeline()
        {
            // ランダムなキャンバスを，旧パイプライン（Sampler → UdpController）と PackPin の両方に通して比べる
            var rng = new Random(1234);
            var canvas = new byte[90 * 120 * 3];
            rng.NextBytes(canvas);

            var perfLedData = new byte[90 * 360];
            for (int c = 0; c < 90; c++)
                for (int row = 0; row < 120; row++)
                    for (int ch = 0; ch < 3; ch++)
                        perfLedData[c * 360 + LegacySamplerLedIndex(c, row) * 3 + ch] = canvas[(c * 120 + row) * 3 + ch];

            var dest = new byte[4 * 360];
            for (int device = 0; device < 8; device++)
            {
                for (int pin = 0; pin < 3; pin++)
                {
                    byte[] legacy = LegacyPerfPacket(perfLedData, device, pin);
                    int n = perf.PackPin(canvas, device, pin, dest);
                    if (legacy == null)
                    {
                        // 旧コードが送らないパケットは，新しい方では外したストリップ（全部 0）になる
                        for (int i = 0; i < n; i++) Assert.AreEqual(0, dest[i], $"device {device} pin {pin}");
                        continue;
                    }
                    Assert.AreEqual(legacy.Length, n, $"device {device} pin {pin} の長さ");
                    for (int i = 0; i < n; i++)
                        Assert.AreEqual(legacy[i], dest[i], $"device {device} pin {pin} byte {i}");
                }
            }
        }

        [Test]
        public void RemovedStripsAreTheLastDevicesOuterPins()
        {
            int removed = 0;
            for (int c = 0; c < perf.ColumnCount; c++)
                if (!perf.GetStrip(c).Installed) removed++;
            Assert.AreEqual(4, removed);
            Assert.IsFalse(perf.GetStrip(perf.ColumnOf(new StripAddress(7, 1, 0))).Installed);
            Assert.IsTrue(perf.GetStrip(perf.ColumnOf(new StripAddress(7, 0, 1))).Installed);
        }

        [Test]
        public void LaneMapMatchesLegacyNoteLeds()
        {
            for (int lane = 0; lane < 16; lane++)
            {
                var (device, module) = LegacyLaneToHard(lane);
                bool ok = notes.TryGetModule(lane, out ModuleAddress m);
                Assert.AreEqual(device >= 0, ok, $"lane {lane}");
                if (!ok) continue;
                Assert.AreEqual(device, m.device, $"lane {lane}");
                Assert.AreEqual(module, m.module, $"lane {lane}");
                Assert.AreEqual(lane, notes.LaneOf(m));
            }
        }

        [Test]
        public void NoteLedIndicesMatchLegacyStride()
        {
            // 旧 NoteLeds: index = (64 + 30) * innerId + raw * 8 + cow / Connect は 64 の後ろ
            Assert.AreEqual(94 * 2 + 3 * 8 + 5, notes.MuguLedIndex(2, 3, 5));
            Assert.AreEqual(94 * 4 + 64 + 29, notes.ConnectLedIndex(4, 29));
            Assert.Less(notes.ConnectLedIndex(4, 29), notes.NotePacketLeds);
        }

        [Test]
        public void LedPositionsLieOnTheRings()
        {
            for (int c = 0; c < perf.ColumnCount; c++)
            {
                PerfStrip s = perf.GetStrip(c);
                perf.GetLedPosition(c, 0, out float x, out float y0, out float z);
                Assert.AreEqual(s.RadiusM, Math.Sqrt(x * x + z * z), 1e-4);
                perf.GetLedPosition(c, 119, out _, out float y1, out _);
                Assert.AreEqual(config.perf.stripLengthM - perf.LedPitchM, y1 - y0, 1e-4);
            }
        }

        [Test]
        public void BrightnessMatchesFirmware()
        {
            // ファーム: (c * (b + 1)) >> 8
            Assert.AreEqual(50, LedOutputModel.ApplyBrightness(255, 50));
            Assert.AreEqual(0, LedOutputModel.ApplyBrightness(5, 50));
            Assert.AreEqual(255, LedOutputModel.ApplyBrightness(255, 255));
            Assert.AreEqual(51, LedOutputModel.DistinctLevels(50));
        }

        [Test]
        public void UnknownKeysAreRejected()
        {
            Assert.Throws<Newtonsoft.Json.JsonSerializationException>(() => HardwareJson.LoadSetup("{\"nmae\":\"typo\"}"));
        }
    }
}
