using System;
using Arte.Rhythm.Hardware;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// 1 フレーム分の送信データ（デバイス × ピン × LED の RGB，配線順）．
    /// 演出の描画（P5）とノーツの描画がここに書き，DeviceLink がピンごとに 1 パケットにして送る．
    /// どのピンに何個送るかはハード構成から決める（デバイス 7 のように本数が少ないピンは短く，外したピンは送らない）．
    /// </summary>
    public sealed class LedFrame
    {
        public const int PinsPerDevice = 4;

        readonly byte[][] data;     // [device * 4 + pin] = RGB
        readonly ushort[] counts;   // 送る LED 数（0 なら送らない）

        public int DeviceCount { get; }
        public int NotePin { get; }

        public LedFrame(int deviceCount, ushort[] ledCounts, int notePin)
        {
            if (ledCounts.Length != deviceCount * PinsPerDevice) throw new ArgumentException("ledCounts の長さ");
            DeviceCount = deviceCount;
            NotePin = notePin;
            counts = (ushort[])ledCounts.Clone();
            data = new byte[ledCounts.Length][];
            for (int i = 0; i < data.Length; i++) data[i] = new byte[counts[i] * 3];
        }

        /// <summary>ハード構成と設営から作る．</summary>
        public static LedFrame FromLayout(PerfLayout perf, NoteLayout notes, int deviceCount)
        {
            var counts = new ushort[deviceCount * PinsPerDevice];
            for (int c = 0; c < perf.ColumnCount; c++)
            {
                var s = perf.GetStrip(c);
                if (!s.Installed || s.Address.device >= deviceCount) continue;
                int i = s.Address.device * PinsPerDevice + s.Address.pin;
                counts[i] = (ushort)Math.Max(counts[i], (s.Address.slot + 1) * perf.LedsPerStrip);
            }
            for (int d = 0; d < deviceCount; d++) counts[d * PinsPerDevice + notes.NoteOutputPin] = (ushort)notes.NotePacketLeds;
            return new LedFrame(deviceCount, counts, notes.NoteOutputPin);
        }

        /// <summary>このピンの RGB（配線順，長さ = LED 数 × 3）．送らないピンは空．</summary>
        public Span<byte> Pin(int device, int pin) => data[device * PinsPerDevice + pin];

        public int LedCount(int device, int pin) => counts[device * PinsPerDevice + pin];

        /// <summary>このデバイスに送るピンのビット集合（PIXELS の expectMask）．</summary>
        public byte PinMask(int device)
        {
            int mask = 0;
            for (int p = 0; p < PinsPerDevice; p++)
                if (counts[device * PinsPerDevice + p] > 0) mask |= 1 << p;
            return (byte)mask;
        }

        public void Clear()
        {
            foreach (var d in data) Array.Clear(d, 0, d.Length);
        }

        /// <summary>1 ピン分の PIXELS パケットを dest に書き，長さを返す．送らないピンなら 0．</summary>
        public int WritePacket(Span<byte> dest, int device, int pin, ushort frame)
        {
            int count = LedCount(device, pin);
            if (count == 0) return 0;
            var h = new PixelsHeader((byte)device, (byte)pin, frame, PinMask(device), 0, (ushort)count);
            int n = ArteProtocol.WritePixelsHeader(dest, h);
            Pin(device, pin).CopyTo(dest.Slice(n));
            return n + count * 3;
        }
    }
}
