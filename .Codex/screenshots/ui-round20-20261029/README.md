# 図鑑の長い行名とページ端（2026-10-29）

通常のController経路から図鑑を開き、隔離した9行の既知カード／レシピで表示・操作を確認。製品名・性能・実セーブは変更していない。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

- 修正前：492文字の行は148文字までしか表示されず、行情報の全文入口がない。既存の性能詳細ではレシピの行情報を読めない。
- 修正後：溢れる行だけ既存の読取専用全文画面へ接続。文字領域の左端は維持し、右側180を全文入口へ割り当てる。短い行・既存のカード／結果性能詳細は維持する。
- 前／次のページ端で無効になった操作から「戻る」へ飛ぶ問題を修正。端では反対側の有効なページ操作へ、中間では移動方向の操作へ戻す。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| cards | 4by3 | [before](cards-before-4by3.png) | [after](cards-after-4by3.png) |
| cards | fhd | [before](cards-before-fhd.png) | [after](cards-after-fhd.png) |
| cards | 4k | [before](cards-before-4k.png) | [after](cards-after-4k.png) |
| recipes | 4by3 | [before](recipes-before-4by3.png) | [after](recipes-after-4by3.png) |
| recipes | fhd | [before](recipes-before-fhd.png) | [after](recipes-after-fhd.png) |
| recipes | 4k | [before](recipes-before-4k.png) | [after](recipes-after-4k.png) |

ページ端のフォーカス：[修正前](edge-before.png)／[修正後](edge-after.png)。全文：[FHD](full-after-fhd.png)／[4K](full-after-4k.png)。

カード／レシピ×通常／保存待ちの反復操作で、全文の末尾、閉じた入口への焦点、ページ端・中間の焦点、既存性能詳細、旧タブ・旧行情報・旧ページ／モーダル背面操作の拒否を確認。業務callback0・専用セーブハッシュ不変。保存待ちはView用fixtureで、実保存障害を起こしていない。

背景・JikiTourai・漢字だけの読替え・保存形式は維持。物理入力／実機pad・高解像度の時間保証は未確認。全件テストは640×360固定の別検証。最終結果はPlanningの本節参照。
