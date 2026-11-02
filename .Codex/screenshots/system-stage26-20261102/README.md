# 外界探索入口: 第一段階

初期拠点の「ガイカイ タンサク」から、独立した静的初期画面へ入り、戻る・再入を行う実経路を追加。GameSystem Root所有のOutsideExplorationSessionを、ViewManager/ResourceManager所有の表示から分離した。

**まだ移動できる外界マップではない。** 移動/戦闘/デッキ/ゲージ/外界保存/ダンジョン往復は未実装。開始マップやゲーム座標/性能の仮値を決めず、既存importの背景/床/idle・JikiTouraiを使った初期表示と寿命だけを閉じた単位にした。既存ダンジョンの計算・保存・固定frameは変更しない。

## 検証

Application548、Core6、Integration605、同Editor反復38、全3662件成功（259.98秒）。console error0、[警告分類](warning-classification.json)。実640×360/2RT・専用TMPDIR/saveguard、最終source hash一致。独立code/native視覚review必須指摘0。

[関連](related-tests.json) / [Core](core-tests.json) / [Integration](integration-tests.json) / [反復](same-editor-regression.json) / [全件](full-tests.json) / [source hash](final-source-hashes.json) / [cleanup](cleanup.json) / [review](independent-review.json)

[実入口](native-entry.json)ではRoot Session/Core/runtimeのidentity・元の位置/HP/revision/論理frameと保存bytes不変を比較。旧Entry/Save/NewGame、戻る同frameの旧Chrome、旧Back、自然Destroy→再生成、仮想pad取消と除去時自動pause→実再開を確認。物理入力ではない。[host寿命](native-lifetime.json)でCloseOpen/Single再入も同じ外界Sessionを保持、共通Camera/Listener/EventSystem各1・音源3を確認。host Suspendの既存保存境界は閲覧のbytes比較と分けた。

|状態|FHD|4K|4:3|
|---|---|---|---|
|初期拠点|[画像](initial-hub-fhd.png)|[画像](initial-hub-4k.png)|[画像](initial-hub-4by3.png)|
|外界初期表示|[画像](outside-active-fhd.png)|[画像](outside-active-4k.png)|[画像](outside-active-4by3.png)|
|戻り|[画像](returned-hub-fhd.png)|[画像](returned-hub-4k.png)|[画像](returned-hub-4by3.png)|
|再入|[画像](outside-reentry-fhd.png)|[画像](outside-reentry-4k.png)|[画像](outside-reentry-4by3.png)|

初回は入口が装備操作と重なり実Raycastで押せず、戻りfocusも既定行となった。[FHD初回](before-header-fix-fhd.png) / [4:3初回](before-header-fix-4by3.png)。さらに漢字残りを視覚reviewで発見。3点をソース修正後、補正なしの最終v3で全経路・画像を取り直した。[初回と試行](initial-failures-and-trial.json)。初回CS1729と通常再読込の待ち/同job追跡も保持し、テスト/Play重複なし。原本・VM/Editor/モデル/認証を維持。

[実装範囲と残り](scope-and-limits.json) / [設計](../../../Planning/外界探索入口の実装段階.md)。初期拠点は既存managed DungeonHostを起点とし、そのCoreは外界表示中停止。新外界Viewはgrid/移動/戦闘を借りて実装済みとは扱わない。Root Sessionの保持は現プロセス内であり、外界永続保存の完成ではない。

実build/物理入力/聴感/全HotReload/製品登録、歴史的UGUI/Pipeline/CLI/UnityAI/登録外Spriteなどの既存限界は独立して保持する。
