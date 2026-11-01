# 第十三段階: checkpoint案内とprogressAction確認

CheckpointOverlayをViewManager、ProgressActionOverlayをDialogManagerへ接続。未移行hubは実表示に結び付くDialogOwnerScopeで橋渡しし、偽のViewを作らない。保存再試行は同ticketを維持し、既存保存/適用順とpending計画を保つ。取消focus、同frame遮断、旧世代/終了後の入力と保存を保護する。

最終関連494件・全3602件、同Editor反復42件と独立レビューを確認。初回関連493/494失敗は既存初期化/rollback/破棄回帰のUnity fake-nullアクセス。3guardのみ修正し、元の期待値で単体と全関連を再実行した。履歴はinitial-related-failure.jsonに保持。

|状態|FHD|4K|4:3|
|---|---|---|---|
|checkpoint案内|[原寸](checkpoint-prompt-fhd.png)|[原寸](checkpoint-prompt-4k.png)|[原寸](checkpoint-prompt-4by3.png)|
|新規開始確認|[原寸](newgame-confirmation-fhd.png)|[原寸](newgame-confirmation-4k.png)|[原寸](newgame-confirmation-4by3.png)|
|checkpoint保存再試行|[原寸](checkpoint-save-retry-fhd.png)|[原寸](checkpoint-save-retry-4k.png)|[原寸](checkpoint-save-retry-4by3.png)|
|手動保存再試行|[原寸](manual-save-retry-fhd.png)|[原寸](manual-save-retry-4k.png)|[原寸](manual-save-retry-4by3.png)|
|保存完了|[原寸](manual-save-success-fhd.png)|[原寸](manual-save-success-4k.png)|[原寸](manual-save-success-4by3.png)|

書込前に一度だけ失敗する隔離store wrapperを用い、保存失敗時の元bytes保持・同計画再試行・実store復元を確認。元データ破壊やガード解除はしていない。ParentClosed結果になる新規開始成功の既存親遷移順、対照before未撮影、仮想入力と物理機器未確認を区別する。

既存Controller内部UI組立と、帰還共有画面・装備系・報酬overlay・各詳細/削除確認・会話は残る。登録外Sprite2095群は未特定で一括削除していない。実build/物理機器/聴感/全HotReload・歴史的UGUIは未確認/未解決。Pipeline/CLI/UnityAI履歴は製品検証と別記録。

[設計・正確な残対象](../../../Planning/GameSystem管理基盤と段階移行.md)
