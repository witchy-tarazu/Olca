# 設定ViewとDialog寿命 第十段階

pad/音量の2画面を既存keyboardと同じViewManagerへ接続しました。3つの登録keyはkeyboard-settings/gamepad-settings/audio-settings。既存KeyboardSettings Prefabは空RectTransform shellで、3画面ともResourceManager経由で生成し、内容は既存Initializeで組み立てます。新Manager/新Prefab/新依存は増やしていません。[設計と正確な残件](../../../Planning/GameSystem管理基盤と段階移行.md)。

Closeではcached leaseを保持し、新OpenでContextを更新。host終了/Singleでは登録・実体・入力・delegate/Contextを解放します。閉鎖/破棄後のSave/Cancel/Slider/捕捉callbackは拒否します。切替成功まで旧画面を手動Closeせず、欠損/取消で旧画面を保持。padの既存初期化確認はkeyboardと同じDialogManagerへ委譲し、親終了は確定せず取消します。音量に存在しないDialog/初期化機能は追加していません。

音量の保存成功→値更新→SoundManager共有Routerへ適用、既存0..100/音量式/保存形式/保存まで不適用/試聴なしは不変。Root音源を画面ごとに増やしたり、画面Closeでsession BGMを終了したりしません。Core/Factory/CheckpointFlow/固定時計/攻略保存順と見た目は維持しています。

|比較|FHD|4K|4:3|
|---|---|---|---|
|キー設定|[前](before-keyboard-settings-fhd.png) / [後](after-keyboard-settings-fhd.png)|[前](before-keyboard-settings-4k.png) / [後](after-keyboard-settings-4k.png)|[前](before-keyboard-settings-4by3.png) / [後](after-keyboard-settings-4by3.png)|
|キー初期化確認|[前](before-reset-dialog-fhd.png) / [後](after-reset-dialog-fhd.png)|[前](before-reset-dialog-4k.png) / [後](after-reset-dialog-4k.png)|[前](before-reset-dialog-4by3.png) / [後](after-reset-dialog-4by3.png)|
|pad設定|[前](before-gamepad-settings-fhd.png) / [後](after-gamepad-settings-fhd.png)|[前](before-gamepad-settings-4k.png) / [後](after-gamepad-settings-4k.png)|[前](before-gamepad-settings-4by3.png) / [後](after-gamepad-settings-4by3.png)|
|pad初期化確認|[前](before-pad-reset-dialog-fhd.png) / [後](after-pad-reset-dialog-fhd.png)|[前](before-pad-reset-dialog-4k.png) / [後](after-pad-reset-dialog-4k.png)|[前](before-pad-reset-dialog-4by3.png) / [後](after-pad-reset-dialog-4by3.png)|
|音量設定|[前](before-audio-settings-fhd.png) / [後](after-audio-settings-fhd.png)|[前](before-audio-settings-4k.png) / [後](after-audio-settings-4k.png)|[前](before-audio-settings-4by3.png) / [後](after-audio-settings-4by3.png)|

[画像出所](before-provenance.json)：キー系の前はStage9実画像の引用。pad/音量の前は同commitのViewコードを[クラス名だけ変更した検証fixture](legacy-comparison-fixture.txt)で同native host/font/値/frameへ載せ、保存delegateを無効化して撮影しました。旧VM全体の再実行や改修前hostの新撮影とはしません。後は実際のViewManager導線です。Canvas更新/固定frameで比較し、別run idle差や4K viewerの限界は[独立視覚レビュー](visual-review.json)を参照。

[実操作](native-settings.json)：実Raycastボタン/Slider、仮想padの捕捉と競合交換・未保存破棄、未割当保存と初期化、Dialog held-cancel/focus、pause終了、同時要求/実shell生成後取消/欠損、音量変更・保存後適用・再表示、非既定値のSingle/checkpoint/CloseOpen再読込を確認。未保存編集と親遷移・終了の実バイト比較は変更なし。最後は一時設定をUIから既定へ保存し仮想機器を除去しました。設定保存は意図した隔離書込みで、ファイル数は実ledgerに記録します。画面破棄後の旧クリック/Sliderも例外や保存を起こさず、Root音源3つのidentityを保持。[基本導線](native-base.json)も確認しました。

最終関連478/478（40.5秒）、全3586/3586（258.93秒）、skip/error0。初回は477成功/1失敗で、旧登録数1の期待と新3画面が不一致でした。[元失敗](initial-related-failure.json)を保持し、厳密3+正確な3キーへ更新。失敗/終了時0・sameSession・購読assertやguardを緩めていません。製品2fileは初回から不変で、修正したテストを含む最終3sourceで再検証。同Editor反復10件と全件後のEventSystemはCLR/Unity current null・登録0/Scene0で、global clearなし。

[独立レビュー](independent-review.json)は今回の必須未解決0。[cleanup](cleanup.json)で原本clean、実640×360、専用TMPDIR/saveguard、機器・設定復元、追跡資産と最終source一致を確認。[警告分類](warning-classification.json)と[Pipeline/CLI/UnityAI履歴](console-classification.json)は製品最終console0と区別しています。VM/Editor/model/認証は維持、未確定要求/テストを重複起動しませんでした。

[未管理画面/Popup](unmanaged-ui-inventory.json)はpause/checkpoint、帰還/編成等のClearReturn群、会話、装備群、撤退/進行確認/詳細/会話内確認/報酬overlayなどを現所有者と分類付きで追跡。[生成資産](generated-assets-inventory.json)は6系統のstatic生成Sprite/TextureにRoot所有の必須境界が残り、既存instance所有や借用元Textureと区別します。単なる内部Initializeやhostのphase/入力接続を無目的に全抽象化しません。全体安定化完了ではなく、外界/ボスラッシュ/未決値は今回実装せず、実build/物理機器/聴感/全HotReload/歴史的UGUIの限界を保持します。

必須check→receipt確認→commit→通常master push/refetchを分離し、必須失敗なら停止します。
