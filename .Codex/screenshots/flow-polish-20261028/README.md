# 店・報酬・会話の表示検証（2026-10-28）

Unity 6000.3.26f1 / Linux / 既存の batchmode・OpenGL 描画で Game View の実画像を取得。背景・JikiTourai・元の16:9配置を維持し、店と会話の操作部を端からの余白へ追従させた。画像は取得した PNG 原本。ハッシュは manifest.json。

## 比較

| 対象 | 修正前 | 修正後 |
|---|---|---|
| 会話のポーズ（4:3） | [前](dialogue-before-4by3.png) | [後](dialogue-after-4by3.png) |
| 店の行・ポーズ（4:3） | [前](shop-before-4by3.png) | [後](shop-after-4by3.png) |
| 店の下部操作（21:9） | [前](shop-before-wide.png) | [後](shop-after-wide.png) |
| 報酬の背景枚数表示（4:3） | [前](reward-before-4by3.png) | [後](reward-padded-after-4by3.png) |
| 報酬の長い名前（4:3） | [前](reward-long-before-4by3.png) | [後](reward-padded-after-4by3.png) |

## 最終 FHD / 4K / 比率確認

| 画面 | FHD | 4K | 4:3 | 21:9 |
|---|---|---|---|---|
| 会話 | [fhd](dialogue-after-fhd.png) | [4k](dialogue-after-4k.png) | [4by3](dialogue-after-4by3.png) | [wide](dialogue-after-wide.png) |
| 店 | [fhd](shop-after-fhd.png) | [4k](shop-after-4k.png) | [4by3](shop-after-4by3.png) | [wide](shop-after-wide.png) |
| 削除選択 | [fhd](removal-after-fhd.png) | [4k](removal-after-4k.png) | [4by3](removal-after-4by3.png) | [wide](removal-after-wide.png) |
| 報酬 | [fhd](reward-padded-after-fhd.png) | [4k](reward-padded-after-4k.png) | [4by3](reward-padded-after-4by3.png) | [wide](reward-padded-after-wide.png) |

## 検証条件と限界

- FHD は1920×1080、4K は3840×2160、4:3 は1440×1080、21:9 は2520×1080。既存 CanvasScaler を変更せず取得した。
- 会話は長文・履歴・スキップ取消・休止復帰を検証用内容で確認。報酬の長いカード名（48文字）と説明（192文字）は表示検証用で、製品データを登録・変更していない。通常の短いカード名は元サイズを維持する。無制限の文字数の保証ではない。
- 削除選択画像の100通貨は検証専用の一時状態。取消後に元へ戻し、保存JSONのハッシュ不変を確認した。削除取引や製品経済は変更していない。
- 報酬の背景側デッキ枚数だけを隠し、報酬固有の枚数表示を残した。終了後は背景表示が復帰する。選択連打・スキップ・通貨選択後のカード取得は1枚で、既存の確定単位を維持する。
- 修正前後の戦闘時間表示の差は検証用の敵撃破タイミングによる。性能時間の変更ではない。
- 通常GUIの黒画面環境、縦長・極端な画面比率、実機パッド/タッチ、全製品データの組合せは今回の確認範囲外。FMOD無音環境で聴感確認は行っていない。
