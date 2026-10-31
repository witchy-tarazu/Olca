# Core起動・復帰組立 第九段階

DungeonCoreFactoryへPlayer/加工System/通常Core/加工なし凍結preview/階層Core/checkpoint復元の組立を集約しました。DungeonCheckpointFlowは保存を読み復帰候補を作り、終了済み/敗北撤退確定を拒否します。現在Coreを停止せず、保存確定・入力・時計・表示・Runtime採用は所有しません。GameSystem.Session.Runtimeの既存Core/Input/主Loop所有を維持し、新しいManagerや時計は増やしていません。

hostは候補検証後、既存順の旧Core停止・route束縛、旧演出/購読/表示解除、timeline/入力reset、Runtime/manager置換、新View接続を行います。次出撃のwrite plan・run ID/旧定義検証・保存順・例外時finally入力解放も維持しています。[設計と有限の必須残件](../../../Planning/GameSystem管理基盤と段階移行.md)。会話/探索再開は攻略停止中に進む別時計なので統合しません。

|実画面の退行比較|FHD|4K|4:3|
|---|---|---|---|
|旧直起動Custom|[前](before-custom-fhd.png) / [後](after-custom-fhd.png)|[前](before-custom-4k.png) / [後](after-custom-4k.png)|[前](before-custom-4by3.png) / [後](after-custom-4by3.png)|
|旧直起動戦闘|[前](before-battle-host-fhd.png) / [後](after-battle-host-fhd.png)|[前](before-battle-host-4k.png) / [後](after-battle-host-4k.png)|[前](before-battle-host-4by3.png) / [後](after-battle-host-4by3.png)|
|管理起動保存復帰|[前](before-managed-battle-host-fhd.png) / [後](after-managed-battle-host-fhd.png)|[前](before-managed-battle-host-4k.png) / [後](after-managed-battle-host-4k.png)|[前](before-managed-battle-host-4by3.png) / [後](after-managed-battle-host-4by3.png)|
|操作設定|[前](before-keyboard-settings-fhd.png) / [後](after-keyboard-settings-fhd.png)|[前](before-keyboard-settings-4k.png) / [後](after-keyboard-settings-4k.png)|[前](before-keyboard-settings-4by3.png) / [後](after-keyboard-settings-4by3.png)|
|確認Dialog|[前](before-reset-dialog-fhd.png) / [後](after-reset-dialog-fhd.png)|[前](before-reset-dialog-4k.png) / [後](after-reset-dialog-4k.png)|[前](before-reset-dialog-4by3.png) / [後](after-reset-dialog-4by3.png)|

前画像は[Stage8出所](before-provenance.json)を明示した引用で、pixel一致や新撮影を装いません。元背景/JikiTourai/本体比率/床前景/入力位置は変更していません。直接起動は新規fixture、管理起動は[既存boss保存の隔離複製](cloned-save-provenance.json)です。

[実経路](native-operations.json)は直起動→Bootstrap、HUD再生成、Custom/pause/設定・実仮想pad取消/保存設定復元、checkpoint、Dialog中Single、timeScale0終了、欠損/実生成後取消/同時Openを確認。[HUD再生成](hud-regeneration.json)は開始前menu/frame0でRuntime/Core/Input/主Loop参照不変、全Worldホットリロードの保証ではありません。[報酬保留](pending-reward.json)は既存damageAPIで勝利を作り実UI経路へ入り、開いたまま終了→checkpoint復帰でphase/deckと実6保存fileのhash不変を確認。物理操作による全戦闘攻略とは区別します。

関連476/476（40.41秒）、全3584/3584（265.43秒）、skip0/error0。追加4ケースは加工あり通常/加工なしpreviewの差、復帰候補作成/敗北確定拒否で現Coreと保存不変を確認。[同Editor反復](event-regression.json)と[全件後](post-full-event-registry.json)はEventSystem current CLR/Unity null、static登録0/Scene0。期待値やguardを緩和せずglobal clearもしません。[最終hash](final-source-hashes.json)はsubmission/project/関連/全件一致。

[独立レビュー](independent-review.json)は今回の必須未解決0。実640×360、専用TMPDIR/saveguard、機器/設定復元、original cleanと資産一致を[cleanup](cleanup.json)で確認。[警告分類](warning-classification.json)と[ツール履歴](console-classification.json)は製品テスト結果と分け、Pipeline期限超過を消しません。

残る必須はhost組立の画面/局所popupのViewManager/DialogManager寿命とSharedAttackPropCatalog生成Texture/SpriteのRoot資源所有です。hostのUnity接続/phase入力調停/保存write planは残り、全Controllerメソッド移動を目的にしません。phase別lazy化/内部UI組立整理は後続候補。外界/ボスラッシュ/D05/S08や保存形式は今回追加せず、実build/物理機器/OS/聴感、全HotReload、歴史的UGUIは未確認/未解決を保持。全体安定化完了や全ゲーム無欠陥の宣言ではありません。

必須check→receipt確認→commit→通常master push/refetchを別操作で行い、必須失敗なら停止します。
