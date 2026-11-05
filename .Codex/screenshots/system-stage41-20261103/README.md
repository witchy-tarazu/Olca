# 新規ゲーム成功時の外界状態の世代切替え

既存Bootstrapの拠点→外界→実キーボード移動→戻る→「はじめから」→確認→外界再入で再現。保存世代だけ更新され、同じSession/runtime/core・位置750000・時計250frame・ゲージ80/100・同じ手札3個体が残った。[初回再現](initial-reproduction.json)。手札の初回登録とゲージ20点消費は追跡用fixtureであり、製品の探索デッキ選択やカード使用の実装ではない。

原因は、外界のRoot寿命保持に対して、新規ゲームの保存成功通知から世代切替えが接続されていなかったこと。`GameSystem.ResetOutsideForNewProgress`は保存済みN形式GUIDを検証し、同世代は無作用。Controllerの成功結果から旧外界bindingをDispose/nullにして、旧runtime→sessionを終了、新しいpairへ交換し、入力キューと同frame境界を更新する。保存・保存形式・報酬・性能・固定60Hz計算は変更していない。通常View/host終了・再入では同じpairを保持する。

修正版は実入口で取消、取消後の旧確認、書込前の一度だけ失敗、取消不可の保留、同ticket/計画の再試行成功を確認。取消/失敗では保存bytesと位置762000・時計246frame・ゲージ80・手札個体が不変。再試行の2回目のwrite成功後だけ、位置480000・時計0・ゲージ100・手札未登録の新pairへ交換。旧runtimeは終了・driver/input参照null、旧Dialog/View callbackは無作用。所持スターター20枚と設定の既存契約は維持。[実経路とidentity](native-product-paths.json)。

View自然Destroyの自動復旧、戻る/再入、hostClose/Open後は新pairを保持。再入前の保持Right+Jumpは流入せず、解放後の実入力で位置300000へ移動した。旧runtimeの時計/位置は進まない。保存bytesも成功後の状態から変化なし。保存失敗は隔離store wrapperの書込前1回失敗で、ユーザーデータやファイルを破損させた試験ではない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|修正前・成功前|[画像](repro-before-newgame-fhd.png)|[画像](repro-before-newgame-4k.png)|[画像](repro-before-newgame-4by3.png)|
|修正前・成功後も旧状態|[画像](repro-after-newgame-fhd.png)|[画像](repro-after-newgame-4k.png)|[画像](repro-after-newgame-4by3.png)|
|修正版・旧世代の状態|[画像](final-before-newgame-fhd.png)|[画像](final-before-newgame-4k.png)|[画像](final-before-newgame-4by3.png)|
|修正版・成功後の新世代|[画像](final-after-newgame-fhd.png)|[画像](final-after-newgame-4k.png)|[画像](final-after-newgame-4by3.png)|

各3比率は同状態/同論理frame凍結、Canvas更新と実Screen/2RT寸法を確認。新規ゲームによる世代間は意図的に位置・frame・手札が異なり、同frameの外観比較とは主張しない。元背景/JikiTourai/キャラ比率・足元/画像/水鏡UIを維持。12枚の原寸独立視覚レビューと3sourceの独立コードレビューは必須指摘0。[画像SHA/寸法](image-manifest.json) / [独立レビュー](independent-review.json)。既知の検証足場ラベルの本体遮蔽は今回で解消したものではない。

最終v1は関連90件（48秒）・全3745件（312.94秒）成功。失敗/skip/inconclusive0、console error0。追加5件は不正GUID3ケース・旧lease/runtime/手札終了と同世代重複通知・通常host保持。実640×360/2RT、専用TMPDIR/saveguard、新run開始/終了、最終ソース/資産一致を確認。[関連](related-tests.json) / [全件](full-tests.json)。警告は既存fixtureを[分類](warning-classification.json)し抑制していない。

再読込と初回動的ヘルパーcompileは同じEditor137059/既存jobで回収。recompile/状態照会と全件待受のtimeoutは再投入せず結果回収した。未対応refresh_assets要求1回は実行されず、Pipelineの対応recompileへ進め、[履歴](unsupported-command-history.json)を分類して保存。検証ヘルパーの引数形式拒否と旧フィールド/プロパティ読取失敗は、実行済みの操作を確認して同じPlay/Dialogから復旧し、製品C#の変更や保存仕様変更で回避していない。初回再現・関連・修正版Playを互いの成功証拠に代用していない。

原本clean/既定SHA、928資産照合（従来privateURP2例外）、保存bytes、仮想機器・注入・旧callback/proof/lease/cache/EventSystem、低解像度/TMP復元は[cleanup](cleanup.json) / [追加キー](extra-cleanup-keys.json)。未使用の過去ログはSHAと解凍内容一致を検証してgzip保全し、稼働中ログは変更していない。

S09/S10/S11/S12、探索デッキ切替/同ID編集/選択中削除、外界永続保存と「つづきから」、製品マップ/衝突/戦闘や性能登録は未決/別件のまま。今回の新規開始境界をそれらの完成と扱わない。実build/物理入力/聴感/全HotReload/製品stat/Itemskip/NPC登録/極端旧長名、歴史的UGUI/Pipeline/CLI/UnityAI/登録外Sprite2095は独立した未確認・履歴として保持。[実装段階](../../../Planning/外界探索入口の実装段階.md)。
