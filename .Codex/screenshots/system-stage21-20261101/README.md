# System Stage 21 — 個別進行／撤退Dialog

Controllerに残っていた2機能の表示・操作状態を、`ProgressActionDialog` / `RetreatConfirmationDialog`へ移行。DialogManagerに固有処理は追加していません。

|責務|変更前|変更後|
|---|---|---|
|本文・widget・ボタン可否・確認revision・retry表示|Controllerとpanel factory|各Dialog|
|popup世代・legacy Menu・Pauseの覆い／復帰・結果形成|Controller|各Dialog（共通の小さなOwnedPopupを利用）|
|保存計画・適用・撤退／報酬計算|既存Session／flow|同じ既存サービス。typed操作口のみ追加|
|開閉・owner/ticket・入力遮断・共通結果|DialogManager|変更なし|
|開始可否のドメイン照会と結果後の遷移|Controller|Controller。UI状態の第二所有は除去|

Application **528件**、Integration **605件**、同Editor **46件**、最終全件 **3640件（246.91秒）**が全成功。実640×360・専用TMPDIR/saveguard、error/skip/inconclusive0、最終source/資産一致。警告は[分類記録](warning-classification.json)を参照。

実Raycast入口で保存失敗→再試行、Disable/Enable、取消focus・同frame旧入力、実撤退確定→保存失敗→帰還画面Retry、checkpoint復帰、host再生成と旧callback失効を確認。進行ファイル失敗時bytes不変、checkpointの意図的書込みとは区別。仮想機器追加なし、fault store復元、元プロジェクトclean。

- [入力／寿命検証](native-paths.json)、[実撤退・保存失敗／Retry](retreat-confirm-path.json)、[cleanup](cleanup.json)
- [コードレビュー](code-review.json)、[21枚の独立視覚レビュー](visual-review.json)、[全件](full-tests.json)、[同Editor反復](same-editor-regression.json)
- [設計・残7機能](../../../Planning/GameSystem管理基盤と段階移行.md)、[仕様保護](spec-update.json)、[接続回復履歴](tool-recovery-history.json)

## 原寸比較

各取得state内は論理進行を保持しCanvas更新・実RT寸法を確認。beforeは公開済みStage20の同画面・同解像度参照で、別epochのglobal frame一致は主張しません。画像の改変なし。

|状態|FHD before / after|4K before / after|4:3 before / after|
|---|---|---|---|
|帰還|[前](before-return-hub-fhd.png) / [後](return-hub-fhd.png)|[前](before-return-hub-4k.png) / [後](return-hub-4k.png)|[前](before-return-hub-4by3.png) / [後](return-hub-4by3.png)|
|新規開始確認|[前](before-progress-confirm-fhd.png) / [後](progress-confirm-fhd.png)|[前](before-progress-confirm-4k.png) / [後](progress-confirm-4k.png)|[前](before-progress-confirm-4by3.png) / [後](progress-confirm-4by3.png)|
|保存再試行|[前](before-save-retry-fhd.png) / [後](save-retry-fhd.png)|[前](before-save-retry-4k.png) / [後](save-retry-4k.png)|[前](before-save-retry-4by3.png) / [後](save-retry-4by3.png)|
|Pause|[前](before-pause-fhd.png) / [後](pause-fhd.png)|[前](before-pause-4k.png) / [後](pause-4k.png)|[前](before-pause-4by3.png) / [後](pause-4by3.png)|
|checkpoint|[前](before-checkpoint-fhd.png) / [後](checkpoint-fhd.png)|[前](before-checkpoint-4k.png) / [後](checkpoint-4k.png)|[前](before-checkpoint-4by3.png) / [後](checkpoint-4by3.png)|

追加状態: [新規開始再試行 FHD](newgame-retry-fhd.png) / [4K](newgame-retry-4k.png) / [4:3](newgame-retry-4by3.png)、[撤退確認 FHD](retreat-confirm-fhd.png) / [4K](retreat-confirm-4k.png) / [4:3](retreat-confirm-4by3.png)。

## 残件と限界

必須残7機能: DeckDiscardDialog、SavedDeckDeleteDialog、ReadOnlyDetailsDialog、BindingResetDialog、DialogueSkipDialog、DialogueBacklogDialog、CustomCardDetailsDialog。今回で基盤全体完了とはしません。

実build・物理入力/IME・聴感・全HotReload、製品装備stat/Item skip native/NPC登録、極端な旧長名末尾、完全重複個体/同位置ボス、歴史的UGUI/Pipeline/CLI/UnityAIと登録外Sprite2095基準群は未確認／未解決を保持。新しい探索仕様は文書取得のみで今回未実装。

公開前gateはremote進展で一度停止し、push未実行を確認。[文書統合記録](remote-update-integration.json)のとおり追加1行を保護し、テスト済みcode/資産不変を再照合して新receiptへ更新。NG+の未採用／将来第二部構想は、既存の進行初期化Dialogとは別で今回未実装。
