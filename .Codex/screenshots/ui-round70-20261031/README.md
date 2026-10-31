# 操作設定の読み面と編集状態（Round70）

通常Start→出撃→初期カスタム→ポーズ→キーボード／パッド設定の実画面を確認。従来の背面が透ける一覧、4:3パッド両端の名前／解除の切れ、待機対象が分かりにくい表示を、既存の水鏡素材で整えた。

見出しと説明を上段、割当一覧を一つの読取面、保存・初期配置・破棄を下段へ整理した。適用済み／未保存候補／入力待機を状態文で示し、待機中も候補の現在値を消さず、操作名と対象強調を添える。必須操作の解除拒否・保存失敗は既存の理由を残して色を区別。同場面での交換はCoreの既存TryAssignを一度だけ呼び、表示文だけ交換結果を伝える。取消入口は入力待機中だけ表示する。

パッドの行見出しを短くし、4:3では列中心を内側へ調整。キーボードも狭幅だけ列間隔を整え、文字の一律縮小はしない。元背景・JikiTourai・水鏡・入力判定・必須／交換規則・保存契約・初期化の候補編集・固定frameは維持。新しい設定機能や性能値、画像生成は追加していない。既存水鏡Spriteの未使用だった読取面への接続が素材面の成果で、新作素材とはしない。

## 原寸比較

|状態|FHD|4K|4:3|
|---|---|---|---|
|キーボード通常|[前](before-keyboard-normal-fhd.png) / [後](after-keyboard-normal-fhd.png)|[前](before-keyboard-normal-4k.png) / [後](after-keyboard-normal-4k.png)|[前](before-keyboard-normal-4by3.png) / [後](after-keyboard-normal-4by3.png)|
|キー入力待機|[前](before-keyboard-listening-fhd.png) / [後](after-keyboard-listening-fhd.png)|[前](before-keyboard-listening-4k.png) / [後](after-keyboard-listening-4k.png)|[前](before-keyboard-listening-4by3.png) / [後](after-keyboard-listening-4by3.png)|
|初期配置の確認|[前](before-reset-confirmation-fhd.png) / [後](after-reset-confirmation-fhd.png)|[前](before-reset-confirmation-4k.png) / [後](after-reset-confirmation-4k.png)|[前](before-reset-confirmation-4by3.png) / [後](after-reset-confirmation-4by3.png)|
|パッド通常|[前](before-pad-normal-fhd.png) / [後](after-pad-normal-fhd.png)|[前](before-pad-normal-4k.png) / [後](after-pad-normal-4k.png)|[前](before-pad-normal-4by3.png) / [後](after-pad-normal-4by3.png)|
|ボタン入力待機|[前](before-pad-listening-fhd.png) / [後](after-pad-listening-fhd.png)|[前](before-pad-listening-4k.png) / [後](after-pad-listening-4k.png)|[前](before-pad-listening-4by3.png) / [後](after-pad-listening-4by3.png)|

## 検証・レビュー

初稿の関連203件は成功したが、原寸視覚レビューでキーボード4:3の右解除列が読取面の枠に接する指摘を受けた。狭幅の列配置を局所修正し、最終ソースを区別して検証する。

## 範囲外・未確認

物理キーボード／パッド、全機器・OS・実機buildは未確認。仮想機器の確認と混同しない。歴史的UGUI例外は未解決のまま。UnityAI接続情報の保存read-only警告は環境警告として区別する。D05製品性能と工場登録拡張は別残件。短文詳細の余白圧縮も今回へ混ぜない。

## 最終実操作と独立レビュー

[実操作結果](actual-result.json)：通常の隔離Start・出撃・自動接敵・初期custom・pauseから設定を開く。実RaycastとPointerDown/Up/Clickで編集・解除・取消・破棄・保存・初期化を操作。仮想KeyboardのJ+K同時押しを採用せず、中立後のSpaceでAttack/Cardを交換。仮想PadのSouth保持でConfirm/Decideを交換し、候補段階では適用済みを変えないことを確認した。必須Confirmの解除は既存Coreが拒否し、表示も理由を示す。

破棄→再表示で元の適用済みに戻り、保存後は同じ隔離保存先を通常再読込・続きからで開き、両デバイスの保存した割当を確認。初期化確認の取消と確定、保存を通し、Keyboard15・Pad37の元の割当mapを完全に復元した。設定編集・保存中、progress/checkpointの6ファイルhashは不変。4:3でも実Raycastで両設定の入力開始・取消・戻りを反復。最後にnative Mouse/Keyboardだけ、Scene EventSystem enabled、実640×360・専用TMPDIRを確認した。

[変更候補・FHD](after-keyboard-changed-fhd.png) / [4K](after-keyboard-changed-4k.png) / [4:3](after-keyboard-changed-4by3.png)

[Pad候補・FHD](after-pad-changed-fhd.png) / [4K](after-pad-changed-4k.png) / [4:3](after-pad-changed-4by3.png)

[必須解除拒否・FHD](after-pad-required-error-fhd.png) / [4K](after-pad-required-error-4k.png) / [4:3](after-pad-required-error-4by3.png)

[独立コードレビュー](code-review.json)は入力／保存の契約境界と位置だけの調整を確認し、具体的未解決0。[独立原寸視覚レビュー](visual-review.json)は最終8枚で右端余白、現在値、待機対象、候補、拒否文と階層を確認し、必須未解決0。全状態／全機器の網羅や美的完成の保証ではない。

[最終関連203件](related-tests.json)は全成功・エラー0。[警告58件](related-warnings.json)は実ログから報酬View未設定50、不正報酬phase2、使用可能カードなし2、設定読込失敗fixture4と分類し、抑制していない。Play現在consoleはエラー0・UnityAI read-only警告1。MCP履歴には前のfixture・already edit・複数PlayのUnityAI記録が残るため、現在のconsole件数と混ぜない。

## ツール待機の扱い

通常再読込時のExisting初期化と、最終recompile後の最初の解像度スクリプトは60000msのPipeline期限を越えた。120000msを指定しても60秒の期限表示が返る場合があることを記録する。editor_statusと読取Snapshot／ScreenStateで処理済み・実640を確認し、初期化・テストを重複起動せず未実施の後段だけ再開した。期限超過を製品失敗や成功の証拠と取り違えず、Editor／VMを再作成しない。最終全件の直前は別途RenderTextureとScreenの双方640×360を正常応答で確認した。

## 最終全件と後片付け

[全3508件](full-tests.json)は全成功（失敗0、244.15秒）。[明示recompile／実RenderTextureとScreen双方640×360／専用TMPDIR](preflight.json)を確認して一度だけ起動した。呼出側180秒の期限後も同じtest_statusを追跡し、重複起動せず完了を回収した。ソースhashは開始・終了・独立コードレビューのものが一致。

最終consoleエラー0・[警告59件](warning-classification.json)はfixture58件とプール上限境界1件。重要な診断を抑制せず、UnityAIのPlay警告やMCPの履歴をこの全件件数へ混ぜない。

[後片付け](cleanup.json)で元プロジェクトclean、validationとsubmissionのtracked Assets一致（既存の非公開URP2ファイル除外）、実640×360・controller inactive・TMPとsave guard、既定のゲーム保存先が未生成であることを確認した。モデル・VM・認証・元背景・フォントを変更していない。

次候補は別件として保留した短文詳細の余白と本文量の対応。今回の操作設定改善に混ぜず、実画面の必要性から次単位を選ぶ。これは全画面の美的完成を示すものではない。
