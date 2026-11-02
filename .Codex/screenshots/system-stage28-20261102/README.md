# 外界探索: 基本ジャンプと既存モーション

既存Bootstrap初期拠点「ガイカイ タンサク」から左右移動とJumpPrimary（既定Space/パッドSouth）。短押し/保持で高さが変わり、空中左右操作が可能。保持着地や空中押下で次のジャンプを予約しない。製品マップ完成ではなく、検証地形と明記した最小プレイ。

検証値は幅32/開始8/横速度6、ジャンプ初速12単位/秒・重力24単位/秒²・解放時上昇上限4単位/秒。製品値ではない。二段/壁/ダッシュ/水中/落下ダメージ/外界戦闘/永続保存/ダンジョン往復は未実装。

## 実操作と原寸比較

[入力・保存不変](native-play.json) / [空中host再入](air-host-reentry.json) / [寿命](native-lifetime.json) / [実ジャンプ動画](live-jump-640.mp4) / [移動動画](live-motion-640.mp4)。InputSystem仮想keyboard/padと実EventSystem Raycastを使用し、物理機器ではない。Core座標や速度を直接書き換えて成功扱いしていない。

元のPlayer_Idle/Player_Moveとjump_rise/apex/fallを借用。論理frame/縦速度から採取し、元Spriteの比率/ピボット/足元を保持。新キャラ素材や未登録攻撃本体モーションの完成とは扱わない。

各行は同frame・縦位置/速度を停止してCanvas更新後に実RT取得。[manifest](image-manifest.json)。変更前は[Stage27](../system-stage27-20261102/README.md)の水平移動/idle表示。ジャンプ未実装だったため、旧版と同じジャンプframeの比較とは主張しない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|outside-initial|[画像](outside-initial-fhd.png)|[画像](outside-initial-4k.png)|[画像](outside-initial-4by3.png)|
|outside-move|[画像](outside-move-fhd.png)|[画像](outside-move-4k.png)|[画像](outside-move-4by3.png)|
|jump-rise|[画像](jump-rise-fhd.png)|[画像](jump-rise-4k.png)|[画像](jump-rise-4by3.png)|
|jump-apex|[画像](jump-apex-fhd.png)|[画像](jump-apex-4k.png)|[画像](jump-apex-4by3.png)|
|jump-fall|[画像](jump-fall-fhd.png)|[画像](jump-fall-4k.png)|[画像](jump-fall-4by3.png)|
|outside-left-edge|[画像](outside-left-edge-fhd.png)|[画像](outside-left-edge-4k.png)|[画像](outside-left-edge-4by3.png)|
|outside-follow|[画像](outside-follow-fhd.png)|[画像](outside-follow-4k.png)|[画像](outside-follow-4by3.png)|
|outside-right-edge|[画像](outside-right-edge-fhd.png)|[画像](outside-right-edge-4k.png)|[画像](outside-right-edge-4by3.png)|
|outside-pause|[画像](outside-pause-fhd.png)|[画像](outside-pause-4k.png)|[画像](outside-pause-4by3.png)|
|returned-hub|[画像](returned-hub-fhd.png)|[画像](returned-hub-4k.png)|[画像](returned-hub-4by3.png)|

初回Southでjumpと戻るが二重実行された[実証](first-south-failure.json) / [停止記録](first-south-failure.log)。外界MenuPadScopeだけ取消専用とし、既存画面の決定/方向を変更しなかった。最終ソースでSouth/取消/Pauseを再検証してから公開した。

中間版の[歩行sample確認](move-connection-read.json)で、clip8キーが有効でもSpriteRenderer単体の結果がnullと判明。[Animatorを持つ実証](move-bare-animator-candidate.json)に従い、v3で無効・controllerなしAnimatorをsample対象へ追加した。自動再生や別時計は追加していない。[中間検査停止](intermediate-move-failure.log)を保存。helperがVector2を直接返して文字列へフォールバックした検証側問題も分離し、数値形式で実操作周期を取り直した。

独立視覚レビューで赤白mock待機と灰黒Move/Jumpの衣装不連続も確認。v4でOutsideの待機に、同じ組の既存Player_Idle clipを使い連続性を揃えた。旧Dungeonやimport素材は変更していない。変更前の初期地点画像：[FHD](before-idle-coherence/outside-initial-fhd.png) / [4K](before-idle-coherence/outside-initial-4k.png) / [4:3](before-idle-coherence/outside-initial-4by3.png)。解像度と初期地点/高さを揃え、別Play間のframe番号まで一致したとは主張しない。

## 最終検証

Core17/Application553/Integration605、同Editor反復78、全3678件成功（295.75秒）。実640×360/2RT・専用TMPDIR/saveguard、15source hash一致。[全件](full-tests.json) / [関連](related-tests.json) / [Core](core-tests.json) / [反復](same-editor-regression.json) / [独立レビュー](independent-review.json)。console error0、警告は[分類](warning-classification.json)。

[cleanup](cleanup.json)で原本clean/資産一致、仮想機器/proof/注入/lease/cache/EventSystemと表示設定復元、隔離保存bytes不変を確認。既存Dungeon状態と固定論理更新・保存互換は維持。Root内保持はプロセス内であり外界永続保存ではない。

[範囲と未確認](scope-and-limits.json) / [履歴](tool-history.json) / [設計](../../../Planning/外界探索入口の実装段階.md)。歴史的UGUI/Pipeline/CLI/UnityAIや登録外Sprite、実build/物理入力/聴感/全HotReload等は別の未確認として保持。
