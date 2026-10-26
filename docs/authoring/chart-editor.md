# 譜面の作り方

## 曲を用意する

1. 音声ファイル（wav 推奨）を `3d-rhythmgame/Assets/Rhythm/Songs/<曲ID>/` に入れる．
   Inspector で Load Type を **Decompress On Load** にしておくと，譜面エディタに波形が出る．
2. 同じフォルダに `song.json` を作る（中身は下の例をコピー）．
3. Project ビューで右クリック → Create / Arte Rhythm / Song で SongAsset を作り，
   `songJson` に song.json，`audio` に音声を入れる．

旧版の譜面がある場合は Tools / Arte Rhythm / Import Legacy Chart で 2 と 3 をまとめて作れる．

```json
{
  "format": "arte-rhythm-song/1",
  "title": "曲名",
  "credits": "",
  "audio": "music.wav",
  "audioStartSec": 0.0,
  "lengthSec": 0.0,
  "video": "",
  "videoOffsetSec": 0.0,
  "tempo": [],
  "notes": []
}
```

## 譜面エディタ

SongAsset をダブルクリックするか，Window / Arte Rhythm / Chart Editor で開く．
横が時間，縦がレーン．レーン番号は 1 から数える（キーボードの数字と同じ）．

| 操作 | 内容 |
|---|---|
| クリック | ノーツを置く / 選ぶ（Shift で追加選択） |
| ドラッグ | 移動（グリッドにスナップ） |
| 右クリック，Delete | 削除 |
| 上端（目盛りと波形）をクリック | カーソル移動 |
| Space | カーソルから再生 / 停止 |
| 1〜0，Q〜P | レーン 1〜20 にノーツを置く（再生中は再生位置に） |
| B | 再生中に拍に合わせて 4 回以上押すと，テンポと 1 拍目の位置を求める |
| ← → / ↑ ↓ | 選んだノーツをグリッド 1 つ / レーン 1 つ動かす |
| Ctrl+Z / Ctrl+Y / Ctrl+S | 元に戻す / やり直し / 保存 |
| ホイール / Ctrl+ホイール | スクロール / 拡大縮小 |

- ツールバーの「設営」で設営プロファイルを選ぶと，配線の無いレーンが赤くなる．
  そこに置いたノーツは本番で押せない．
- ノーツを 1 つ選ぶと，判定の範囲が薄く表示される（白 = Good，紫 = Perfect）．
- 再生しながら数字キーを押すと，その場でノーツを置ける．押すのが遅れがちなら「タップ補正(ms)」で早める．
- テンポが分かれば BPM と 1 拍目を直接入れてもよい．テンポは表示とスナップにだけ使い，
  あとから変えてもノーツの時刻（秒）は変わらない．

## 試しに遊ぶ

`Assets/Rhythm/Scenes/Play.unity` を開いて再生し，Enter で曲を始める．
1〜0，Q〜P キーでタッチの代わりになる．LED の表示ができるまでは，画面のレーン表示で確かめる．
タッチの記録は曲の終わりに `%USERPROFILE%/AppData/LocalLow/<会社名>/<製品名>/replays/` に保存される．
