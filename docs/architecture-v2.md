# アーキテクチャ v2（リファクタ計画）

作業ブランチ: `refactor/v2`（両リポジトリ）．リファクタ前の状態はタグ `legacy-v1`．

## 目的

1. 演出の質を上げ，美大生がコードを読まずに演出を作れるようにする
2. Unity での実機の再現度を上げる（光の広がり，暗幕の部屋，観客がリングの内側に立つ視点，将来の VR）
3. 処理を速くする（本番 PC のノート Ryzen 7 5800U 内蔵 GPU で 60fps 固定）
4. 譜面を編集しやすくする
5. 演出・映像・ノーツを音楽と同期させる

**ゲーム仕様（判定，ノーツの種類と挙動）は変えない．** 変える場合は提案してから．

## 層の構成

```
Data   : hardware.json / setups/*.json / 曲データ（音源・テンポマップ・セクション・ノーツ・演出キュー）
Core   : Unity 非依存の C#（ハード配置・配線，判定，時刻管理）。dotnet test で検証できる
Runtime: Unity。時刻源（オーディオ DSP 時刻），演出の評価（GPU），UDP 送信
Viewer : 送信と同じ LED データを受け取って描く「仮想 ESP32」。HDR・ブルーム・拡散・フォグ・VR
Authoring: Timeline ベースの譜面・演出エディタ，プレビュー動画の書き出し
```

## 決定事項

| 日付 | 決定 | 理由 |
|---|---|---|
| 2026-10-26 | CRI ADX2 を使わない（プラグイン削除） | 譜面エディタ機能はなく，Unity Timeline のほうが波形表示・スクラブ・ノーツ配置に向く．ADX を入れると音源管理が Unity と Atom Craft に分かれる．拍の同期は自前のテンポマップで，正確な再生時刻はオーディオ時刻＋遅延キャリブレーションで足りる．時刻源は差し替え可能にしておく．当時も導入を諦めた経緯あり．Atom Craft プロジェクト（`gamemusic*/`）は資料として残す |
| 2026-10-26 | 寸法・配線・設営差分をデータ化（docs/hardware/README.md） | 設営ごとにモジュール数や外すストリップが違う．コードに散っていた定数を一か所にまとめ，シミュレーション・送信・譜面エディタで共有する |
| 2026-10-26 | マイコンは ESP32-S3 に統一 | ユーザー確認 |
| 2026-10-26 | 本番はノート PC をローカルで使う | 遠隔 PC だと UDP とタッチ入力がインターネット越しになり，遅延・揺れ・切断が判定とショーに直結する．デスクトップ（RTX 3060）は開発・Viewer・VR・プレビュー書き出し用 |

## フェーズ

| # | 内容 | 状態 |
|---|---|---|
| P0 | 基準値の計測（開発ビルド + `-probe` → `probe-<PC名>.json`），寸法の実測 | 寸法は反映済み．旧版はエディタでしか動かず自動計測は打ち切り（下記） |
| P1 | ハード構成データ＋設営プロファイル，出力モデル，Core の asmdef とテスト | 作成済み（旧コードと一致をテストで保証） |
| P2 | Core（判定・時刻管理）の書き直し，テスト，リプレイ入力 | 判定・曲時刻・リプレイ作成済み（旧版との一致をテストで保証）．Runtime への接続は P3 で |
| P3 | 曲データ形式＋Timeline での譜面・演出編集環境＋音楽との同期 | song.json，予約再生と時刻補間，動画/Timeline 同期，譜面エディタ，確認用シーン（`docs/authoring/chart-editor.md`）．演出クリップは P5/P7 |
| P4 | UDP v2＋ファーム v2（S3 に統一） | プロトコル v2，ファーム v2.1，DeviceLink・接続モニタ・テスト表示，LinkTool~．実機 1 台で接続・時刻合わせ・表示を確認（タッチは未接続） |
| P5 | LED 描画の GPU 化（ノート PC で 60fps 固定） | 送信の圧縮，明るさを PC で掛ける（ファーム v2.2），色の曲線（linear/srgb），演出用キャンバス（GPU → LED），シミュレーション表示（HDR），ノーツ用 LED（旧版と同じ動き），ノーツモジュールの置き場所（仮） |
| P6 | Viewer（光の広がり，暗幕の部屋，内側からの視点，VR の準備） | 部屋（床・暗幕・テープ本体を LED の光で照らす），カメラ操作，別 PC へのミラー（Viewer.exe）．VR は未（下記） |
| P7 | 演出の制作環境とプレビュー書き出し，美大生向けテンプレート | Timeline の LED 演出トラック，テンプレート 7 種と見本，プレビュー動画の書き出し，手引き（`docs/authoring/led-effects.md`） |
| P8 | 既存演出の移行，仕様書の更新 | 曲の区切りとテンポ，Timeline を小節・拍で扱う仕組み，古代祭版の演出の下書き（`docs/effects/kodaisai-2025.md`），仕様書 v2（`docs/SPECIFICATION.md`．旧版は `docs/legacy/SPECIFICATION-v1.md`） |

