# システム第十九段階: 会話Viewとskip/backlog Dialog

会話本体をViewManagerのdialogue層/ResourceManager実体leaseへ、skip確認とbacklogをDialogManager ticketへ移行。既存表示部品を同座標系の子Popupへ借用し、閉鎖/親終了で元順へ戻す。既存会話Menuが入力を一つだけ所有し、manager標準Menuは二重生成しない。

進行/選択肢/skip条件/履歴/DialogueSession・専用時計/保存時点は変更しない。Viewは表示を返し、Controllerが既存FinishDialogueで中断を判断する。pause時はticketだけを返してflagsを保持、復帰時は新世代で再提示する。現在のButtonイベントもHideで返し、外部に残った旧Eventはowner/Version/世代で拒否する。

## 最終検証

関連513/513、既存会話Core6/レイアウト2/旧入口・checkpoint復帰2、同Editor2周80件、全3621/3621（286.42秒）。失敗/skip/inconclusive0、console error0。warningは[分類](warning-classification.json)に実数を保持。実640×360/両RT・専用TMPDIR/saveguard、最終4source一致、独立コード/原寸視覚レビュー必須0。

[関連](related-tests.json) / [既存会話](dialogue-regression.json) / [全件](full-tests.json) / [同Editor](same-editor-regression.json) / [hash](final-source-hashes.json) / [cleanup](cleanup.json) / [レビュー](independent-review.json)

## 実入口と読み取り

本番Bootstrapの既存DungeonBattleController.TryStartDialogueへ隔離台本を渡し、EventSystem raycastの通常Button/ScrollRect経路を使用。製品NPC/ストーリー登録や物理入力の完成とはしない。全文表示の明示操作後を基準に、backlog閲覧/skip取消でindex・visible・choices・履歴・ゲーム状態と実隔離保存bytes不変を比較した。Popup flags/Versionの既存変化と進行は区別する。

backlogは隔離80行+末尾印を持つ台本で実scrollイベントを通し、各比率で末尾位置を検査。skip確定/取消、同frame旧Next、pause→別確認→取消→再開、旧close/confirm・二重skip、外部親View終了、host CloseOpen/checkpoint/Single再入と購読/参照解放を確認。host Suspendは既存保存境界として閲覧区間と分ける。

[実操作](native-dialogue.json) / [親終了・再生成](native-lifetime.json) / [console履歴](native-console-history.json)

|状態|FHD|4K|4:3|
|---|---|---|---|
|会話本体|[画像](conversation-fhd.png)|[画像](conversation-4k.png)|[画像](conversation-4by3.png)|
|backlog末尾|[画像](backlog-tail-fhd.png)|[画像](backlog-tail-4k.png)|[画像](backlog-tail-4by3.png)|
|skip確認|[画像](skip-confirm-fhd.png)|[画像](skip-confirm-4k.png)|[画像](skip-confirm-4by3.png)|

原寸9画像は同状態/論理frame保持・Canvas更新後。matched-before同一や物理機器検証は主張しない。元背景/JikiTourai/水鏡表現と既存座標を維持する。

## 初回失敗と復旧

先行2ケースは取消後focus=nullで両方失敗。Popup非active化と同frame入力fenceにより自動focusが働かないため、元のHistory/Skip期待を維持し、部品復元・Refresh後に有効な起点へ選択表示だけ返した。入力fence/CanActは緩めず、確定終了/親終了にはfocusを返さない。テストのTask受取/awaitでCS4014も修正。[初回結果](initial-critical-classification.json)

compile/PlayのDomainReload中のPipeline読取timeoutは[回復履歴](recovery-history.json)に保持。同じVM/Editor137059/モデル/認証を維持し、要求状態と期限を確認して重複テスト/不明な保存を再送していない。UnityAI/CLI等の診断は製品結果と区別し、重要診断を抑制しない。checkpoint再開直後の入口拒否ではhelperを停止し、既存の準備条件を待って残りだけ再開した。拒否時の単一guardは未採取なので原因を断定せず、初回Suspendの未保存local hashは再構成しない。最終Suspendと欠損/取消ロード後bytesを別に測定。[手順修正](helper-corrections.json)

## 仕様と残件

ac9f6beの攻略順によるメイン分岐、全主要対象攻略、外界敵固定強さ、寄り道/任意bossは文書として保護。複数エンドは未決。既存会話選択肢と混同せず、新外界/ストーリー機能は実装していない。さらに公開前pullで667cd4bまでの探索style/仕掛け/deck制限・共有所持/card動作互換・motion開始時消費と中断条件を文書として保護した。検証済みC#は不変で、探索機能は実装していない。[確認](spec-update.json)

必須残件はController.CreatePauseControls/CreateProgressActionsのhost側表示組立を必要時factory/既存管理寿命へ分けること。GameSystemへ具体処理を集中させず、保存/進行delegateを注入し、生成失敗/取消/返却後再生成と本番Bootstrap導線を閉じる。全Widgetのserialize済Prefab化や任意抽象化まで必須に増やさない。会話管理の完了を全システム移行完了とはしない。

Itemスキップnative、製品装備stat実値、極端な旧デッキ長名末尾、実build/物理入力/聴感/全HotReload、歴史的UGUI/Pipeline/CLI/UnityAI、登録外Sprite2095と従来表示限界は別に保持する。
