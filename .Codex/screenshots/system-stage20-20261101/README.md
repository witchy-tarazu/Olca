# システム基盤 第20段階 — 必要時factory/binding

本番Bootstrapと既存互換hostで、表示しない画面を先行生成せず、最初の要求からView/Popupの生成・参照束縛・終了後解放を通す。
**追加指示の個別Dialog9機能は未移行であり、基盤必須完了ではない。** [責務点検](dialog-responsibility-assessment.json) / [必須残件](required-followup.json)。

## 実変更

- `GameplayPanelFactory`: 旧CreatePauseControls/CreateProgressActionsのwidget・surface組立を分離。既存font/色/サイズ/座標/名前を維持。
- `HostPanelBindings`: 必要なView層/ResourceManager実体leaseとPopup実体leaseをhost世代で所有。未表示は未生成、再表示は再利用、host終端とcontent破棄で返却。保存/計算は持たない。
- `ViewManager`: 破棄済み同画面の再生成で旧lease/contextを返し、新実体を旧entryとして非表示にしない。同entry失敗は旧Dialogも返し、別entry失敗の旧画面復帰は維持。
- 図鑑内部停止で表示しないPauseを生成しない。遅延生成の帰還面が装備入口を覆う順序と、checkpoint案内の前へ初回TouchHUDが出る順序を修正。
- 非active互換hostのcheckpoint表示データ更新は生成まで保留し、個別本文を保持してOnEnableで反映。Core・保存順・固定frame・初手5/旧10・LIFO等は変更しない。

## 検証

|検証|最終結果|
|---|---|
|EditMode全件|3633/3633、256.73秒|
|Application関連|521/521、40.97秒|
|Integration関連|605/605、55.42秒|
|同Editor反復|108/108、各終了でEventSystem登録と管理生成資産lease残留なし|
|追加実行|active/inactiveを含む表示境界4ケース群・個別本文保持・lazy fixture11ケース|
|console|最終全件 error0。警告59件は既存の意図的fixture診断、抑制なし|
|実画面|FHD1920×1080 / 4K3840×2160 / 4:3 1440×1080、独立15枚レビュー必須指摘0|

テスト前はScreenと両RenderTextureの実640×360、専用TMPDIR/saveguardを確認。高解像度確認後は640×360へ戻した。Source/資産一致、原本clean、既定保存先未生成、仮想機器/注入/旧callback/proof key/leaseと資源の後始末は[cleanup](cleanup.json)。未特定の登録外Sprite2095基準群を一括削除していない。

[全件](full-tests.json) / [Application](related-tests.json) / [Integration](integration-tests.json) / [同Editor](same-editor-regression.json) / [最終source hash](final-source-hashes.json) / [独立code review](code-review.json) / [独立visual review](visual-review.json)。

## 実入口と保存境界

隔離Bootstrap起動→図鑑反復→新規開始確認取消/旧同frame入力拒否→手動保存の書込前1回失敗→同ticket再試行→出撃→Pause/撤退取消/復帰→公開会話入口/履歴末尾/skip1回→timeScale0 host終了→checkpoint再開→Single再入を確認。[実操作結果](native-paths.json) / [終端](native-cleanup.json)。

閲覧/取消/保存失敗のbytes不変と、意図的な手動保存・中断checkpoint書込を分けて記録。失敗注入は隔離storeだけ、再試行Writes2、元storeへ復元。Root/Camera/Listener/EventSystem各1・音源3、旧input破棄、旧bindingのslots/Popup/Dialog0を確認。製品NPCデータ登録や物理入力の確認ではない。

実操作時のconsoleはerror0、環境警告1件（UnityAI接続情報をread-only領域へ保存できない警告）。[実ログ分類](native-console-classification.json) / [生console](native-console-history.json)。製品警告や歴史的UGUI全般が解決したとは扱わない。

## 原寸比較

帰還はDungeon/位置0/HP100/revision1、checkpointは位置0/HP100/revision3の対応状態。各比率の撮影中は論理frameを保持しCanvas更新と実RenderTextureを確認。別run間の全世界frame一致までは主張しない。[画像寸法とSHA](image-manifest.json)。