## P0 の基準値について

旧版は ObjectRelocation がファイルを直接読むためビルドでは動かず，エディタのプレイモードでしか計測できない．
自動計測（`BaselineProbe.RunEditorProbe`）は完了しなかったので打ち切った．参考値として，
開発用デスクトップのエディタで Stats が 2.1 FPS（描画スレッド 477ms，Batches 4877，ポリゴン約350万）を示した．
ただし本番ではここまで遅くなかった（利用者の記憶）ので，エディタ固有の負荷を含む値として扱う．
新旧の比較は，新しい Runtime がビルドで動くようになってから同じプローブで行う．

## P2: 判定の仕様

`Assets/Rhythm/Core/Gameplay/`．**2026-10-27 にユーザーの決定で旧仕様書（docs/legacy/SPECIFICATION-v1.md 9.1）の判定幅に変えた**（「一旦仕様書通り．子ども向けなので多少緩めてもよい．タッチしっぱなしは禁止」）．
d = タッチ時刻 − ノーツ時刻として，

- Perfect: |d| ≦ 0.05，Good: |d| ≦ 0.15，d > +0.15 までタッチが無ければ Miss．スコアは Perfect 100 / Good 50
- 判定は押し始め（タッチの立ち上がり）だけで行うので，触れたままでは次のノーツは取れない
- 同じレーンの 0.1 秒以内の再タッチは無視
- 曲ごとに緩めたいときは song.json の `rules` に書く．旧版（古代祭のシーン設定）の値は `JudgeRules.Legacy()`
  （Good: -3.0 ≦ d < -2.0，Perfect: -2.0 ≦ d ≦ +0.5，d > +0.5 で Miss，スコア 5 / 3．3 秒前から受け付ける，ほぼ押せば当たる判定だった）
- 1回のタッチで判定するのは，そのレーンの最も早い未判定ノーツだけ
- Connect ノーツは判定しない（旧版と同じ）

判定の仕組みの旧版との違いは，フレームではなくタッチ時刻で判定することと，連続タッチ防止を秒で数えること
（旧版は 1 フレームごとに 0.02 秒減らしていたので，60fps では約 0.083 秒だった）．
`GameplayTests` で旧版の判定をフレーム単位でまねたものと比べ，同じルール（`Legacy()`）なら同じ結果になることを確かめている．

### レーン番号

内部のレーン番号は0始まりに統一する（設営プロファイル・判定・送信すべて）．譜面制作の画面では1始まりで見せてよい（`Chart.LaneLabel`）．

旧譜面は作者が1始まりのつもりで書いていたが，旧版のゲームは0始まりとして扱っていたため，本番ではレーンが1つずれていた．
`LegacyChartJson` は読み込み時に1を引いて直す．

