using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Arte.Rhythm.Net
{
    /// <summary>PC 側の通信設定．</summary>
    public sealed class LinkOptions
    {
        public int HostPort = 9999;
        public int DeviceCount = 8;
        /// <summary>デバイスに送る設定（Target は無視して全台に送る）．</summary>
        public DeviceConfig Config = new DeviceConfig
        {
            BrightnessPerf = 50, BrightnessNote = 50, Flags = ArteProtocol.FlagTouchFeedback,
            DebounceMs = 5, HeartbeatMs = 50, HoldMs = 50,
        };
        /// <summary>時刻合わせに使う（AdaptiveRate のときは混み具合を見るため 0.05 秒以下にする）．</summary>
        public double PingIntervalSec = 0.25;
        public double ConfigIntervalSec = 1.0;
        /// <summary>色を LED に送る値にする曲線（SetupProfile.outputCurve）．変えると次のフレームから反映される．</summary>
        public Hardware.LedOutputModel.OutputCurve Curve = Hardware.LedOutputModel.OutputCurve.Srgb;
        /// <summary>差分と PackBits で通信量を減らす（false は比較用．毎回全ピンをそのまま送る）．</summary>
        public bool Compact = true;
        /// <summary>
        /// 混み具合を見て台ごとに送る頻度を下げる．Wi-Fi が詰まるとルーターに数秒分たまって遅れるので，
        /// 往復時間が CongestionRttMs を超えるか PING の返事が PingTimeoutMs 来なければ頻度を下げ，空いていれば少しずつ戻す．
        /// 本番は強いルーターを使うので既定では切っておく（家庭用ルーターで試すとき用）．
        /// </summary>
        public bool AdaptiveRate = false;
        public double MaxSendFps = 60;
        public double MinSendFps = 5;
        public double CongestionRttMs = 50;
        public double PingTimeoutMs = 200;
        /// <summary>
        /// 実機に送るのと同じ PIXELS を，つながっていないデバイスの分も含めて送る先（別の PC の Viewer．MirrorReceiver）．
        /// 実機が無くても Viewer で見られる．
        /// </summary>
        public List<IPEndPoint> MirrorTargets = new List<IPEndPoint>();
        /// <summary>この間何も届かなければ切断とみなす．</summary>
        public double DeviceTimeoutSec = 3.0;
    }

    /// <summary>タッチの変化 1 回分．時刻は PC の時刻（LinkHost に渡した時計，マイクロ秒）．</summary>
    public readonly struct DeviceTouch
    {
        public readonly int Device;
        public readonly int Sensor;
        public readonly bool Down;
        public readonly ulong HostTimeUs;
        /// <summary>時刻合わせが済んでいたか（済んでいなければ届いた時刻から逆算した値）．</summary>
        public readonly bool Synced;

        public DeviceTouch(int device, int sensor, bool down, ulong hostTimeUs, bool synced)
        {
            Device = device;
            Sensor = sensor;
            Down = down;
            HostTimeUs = hostTimeUs;
            Synced = synced;
        }
    }

    /// <summary>1 台分の状態（表示用のコピー）．</summary>
    public sealed class DeviceInfo
    {
        public int Id;
        public MacAddress Mac;
        public IPEndPoint EndPoint;
        public ushort Firmware;
        public bool Connected;
        public double SecondsSinceSeen;
        public bool HasStatus;
        public DeviceStatus Status;
        public byte TouchState;
        public bool Synced;
        public long ClockOffsetUs;
        /// <summary>時刻合わせに使った往復時間（誤差は最大でこの半分）．</summary>
        public long RttUs;
        public long LastRttUs;
        /// <summary>送信してから LED の表示が始まるまで（平均）．NaN なら未計測．</summary>
        public double DisplayLatencyMs = double.NaN;
        /// <summary>同じ番号の別の基板が見つかった．</summary>
        public bool IdConflict;
        /// <summary>今このデバイスに送ってよい頻度（AdaptiveRate でなければ NaN）．</summary>
        public double SendFps;
        /// <summary>直近 1 秒に送ったピクセルデータ（kbit/s）．</summary>
        public double SentKbps;
        /// <summary>送ったバイト数 ÷ 毎回全ピンをそのまま送った場合のバイト数（これまでの合計）．</summary>
        public double CompressionRatio = double.NaN;
        /// <summary>混んでいると判断して頻度を下げた回数．</summary>
        public int CongestionCount;
    }

    /// <summary>
    /// PC 側の通信（Unity 非依存）．デバイスの発見・時刻合わせ・タッチの受信・LED データの送信をする．
    /// 受信はバックグラウンドスレッドで行い，Touched もそのスレッドから呼ぶ（GameSession.Touch はスレッドセーフ）．
    /// Update と SendFrame はメインスレッドから呼ぶ．
    /// </summary>
    public sealed class LinkHost : IDisposable
    {
        const int SendTimeRing = 1024;

        readonly LinkOptions options;
        readonly Func<ulong> clockUs;
        readonly object gate = new object();
        readonly Device[] devices;
        readonly Dictionary<MacAddress, (DeviceInfo info, ulong seenUs)> unassigned = new Dictionary<MacAddress, (DeviceInfo, ulong)>();
        readonly byte[] sendBuf = new byte[ArteProtocol.MaxPacket];
        readonly byte[] replyBuf = new byte[ArteProtocol.MaxPacket];
        readonly ushort[] sentFrame = new ushort[SendTimeRing];
        readonly ulong[] sentUs = new ulong[SendTimeRing];
        readonly byte[][] packets = new byte[LedFrame.PinsPerDevice][];
        readonly int[] packetLengths = new int[LedFrame.PinsPerDevice];
        FrameEncoder[] mirrorEncoders;

        Socket socket;
        Thread thread;
        volatile bool running;
        ushort frame;
        uint pingSeq;
        ulong lastPingUs, lastConfigUs;

        /// <summary>タッチが変化した（受信スレッドから呼ばれる）．</summary>
        public event Action<DeviceTouch> Touched;

        /// <summary>デバイスが見つかった・番号が変わったなど（受信スレッドから呼ばれる．ログ用）．</summary>
        public event Action<string> Log;

        public uint Session { get; }
        /// <summary>送る設定．Config を変えると次の送信（1 秒以内）から反映される．</summary>
        public LinkOptions Options => options;
        public int LocalPort => socket != null ? ((IPEndPoint)socket.LocalEndPoint).Port : 0;
        public ushort LastFrame => frame;

        sealed class Device
        {
            public readonly ClockSync Clock = new ClockSync();
            public readonly TouchEventTracker Tracker = new TouchEventTracker();
            public IPEndPoint EndPoint;
            public MacAddress Mac;
            public ushort Firmware;
            public ulong LastSeenUs;
            public bool HasStatus;
            public DeviceStatus Status;
            public byte TouchState;
            public double LatencyEmaUs = double.NaN;
            public bool IdConflict;

            // 送信（メインスレッドだけが触る）
            public readonly FrameEncoder Encoder = new FrameEncoder();
            public ulong LastSendUs;
            public ulong RateWindowUs;
            public long RateWindowBytes;
            public double SentKbps;

            // 混み具合など（gate の中で触る）
            public bool NeedsFullFrame = true;
            public double AllowedFps;
            public ulong LastCutUs;
            public uint PendingPingSeq;
            public ulong PendingPingUs;
            public int CongestionCount;
        }

        public LinkHost(LinkOptions options, Func<ulong> clockUs)
        {
            this.options = options;
            this.clockUs = clockUs;
            devices = new Device[options.DeviceCount];
            for (int i = 0; i < packets.Length; i++) packets[i] = new byte[ArteProtocol.MaxPacket];
            Session = (uint)new Random().Next(1, int.MaxValue);
        }

        public void Start()
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.EnableBroadcast = true;
            socket.ReceiveTimeout = 200;
            socket.Bind(new IPEndPoint(IPAddress.Any, options.HostPort));
            running = true;
            thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "Arte LinkHost" };
            thread.Start();
        }

        public void Dispose()
        {
            running = false;
            socket?.Close();
            thread?.Join(500);
            socket = null;
        }

        bool IsConnected(Device d, ulong now) =>
            d != null && d.EndPoint != null && now - d.LastSeenUs < (ulong)(options.DeviceTimeoutSec * 1e6);

        // ---- メインスレッド ----

        /// <summary>PING と設定を必要に応じて送る．毎フレーム呼ぶ．</summary>
        public void Update()
        {
            ulong now = clockUs();
            double pingInterval = options.AdaptiveRate ? Math.Min(options.PingIntervalSec, 0.05) : options.PingIntervalSec;
            if (now - lastPingUs >= (ulong)(pingInterval * 1e6))
            {
                lastPingUs = now;
                pingSeq++;
                for (int id = 0; id < devices.Length; id++)
                {
                    IPEndPoint ep;
                    lock (gate)
                    {
                        var d = devices[id];
                        if (!IsConnected(d, now)) continue;
                        ep = d.EndPoint;
                        // 返事の来ていない一番古い PING が期限を過ぎたら混んでいるとみなす
                        if (options.AdaptiveRate && d.PendingPingUs != 0 && now - d.PendingPingUs >= (ulong)(options.PingTimeoutMs * 1000))
                        {
                            Congested(d, now);
                            d.PendingPingUs = 0;
                        }
                        if (d.PendingPingUs == 0)
                        {
                            d.PendingPingUs = now;
                            d.PendingPingSeq = pingSeq;
                        }
                    }
                    int n = ArteProtocol.WritePing(sendBuf, pingSeq, clockUs());
                    SendTo(sendBuf, n, ep);
                }
            }
            if (now - lastConfigUs >= (ulong)(options.ConfigIntervalSec * 1e6))
            {
                lastConfigUs = now;
                var c = options.Config;
                c.Target = ArteProtocol.AllDevices;
                int n = ArteProtocol.WriteConfig(sendBuf, c);
                ForEachConnected(now, ep => SendTo(sendBuf, n, ep));
            }
        }

        /// <summary>
        /// 1 フレーム分を，つながっている全デバイスに送る．AdaptiveRate のときは，混んでいるデバイスには
        /// 間引いて送る（間引いたフレームは送らないだけで，次に送るときは最新の内容になる）．
        /// </summary>
        public void SendFrame(LedFrame ledFrame)
        {
            frame++;
            ulong now = clockUs();
            sentFrame[frame % SendTimeRing] = frame;
            sentUs[frame % SendTimeRing] = now;
            var config = options.Config;
            for (int id = 0; id < devices.Length && id < ledFrame.DeviceCount; id++)
            {
                var d = devices[id];
                IPEndPoint ep;
                bool compact, full;
                double fps;
                lock (gate)
                {
                    if (!IsConnected(d, now)) continue;
                    ep = d.EndPoint;
                    compact = options.Compact;
                    if (d.AllowedFps <= 0) d.AllowedFps = options.MaxSendFps;
                    fps = options.AdaptiveRate ? d.AllowedFps : double.PositiveInfinity;
                    full = d.NeedsFullFrame;
                    d.NeedsFullFrame = false;
                }
                // 間隔の 1/4 の余裕を持たせる（呼び出し側の周期と少しずれても上限どおりに送れるように）
                if (!full && d.LastSendUs != 0 && now - d.LastSendUs < (ulong)(0.75e6 / fps)) continue;
                if (full) d.Encoder.Invalidate();
                d.LastSendUs = now;
                d.Encoder.Encode(ledFrame, id, frame, config, options.Curve, compact, now, packets, packetLengths);
                for (int pin = 0; pin < LedFrame.PinsPerDevice; pin++)
                {
                    if (packetLengths[pin] == 0) continue;
                    SendTo(packets[pin], packetLengths[pin], ep);
                    d.RateWindowBytes += packetLengths[pin];
                }
                if (now - d.RateWindowUs >= 1_000_000)
                {
                    if (d.RateWindowUs != 0) d.SentKbps = d.RateWindowBytes * 8 / ((now - d.RateWindowUs) / 1e3);
                    d.RateWindowUs = now;
                    d.RateWindowBytes = 0;
                }
            }
            SendMirror(ledFrame, now, config);
        }

        void SendMirror(LedFrame ledFrame, ulong now, in DeviceConfig config)
        {
            var targets = options.MirrorTargets;
            if (targets == null || targets.Count == 0) return;
            if (mirrorEncoders == null || mirrorEncoders.Length != ledFrame.DeviceCount)
            {
                mirrorEncoders = new FrameEncoder[ledFrame.DeviceCount];
                // Viewer を後から起動しても早く揃うよう，変わっていないピンも短い間隔で送り直す
                for (int i = 0; i < mirrorEncoders.Length; i++) mirrorEncoders[i] = new FrameEncoder { RefreshIntervalUs = 250_000 };
            }
            for (int id = 0; id < ledFrame.DeviceCount; id++)
            {
                mirrorEncoders[id].Encode(ledFrame, id, frame, config, options.Curve, true, now, packets, packetLengths);
                for (int pin = 0; pin < LedFrame.PinsPerDevice; pin++)
                {
                    if (packetLengths[pin] == 0) continue;
                    foreach (var ep in targets) SendTo(packets[pin], packetLengths[pin], ep);
                }
            }
        }

        // gate の中で呼ぶ
        void Congested(Device d, ulong now)
        {
            // 下げた効果が往復時間に出るまでは続けて下げない
            if (now - d.LastCutUs < 200_000) return;
            d.LastCutUs = now;
            d.CongestionCount++;
            d.AllowedFps = Math.Max(options.MinSendFps, (d.AllowedFps > 0 ? d.AllowedFps : options.MaxSendFps) * 0.6);
        }

        /// <summary>MAC で指定した基板の番号を変える（基板は保存して再起動する）．</summary>
        public void AssignId(MacAddress mac, byte newId)
        {
            IPEndPoint ep = null;
            lock (gate)
            {
                if (unassigned.TryGetValue(mac, out var u)) ep = u.info.EndPoint;
                foreach (var d in devices)
                    if (d != null && d.Mac.Equals(mac)) ep = d.EndPoint;
            }
            if (ep == null) return;
            Span<byte> m = stackalloc byte[6];
            mac.CopyTo(m);
            int n = ArteProtocol.WriteSetId(sendBuf, m, newId);
            SendTo(sendBuf, n, ep);
        }

        /// <summary>全デバイスと，番号未設定の基板の状態．</summary>
        public List<DeviceInfo> Snapshot()
        {
            ulong now = clockUs();
            var list = new List<DeviceInfo>();
            lock (gate)
            {
                for (int id = 0; id < devices.Length; id++)
                {
                    var d = devices[id];
                    if (d == null) continue;
                    list.Add(new DeviceInfo
                    {
                        Id = id, Mac = d.Mac, EndPoint = d.EndPoint, Firmware = d.Firmware,
                        Connected = IsConnected(d, now), SecondsSinceSeen = (now - d.LastSeenUs) / 1e6,
                        HasStatus = d.HasStatus, Status = d.Status, TouchState = d.TouchState,
                        Synced = d.Clock.IsSynced, ClockOffsetUs = d.Clock.OffsetUs, RttUs = d.Clock.RttUs,
                        LastRttUs = d.Clock.LastRttUs, DisplayLatencyMs = d.LatencyEmaUs / 1000.0, IdConflict = d.IdConflict,
                        SendFps = options.AdaptiveRate ? (d.AllowedFps > 0 ? d.AllowedFps : options.MaxSendFps) : double.NaN,
                        SentKbps = d.SentKbps, CongestionCount = d.CongestionCount,
                        CompressionRatio = d.Encoder.BytesRaw > 0 ? (double)d.Encoder.BytesSent / d.Encoder.BytesRaw : double.NaN,
                    });
                }
                foreach (var (info, seenUs) in unassigned.Values)
                {
                    info.SecondsSinceSeen = (now - seenUs) / 1e6;
                    info.Connected = info.SecondsSinceSeen < options.DeviceTimeoutSec;
                    list.Add(info);
                }
            }
            return list;
        }

        void ForEachConnected(ulong now, Action<IPEndPoint> send)
        {
            for (int id = 0; id < devices.Length; id++)
            {
                IPEndPoint ep;
                lock (gate)
                {
                    if (!IsConnected(devices[id], now)) continue;
                    ep = devices[id].EndPoint;
                }
                send(ep);
            }
        }

        void SendTo(byte[] buf, int len, IPEndPoint ep)
        {
            try { socket?.SendTo(buf, 0, len, SocketFlags.None, ep); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        // ---- 受信スレッド ----

        void ReceiveLoop()
        {
            var buf = new byte[ArteProtocol.MaxPacket + 1];
            var events = new TouchEvent[ArteProtocol.TouchMaxEvents];
            var fresh = new TouchEvent[ArteProtocol.TouchMaxEvents];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                int len;
                try { len = socket.ReceiveFrom(buf, ref remote); }
                catch (SocketException) { continue; }
                catch (ObjectDisposedException) { return; }
                ulong now = clockUs();
                var p = new ReadOnlySpan<byte>(buf, 0, len);
                try { Handle(p, (IPEndPoint)remote, now, events, fresh); }
                catch (Exception e) { Log?.Invoke($"受信処理の例外: {e}"); }
            }
        }

        void Handle(ReadOnlySpan<byte> p, IPEndPoint from, ulong now, TouchEvent[] events, TouchEvent[] fresh)
        {
            switch (ArteProtocol.TypeOf(p))
            {
                case PacketType.Hello when ArteProtocol.TryReadHello(p, out var h):
                    OnHello(h, from, now);
                    break;
                case PacketType.Pong when ArteProtocol.TryReadPong(p, out var m):
                    OnPong(m, now);
                    break;
                case PacketType.Touch when ArteProtocol.TryReadTouch(p, out var th, events):
                    OnTouch(th, events.AsSpan(0, th.Count), fresh, now);
                    break;
                case PacketType.Status when ArteProtocol.TryReadStatus(p, out var s):
                    lock (gate)
                    {
                        var d = Get(s.Device);
                        if (d == null) break;
                        d.Status = s;
                        d.HasStatus = true;
                        d.LastSeenUs = now;
                    }
                    break;
            }
        }

        Device Get(int id) => id >= 0 && id < devices.Length ? devices[id] : null;

        void OnHello(in Hello h, IPEndPoint from, ulong now)
        {
            string log = null;
            var ep = new IPEndPoint(from.Address, from.Port);
            lock (gate)
            {
                if (h.Device >= devices.Length)
                {
                    if (!unassigned.ContainsKey(h.Mac)) log = $"番号未設定の基板 {h.Mac}（{ep}）";
                    unassigned[h.Mac] = (new DeviceInfo { Id = h.Device, Mac = h.Mac, EndPoint = ep, Firmware = h.Firmware }, now);
                    return;
                }
                unassigned.Remove(h.Mac);
                var d = devices[h.Device] ??= new Device();
                bool known = d.EndPoint != null;
                if (known && !d.Mac.Equals(h.Mac) && IsConnected(d, now))
                {
                    d.IdConflict = true;
                    log = $"デバイス {h.Device} の番号が重複: {d.Mac} と {h.Mac}";
                }
                else if (!known || !d.EndPoint.Equals(ep) || !d.Mac.Equals(h.Mac))
                {
                    log = $"デバイス {h.Device} 接続 {ep} {h.Mac} ファーム v{h.Firmware >> 8}.{h.Firmware & 0xFF}";
                }
                d.EndPoint = ep;
                d.Mac = h.Mac;
                d.Firmware = h.Firmware;
                d.LastSeenUs = now;
                d.Clock.Reset();
                d.Tracker.Reset();
                d.NeedsFullFrame = true;
            }
            if (log != null) Log?.Invoke(log);
            int n = ArteProtocol.WriteWelcome(replyBuf, Session);
            SendTo(replyBuf, n, ep);
            var c = options.Config;
            c.Target = ArteProtocol.AllDevices;
            n = ArteProtocol.WriteConfig(replyBuf, c);
            SendTo(replyBuf, n, ep);
        }

        void OnPong(in Pong m, ulong now)
        {
            lock (gate)
            {
                var d = Get(m.Device);
                if (d == null) return;
                d.LastSeenUs = now;
                d.Clock.AddSample(m.HostTimeUs, m.DeviceTimeUs, now);
                if (d.PendingPingUs != 0 && (int)(m.Seq - d.PendingPingSeq) >= 0) d.PendingPingUs = 0;
                if (options.AdaptiveRate)
                {
                    if (now - m.HostTimeUs > (ulong)(options.CongestionRttMs * 1000)) Congested(d, now);
                    else if (d.AllowedFps > 0 && now - d.LastCutUs > 500_000)
                        // 空いていれば PONG ごとに少しずつ戻す（20 回/秒 × 0.5 = 10fps/秒）
                        d.AllowedFps = Math.Min(options.MaxSendFps, d.AllowedFps + 0.5);
                }
                int slot = m.LastFrame % SendTimeRing;
                if (m.LastShowUs != 0 && sentFrame[slot] == m.LastFrame)
                {
                    double latency = (double)(long)(d.Clock.ToHost(m.LastShowUs) - sentUs[slot]);
                    if (latency >= 0 && latency < 1e6)
                        d.LatencyEmaUs = double.IsNaN(d.LatencyEmaUs) ? latency : d.LatencyEmaUs * 0.9 + latency * 0.1;
                }
            }
        }

        void OnTouch(in TouchHeader h, ReadOnlySpan<TouchEvent> events, Span<TouchEvent> fresh, ulong now)
        {
            int n;
            bool synced;
            long offset;
            lock (gate)
            {
                var d = Get(h.Device);
                if (d == null || d.EndPoint == null) return;
                d.LastSeenUs = now;
                d.TouchState = h.State;
                n = d.Tracker.Accept(h, events, fresh);
                synced = d.Clock.IsSynced;
                offset = d.Clock.OffsetUs;
            }
            for (int i = 0; i < n; i++)
            {
                var e = fresh[i];
                // 時刻合わせ前は，届いた時刻から「パケットを作った時刻との差」を引いて近似する
                ulong hostUs = synced ? (ulong)((long)e.TimeUs - offset) : now - (h.DeviceTimeUs - e.TimeUs);
                Touched?.Invoke(new DeviceTouch(h.Device, e.Sensor, e.Down, hostUs, synced));
            }
        }
    }
}
