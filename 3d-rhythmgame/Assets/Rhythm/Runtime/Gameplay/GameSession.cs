using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Song;
using UnityEngine;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// 1回のプレイ．SongConductor の曲時刻で TouchJudge を進め，判定結果をイベントで知らせる．
    ///
    /// タッチはどのスレッドからでも Touch(lane, realtime) で入れてよい（UDP 受信スレッドから直接呼べる）．
    /// 受信した実時間を曲時刻に直してから判定するので，フレームの区切りで判定がずれない．
    /// プレイ中のタッチは TouchLog に記録し，曲の終わりに persistentDataPath/replays/ に保存する．
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        public SongConductor conductor;

        [Tooltip("曲の終わりにタッチの記録を保存する")]
        public bool saveReplay = true;

        /// <summary>判定が確定した（Perfect/Good/Miss）．</summary>
        public event Action<JudgeEvent> Judged;

        /// <summary>タッチを受け付けた（空タッチを含む）．レーン，曲時刻，扱い．</summary>
        public event Action<int, double, TouchOutcome> Touched;

        public TouchJudge Judge { get; private set; }
        public Chart Chart { get; private set; }
        public TouchLog Log { get; private set; }

        readonly ConcurrentQueue<(int lane, double realtime)> pending = new ConcurrentQueue<(int, double)>();
        readonly List<(int lane, double songTime)> ordered = new List<(int, double)>();
        readonly List<JudgeEvent> events = new List<JudgeEvent>();
        double lastAdvanced = double.NegativeInfinity;

        void OnEnable()
        {
            if (conductor == null) return;
            conductor.Started += OnStarted;
            conductor.Ended += OnEnded;
        }

        void OnDisable()
        {
            if (conductor == null) return;
            conductor.Started -= OnStarted;
            conductor.Ended -= OnEnded;
        }

        void OnStarted(double fromSec)
        {
            Chart = conductor.Data.ToChart();
            Judge = new TouchJudge(Chart, conductor.Data.EffectiveRules);
            Log = new TouchLog { ChartName = conductor.song != null ? conductor.song.name : "" };
            while (pending.TryDequeue(out _)) { }
            lastAdvanced = double.NegativeInfinity;
            // 途中から再生した場合，それより前のノーツは判定しない（Miss も出さない）
            if (fromSec > 0)
            {
                Judge.Advance(fromSec, null);
                lastAdvanced = fromSec;
            }
        }

        void OnEnded()
        {
            Flush();
            if (!saveReplay || Log == null || Log.Touches.Count == 0) return;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "replays");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Log.ChartName}.json");
                File.WriteAllText(file, Log.ToJson());
                Debug.Log($"[GameSession] タッチの記録を保存: {file}（スコア {Judge.Score}）");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GameSession] タッチの記録を保存できなかった: {e.Message}");
            }
        }

        /// <summary>レーンへのタッチ．realtime は AudioClock.Realtime で取った受信時刻（省略時は今）．どのスレッドからでも呼べる．</summary>
        public void Touch(int lane, double realtime = double.NaN)
        {
            pending.Enqueue((lane, double.IsNaN(realtime) ? AudioClock.Realtime : realtime));
        }

        void Update()
        {
            if (Judge == null || conductor == null || !conductor.IsStarted) return;
            Flush();
        }

        void Flush()
        {
            if (Judge == null) return;
            double now = conductor.SongTime;
            if (double.IsNaN(now)) return;

            ordered.Clear();
            while (pending.TryDequeue(out var t))
                if (!conductor.IsPaused) ordered.Add((t.lane, conductor.TouchTimeAt(t.realtime)));
            ordered.Sort((a, b) => a.songTime.CompareTo(b.songTime));

            events.Clear();
            foreach (var (lane, time) in ordered)
            {
                // 判定は時刻順にしか進められないので，補正で今より前になったタッチもそのまま使い，
                // すでに進めた時刻より前なら進めた時刻で扱う
                double t = Math.Max(time, lastAdvanced);
                var outcome = Judge.Touch(lane, t, events);
                lastAdvanced = t;
                Log.Add(t, lane);
                Touched?.Invoke(lane, t, outcome);
            }
            if (now > lastAdvanced)
            {
                Judge.Advance(now, events);
                lastAdvanced = now;
            }
            foreach (var e in events) Judged?.Invoke(e);
        }
    }
}
