# カスタム内加工の素材・結果・操作（Round66）

Round53の証跡を照合すると、拠点専用ではなく同じカスタム内加工の素材mini／結果本絵接続・実行取消も対象だった。今回はそこを作り直さず、Round65後の実画面で残る通常選択用見出しと加工captionの重なり、使用順0/5・通常コストの残留、強い青いoverlay、結果より先に続く素材情報を整理する。

加工中は既存の見出しを「ケッカ」「ソザイをえらぶ」「ソザイn/2」へ切り替え、通常使用順・LIFO案内・コスト列を元のactive状態を保管して退避し、加工終了後に復元する。重複captionは描画しない。overlayの矩形は透明にし、Round65の読み取り面へ統一。active／Rect／raycast／callbackは変えない。空の素材1・2枠、選択済みとカーソル候補を区別し、結果本絵は正方形の専用画像を借用する。

本文は成立時に結果性能、不成立時に理由を先に置く。素材名と消費の説明は既存scrollに保持。融合／超越の2枚から結果1枚と、調律素材の消費／対象強化を区別し、一律に2枚消費とはしない。下部は既存実行／取消と次の操作を案内し、計算・消費・結果・command・保存契約を変更しない。

## 原寸比較

|状態|FHD 前／後|4K 前／後|4:3 前／後|
|---|---|---|---|
|素材0|[前](before-empty-fhd.png) / [後](after-empty-fhd.png)|[前](before-empty-4k.png) / [後](after-empty-4k.png)|[前](before-empty-4by3.png) / [後](after-empty-4by3.png)|
|不成立|[前](before-invalid-fhd.png) / [後](after-invalid-fhd.png)|[前](before-invalid-4k.png) / [後](after-invalid-4k.png)|[前](before-invalid-4by3.png) / [後](after-invalid-4by3.png)|
|カーソル候補|[前](before-preview-fhd.png) / [後](after-preview-fhd.png)|[前](before-preview-4k.png) / [後](after-preview-4k.png)|[前](before-preview-4by3.png) / [後](after-preview-4by3.png)|
|2枚選択・未実行|[前](before-ready-fhd.png) / [後](after-ready-fhd.png)|[前](before-ready-4k.png) / [後](after-ready-4k.png)|[前](before-ready-4by3.png) / [後](after-ready-4by3.png)|

前後は同じ旧10枚の隔離保存を複製し、実Continue→初期カスタム→仮想pad North→実Raycast選択で表示したGameView全体。物理機器の検証とは別。初期5枚の規則、旧10枚互換、手札5×2／使用5枚上限／LIFO、左半分と正方形、元背景／JikiTourai／本体scale、Round60の4修正、固定frameは維持。

今回の素材と結果は既存の専用mini／本絵が登録されており、新しいraster生成を要しない。空素材・不成立の画像なしは未選択状態であり、mini拡大や別絵で埋めない。過去拒否されたキャラ・剣生成の再試行は行わない。

## レビュー・限界

独立コードレビューで重複した「消費」辞書キーをテスト前に検出・削除した。既存キーを使い、「強化」の読みだけを追加した。レビュー指摘0やテストは全体の美的完成の保証ではない。歴史的UGUI例外は原因未確定・未解決。実機build／物理タッチ・pad／IMEは未確認。


初回候補の原寸批評では、4:3の取消文が枠へ接することと、消費説明が結果詳細の下へ埋もれることを確認した。同じButtonの文を「取消して戻る」へ短縮し、既存の結果名の直後へ消費説明を移した。詳細本文・素材名を削除せず、最初の改行がない詳細も全文保持する。

既存レシピのボム＋ソードは `OlcaCardArt_fire_sword` へ解決し、素材miniは個別アイコンのまま。新規本絵の生成やminiの拡大代用ではない。


## 実初期5枚で見つかったデータ境界

