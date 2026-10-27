using System;
using Arte.Rhythm.Net;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>PIXELS の圧縮（PackBits・明るさによる値そろえ・差分）．</summary>
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
        public void CanonicalizeKeepsOutput()
        {
            foreach (byte b in new byte[] { 0, 1, 10, 50, 128, 254, 255 })
            {
                var src = new byte[256];
                for (int i = 0; i < 256; i++) src[i] = (byte)i;
                var dst = new byte[256];
                PixelCodec.Canonicalize(src, dst, b);
                for (int i = 0; i < 256; i++)
                {
                    Assert.AreEqual(PixelCodec.Scale(src[i], b), PixelCodec.Scale(dst[i], b), $"明るさ {b} 値 {i}");
                    Assert.LessOrEqual(dst[i], src[i]);
                }
                if (b == 255) CollectionAssert.AreEqual(src, dst, "明るさ最大ならそのまま");
            }
            // 明るさ 10 なら 11 段階になる
            var all = new byte[256];
            for (int i = 0; i < 256; i++) all[i] = (byte)i;
            PixelCodec.Canonicalize(all, all, 10);
            Assert.AreEqual(11, new System.Collections.Generic.HashSet<byte>(all).Count);
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

        static readonly DeviceConfig Bright255 = new DeviceConfig { BrightnessPerf = 255, BrightnessNote = 255 };

        [Test]
        public void EncoderSendsOnlyChangedPins()
        {
            var (enc, frame, packets, lengths) = MakeEncoder();
            // 初回は全ピン
            Assert.AreEqual(3, enc.Encode(frame, 0, 1, Bright255, true, 0, packets, lengths));
            Assert.Greater(lengths[0], 0);
            Assert.AreEqual(0, lengths[2], "LED の無いピンは送らない");
            // 何も変わらなければ送らない
            Assert.AreEqual(0, enc.Encode(frame, 0, 2, Bright255, true, 10_000, packets, lengths));
            // ピン 1 だけ変える → ピン 1 だけ，expectMask もピン 1 だけ
            frame.Pin(0, 1)[5] = 9;
            Assert.AreEqual(1, enc.Encode(frame, 0, 3, Bright255, true, 20_000, packets, lengths));
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
            enc.Encode(frame, 0, 1, Bright255, true, 0, packets, lengths);
            // 0.5 秒たつと，変わっていないピンを 1 本ずつ送り直す（パケットが落ちても直るように）
            int sent = 0;
            for (int i = 0; i < 3; i++)
            {
                int n = enc.Encode(frame, 0, (ushort)(2 + i), Bright255, true, 600_000 + (ulong)i * 16_000, packets, lengths);
                Assert.AreEqual(1, n);
                sent |= Array.FindIndex(lengths, l => l > 0) is int p && p >= 0 ? 1 << p : 0;
            }
            Assert.AreEqual(0b1011, sent, "3 本とも 1 回ずつ");
            Assert.AreEqual(0, enc.Encode(frame, 0, 9, Bright255, true, 700_000, packets, lengths));
        }

        [Test]
        public void EncoderSendsRawToOldFirmware()
        {
            var (enc, frame, packets, lengths) = MakeEncoder();
            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(3, enc.Encode(frame, 0, (ushort)(1 + i), Bright255, false, (ulong)i * 16_000, packets, lengths));
                Assert.AreEqual(ArteProtocol.PixelsHeaderSize + 480 * 3, lengths[0]);
                Assert.IsTrue(ArteProtocol.TryReadPixels(packets[0].AsSpan(0, lengths[0]), out var h, out _));
                Assert.AreEqual(ArteProtocol.EncodingRaw, h.Encoding);
                Assert.AreEqual(0b1011, h.ExpectMask);
            }
        }

        [Test]
        public void EncoderTreatsSameOutputAsUnchanged()
        {
            var (enc, frame, packets, lengths) = MakeEncoder();
            var dim = new DeviceConfig { BrightnessPerf = 10, BrightnessNote = 10 };
            enc.Encode(frame, 0, 1, dim, true, 0, packets, lengths);
            // 明るさ 10 では 0〜23 はどれも 0 になる → 光り方が同じなので送らない
            frame.Pin(0, 0)[0] = 20;
            Assert.AreEqual(0, enc.Encode(frame, 0, 2, dim, true, 16_000, packets, lengths));
            frame.Pin(0, 0)[0] = 200;
            Assert.AreEqual(1, enc.Encode(frame, 0, 3, dim, true, 32_000, packets, lengths));
        }
    }
}
