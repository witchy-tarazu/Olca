# 図鑑の一覧と詳細の読み面（Round69）

既存の図鑑登録／公開判定・同名hash・ページと詳細復帰の監査を繰り返すのでなく、現在の原寸画面でカード行の中央寄せによる名前位置のずれ、Normal Normalと性能の混在、レシピのmini／式／入口の離れ、詳細背面の透けと読取面の不統一を確認した。

一覧を同じ左端へ揃え、既知カードだけ登録済み専用miniを接続。既存Card参照のresolverを使い、IDから別定義の公開を推測しない。カード表示は名前＋段階★／rarity・実効cost・威力／説明の三段へ整形し、値と説明を保持する。現在カテゴリの下線はフォーカスから独立、面材はページ内の行数に追従し、空白を飾りで埋めない。

レシピの式と未知素材「？？？」は変更しない。結果miniを性能詳細の見出し帯へ寄せ、性能本文を上揃えで読む。図鑑／レシピからのreadonlyだけ、見出しと本文の水鏡面を追加し、他画面のreadonlyは変更しない。

元背景／JikiTourai／青白い水鏡／miniと正方形本絵の区別、Round60・65〜68、名前16文字／旧save、登録・保存・公開条件・レシピ計算・sort・input・固定frameを維持。画像生成・製品性能追加・D05/工場拡張はない。素材面の実成果は既にあるminiの未接続経路の補完で、新規絵を作成したとはしない。

## 原寸前後

通常の隔離Start→出撃→初期custom→pause→CardCollectionButtonから開いたカード／未公開レシピと既知カード詳細。公開レシピと結果詳細だけは実在factory/providerの定義を使う保存しない表示fixture（RevealedRecipesだけ与え、素材は未知のまま）。結果の公開と本体の入手登録を混同しない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|カード一覧|[前](before-cards-normal-fhd.png) / [後](after-cards-normal-fhd.png)|[前](before-cards-normal-4k.png) / [後](after-cards-normal-4k.png)|[前](before-cards-normal-4by3.png) / [後](after-cards-normal-4by3.png)|
|未公開レシピ|[前](before-recipes-normal-fhd.png) / [後](after-recipes-normal-fhd.png)|[前](before-recipes-normal-4k.png) / [後](after-recipes-normal-4k.png)|[前](before-recipes-normal-4by3.png) / [後](after-recipes-normal-4by3.png)|
|カード詳細|[前](before-card-detail-fhd.png) / [後](after-card-detail-fhd.png)|[前](before-card-detail-4k.png) / [後](after-card-detail-4k.png)|[前](before-card-detail-4by3.png) / [後](after-card-detail-4by3.png)|
|公開レシピ・fixture|[前](before-recipes-revealed-fhd.png) / [後](after-recipes-revealed-fhd.png)|[前](before-recipes-revealed-4k.png) / [後](after-recipes-revealed-4k.png)|[前](before-recipes-revealed-4by3.png) / [後](after-recipes-revealed-4by3.png)|
|結果詳細・fixture|[前](before-recipe-detail-fhd.png) / [後](after-recipe-detail-fhd.png)|[前](before-recipe-detail-4k.png) / [後](after-recipe-detail-4k.png)|[前](before-recipe-detail-4by3.png) / [後](after-recipe-detail-4by3.png)|

## 検証経過

最初のPlay初期化がrun_scriptの60秒期限を越えた。editor_statusと読取Snapshotで同じ初期化が完了済みと確認し、再初期化せず続行した。原因は断定しない。

初稿の関連457は成功。その後、名前三段化と結果詳細の本文枠500への拡大を追加し、旧長文fixtureを使う6ケースが全文入口を見つけず失敗した。実測は1002文字・preferred492／枠500で全文が収まっていた。性能側だけ1992文字へ長文化し、実overflowのassertを追加した。全文一致・readonly・背面／旧操作拒否・戻りfocus・非保存と短文で入口なしの既存確認は削除／緩和していない。[実測](overflow-measure.json)を残す。初稿や途中失敗を最終成功へ引き継がない。

## 未確認・別残件

