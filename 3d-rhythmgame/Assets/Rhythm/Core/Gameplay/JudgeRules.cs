using Newtonsoft.Json;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// タッチノーツの判定ルール．既定値は古代祭（2025-11-01）のシーン設定（GameManager のインスペクタ値）．
    ///
    /// 旧版の判定を時間軸で書くと次のとおり（d = タッチ時刻 − ノーツ時刻）:
    /// <code>
    ///   d &lt; -GoodSec                : 判定対象外（空タッチ）
    ///   -GoodSec ≤ d &lt; -PerfectSec  : Good
    ///   -PerfectSec ≤ d ≤ MissAfterSec : Perfect（|d| ≤ PerfectSec かつ見逃し前）
    ///   d &gt; MissAfterSec            : タッチが無ければ Miss
    /// </code>
    /// 既定値では Perfect = [-2.0, +0.5] 秒，Good = [-3.0, -2.0) 秒．遅れ側は MissAfterSec で切れるので，
    /// PerfectSec の遅れ側（+0.5〜+2.0）は実際には使われない．値を変えるとゲーム仕様が変わるので注意．
    /// </summary>
    public sealed class JudgeRules
    {
        /// <summary>|d| がこれ以下なら Perfect（旧 perfectRange）．</summary>
        [JsonProperty("perfectSec")] public double PerfectSec = 2.0;

        /// <summary>|d| がこれ以下なら Good．判定の受付開始も -GoodSec（旧 goodRange）．</summary>
        [JsonProperty("goodSec")] public double GoodSec = 3.0;

        /// <summary>ノーツ時刻からこれだけ過ぎてもタッチが無ければ Miss（旧 missRange）．</summary>
        [JsonProperty("missAfterSec")] public double MissAfterSec = 0.5;

        /// <summary>同じレーンへの連続タッチを無視する時間（旧 touchInputCooldown）．</summary>
        [JsonProperty("laneCooldownSec")] public double LaneCooldownSec = 0.1;

        /// <summary>ノーツを出現させる（合図を出し始める）のはノーツ時刻の何秒前か（旧 notesignalTime）．</summary>
        [JsonProperty("spawnLeadSec")] public double SpawnLeadSec = 3.0;

        [JsonProperty("perfectScore")] public int PerfectScore = 5;
        [JsonProperty("goodScore")] public int GoodScore = 3;

        /// <summary>
        /// タッチを受け付ける時間幅の終わり（旧 HandleTouchInput の goodRange + missRange）．
        /// 実際には MissAfterSec を過ぎたノーツは先に Miss になるので，効くのは MissAfterSec &gt; GoodSec のときだけ．
        /// </summary>
        public double AcceptUntilSec => GoodSec + MissAfterSec;
    }
}
