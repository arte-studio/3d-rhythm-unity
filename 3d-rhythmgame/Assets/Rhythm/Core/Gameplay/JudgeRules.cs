using System;
using Arte.Rhythm.Charts;
using Newtonsoft.Json;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// タッチノーツの判定ルール．既定値は Perfect ±0.1 秒，Great ±0.25 秒，Good ±1 拍（2026-11-03 にユーザーが決定．Great は前の Good．VR で遊ぶと
    /// 旧仕様書（docs/legacy/SPECIFICATION-v1.md 9.1）の ±0.05 / ±0.15 秒では厳しすぎた．本番と VR で共通．今の仕様は docs/SPECIFICATION.md 5 章）．
    /// 旧版（工大祭 2025-11-01 のシーン設定）の値は Legacy() で作れる．
    ///
    /// d = タッチ時刻 − ノーツ時刻として:
    /// <code>
    ///   d &lt; -GoodSec                : 判定対象外（空タッチ）
    ///   |d| ≤ PerfectSec             : Perfect
    ///   PerfectSec &lt; |d| ≤ GoodSec  : Good
    ///   d &gt; MissAfterSec            : タッチが無ければ Miss
    /// </code>
    /// 判定はタッチした瞬間（押し始め）だけで行うので，触れたままにしても次のノーツは取れない．
    ///
    /// 幅は ms（xxxMs）と拍（xxxBeats，その曲のテンポでノーツの所の 1 拍の長さを掛ける）のどちらでも書ける．
    /// 両方を書いたら長い方を使う．拍を書かなければ ms だけ（拍を書くときは ms を 0 にすれば拍だけ）．
    /// </summary>
    public sealed class JudgeRules
    {
        /// <summary>|d| がこれ以下なら Perfect（秒．JSON では perfectMs）．</summary>
        [JsonIgnore] public double PerfectSec = 0.1;
        [JsonProperty("perfectBeats", NullValueHandling = NullValueHandling.Ignore)] public double? PerfectBeats;

        /// <summary>|d| がこれ以下なら Great（JSON では greatMs）．</summary>
        [JsonIgnore] public double GreatSec = 0.25;
        [JsonProperty("greatBeats", NullValueHandling = NullValueHandling.Ignore)] public double? GreatBeats;

        /// <summary>|d| がこれ以下なら Good．判定の受付開始も -GoodSec（JSON では goodMs）．</summary>
        [JsonIgnore] public double GoodSec = 0;
        [JsonProperty("goodBeats", NullValueHandling = NullValueHandling.Ignore)] public double? GoodBeats = 1;

        /// <summary>ノーツ時刻からこれだけ過ぎてもタッチが無ければ Miss（JSON では missAfterMs）．Good の遅れ側の端と同じにしておく．</summary>
        [JsonIgnore] public double MissAfterSec = 0;
        [JsonProperty("missAfterBeats", NullValueHandling = NullValueHandling.Ignore)] public double? MissAfterBeats = 1;

        /// <summary>
        /// 同じレーンの次のノーツの方がタッチに近ければ，前のノーツを Miss にして次のノーツを取る．
        /// Good が 1 拍と広いので，これが無いと 1 拍おきに同じむぎゅっとが続くところで 1 つずつずれていく．旧版は無し．
        /// </summary>
        [JsonProperty("preferNearestNote")] public bool PreferNearestNote = true;

        /// <summary>
        /// 同じむぎゅっとの前のノーツからこれだけ（拍）あかないタッチノーツは遊ぶ譜面から外す（SongData.ToChart）．0 で外さない．
        /// 無し（既定）なら 数える拍 + 1（3，2，1，はい なら 4 拍．前のノーツの はい と次のノーツの数え始めが重ならない．2026-11-05 ユーザー）．
        /// </summary>
        [JsonProperty("sameLaneMinGapBeats", NullValueHandling = NullValueHandling.Ignore)] public double? SameLaneMinGapBeats;

        /// <summary>同じ流れの前のノーツからこれだけ（拍）あかないタッチノーツも外す（同時のものは残す）．0 で外さない．無しなら 数える拍 + 1．</summary>
        [JsonProperty("sameFlowMinGapBeats", NullValueHandling = NullValueHandling.Ignore)] public double? SameFlowMinGapBeats;

        /// <summary>
        /// Miss を見せる（演出・判定の文字）のはノーツ時刻からどれだけ後か（JSON では missShowAfterMs / missShowAfterBeats）．
        /// 判定そのもの（MissAfter）は変えない．Miss がすぐ出るとリズムが乱されるので，既定は 1 拍後．Perfect / Great / Good はすぐ見せる．
        /// </summary>
        [JsonIgnore] public double MissShowAfterSec = 0;
        [JsonProperty("missShowAfterBeats", NullValueHandling = NullValueHandling.Ignore)] public double? MissShowAfterBeats = 1;

        [JsonProperty("perfectMs")] double PerfectMs { get => PerfectSec * 1000; set => PerfectSec = value / 1000; }
        [JsonProperty("greatMs")] double GreatMs { get => GreatSec * 1000; set => GreatSec = value / 1000; }
        [JsonProperty("goodMs")] double GoodMs { get => GoodSec * 1000; set => GoodSec = value / 1000; }
        [JsonProperty("missAfterMs")] double MissAfterMs { get => MissAfterSec * 1000; set => MissAfterSec = value / 1000; }
        [JsonProperty("missShowAfterMs")] double MissShowAfterMs { get => MissShowAfterSec * 1000; set => MissShowAfterSec = value / 1000; }

        /// <summary>ノーツ（時刻 noteTime）の所での幅（秒）．tempo が無ければ 120 BPM．</summary>
        public double PerfectAt(TempoMap tempo, double noteTime) => Window(PerfectSec, PerfectBeats, tempo, noteTime);
        public double GreatAt(TempoMap tempo, double noteTime) => Window(GreatSec, GreatBeats, tempo, noteTime);
        public double GoodAt(TempoMap tempo, double noteTime) => Window(GoodSec, GoodBeats, tempo, noteTime);
        public double MissAfterAt(TempoMap tempo, double noteTime) => Window(MissAfterSec, MissAfterBeats, tempo, noteTime);
        public double MissShowAfterAt(TempoMap tempo, double noteTime) => Window(MissShowAfterSec, MissShowAfterBeats, tempo, noteTime);

        static double Window(double sec, double? beats, TempoMap tempo, double noteTime)
        {
            if (beats == null) return sec;
            var map = tempo ?? Default;
            double b = map.BeatOf(noteTime);
            double secPerBeat = map.TimeOf(b + 0.5) - map.TimeOf(b - 0.5);
            return Math.Max(sec, beats.Value * secPerBeat);
        }

        static readonly TempoMap Default = new TempoMap(null);

        /// <summary>同じレーンへの連続タッチを無視する時間（旧 touchInputCooldown）．</summary>
        [JsonProperty("laneCooldownSec")] public double LaneCooldownSec = 0.1;

        /// <summary>ノーツの案内（受付色の点滅）を始めるのはノーツ時刻の何秒前か（旧 notesignalTime）．</summary>
        [JsonProperty("spawnLeadSec")] public double SpawnLeadSec = 3.0;

        [JsonProperty("perfectScore")] public int PerfectScore = 100;
        [JsonProperty("greatScore")] public int GreatScore = 50;
        [JsonProperty("goodScore")] public int GoodScore = 20;

        /// <summary>
        /// 旧版（工大祭のシーン設定）の判定．Perfect = [-2.0, +0.5] 秒，Good = [-3.0, -2.0) 秒，得点 5/3（ここでは旧版の Good は Great として出る）．
        /// 遅れ側は MissAfterSec で切れるので，PerfectSec の遅れ側（+0.5〜+2.0）は使われない．
        /// </summary>
        public static JudgeRules Legacy() => new JudgeRules
        {
            PerfectSec = 2.0, GreatSec = 3.0, GoodSec = 3.0, GoodBeats = null, MissAfterSec = 0.5, MissAfterBeats = null, LaneCooldownSec = 0.1, SpawnLeadSec = 3.0,
            PerfectScore = 5, GreatScore = 3, GoodScore = 3, MissShowAfterBeats = null, PreferNearestNote = false,
        };
    }
}
