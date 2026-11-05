using System;
using System.Collections.Generic;
using Arte.Rhythm.Charts;

namespace Arte.Rhythm.Gameplay
{
    public enum JudgeResult
    {
        Perfect,
        Great,
        Good,
        Miss,
    }

    /// <summary>判定が確定したときに出るイベント．演出・効果音・スコア表示はこれを見て動く．</summary>
    public readonly struct JudgeEvent
    {
        /// <summary>譜面（Chart.Notes）内のノーツ番号．</summary>
        public readonly int NoteIndex;
        public readonly int Lane;
        public readonly JudgeResult Result;
        /// <summary>判定が確定した曲時刻（秒）．Miss はノーツ時刻 + MissAfterSec を過ぎて最初に Advance された時刻．</summary>
        public readonly double SongTime;
        /// <summary>SongTime − ノーツ時刻．負なら早押し．</summary>
        public readonly double Offset;

        public JudgeEvent(int noteIndex, int lane, JudgeResult result, double songTime, double offset)
        {
            NoteIndex = noteIndex;
            Lane = lane;
            Result = result;
            SongTime = songTime;
            Offset = offset;
        }

        public override string ToString() => $"{Result} note {NoteIndex} lane {Lane} @ {SongTime:F3}s ({Offset:+0.000;-0.000}s)";
    }

    /// <summary>タッチ1回がどう扱われたか．空タッチの演出やログに使う．</summary>
    public enum TouchOutcome
    {
        /// <summary>ノーツを判定した．</summary>
        Judged,
        /// <summary>そのレーンに受付時間内のノーツが無かった（空タッチ）．</summary>
        NoNote,
        /// <summary>受付時間内のノーツはあったが Perfect/Great/Good の範囲外だった（そのノーツはいずれ Miss になる）．</summary>
        OutOfRange,
        /// <summary>同じレーンの連続タッチとして無視した．</summary>
        Cooldown,
    }

    /// <summary>
    /// タッチノーツの判定（旧 GameManager.TouchNotes_judge / HandleTouchInput を置き換える）．
    ///
    /// Unity に依存せず，時刻はすべて呼び出し側が渡す曲時刻（秒）で扱う．同じ譜面・同じ入力列なら
    /// 何度実行しても同じ結果になるので，リプレイや単体テストでそのまま使える．
    ///
    /// 旧版との違い:
    /// - 旧版は毎フレームの時刻で判定していた．こちらはタッチを受けた時刻で判定するので，
    ///   フレームレートによって判定が変わらない（判定範囲が秒単位なので結果はほぼ同じ）．
    /// - 旧版のレーンの連続タッチ防止は Update ごとに Time.fixedDeltaTime（0.02秒）ずつ減らしていたため，
    ///   実際の長さがフレームレートで変わっていた（60fps なら約0.083秒）．こちらは秒で数える．
    /// - 旧版はスクリプトの実行順によって，Miss になる直前のフレームのタッチが拾われることがあった．
    ///   こちらは必ず「時刻を進めて Miss を確定 → タッチを判定」の順に処理する．
    /// </summary>
    public sealed class TouchJudge
    {
        readonly IReadOnlyList<ChartNote> notes;
        readonly JudgeRules rules;
        readonly JudgeResult?[] results;
        // レーンごとの，未判定のタッチノーツの番号（時刻順）．先頭から順に判定されていく
        readonly Dictionary<int, LaneQueue> lanes = new Dictionary<int, LaneQueue>();
        readonly Dictionary<int, double> lastAcceptedTouch = new Dictionary<int, double>();
        readonly List<JudgeEvent> missScratch = new List<JudgeEvent>();
        // ノーツごとの幅（秒）．拍で書いた幅はノーツの所のテンポで秒にする
        readonly double[] perfect, great, good, missAfter, missShowAfter;
        double now = double.NegativeInfinity;

        sealed class LaneQueue
        {
            public readonly List<int> Indices = new List<int>();
            public int Head;
            public bool Empty => Head >= Indices.Count;
            public int Peek() => Indices[Head];
        }

        public TouchJudge(Chart chart, JudgeRules rules, TempoMap tempo = null)
        {
            notes = chart.Notes;
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            results = new JudgeResult?[notes.Count];
            perfect = new double[notes.Count];
            great = new double[notes.Count];
            good = new double[notes.Count];
            missAfter = new double[notes.Count];
            missShowAfter = new double[notes.Count];
            for (int i = 0; i < notes.Count; i++)
            {
                double t = notes[i].Time;
                perfect[i] = rules.PerfectAt(tempo, t);
                great[i] = rules.GreatAt(tempo, t);
                good[i] = rules.GoodAt(tempo, t);
                missAfter[i] = rules.MissAfterAt(tempo, t);
                missShowAfter[i] = rules.MissShowAfterAt(tempo, t);
                if (notes[i].Type != NoteType.Touch) continue;
                if (!lanes.TryGetValue(notes[i].Lane, out var q)) lanes[notes[i].Lane] = q = new LaneQueue();
                q.Indices.Add(i);
            }
        }

        public JudgeRules Rules => rules;
        public int Score { get; private set; }
        public int PerfectCount { get; private set; }
        public int GreatCount { get; private set; }
        public int GoodCount { get; private set; }
        public int MissCount { get; private set; }
        /// <summary>途中から再生したときに飛ばしたノーツの数（Miss にもスコアにも入れない．SkipTo）．</summary>
        public int SkippedCount { get; private set; }

