# 外界View: 閉鎖後の外部破棄と表示中の復旧を区別

管理HideでbindingのView参照を外す最小修正。戻る・別画面切替・CloseCurrentで閉じたViewが後から外部Destroyされても自動再表示しない。表示中の自然Destroyは従来どおりViewを再生成し、Root Core/gaugeの同一実体と状態を維持する。Manager増設・ゲーム規則・保存形式・画像変更なし。

[実操作と自然破棄](native-lifecycle.json) / [host再入と解放](native-host-lifetime.json)。既存入口・戻る・Pauseは実EventSystem Raycast、移動はInputSystem keyboard。外部Destroyと部分ゲージ20の準備は明示検証fixture。通常UIのみで外部破棄が起こる経路は未確認。旧版の再現は[Stage31履歴](../system-stage31-20261102/README.md)を保持し、旧版画像を新たに撮り直したとは扱わない。

閉鎖後は通常時間1000msと旧Back呼出後も拠点focus・非表示・Core/frame/gauge停止・driver/input返却を確認。明示再入とactive自然Destroy回復で同じCore/gauge/残量を確認。fresh入力・held入力Pause/解除、host CloseOpen/Single再入・取消欠損も回帰。通常操作区間の旧Dungeon状態と実隔離保存bytesは不変。既存host Suspendは別の保存境界。

9枚は各行で同論理frame・状態・Canvas更新後の実RT取得。見た目の新デザインではなく、表示すべき画面と復旧の退行確認。[manifest](image-manifest.json)。開いている前後2行は同じCore位置/frame/gauge、閉じた行は拠点表示。

|状態|FHD|4K|4:3|
|---|---|---|---|
|open-before|[画像](open-before-fhd.png)|[画像](open-before-4k.png)|[画像](open-before-4by3.png)|
|closed-after-destroy|[画像](closed-after-destroy-fhd.png)|[画像](closed-after-destroy-4k.png)|[画像](closed-after-destroy-4by3.png)|
|active-recovered|[画像](active-recovered-fhd.png)|[画像](active-recovered-4k.png)|[画像](active-recovered-4by3.png)|

最終Application554/Integration605、同Editor反復122、全3700件成功（303.43秒）。実640×360/2RT・専用TMPDIR/saveguard、最終15source hash一致。[全件](full-tests.json) / [反復](same-editor-regression.json) / [独立コード](independent-code-review.md) / [原寸視覚レビュー](visual-review.json)。console error0、警告は[分類](warning-classification.json)して抑制なし。[cleanup](cleanup.json) / [履歴と未確認](history-and-limits.json) / [設計](../../../Planning/外界探索入口の実装段階.md)。