直したあとも，古代祭の譜面はレーン 0〜14（15本）を使い，配線はレーン 0〜10（11本）なので，
レーン 11〜14（画面上は 12〜15）の 52 ノーツ（全 186）は押せる場所が無い．工華祭の譜面も 47/161 が同様．

## P3: 曲データと同期

- 1曲 = `Assets/Rhythm/Songs/<曲ID>/` に song.json（譜面・テンポ・補正値）と SongAsset（音声・動画・Timeline の参照）．
- 曲時刻の基準は音声．`SongConductor` が `PlayScheduled` で予約し，その DSP 時刻を曲頭にする．
  DSP 時刻は `AudioClock` で実時間補間して滑らかにする．
- 演出動画は `VideoTimeReference.ExternalTime`，Timeline は手動評価で，曲時刻（+ LED の遅れ）に合わせる．
  旧版の動画は曲と無関係にループ再生していた．動画の音声は GameMusic.wav より約 0.017 秒早い（相互相関で測定）．
- 譜面の編集は Timeline ではなく専用の譜面エディタにした（拍のグリッド・スナップ・タップ入力のため）．
  演出は Timeline で作り，同じ曲時刻で再生する．
- 古代祭の曲のテンポは未測定（自動推定の候補は 76 / 152 BPM 付近）．

## P4: 実機との通信（プロトコル v2）

仕様は `3d-rhythm-lowlayer-program/docs/protocol-v2.md`．要点:

- タッチはデバイスが 1ms ごとに読み，変化した時刻（デバイス時刻）を付けて送る．PC は PING/PONG の時刻合わせで
  `AudioClock.Realtime` に直して `GameSession.Touch` に渡す．旧版は 50ms ごとの状態を届いた時刻で判定していた．
- LED はフレーム番号と「このフレームで送るピン」を付けて送り，デバイスは揃ったら表示する．
  旧ファームは 4 系統揃うまで表示しなかったため，2 系統しか送られないデバイス 7 は表示が更新されていなかったと思われる（要実機確認）．
- 表示の遅れ（送信 → show()）を PONG から実測できる．`SongConductor.outputLatencySec` を決める材料にする．
- デバイス番号と Wi-Fi は基板の NVS に保存．番号未設定の基板は接続モニタ（F2）から割り当てられる．

| ファイル | 役割 |
|---|---|
| `Core/Net/ArteProtocol.cs` | パケットの読み書き（ファームの ArteProtocol.h と同じバイト列をテストで照合） |
| `Core/Net/ClockSync.cs`，`TouchEventTracker.cs` | 時刻合わせ，タッチの重複除去 |
| `Core/Net/LinkHost.cs` | PC 側の通信本体（Unity 非依存．localhost の仮想デバイスとつないでテスト） |
| `Core/Net/LedFrame.cs` | 送る RGB（デバイス × ピン，配線順）．設営から送るピンと長さを決める |
| `Core/Net/FrameEncoder.cs`，`PixelCodec.cs` | 送信量を減らす（変わっていないピンを送らない，PackBits，明るさで同じ出力になる値をそろえる．v2.1 のファームだけ） |
| `Core/Net/VirtualDevice.cs` | PC 上の仮想デバイス（テスト用．P6 の Viewer でも使う） |
| `Runtime/Net/DeviceLink.cs` | Unity 側の窓口．タッチ → レーン → 判定，Frame の送信 |
| `Runtime/Net/DeviceMonitorHud.cs` | 接続状態（F2）．往復時間，表示 fps，欠けたフレーム，表示の遅れ，タッチ |
| `Runtime/Net/LedTestPattern.cs` | 設営確認用の表示（配線の先頭，ジグザグと列の並び，ノーツモジュール） |

