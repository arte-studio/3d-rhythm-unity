# 3d-rhythm-unity

3D リズムゲーム（工大祭の LED のリング）の PC 側．Unity のゲーム本体と，その周りの道具・資料．
マイコン（ESP32-S3）のファームウェアは別のリポジトリ（3d-rhythm-lowlayer-program）．

## 置き場所

```
3d-rhythmgame/            Unity のプロジェクト（Unity 6000.0.55f1，URP）
  Assets/Rhythm/          ゲーム本体
    Core/                 Unity を使わない部分（判定・譜面・テンポ・配線・通信）．dotnet test で確かめられる
    Runtime/              Unity 上で動く部分（曲の再生・LED の描画・Viewer・VR・実機との通信）
    Editor/               エディタの道具（譜面エディタ・演出・ビルド）
    Songs/ Effects/ Data/ Scenes/ Sounds/ Tests/
  Assets/Legacy/          旧版の素材（LED テープと筒のモデル）
  Assets/Settings/ XR/    URP と VR の設定（Unity が管理する）
tools/                    Unity の外の道具（テスト，コンパイルの確認，実機とつなぐ，拍の解析，演出の評価）
docs/                     仕様書（SPECIFICATION.md）と手引き
sandbox/                  試作と資料（後から見るために残してある）
```

くわしくは `docs/SPECIFICATION.md` の「4. ソフトウェア」．

## よく使うこと

| したいこと | やり方 |
| --- | --- |
| 遊ぶ・演出を見る | Unity で `Assets/Rhythm/Scenes/Play.unity` を開いて Play |
| Core のテスト | `dotnet test tools/CoreTests` |
| Unity を開かずにコンパイルを確かめる | `python tools/unity-check/check.py`（Quest 単体向けは `--android`） |
| 参照が切れていないかを確かめる | Unity の `Tools / Arte Rhythm / Check Project` |
| Quest 単体の APK を作る | Unity の `Tools / Arte Rhythm / VR / Build Quest APK` |
| 実機とつなぐ（Unity なし） | `dotnet run --project tools/LinkTool -- canvas 30` |

旧版（2025 年の工大祭のもの）はタグ `legacy-v1`．旧版の説明は `docs/legacy/`．
