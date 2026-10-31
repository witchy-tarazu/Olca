# Battle/Custom 表示Prefab 第五段階

BattleView配下50オブジェクト/162 Sceneブロックを実Prefabへ移行しました。[Scene範囲](scene-scope.json)は残る2ブロック（Controller参照とSceneRoots）以外の不変を確認。[Prefab Text9値/Font/localID](prefab-values.json)もUnity読込前後一致。新しい絵・ゲーム値は追加していません。

BattlePresentationBindingがResourceManagerの生成leaseをViewManagerのbattle層へ登録し、生成時にBattleView/CustomScreenViewをControllerへ渡します。CustomはBattle内の操作モードで既存pause/inputを維持。ポップアップのDialogManagerと設定Viewを置換しません。終了はBattleSession.Detach→破棄専用演出解除（layout再生成なし）→Customのmodel/input/Mode/delegate解除→owner解除/破棄→Controller参照解除。生成Sprite/手札は各Viewが所有します。

Customの素材resolverが図鑑/店でも必要なためhost開始時に生成し、戦闘ごとのactive切替は既存Controllerです。戦闘時だけの遅延loadは未移行。旧Scene直起動も同じPrefab供給を使い、明示Viewを注入する既存fixtureは維持します。

[設計と残依存](../../../Planning/GameSystem管理基盤と段階移行.md)

|場面|FHD前→後|4K前→後|4:3前→後|
|---|---|---|---|
|Custom|[前](before-custom-fhd.png) / [後](after-custom-fhd.png)|[前](before-custom-4k.png) / [後](after-custom-4k.png)|[前](before-custom-4by3.png) / [後](after-custom-4by3.png)|
|閉じた戦闘HUD|[前](before-battle-host-fhd.png) / [後](after-battle-host-fhd.png)|[前](before-battle-host-4k.png) / [後](after-battle-host-4k.png)|[前](before-battle-host-4by3.png) / [後](after-battle-host-4by3.png)|

管理起動：[戦闘FHD](managed-battle-host-fhd.png) / [4K](managed-battle-host-4k.png) / [4:3](managed-battle-host-4by3.png)。[設定FHD](keyboard-settings-fhd.png) / [4K](keyboard-settings-4k.png) / [4:3](keyboard-settings-4by3.png)。[Dialog FHD](reset-dialog-fhd.png) / [4K](reset-dialog-4k.png) / [4:3](reset-dialog-4by3.png)。独立原寸確認は今回15枚で新しい必須指摘0。異なるrunの描画位相/敵数等をpixel同一と扱っていません。

関連9/9成功8.1秒。最終全件3566/3566成功285.71秒、error0。警告は[実分類](warning-classification.json)。[独立設計/コード/原寸レビュー](independent-review.json)は今回範囲の未解決具体指摘0です。

[実経路](native-operations.json)：直Play/Bootstrap/確認Dialog/保存復帰/Single再入/host close-open/ロード失敗取消、camera/listener/event各1、closed時Battle/Custom0、旧入力/モデル参照と生成Sprite解放、root/audio/camera/UI再利用、保存byte不変。[Custom実操作](custom-flow.json)：Raycast手札選択1→2→LIFO1→0、詳細往復、InputSystem仮想padで加工開始/取消。[次戦](next-battle.json)：既存damage APIの勝利fixture→実UIの報酬skip/訪問退室→次接触、同じView実体と新しいBattleモデル、古い購読解除を確認。物理の戦闘操作を通した勝利ではありません。

検証pad除去は仕様どおり自動pauseとなり、先頭RaycastがPauseOverlayだったため通常Resume操作を挟みました。次戦待ちは既存shop/eventで止まっており、通常ExitVisitで進行しました。いずれも製品コード変更なし。[console/timeout履歴](console-classification.json)は最終suiteのerror0と分離。ネイティブ時はMCP列挙2件がPipeline timeout。groundTruthカウンタ3との差1件は[限定Editorログ](native-error-log-lines.json)のPipeline `Thread was being aborted`で照合しました。ネイティブ期間全体の清浄とは報告しません。

Core/Input/固定時計・攻略状態、GameFieldの役者/床/背景、操作設定内部Initialize、Custom内部の従来詳細UIは残ります。外界/ボスラッシュ文書やD05/S08を実装完了へ混ぜません。実build/物理機器/OS/聴感は未確認。破壊的なengine unload失敗注入は行わず、隔離lease回帰でretryを確認。歴史的UGUI未解決とPipeline/UnityAI履歴も保持します。

公開checkは不変Materialのm_LockedProperties行で一度停止し、Assetsを改変せず同一type/一意fileIDの完全block byte一致を厳密指定行にだけ証明する判定を追加しました。stripped/未知header・CR・重複ID・新規/変更blockは拒否し、[隔離18テスト](publication-tests.json)と独立レビューで確認。検証済みUnity最終8ファイルは不変です。

公開は[receipt付きcheck/commit/push](../../tools/README.md)を別操作で実行し、必須失敗時は停止します。
