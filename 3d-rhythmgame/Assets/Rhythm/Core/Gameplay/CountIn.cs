namespace Arte.Rhythm.Gameplay
{
    /// <summary>
    /// 何拍前から数えるか（2026-11-05 ユーザー: 数え始めではなく数え終わりの合図をそろえる）．
    /// ふつうは 3，2，1，はい，むずかしいは 2，1，はい．どちらも終わりは 1，はい．
    /// 遊ぶ譜面からは，前のノーツから 数える拍 + 1 あかないノーツを外す（PlayChart．前の はい と次の数え始めが重ならない）．
    /// </summary>
    public static class CountIn
    {
        public const int Normal = 3;
        public const int Hard = 2;

        public static string Name(int beats) => beats <= Hard ? "2，1，はい（むずかしい）" : "3，2，1，はい（ふつう）";
    }
}
