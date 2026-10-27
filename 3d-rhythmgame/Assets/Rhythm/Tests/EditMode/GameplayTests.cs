using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Arte.Rhythm.Charts;
using Arte.Rhythm.Gameplay;
using Arte.Rhythm.Timing;
using NUnit.Framework;

namespace Arte.Rhythm.Tests
{
    /// <summary>
    /// 判定（TouchJudge）が旧版（legacy-v1 の GameManager）と同じ結果になることを確かめる．
    /// 旧版の判定はこのファイル内の LegacyJudgeSim に写してある．
    /// </summary>
    public class GameplayTests
    {
        static Chart LoadLegacy(string name) =>
            LegacyChartJson.Parse(File.ReadAllText(Path.Combine(TestData.LegacyResourcesDir(), name)));

        // ---- 旧版の判定（Assets/Script/GameManager.cs の TouchNotes_judge / HandleTouchInput / Update） ----

        /// <summary>
        /// 旧版をフレーム単位でまねる．1フレームの処理は「判定 → タッチ受付 → 判定」とする
        /// （旧版は UdpController と GameManager の Update の順序が決まっておらず，新版は Miss の確定を先にする）．
        /// 連続タッチ防止は旧版の fixedDeltaTime ではなく実際の経過時間で減らす（新版の仕様）．
        /// </summary>
        sealed class LegacyJudgeSim
        {
            readonly float[] time;
            readonly int[] lane;
            readonly bool[] flag, used;
            readonly double perfectRange, goodRange, missRange, cooldown, notesignalTime;
            readonly Dictionary<int, double> laneCooldowns = new Dictionary<int, double>();
            public readonly List<(int index, JudgeResult result, double t)> Events = new List<(int, JudgeResult, double)>();
            public int Score;
            readonly JudgeRules rules;
            int lastSpawned;
            double prevT = double.NaN;

            public LegacyJudgeSim(Chart chart, JudgeRules r)
            {
                rules = r;
                time = chart.Notes.Select(n => (float)n.Time).ToArray();
                lane = chart.Notes.Select(n => n.Lane).ToArray();
                flag = new bool[time.Length];
                used = new bool[time.Length];
                perfectRange = r.PerfectSec; goodRange = r.GoodSec; missRange = r.MissAfterSec;
                cooldown = r.LaneCooldownSec; notesignalTime = r.SpawnLeadSec;
            }

            public void Frame(double currentTime, IEnumerable<int> touchedLanes)
            {
                double dt = double.IsNaN(prevT) ? 0 : currentTime - prevT;
                prevT = currentTime;
                foreach (int l in laneCooldowns.Keys.ToList()) if (laneCooldowns[l] > 0) laneCooldowns[l] -= dt;
                while (lastSpawned < time.Length && time[lastSpawned] <= currentTime + notesignalTime) lastSpawned++;

                Judge(currentTime);
                foreach (int l in touchedLanes) HandleTouchInput(l, currentTime);
                Judge(currentTime);
            }

            void HandleTouchInput(int l, double currentTime)
            {
                if (laneCooldowns.TryGetValue(l, out double cd) && cd > 1e-9) return;
                laneCooldowns[l] = cooldown;
                for (int i = 0; i < lastSpawned; i++)
                {
                    if (used[i] || lane[i] != l) continue;
                    if (currentTime >= time[i] - goodRange && currentTime <= time[i] + goodRange + missRange)
                    {
                        flag[i] = true;
                        break;
                    }
                }
            }

