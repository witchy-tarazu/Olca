# 外界探索: 検証地形での最小プレイ

初期拠点の「ガイカイ タンサク」から、外界専用Coreの左右移動・停止と水平追従へ接続。Root所有の状態/60Hz時計を、表示とhostの寿命から分離した。入口前のheld入力・Pause復帰held・古いcallbackを遮断し、戻りfocus/自然Destroy/CloseOpen/Single再入を実経路で確認。

**製品マップの完成ではない。** 未登録の開始マップを決めず「ケンショウ チケイ」と明記。平地32単位・開始8・速度6単位/秒は操作/境界を実証する検証設定で、製品バランス値ではない。ジャンプ以降のアクション・外界保存・ダンジョン往復・タッチ移動は未実装。既存idle画像の移動であり歩行本体モーション完成とは扱わない。既存Root Camera設定は変えずViewの連続座標投影で水平追従する。

## 実操作と比較

[実入力/保存不変](native-play.json) / [host寿命と単一時計](native-lifetime.json) / [640×360実動動画](live-motion-640.mp4)。動画は既存Xvfb GameViewの実キャプチャで、原寸UI確認は下表。InputSystem仮想keyboard/Dpad/Startと実EventSystem Raycastを使用し、物理機器ではない。直接Coreの位置/時間を書き換えて成功と扱っていない。

各行は同じ論理frame・状態をTimeScale0で保持し、Canvas更新後の実RTで取得。背景/JikiTourai/元画像比率と足元を維持。

|状態|FHD|4K|4:3|
|---|---|---|---|
|初期表示|[画像](outside-initial-fhd.png)|[画像](outside-initial-4k.png)|[画像](outside-initial-4by3.png)|
|左端・停止|[画像](outside-left-edge-fhd.png)|[画像](outside-left-edge-4k.png)|[画像](outside-left-edge-4by3.png)|
|水平追従|[画像](outside-follow-fhd.png)|[画像](outside-follow-4k.png)|[画像](outside-follow-4by3.png)|
|右端・停止|[画像](outside-right-edge-fhd.png)|[画像](outside-right-edge-4k.png)|[画像](outside-right-edge-4by3.png)|
|Pause|[画像](outside-pause-fhd.png)|[画像](outside-pause-4k.png)|[画像](outside-pause-4by3.png)|
|戻り|[画像](returned-hub-fhd.png)|[画像](returned-hub-4k.png)|[画像](returned-hub-4by3.png)|

中間版の[4:3端ラベル](before-v7/outside-initial-4by3.png)と[Pause背面Chrome](before-v7/outside-pause-fhd.png)は、最終版で表示範囲/被覆を修正した。最終画像の各行では論理frameを固定しているが、中間版と最終版の実プレイ間でframe番号まで同一とは主張しない。

機能追加前は[Stage26静的入口FHD](../system-stage26-20261102/outside-active-fhd.png) / [4K](../system-stage26-20261102/outside-active-4k.png) / [4:3](../system-stage26-20261102/outside-active-4by3.png)。当時は外界移動Coreがなく、今回のゲーム座標/論理frameと同一状態だとは扱わない。

## 検証

Application553 / Core12 / Integration605、同Editor反復68、最終全3673件成功（309.63秒）。実640×360/2RT、専用TMPDIR/saveguard、最終13source hash一致。[全件](full-tests.json) / [関連](related-tests.json) / [Core](core-tests.json) / [Integration](integration-tests.json) / [反復](same-editor-regression.json) / [source hash](final-source-hashes.json) / [cleanup](cleanup.json)。最終console error0。関連Integration終了時にUnityAI relay終了通知1件で検査が停止した記録は[別分類](integration-tool-diagnostic.json)へ保存し、製品605件失敗0と分けた。警告は[分類](warning-classification.json)を別記。

[独立レビュー](independent-review.json)。中間版の関連成功を最終全件へ流用せず、入力gateの採用順と床の実UI周期修正後に検証。既存ダンジョンCoreの位置・HP・revision・論理frameと実隔離保存bytesは移動/戻り/再入で不変。host Suspendの既存保存境界とは区別。RootのCamera/Listener/EventSystem各1・音源3、driver/input返却を確認した。

[実装範囲・未実装](scope-and-limits.json) / [履歴](tool-history.json) / [設計](../../../Planning/外界探索入口の実装段階.md)。実build、物理入力、聴感、全HotReload、既存の製品登録/歴史的UGUI/Pipeline/UnityAI/登録外Sprite等は独立した未確認として保持する。
