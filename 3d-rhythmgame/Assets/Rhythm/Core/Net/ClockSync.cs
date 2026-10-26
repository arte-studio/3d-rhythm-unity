using System;

namespace Arte.Rhythm.Net
{
    /// <summary>
    /// デバイス時刻と PC 時刻の対応を PING/PONG から求める（NTP と同じ考え方）．
    ///
    /// PC が t0 に送り，デバイスが td で受け取って返し，PC が t1 に受け取ったとき，
    /// 行きと帰りが同じ時間なら td = (t0 + t1) / 2 + offset．Wi-Fi は片道の時間が大きく揺れるので，
    /// 直近の標本のうち往復時間（t1 − t0）が最も短いものだけを使う（揺れの少ない標本ほど正しい）．
    /// ESP32 の水晶のずれは 1 秒あたり数十マイクロ秒程度なので，数秒分の窓なら傾きは無視できる．
    /// </summary>
    public sealed class ClockSync
    {
        readonly Sample[] window;
        int count, next;

        struct Sample
        {
            public long Offset;
            public long Rtt;
        }

        /// <param name="windowSize">何個の標本から選ぶか（PING 0.25 秒おきなら 20 個 = 5 秒）．</param>
        public ClockSync(int windowSize = 20)
        {
            window = new Sample[Math.Max(1, windowSize)];
        }

        public bool IsSynced => count > 0;

        /// <summary>デバイス時刻 − PC 時刻（マイクロ秒）．</summary>
        public long OffsetUs { get; private set; }

        /// <summary>採用した標本の往復時間（マイクロ秒）．時刻合わせの誤差は最大でこの半分．</summary>
        public long RttUs { get; private set; }

        /// <summary>最後に受け取った標本の往復時間（通信の揺れの目安）．</summary>
        public long LastRttUs { get; private set; }

        public void Reset()
        {
            count = 0;
            next = 0;
        }

        public void AddSample(ulong hostSendUs, ulong deviceUs, ulong hostRecvUs)
        {
            if (hostRecvUs < hostSendUs) return;
            long rtt = (long)(hostRecvUs - hostSendUs);
            long mid = (long)hostSendUs + rtt / 2;
            window[next] = new Sample { Offset = (long)deviceUs - mid, Rtt = rtt };
            next = (next + 1) % window.Length;
            if (count < window.Length) count++;
            LastRttUs = rtt;

            int best = 0;
            for (int i = 1; i < count; i++)
                if (window[i].Rtt < window[best].Rtt) best = i;
            OffsetUs = window[best].Offset;
            RttUs = window[best].Rtt;
        }

        public ulong ToHost(ulong deviceUs) => (ulong)((long)deviceUs - OffsetUs);

        public ulong ToDevice(ulong hostUs) => (ulong)((long)hostUs + OffsetUs);
    }
}
