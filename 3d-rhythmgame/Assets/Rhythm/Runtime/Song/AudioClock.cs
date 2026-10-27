using System.Diagnostics;
using UnityEngine;

namespace Arte.Rhythm.Song
{
    /// <summary>
    /// オーディオの DSP 時刻を滑らかにした時計．
    ///
    /// AudioSettings.dspTime はオーディオバッファ単位（数十ミリ秒）でしか進まないので，そのまま使うと
    /// 演出がカクつく．DSP 時刻が更新された瞬間の実時間を覚えておき，その後は実時間で補間する．
    /// 補間がずれても次の更新で DSP 時刻に引き戻すので，長時間でも音とずれない．
    ///
    /// 実時間には Stopwatch を使う．別スレッド（UDP 受信）で取った時刻も DspAt で同じ時間軸に直せる．
    /// </summary>
    public static class AudioClock
    {
        static readonly Stopwatch Watch = Stopwatch.StartNew();
        static double lastDsp = double.NaN;
        static double lastDspRealtime;
        static double lastReturned;
        static int lastFrame = -1;
        static double frameValue;
        static double frameRealtime;

        /// <summary>
        /// 動画の書き出しなど，実時間でなく 1 フレームずつ進めたいときに入れる（null で通常どおり）．
        /// 入れている間は Now と DspNow がこの値を返す．
        /// </summary>
        public static double? Manual;

        /// <summary>今の DSP 時刻（Manual があればそれ）．再生の予約に使う．</summary>
        public static double DspNow => Manual ?? AudioSettings.dspTime;

        /// <summary>スレッドを問わず使える実時間（秒）．</summary>
        public static double Realtime => Watch.Elapsed.TotalSeconds;

        /// <summary>このフレームの滑らかな DSP 時刻．同じフレーム内では同じ値を返す（メインスレッド専用）．</summary>
        public static double Now
        {
            get
            {
                if (Manual.HasValue) return Manual.Value;
                if (Time.frameCount == lastFrame) return frameValue;
                lastFrame = Time.frameCount;
                double dsp = AudioSettings.dspTime;
                double rt = Realtime;
                if (dsp != lastDsp)
                {
                    lastDsp = dsp;
                    lastDspRealtime = rt;
                }
                double estimate = lastDsp + (rt - lastDspRealtime);
                // DSP が止まっている（一時停止・デバイス切り替え）ときに先へ行き過ぎないよう，2バッファ分までに抑える
                AudioSettings.GetDSPBufferSize(out int bufferLength, out _);
                double maxAhead = (double)bufferLength / AudioSettings.outputSampleRate * 2;
                if (estimate > lastDsp + maxAhead) estimate = lastDsp + maxAhead;
                if (estimate < lastReturned) estimate = lastReturned; // 逆戻りさせない
                lastReturned = estimate;
                frameValue = estimate;
                frameRealtime = rt;
                return estimate;
            }
        }

        /// <summary>実時間 realtime（Realtime で取った値）に対応する DSP 時刻．別スレッドで取った時刻の変換用．</summary>
        public static double DspAt(double realtime)
        {
            double now = Now;
            return now + (realtime - frameRealtime);
        }
    }
}
