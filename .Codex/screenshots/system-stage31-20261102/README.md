# 外界探索: カード共通ゲージの最小接続

ゲーム全体仕様§1の197〜202行で時間回復/時間停止時は停止/最大変更時の残量維持と超過切捨て、基本最大量/回復速度の仮値許可、222行でHUD表示を確認。外界Coreが独立OutsideCardGaugeを所有し既存60Hzで回復、Viewは表示だけを担当する。DungeonのCustomGaugeSystem（カスタムを開く時計）は変更/再利用していない。

**最大100・毎秒10は明示仮値**。100点スケールで残量/バー幅を確認し、空→満10秒で回復と停止を観測するため選定。製品バランス値ではない。初期満量100も検証マップだけの設定。1点=60subunitの整数積算で描画時間を回復量へ使わない。消費APIは整数カードコストと同じ単位、不足/負値で非破壊。最大量増加で残量を増やさず、減少時は超過だけ切捨てる。最大正数/回復非負を要求し、未定の最大0以下効果や小数速度を推測clampしない。

カード/引き直し/攻撃/装備効果の登録、同frameの攻撃開始と回復順、外界保存やマップ/回復/ダンジョン往復への接続は未実装。消費/全快/設定はデータAPIであり、今回新しい使用ボタンやキーを作っていない。全探索HUDではなくゲージ1項目だけ。[範囲/未確認](scope-and-limits.json)。

[検証残量準備後の実移動・回復動画](gauge-motion-640.mp4) / [実入口・回復・停止・最大変更・保存](native-gauge.json) / [Rootとhost寿命](native-lifetime.json)。検証APIで部分残量20を準備し、既存Bootstrap拠点の「ガイカイ タンサク」から実RaycastとInputSystem keyboardで移動/ジャンプ/Pause/再入を確認。Core座標やTickを直接書き換えていない。Gaugeのconfig/spend/refillは明記したfixtureであり製品カード使用ではない。nativeパッド/物理入力は未実施。既存host Suspendの保存境界を通常入力/閲覧のbyte不変区間と分離した。

検証途中で閉じたViewを外部Destroyした際、既存のHasLostView判定が自動再表示した。非表示のままというhelper前提が誤りで、ゲージ不具合とは断定していない。通常Backで700ms停止する経路と、表示中の自然Destroy復帰を分け、検証残量20を再準備して後半を確認した。閉鎖後外部Destroyの既存挙動/通常UI経路は別課題として保持し、今回変更していない。前後の画像を単一の連続回復曲線とは扱わない。

初回静的レビューの必須指摘1は、四捨五入で残量59/60を1.0と表示する点。[初回レビュー](initial-static-review.json)を保持し、テスト前に停止。v2は表示だけ小数1桁へ切捨て、59/60と99+59/60の整数コスト直前/満量直前の回帰を追加した。消費や回復を表示へ合わせて変更していない。

24枚は各行の論理frame/位置/高さ/支持/ゲージ値を固定しCanvas更新後の実RTで取得。[manifestと表示数値/幅測定](image-manifest.json)。元背景/JikiTourai/PPU/pivot/既存カード・Dungeonを維持。[前段階](../system-stage30-20261102/README.md)にはゲージHUDがなく、旧版のカード機構完成とは扱わない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|gauge-initial|[画像](gauge-initial-fhd.png)|[画像](gauge-initial-4k.png)|[画像](gauge-initial-4by3.png)|
|gauge-moving|[画像](gauge-moving-fhd.png)|[画像](gauge-moving-4k.png)|[画像](gauge-moving-4by3.png)|
|gauge-jump-apex|[画像](gauge-jump-apex-fhd.png)|[画像](gauge-jump-apex-4k.png)|[画像](gauge-jump-apex-4by3.png)|
|gauge-pause|[画像](gauge-pause-fhd.png)|[画像](gauge-pause-4k.png)|[画像](gauge-pause-4by3.png)|
|gauge-max-increase|[画像](gauge-max-increase-fhd.png)|[画像](gauge-max-increase-4k.png)|[画像](gauge-max-increase-4by3.png)|
|gauge-max-clamp|[画像](gauge-max-clamp-fhd.png)|[画像](gauge-max-clamp-4k.png)|[画像](gauge-max-clamp-4by3.png)|
|gauge-empty|[画像](gauge-empty-fhd.png)|[画像](gauge-empty-4k.png)|[画像](gauge-empty-4by3.png)|
|gauge-restored-full|[画像](gauge-restored-full-fhd.png)|[画像](gauge-restored-full-4k.png)|[画像](gauge-restored-full-4by3.png)|

最終v2はCore38/Application553/Integration605、同Editor反復120、全3699件成功（358.18秒）。[全件](full-tests.json) / [反復](same-editor-regression.json) / [独立レビュー](independent-review.json)。実640×360/2RT・専用TMPDIR/saveguard、最終15hash一致。console error0、警告は[分類](warning-classification.json)して抑制していない。

[cleanup](cleanup.json)で原本clean/928資産照合（従来privateURP2例外のみ）、保存bytes・仮想機器/proof/注入/callback/lease/cache/EventSystem・実640/TMPを確認。[ツール履歴](tool-history.json) / [設計](../../../Planning/外界探索入口の実装段階.md)。実build/物理入力/聴感/全HotReloadと歴史的問題は別に保持する。
