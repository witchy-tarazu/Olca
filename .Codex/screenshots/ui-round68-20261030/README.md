# 編成のカード識別と数量（Round68）

拠点の保存デッキ編集／所持からの出撃編成を対象とする。Round52の所持一覧・削除画面、Round53の融合加工、Round65〜67のカスタム画面とは別のView経路。変更前の通常実画面では名称・Normal・威力・編成数・所持数が同じ一行に並び、名称長により数量位置が変わり、専用miniが未接続だった。

既存の一つの候補一覧と並び順を保ち、名称と威力を二段に分離。編成数は既存の−／＋の間、所持数は右側の独立列とし、全体の編成枚数と消費しない説明・採用制限を上部で分ける。既存の登録済み専用miniを解決済みoptionから借用して表示する。未解決定義を同名の別カードに置き換えない。新しい素材の生成・複製や製品値追加はない。

表示だけの変更で、profile/index順、hash-aware draftKeys、通常編成と保存下書きの追加制限差、詳細・ページ・取消と旧callbackガードを維持。デッキ名16文字／実表示幅／旧長名互換／入力取消、20枚と採用制限、保存と非消費、元背景・JikiTourai・Round60／65〜67は変更しない。

## 原寸比較

同じ通常Start→初期custom→pause→撤退確定→保存デッキ一覧→新規→0番を2回追加。各回は別の隔離保存root。描画fixtureの20枚から作った画像ではない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|通常・編集2枚|[前](before-draft-common-fhd.png) / [後](after-draft-common-fhd.png)|[前](before-draft-common-4k.png) / [後](after-draft-common-4k.png)|[前](before-draft-common-4by3.png) / [後](after-draft-common-4by3.png)|

追加の多件／同名別定義／長名／空候補は保存しない表示専用fixtureであり、製品カード登録や実保存内容とは区別する。

[多件／同名別定義・長名 FHD](after-draft-dense-fhd.png) / [4K](after-draft-dense-4k.png) / [4:3](after-draft-dense-4by3.png)  
[空候補 FHD](after-draft-empty-fhd.png) / [4K](after-draft-empty-4k.png) / [4:3](after-draft-empty-4by3.png)

## 実操作と境界

実Raycast→PointerDown/Up/Clickで追加2→削除1→追加2、カード詳細往復、閉じる→編集継続、保存、一覧へ戻る、保存から再読込、編集再開2枚を確認。追加3枚目→閉じる→破棄→再読込で2枚へ戻り、所持20枚・JSON本体/backupのhash不変。SaveEditingDeckは保存後も編集画面に残る既存契約。検証側の一覧へ戻る前提でStopIterationとなったが、実ファイルで2枚保存済みと確認し、保存操作を再送せず閉じる以降から継続した。

13候補／3ページと同名別定義1枚・3枚、長名の全文入口・戻り・ページ端往復、空候補を保存しないfixtureで確認。所持数量は既存保存検証の1〜4に従う。fixture内の性能差は対応検証だけで、製品性能/登録へ反映しない。名前入力の生文字はカナ変換を書き戻さない既存契約のため「検証」のまま。

初稿の原寸画像でroot暗化により上部文字が読みにくく、見出し間隔も狭いと確認し、元の明るい地色へ戻して行全体の位置を調整。最終画像はその後の明示recompile/関連445成功のソース。初稿を成功として引き継いでいない。

独立[コードレビュー](code-review.md)と[原寸視覚レビュー](visual-review.md)の今回範囲は未解決指摘0。4:3の制限説明は主たる名称・数量より小さい副情報。全画面の美的承認やゲーム全体の無欠陥を主張しない。

## 未確認・分離した残り

物理IME・物理パッド／タッチ、実機ビルド、全Unicode名や全機器配列は未確認。実Playの保存確認は2枚の非装備下書きで行い、20枚装備／出撃可否の境界や旧長名・pending・未解決定義・古い操作は既存関連テストで回帰確認。今回の表示fixtureを通常製品データや実取得の証拠とはしない。

歴史的UGUI例外は未解決。UnityAI接続情報保存のread-only警告と、保持されたPipelineの重複停止警告は製品のカード表示／保存不具合と混同せず、抑制しない。D05製品値・工場登録拡張、拒否済み本体素材は別残件のまま。次の視覚候補は図鑑のカード／レシピ一覧と詳細の一貫性で、現物の必要性から選ぶ。

## 最終検証

最終明示recompile errors[]・compilationFailed=false、関連 **445/445**（CustomScreenCostViewTests380 / DungeonBattleManagerTests65）、最終全件 **3508/3508・242.77秒**。実640×360と専用TMPDIR/saveguard、source一致確認後に全件を1回起動し、呼出側期限後も同じrunを追跡してcompletedを回収。期待値緩和・ガード解除・重複全件実行なし。

console前後エラー0。全件の警告59は報酬View未登録fixture50、操作設定読込fixture4、報酬phase違い2、使用可能カードなし2、pool上限20の境界1。Play実consoleのUnityAI read-only警告1と、保持されたPipeline重複停止warningを別記。警告0や歴史的UGUI解決を主張しない。

[関連テスト](targeted-tests.json) / [全件結果](final-test-result.json) / [検証source](production-source-hashes.json) / [警告分類](warnings.json) / [Play警告](play-warnings.json) / [実保存確認](actual-save-result.json)

後片付けは[記録](cleanup.json)と[入力・EventSystem](input-cleanup.json)を参照。実640×360・専用TMPDIR、controller inactive・保存先ガード、仮想デバイスなし（native Mouse/Keyboardのみ）、元EventSystem有効、元プロジェクトclean、検証Assets一致（既存private URP2件除外）を確認。モデル・認証・VM/Editorは変更なし。
