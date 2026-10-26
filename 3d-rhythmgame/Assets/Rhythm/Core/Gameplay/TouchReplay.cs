using System.Collections.Generic;
using System.Linq;
using Arte.Rhythm.Charts;
using Newtonsoft.Json;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>タッチ1回の記録．</summary>
    public struct TouchRecord
    {
        [JsonProperty("t")] public double Time;
        [JsonProperty("lane")] public int Lane;

        public TouchRecord(double time, int lane)
        {
            Time = time;
            Lane = lane;
        }
    }

    /// <summary>
    /// 本番中のタッチを記録し，あとで同じ判定を再現する．
    /// 判定の調整（判定幅の見直し）を，実際のプレイの記録で試せるようにするためのもの．
    /// </summary>
    public sealed class TouchLog
    {
        [JsonProperty("chart")] public string ChartName;
        [JsonProperty("touches")] public List<TouchRecord> Touches = new List<TouchRecord>();

        public void Add(double songTime, int lane) => Touches.Add(new TouchRecord(songTime, lane));

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

        public static TouchLog FromJson(string json) => JsonConvert.DeserializeObject<TouchLog>(json);
    }

    public sealed class ReplayResult
    {
        public List<JudgeEvent> Events = new List<JudgeEvent>();
        public int Score;
        public int Perfect;
        public int Good;
        public int Miss;
    }

    public static class TouchReplay
    {
        /// <summary>記録したタッチを時刻順に判定し直す．endTime まで時刻を進めて残りを Miss にする．</summary>
        public static ReplayResult Run(Chart chart, JudgeRules rules, IEnumerable<TouchRecord> touches, double endTime)
        {
            var judge = new TouchJudge(chart, rules);
            var result = new ReplayResult();
            double last = double.NegativeInfinity;
            foreach (var t in touches.OrderBy(t => t.Time))
            {
                judge.Touch(t.Lane, t.Time, result.Events);
                last = t.Time;
            }
            judge.Advance(System.Math.Max(endTime, last), result.Events);
            result.Score = judge.Score;
            result.Perfect = judge.PerfectCount;
            result.Good = judge.GoodCount;
            result.Miss = judge.MissCount;
            return result;
        }
    }
}
