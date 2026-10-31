# 内容量に応じた詳細の読み面（Round71）

Round69の短いカード詳細は、4行の本文に対して最大の読取面が確保され、空の枠が長く続いていた。レシピ結果も本文500高固定だった。今回、本文幅確定後の必要高へ読み面を合わせ、上端と各画面の既存の戻る位置を維持した。短文を中央へ浮かせず、長文は従来の最大領域とScroll／全文入口を使う。

共通readonlyは本文＋20、最小120、最大は既存の親anchor／offsetに基づく領域。縮めたrectを次の上限に再利用しない。本文・親サイズ・pixelsPerUnitを更新条件にし、FHD／4Kで同じ論理寸法でも再計測する。レシピプレビューは最大500を維持し、既存の全文入口が確保した幅／Xを残して高さと読み面・注意書きを追従させた。

縮小した初稿では、下に旧一覧の文字や操作が透けた。独立原寸レビューでも操作候補に見えると指摘され、readonlyモーダルの背面だけ既存Surface RGBのままalpha1にした。前景の水鏡素材・親画面のactive状態・入力rootは変えず、背面の文字を遮蔽する。固定の戻るまでには意図した空間が残るが、大きな空枠は残さない。

元背景／JikiTourai／青白い水鏡／画像比率／専用mini、Round60・65〜70、全入力・保存・計算契約を維持。公開判定・登録・未知素材「？？？」・Card/Recipeのデータは変えていない。画像生成・製品性能・D05／工場拡張は扱わない。

## 原寸前後

通常の隔離Start→出撃→自動接敵→初期custom→pause→図鑑からカード詳細を開いた。レシピの公開結果は既存factory/providerの定義を使う保存しない表示fixtureで、素材は未知のまま。空説明／長文／長名は既存値を基にした表示fixtureで、定義登録・入手・公開の製品データを増やさない。

|状態|FHD|4K|4:3|
|---|---|---|---|
|短いカード詳細|[前](before-card-short-fhd.png) / [後](after-card-short-fhd.png)|[前](before-card-short-4k.png) / [後](after-card-short-4k.png)|[前](before-card-short-4by3.png) / [後](after-card-short-4by3.png)|
|短いレシピ結果|[前](before-recipe-short-fhd.png) / [後](after-recipe-short-fhd.png)|[前](before-recipe-short-4k.png) / [後](after-recipe-short-4k.png)|[前](before-recipe-short-4by3.png) / [後](after-recipe-short-4by3.png)|
|説明が空のカード|[前](before-empty-description-fhd.png) / [後](after-empty-description-fhd.png)|[前](before-empty-description-4k.png) / [後](after-empty-description-4k.png)|[前](before-empty-description-4by3.png) / [後](after-empty-description-4by3.png)|
|長いカード説明|[前](before-card-long-fhd.png) / [後](after-card-long-fhd.png)|[前](before-card-long-4k.png) / [後](after-card-long-4k.png)|[前](before-card-long-4by3.png) / [後](after-card-long-4by3.png)|
|長いレシピ結果|[前](before-recipe-long-fhd.png) / [後](after-recipe-long-fhd.png)|[前](before-recipe-long-4k.png) / [後](after-recipe-long-4k.png)|[前](before-recipe-long-4by3.png) / [後](after-recipe-long-4by3.png)|
|レシピ全文|[前](before-recipe-full-fhd.png) / [後](after-recipe-full-fhd.png)|[前](before-recipe-full-4k.png) / [後](after-recipe-full-4k.png)|[前](before-recipe-full-4by3.png) / [後](after-recipe-full-4by3.png)|
|長いカード名|[前](before-long-name-fhd.png) / [後](after-long-name-fhd.png)|[前](before-long-name-4k.png) / [後](after-long-name-4k.png)|[前](before-long-name-4by3.png) / [後](after-long-name-4by3.png)|

## 途中の検証を最終成功と混ぜない

初稿の関連382件は377成功・5失敗。5件はすべて、本文が完全に収まる場合にもnormalizedPosition=0を要求する旧テストだった。実測の長名は511文字・必要高294・内容314・viewport313.999939で20の余白を確保し、Unityの戻り値は1だった。該当2familyだけ、overflow時は従来の末尾0を厳守し、fits時は本文高とcontent全体のviewport内包を確認するよう変更。全文・旧callback拒否・非保存のassertは残した。独立コードレビューでも退行を隠す緩和ではないと確認した。

