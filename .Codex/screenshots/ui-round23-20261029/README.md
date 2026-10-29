# 公開済み・結果定義未特定のレシピ（2026-10-29）

通常のControllerから図鑑を開き、公開履歴と記録名がある旧レシピを隔離データで表示。結果定義がないとき、性能を推測せず既存の取得判定に従う。製品定義・性能値・公開条件・保存形式は変更していない。PNGは切抜き・縮小・合成なしのGameView原本。寸法・SHA256はmanifest参照。

- 修正前：公開済みの記録名はあるが、性能入口がない理由が分からない。
- 修正後：既存IsRevealedがtrueかつTryGetResultが失敗した行だけ「結果の詳細定義は未特定です。」を加える。UIは漢字部分だけを既存の読みへ変換する。
- 短い行は行内で、長い行は既存の全文画面で読める。記録名は維持し、同じIDのカタログに性能999の検証定義があっても結果性能として採用しない。

| 対象 | 解像度 | 修正前 | 修正後 |
| --- | --- | --- | --- |
| short | 4by3 | [before](short-before-4by3.png) | [after](short-after-4by3.png) |
| short | fhd | [before](short-before-fhd.png) | [after](short-after-fhd.png) |
| short | 4k | [before](short-before-4k.png) | [after](short-after-4k.png) |
| long | 4by3 | [before](long-before-4by3.png) | [after](long-after-4by3.png) |
| long | fhd | [before](long-before-fhd.png) | [after](long-after-fhd.png) |
| long | 4k | [before](long-before-4k.png) | [after](long-after-4k.png) |

長文の案内末尾：[FHD](full-after-fhd.png)／[4K](full-after-4k.png)。[未公開状態](unpublished-after.png)には記録名・未特定の案内・性能入口を出さず、従来のマスクを維持する。

通常1周・保存待ち1周で、全文開閉、3/3と全文入口への復帰、定義あり→定義未特定の更新、旧詳細・旧戻る・背面ページ・他画面後の旧入口拒否を確認。業務callback0・専用セーブハッシュ不変。保存待ちはView fixtureで、実保存障害は起こしていない。

背景・JikiTourai・元のひらがな等を保持し、Unity参照・製品Sceneを変更していない。物理入力／実機pad・高解像度の時間保証は未確認。全件テストは640×360固定の別検証。最終結果はPlanningの本節参照。
