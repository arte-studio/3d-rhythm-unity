using System;

namespace Arte.Rhythm.Timing
{
    /// <summary>
    /// 曲時刻（曲頭からの秒）の計算．判定・演出・LED 出力のすべてがこの時刻を基準にする．
    ///
    /// 元になる時刻（source）は単調増加する秒なら何でもよい．Unity ではオーディオの DSP 時刻を
    /// 滑らかにしたもの（Runtime 側で用意する）を渡す．テストやリプレイでは任意の値を渡せる．
    ///
    /// 旧版は AudioSource.Play() の直後の dspTime を曲頭としていたが，Play() は次のオーディオバッファから
    /// 鳴り始めるので数十ミリ秒ずれる．新しい Runtime は PlayScheduled(開始時刻) で鳴らし，同じ開始時刻を StartAt に渡す．
    ///
    /// 遅延の補正:
    /// - InputLatencySec: タッチしてから PC が受け取るまでの遅れ．タッチ時刻からこれを引いて判定する．
    /// - OutputLatencySec: LED に色を送ってから光るまでの遅れ．この分だけ先の時刻の演出を描いて送る．
    /// どちらも実機で測って設営プロファイル側で設定する想定．既定は 0（旧版と同じ）．
    /// </summary>
    public sealed class SongClock
    {
        double startSource = double.NaN;
        double pausedAtSource = double.NaN;
        double pausedTotal;

        public double InputLatencySec { get; set; }
        public double OutputLatencySec { get; set; }

        public bool IsStarted => !double.IsNaN(startSource);
        public bool IsPaused => !double.IsNaN(pausedAtSource);

        /// <summary>曲頭が鳴る元時刻を設定する（過去でも未来でもよい．未来なら曲時刻は負から始まる）．</summary>
        public void StartAt(double source)
        {
            startSource = source;
            pausedAtSource = double.NaN;
            pausedTotal = 0;
        }

        /// <summary>開始前の状態に戻す．</summary>
        public void Reset()
        {
            startSource = double.NaN;
            pausedAtSource = double.NaN;
            pausedTotal = 0;
        }

        public void Pause(double source)
        {
            if (!IsStarted || IsPaused) return;
            pausedAtSource = source;
        }

        public void Resume(double source)
        {
            if (!IsPaused) return;
            if (source < pausedAtSource) throw new ArgumentException("再開時刻が一時停止時刻より前");
            pausedTotal += source - pausedAtSource;
            pausedAtSource = double.NaN;
        }

        /// <summary>元時刻 source のときの曲時刻．一時停止中は止まった時刻を返す．開始前は NaN．</summary>
        public double SongTime(double source)
        {
            if (!IsStarted) return double.NaN;
            double s = IsPaused ? pausedAtSource : source;
            return s - startSource - pausedTotal;
        }

        /// <summary>source に受け取ったタッチが，実際に押された曲時刻．</summary>
        public double TouchTime(double receivedSource) => SongTime(receivedSource) - InputLatencySec;

        /// <summary>source の時点で LED に送るべき演出の曲時刻（光るときに曲と合うよう先回りする）．</summary>
        public double OutputTime(double source) => SongTime(source) + OutputLatencySec;
    }
}
