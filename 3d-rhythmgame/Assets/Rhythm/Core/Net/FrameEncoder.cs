using System;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 1 台分の PIXELS パケットを作る．明るさ（CONFIG の値）はここで掛け，デバイスは届いた値をそのまま光らせる．
    /// 通信量を減らすために
    /// - 前に送ったときから変わっていないピンは送らない（デバイスは前の内容を表示し続ける）
    /// - 小さくなるなら PackBits にする
    /// パケットが落ちても直るように，変わっていないピンも RefreshIntervalUs ごとに 1 本ずつ送り直す．
    /// 明るさを掛けたあとで比べるので，明るさが低くて光り方が変わらない色の変化も送らずに済む．
    /// </summary>
    public sealed class FrameEncoder
    {
        readonly byte[][] last = new byte[LedFrame.PinsPerDevice][];
        readonly ulong[] lastSentUs = new ulong[LedFrame.PinsPerDevice];
        readonly bool[] valid = new bool[LedFrame.PinsPerDevice];
        readonly byte[] work = new byte[ArteProtocol.MaxPacket];

        /// <summary>変わっていないピンを送り直す間隔．</summary>
        public ulong RefreshIntervalUs = 500_000;

        /// <summary>これまでに作ったパケットの合計（ヘッダ込み）と，そのままなら何バイトだったか．</summary>
        public long BytesSent { get; private set; }
        public long BytesRaw { get; private set; }

        /// <summary>次の Encode で全ピンを送る（接続し直したとき）．</summary>
        public void Invalidate() => Array.Clear(valid, 0, valid.Length);

        /// <summary>
        /// device の分を packets[pin] に書き，lengths[pin] にバイト数を入れる（送らないピンは 0）．送るパケットの数を返す．
        /// packets[pin] は ArteProtocol.MaxPacket バイト以上．compact が false なら毎回全ピンをそのまま送る（比較用）．
        /// </summary>
        public int Encode(LedFrame frame, int device, ushort frameNo, in DeviceConfig config, bool compact, ulong nowUs,
                          byte[][] packets, int[] lengths)
        {
            int mask = 0;
            int refreshPin = -1;
            ulong oldest = ulong.MaxValue;
            for (int pin = 0; pin < LedFrame.PinsPerDevice; pin++)
            {
                lengths[pin] = 0;
                int count = frame.LedCount(device, pin);
                if (count == 0) continue;
                byte brightness = pin == frame.NotePin ? config.BrightnessNote : config.BrightnessPerf;
                var cur = work.AsSpan(0, count * 3);
                PixelCodec.ApplyBrightness(frame.Pin(device, pin), cur, brightness);
                if (last[pin] == null || last[pin].Length != cur.Length)
                {
                    last[pin] = new byte[cur.Length];
                    valid[pin] = false;
                }
                if (!compact || !valid[pin] || !cur.SequenceEqual(last[pin]))
                {
                    cur.CopyTo(last[pin]);
                    valid[pin] = true;
                    mask |= 1 << pin;
                }
                else if (nowUs - lastSentUs[pin] >= RefreshIntervalUs && lastSentUs[pin] < oldest)
                {
                    oldest = lastSentUs[pin];
                    refreshPin = pin;
                }
            }
            if (refreshPin >= 0) mask |= 1 << refreshPin;

            int packetCount = 0;
            for (int pin = 0; pin < LedFrame.PinsPerDevice; pin++)
            {
                if ((mask & (1 << pin)) == 0) continue;
                int count = frame.LedCount(device, pin);
                ReadOnlySpan<byte> rgb = last[pin];
                var dest = packets[pin].AsSpan();
                int body = -1;
                byte encoding = ArteProtocol.EncodingRaw;
                if (compact)
                {
                    // そのままより小さくなるときだけ PackBits にする
                    body = PixelCodec.EncodePackBits(rgb, dest.Slice(ArteProtocol.PixelsHeaderSize, count * 3 - 1));
                    if (body >= 0) encoding = ArteProtocol.EncodingPackBits;
                }
                if (body < 0)
                {
                    rgb.CopyTo(dest.Slice(ArteProtocol.PixelsHeaderSize));
                    body = count * 3;
                }
                ArteProtocol.WritePixelsHeader(dest, new PixelsHeader((byte)device, (byte)pin, frameNo, (byte)mask, 0, (ushort)count, encoding));
                lengths[pin] = ArteProtocol.PixelsHeaderSize + body;
                lastSentUs[pin] = nowUs;
                BytesSent += lengths[pin];
                packetCount++;
            }
            for (int pin = 0; pin < LedFrame.PinsPerDevice; pin++)
            {
                int count = frame.LedCount(device, pin);
                if (count > 0) BytesRaw += ArteProtocol.PixelsHeaderSize + count * 3;
            }
            return packetCount;
        }
    }
}
