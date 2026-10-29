# 図鑑メッセージとレシピ性能の全文（2026-10-29）

通常のControllerから図鑑を開き、隔離した長文メッセージ／レシピ性能を表示。製品定義・値・実セーブは変更していない。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

- status：修正前は1,006文字中207文字だけ表示され、続きが読めない。
- performance：修正前は1,007文字中598文字だけ表示され、続きが読めない。
- 修正後：溢れる場合だけ既存の読取専用全文へつなぐ。左端を維持して右側180を全文入口へ割り当て、短文の寸法・性能表示・公開情報の注意文は維持する。
- 前周の行情報と同じ右側予約処理を小さなhelperへ共用。前周の座標・入口名・ガードは維持する。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| status | 4by3 | [before](status-before-4by3.png) | [after](status-after-4by3.png) |
| status | fhd | [before](status-before-fhd.png) | [after](status-after-fhd.png) |
| status | 4k | [before](status-before-4k.png) | [after](status-after-4k.png) |
| performance | 4by3 | [before](performance-before-4by3.png) | [after](performance-after-4by3.png) |
| performance | fhd | [before](performance-before-fhd.png) | [after](performance-after-fhd.png) |
| performance | 4k | [before](performance-before-4k.png) | [after](performance-after-4k.png) |

全文末尾：メッセージ [FHD](status-full-after-fhd.png)／[4K](status-full-after-4k.png)、レシピ性能 [FHD](performance-full-after-fhd.png)／[4K](performance-full-after-4k.png)。

通常1周・保存待ち1周で、それぞれメッセージ／性能の全文を開閉。3比率で全文全字生成・末尾、入口への焦点、更新による旧モーダル解消、古い入口・背面操作・他画面後の旧操作拒否を確認。業務callback0・専用セーブハッシュ不変。閲覧callback以外の加工・選択・再試行は呼んでいない。保存待ちはView用fixtureで、実保存障害は起こしていない。

背景・JikiTourai・漢字だけの読替え・保存形式は維持。物理入力／実機pad・高解像度の時間保証は未確認。全件テストは640×360固定の別検証。最終結果はPlanningの本節参照。