物理IME／物理Pad／Touch、実機build、全Unicode／全機器は未確認。表示fixtureを実ゲームで融合取得した証拠や製品登録として扱わない。D05の未登録性能・工場拡張、拒否済み本体素材は別残件のまま。歴史的UGUI例外は未解決、UnityAI接続情報保存read-only警告を製品不具合と混同しない。

視覚レビューで4Kのカード詳細の枠重なりと4:3の内側余白不足が指摘され、図鑑限定readonlyをheader下端145→読み面上端160→本文上端180、左右面gutter80へ再調整した。戻る・Scroll下端は維持し、全文はscrollで読む。短文でも長文用の共通読取領域を保つため大きい余白は残り、内容量に応じた圧縮は任意の後続候補であって美的完成とはしない。

privateの長文測定が裏のRecipePerformanceを拾った1件は、ReadonlyCardPerformance優先へ訂正し実reader1429/1429文字を確認。製品の文字欠落として修正していない。ツールの解像度確認が期限後も完了した場合はready/Screen640を読取確認し、テスト未起動を確認してから続行した。run_scriptの公開schemaにtimeout_msの指定があり、後続検証は120秒の待機枠を使う。ゲーム時間や安全設定の変更ではない。

## 最終実操作

[保存確認](actual-result.json)：実Raycastからカテゴリ／詳細／ページ／戻りを操作し、閲覧前後のJSON本体・backup hash不変。次ページの既知4番だけ詳細とminiがあり、未知5〜7番はなし。通常の未公開レシピには結果mini・性能入口なし。閉じて撤退→保存デッキ画面の再読込→図鑑再表示を通して登録・公開・所持・通貨の値を維持。

[表示fixture確認](fixture-result.json)：同名別hash9件、長名の全文／カード詳細／レシピ性能の全文を末尾まで読み、ページ端往復と空の両tabを確認。保存file hash不変。画面fixtureのカード値は識別検証のみで製品登録しない。

[長名・同名 FHD](after-cards-dense-fhd.png) / [4K](after-cards-dense-4k.png) / [4:3](after-cards-dense-4by3.png)  
[空レシピ FHD](after-empty-recipes-fhd.png) / [4K](after-empty-recipes-4k.png) / [4:3](after-empty-recipes-4by3.png)

関連テスト最終457/457（表示380・連携65・融合12）。検証用データ／実Raycast経路を使用し、物理機器の検証と混同しない。[独立コードレビュー](code-review.md)は今回範囲で未解決指摘0。[Play警告](play-warnings.json)はlive UnityAI警告1と保持されたPipeline警告を区別する。開始と区切りのpullは更新なし。

[独立原寸視覚レビュー](visual-review.md)：代表画像と追加fixtureの確認範囲を明記。修正版3解像度で枠／余白の必須指摘を解消し、確認した範囲で未解決必須0。全15枚網羅やユーザーの美的承認とは主張しない。

## 最終全件と警告

最終ソースのMCP全件 **3508/3508・241.65秒**。実640×360・専用TMPDIR/saveguard・source一致を確認して1回起動、呼出側期限後も同じrunを追跡しcompletedを回収。前後consoleエラー0、検証中source不変。ガード／期待値解除や重複全件起動なし。

全件警告59は報酬view未登録fixture50、操作設定読込fixture4、報酬phase違い2、使用可能カードなし2、pool上限20の境界1。PlayのUnityAI警告1と保持されたPipeline重複停止警告は別記し、抑制していない。

[全件結果](final-test-result.json) / [関連457](targeted-tests.json) / [検証source](production-source-hashes.json) / [全件警告](warnings.json) / [Play警告](play-warnings.json)

後続の実質的候補は、今回任意として残した短文詳細の密度（長文・scroll・入力を保った面の内容高追従）と、既存操作設定画面の割当／競合／保存結果の視覚整合。現物を見て有益なまとまりを選び、無目的な変更や未決仕様の決定はしない。

[後片付け](cleanup.json) / [入力・EventSystem](input-cleanup.json)：実640×360、専用TMPDIR、controller inactiveと保存ガード、native Mouse/Keyboardのみ、元EventSystem有効、元プロジェクトclean、検証Assets一致（既存privateURP2件除外）を確認。VM/Editor/モデル/認証は変更していない。
