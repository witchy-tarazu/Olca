# システム基盤 第22段階 — 共通確認と読取詳細

最新ユーザー指示「テキスト＋はい／いいえは共通化」を反映。編成破棄と保存デッキ削除は同じ`ConfirmationDialog`へ文言・ボタン挙動・対象名・可否を渡す。用途別class数を増やさず、`ReadOnlyDetailsDialog`が長短本文、装飾、scroll、閉鎖を所有。親ClearReturnViewは開始と既存サービスの結果に応じた進行、DialogManagerは共通ticket/owner/入力遮断/focus/結果管理だけを行う。デッキ計算・保存write plan・公開条件・固定frameは変更していない。

## 検証

全件3649/3649、Application537/537、Integration605/605成功。最終全件console error0、警告59件は[実ログ分類](warning-classification.json)。初回Integration実行中のUnityAI Relayエラー1件は[初回記録](integration-first-result.json)と[実スタック](integration-first-environment-error.json)に保持し、console0と偽っていない。

実640×360と専用TMPDIR/saveguard確認後に実施。同Editor反復、original保護、仮想入力/注入/callback/lease/EventSystem/保存bytes/表示設定は[cleanup](cleanup.json)。登録外Sprite2095基準群は未特定のまま。

[全件](full-tests.json) / [関連](related-tests.json) / [同Editor反復](same-editor-regression.json) / [独立code review](code-review.json) / [独立visual review](visual-review.json) / [ソースhash](final-source-hashes.json)

## 実入口

隔離Bootstrap→通常帰還→所持編成破棄の取消/確定/同frame旧入力遮断、保存デッキA/B作成→削除取消→書込前1回失敗→保留同計画再試行→Aのみ削除、読取詳細の開閉/旧ticket/元focus/表示用長文末尾への実scroll、親終了・自然Destroy・host終了/再生成/Single再入を記録。

[編成破棄](native-discard.json) / [削除と失敗再試行](native-delete.json) / [詳細末尾](native-reader.json) / [寿命境界](native-lifetime.json) / [終了](native-cleanup.json)。製品データでない隔離fixtureのみ使用。長い削除対象名はdisplay-only fixtureで、極端な旧保存名の実保存末尾検証を完了したとはしていない。

## 原寸画像

同状態と解像度の比較。撮影中は論理frame保持、Canvas更新、実RenderTexture確認。別runの全世界frame一致は主張しない。Stage15→21間のClearReturnViewは不変で、既存画像を前の基準に使用。[寸法/SHA](image-manifest.json)

|状態|前 FHD|後 FHD|後4K|後4:3|
|---|---|---|---|---|
|破棄確認|[前](before-discard-confirm-fhd.png)|[後](discard-confirm-fhd.png)|[4K](discard-confirm-4k.png)|[4:3](discard-confirm-4by3.png)|
|削除確認|[前](before-delete-confirm-fhd.png)|[後](delete-confirm-fhd.png)|[4K](delete-confirm-4k.png)|[4:3](delete-confirm-4by3.png)|
|保存再試行|[前](before-delete-save-retry-fhd.png)|[後](delete-save-retry-fhd.png)|[4K](delete-save-retry-4k.png)|[4:3](delete-save-retry-4by3.png)|
|読取詳細|—|[後](owned-detail-fhd.png)|[4K](owned-detail-4k.png)|[4:3](owned-detail-4by3.png)|
|長文末尾|—|[後](long-reader-end-fhd.png)|[4K](long-reader-end-4k.png)|[4:3](long-reader-end-4by3.png)|

## 初回失敗と残件

初回Application537件は532成功5失敗。[生結果](first-related-failures.json)。初回全件は3638成功11失敗で、初回同期layout4ケースと旧fixture階層7ケースを修正し、最終sourceでUI382件を含め再検証した。[初回全件](first-full-failures.json) / [UI関連](ui-tests.json)。破棄済みfocusへのC#?.参照3件をUnity生存判定で修正。EditMode直接破棄fixture2件は実際にOnDestroyが呼ばれない前提を明示lifecycle呼出へ修正し、期待値を維持した。自然なPlay中Destroyは別途実操作で確認。通常reload中のPipeline timeoutと冷間helperの同job回収は[履歴](tool-recovery-history.json)。

[仕様保護](spec-update.json)は文書のみ。八属性/六特性/相性基本耐性、神依(カムイ)/神憑(カンガカリ)の更新を保持し、ゲーム実装変更へ混ぜていない。

残必須は割当初期化と会話skipの共通確認化、会話backlogとCustom詳細の固有Dialog、Retreatの単純描画共用整合。[具体的残件](required-followup.json)。ProgressActionの固有再試行状態は現行個別classを維持。基盤必須完了ではない。

実build、物理入力/IME、聴感、全HotReload、製品stat/Item skip/NPC登録、極端な旧長名、完全重複敵等の未確認、歴史的UGUI/Pipeline/CLI/UnityAIと登録外Spriteは分離して保持。
