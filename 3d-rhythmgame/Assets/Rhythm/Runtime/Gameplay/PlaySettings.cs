using System;
using UnityEngine;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// 遊び方の設定（人数・数え方）．選んだものは覚えておき，次に起動したときも使う．曲を始めるときに効く（GameSession）．
    /// VR のホーム画面と DebugHud（F10 / F8）から変える．
    /// </summary>
    public static class PlaySettings
    {
        const string PlayersKey = "Arte.Rhythm.Players";
        const string CountKey = "Arte.Rhythm.CountBeats";

        static int? players, countBeats;

        /// <summary>
        /// 遊ぶ人数（1〜3）．譜面の 3 本の流れのうち，この数だけを出す（2026-11-03 ユーザー決定）．
        /// 起動引数 -players N があればそれを使う（覚えている人数より優先）．
        /// </summary>
        public static int Players
        {
            get
            {
                if (players == null)
                {
                    int p = PlayerPrefs.GetInt(PlayersKey, 3);
                    var args = Environment.GetCommandLineArgs();
                    int i = Array.IndexOf(args, "-players");
                    if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) p = n;
                    players = Math.Max(1, Math.Min(3, p));
                }
                return players.Value;
            }
            private set
            {
                players = Math.Max(1, Math.Min(3, value));
                PlayerPrefs.SetInt(PlayersKey, players.Value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>数え方（3 = 3，2，1，はい / 2 = 2，1，はい．CountIn）．</summary>
        public static int CountBeats
        {
            get => countBeats ??= PlayerPrefs.GetInt(CountKey, CountIn.Normal) == CountIn.Hard ? CountIn.Hard : CountIn.Normal;
            private set
            {
                countBeats = value;
                PlayerPrefs.SetInt(CountKey, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>人数を次へ（1 → 2 → 3 → 1）．</summary>
        public static void NextPlayers() => Players = Players % 3 + 1;

        /// <summary>数え方を切り替える（ふつう ⇔ むずかしい）．</summary>
        public static void NextCountBeats() => CountBeats = CountBeats == CountIn.Normal ? CountIn.Hard : CountIn.Normal;
    }
}