比較fixtureの最初の末尾測定では、解像度をSmallへ戻した直後にEditorが停止しており、1517/1589文字だった。通常の追加2frames後は1589/1589へ戻った。恒常的な製品欠落とは扱わず、helperを描画更新後に実ScrollRectのPointerEvent scrollHandlerへスクロールを送り、末尾を測る手順へ直した。期待値は下げていない。

## 未確認・別件

物理機器／OS／実機build、全Unicodeや全説明長は未確認。歴史的UGUI例外は未解決のまま。UnityAI接続情報保存read-only警告は製品不具合と分ける。D05の未登録性能・工場拡張・拒否済み本体素材は別残件。今回の結果は全画面の美的完成や無欠陥保証ではない。

## 実操作・レビュー

[実操作と保存確認](actual-result.json)：各比率の描画更新後、実ScrollRectのPointerEvent scrollHandlerにスクロールを送り末尾へ到達。長文・長名・空説明を閉じて開き直し、長文の先頭復帰とカテゴリ往復を確認した。fixture終了後は通常の図鑑へ戻して操作し、progress/checkpoint6ファイルのhash不変を確認。次候補用に既存の「カスタムを隠す」表示も開閉し、論理frame・手札・選択・保存を変えずに戻した。機器はnative Mouse／Keyboardのみ、EventSystem enabled、専用TMPDIRと640×360へ復元した。

[カード説明末尾 FHD](after-card-long-end-fhd.png) / [4K](after-card-long-end-4k.png) / [4:3](after-card-long-end-4by3.png)

[レシピ全文末尾 FHD](after-recipe-full-end-fhd.png) / [4K](after-recipe-full-end-4k.png) / [4:3](after-recipe-full-end-4by3.png)

[初稿の背面透け](iteration1-ghost-card-short-4by3.png)と[最終の遮蔽](after-card-short-4by3.png)を分ける。[独立コードレビュー](code-review.json)は最終2C#差分に具体的未解決0。[独立原寸レビュー](visual-review.json)は短文card3比率、短文recipe4:3、長文card末尾4:3、recipe全文末尾4Kの6枚で必須残0。静止画評価で動的挙動・全機器を保証しない。

[最終関連459件](related-tests.json)は全成功・エラー／警告0。[初稿の5件](initial-failures.json)と[fits実測](fit-measure.json)も残す。最終成功に途中の失敗や別版の成功を混ぜない。

## 次の大きな見直し候補

[戦場全景 FHD](after-next-battle-composition-fhd.png) / [4K](after-next-battle-composition-4k.png) / [4:3](after-next-battle-composition-4by3.png)

通常の接敵から既存の「カスタムを隠す」を使い、論理frameを止めた戦場の表示を確認した。動的な実戦検証の証拠ではなく、今回ここへ表示修正は行っていない。敵名・HPが敵本体より大きく上に離れ、右下タッチHUDと本体の下側も競合するため、次はバトル全体の情報とキャラクターの対応をまとまりとして見直す候補。spriteの可視領域／既存HUD基準点を照合し、必要なら既存素材から表示用基準位置データを整える。キャラやカメラのscale・戦闘性能を変える提案ではない。短文の数px調整だけを続けない。

## 最終全件・後片付け

[全3510件](full-tests.json)は全成功（失敗／skip0、248.22秒）。[実RenderTexture／Screen640×360・専用TMPDIR・明示recompile](preflight.json)を確認して一度だけ起動し、呼出側の180秒期限後も同じtest_statusを追跡。最終ソースhashは開始・終了・レビューのものが一致。

最終consoleエラー0・[警告59件](warning-classification.json)は既存fixture58件とプール上限境界1件。抑制していない。Play現在consoleはエラー0・UnityAI接続情報保存read-only警告1で、MCP履歴に残る別Playやfixtureの件数と混ぜない。

[後片付け](cleanup.json)で元プロジェクトclean、validation／submissionのtracked Assets一致（既存の私用URP2ファイルのみ除外）、controller inactive・実640×360・TMP／save guardと既定ゲーム保存先未生成を確認。VM／Editor／モデル／認証は置換していない。

最終Playの準備では待機枠終了時にPipeline接続が戻り、ゲーム初期化はまだ呼ばれていなかった。status・同じPID・準備済みguardを確認し、未実施の初期化からだけ再開。期限超過した初期化はSnapshotで処理済みを確認し、重複起動やEditor再作成をしない。初回の解像度要求が期限を越えた場合も、読取でGameView/PlayModeViewの実RenderTextureとScreenが640×360であることを確認した後にテストを開始した。
