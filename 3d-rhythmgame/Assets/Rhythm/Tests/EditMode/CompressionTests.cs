using System;
using Arte.Rhythm.Hardware;
using Arte.Rhythm.Net;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>PIXELS の作り方（明るさ・PackBits・差分）．</summary>
    public class CompressionTests
    {
        // ファームのテスト（test_pixels_packbits_golden）と同じバイト列
        [Test]
        public void PackBitsMatchesFirmware()
        {
            byte[] rgb = { 1, 2, 3, 1, 2, 3, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var buf = new byte[64];
            int n = ArteProtocol.WritePixelsHeader(buf, new PixelsHeader(3, 1, 0x1234, 0x0F, 0, 5, ArteProtocol.EncodingPackBits));
            n += PixelCodec.EncodePackBits(rgb, buf.AsSpan(n));
            byte[] expected =
            {
                0x41, 0x52, 0x02, 0x10, 0x03, 0x01, 0x34, 0x12, 0x0F, 0x01, 0x00, 0x00, 0x05, 0x00,
                0x81, 1, 2, 3, 0x01, 4, 5, 6, 7, 8, 9,
            };
            CollectionAssert.AreEqual(expected, buf.AsSpan(0, n).ToArray());

            Assert.IsTrue(ArteProtocol.TryReadPixels(expected, out var h, out var payload));
            Assert.AreEqual(ArteProtocol.EncodingPackBits, h.Encoding);
            var back = new byte[15];
            Assert.IsTrue(PixelCodec.DecodePackBits(payload, back, h.Count));
            CollectionAssert.AreEqual(rgb, back);
        }

        [Test]
        public void PackBitsRoundTripsAnyData()
        {
            var rnd = new Random(1);
            var dest = new byte[2000];
            foreach (int count in new[] { 1, 2, 127, 128, 129, 130, 300, 480 })
            {
                for (int trial = 0; trial < 50; trial++)
                {
                    // 同じ色が続く所と，ばらばらな所が混ざったデータ
                    var rgb = new byte[count * 3];
                    int palette = 1 + rnd.Next(trial % 2 == 0 ? 3 : 256);
                    for (int i = 0; i < count; i++)
                    {
                        int c = rnd.Next(palette);
                        rgb[i * 3] = (byte)c;
                        rgb[i * 3 + 1] = (byte)(c * 7);
                        rgb[i * 3 + 2] = (byte)(c * 13);
                    }
                    int n = PixelCodec.EncodePackBits(rgb, dest);
                    Assert.Greater(n, 0);
                    Assert.LessOrEqual(n, count * 3 + (count + 127) / 128, "最悪でも 128 個ごとに 1 バイト");
                    var back = new byte[count * 3];
                    Assert.IsTrue(PixelCodec.DecodePackBits(dest.AsSpan(0, n), back, count));
                    CollectionAssert.AreEqual(rgb, back);
                }
            }
        }

        [Test]
        public void PackBitsShrinksUniformStrip()
        {
            var rgb = new byte[480 * 3];
            var dest = new byte[2000];
            // 480 個同じ色 → 129 個 × 3 + 93 個 = 4 回 × 4 バイト
            Assert.AreEqual(16, PixelCodec.EncodePackBits(rgb, dest));
            // 入りきらなければ −1
            Assert.AreEqual(-1, PixelCodec.EncodePackBits(rgb, dest.AsSpan(0, 15)));
        }

        [Test]
        public void BrightnessMatchesOutputModel()
        {
            var src = new byte[256];
            for (int i = 0; i < 256; i++) src[i] = (byte)i;
            var dst = new byte[256];
            foreach (byte b in new byte[] { 0, 1, 10, 50, 128, 254, 255 })
            {
                PixelCodec.ApplyOutput(src, dst, b, Lin);
                for (int i = 0; i < 256; i++) Assert.AreEqual(LedOutputModel.ApplyBrightness((byte)i, b), dst[i], $"明るさ {b} 値 {i}");
            }
            PixelCodec.ApplyOutput(src, dst, 255, Lin);
            CollectionAssert.AreEqual(src, dst, "明るさ最大ならそのまま");
        }

        static (FrameEncoder enc, LedFrame frame, byte[][] packets, int[] lengths) MakeEncoder()
        {
            var counts = new ushort[8];
            counts[0] = 480; counts[1] = 480; counts[3] = 470;
            var frame = new LedFrame(2, counts, 3);
            var packets = new byte[4][];
            for (int i = 0; i < 4; i++) packets[i] = new byte[ArteProtocol.MaxPacket];
            return (new FrameEncoder(), frame, packets, new int[4]);
        }

        const LedOutputModel.OutputCurve Lin = LedOutputModel.OutputCurve.Linear;
        const LedOutputModel.OutputCurve Srgb = LedOutputModel.OutputCurve.Srgb;

        [Test]
        public void SrgbCurveConvertsToLight()
        {
            // 明るさ最大: 端はそのまま，中間（sRGB 128 ≒ 光量 21.6%）は暗くなる
            Assert.AreEqual(0, LedOutputModel.Encode(0, 255, Srgb));
            Assert.AreEqual(255, LedOutputModel.Encode(255, 255, Srgb));
            Assert.AreEqual(55, LedOutputModel.Encode(128, 255, Srgb));
            // 明るさ 50: 最大は Linear と同じ 50．暗い色は潰れやすい
            Assert.AreEqual(50, LedOutputModel.Encode(255, 50, Srgb));
            Assert.AreEqual(11, LedOutputModel.Encode(128, 50, Srgb));
            Assert.AreEqual(0, LedOutputModel.Encode(20, 50, Srgb));
            // 単調に増える
            for (int b = 0; b < 256; b += 17)
                for (int c = 1; c < 256; c++)
                    Assert.GreaterOrEqual(LedOutputModel.Encode((byte)c, b, Srgb), LedOutputModel.Encode((byte)(c - 1), b, Srgb));
            // Linear は従来と同じ
            for (int c = 0; c < 256; c++) Assert.AreEqual(LedOutputModel.ApplyBrightness((byte)c, 50), LedOutputModel.Encode((byte)c, 50, Lin));
            Assert.AreEqual(LedOutputModel.OutputCurve.Srgb, LedOutputModel.ParseCurve("sRGB"));
            Assert.AreEqual(LedOutputModel.OutputCurve.Linear, LedOutputModel.ParseCurve(null));
        }

        [Test]
        public void EncoderUsesCurve()
        {
            var (enc, frame, packets, lengths) = MakeEncoder();
            frame.Pin(0, 0).Fill(128);
            enc.Encode(frame, 0, 1, Bright255, Srgb, false, 0, packets, lengths);
            Assert.AreEqual(55, packets[0][ArteProtocol.PixelsHeaderSize]);
        }

        static readonly DeviceConfig Bright255 = new DeviceConfig { BrightnessPerf = 255, BrightnessNote = 255 };

        [Test]
        public void EncoderSendsOnlyChangedPins()
        {
            var (enc, frame, packets, lengths) = MakeEncoder();
            // 初回は全ピン
            Assert.AreEqual(3, enc.Encode(frame, 0, 1, Bright255, Lin, true, 0, packets, lengths));
            Assert.Greater(lengths[0], 0);
            Assert.AreEqual(0, lengths[2], "LED の無いピンは送らない");
            // 何も変わらなければ送らない
            Assert.AreEqual(0, enc.Encode(frame, 0, 2, Bright255, Lin, true, 10_000, packets, lengths));
            // ピン 1 だけ変える → ピン 1 だけ，expectMask もピン 1 だけ
            frame.Pin(0, 1)[5] = 9;
            Assert.AreEqual(1, enc.Encode(frame, 0, 3, Bright255, Lin, true, 20_000, packets, lengths));
            Assert.IsTrue(ArteProtocol.TryReadPixels(packets[1].AsSpan(0, lengths[1]), out var h, out _));
            Assert.AreEqual(1, h.Pin);
            Assert.AreEqual(0b0010, h.ExpectMask);
            Assert.AreEqual(ArteProtocol.EncodingPackBits, h.Encoding);
            Assert.Less(lengths[1], 40, "ほとんど黒なら小さくなる");
        }

        [Test]
        public void EncoderRefreshesOnePinAtATime()
        {
            var (enc, frame, packets, lengths) = MakeEncoder();
            enc.Encode(frame, 0, 1, Bright255, Lin, true, 0, packets, lengths);
            // 0.5 秒たつと，変わっていないピンを 1 本ずつ送り直す（パケットが落ちても直るように）
            int sent = 0;
            for (int i = 0; i < 3; i++)
            {
                int n = enc.Encode(frame, 0, (ushort)(2 + i), Bright255, Lin, true, 600_000 + (ulong)i * 16_000, packets, lengths);
                Assert.AreEqual(1, n);
                sent |= Array.FindIndex(lengths, l => l > 0) is int p && p >= 0 ? 1 << p : 0;
            }
            Assert.AreEqual(0b1011, sent, "3 本とも 1 回ずつ");
            Assert.AreEqual(0, enc.Encode(frame, 0, 9, Bright255, Lin, true, 700_000, packets, lengths));
        }

        [Test]
        public void EncoderWithoutCompactSendsEveryPinRaw()
        {
            var (enc, frame, packets, lengths) = MakeEncoder();
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(3, enc.Encode(frame, 0, (ushort)(1 + i), Bright255, Lin, false, (ulong)i * 16_000, packets, lengths));
                Assert.AreEqual(ArteProtocol.PixelsHeaderSize + 480 * 3, lengths[0]);
                Assert.IsTrue(ArteProtocol.TryReadPixels(packets[0].AsSpan(0, lengths[0]), out var h, out _));
                Assert.AreEqual(ArteProtocol.EncodingRaw, h.Encoding);
                Assert.AreEqual(0b1011, h.ExpectMask);
            }
        }

        [Test]
        public void EncoderAppliesBrightnessBeforeComparing()
        {
            var (enc, frame, packets, lengths) = MakeEncoder();
            var dim = new DeviceConfig { BrightnessPerf = 10, BrightnessNote = 10 };
            enc.Encode(frame, 0, 1, dim, Lin, true, 0, packets, lengths);
            // 明るさ 10 では 0〜23 はどれも 0 になる → 光り方が同じなので送らない
            frame.Pin(0, 0)[0] = 20;
            Assert.AreEqual(0, enc.Encode(frame, 0, 2, dim, Lin, true, 16_000, packets, lengths));
            frame.Pin(0, 0)[0] = 200;
            Assert.AreEqual(1, enc.Encode(frame, 0, 3, dim, Lin, true, 32_000, packets, lengths));
            // 送る値は明るさを掛けた後
            Assert.IsTrue(ArteProtocol.TryReadPixels(packets[0].AsSpan(0, lengths[0]), out var h, out var payload));
            var rgb = new byte[480 * 3];
            Assert.IsTrue(PixelCodec.DecodePackBits(payload, rgb, h.Count));
            Assert.AreEqual(LedOutputModel.ApplyBrightness(200, 10), rgb[0]);
        }
    }
}
