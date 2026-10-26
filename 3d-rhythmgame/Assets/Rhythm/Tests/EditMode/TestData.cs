using System;
using System.IO;

namespace Arte.Rhythm.Tests
{
    /// <summary>
    /// テスト用データの場所を探す．Unity のテストランナー（カレントディレクトリ = プロジェクト直下）と
    /// dotnet test（カレントディレクトリ = ビルド出力先）の両方から使えるよう，上の階層へ順にたどる．
    /// </summary>
    static class TestData
    {
        public static string HardwareDir() => Find(Path.Combine("Assets", "Rhythm", "Data", "Hardware"));

        /// <summary>旧版の譜面などが入っている Assets/Resources．</summary>
        public static string LegacyResourcesDir() => Find(Path.Combine("Assets", "Resources"));

        static string Find(string relative)
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, relative);
                    if (Directory.Exists(candidate)) return candidate;
                    candidate = Path.Combine(dir.FullName, "3d-rhythmgame", relative);
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            throw new DirectoryNotFoundException(relative);
        }
    }
}
