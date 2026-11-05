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

        [Tooltip("遊ぶ人数（1〜3）．譜面の 3 本の流れのうち，この数だけを出す（2026-11-03 ユーザー決定）．起動引数 -players N でも変えられる．曲を始めるときに効く")]
        [Range(1, 3)] public int players = 3;
        // 1 人のときは最初のノーツが正面のむぎゅっとに来るよう回す（SongData.ToChart(players, ring, frontLane)）

        void Awake()
        {
            // 前に選んだ人数を使う（Play し直すたびに 3 人に戻っていた）．起動引数 -players N がそれより優先
            players = Math.Max(1, Math.Min(3, PlayerPrefs.GetInt(PlayersKey, players)));
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-players");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) players = Math.Max(1, Math.Min(3, n));
            CountBeats = PlayerPrefs.GetInt(CountKey, CountIn.Normal) == CountIn.Hard ? CountIn.Hard : CountIn.Normal;
            CountdownSound.EnsureFor(this);
        }

        /// <summary>テストプレイ中か（正面のむぎゅっとに 4 拍おきにノーツを出し続ける．SongData.TestChart）．</summary>
        public bool TestPlay { get; private set; }

        /// <summary>曲を最初から始める．test ならテストプレイ（曲はそのまま流れ，ノーツだけ差し替える）．</summary>
        public void StartSong(bool test)
        {
            TestPlay = test;
            conductor.Play();
        }

        const string PlayersKey = "Arte.Rhythm.Players";
        const string CountKey = "Arte.Rhythm.CountBeats";

        /// <summary>選んでいる数え方（3 = 3，2，1，はい / 2 = 2，1，はい．CountIn）．曲を始めるときに効く．選んだものは覚えておく．</summary>
        public int CountBeats { get; private set; } = CountIn.Normal;

        /// <summary>今の譜面の数え方（光の案内と音はこれで数える）．</summary>
        public int PlayingCountBeats { get; private set; } = CountIn.Normal;

        /// <summary>数え方を切り替える（ふつう ⇔ むずかしい）．</summary>
        public void NextCountBeats()
        {
            CountBeats = CountBeats == CountIn.Normal ? CountIn.Hard : CountIn.Normal;
            PlayerPrefs.SetInt(CountKey, CountBeats);
            PlayerPrefs.Save();
        }

        /// <summary>人数を次へ（1 → 2 → 3 → 1）．選んだ人数は覚えておき，次に起動したときも使う．曲を始めるときに効く．</summary>
        public void NextPlayers()
        {
            players = players % 3 + 1;
            PlayerPrefs.SetInt(PlayersKey, players);
            PlayerPrefs.Save();
        }

        /// <summary>判定を見せる（Perfect/Great/Good はすぐ．Miss はノーツ時刻の JudgeRules.MissShowAfter 後，既定 1 拍後）．</summary>
        public event Action<JudgeEvent> Judged;

        /// <summary>タッチを受け付けた（空タッチを含む）．レーン，曲時刻，扱い．</summary>
        public event Action<int, double, TouchOutcome> Touched;

        public TouchJudge Judge { get; private set; }
        public Chart Chart { get; private set; }
        public TouchLog Log { get; private set; }

        readonly ConcurrentQueue<(int lane, double realtime)> pending = new ConcurrentQueue<(int, double)>();
        readonly List<(int lane, double songTime)> ordered = new List<(int, double)>();
        readonly List<JudgeEvent> events = new List<JudgeEvent>();
        // 確定したがまだ見せていない Miss
        readonly List<JudgeEvent> lateMisses = new List<JudgeEvent>();
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
            var notes = FindFirstObjectByType<Net.DeviceLink>()?.Notes;
            PlayingCountBeats = CountBeats;
            if (TestPlay)
            {
                var normal = conductor.Data.ToChart(1);
                int lane = notes != null ? notes.FrontLane : normal.Notes.Count > 0 ? normal.Notes[0].Lane : 0;
                Chart = conductor.Data.TestChart(lane, conductor.LengthSec);
            }
            else Chart = notes != null ? conductor.Data.ToChart(players, notes.Lanes, notes.FrontLane, CountBeats) : conductor.Data.ToChart(players, CountBeats);
            Judge = new TouchJudge(Chart, conductor.Data.EffectiveRules, conductor.Data.TempoMap());
            lateMisses.Clear();
            Log = new TouchLog { ChartName = (conductor.song != null ? conductor.song.name : "") + (TestPlay ? "-test" : ""), Players = players, FrontLane = notes != null && players == 1 ? notes.FrontLane : -1 };
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
            foreach (var e in events)
            {
                if (e.Result == JudgeResult.Miss && now < Judge.MissShowAt(e.NoteIndex)) lateMisses.Add(e);
                else Judged?.Invoke(e);
            }
            for (int i = 0; i < lateMisses.Count; i++)
            {
                if (now < Judge.MissShowAt(lateMisses[i].NoteIndex)) continue;
                Judged?.Invoke(lateMisses[i]);
                lateMisses.RemoveAt(i--);
            }
        }
    }
}
