# システム第十八段階: Custom詳細Popup

Custom画面本体と分離し、カード/戦闘個体等の詳細PopupをDialogManagerのowner/ticketで管理する。Battle/Custom Prefab内の実体を借用し、import画像を破棄しない。既存Custom入力を使うため標準Menuは二重生成しない。

- 親終了・host終了でticket/callback/manager参照を解除。
- 閉鎖世代と同frameの背面選択/確定/加工/Undoを遮断し、通常閉鎖は元カードへfocus復帰。
- system pauseへ移る時は詳細ticketだけを返し、pause側の確認を妨げない。Customモデル・手札・選択は保持。
- 計算、初手5/旧10保存、選択上限5、LIFO、cost、固定frame、保存形式/時点は変更しない。

## 最終検証

関連 511/511、最終全件 3619/3619（306.43秒）。同Editor2周 76件。実640×360/両RT・専用TMPDIR/saveguard、最終source hash一致。console error0、warningは[分類](warning-classification.json)参照。独立コード/原寸視覚レビュー必須指摘0。

[関連](related-tests.json) / [全件](full-tests.json) / [同Editor](same-editor-regression.json) / [source hash](final-source-hashes.json) / [cleanup](cleanup.json) / [レビュー](independent-review.json)

## 実入口

InputSystemUIInputModuleへ仮想Touchscreenの押下/長押し/離しを送信し、CustomDetailHold経由の実入口を確認。物理touchではない。実raycastによる閉鎖・元カードfocus・同frame旧確定、再Open後の旧close拒否、状態詳細の対象巡回と旧cycle拒否を確認。閲覧前後の手札参照/選択/cost/論理frameと実隔離保存bytesを比較した。最後の明示確定とhost Suspendは既存保存境界として別時点に区別する。

長文はUI本文だけへ隔離80行+末尾印を追加（製品Card/性能値は変更せず）。実ScrollRect wheelイベントで末尾へ到達し、原寸画像も確認後に本文を復元。通常詳細・末尾・状態詳細の各3比率9画像は同論理frame保持/Canvas更新済み。matched-before比較ではなく、既存見た目の移行退行確認である。

[実操作](native-details.json) / [親終了・CloseOpen・Single再入](native-lifetime.json) / [画像manifest](image-manifest.json)

|状態|FHD|4K|4:3|
|---|---|---|---|
|カード詳細|[画像](card-details-fhd.png)|[画像](card-details-4k.png)|[画像](card-details-4by3.png)|
|長文末尾|[画像](long-details-tail-fhd.png)|[画像](long-details-tail-4k.png)|[画像](long-details-tail-4by3.png)|
|状態詳細|[画像](combatant-details-fhd.png)|[画像](combatant-details-4k.png)|[画像](combatant-details-4by3.png)|

## 初回不具合と回復

初回実入口でCustom詳細→pause→撤退確認を押しても詳細ticketが残り確認を開けない競合を再現し、pause handoffで修正した。また標準MenuなしticketのRoot Tickがnullを参照する製品例外をconsole guardが検出し、テスト開始前に停止。Menuのnull対応とTick回帰を追加して再検証した。初回レビューでTickを見落とした履歴も残す。製品例外を環境ログとして除外しない。終了guardはCLI statusが未登録の`editor status`名を探すPipelineログを検出して停止し、stackに基づきtool履歴と分類した。完了済みCloseOpen/Singleを再実行せず、最終close/cleanupのみ再開。初回Suspendのlocal hashはguard停止時に未保存だったため捏造せず、最終Suspendと欠損/取消ロード後の実bytes比較を別に記録した。

compile/Playの通常DomainReload中のPipeline接続/主thread timeout、UnityAI/環境ログは[回復履歴](recovery-history.json)へ保存。同じVM/Editor137059/モデル/認証を保持し、既存要求の期限と結果を回収して重複操作を避けた。以前の成功結果は修正前hashの履歴として区別する。

## 文書更新

公開前pullでac9f6be/merge aafdecbのゲーム全体仕様を保護。分岐は攻略順（会話選択で進行先を決める方式ではない）、最終的に全主要対象攻略、外界敵固定強さ、寄り道/任意boss追加。複数エンドは未決。文書取得のみで実装追加なし、検証済み4source不変。[確認内容](spec-update.json)

## 残件・限界

必須残件はDialoguePanelの会話skip/backlogとController内部Initialize/必要時Prefab分解。本番Bootstrapで生成/入力/親終了/資源返却を管理経路へ揃えることが終点。任意の見た目改善や外界/依頼/ボスラッシュ新機能・D05等未決値とは分ける。

Itemスキップnative、装備stat実値、極端な旧デッキ長名末尾、実build/物理入力/聴感/全HotReloadは未確認。歴史的UGUI/Pipeline/CLI/UnityAI、登録外Sprite2095基準群、完全同位置個体/同位置複数boss等の既存限界は解決済み扱いにしない。
