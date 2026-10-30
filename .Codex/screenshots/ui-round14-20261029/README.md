# 長いデッキ名の編集・取消・再表示（2026-10-29）

View専用の隔離データによる比較。タグ風の記号を含む264文字の名前を使用し、名前の保存文字列は変更しない。PNGはUnity GameView原本で切抜き・縮小・合成なし。寸法・SHA256はmanifest参照。

- 修正前は名前の描画が入力枠の外と右上装備操作に重なり、編成タイトルも上端操作に隠れた。修正後は入力枠をマスクし、右上からの余白を確保。タイトル文字枠を実toolbar間へ収める。
- 名前はプレーンテキストで描画し、ひらがな・英数字・記号を保持。長さ制限や保存時の自動変換を追加しない。
- 取消確認後の旧入力listenerでdraftが書き換わる状態を再現。入力と旧ボタンはowner／active／pending／モーダルを確認し、閉じた画面からの操作を拒否する。

| 解像度 | 修正前 | 修正後（入力末尾） | 破棄・再表示 |
| --- | --- | --- | --- |
| 4by3 | [before](edit-before-4by3.png) | [after](edit-after-4by3.png) | [reopen](reopen-after-4by3.png) |
| fhd | [before](edit-before-fhd.png) | [after](edit-after-fhd.png) | [reopen](reopen-after-fhd.png) |
| 4k | [before](edit-before-4k.png) | [after](edit-after-4k.png) | [reopen](reopen-after-4k.png) |

[取消確認の4K](discard-after-4k.png)。

入力→取消確認→編集に戻るでは変更を保持し、破棄→一覧→再表示では元名を復元するViewホストの流れを2周確認。旧入力・旧保存callbackは再表示後も実行せず、保存callback0・専用セーブハッシュ不変。実保存・実デッキ削除・保存障害注入は行わない。業務の保存／出撃条件をこの表示用profileで検証した扱いにしない。

InputFieldの値・選択・caret操作とpointer操作によるEditor検証。物理キーボード／IME／実機padの入力は未確認。高解像度画像は時間性能の保証ではない。統一640×360での最終全件結果は Planning/既存UI監査と図鑑表示修正-20261028.md の編集・取消・再表示節を参照。