実機での通信の様子（家庭用ルーター，2.4GHz，1 台）: 圧縮なしの 60fps（約 2.8Mbps）ではルーターに詰まって往復 1〜4 秒の遅れが出た．
圧縮で止まった絵や一部だけ動く絵の送信量は大きく減るが，全 LED が毎フレーム変わる絵は 6 割程度までしか減らない．
本番は強いルーターを用意するので，混み具合で送る頻度を下げる仕組み（`LinkOptions.AdaptiveRate`）は既定では切ってある．
`dotnet run --project 3d-rhythmgame/LinkTool~ -- rainbow 30 [--raw] [--adaptive]` で比べられる．

旧版の `UdpController` とは同じポート（9999）を使うので，同じシーンに置かない．旧ファームとは通信できない（全台 v2 に書き換える）．

## P5: LED の描画

- **演出用キャンバス**（`Runtime/Render/LedCanvas.cs`）: 幅 = ストリップの列数，高さ = 1 本の LED 数の RenderTexture（1 ピクセル = LED 1 個，y = 0 が下端）．
  カメラ・動画・シェーダー（`LedCanvasEffect`）など何で描いてもよく，非同期で読み戻して `CanvasPacker` が配線順（ジグザグ・外したストリップ）に詰める．
  読み戻しのぶん LED に出るのは描いてから 1〜2 フレーム後．キャンバスの色は画面で見える値（sRGB の 8bit）．
- **LED に送る値**: `LedOutputModel.Encode`（色の曲線 → 明るさ → 1 回だけ丸める）．曲線は設営データの `outputCurve`
  - `linear`（既定，旧版と同じ）: 色の値に比例して光る．LED の光量は送った値にほぼ比例し，人の目は暗い所の差に敏感なので，画面の絵より中間が明るく（白っぽく）見える
  - `srgb`: 色を画面用の値とみなし光量に直して送る．画面の絵に近い見た目になるが，明るさ 50（51 段階）だと暗い色が 0 や 1 に潰れやすい
  - 接続モニタ（F2）で切り替えて実機で見比べられる．どちらにするかは未決定
- **シミュレーション表示**（`LedPreview`）: 実機に送るのと同じ値を光量（`OutputToLinear`，送った値に比例．`hardware.json` の `output` で補正できるが未計測）として
  HDR で描き，ブルームとトーンマップで画面に出す．全 LED を 1 回の描画で描く．旧版は送る値をそのまま画面の色にしていたため，明るさ（50）が掛からず約 5 倍明るく，中間は実機より暗く見えていた．
- **速度**（`Builds/Play/Play.exe -probe`，1920×1080）: 開発用デスクトップ（i7-11700 / RTX 3060）で 1 フレーム平均 1.9ms（99% 4.0ms），GC 割り当て 0．本番ノート PC では未計測．
- 見た目の自動確認: `Play.exe -screenshot <png> [-screenshot-delay 秒] [-quit-after 秒]`．ビルドは `Tools/Arte Rhythm/Build Play Player`．
- **ノーツ用 LED**（`Core/Gameplay/NoteLedRenderer.cs`，`Runtime/Render/NoteLedPainter.cs`）: 旧仕様書（docs/legacy/SPECIFICATION-v1.md 4.7，7.1，7.1.1）に沿う（2026-10-27 ユーザー決定．今の仕様は docs/SPECIFICATION.md 6 章）．
  - 案内: ノーツ時刻の 3 秒前から青の点滅（周期 0.5 秒，ノーツ時刻にちょうど点く位相）
  - 判定: Perfect 緑（最初の 0.2 秒は虹色の回転），Good 黄，Miss 赤．0.5 秒でフェードアウト
  - 判定したらそのレーンのつながるノーツ用 LED に判定色の光を流す（3 秒．旧版の計算どおり）
  - 曲の間，全レーンのつながるノーツ用 LED を色相を変えながら塗っていく（旧版の色の範囲，60 個/秒）．曲が終わったら消す
  - 旧版の実装からの修正（ユーザーに報告済み）: 判定色が次のノーツまで残っていた，Perfect の虹色が画面上の 3D オブジェクトにしか出ていなかった，
    色相の演出がレーン 0 にしか出ずフレームレートで速さが変わっていた，画面のノーツの 3D オブジェクトがレーンと関係なく使い回されていた．
    色は旧版の実装（受付中 黄，Perfect magenta，Good 白，Miss cyan）から仕様書の色に変えた
