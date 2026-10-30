# 旧図鑑記録・削除定義の互換監査（2026-10-29）

同名・同IDでもhashが異なる定義を取り違えないか、hash欄自体がない旧Version1封筒と削除済み定義を隔離データで監査した。今回の範囲で不具合を再現できず、製品コードと見た目は変更していない。画像は修正前後ではなく、記録状態による表示の対応比較。GameView原本で切抜き・縮小・合成なし。寸法・SHA256はmanifest参照。

検証用A/B/Cは同じID `shared`・名前 `おなじナマエ`、威力17/71/99、別hash。製品データへ登録しない。Aの本文はカクニンA、未公開B/CはヒミツB/C。通常Controllerから開いた図鑑Viewへ既存CollectionDisplayの行と詳細配列を供給した。Controllerの同一順序とnull対応はコードで照合している。

| 記録と現定義 | 4:3 | FHD | 4K |
| --- | --- | --- | --- |
| hash欄なし・Aだけ候補、一意補完してA詳細あり | [画像](unique-list-4by3.png) | [画像](unique-list-fhd.png) | [画像](unique-list-4k.png) |
| hash欄なし・A/B候補、曖昧なので両性能を隠す | [画像](ambiguous-list-4by3.png) | [画像](ambiguous-list-fhd.png) | [画像](ambiguous-list-4k.png) |
| 記録hashA・現定義Bだけ、Bの性能へ置換しない | [画像](removed-list-4by3.png) | [画像](removed-list-fhd.png) | [画像](removed-list-4k.png) |

- 一意補完したAの性能：[FHD](unique-detail-fhd.png)／[4K](unique-detail-4k.png)。威力17・カクニンA。
- 長い削除済み記録の全文：[FHD](orphan-info-fhd.png)／[4K](orphan-info-4k.png)。記録名と定義未特定の案内を読み、性能は示さない。
- 現存Aと削除Cの記録が混在：[FHD](mixed-list-fhd.png)。Aだけ性能入口、Bは未知、Cの旧記録は保持。

## 保存と操作

専用の新規ディレクトリへ旧Version1封筒をCreateNewで置き、通常ClearReturnFileStore/ClearReturnSessionで読み込んだ。hash欄省略の封筒は同じPortableCheckpointJsonの正規checksumを持つ。Aしか候補のない旧記録は既存互換処理により1回だけhash補完・保存され、元封筒はbackupへそのまま残る。曖昧な候補や削除hashは補完しない。互換処理前後の意図された保存と、閲覧による保存不変は区別した。

互換処理後のファイルバイト・論理profileと、通常ゲームの専用セーブは閲覧中不変。通常と保存待ち表示の各1周で、詳細・全文・閉じる・復帰・定義再表示と再削除・別画面後の古い入力拒否を確認。全文を閉じた際は元入口へフォーカス復帰し、業務callbackは0。保存待ちはView fixtureで実保存障害を起こしていない。

新8回帰（Core2・View6）と全件結果はPlanningの本節参照。背景・JikiTourai・公開条件・製品値・保存形式・Unity参照は変更なし。物理機器入力・実保存障害・高解像度の時間保証は未確認。
