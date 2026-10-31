# 攻略Runtimeの所有・入力・主時計 第八段階

GameSystem.Sessionが普通のサービスGameplayRuntimeSessionを所有し、Input/Random/Loggerの生成、Core factory結果の所有と置換、既存主Loopの所有、driver限定のPump、停止時の入力捕捉/文脈callback/Core/driver解放を扱います。新しいManagerやUpdate/時計は増やしません。Controllerのalias fieldと更新順、主Loopのctor生成時点は互換維持し、所有を採用します。Core引数選択のfactory lambda、表示接続・入力文脈/phase条件はhost側に残ります。

起動失敗ではLoopの所有をController再試行へ戻し、既存StartNewTimeline境界を維持。最終終了ではLoop.Disposeを終端化し、予約/入力command参照とEditor購読を返却します。更新中のDispose後は同frame後段/残catchupを止め、Clock本体や未処理負債はresetしません。正常catchupを描画frame単位で間引かず、pause/追いつき/保存再開の規則を変えていません。会話/探索再開演出の別Loopも今回は統合しません。

[設計・安定化残件](../../../Planning/GameSystem管理基盤と段階移行.md)。本番Bootstrap単独/旧Scene API・直起動互換、既存Managers/Prefab・保存とRoot資源を維持しています。

|直起動の同条件比較|FHD|4K|4:3|
|---|---|---|---|
|Custom|[前](before-custom-fhd.png) / [後](after-custom-fhd.png)|[前](before-custom-4k.png) / [後](after-custom-4k.png)|[前](before-custom-4by3.png) / [後](after-custom-4by3.png)|
|閉じた戦闘|[前](before-battle-host-fhd.png) / [後](after-battle-host-fhd.png)|[前](before-battle-host-4k.png) / [後](after-battle-host-4k.png)|[前](before-battle-host-4by3.png) / [後](after-battle-host-4by3.png)|

前画像はStage7の引用で[出所](before-provenance.json)を明示。新before撮影や別run idleのpixel一致とはしません。管理起動は[戦闘FHD](after-managed-battle-host-fhd.png)/[4K](after-managed-battle-host-4k.png)/[4:3](after-managed-battle-host-4by3.png)、[設定FHD](after-keyboard-settings-fhd.png)/[4K](after-keyboard-settings-4k.png)/[4:3](after-keyboard-settings-4by3.png)、[Dialog FHD](after-reset-dialog-fhd.png)/[4K](after-reset-dialog-4k.png)/[4:3](after-reset-dialog-4by3.png)。管理起動は[複製元](cloned-save-provenance.json)の既存ボスcheckpointから再開し、旧Stage7通常敵背景との違いを退行と誤認せずUIの倍率/文字/配置を比較しました。元背景/JikiTourai/本体スケール/足場は変更していません。

[実経路](native-operations.json)：旧直起動→Bootstrap、Custom/取消/決定、pause、設定変更と既定へ復元、実仮想padによるDialog取消、checkpoint、Dialog中Single、host終了/欠損/生成後取消/同時reopen。[HUDのManager破棄/再生成](hud-regeneration.json)は旧HUDをnative破棄し1個再生成、Runtime/Core/Input/主Loop参照と論理frameを不変としました（開始前menuでframe0、全World/Battleホットリロード機能ではありません）。[timeScale0初回終了](zero-timescale-lifecycle.json)で旧Input/Core/driver/Loop停止と資源返却を確認。実保存9801の6ファイルはSingle前からcheckpoint再開/取消/CloseOpen後までhash一致。View再生成でCoreを作り直さず、意図的なcheckpoint復元だけ既存境界でCoreを置き換えます。

関連472/472（40.24秒）、全3580/3580（278.55秒）、skip0/error0。追加5ケースは正常catchup6/同観測時刻の二重進行なし/別driverと再入拒否、入力購読終了、再試行時Loop返却、tick内と予約command内終了で後続callback0・Frame1/Pending5保持を確認。独立レビューの「更新中終了でも後続catchupが走る」指摘を修正してからこの最終source5で検証しました。[同Editor反復](event-regression.json)と[全件直後](post-full-event-registry.json)のEventSystemはCLR/Unity current null・登録0/Scene0、global clearは使いません。

[独立コード/視覚/最終証跡レビュー](independent-review.json)は今回範囲の必須未解決0。実640×360/専用TMPDIR/save guardを確認し、画像後は低解像度に戻しました。original clean、追跡資産と最終5sourceのsubmission/project/test hash一致、一時機器なしをcleanupで確認。警告は[実分類](warning-classification.json)、Domain Reload後のPipeline timeout/CLIのeditor status名不一致/UnityAI保存警告は[履歴](console-classification.json)に分け、cleanな実Playと偽りません。同じVM/Editor PID137059/model/認証を保持し、未確定Play/fixture/compile/testを重複再送しませんでした。

残る必須境界と任意改善を設計文書で分離しています。Core起動設定/保存復元の組立、入力文脈/phaseと表示接続、会話/再開演出Loopはhostに残る。CreatePauseControlsのprogrammatic画面/局所popupと、SharedAttackPropCatalogの生成Sprite/Texture static cacheのRoot解放所有者は後続の管理境界。Pure C# portable catalogは値データなのでManagerへ機械的に変えません。phase別lazy化やUI内部Initializeの見た目整理はその後の改善候補。D05/S08/外界/ボスラッシュ追加は実装せず、実build/物理機器/OS/聴感と歴史的UGUIは未確認/未解決。全体安定化・全ゲーム無欠陥の宣言ではありません。

公開は必須check→receipt確認→commit→通常master push/refetchを別操作にし、必須失敗で停止します。
