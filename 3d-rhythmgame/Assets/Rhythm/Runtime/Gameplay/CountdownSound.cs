using System.Collections.Generic;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Song;
using UnityEngine;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// 3，2，1，はい の音（2026-11-04 ユーザー）．3 → 2 → 1 → はい とだんだん高くなる（ド・ミ・ソ・高いド．
    /// 2026-11-05 ユーザー: 音をそれぞれ変える．数え始めを聞き逃しても，高さで次が はい と分かる．2〜3 人で音が重なっても聞き分けやすい）．
    /// 曲と同じ DSP の時計で少し先を予約して鳴らすので，曲の拍とずれない（CountdownCues）．
    /// 音はここで作る（素材のファイルは要らない）．GameSession が無ければ足す．
    /// </summary>
    public sealed class CountdownSound : MonoBehaviour
    {
        public GameSession session;
        public SongConductor conductor;

        [Tooltip("音量（0 で鳴らさない）")]
        [Range(0, 1)] public float volume = 0.6f;
        [Tooltip("3 の音の高さ（Hz）")]
        public float count3Hz = 523.25f;
        [Tooltip("2 の音の高さ（Hz）")]
        public float count2Hz = 659.26f;
        [Tooltip("1 の音の高さ（Hz）")]
        public float count1Hz = 783.99f;
        [Tooltip("はい の音の高さ（Hz）")]
        public float hitHz = 1046.5f;
        [Tooltip("何秒先まで予約するか")]
        public float lookAheadSec = 0.25f;

        AudioClip count3Clip, count2Clip, count1Clip, hitClip;
        readonly List<AudioSource> pool = new List<AudioSource>();
        int nextSource;
        List<CountdownCues.Cue> cues;
        Chart cuesFor;
        int cursor;
        double scheduledUntil = double.NegativeInfinity;
        bool wasPaused;

        void Awake()
        {
            count3Clip = Blip("3", count3Hz, 0.07f);
            count2Clip = Blip("2", count2Hz, 0.07f);
            count1Clip = Blip("1", count1Hz, 0.07f);
            hitClip = Blip("はい", hitHz, 0.12f);
            for (int i = 0; i < 8; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0;
                pool.Add(s);
            }
        }

        // AddComponent のあとに session と conductor を入れるので，OnEnable ではなく Start でつなぐ
        void Start()
        {
            if (conductor != null) conductor.Started += OnStarted;
        }

        void OnDestroy()
        {
            if (conductor != null) conductor.Started -= OnStarted;
        }

        void OnDisable() => StopAll();

        void OnStarted(double fromSec)
        {
            StopAll();
            cuesFor = null;
            scheduledUntil = fromSec;
        }

        void Update()
        {
            if (session == null || conductor == null || !conductor.IsStarted || session.Chart == null) return;
            if (conductor.IsPaused)
            {
                if (!wasPaused) StopAll();
                wasPaused = true;
                return;
            }
            double now = conductor.SongTime;
            if (wasPaused) scheduledUntil = now;
            wasPaused = false;
            if (session.Chart != cuesFor)
            {
                cuesFor = session.Chart;
                cues = CountdownCues.From(cuesFor, conductor.Data?.TempoMap(), session.PlayingCountBeats);
                cursor = 0;
            }
            if (volume <= 0) return;
            double until = now + lookAheadSec;
            while (cursor < cues.Count && cues[cursor].Time <= until)
            {
                var c = cues[cursor++];
                if (c.Time <= scheduledUntil || c.Time < now) continue;
                var s = pool[nextSource];
                nextSource = (nextSource + 1) % pool.Count;
                s.clip = c.Count == 0 ? hitClip : c.Count == 1 ? count1Clip : c.Count == 2 ? count2Clip : count3Clip;
                s.volume = volume;
                s.PlayScheduled(AudioClock.Now + (c.Time - now));
            }
            scheduledUntil = until;
        }

        void StopAll()
        {
            foreach (var s in pool) if (s != null) s.Stop();
        }

        static AudioClip Blip(string name, float hz, float sec)
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            int n = (int)(rate * sec);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float attack = Mathf.Clamp01(t / 0.002f);
                data[i] = Mathf.Sin(2 * Mathf.PI * hz * t) * attack * Mathf.Exp(-t / (sec * 0.3f)) * 0.8f;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>シーンに GameSession があって CountdownSound が無ければ足す．</summary>
        public static void EnsureFor(GameSession session)
        {
            if (session == null || session.GetComponent<CountdownSound>() != null) return;
            var s = session.gameObject.AddComponent<CountdownSound>();
            s.session = session;
            s.conductor = session.conductor;
        }
    }
}
