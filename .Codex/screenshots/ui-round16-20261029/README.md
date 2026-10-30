# 編成のページ端・取消確認・詳細切替（2026-10-29）

隔離された13件・3ページのView用データによる比較。GameViewのPNG原本で切抜き・縮小・合成なし。寸法とSHA256はmanifest参照。

- return：取消確認で「編集に戻る」を選ぶと、従来は「保存」へ焦点が移った。通常編成と取消確認のruntime root名を分離し、元の「戻る」操作へ復帰する。
- end：従来はページ端でも「保存」へ焦点が移った。末尾は「前へ」、先頭は「次へ」に戻し、中間は移動方向を保つ。
- 詳細・取消確認の背面クリックと、保持された旧ページ／旧全文入口は拒否する。保存・カード操作callback0。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| return | 4by3 | [before](return-before-4by3.png) | [after](return-after-4by3.png) |
| return | fhd | [before](return-before-fhd.png) | [after](return-after-fhd.png) |
| return | 4k | [before](return-before-4k.png) | [after](return-after-4k.png) |
| end | 4by3 | [before](end-before-4by3.png) | [after](end-after-4by3.png) |
| end | fhd | [before](end-before-fhd.png) | [after](end-after-fhd.png) |
| end | 4k | [before](end-before-4k.png) | [after](end-after-4k.png) |

[全文表示FHD](detail-after-fhd.png)／[取消確認4K](dialog-after-4k.png)。

保存デッキ編成2周・通常編成1周で、全文→閉じる→取消確認→編集に戻る→ページ往復を確認。専用セーブハッシュは不変。実保存・削除・製品データ変更は行わない。解像度変更時の背面検証座標はview相対座標から再計算し、画面外をクリックした状態を入力漏れと誤判定しない。

実機pad・物理入力は未確認。高解像度画像を時間性能の保証としない。最終全件は640×360固定で別実施し、Planning/既存UI監査と図鑑表示修正-20261028.md のページ端と取消確認節に記録する。
