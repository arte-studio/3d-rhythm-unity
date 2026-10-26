using System;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Timing;
using UnityEngine;

namespace Arte.Rhythm.Song
{
    /// <summary>
    /// 曲の再生と曲時刻の管理．判定・演出・動画・Timeline はすべてここの SongTime / OutputTime に従う．
    ///
    /// 旧版は AudioSource.Play() の直後の dspTime を曲頭としていたので，実際に音が出る時刻と数十ミリ秒ずれていた．
    /// ここでは PlayScheduled で「少し先の DSP 時刻」に鳴らす予約をし，その時刻をそのまま曲頭にする．
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class SongConductor : MonoBehaviour
    {
        [Tooltip("再生する曲")]
        public SongAsset song;

        [Tooltip("再生ボタンから音が出るまでの余裕（秒）．短すぎると予約が間に合わず頭が欠ける")]
        [Min(0.05f)] public float scheduleLeadSec = 0.2f;

        [Tooltip("タッチしてから PC が受け取るまでの遅れ（秒）．設営ごとに実測して入れる")]
        public float inputLatencySec;

        [Tooltip("LED に送ってから光るまでの遅れ（秒）．設営ごとに実測して入れる")]
        public float outputLatencySec;

        [Tooltip("シーン開始と同時に再生する")]
        public bool playOnStart;

        readonly SongClock clock = new SongClock();
        AudioSource source;
        bool ended;

        public SongData Data { get; private set; }
        public bool IsPlaying => clock.IsStarted && !clock.IsPaused && !ended;
        public bool IsPaused => clock.IsPaused;
        public bool IsStarted => clock.IsStarted;
        public double LengthSec => song != null ? song.LengthSec : 0;

        /// <summary>このフレームの曲時刻（秒）．開始前は NaN，予約から鳴り始めまでは負．</summary>
        public double SongTime => clock.SongTime(AudioClock.Now);

        /// <summary>LED に送る演出の曲時刻（光る瞬間に曲と合うよう OutputLatency だけ先）．</summary>
        public double OutputTime => clock.OutputTime(AudioClock.Now);

        /// <summary>AudioClock.Realtime で記録したタッチの受信時刻を，押された曲時刻に直す．</summary>
        public double TouchTimeAt(double realtime) => clock.TouchTime(AudioClock.DspAt(realtime));

        /// <summary>再生（またはシーク）した．引数は開始した曲時刻．</summary>
        public event Action<double> Started;
        public event Action Ended;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
        }

        void Start()
        {
            if (playOnStart) Play();
        }

        /// <summary>曲時刻 fromSec から再生する．</summary>
        public void Play(double fromSec = 0)
        {
            if (song == null || song.audio == null) throw new InvalidOperationException("SongAsset か音声が設定されていない");
            Data = song.Data ?? new SongData();
            clock.InputLatencySec = inputLatencySec;
            clock.OutputLatencySec = outputLatencySec;

            source.Stop();
            source.clip = song.audio;
            double dspStart = AudioSettings.dspTime + scheduleLeadSec;
            double filePos = Data.AudioStartSec + fromSec;
            if (filePos >= 0)
            {
                source.time = (float)filePos;
                source.PlayScheduled(dspStart);
            }
            else
            {
                // 曲時刻が音声ファイルの頭より前（前奏の前の待ち時間など）: 頭が来る時刻に予約する
                source.time = 0;
                source.PlayScheduled(dspStart - filePos);
            }
            clock.StartAt(dspStart - fromSec);
            ended = false;
            Started?.Invoke(fromSec);
        }

        public void Pause()
        {
            if (!IsPlaying) return;
            source.Pause();
            clock.Pause(AudioClock.Now);
        }

        public void Resume()
        {
            if (!clock.IsPaused) return;
            // UnPause はすぐに鳴り始めるので，時計も今の時刻で再開する
            source.UnPause();
            clock.Resume(AudioClock.Now);
        }

        public void Stop()
        {
            source.Stop();
            clock.Reset();
            ended = false;
        }

        void Update()
        {
            if (!IsPlaying) return;
            if (SongTime >= LengthSec)
            {
                ended = true;
                Ended?.Invoke();
            }
        }
    }
}