        /// <summary>判定が確定していればその結果，未確定なら null．</summary>
        public JudgeResult? ResultOf(int noteIndex) => results[noteIndex];

        /// <summary>このノーツの Miss を見せる曲時刻（ノーツ時刻 + MissShowAfter．Miss の確定より前なら確定した時）．</summary>
        public double MissShowAt(int noteIndex) => notes[noteIndex].Time + Math.Max(missShowAfter[noteIndex], missAfter[noteIndex]);

        /// <summary>
        /// 未判定で，案内（受付色の点滅）が始まっているか（ノーツ時刻の SpawnLeadSec 前から）．
        /// </summary>
        public bool IsOpen(int noteIndex, double songTime) =>
            results[noteIndex] == null && notes[noteIndex].Type == NoteType.Touch && songTime >= notes[noteIndex].Time - rules.SpawnLeadSec;

        /// <summary>
        /// 曲時刻を進め，タッチされないまま MissAfterSec を過ぎたノーツを Miss にする．
        /// 毎フレーム呼ぶ．Miss はノーツ番号順に output へ追加する．
        /// </summary>
        public void Advance(double songTime, List<JudgeEvent> output) => Advance(songTime, output, false);

        /// <summary>
        /// 途中から再生するとき，その位置より前のノーツを飛ばす（2026-11-06 ユーザー: 飛ばしたノーツが Miss に数えられていた）．
        /// 飛ばしたノーツは判定済み（ResultOf は Miss．案内も出ない）にするが，Miss の数・スコアには入れず，JudgeEvent も出さない．
        /// </summary>
        public void SkipTo(double songTime) => Advance(songTime, null, true);

        void Advance(double songTime, List<JudgeEvent> output, bool skip)
        {
            if (songTime < now) throw new ArgumentException($"曲時刻が戻った: {songTime} < {now}（シーク時は作り直すこと）");
            now = songTime;
            missScratch.Clear();
            foreach (var q in lanes.Values)
            {
                while (!q.Empty && songTime > notes[q.Peek()].Time + missAfter[q.Peek()])
                {
                    int i = q.Peek();
                    q.Head++;
                    if (skip)
                    {
                        results[i] = JudgeResult.Miss;
                        SkippedCount++;
                    }
                    else Commit(i, JudgeResult.Miss, songTime, missScratch);
                }
            }
            if (missScratch.Count > 1) missScratch.Sort((a, b) => a.NoteIndex.CompareTo(b.NoteIndex));
            output?.AddRange(missScratch);
        }

        /// <summary>
        /// レーンへのタッチ（押した瞬間）を判定する．先に Advance(songTime) を行うので，Miss も output に入ることがある．
        /// </summary>
        public TouchOutcome Touch(int lane, double songTime, List<JudgeEvent> output)
        {
            Advance(songTime, output);

            if (lastAcceptedTouch.TryGetValue(lane, out double last) && songTime - last < rules.LaneCooldownSec)
                return TouchOutcome.Cooldown;
            // 旧版どおり，ノーツが無くても受け付けたタッチなら連続タッチ防止を始める
            lastAcceptedTouch[lane] = songTime;

            if (!lanes.TryGetValue(lane, out var q) || q.Empty) return TouchOutcome.NoNote;
            // 先頭（最も早い未判定ノーツ）だけが候補．後ろのノーツは先頭より遅いので，先頭が受付前なら後ろも受付前
            int index = q.Peek();
            double d = songTime - notes[index].Time;
            // 幅が広いと，前のノーツが取られて次のノーツが Miss になることがある．
            // 次のノーツの方が近ければ，前のノーツは Miss にして次のノーツを判定する
            while (rules.PreferNearestNote && q.Head + 1 < q.Indices.Count)
            {
                int next = q.Indices[q.Head + 1];
                double dn = songTime - notes[next].Time;
                if (Math.Abs(dn) >= Math.Abs(d) || dn < -good[next]) break;
                q.Head++;
                Commit(index, JudgeResult.Miss, songTime, output);
                index = next;
                d = dn;
            }
            if (d < -good[index] || d > good[index] + missAfter[index]) return TouchOutcome.NoNote;

            double abs = Math.Abs(d);
            JudgeResult result;
            if (abs <= perfect[index]) result = JudgeResult.Perfect;
            else if (abs <= great[index]) result = JudgeResult.Great;
            else if (abs <= good[index]) result = JudgeResult.Good;
            else return TouchOutcome.OutOfRange;

            q.Head++;
            Commit(index, result, songTime, output);
            return TouchOutcome.Judged;
        }

        void Commit(int index, JudgeResult result, double songTime, List<JudgeEvent> output)
        {
            results[index] = result;
            switch (result)
            {
                case JudgeResult.Perfect: PerfectCount++; Score += rules.PerfectScore; break;
                case JudgeResult.Great: GreatCount++; Score += rules.GreatScore; break;
                case JudgeResult.Good: GoodCount++; Score += rules.GoodScore; break;
                default: MissCount++; break;
            }
            output?.Add(new JudgeEvent(index, notes[index].Lane, result, songTime, songTime - notes[index].Time));
        }
    }
}