- **ノーツモジュールの置き場所**: 設営データの `modules`（レーンごとのむぎゅっとの中心と，つながるノーツ用テープの両端）．
  kodaisai-1101 は旧版の配置ファイル `object_positions_kokasai.json` から `Tools/Arte Rhythm/Import Module Placement` で作った仮のもの
  （レーンとの対応は旧版に無いので，モジュールがつながるデバイスの演出用ストリップに近い位置を選んだ）．
  決まりごと（ユーザー確認済み）: 演出用 LED と同じく内側から見て左から右へレーン番号が進む．つながるノーツ用テープは次のむぎゅっとへ伸び，0 番目の LED は若い番号の側．

## P6: Viewer

- **何を描くか**: 「LED に送った値」を光量にしたもの（`LedPreview`）．値の取り方は 2 通り
  - Local: 同じシーンの DeviceLink.Frame（Play シーン）
  - Mirror: 別の PC の LinkHost が送る PIXELS を受け取る（`Viewer.exe`，ポート 8899）．送る側は `DeviceLink.mirrorTo`，
    起動引数 `-mirror IP[:ポート]`，`LinkTool~ --mirror IP`．実機がつながっていないデバイスの分も送るので，実機が無くても見られる
- **LED の光り方**: LED はリングの内側（中心の縦の軸）へ水平に向けて付けてある（ユーザー確認）．WS2812B の視野角は 120°（半値角 ±60°．
  販売店の仕様．Worldsemi のデータシートには載っていない）で，これは正面から cos で弱まる配光と一致するので，
  正面が一番明るく横へ cos で暗くなり，裏には出ないとした．LED の点の見え方（外から見ると手前の LED は暗い）と，部屋を照らす光の両方に使う．
  データシートの光度は赤 390〜420，緑 660〜720，青 180〜200 mcd（`hardware.json` の `output.channelLuminance` で色ごとの明るさを変えられるが，
  画面の原色と LED の原色の違いを考える必要があるので，まだ 1:1:1 のまま）
- **部屋**（`ViewerRoom`，`Shaders/LitByLeds.shader`）: 床・暗幕（円筒）・LED テープ本体を，LED の光だけで照らす．
  近くの LED をまとめた光源（演出用は 1 本を 6 個，むぎゅっと 1 個，テープ 3 個．約 560 個）として，LED の向きの cos，受ける面の向きの cos，距離の 2 乗で弱める．
  頂点ごとに計算するので画面の大きさによらず軽い（床と暗幕は 8cm の網）．
  床と暗幕は設営データの `room` で別々に選ぶ（`Core/Viewer/RoomSurfaces.cs`．反射率は一般的な材料の目安）:
  - 床: `carpet-gray`（グレーのカーペット 15%），`school-white`（白い床・学校 60%），`concrete`（展示場のコンクリート 30%）．Viewer では F3 か `-floor` で切り替え
  - 暗幕: `blackout`（一般的な黒い暗幕 3%）
  - 学校の床とコンクリートのつやによる LED の映り込みはまだ描いていない．暗幕までの距離（3m）と高さ（3m）は仮の値
- **視点**（`ViewerCamera`）: 右ボタン + マウス / WASD / Q E で歩く．F5 中心・子どもの目（1.1m），F6 中心・大人の目（1.6m），F7 外から，F8 真上から．
  右ボタンを押している間はキーボードのレーン入力を止める