            void Judge(double currentTime)
            {
                for (int i = 0; i < lastSpawned; i++)
                {
                    if (used[i]) continue;
                    float targetTime = time[i];
                    float diff = (float)(currentTime - targetTime);
                    if (currentTime >= targetTime - goodRange && currentTime <= targetTime + goodRange + missRange)
                    {
                        JudgeResult? r = null;
                        if (Math.Abs(diff) <= perfectRange && flag[i]) { r = JudgeResult.Perfect; Score += rules.PerfectScore; }
                        else if (Math.Abs(diff) <= goodRange && flag[i]) { r = JudgeResult.Good; Score += rules.GoodScore; }
                        else if (currentTime > targetTime + missRange) r = JudgeResult.Miss;
                        if (r != null)
                        {
                            used[i] = true;
                            flag[i] = false;
                            Events.Add((i, r.Value, currentTime));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// ランダムなタッチ列（ノーツ付近を狙う押下 + 無関係な連打）を作る．
        /// フレーム時刻にそろえるので，旧版のまねと新版に同じ入力を与えられる．
        /// </summary>
        static SortedDictionary<int, List<int>> RandomTouches(Chart chart, int seed, double fps)
        {
            var rng = new Random(seed);
            var byFrame = new SortedDictionary<int, List<int>>();
            void Add(double t, int l)
            {
                int f = (int)Math.Round(t * fps);
                if (f < 0) return;
                if (!byFrame.TryGetValue(f, out var list)) byFrame[f] = list = new List<int>();
                if (!list.Contains(l)) list.Add(l);
            }
            foreach (var n in chart.Notes)
            {
                if (rng.NextDouble() < 0.8) Add(n.Time + (rng.NextDouble() * 4.5 - 3.5), n.Lane);
                if (rng.NextDouble() < 0.2) Add(n.Time + rng.NextDouble() * 0.15, n.Lane); // 連打
            }
            int[] lanes = chart.Lanes.ToArray();
            double end = chart.Notes.Last().Time + 5;
            for (int k = 0; k < 300; k++) Add(rng.NextDouble() * end, lanes[rng.Next(lanes.Length)]);
            return byFrame;
        }

        /// <summary>旧版と同じ float 精度のノーツ時刻にそろえる（境界ちょうどの比較で差が出ないように）．</summary>
        static Chart FloatTimes(Chart chart) =>
            new Chart(chart.Notes.Select(n => new ChartNote((float)n.Time, n.Lane, n.Type)));

        static IEnumerable<object[]> EquivalenceCases()
        {
            var kodaisai = JudgeRules.Legacy();
            var tight = new JudgeRules { PerfectSec = 0.05, GoodSec = 0.12, MissAfterSec = 0.2, LaneCooldownSec = 0.1, SpawnLeadSec = 0.12 };
            foreach (string file in new[] { "Kodaisai_NoteData_1101.json", "Kokasai_NoteData_1026.json" })
                foreach (int seed in new[] { 1, 2, 3, 4, 5 })
                {
                    yield return new object[] { file, seed, kodaisai, "kodaisai" };
                    yield return new object[] { file, seed, tight, "tight" };
                }
        }

        [TestCaseSource(nameof(EquivalenceCases))]
        public void Judge_MatchesLegacyFrameSimulation(string file, int seed, JudgeRules rules, string rulesName)
        {
            const double fps = 59; // 1/fps が 0.1 秒などの境界にちょうど乗らない値
            var chart = FloatTimes(LoadLegacy(file));
            var touches = RandomTouches(chart, seed, fps);
            int lastFrame = (int)((chart.Notes.Last().Time + 5) * fps);

            var legacy = new LegacyJudgeSim(chart, rules);
            var judge = new TouchJudge(chart, rules);
            var events = new List<JudgeEvent>();
            for (int f = 0; f <= lastFrame; f++)
            {
                double t = f / fps;
                touches.TryGetValue(f, out var lanes);
                lanes ??= new List<int>();
                legacy.Frame(t, lanes);
                judge.Advance(t, events);
                foreach (int l in lanes) judge.Touch(l, t, events);
            }

            var expected = legacy.Events.OrderBy(e => e.t).ThenBy(e => e.index).Select(e => $"{e.index}:{e.result}@{e.t:F4}").ToList();
            var actual = events.OrderBy(e => e.SongTime).ThenBy(e => e.NoteIndex).Select(e => $"{e.NoteIndex}:{e.Result}@{e.SongTime:F4}").ToList();
            Assert.That(actual, Is.EqualTo(expected), $"{file} seed {seed} rules {rulesName}");
            Assert.That(judge.Score, Is.EqualTo(legacy.Score));
            Assert.That(events.Count, Is.EqualTo(chart.Notes.Count(n => n.Type == NoteType.Touch)), "全ノーツが1回ずつ判定される");
        }

        // ---- 判定の境界（旧版 = 古代祭の値） ----

        static Chart OneNote(double t = 10, int lane = 0) => new Chart(new[] { new ChartNote(t, lane, NoteType.Touch) });

        [TestCase(-3.01, null)]
        [TestCase(-3.0, JudgeResult.Good)]
        [TestCase(-2.5, JudgeResult.Good)]
        [TestCase(-2.0, JudgeResult.Perfect)]
        [TestCase(0.0, JudgeResult.Perfect)]
        [TestCase(0.5, JudgeResult.Perfect)]
        [TestCase(0.51, JudgeResult.Miss)]
        public void Judge_KodaisaiWindows(double offset, JudgeResult? expected)
        {
            var judge = new TouchJudge(OneNote(), JudgeRules.Legacy());
            var events = new List<JudgeEvent>();
            judge.Touch(0, 10 + offset, events);
            Assert.That(judge.ResultOf(0), Is.EqualTo(expected));
        }

        // 既定 = 仕様書 9.1（Perfect ±50ms，Good ±150ms）
        [TestCase(-0.16, null)]
        [TestCase(-0.149, JudgeResult.Good)]
        [TestCase(-0.06, JudgeResult.Good)]
        [TestCase(-0.049, JudgeResult.Perfect)]
        [TestCase(0.0, JudgeResult.Perfect)]
        [TestCase(0.049, JudgeResult.Perfect)]
        [TestCase(0.1, JudgeResult.Good)]
        [TestCase(0.149, JudgeResult.Good)]
        [TestCase(0.16, JudgeResult.Miss)]
        public void Judge_SpecWindows(double offset, JudgeResult? expected)
        {
            var judge = new TouchJudge(OneNote(), new JudgeRules());
            var events = new List<JudgeEvent>();
            judge.Touch(0, 10 + offset, events);
            Assert.That(judge.ResultOf(0), Is.EqualTo(expected));
            Assert.That(judge.Score, Is.EqualTo(expected == JudgeResult.Perfect ? 100 : expected == JudgeResult.Good ? 50 : 0));
        }

        [Test]
        public void Judge_CooldownIgnoresRapidRetouch()
        {
            var chart = new Chart(new[] { new ChartNote(10, 0, NoteType.Touch), new ChartNote(10.05, 0, NoteType.Touch) });
            var judge = new TouchJudge(chart, JudgeRules.Legacy());
            var events = new List<JudgeEvent>();
            Assert.That(judge.Touch(0, 10.00, events), Is.EqualTo(TouchOutcome.Judged));
            Assert.That(judge.Touch(0, 10.05, events), Is.EqualTo(TouchOutcome.Cooldown));
            Assert.That(judge.Touch(0, 10.11, events), Is.EqualTo(TouchOutcome.Judged));
            Assert.That(judge.Touch(1, 10.11, events), Is.EqualTo(TouchOutcome.NoNote), "別レーンは影響しない");
        }

        [Test]
        public void Judge_ConnectNotesAreNotJudged()
        {
            var chart = new Chart(new[] { new ChartNote(1, 0, NoteType.Connect) });
            var judge = new TouchJudge(chart, JudgeRules.Legacy());
            var events = new List<JudgeEvent>();
            judge.Advance(100, events);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void Replay_ReproducesLiveJudgement()
        {
            var chart = LoadLegacy("Kodaisai_NoteData_1101.json");
            var rules = JudgeRules.Legacy();
            var rng = new Random(7);
            var log = new TouchLog { ChartName = "Kodaisai_NoteData_1101" };
            var live = new TouchJudge(chart, rules);
            var liveEvents = new List<JudgeEvent>();
            foreach (var n in chart.Notes)
            {
                double t = n.Time + rng.NextDouble() * 3 - 2.5;
                if (t < 0) continue;
                // 本番と同じく時刻順にしか呼べないので，ここでは前のタッチより後ろのものだけ使う
                if (log.Touches.Count > 0 && t < log.Touches[^1].Time) continue;
                log.Add(t, n.Lane);
                live.Touch(n.Lane, t, liveEvents);
            }
            live.Advance(200, liveEvents);

            var replay = TouchReplay.Run(chart, rules, TouchLog.FromJson(log.ToJson()).Touches, 200);
            Assert.That(replay.Score, Is.EqualTo(live.Score));
            Assert.That(replay.Events.Select(e => (e.NoteIndex, e.Result)), Is.EqualTo(liveEvents.Select(e => (e.NoteIndex, e.Result))));
        }

        // ---- 譜面の読み込み ----

        [Test]
        public void LegacyChart_Loads()
        {
            var chart = LoadLegacy("Kodaisai_NoteData_1101.json");
            Assert.That(chart.Notes.Count, Is.EqualTo(186));
            Assert.That(chart.Warnings, Is.Empty);
            Assert.That(chart.Lanes, Is.EqualTo(Enumerable.Range(0, 15)), "旧譜面の1〜15は内部では0〜14");
        }

        [Test]
        public void LegacyChart_UnsortedIsSortedWithWarning()
        {
            var chart = LoadLegacy("Kokasai_NoteData_mugyu.json");
            Assert.That(chart.Warnings, Is.Not.Empty);
            for (int i = 1; i < chart.Notes.Count; i++) Assert.That(chart.Notes[i].Time, Is.GreaterThanOrEqualTo(chart.Notes[i - 1].Time));
        }

        // ---- 曲時刻 ----

        [Test]
        public void SongClock_PauseAndLatency()
        {
            var clock = new SongClock { InputLatencySec = 0.03, OutputLatencySec = 0.05 };
            Assert.That(clock.SongTime(0), Is.NaN);
            clock.StartAt(100);
            Assert.That(clock.SongTime(99), Is.EqualTo(-1).Within(1e-9), "開始前は負");
            Assert.That(clock.SongTime(110), Is.EqualTo(10).Within(1e-9));
            clock.Pause(110);
            Assert.That(clock.SongTime(115), Is.EqualTo(10).Within(1e-9), "一時停止中は止まる");
            clock.Resume(120);
            Assert.That(clock.SongTime(121), Is.EqualTo(11).Within(1e-9));
            Assert.That(clock.TouchTime(121), Is.EqualTo(10.97).Within(1e-9));
            Assert.That(clock.OutputTime(121), Is.EqualTo(11.05).Within(1e-9));
        }
    }
}