|状態|修正前|修正後|
|---|---|---|
|帰還FHD|[前](before-return-hub-fhd.png)|[後](return-hub-fhd.png)|
|帰還4K|[前](before-return-hub-4k.png)|[後](return-hub-4k.png)|
|帰還4:3|[前](before-return-hub-4by3.png)|[後](return-hub-4by3.png)|
|checkpoint FHD|[前](before-checkpoint-fhd.png)|[後](checkpoint-fhd.png)|
|checkpoint 4K|[前](before-checkpoint-4k.png)|[後](checkpoint-4k.png)|
|checkpoint 4:3|[前](before-checkpoint-4by3.png)|[後](checkpoint-4by3.png)|

確認: [FHD](progress-confirm-fhd.png) / [4K](progress-confirm-4k.png) / [4:3](progress-confirm-4by3.png)。保存再試行: [FHD](save-retry-fhd.png) / [4K](save-retry-4k.png) / [4:3](save-retry-4by3.png)。Pause: [FHD](pause-fhd.png) / [4K](pause-4k.png) / [4:3](pause-4by3.png)。

## 初回失敗と回復

最初の全3632件は3551成功81失敗。既存非active host起動で未生成checkpoint widgetを参照する共通退行を修正した。[初回全件](first-full-failures.json)。次のIntegrationは596成功9失敗で、先行生成を前提とする旧fixture取得順を実表示入口へ更新。ゲーム条件/保存/時間の期待値を緩めず、未表示Pauseはむしろ未生成を厳密化した。[関連初回](first-integration-failures.json) / [11ケース](lazy-fixture-tests.json) / [初期fixture失敗](initial-fixture-failures.json)。

ヘルパーの新規store前提誤り、非active Scene準備の欠落、Single結果の階層誤参照は製品失敗と区別して保持。動的ヘルパーcompile約206秒で120秒Task予算を使い切ったtimeoutは、同要求の結果と遅延描画を読取で回収し再送しなかった。以後のヘルパー予算は300秒、ゲーム時間は変更なし。通常reload/接続待ちでは同じEditor/PID137059・VM・認証・モデルを維持。[回復履歴](recovery-history.json) / [timeout](tool-timeout-history.json)。

## 必須残件と完了条件

DialogManagerは共通ticket/owner・開閉・重なり順・入力遮断・focus/結果だけ。種別switch・固有ドメイン依存・個別UI生成はない。一方で固有機能は親View/Controllerとfactoryに残り、以下5単位を必須として追跡する。

1. `ProgressActionDialog` / `RetreatConfirmationDialog`
2. `DeckDiscardDialog` / `SavedDeckDeleteDialog` / `ReadOnlyDetailsDialog`
3. `BindingResetDialog`
4. `DialogueSkipDialog` / `DialogueBacklogDialog`
5. `CustomCardDetailsDialog`

各Dialogが表示model/操作状態・固有入力・内容更新/scrollを所有し、保存やゲーム判定は既存サービスへ委譲。callbackだけ渡して固有処理を親へ残す移動では完了としない。全widgetのPrefab serializeや既に管理されたfactoryの改名だけを追加必須にはしない。

## 文書取得と未確認

[仕様保護](spec-update.json): cf6a664〜de9ddb7の探索HUD/ゲージ/最大HP/通常map移動と、bbe75b8の種類別探索率（ボス含む・依頼除外、対象種類全一覧未定）を保護。文書取得であり今回の外界機能実装ではない。

実build、物理pad/touch/IME、聴感、全HotReload、製品装備stat実値、Item skipのnative操作、製品NPC登録、極端な旧長名末尾、完全重複敵/複数同位置ボス、歴史的UGUI/Pipeline/CLI/UnityAI、未特定Sprite基準群は未確認/履歴として保持。外界/ボスラッシュ/D05/工場拡張/未決値の実装は混ぜていない。
