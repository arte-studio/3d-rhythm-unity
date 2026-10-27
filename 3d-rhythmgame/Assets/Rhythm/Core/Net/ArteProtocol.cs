using System;
using System.Buffers.Binary;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// PC ⇔ ESP32-S3 の通信プロトコル v2．ファーム側の同じ定義は
    /// 3d-rhythm-lowlayer-program/tape_led_module_s3/lib/arte_link/ArteProtocol.h，仕様書は同リポジトリの docs/protocol-v2.md．
    /// 両者で同じバイト列になることを ProtocolTests とファームの test_link で確かめている．
    /// すべてリトルエンディアン．先頭 4 バイトは 'A' 'R' バージョン 種類．
    /// </summary>
    public static class ArteProtocol
    {
        public const byte Magic0 = (byte)'A';
        public const byte Magic1 = (byte)'R';
        public const byte Version = 2;
        public const int HeaderSize = 4;
        public const int MaxPacket = 1472;

        public const byte AllDevices = 0xFF;
        public const byte UnassignedDevice = 0xFF;

        public const byte FlagTouchFeedback = 0x01;
        public const byte FlagSelfTest = 0x02;

        /// <summary>PIXELS の encoding．0 = RGB そのまま，1 = PackBits（PixelCodec）．</summary>
        public const byte EncodingRaw = 0;
        public const byte EncodingPackBits = 1;

        public const int WelcomeSize = HeaderSize + 4;
        public const int PingSize = HeaderSize + 12;
        public const int PixelsHeaderSize = HeaderSize + 10;
        public const int ConfigSize = HeaderSize + 8;
        public const int SetIdSize = HeaderSize + 7;
        public const int HelloSize = HeaderSize + 17;
        public const int PongSize = HeaderSize + 32;
        public const int TouchHeaderSize = HeaderSize + 12;
        public const int TouchEventSize = 12;
        public const int TouchMaxEvents = 8;
        public const int StatusSize = HeaderSize + 36;

        public static int WriteHeader(Span<byte> p, PacketType type)
        {
            p[0] = Magic0;
            p[1] = Magic1;
            p[2] = Version;
            p[3] = (byte)type;
            return HeaderSize;
        }

        /// <summary>ヘッダが正しければ種類，違えば 0．</summary>
        public static PacketType TypeOf(ReadOnlySpan<byte> p) =>
            p.Length >= HeaderSize && p[0] == Magic0 && p[1] == Magic1 && p[2] == Version ? (PacketType)p[3] : 0;

        // ---- PC → デバイス ----

        public static int WriteWelcome(Span<byte> p, uint session)
        {
            WriteHeader(p, PacketType.Welcome);
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(4), session);
            return WelcomeSize;
        }

        public static int WritePing(Span<byte> p, uint seq, ulong hostTimeUs)
        {
            WriteHeader(p, PacketType.Ping);
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(4), seq);
            BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(8), hostTimeUs);
            return PingSize;
        }

        /// <summary>ピクセルパケットのヘッダを書く．中身（encoding 0 なら count*3 バイトの RGB）は続けて呼び出し側が書く．</summary>
        public static int WritePixelsHeader(Span<byte> p, in PixelsHeader h)
        {
            WriteHeader(p, PacketType.Pixels);
            p[4] = h.Device;
            p[5] = h.Pin;
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(6), h.Frame);
            p[8] = h.ExpectMask;
            p[9] = h.Encoding;
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(10), h.Start);
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(12), h.Count);
            return PixelsHeaderSize;
        }

        public static int WriteConfig(Span<byte> p, in DeviceConfig c)
        {
            WriteHeader(p, PacketType.Config);
            p[4] = c.Target;
            p[5] = c.BrightnessPerf;
            p[6] = c.BrightnessNote;
            p[7] = c.Flags;
            p[8] = c.DebounceMs;
            p[9] = c.HeartbeatMs;
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(10), c.HoldMs);
            return ConfigSize;
        }

        public static int WriteSetId(Span<byte> p, ReadOnlySpan<byte> mac, byte newId)
        {
            WriteHeader(p, PacketType.SetId);
            mac.Slice(0, 6).CopyTo(p.Slice(4));
            p[10] = newId;
            return SetIdSize;
        }

        /// <summary>payload は encoding 0 なら count*3 バイトの RGB，1 なら残り全部（PixelCodec.DecodePackBits で戻す）．</summary>
        public static bool TryReadPixels(ReadOnlySpan<byte> p, out PixelsHeader h, out ReadOnlySpan<byte> payload)
        {
            h = default;
            payload = default;
            if (TypeOf(p) != PacketType.Pixels || p.Length < PixelsHeaderSize) return false;
            h = new PixelsHeader(p[4], p[5], BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(6)), p[8],
                BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(10)), BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(12)), p[9]);
            switch (h.Encoding)
            {
                case EncodingRaw:
                    if (p.Length < PixelsHeaderSize + h.Count * 3) return false;
                    payload = p.Slice(PixelsHeaderSize, h.Count * 3);
                    return true;
                case EncodingPackBits:
                    payload = p.Slice(PixelsHeaderSize);
                    return true;
                default:
                    return false;
            }
        }

        public static bool TryReadConfig(ReadOnlySpan<byte> p, out DeviceConfig c)
        {
            c = default;
            if (TypeOf(p) != PacketType.Config || p.Length < ConfigSize) return false;
            c = new DeviceConfig
            {
                Target = p[4], BrightnessPerf = p[5], BrightnessNote = p[6], Flags = p[7],
                DebounceMs = p[8], HeartbeatMs = p[9], HoldMs = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(10)),
            };
            return true;
        }

        // ---- デバイス → PC ----

        public static bool TryReadHello(ReadOnlySpan<byte> p, out Hello h)
        {
            h = default;
            if (TypeOf(p) != PacketType.Hello || p.Length < HelloSize) return false;
            h = new Hello
            {
                Device = p[4],
                Firmware = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(5)),
                Mac = MacAddress.From(p.Slice(7, 6)),
                Pins = p[13],
                LedsPerPin = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(14)),
                TouchCount = p[16],
                Session = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(17)),
            };
            return true;
        }

        public static bool TryReadPong(ReadOnlySpan<byte> p, out Pong m)
        {
            m = default;
            if (TypeOf(p) != PacketType.Pong || p.Length < PongSize) return false;
            m = new Pong
            {
                Device = p[4],
                Seq = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(6)),
                HostTimeUs = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(10)),
                DeviceTimeUs = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(18)),
                LastFrame = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(26)),
                LastShowUs = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(28)),
            };
            return true;
        }

        /// <summary>タッチパケットを読む．events には古い順に入る（長さ TouchMaxEvents 以上を渡す）．</summary>
        public static bool TryReadTouch(ReadOnlySpan<byte> p, out TouchHeader h, Span<TouchEvent> events)
        {
            h = default;
            if (TypeOf(p) != PacketType.Touch || p.Length < TouchHeaderSize) return false;
            h = new TouchHeader
            {
                Device = p[4], State = p[5], Count = p[6],
                DeviceTimeUs = BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(8)),
            };
            if (h.Count > TouchMaxEvents || h.Count > events.Length || p.Length < TouchHeaderSize + h.Count * TouchEventSize) return false;
            for (int i = 0; i < h.Count; i++)
            {
                var e = p.Slice(TouchHeaderSize + i * TouchEventSize);
                events[i] = new TouchEvent(BinaryPrimitives.ReadUInt16LittleEndian(e), e[2], e[3] != 0,
                    BinaryPrimitives.ReadUInt64LittleEndian(e.Slice(4)));
            }
            return true;
        }

        public static bool TryReadStatus(ReadOnlySpan<byte> p, out DeviceStatus s)
        {
            s = default;
            if (TypeOf(p) != PacketType.Status || p.Length < StatusSize) return false;
            s = new DeviceStatus
            {
                Device = p[4], Flags = p[5], BrightnessPerf = p[6], BrightnessNote = p[7],
                UptimeMs = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(8)),
                FramesShown = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(12)),
                FramesForced = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(16)),
                FramesDropped = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(20)),
                PacketsRx = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(24)),
                PacketsBad = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(28)),
                Rssi = (sbyte)p[32],
                DebounceMs = p[33],
                ShowUs = BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(34)),
                FreeHeap = BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(36)),
            };
            return true;
        }

        // ---- デバイス側の書き出し（仮想デバイスとテスト用）----

        public static int WriteHello(Span<byte> p, in Hello h)
        {
            WriteHeader(p, PacketType.Hello);
            p[4] = h.Device;
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(5), h.Firmware);
            h.Mac.CopyTo(p.Slice(7, 6));
            p[13] = h.Pins;
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(14), h.LedsPerPin);
            p[16] = h.TouchCount;
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(17), h.Session);
            return HelloSize;
        }

        public static int WritePong(Span<byte> p, in Pong m)
        {
            WriteHeader(p, PacketType.Pong);
            p[4] = m.Device;
            p[5] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(p.Slice(6), m.Seq);
            BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(10), m.HostTimeUs);
            BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(18), m.DeviceTimeUs);
            BinaryPrimitives.WriteUInt16LittleEndian(p.Slice(26), m.LastFrame);
            BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(28), m.LastShowUs);
            return PongSize;
        }

        public static int WriteTouch(Span<byte> p, in TouchHeader h, ReadOnlySpan<TouchEvent> events)
        {
            WriteHeader(p, PacketType.Touch);
            p[4] = h.Device;
            p[5] = h.State;
            p[6] = (byte)events.Length;
            p[7] = 0;
            BinaryPrimitives.WriteUInt64LittleEndian(p.Slice(8), h.DeviceTimeUs);
            for (int i = 0; i < events.Length; i++)
            {
                var e = p.Slice(TouchHeaderSize + i * TouchEventSize);
                BinaryPrimitives.WriteUInt16LittleEndian(e, events[i].Seq);
                e[2] = events[i].Sensor;
                e[3] = (byte)(events[i].Down ? 1 : 0);
                BinaryPrimitives.WriteUInt64LittleEndian(e.Slice(4), events[i].TimeUs);
            }
            return TouchHeaderSize + events.Length * TouchEventSize;
        }
    }

    public enum PacketType : byte
    {
        Welcome = 0x01,
        Ping = 0x02,
        Pixels = 0x10,
        Config = 0x20,
        SetId = 0x21,
        Hello = 0x81,
        Pong = 0x82,
        Touch = 0x90,
        Status = 0xA0,
    }

    public readonly struct PixelsHeader
    {
        public readonly byte Device;
        /// <summary>0〜2 = 演出用，3 = ノーツ用．</summary>
        public readonly byte Pin;
        public readonly ushort Frame;
        /// <summary>このフレームで送るピンのビット集合．デバイスは揃ったら表示する．</summary>
        public readonly byte ExpectMask;
        public readonly ushort Start;
        /// <summary>LED 数（PackBits でも戻したあとの数）．</summary>
        public readonly ushort Count;
        /// <summary>ArteProtocol.EncodingRaw / EncodingPackBits．</summary>
        public readonly byte Encoding;

        public PixelsHeader(byte device, byte pin, ushort frame, byte expectMask, ushort start, ushort count, byte encoding = ArteProtocol.EncodingRaw)
        {
            Device = device;
            Pin = pin;
            Frame = frame;
            ExpectMask = expectMask;
            Start = start;
            Count = count;
            Encoding = encoding;
        }
    }

    public struct DeviceConfig
    {
        public byte Target;
        public byte BrightnessPerf;
        public byte BrightnessNote;
        public byte Flags;
        public byte DebounceMs;
        public byte HeartbeatMs;
        public ushort HoldMs;
    }

    public readonly struct MacAddress : IEquatable<MacAddress>
    {
        readonly ulong value;

        MacAddress(ulong value) => this.value = value;

        public static MacAddress From(ReadOnlySpan<byte> b)
        {
            ulong v = 0;
            for (int i = 0; i < 6; i++) v = (v << 8) | b[i];
            return new MacAddress(v);
        }

        public void CopyTo(Span<byte> dest)
        {
            for (int i = 0; i < 6; i++) dest[i] = (byte)(value >> (8 * (5 - i)));
        }

        public bool Equals(MacAddress other) => value == other.value;
        public override bool Equals(object obj) => obj is MacAddress m && Equals(m);
        public override int GetHashCode() => value.GetHashCode();

        public override string ToString()
        {
            Span<byte> b = stackalloc byte[6];
            CopyTo(b);
            return $"{b[0]:X2}:{b[1]:X2}:{b[2]:X2}:{b[3]:X2}:{b[4]:X2}:{b[5]:X2}";
        }
    }

    public struct Hello
    {
        public byte Device;
        public ushort Firmware;
        public MacAddress Mac;
        public byte Pins;
        public ushort LedsPerPin;
        public byte TouchCount;
        public uint Session;
    }

    public struct Pong
    {
        public byte Device;
        public uint Seq;
        public ulong HostTimeUs;
        public ulong DeviceTimeUs;
        public ushort LastFrame;
        public ulong LastShowUs;
    }

    public readonly struct TouchEvent
    {
        public readonly ushort Seq;
        public readonly byte Sensor;
        public readonly bool Down;
        public readonly ulong TimeUs;

        public TouchEvent(ushort seq, byte sensor, bool down, ulong timeUs)
        {
            Seq = seq;
            Sensor = sensor;
            Down = down;
            TimeUs = timeUs;
        }
    }

    public struct TouchHeader
    {
        public byte Device;
        public byte State;
        public byte Count;
        public ulong DeviceTimeUs;
    }

    public struct DeviceStatus
    {
        public byte Device;
        public byte Flags;
        public byte BrightnessPerf;
        public byte BrightnessNote;
        public uint UptimeMs;
        public uint FramesShown;
        public uint FramesForced;
        public uint FramesDropped;
        public uint PacketsRx;
        public uint PacketsBad;
        public sbyte Rssi;
        public byte DebounceMs;
        public ushort ShowUs;
        public uint FreeHeap;
    }
}
