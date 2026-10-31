# World表示と素材取得 第六段階

DungeonRoot7オブジェクト（Player/PlayerSprite、背景、Ground、WorldBattleGrid、WorldDamageNumberPool、GameFieldView）を実Prefabへ移行し、ResourceManager leaseをViewManager world層で所有します。[Scene差分](scene-scope.json)は23worldブロック＋2Material削除、Controller参照とSceneRoots以外の不変を確認。

WorldPresentationBindingが材質を供給してからCameraを束縛し、HUD/Battle/input準備後にWorldを有効化→既存Initialize。ControllerはCore/Input/固定時計/攻略状態を維持します。旧直Playも同じWorldを供給し、Cameraは互換Sceneのものを使います。

[Ground材質](ground-material.json)・[背景材質](background-material.json)は通常Unity資産へ分離し、資産名以外のshader/keyword/保存値は元Scene Materialと一致。[正規化とimport確認](material-normalization.json)は空LockedProperties scalar表現だけの変更。Prefabは新MaterialGUID/Sceneへ依存せず、Resources pathで有効化前に供給します。WorldVisualResources/ResourceManagerは共有importの取得だけを担当し、未登録任意素材null・必須素材失敗を区別。backjump Sprite、marker Texture、地形/攻撃演出などの生成物は既存Viewが所有します。

敵はGameFieldのWorld子、床/予兆はWorldBattleGrid、数値はWorldDamageNumberPool（既存10/20）、前景独立ルート/TextureはScrollingBackgroundが所有。終了はBattle/HUD→Camera返却→World非active→モデル購読/Camera/resolver参照解除→lease破棄。RootのUI/Camera/音と二重所有しません。

[設計・残依存](../../../Planning/GameSystem管理基盤と段階移行.md)

|場面|FHD前→後|4K前→後|4:3前→後|
|---|---|---|---|
|Custom|[前](before-custom-fhd.png) / [後](after-custom-fhd.png)|[前](before-custom-4k.png) / [後](after-custom-4k.png)|[前](before-custom-4by3.png) / [後](after-custom-4by3.png)|
|複数敵戦|[前](before-battle-host-fhd.png) / [後](after-battle-host-fhd.png)|[前](before-battle-host-4k.png) / [後](after-battle-host-4k.png)|[前](before-battle-host-4by3.png) / [後](after-battle-host-4by3.png)|

前6枚は変更前HEAD094cab5のStage5撮影から[出所とbyte一致](before-provenance.json)を確認して引用し、今回の新before撮影とは扱いません。同じテストmap/seed7・Custom frame0/closed frame60です。idle位相等の別run差をpixel同一と断定しません。

管理起動：[戦闘FHD](managed-battle-host-fhd.png)/[4K](managed-battle-host-4k.png)/[4:3](managed-battle-host-4by3.png)、[設定FHD](keyboard-settings-fhd.png)/[4K](keyboard-settings-4k.png)/[4:3](keyboard-settings-4by3.png)、[Dialog FHD](reset-dialog-fhd.png)/[4K](reset-dialog-4k.png)/[4:3](reset-dialog-4by3.png)。次の通常敵（セル80）と実ボスは識別情報で区別。[実ボスFHD](actual-boss-fhd.png)/[4K](actual-boss-4k.png)/[4:3](actual-boss-4by3.png)はIsBossGauge/GaugeVisibleを実状態で確認しました。bossの一致before画像は今回ありません。

関連12/12成功10.97秒。最終全件3569/3569成功276.76秒、error0。 初回と診断付き再全件はともに3568成功/1失敗。診断でheader上端1.5に対し数値1.329/4.285、Camera位置(2.40,1.28)/size3.15を確認しました。単体時のCamera位置(0,0)/size5と異なり、群の上端補正と衝突するambientCamera依存fixtureでした。期待値を維持し専用Cameraを供給、他Cameraタグをfinallyで復元します。製品の浮上/補正や性能は変えていません。境界専用テストも維持しています。 [Camera回帰22件](pool-camera-regression.json)は成功しましたが、Runner直前のambient設定を全実行中の同一状態の証明とは扱いません。対象自身のCamera identityと同じ全件順序の最終成功を確認しています。正規化後の再全件では別の既存fixtureのEventSystem解除漏れが露出しました。[診断](event-leak-diagnosis.json)でScene実体0/static破棄済みwrapper2、EditModeの手動OnEnableと解除の欠落、run末尾→次run先頭の順を区別。[自己所有の解除と失敗時finally](event-leak-review.json)だけを修正し、strict null前提/製品管理コードは維持しました。[同Editor2周回帰](event-regression.json)ではstatic一覧を消去せず各Scroll1/UI4後に空を確認しています。 [全件終了直後](post-full-event-registry.json)もCLR/Unity current null・static0・Scene0でした。 警告は[実分類](warning-classification.json)、[独立設計/コード/原寸レビュー](independent-review.json)は今回範囲で具体的未解決0。

[実経路](native-operations.json)：直Play/Bootstrap/Dialog/保存復帰/Single/close-open/missing/cancel、World/背景/pool/Cameraの個数と再利用。closed時World/役者/床/背景/pool/SpriteRenderer0、旧購読/モデル/Camera/resolver参照解除。[Custom/次戦](custom-flow.json)：Raycast選択/LIFO/詳細、仮想pad加工取消と切断時の正常Resume、既存damageAPIによる被弾/回復数値・poolclear、fixture勝利→実報酬skip→通常探索/訪問退室→次接触。同じWorld/UI実体を維持。[ボス](boss-route.json)も通常報酬/探索を通しました。物理の攻撃で勝利した検証ではありません。

[console履歴](console-classification.json)と[限定Editorログ](native-error-log-lines.json)でPipelineのThreadAbort/timeoutを製品結果から分離し、native期間を清浄とは扱いません。最終suiteは履歴保全後に別clearし、error0を確認。 公開checkはAnimator空文字m_WarningMessageの末尾空白1行で停止しました。ゲートは広げず[正規化](prefab-normalization.json)し、Unity再importでString空値/Animator1/objects7の一致を確認して最終資産で再検証。掲載画像はこの1行正規化前で、pixelの新撮影やbyte同一とは扱いません。歴史的UGUI未解決/UnityAI/FMOD無音は保持。

Sceneには互換MainCamera/GameManager/CardSelectionCanvasが残ります。Core/Input/時計/攻略状態、UI内部Initialize、既存静的素材Catalog、Camera追従計算などは未移行。外界/ボスラッシュ/未決値の実装や全体安定化完了とは混同しません。実build/物理機器/OS/聴感は未確認。engine解放失敗の破壊的注入はせず、隔離lease回帰のretryと区別します。

公開は[check/commit/push](../../tools/README.md)を別操作にし、必須失敗で停止、最終資産byteとremoteを照合します。
