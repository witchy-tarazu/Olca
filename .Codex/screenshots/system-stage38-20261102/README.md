# 明示カード定義の六特性

斬撃/衝撃/風圧の旧bit1/2/4を保ち、射撃8・回復16・支援32をCardMaster/Cardへ明示登録できる。追加3種は分類のみで共通効果なし。複数指定を保存/復元/定義hash検証し、未知64以上/負値や単一解除弱点の不正指定を拒否する。旧製品18定義のhash/無特性と旧0〜7定義hash/輸送データを変更前と照合し不変。既存未指定カードへの自動タグ、攻撃/相性変更、八属性/Wind属性移行は行わない。

追加3種の専用画像は未登録のため、意味の異なる画像を使わず、折返し可能な「トクセイ」名称を共通詳細本文に表示する。旧3画像とleaseは維持し、空の画像枠を作らない。Custom詳細と図鑑/読取詳細で同じBind経路を使う。画像生成/キャラクター素材の変更はない。

通常Bootstrap→拠点「カード図鑑」→ページ移動→詳細→戻る→再表示を実EventSystem先頭Raycast一致で確認。明示CardMaster7定義を隔離して準備し、既存PortableCardDefinition経由でファイル保存/再読込/hash検証→既存catalogへ登録→図鑑の既知データとして表示した。検証用定義/所持の種付けはfixtureであり、製品カードを新規追加したものでも製品の取得/報酬transactionでもない。5種類の詳細閲覧・戻る・再表示でprofile/所持/Dungeon/外界Core不変。各ループ内の保存hash比較も通過したが、最初のcoordinatorは図鑑→拠点で変わるfocus/texts/buttonsまで一致を要求して停止し、当時のhash辞書をreceiptへ書く前だった。この誤ったGUI条件を分離し、改めて保存hashを記録した拠点→図鑑→6分類詳細→旧callback拒否→戻る→拠点でbytes不変を確認した。[実操作と境界](native-product-paths.json)。

|明示分類fixture|FHD|4K|4:3|
|---|---|---|---|
|旧斬撃画像|[画像](legacy-slash-fhd.png)|[画像](legacy-slash-4k.png)|[画像](legacy-slash-4by3.png)|
|射撃|[画像](shooting-fhd.png)|[画像](shooting-4k.png)|[画像](shooting-4by3.png)|
|回復|[画像](healing-fhd.png)|[画像](healing-4k.png)|[画像](healing-4by3.png)|
|支援|[画像](support-fhd.png)|[画像](support-4k.png)|[画像](support-4by3.png)|
|六分類複合|[画像](six-classifications-fhd.png)|[画像](six-classifications-4k.png)|[画像](six-classifications-4by3.png)|

初回ヘルパーのファイル指定ミス（登録処理未実行で停止）、再読込/Pipeline待機、環境再接続による待受stdout切断と既存job回収を記録に保持した。製品コードは最終v1から変えず、Play/全件を重複開始しない。UnityAIの起動時error1/warn3は[起動履歴](native-startup-history.json)へ別記録し、分類後の消去直後に返った古いgroundTruth値と、更新後error0も区別した。

各状態を同論理frame/profileのままCanvas更新後、実Screen/2RT寸法を確認して撮影した最終版15枚。新分類は旧版が拒否するため、存在しなかった旧画面のbefore画像とは扱わない。旧表示は旧フラグの保存/画像不変回帰と今回の旧斬撃画面で区別。[原寸視覚レビュー](visual-review.json) / [画像hash](image-manifest.json)。

最終5sourceで関連11件と全3723件成功（全件291.72秒、失敗/skip/inconclusive0）。実640×360/2RTと専用TMPDIR/saveguard、新規RunStarted/RunFinishedを確認。console error0、警告は[分類](warning-classification.json)し抑制なし。[全件](full-tests.json) / [関連](related-tests.json) / [独立コード](independent-review.json) / [cleanup](cleanup.json)。

今回のnative画像は図鑑の読取詳細で、Customの新6分類nativeや極端な長文を今回撮影したものではない（共通Bind/既存全件回帰で確認）。六分類名は今回幅では1行に収まるため、実際の折返し発生はこの15枚で実証していない。

S09/S10/S11、D05/未決性能/工場登録拡張、八属性/Wind属性移行は別。実build/物理入力/聴感/全HotReload/製品NPCや既存製品データ等の未確認、歴史的UGUI/Pipeline/CLI/UnityAIと登録外Sprite2095は解決済み扱いにしない。[範囲記録](../../../Planning/外界探索入口の実装段階.md)。
