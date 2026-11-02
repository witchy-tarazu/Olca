# システム第25段階: Custom詳細の責務分離

`CustomCardDetailsDialog` がカード・変身・戦闘対象の本文/属性icon、見出し、対象巡回、長文scroll、閉鎖と機器案内を所有。親 `CustomScreenView` は対象と既存入力の配送・開始終了のframe fenceに集中する。DialogManagerへカード依存を加えず、既存標準Menuとは二重に入力を持たない。

手札初期5/最大10/選択5/LIFO/cost/確定、計算・固定frame・保存形式と保存境界は変更しない。import画像/fontは借用し、所有生成資源だけ返す。

## 検証

Application546、Custom UI382、加工境界22、Integration605、同Editor反復64、最終全3658件成功（237.53秒）。実640×360/2 RenderTexture・専用TMPDIR/saveguard、console error0。警告は [実分類](warning-classification.json) を参照。独立コード/原寸視覚レビュー必須指摘0。

[関連](related-tests.json) / [Custom UI](custom-ui-tests.json) / [加工](material-ui-tests.json) / [Integration](integration-tests.json) / [全件](full-tests.json) / [反復](same-editor-regression.json) / [source hash](final-source-hashes.json) / [cleanup](cleanup.json) / [独立レビュー](independent-review.json)

## 実入口と比較

Bootstrapの隔離保存25001から通常接触してCustomへ入る。InputSystemUIInputModuleに仮想Touchscreenの長押しを送り、実CustomDetailHold経由で開く。直接詳細呼出しを入力成功の代用にしない。元カードfocus/同frame旧確定/旧closeと旧cycle/pauseの実Raycast/親終了/CloseOpen/Single再入・自然Destroyを確認。[実操作](native-details.json) / [寿命](native-lifetime.json)。物理touch未実施。

長文80行と変身長名は独立した表示fixtureであり、製品データ/変身性能登録の完了ではない。製品実カードと戦闘対象の表示を別に記録する。閲覧の手札参照/選択/cost/frameと実保存bytes不変、最後の明示確定/host中断保存は別境界として測定する。

|状態|FHD|4K|4:3|
|---|---|---|---|
|カード|[現](card-details-fhd.png) / [旧](before-card-details-fhd.png)|[現](card-details-4k.png) / [旧](before-card-details-4k.png)|[現](card-details-4by3.png) / [旧](before-card-details-4by3.png)|
|長文末尾|[現](long-details-tail-fhd.png) / [旧](before-long-details-tail-fhd.png)|[現](long-details-tail-4k.png) / [旧](before-long-details-tail-4k.png)|[現](long-details-tail-4by3.png) / [旧](before-long-details-tail-4by3.png)|
|戦闘対象|[現](combatant-details-fhd.png) / [旧](before-combatant-details-fhd.png)|[現](combatant-details-4k.png) / [旧](before-combatant-details-4k.png)|[現](combatant-details-4by3.png) / [旧](before-combatant-details-4by3.png)|
|変身fixture|[現](transformation-details-fhd.png)|[現](transformation-details-4k.png)|[現](transformation-details-4by3.png)|

各撮影群は論理状態を保持しCanvasと実RTを確認。旧画像は公開Stage18の同UI経路・同解像度の参照。カード内容は旧シールド/現チューニングで異なり別実行のため、世界全画素の同frame一致は主張しない。今回は外観変更が目的ではない。

## 基盤の一区切り

[完了条件](foundation-completion.json)。単純確認は共通ConfirmationDialog（編成破棄/保存デッキ削除/キー・pad初期化/撤退/会話skip）。ProgressActionは保存再試行、ReadOnlyDetailsは帰還/図鑑、DialogueBacklogは履歴、CustomCardDetailsは固有読取を所有。Bootstrapのみの本番起動、Rootの各Manager、Session/Core/時計、必要View/Popupの寿命境界という必須移行を一区切りとする。全widget Prefab化など任意抽象化を新たな必須にしない。

[初回失敗・ツール履歴](tool-and-failure-history.json) を保持。EditMode明示終了とPlay自然Destroyを区別し、期待値を緩めない。実build、物理入力/聴感/全HotReload、製品装備stat/Item skip/NPC登録、極端な旧長名、歴史的UGUI/Pipeline/CLI/UnityAI、登録外Sprite2095は独立した未確認/歴史的問題のまま。新外界機能やD05/工場/S08未決値は実装していない。