追加の新規出撃では初期5枚を維持。ボム2枚の超越は `CustomScreenSystem.PreviewCustomization` → `ExceedSystem.HasResultDefinition` → `SimpleCardFactory.HasExceededDefinition` の現行定義判定で上位性能未登録となった。実行可と仮定した私有harnessのassertは失敗したが、Unity側は「未定義」の理由・disabledを表示し、手札5／Undo0／frame0を維持していた。製品不具合・全件テスト失敗とは区別する。値やガードを変更せず、disabledへのクリック／古いボタン再送で無変更、取消後の元カードhash完全一致、通常選択→LIFO→詳細から戻る経路まで確認した。[製品データ欄D05](../../../Planning/未決仕様引継ぎ.md)へ根拠を集約し、未決mechanicsと混同しない。

追加の原寸画像：[FHD](after-normal-five-undefined-fhd.png)／[4K](after-normal-five-undefined-4k.png)／[4:3](after-normal-five-undefined-4by3.png)。新規5枚の超越成功や調律の製品経路確認とは報告しない。調律の表示分岐は既存判定から説明を変えるだけで、今回の実製品データによる成功Playは未確認。

不成立画像のボム＋シールドは、現在の通常融合の組合せ表が未登録という既存理由を表示している。新しく融合禁止の仕様を決めたものではない。結果シリーズや性能を画像の都合で補完せず、D05の製品登録へ区別して記録した。

## 初回全件の失敗と追跡

初回全件は3506成功／1失敗（252.06秒）、console error0／warning59。`CustomizeMaterialBoundaryTests.加工プレビューは本絵と専用miniを分け借用し素材とUndoを変更しない` の空状態が「MaterialIconコンポーネントなし」を期待していた。今回の空2枠は、非描画のIconコンポーネントを持つactive枠として意図的に表示するため、その存在だけを描画判定にする旧assertと矛盾した。

製品コードを戻したり診断を抑制せず、空2枠・2Iconの存在、Icon.enabled=false／sprite=null／raycastTarget=false、実行不可を明示する検証へ更新した。素材不変・Undo0・本絵とminiの別参照・借用元Sprite生存・退出時非表示の既存assertは維持。独立コード再レビューも、期待の削除や失敗の隠蔽ではないことを確認した。関連対象へ加工境界suiteを追加し、最終sourceの再コンパイルとテストを実施する。初回失敗は[first-full-failure.json](first-full-failure.json)へ残す。


## 最終検証・証跡

関連 **508件成功**（380＋98＋9＋加工境界21）、最終全 **3507件成功**（243.06秒、failure／skip0）、compilation／console error0。初回全件の失敗後はテストの空枠契約だけを修正し、表示コード・画像・消費計算は変更していない。最終sourceで実640×360／専用TMPDIR・save guardを再確認し、全件を一度だけ起動、呼出側180秒期限後も同じ実行を追跡した。

warning59は報酬View未設定fixture50、設定読込失敗fixture4、誤フェーズ2、使用可能カードなし2、プール上限1。Play現在groundTruth warning1はUnity AI接続情報のread-only保存で、保持ログには同警告2回とPipeline既停止通知1回。母集団を分けて分類し、診断は抑制していない。

原本clean、追跡Assetsは検証専用URP2件を除きsubmission一致、実640×360／inactive controller／専用TMPDIR・save guardへ復元。全件終了後に残った非native・空descriptionの仮想Gamepad1台だけを除去し、native Mouse／Keyboardのみ、error0を確認。元VM・Editor・認証を変更していない。

- [コードレビュー](code-review.md)／[原寸視覚レビュー・履歴と確認範囲](visual-review.md)
- [実操作](actual-operations.json)／[保存不変・Undoの元カードhash](save-invariance.json)／[新規5枚の未定義境界](five-outcome.json)
- [関連テスト](targeted-tests.json)／[最終全件・最終source](final-test-result.json)／[警告分類](warnings.json)／[Play警告](play-warnings.json)
- [後片付け](cleanup.json)／[一時入力除去](input-cleanup.json)

現工場の上位登録はソード専用でもあり、D05の製品値だけでなく、別カードへ広げる登録接続も後続に残る。今回のUI単位で汎用登録済みとは扱わない。通常5枚の超越成功・調律成功の製品Play、物理入力・実機buildは未確認。歴史的UGUI例外は未解決。次の具体的な表示候補は、加工footerの固定「Enter」と割当／入力デバイス別案内の整合。
