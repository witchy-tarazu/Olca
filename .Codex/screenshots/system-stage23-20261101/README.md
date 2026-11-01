# システム基盤 第23段階 — 初期化と撤退の共通確認

キー／パッド初期化と撤退確認はStage22の`ConfirmationDialog`を共有する。文言・ボタン挙動・既存geometryを渡し、用途別DialogクラスやDialogManagerの種別分岐は増やしていない。未表示の初期化UIは生成せず、終了時に共通Popupを破棄して旧世代を無効化する。

- `GameplayKeyboardSettingsView`は設定候補と保存・適用の既存経路を保持。確認の確定は`Defaults().BeginEdit()`だけ、保存成功まで現在の適用値を変えない。専用reset UI生成と旧確認用Menu分岐を除去し、背面の保存／戻りを拒否する。
- `RetreatConfirmationDialog`は最新可否照会・Pause覆い／復帰・結果通知だけの薄い調整役。本文・ボタン・ticket・入力・破棄は共通Dialogへ委譲し、効果音の既存callbackも維持する。
- 共通`ConfirmationLayout`は表示geometryだけ。`CanConfirm`で押下時の最新可否、`Finished`で親終了も通知する。保存・報酬・checkpoint・入力計算を持たない。
- `ProgressActionDialog`はrevision確認、pending計画、同ticket Retry、取消不能な保存失敗、成功通知を所有する実状態機械。単純二択へ無理に統合せず、業務状態を共通DialogやManagerへ流入させない。

## 検証

全件3653/3653（247秒）、Application541/541、Integration605/605、同Editor2周84件成功。各終了でEventSystem登録・管理生成asset lease残留なし。最終全件console error0、警告59件は[既存fixture診断5分類](warning-classification.json)で抑制していない。

実640×360と両RenderTexture・専用TMPDIR/saveguardを確認して実施。原本clean、928資産一致（既存private URP2例外）、既定保存先未生成、入力・注入・callback/proof key・lease・保存bytesと表示復元は[cleanup](cleanup.json)。登録外Sprite2095基準群は未特定のまま。

[全件](full-tests.json) / [関連](related-tests.json) / [Integration](integration-tests.json) / [同Editor](same-editor-regression.json) / [code review](code-review.json) / [visual review](visual-review.json) / [最終hash](final-source-hashes.json)

## 実入口と保存境界

隔離Bootstrap23001→出撃→Pause→キー／パッド設定。既存InputSystemの仮想機器による実captureで候補を変更し保存→初期化取消・focus復帰・同frame旧保存／戻り拒否→再表示・旧確認拒否→確定で候補だけ初期化→書込前1回失敗→旧適用値とbytes保持→通常保存の再試行（試行2回）→初期値保存・適用と再読込を確認。両機器は除去し、実ユーザー設定を変更していない。[設定実経路](native-settings.json)

確認中のPause再開／再停止、host Close/Open、Single再入で旧View／callbackを破棄。checkpointの実再開入口を通し、Root/Camera/Listener/EventSystem各1・音源3を維持。撤退は取消→旧確定拒否→再表示→保存失敗→同計画再試行2回、所持・通貨・revision整合を確認。[寿命・撤退](native-lifetime-retreat.json)。実backupが前保存hashと一致し、確認した状態でExperience0／Level1／Tokens0を維持した。[保存成長値](native-retreat-growth.json)。非ゼロ報酬の破棄条件等は既存Integration回帰の範囲であり、今回新規に戦闘で稼ぐ実経路は行っていない。

意図的なhost中断／撤退checkpoint書込は、閲覧／取消のbytes不変と区別する。[終端](native-cleanup.json)。実操作中console error/warning0。起動直後のUnityAI read-only接続情報保存warning1と、MCPに残る以前のfixture履歴は[分類](native-console-classification.json)で区別して保持。

## 原寸比較

原寸FHD1920×1080／4K3840×2160／4:3 1440×1080。撮影は停止中の論理状態、Canvas更新後、実RenderTextureを確認。過去参照は同じ確立済みlayoutと解像度の別runで、全世界同一frameや全画素一致とは主張しない。[寸法／SHA／来歴](image-manifest.json)。独立18枚＋4K局所レビュー必須指摘0。

|確認|前FHD|後FHD|後4K|後4:3|
|---|---|---|---|---|
|キー初期化|[前](before-keyboard-reset-fhd.png)|[後](keyboard-reset-fhd.png)|[4K](keyboard-reset-4k.png)|[4:3](keyboard-reset-4by3.png)|
|パッド初期化|[前](before-pad-reset-fhd.png)|[後](pad-reset-fhd.png)|[4K](pad-reset-4k.png)|[4:3](pad-reset-4by3.png)|
|撤退確認|[前](before-retreat-confirm-fhd.png)|[後](retreat-confirm-fhd.png)|[4K](retreat-confirm-4k.png)|[4:3](retreat-confirm-4by3.png)|

キー保存失敗:[FHD](keyboard-save-failure-fhd.png)／[4K](keyboard-save-failure-4k.png)／[4:3](keyboard-save-failure-4by3.png)。パッド保存失敗:[FHD](pad-save-failure-fhd.png)／[4K](pad-save-failure-4k.png)／[4:3](pad-save-failure-4by3.png)。撤退保存再試行:[FHD](retreat-save-retry-fhd.png)／[4K](retreat-save-retry-4k.png)／[4:3](retreat-save-retry-4by3.png)。

## 残件・限界

残必須は会話skipの共通確認化、backlogの固有内容／scroll所有、Custom詳細の固有内容／入力所有。[責務基準の残件](required-followup.json)。クラス数や任意のPrefab化を完了条件にせず、今回で基盤全体完了とはしない。

仕様fetch差分はStage22からなし。[保護記録](spec-update.json)。新外界仕様・未決D05/S08・工場登録拡張を実装していない。実build、物理キー／パッド／touch／IME、聴感、全HotReload、製品stat／Item skip／NPC登録／極端な旧長名、歴史的UGUI/Pipeline/CLI/UnityAI・未特定Sprite2095等の未確認を保持。通常reload／冷間helperと外側待機期限の回収は[tool履歴](tool-history.json)。最終ソース前のfocus順序・UI Closeガードの点検修正はpretest修正で、テスト失敗を捏造していない。
