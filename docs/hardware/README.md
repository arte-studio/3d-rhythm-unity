# ハード構成データ

実機の寸法・配線・設営ごとの違いを，コードではなくデータとして持つ．ゲーム，演出，シミュレーション，UDP 送信はすべてここから配置と配線を引く．

| ファイル | 中身 | 変える人・タイミング |
|---|---|---|
| `3d-rhythmgame/Assets/Rhythm/Data/Hardware/hardware.json` | ハードそのもの（リング半径，ストリップ長，LED 数，ピンとリングの対応，ジグザグの向き，ノーツモジュールの LED 並び，ファームの出力特性） | ハードを作り変えたとき |
| `3d-rhythmgame/Assets/Rhythm/Data/Hardware/setups/*.json` | 設営ごとの違い（外したストリップ，レーンとモジュールの対応，明るさ） | 会場ごと |

読み込みと計算は `Assets/Rhythm/Core/Hardware/`（Unity に依存しない C#）．

- `PerfLayout`: 演出用 LED．キャンバス座標 (column, row) ↔ 配線 (device, pin, slot) ↔ 3D 座標を変換し，UDP パケット用に配線順へ詰める（`PackPin`）．
- `NoteLayout`: ノーツ用モジュール．レーン ↔ (device, module) と，ノーツ用パケット内の LED 番号．
- `LedOutputModel`: ファームと同じ明るさの式 `(c * (b + 1)) >> 8`．シミュレーションも同じ式を通す．

## 座標と並び

- 単位はメートル．Y が上．角度は真上から見て +Z を 0°，時計回り（+X 側）が正．
- 演出用ストリップの配線: デバイス `d` のピン `pin`（= UDP パケットの type 0〜2）に 4 本が直列につながり，`slot` 本目は角度番号 `angleIndex = d * 4 + slot` の位置にある．最後のデバイス（7）は 2 本だけ．
- キャンバス（旧来の動画 90×120 と同じ並び）: 列 = `angleIndex * 3 + canvasColumnOffsetOfPin[pin]`，行 0 が下端．
- ジグザグ: `(angleIndex + topFirstParityOfPin[pin])` が偶数なら配線上の先頭 LED が上端．

## 確定済み・未確認

| 項目 | 状態 | 出典 |
|---|---|---|
| ストリップ 2m・60LED/m（120 個，約 16.7mm 間隔） | 確定 | ユーザー確認 |
| リング半径 0.91 / 1.15 / 1.42 m，各 30 本 | モデル由来 | `LEDテープs.prefab` の支柱位置 |
| 配線とキャンバスの並び，ジグザグの向き | 旧コードと一致（単体テストで保証） | `UdpController.ConvertPerfLedsID`，`RenderTextureSampler` |
| ピンとリングの対応（`ringOfPin`），リングごとの角度のずれ，ストリップ下端の高さ | **未確認** | Unity でモデルを実測して埋める（`docs/hardware/derived/strips-from-prefab.csv`） |
| ピン 2 のジグザグが他のピンと逆向き | **要実機確認** | 旧コードの式から導いた結果．ストリップ 1 本で先頭 LED を光らせれば確認できる |
| ノーツモジュールの LED 並び（94 個おき） | 旧 Unity コードと C6 ファームの修正（4be2d30）で一致 | S3 ファーム v1 のピンク表示の範囲（0,96,192,320,416）は古い値だった．v2 で 94 個おきに直した |
| ファームのガンマ補正 | なし | `tape_led_module_s3/src/main.cpp` |

## 設営プロファイル `kodaisai-1101`

- 入口のため，デバイス 7 のピン 1・2（角度番号 28, 29 の 2 リング分，計 4 本）を外している．
- ノーツモジュールは 11 個（レーン 0〜10）．譜面 `Kodaisai_NoteData_1101.json` はレーン 11〜14 にも 52 ノーツある（0 始まりに直した後）が，この設営では実機に存在しない．

## テスト

Unity を起動せずに実行できる．

```
dotnet test 3d-rhythmgame/CoreTests~
```

Unity の Test Runner（EditMode）からも同じテストが走る．