- **速度**: Play シーン（曲の再生・部屋込み）1920×1080 で，開発用デスクトップ 1 フレーム平均 3.8ms（99% 6.9ms），GC 0
- **VR（計画．Meta Quest 3 が届いてから実装）**
  1. 方式: 開発用デスクトップ（RTX 3060）で Viewer を動かし，Quest Link（USB-C ケーブル）か Air Link で Quest 3 に映す PC VR にする．
     本番のノート PC から `-mirror` でデスクトップへ LED のデータを送るので，ゲームの PC には負荷がかからない．
     Quest 3 単体で動かす（Android ビルド）のは，描画の負荷と Wi-Fi でのミラー受信を確かめてから考える
  2. Unity: XR Plugin Management + OpenXR（Meta Quest の機能グループ）．URP の設定で HDR・ブルームは残し，描画は single-pass instanced
  3. シェーダー: `LedPoint.shader` は頂点番号から四角形を作っているので，single-pass instanced では両目分を描けるように
     インスタンス番号で目を選ぶ（`unity_StereoEyeIndex`）か，multi-pass にする．`LitByLeds.shader` は普通のメッシュなのでステレオ用のマクロを足すだけ
  4. 視点: `ViewerCamera` の代わりに XR Origin（頭の位置はヘッドセット）．床の高さを合わせ，コントローラで F5〜F8 と同じ視点に移動・床の種類を切り替え
  5. 速度: 両目で 2064×2208 × 2，72〜90Hz．デスクトップの今の値（1080p で 3.8ms）からは余裕がある見込みだが，ブルームの解像度を下げるなど調整する
  6. 確認すること: 暗幕の部屋の暗さ（Quest 3 の LCD の黒の浮き），LED の眩しさ（トーンマップ），実際の大きさ（1m が 1m に見えるか）

## P7: 演出の制作環境

手引きは `docs/authoring/led-effects.md`（美大生向け）．

- **キャンバスの流れ**（`LedCanvas`）: 土台（カメラ・動画）→ 演出を重ねる（`Composite`．リニアの半精度で計算）→ 描画の直前に sRGB 8bit にして読み戻す．
  重ね方は 普通 / 足す / 掛ける / 明るい方（`Shaders/Composite.shader`）
- **Timeline**（`Runtime/Render/Timeline/`）: `LedEffectTrack`（バインド先 LedCanvas．空ならシーンの LedCanvas）と `LedEffectClip`（マテリアル，重ね方，不透明度）．
  クリップの重なりでクロスフェード，トラックの順に重ねる．曲の Timeline が無いときは Play シーンの見本（`Effects/Samples/Showcase.playable`）が流れる
- **演出に渡す値**（`Shaders/ArteLed.cginc`）: 曲の時刻，拍・小節（テンポマップ．無ければ 120BPM），キャンバスの大きさ，
  各ピクセルの LED の 3D 位置（`_ArtePositionTex`．キャンバスの列はリングを順に回らないので，位置で決まる演出はこれを使う），クリップの時刻
- **テンプレート**（`Shaders/Templates/`，マテリアルは `Effects/Templates/`．`Tools/Arte Rhythm/Create LED Effect Templates` で作り直せる）:
  Solid，Gradient，BeatPulse，Ripple3D，Plane3D，Sparkle，Texture
- **プレビュー書き出し**（`Diagnostics/PreviewRecorder.cs`，`Window/Arte Rhythm/Preview Export`）: `AudioClock.Manual` で時刻を 1 フレームずつ進め，
  カメラを RenderTexture に描いて ffmpeg（無ければ PNG の連番）へ．音は曲の音声から同じ範囲を WAV にして付ける．
  1280×720・30fps の 10 秒が開発用デスクトップで約 10 秒．LED の値はキャンバスの読み戻しのぶん 1 フレーム遅れて映る
- 確認用に Play シーンで ← → で 5 秒ずつ飛べる．`-screenshot` でキャンバスも `-canvas.png` に保存する
