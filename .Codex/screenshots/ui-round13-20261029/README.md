# 保存デッキの長い名前とページ復帰（2026-10-29）

View専用の11件・3ページの検証profileを使用した前後比較。Unity GameView PNG原本を切抜き・縮小・合成せず保存。寸法とSHA256はmanifest参照。

- first：装備中の長名520文字中44文字しか表示されず、全文を確認できなかった。長い行だけ既存の読取専用全文へ接続し、行内で文字と入口を分離。短い行の配置と操作は維持。
- last：従来は無効になった「次へ」から帰還へ、「前へ」から新規作成へフォーカスが移った。修正後は反対側の有効なページ操作へ戻り、ページ内で往復できる。
- details：名前の末尾まで全文を表示。保存・装備・編集・複製・削除callbackは呼ばない。ページ遷移／再表示後の古い操作と、全文モーダル中の保持されたページlistenerを拒否。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| first | 4by3 | [before](first-before-4by3.png) | [after](first-after-4by3.png) |
| first | fhd | [before](first-before-fhd.png) | [after](first-after-fhd.png) |
| first | 4k | [before](first-before-4k.png) | [after](first-after-4k.png) |
| last | 4by3 | [before](last-before-4by3.png) | [after](last-after-4by3.png) |
| last | fhd | [before](last-before-fhd.png) | [after](last-after-fhd.png) |
| last | 4k | [before](last-before-4k.png) | [after](last-after-4k.png) |

全文表示：[FHD](details-fhd.png)／[4K](details-4k.png)。

最終ソースで全文開閉・3ページ往復を2周確認し、業務callback0・専用セーブハッシュ不変。検証用profileだけを表示へ渡し、実デッキの削除・保存障害注入・製品データ変更は行わない。実機pad確認ではなくEditorのpointer／選択イベント。背景・JikiTourai・名前のひらがなを維持し、名前の長さ制限等は導入しない。

高解像度画像は時間性能の保証ではない。640×360の統一条件による最終全件結果は Planning/既存UI監査と図鑑表示修正-20261028.md の長いデッキ名とページ復帰節を参照。
