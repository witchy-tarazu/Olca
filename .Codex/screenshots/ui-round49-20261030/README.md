# Round 49: 長押し詳細入口の実入力確認と変身詳細

基準master `09bfec7dba5fbed9ed62323cec3a22fd13a10bb6`。開始時の安全なfetch/fast-forward pullで追加仕様なし。§3「変身の表示・詳細確認」の既存規則「スマホでは長押しでカード/変身詳細を開く」を、直接詳細を開く呼出しと区別して検証した。製品C#、入力ガード、既存の暫定0.5秒、表示仕様・素材・保存・ゲーム性能は変更していない。

## 前回未成立の境界

|入力手順|観測|
|---|---|
|Editor停止中に合成PointerDown、その後Editorを解除|対象Buttonと受信先・press参照・Validは一致。しかし約0.21秒でeligible=false/canceled=trueとなり詳細は開かない|
|Editorを先に解除して中立待機、その後同じ合成PointerDown|保持後に詳細が開く。これは解除順序を分ける比較試験であり、実入力モジュール経由の成立証拠とは分ける|
|Editor再生/中立状態で仮想TouchscreenへTouchState.Began→通常InputSystemUIInputModule→長押し→Ended|カード/変身の実入口が全9回成立。詳細コード・PointerDown・Advanceは直接呼んでいない|

`UnityInputProvider.EditorPauseChanged(Unpaused)`は`QueueSnapshot`→`SnapshotTouches`→`TouchUiCancelGuard.CancelAll`を実行する。保持済みの入力を復帰後へ持ち越さない既存ガードで、最初の合成手順は自分でその取消境界を作っていた。復帰後に中立確認してから入力する手順へ直すことで、配線やガードを変更せず成立した。製品長押しの失敗や物理タッチ失敗とは扱わない。

実入力ではPointer種別Touch、入力device Touchscreen、touchId11を受信。既存の暫定0.5秒以上の保持で開き、解放のクリックは取消され、選択カード・変身の仮選択は変化しなかった。Canceled入力では詳細を開かず、解放後も選択不変。解放後のPointerEventDataはInputModuleによってプールへ返されdevice等がクリアされるため、必要な値は解放前に記録した。途中の私的集計がこの寿命を越えて参照した例外は採用せず、値コピーへ直した結果を記録した。

実記録は [input-evidence.json](input-evidence.json)。旧手順・順序を直した合成手順・TouchState経路を分離している。検証の教訓だけを既存 [testing.md](../../rules/testing.md) に追記し、新しい製品テスト/基盤を水増ししていない。

## 原寸表示

|表示|FHD|4K|4:3|
|---|---|---|---|
|カードの実長押し入口|[画像](card-fhd.png)|[画像](card-4k.png)|[画像](card-4by3.png)|
|通常の変身詳細|[画像](transform-fhd.png)|[画像](transform-4k.png)|[画像](transform-4by3.png)|
|長い名称の変身詳細|[画像](long-fhd.png)|[画像](long-4k.png)|[画像](long-4by3.png)|

全て実GameView取得原本。今回は外観変更がないため、架空の見た目前後比較は作らない。前の表が入力手順の比較、こちらは新たに確認した表示の証跡。

変身モデルには自由なDescription欄がない。既存の名称・手札補正・弱点・属性耐性・固有攻撃の生成説明を対象とした。通常名と94文字の長い名前の2定義は、既存型とコンストラクタ既定値を使ったメモリ内の表示fixture。製品名称/能力値として登録・保存・適用していない。長い名称は折り返され、後続の性能値と末尾「60フレーム」まで読める。新しい説明文機能や専用キャラ絵は創作しない。

本文はFHD/4:3約23.61px、4K約47.21px。共通見出し・本文・閉じるの分離が保たれ、今回画像で文字欠けや戦場への追加遮蔽は見つからなかった。元の背景、JikiTourai、正方形カード絵/ミニアイコンとキャラの比率・基準位置を維持。画像生成は不要で、拒否済み生成も再試行していない。寸法/本文は [native-metrics.json](native-metrics.json)、画像hashは [image-manifest.json](image-manifest.json)。

## 復帰と限界

各表示を通常の閉じるボタンのRaycastで戻り、最後に元の画面/変身状態へ再接続してfixtureを破棄した。論理frame60・選択0・変身Pendingなしを保持し、隔離checkpoint/progressの3ファイルはbyte不変。一時入力デバイスはfinallyで取消/解放して全て除去済み。物理タッチ/物理パッド/OS IMEは未確認であり、仮想TouchStateの成立と混同しない。

途中のconsole error1件をUnityの実LogEntriesから取得すると、Pipelineの`HandleExecRequest`にある`Thread was being aborted`だった。記録・分類後にclearし、通常TouchState入口と復帰を再確認してPlay error/warning0を得た。製品起点の例外や過去のEditor障害原因とは断定しない。

独立コードレビューでは製品配線と既存取消ガードを保つ検証手順を確認し、具体指摘0。独立原寸レビューではカード/変身/94文字名のFHD/4K/4:3を確認し、今回画像の判読・美観の具体指摘0。無欠陥保証ではない。

Unity 6000.3.26f1、Unity MCP関連368件成功（20.81秒、error/warning0）、最終EditMode全3,472件成功（238.51秒、失敗/skip0、error0）。警告62件は報酬View省略fixture53、報酬フェーズ外の拒否2、のけぞり中の使用拒否2、不正操作設定の拒否4、プール上限1で、前回から変化なし。新規警告やマーカー不足警告はない。実ログとgroundTruthが一致し、診断を抑制していない。詳細は [warning-classification.json](warning-classification.json)。

起動要求は1回だけで、180秒の呼出待機期限後も同じ実行を追跡した。実ログには連続した開始通知2件と終了通知1件があるが、開始通知間に警告はなく、どちらを起点としても62件。通知の重複原因や二重実行をこの記録だけから断定しない。集計結果は全3,472件成功として回収済み。

最終ソースhashは公開用・検証用・全件時で一致。検証専用URP設定2ファイルを除く既存Assetsは同一。最終実Screen/GameView640×360、専用TMPDIR/保存ガード、Controller非アクティブ、runner/Play停止を確認。保護した原本checkoutは移行SHAのcleanのまま。秘密・環境設定・セーブ・私的スクリプトは公開に含めない。[verification.json](verification.json)へ集約した。

今回の未成立入口は検証順序を正して確認できたため、取消ガードを外す製品修正は行わない。次の視覚改善は、既存装備/パーツ編集など別画面を実画像で評価して選ぶ。攻撃本体素材や製品変身データの別残件を、この確認だけで完成扱いにはしない。
