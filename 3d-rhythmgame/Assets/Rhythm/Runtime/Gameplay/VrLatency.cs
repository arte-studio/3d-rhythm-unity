using UnityEngine;

namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// VR で手がむぎゅっとに触れた時刻から引く遅れ（ms）．音がヘッドセットに届くまでの遅れや手の追跡の遅れで，
    /// 曲に合わせて叩いても遅れて判定されるのを直す．本物のむぎゅっと（DeviceLink）のタッチには効かない（あちらは SongConductor.inputLatencySec）．
    ///
    /// PC から Quest Link で遊ぶときと Quest 単体（Android）では遅れが違うので，別々の値を覚えておく．VR のホーム画面から変えられる．
    /// 既定の 115ms は 2026-11-04 のテストプレイ（PC + Quest Link，49 回）で叩いた時刻の遅れの真ん中の値．Quest 単体の値はまだ測っていない．
    /// </summary>
    public static class VrLatency
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string Key = "Arte.Rhythm.VrLatencyMs.Standalone";
        public const int DefaultMs = 115;
        public const string Where = "Quest 単体";
#else
        const string Key = "Arte.Rhythm.VrLatencyMs.Link";
        public const int DefaultMs = 115;
        public const string Where = "PC + Quest Link";
#endif

        static int? ms;

        public static int Ms
        {
            get => ms ??= PlayerPrefs.GetInt(Key, DefaultMs);
            set
            {
                ms = Mathf.Clamp(value, -200, 500);
                PlayerPrefs.SetInt(Key, ms.Value);
                PlayerPrefs.Save();
            }
        }

        public static double Sec => Ms / 1000.0;
    }
}
