using System;
using System.Net;
using System.Net.Sockets;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// LinkHost が実機に送るのと同じ PIXELS（LinkOptions.MirrorTargets）を受け取り，LED に送った値（明るさ・曲線を掛けた後）を
    /// LedFrame に書く．別の PC の Viewer で本番の LED をそのまま見るため．
    ///
    /// フレームを揃える処理はしない（届いたピンからすぐ書く）．変わっていないピンは送られてこないので前の内容のまま．
    /// スレッドは持たないので Poll を呼び続ける．
    /// </summary>
    public sealed class MirrorReceiver : IDisposable
    {
        public const int DefaultPort = 8899;

        readonly Socket socket;
        readonly byte[] rx = new byte[ArteProtocol.MaxPacket + 1];
        readonly byte[] decoded = new byte[ArteProtocol.MaxPacket * 2];
        readonly LedFrame output;
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);

        public long PacketsReceived { get; private set; }
        public long PacketsBad { get; private set; }
        /// <summary>最後に受け取ったフレーム番号．</summary>
        public ushort LastFrame { get; private set; }
        public IPEndPoint LastSender { get; private set; }
        public int Port => ((IPEndPoint)socket.LocalEndPoint).Port;

        /// <param name="output">書き込み先（LedFrame.FromLayout で作った，実機と同じ形のもの）．</param>
        public MirrorReceiver(LedFrame output, int port = DefaultPort)
        {
            this.output = output;
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }

        public void Dispose() => socket.Close();

        /// <summary>"192.168.0.10" や "192.168.0.10:8899" を送り先にする（ポートの既定は DefaultPort）．</summary>
        public static bool TryParseTarget(string s, out IPEndPoint endPoint)
        {
            endPoint = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            int port = DefaultPort;
            int colon = s.LastIndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(s.Substring(colon + 1), out port) || port <= 0 || port > 65535) return false;
                s = s.Substring(0, colon);
            }
            if (!IPAddress.TryParse(s, out var ip)) return false;
            endPoint = new IPEndPoint(ip, port);
            return true;
        }

        /// <summary>届いているパケットを全部処理し，処理した PIXELS の数を返す．</summary>
        public int Poll()
        {
            int n = 0;
            while (true)
            {
                int len;
                try { len = socket.ReceiveFrom(rx, ref remote); }
                catch (SocketException) { break; }
                catch (ObjectDisposedException) { break; }
                if (Apply(new ReadOnlySpan<byte>(rx, 0, len)))
                {
                    n++;
                    LastSender = (IPEndPoint)remote;
                }
            }
            return n;
        }

        /// <summary>PIXELS 1 個を書き込む（テスト用に公開）．PIXELS でなければ false．</summary>
        public bool Apply(ReadOnlySpan<byte> packet)
        {
            if (ArteProtocol.TypeOf(packet) != PacketType.Pixels) return false;
            if (!ArteProtocol.TryReadPixels(packet, out var h, out var payload) || h.Device >= output.DeviceCount || h.Pin >= LedFrame.PinsPerDevice)
            {
                PacketsBad++;
                return false;
            }
            var pin = output.Pin(h.Device, h.Pin);
            int start = h.Start * 3, bytes = h.Count * 3;
            if (start + bytes > pin.Length)
            {
                PacketsBad++;
                return false;
            }
            if (h.Encoding == ArteProtocol.EncodingPackBits)
            {
                if (!PixelCodec.DecodePackBits(payload, decoded, h.Count))
                {
                    PacketsBad++;
                    return false;
                }
                decoded.AsSpan(0, bytes).CopyTo(pin.Slice(start));
            }
            else
            {
                payload.CopyTo(pin.Slice(start));
            }
            PacketsReceived++;
            LastFrame = h.Frame;
            return true;
        }
    }
}
