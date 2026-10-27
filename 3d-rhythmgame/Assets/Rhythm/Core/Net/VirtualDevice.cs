using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// ファームの FrameAssembler と同じ規則（揃ったら表示，揃う前に次が来たら捨てる，holdMs 待ったら今あるもので表示）．
    /// </summary>
    public sealed class FrameAssembler
    {
        public enum Result { Ignore, Accept, Complete }

        const short RestartGap = 1000;

        bool hasFrame, shown;
        byte expect, received;
        ulong startUs;

        public ushort Frame { get; private set; }
        public uint FramesShown { get; private set; }
        public uint FramesForced { get; private set; }
        public uint FramesDropped { get; private set; }

        public Result OnPart(ushort frame, int pin, byte expectMask, ulong nowUs)
        {
            if (!hasFrame || IsNewer(frame))
            {
                if (hasFrame && !shown) FramesDropped++;
                hasFrame = true;
                Frame = frame;
                expect = expectMask;
                received = 0;
                startUs = nowUs;
                shown = false;
            }
            else if (frame != Frame)
            {
                return Result.Ignore;
            }
            received |= (byte)(1 << pin);
            return !shown && (received & expect) == expect ? Result.Complete : Result.Accept;
        }

        public bool Overdue(ulong nowUs, ushort holdMs) => hasFrame && !shown && nowUs - startUs >= holdMs * 1000UL;

        public void MarkShown(bool forced)
        {
            shown = true;
            if (forced) FramesForced++;
            else FramesShown++;
        }

        public void Reset() => hasFrame = false;

        bool IsNewer(ushort frame)
        {
            short d = (short)(ushort)(frame - Frame);
            return d > 0 || d < -RestartGap;
        }
    }

    /// <summary>
    /// ESP32 の代わりに PC 上で動くデバイス（同じプロトコルを話す）．
    /// テストと，Viewer（P6）で「本番と同じパケットを受け取って描く」ために使う．
    /// 時刻は呼び出し側が渡す（テストで時計を進められるように）．スレッドは持たないので Poll を呼び続ける．
    /// </summary>
    public sealed class VirtualDevice : IDisposable
    {
        readonly Socket socket;
        readonly IPEndPoint hostHelloTarget;
        readonly byte[] rx = new byte[ArteProtocol.MaxPacket + 1];
        readonly byte[] tx = new byte[ArteProtocol.MaxPacket];
        readonly byte[][] working, shown;
        readonly byte[] decoded;
        readonly List<TouchEvent> log = new List<TouchEvent>();
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        IPEndPoint host;
        ushort nextSeq;
        byte touchState;
        ushort lastShownFrame;
        ulong lastShowUs;

        public byte Id { get; }
        public MacAddress Mac { get; }

        public bool Registered => host != null;
        public uint Session { get; private set; }
        public DeviceConfig Config { get; private set; }
        public FrameAssembler Frames { get; } = new FrameAssembler();
        public int Port => ((IPEndPoint)socket.LocalEndPoint).Port;

        /// <summary>表示が更新された（引数はフレーム番号）．</summary>
        public event Action<ushort> Shown;

        /// <param name="hostHelloTarget">HELLO の送り先（実機はブロードキャスト．テストでは 127.0.0.1:ホストのポート）．</param>
        public VirtualDevice(byte id, MacAddress mac, IPEndPoint hostHelloTarget, int ledsPerPin = 480, int port = 0)
        {
            Id = id;
            Mac = mac;
            this.hostHelloTarget = hostHelloTarget;
            working = new byte[LedFrame.PinsPerDevice][];
            shown = new byte[LedFrame.PinsPerDevice][];
            for (int i = 0; i < working.Length; i++)
            {
                working[i] = new byte[ledsPerPin * 3];
                shown[i] = new byte[ledsPerPin * 3];
            }
            decoded = new byte[ledsPerPin * 3];
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, Blocking = false };
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }

        public void Dispose() => socket.Close();

        /// <summary>表示中の RGB（PC が明るさを掛けた後の，LED に出る値）．</summary>
        public ReadOnlySpan<byte> ShownPin(int pin) => shown[pin];

        public void SendHello()
        {
            var h = new Hello { Device = Id, Firmware = 0x0202, Mac = Mac, Pins = 4, LedsPerPin = (ushort)(working[0].Length / 3), TouchCount = 5, Session = Session };
            Send(ArteProtocol.WriteHello(tx, h), hostHelloTarget);
        }

        /// <summary>センサーの変化を記録して，すぐに TOUCH を送る．</summary>
        public void Touch(int sensor, bool down, ulong timeUs, ulong nowUs)
        {
            log.Add(new TouchEvent(nextSeq++, (byte)sensor, down, timeUs));
            if (log.Count > ArteProtocol.TouchMaxEvents) log.RemoveAt(0);
            if (down) touchState |= (byte)(1 << sensor);
            else touchState &= (byte)~(1 << sensor);
            SendTouch(nowUs);
        }

        public void SendTouch(ulong nowUs)
        {
            if (host == null) return;
            var h = new TouchHeader { Device = Id, State = touchState, DeviceTimeUs = nowUs };
            Send(ArteProtocol.WriteTouch(tx, h, log.ToArray()), host);
        }

        /// <summary>届いているパケットを全部処理し，揃わないフレームの期限も見る．</summary>
        public void Poll(ulong nowUs)
        {
            while (true)
            {
                int len;
                try { len = socket.ReceiveFrom(rx, ref remote); }
                catch (SocketException) { break; }
                Handle(new ReadOnlySpan<byte>(rx, 0, len), (IPEndPoint)remote, nowUs);
            }
            if (Frames.Overdue(nowUs, Config.HoldMs)) Show(nowUs, true);
        }

        void Handle(ReadOnlySpan<byte> p, IPEndPoint from, ulong nowUs)
        {
            switch (ArteProtocol.TypeOf(p))
            {
                case PacketType.Welcome:
                    host = new IPEndPoint(from.Address, from.Port);
                    Session = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(4));
                    break;
                case PacketType.Ping when host != null:
                {
                    uint seq = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(4));
                    ulong hostUs = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(p.Slice(8));
                    var m = new Pong { Device = Id, Seq = seq, HostTimeUs = hostUs, DeviceTimeUs = nowUs, LastFrame = lastShownFrame, LastShowUs = lastShowUs };
                    Send(ArteProtocol.WritePong(tx, m), host);
                    break;
                }
                case PacketType.Config when ArteProtocol.TryReadConfig(p, out var c):
                    if (c.Target == ArteProtocol.AllDevices || c.Target == Id) Config = c;
                    break;
                case PacketType.Pixels when host != null && ArteProtocol.TryReadPixels(p, out var h, out var payload):
                {
                    if (h.Device != Id || h.Pin >= LedFrame.PinsPerDevice || (h.Start + h.Count) * 3 > working[h.Pin].Length) break;
                    var rgb = payload;
                    if (h.Encoding == ArteProtocol.EncodingPackBits)
                    {
                        if (!PixelCodec.DecodePackBits(payload, decoded, h.Count)) break;
                        rgb = decoded.AsSpan(0, h.Count * 3);
                    }
                    var r = Frames.OnPart(h.Frame, h.Pin, h.ExpectMask, nowUs);
                    if (r == FrameAssembler.Result.Ignore) break;
                    rgb.CopyTo(working[h.Pin].AsSpan(h.Start * 3));
                    if (r == FrameAssembler.Result.Complete) Show(nowUs, false);
                    break;
                }
            }
        }

        void Show(ulong nowUs, bool forced)
        {
            for (int i = 0; i < working.Length; i++) Buffer.BlockCopy(working[i], 0, shown[i], 0, working[i].Length);
            Frames.MarkShown(forced);
            lastShownFrame = Frames.Frame;
            lastShowUs = nowUs;
            Shown?.Invoke(Frames.Frame);
        }

        void Send(int len, IPEndPoint to)
        {
            try { socket.SendTo(tx, 0, len, SocketFlags.None, to); }
            catch (SocketException) { }
        }
    }
}
