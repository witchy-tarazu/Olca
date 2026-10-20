# §16 地形進入2修正の合同監査（2026-10-20）

対象: e4739cc（敵の同居マグマ判定）、4aaa7e9（プレイヤーの進入通知集約）。自動import3件を保全し、論理位置と一時ヒット座標の混同だけを関連修正する。

|位置変更|仕様・実装の根拠|判定|
|---|---|---|
|戦闘の通常移動|全体仕様§7の中間フレーム確定、§16の地形接触。PlayerMovementSystem.AdvanceNormalMoveが確定時にMoveTo|実際の論理位置変更で1回だけ進入。通常専用OnMovedとの二重購読は撤去済み|
|敵の論理移動|§16の同居全員を個別判定してから消費。Enemy.MoveToの位置通知|生存占有者を先に固定。火属性/浮遊/無敵/バリアを個別判定し、削り0・設定済み無敵。同期勝敗も地形消費後|
|一時移動カードと復帰|カード効果について§3.3: ヒット判定は移動先、移動可否判断は元。PlayerCharacter.MoveTemporarily/ReturnToOriginalPositionは論理位置を変更しない|4aaa7e9で同じ通知を新規接地へ一律転用していた。新しい進入だけGetLogicalPositionの実変化に限定し、従来の占有位置からの離脱/ヒビ破壊は維持する。独自の一時移動地形ルールは追加しない|
|探索ジャンプ/着地・ダッシュ|全体仕様§9/§16の探索規則。Dungeon.PlayerMovementが連続位置/高さを別管理|BattleSystemのグリッド通知を通らない。探索の接地判定実装まで完成したとは扱わない|
|接敵ノックバック表示|DungeonSystem.SetupBattlePositionsはBattleSystem生成前。表示側ContactAnimator等のTransform進行は別|表示移動を新しい論理接地として扱わない|
|吹き飛ばし・引き寄せ|全体仕様§15。実位置が変わる場合のみ進入入口を使用できる|今回新たな強制移動効果/入力を作らない。既存の低水準位置変更試験と、通常入力での発動確認を区別する|
|開始配置・チェックポイント復元|DungeonSystemは位置を復元してから新BattleSystemを構築|購読前の配置を新規進入として再適用しない。保存形式変更なし|

GUIでは新しい固定fixtureの地形・敵停止をAPIで準備し、実Input System Dの通常移動により進入を確認する。素材転送・外部共有は行わない。全EditModeは関連修正の最後に一度だけ実行する。

## GUI確認

Editor PID19159、既存通常DungeonScene/Controller。独立root `/workspace/work/Olca-terrain-entry-proof-20261020` へ旧mobile-v2のチェックポイントだけ通常コピー。ContinueCheckpointButton→手札ソード選択→Input System Enterで通常戦闘へ復帰。準備はAPIで敵スタン/位置/マグマ2/停止障害物3/HP8ダメージ/浮遊フラグを設定したfixtureであり、製品データ変更ではない。

その後は実Input System KeyboardState Dの押下/解放を通常Controllerへ送り、1→2マスの移動を観測。地上HP92→87、浮遊HP79→79、双方強靭度蓄積8不変/パネル消費イベント1回/Normalへ復帰。入力による新しいノックバックや一時移動カードの発動確認ではない。画像はローカル `/workspace/.olca-cloud/results/terrain-entry-audit/gui-ground.png` のみ。

準備Setup/Configureの応答はタイムアウトしたが、その後のroot/シーン/Controller.enabled/Dungeon/Editor pause読取で完了を確認し二重実行しなかった。GUI現在console error0/compilationFailed=false、警告1。履歴のPipeline操作タイムアウトは製品エラーと区別する。Editorは同じPIDのPlay/一時停止で保持。画像転送・共有・認証/通信設定変更なし。

## 回帰失敗と修正

最初の全件1847件中1件が失敗: 既存の一時移動先から復帰した際、ヒビが穴へ変わる離脱判定を止めていた。進入と離脱の比較座標を分離して修正し、既存離脱を保持した。first-all-tests.jsonを保存。GUIの通常D入力はこの離脱復元前に確認したもので、当該追修正は一時移動離脱のみ。GUIの使用ソースをgui-source-battle.csへ保持し、最終ソースと完全一致のGUI再試験とは扱わない。最終Core全件で一時移動と通常移動の双方を確認する。

合同監査の最終回帰: UnityMCP console→全EditMode→console、1847/1847成功（44.44秒）、現在error0/compilationFailed=false。初回1失敗の原因修正を含め今回の全件実行は計2回。旧検証保存JSON72件不変、保護原本差分0。
