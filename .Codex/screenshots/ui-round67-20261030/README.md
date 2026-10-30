# カスタムの操作案内と実割当（Round67）

現在のcustom入力を追跡し、加工中のfooter Enterと実Pad Confirm割当が食い違うこと、最後にTouch／Keyboardを使っても接続中Pad帯が残ることを実画面で確認した。Keyboard profileはbattle／explorationであり、customはSpace／Enter・NumpadEnter／Escape等の既存固定操作。そこから別のリバインドを推測して表示しない。

加工の意味状態（最初の素材・2枚目・不成立・候補・実行可）を保持し、描画時に既存InputGuideActivity.Currentと現在custom割当へ再解決する。描画更新はPreviewやClearErrorを呼ばない。Padはcustom Confirm／Decide、Touchは既存ボタンとタップ、Keyboardは実固定キー。Pad帯は最後に使った機器と接続へ連動し、通常の選択／決定と加工の素材選択／実行／取消復帰を区別。既存の2行／狭幅4行と面を保ち、無効な加工中の変身案内を外す。詳細の閉じる案内も同じ機器の実経路へ合わせる。新しい入力項目・操作規則・案内面は作らない。

元背景／JikiTourai／左半分／正方形本絵とmini／手札5×2／初手5・旧10互換／上限5・LIFO／加工とUndo・消費・保存・固定frame／Round60の4修正は維持。D05の製品値と工場登録拡張は未実装のまま、今回へ混ぜない。ラスタ生成・拒否済み生成の再試行はない。

## 原寸前後

同じ旧10枚の隔離保存を別rootへ複製し、実Continue→加工→ボム＋ソード選択後にConfirm=LT／Cancel=RTを既存公開APIで適用。Pad方向、Touch空所、実OSのA入力で最後の機器を切り替えた。全画像は同じ2素材・未実行の実GameView全体。profile適用直後のメモリ値ではなく描画更新後を比較する。

|最後の機器|FHD 前／後|4K 前／後|4:3 前／後|
|---|---|---|---|
|Pad・custom再割当|[前](before-pad-rebound-fhd.png) / [後](after-pad-rebound-fhd.png)|[前](before-pad-rebound-4k.png) / [後](after-pad-rebound-4k.png)|[前](before-pad-rebound-4by3.png) / [後](after-pad-rebound-4by3.png)|
|Touch|[前](before-touch-ready-fhd.png) / [後](after-touch-ready-fhd.png)|[前](before-touch-ready-4k.png) / [後](after-touch-ready-4k.png)|[前](before-touch-ready-4by3.png) / [後](after-touch-ready-4by3.png)|
|KeyboardMouse|[前](before-keyboard-ready-fhd.png) / [後](after-keyboard-ready-fhd.png)|[前](before-keyboard-ready-4k.png) / [後](after-keyboard-ready-4k.png)|[前](before-keyboard-ready-4by3.png) / [後](after-keyboard-ready-4by3.png)|

## 実操作と限界

既存の公開TryApplyGameplayGamepadProfileをユーザーポーズ中に使い、保存先は検証専用rootだけ。Keyboardは既存VMの通常GameウィンドウへXTestキー、Padは仮想GamepadのStateEvent、TouchはTouchState→InputSystemUIInputModuleの実pointer経路。Touch実行コードはonClickや詳細表示を直接呼んでいない。物理入力機器／端末の検証ではない。

OS Enter、再割当LTの保持、Touchの実行ボタンタップが各1回だけ融合し、手札9・Undo1・custom openを維持。各Undoで10へ戻り、古い実行ボタン再送・RT取消・詳細往復を確認し、公開APIで元の割当値を復元した。任意の全ボタン組合せ・全キーボード配列・物理機器・実機buildは未確認。通常決定まで加工のEnterが漏れる現象は、今回の実Enter操作では再現していない。

最初の保存復帰準備がrun_scriptの60秒期限を超えた。操作を再実行せずeditor_status／読取SnapshotでContinue画面まで初期化済みと確認し、その先だけを継続した。期限超過の原因は断定せず、Editor再起動・環境変更はしていない。過去のUGUI例外は未解決。


素材の選択は既存Raycastクリックで準備し、実行／取消の機器別経路を確認した。すべての材料選択キー・全割当の網羅を主張するものではない。状態JSONはイベント直後のメモリも含むため、同フレームの描画前にある文の更新は原寸画像と区別する。

## 最終検証

関連 **509件成功**（380＋98＋9＋加工境界22）、最終全 **3508件成功**（242.59秒、失敗／skip0）、コンパイル／console error0。実640×360と専用TMPDIR／save guard・最終source一致を確認して全件を一度だけ起動し、呼出側180秒期限後も同じ実行を追跡した。

warning59は報酬View未設定fixture50、設定読込失敗fixture4、誤フェーズ2、使用可能カードなし2、プール上限1で実ログ分類。Play警告は[実件数](play-warnings.json)へ分離し、環境／toolの診断を製品エラーと混同しない。抑制なし。原本clean、追跡Assets一致（検証専用URP2件除く）、実640×360／inactive controller／専用TMPDIR・保存guardへ復元。テスト前後ともnative Mouse／Keyboardだけを確認し、検証のPad／Touchは除去済み。

- [コードレビュー](code-review.md)／[原寸視覚レビュー](visual-review.md)
- [機器別の実操作](actual-operations.json)／[関連](targeted-tests.json)／[最終全件・source](final-test-result.json)
- [全件警告](warnings.json)／[Play警告](play-warnings.json)／[後片付け](cleanup.json)／[機器](input-cleanup.json)／[元保存保護](original-save-preserved.json)

最終9画像の視覚レビューとコード差分の範囲で未解決0。美的完成・全ゲーム無欠陥とは別。次の実質的な見た目候補は編成画面の現在デッキ／所持候補・選択と操作の視覚階層。D05の未登録性能／ソード専用登録の拡張は引継ぎの別項目のまま。
