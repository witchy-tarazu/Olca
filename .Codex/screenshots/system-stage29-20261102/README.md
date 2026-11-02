# 外界探索: 検証上面への接地

既存Bootstrap初期拠点「ガイカイ タンサク」の検証地形へ、x12〜16・高さ1単位の上面を1つ追加。左右＋既定Space/パッドSouthで飛び乗り、足場上でジャンプ/停止、両端から歩き落ちてfloor0へ戻れる。製品マップ登録や一般的なsolid/one-way衝突の完成ではない。

## 確定と未決

外界の連続地形/着地は文書仕様。製品開始マップ、物理値、側面/下面/天井、身体幅、solid/one-wayは未確定。今回は下降時の足元1点だけを扱い、製品方針を推測していない。[検証モデルと未決範囲](scope-and-limits.json)。

検証上面は左右閉区間、下降crossing位置とframe終点がともに面内なら接地。候補は最初に横切る高い面、同高度はOrdinal ID。終点が外なら途中の一時接地を省略し、支持外へ出たframeは支持解除してV0から重力を開始する。これらは検証上の明示条件。側面/下面/天井/身体幅の衝突、落下damage/穴死亡は追加していない。floor0は検証安全床。

高さ1は既存最大jump3.1を足場上から行ってもHUDに被らないための検証値。width32/start8/横6、jump12/重力24/解放上昇4も既存検証設定で、製品値ではない。二段/壁/ダッシュ/水中/戦闘/永続保存/ダンジョン往復は未実装。

## 実操作と原寸

[実入力/保存不変](native-play.json) / [支持中host再入](platform-host-reentry.json) / [空中host再入](air-host-reentry.json) / [寿命](native-lifetime.json)。InputSystem仮想keyboard/pad＋実EventSystem Raycast、Core座標/支持を直接書き換えていない。物理機器ではない。[実移動動画](live-motion-640.mp4) / [既存ジャンプ動画](live-jump-640.mp4)。

同じ定義からGroundをTiled薄上面として表示し、元PPU/pivotとIdle/Move/Jumpを保持。検証ラベルと上面はplayerより後ろで、操作hitareaを増やさない。元背景/JikiTourai/既存Dungeon/Root Cameraを変更していない。

各行は同frame・高さ/速度/支持IDを固定してCanvas更新後に実RT取得。[manifest](image-manifest.json)。変更前は[Stage28平地](../system-stage28-20261102/README.md)。旧版には上面支持がなく、足場接地frameと同一状態だとは主張しない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|outside-initial|[画像](outside-initial-fhd.png)|[画像](outside-initial-4k.png)|[画像](outside-initial-4by3.png)|
|outside-move|[画像](outside-move-fhd.png)|[画像](outside-move-4k.png)|[画像](outside-move-4by3.png)|
|jump-rise|[画像](jump-rise-fhd.png)|[画像](jump-rise-4k.png)|[画像](jump-rise-4by3.png)|
|jump-apex|[画像](jump-apex-fhd.png)|[画像](jump-apex-4k.png)|[画像](jump-apex-4by3.png)|
|jump-fall|[画像](jump-fall-fhd.png)|[画像](jump-fall-4k.png)|[画像](jump-fall-4by3.png)|
|platform-standing|[画像](platform-standing-fhd.png)|[画像](platform-standing-4k.png)|[画像](platform-standing-4by3.png)|
|platform-jump-apex|[画像](platform-jump-apex-fhd.png)|[画像](platform-jump-apex-4k.png)|[画像](platform-jump-apex-4by3.png)|
|outside-left-edge|[画像](outside-left-edge-fhd.png)|[画像](outside-left-edge-4k.png)|[画像](outside-left-edge-4by3.png)|
|outside-follow|[画像](outside-follow-fhd.png)|[画像](outside-follow-4k.png)|[画像](outside-follow-4by3.png)|
|outside-right-edge|[画像](outside-right-edge-fhd.png)|[画像](outside-right-edge-4k.png)|[画像](outside-right-edge-4by3.png)|
|outside-pause|[画像](outside-pause-fhd.png)|[画像](outside-pause-4k.png)|[画像](outside-pause-4by3.png)|
|returned-hub|[画像](returned-hub-fhd.png)|[画像](returned-hub-4k.png)|[画像](returned-hub-4by3.png)|

## 最終検証

Core25/Application553/Integration605、同Editor反復94、全3686件成功（317.23秒）。実640×360/2RT・専用TMPDIR/saveguard、最終15source hash一致。[全件](full-tests.json) / [関連](related-tests.json) / [Core](core-tests.json) / [反復](same-editor-regression.json) / [独立レビュー](independent-review.json)。console error0、警告は[分類](warning-classification.json)。

[cleanup](cleanup.json)で原本clean/928資産一致（従来privateURP2例外のみ）、仮想機器/proof/注入/lease/cache/EventSystemと実640/TMPを確認。Native保存bytesは再開時の明示baselineから不変。停止前の初期入場・旧入力検証でも元Python内の比較を通過したが、その初回hash辞書は停止時に失われたため、再開後のbaselineと区別する。旧Dungeon状態は操作中不変。host Suspendは既存の別保存境界。Root支持保持はプロセス内であり外界永続保存ではない。

[範囲/未確認](scope-and-limits.json) / [履歴](tool-history.json) / [検証停止と読取結果](verification-history.json) / [設計](../../../Planning/外界探索入口の実装段階.md)。実build/物理入力/聴感/全HotReloadと歴史的問題等は別に保持する。
