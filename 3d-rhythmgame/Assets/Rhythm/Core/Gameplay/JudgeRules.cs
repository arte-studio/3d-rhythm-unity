using Newtonsoft.Json;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// タッチノーツの判定ルール．既定値は仕様書（docs/SPECIFICATION.md 9.1）の判定幅（2026-10-27 にユーザーが決定）．
    /// 旧版（古代祭 2025-11-01 のシーン設定）の値は Legacy() で作れる．
    ///
    /// d = タッチ時刻 − ノーツ時刻として:
    /// <code>
    ///   d &lt; -GoodSec                : 判定対象外（空タッチ）
    ///   |d| ≤ PerfectSec             : Perfect
    ///   PerfectSec &lt; |d| ≤ GoodSec  : Good
    ///   d &gt; MissAfterSec            : タッチが無ければ Miss
    /// </code>
    /// 判定はタッチした瞬間（押し始め）だけで行うので，触れたままにしても次のノーツは取れない．
    /// </summary>
    public sealed class JudgeRules
    {
        /// <summary>|d| がこれ以下なら Perfect．</summary>
        [JsonProperty("perfectSec")] public double PerfectSec = 0.05;

        /// <summary>|d| がこれ以下なら Good．判定の受付開始も -GoodSec．</summary>
        [JsonProperty("goodSec")] public double GoodSec = 0.15;

        /// <summary>ノーツ時刻からこれだけ過ぎてもタッチが無ければ Miss．</summary>
        [JsonProperty("missAfterSec")] public double MissAfterSec = 0.15;

        /// <summary>同じレーンへの連続タッチを無視する時間（旧 touchInputCooldown）．</summary>
        [JsonProperty("laneCooldownSec")] public double LaneCooldownSec = 0.1;

        /// <summary>ノーツの案内（受付色の点滅）を始めるのはノーツ時刻の何秒前か（旧 notesignalTime）．</summary>
        [JsonProperty("spawnLeadSec")] public double SpawnLeadSec = 3.0;

        [JsonProperty("perfectScore")] public int PerfectScore = 100;
        [JsonProperty("goodScore")] public int GoodScore = 50;

        /// <summary>
        /// タッチを受け付ける時間幅の終わり（旧 HandleTouchInput の goodRange + missRange）．
        /// 実際には MissAfterSec を過ぎたノーツは先に Miss になるので，効くのは MissAfterSec &gt; GoodSec のときだけ．
        /// </summary>
        [JsonIgnore] public double AcceptUntilSec => GoodSec + MissAfterSec;

        /// <summary>
        /// 旧版（古代祭のシーン設定）の判定．Perfect = [-2.0, +0.5] 秒，Good = [-3.0, -2.0) 秒，得点 5/3．
        /// 遅れ側は MissAfterSec で切れるので，PerfectSec の遅れ側（+0.5〜+2.0）は使われない．
        /// </summary>
        public static JudgeRules Legacy() => new JudgeRules
        {
            PerfectSec = 2.0, GoodSec = 3.0, MissAfterSec = 0.5, LaneCooldownSec = 0.1, SpawnLeadSec = 3.0, PerfectScore = 5, GoodScore = 3,
        };
    }
}
