# tools

Unity の外で使う道具．Unity のプロジェクト（3d-rhythmgame）の外に置いてあるので Unity は読まない．

| フォルダ | 中身 | 使い方 |
| --- | --- | --- |
| `CoreTests` | Core（Unity を使わない部分）とテストを .NET で動かす | リポジトリの一番上で `dotnet test tools/CoreTests` |
| `unity-check` | Unity を開かずに Assets/Rhythm のコンパイルを確かめる（.cs はフォルダから拾う） | `python tools/unity-check/check.py`（`--android` で Quest 単体向け）．一度 Unity で開いて csproj ができている必要がある |
| `LinkTool` | Unity なしで実機とつなぐ（テスト表示，通信の計測，ミラー） | `dotnet run --project tools/LinkTool -- canvas 30` |
| `BeatTool` | 新しい曲の拍・小節の頭・区切りの候補を出す，譜面のレーンや流れを直す | `docs/authoring/beat-finding.md`．譜面を直すものは 3d-rhythmgame で `py ../tools/BeatTool/….py` |
| `EffectEval` | 演出の評価（点滅の安全，音と光の同期，盛り上がり） | `docs/effects/ai-proposals.md`．3d-rhythmgame で `sh ../tools/EffectEval/cycle.sh v8` など |
