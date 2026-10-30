# Round 44: 戦闘表示の主従と共用爆風

基準master: `c43e42ee73cc067afdb68c8db11eaf502dca592d`。開始時・最終検証前の通常pullで仕様更新なし。

## 変更

- 敵名のTextMesh character sizeを.05から.02へ。実測高さ.40→.16world、HP表示.14worldは不変。名前を補足情報にし、HPとダメージを優先。Actorのscale/位置/anchor、文字フォントは不変。
- 下端操作HUDの補足ガイド20→18、状態22→20。主要操作26、入力範囲・パネル位置寸法は不変。
- 人物・参照画像を含まない独立した爆風を画像生成ツールで1枚制作。64×32、真透過、Point filter、既存共用パレットの10色。3タイルの旧白矩形を置換。キャラ本体モーションの完成ではない。
- 爆風は既存の可視幅.82/高さ.18論理単位へ正規化。位置・判定・威力・6論理frame寿命・fadeを維持。実ボム着弾でダメージ40、敵HP1000→960。停止10回Tickで表示不変、3frameでalpha .375、6frameで残留0。

## 比較画像

- [変更前FHD](before-selected-fhd.png) / [変更後FHD](final-selected-fhd.png) / [変更後4K](final-selected-4k.png): 同じSword+Bomb選択、cost3/10、focus Bomb、HP100、敵HP30。
- [カスタム4:3](final-selected-4by3.png) / [通常戦闘4:3](normal-4by3.png)。カスタム4:3では既存のパネル・ゲージによる敵側の遮蔽が残る。今回解決済みとは扱わない。通常戦闘では敵名・HP・ガイドを確認。
- [旧矩形を再構成したfixture](blast-legacy-fhd.png) / [新爆風FHD](blast-after-fhd.png) / [新爆風4K](blast-after-4k.png)。旧矩形は同じ着弾フレームで描画だけを再構成した比較であり、旧ソースを実行した画像ではない。

すべてGameView実取得。無効な初期撮影（Pause画面）は比較対象から除外し、撮影時phase/選択costを確認して撮り直した。画像合成や切り貼りなし。

## 検証

Unity 6000.3.26f1、Unity MCP EditMode最終全3,466件成功（258.01秒、失敗/skip0）。変更後追加は爆風素材の1 TestCase。関連素材6/飛翔体8/敵ゲージ10/進行199件も成功。

最終console→全件→consoleのerror0、compile error0。警告はgroundTruth66件（警告0とは報告しない）。MCP呼出しの180秒待機期限を超えたため、同一実行をtest_statusで回収した。重複テストなし。テスト前後で実Screen/GameView640×360、専用TMPDIR/saveガード、実行後runner停止を確認。最終ソースhashも検証用・公開用で一致。

初期のnative検証終了時にPipeline HTTP処理のThread was being abortedが1件あり、スタックでUnity.Pipeline内と分類・保存後にclear。最終全件後のerror0とは区別する。通常OSの/proc経由のEditor環境読取は権限で不可だったが、既存のEditor内通常APIによるTMPDIR/実temp path一致とガード確認は成功。権限変更はしていない。

独立コード/素材/視覚レビューでは今回範囲の未解決具体指摘なし。4:3カスタム遮蔽は別の残件。背景・JikiTourai・キャラ等倍・ゲーム性能・時間・保存形式は不変。物理パッド/IME/実機OSは未検証。拒否済みの攻撃本体生成は再試行していない。
