# 図鑑件数の増減と全文入口への復帰（2026-10-29）

通常のControllerから図鑑を開き、隔離データの件数を9→13→5→0などへ更新。製品データ・仕様・公開条件・保存形式は変更していない。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

- grow：9→13件では3ページ目と全文入口8が残るのに、更新時にタブへ焦点を失っていた。修正後は有効な入口を保持し、再度開くと更新後の情報を表示する。
- shrink：13→5件のページ補正は従来から2/2で正常。消えた入口12へ戻らず、表示中タブへ戻る挙動を維持する。
- 同じ一覧を表示中のデータ更新だけ有効な焦点を保持し、再表示・別画面からの遷移は従来のタブ初期焦点を維持。明示されたタブ・ページ・詳細復帰先は維持し、消失／無効化された履歴だけ解除する。短文化で全文入口が消えた場合、0件、無効なページ操作も表示中タブへ戻す。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| grow | 4by3 | [before](grow-before-4by3.png) | [after](grow-after-4by3.png) |
| grow | fhd | [before](grow-before-fhd.png) | [after](grow-after-fhd.png) |
| grow | 4k | [before](grow-before-4k.png) | [after](grow-after-4k.png) |
| shrink | 4by3 | [before](shrink-before-4by3.png) | [after](shrink-after-4by3.png) |
| shrink | fhd | [before](shrink-before-fhd.png) | [after](shrink-after-fhd.png) |
| shrink | 4k | [before](shrink-before-4k.png) | [after](shrink-after-4k.png) |

更新後の全文末尾：[FHD](full-after-fhd.png)／[4K](full-after-4k.png)。本文中の「コウシン13」は検証用の更新識別子。

通常1周・保存待ち1周でカード／レシピ双方を確認。残る入口・消える入口・短文化・0件・無効ページを区別し、古い入口／モーダルclose／ページ・背面入力・他画面後の旧操作を拒否。業務callback0・専用セーブハッシュ不変。保存待ちはView fixtureで、実保存障害は起こしていない。

背景・JikiTourai・元のひらがな等を保持し、Unity参照・製品Sceneを変更していない。物理入力／実機pad・高解像度の時間保証は未確認。全件テストは640×360固定の別検証。最終結果と中間fixture失敗の切り分けはPlanningの本節参照。
