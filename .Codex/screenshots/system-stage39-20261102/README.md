# 八属性の明示登録と七属性耐性

旧enum0〜4（Wind=3）と既存18製品の定義hashを保ち、Metal5/Wood6/Yin7/Yang8を追加した。新規の`Card.FromCurrentMaster`は現在の八属性だけを許可し、既存の保存復元経路は旧Windもそのまま読む。未知属性や上書き値は拒否し、無属性へ表示を落とさない。属性と風圧特性の自動付与はない。

七耐性は既存四欄（火/水/旧風/土）を維持し、追加四欄（金/木/陰/陽）を別領域で保存する。敵portableは旧4欄または新8欄のみ。プレイヤー基礎値と装備sourceは独立して保存・deep-copyし、元値合算後に一度制限する。旧payloadのchecksumは元bytesで検証し、読込時に保存を書き換えない。新形式を旧buildが読める保証とは区別する。

Bootstrapの既存製品図鑑入口から、隔離保存先へ明示登録した4属性カードの詳細・戻る・再表示・旧callback拒否を実EventSystem先頭Raycastで確認した。カードの値・所持登録はfixtureであり、製品カードへの新属性割当や新しい取得経路ではない。旧5カードbytes/hash・旧敵bytes・既存18製品hashも変更前実測と一致。独立した実FileCheckpointStoreで新七耐性＋装備sourceを保存・3往復復元し、読込前後のファイルbytes不変と本体Session不変を確認した。[製品入口と境界](native-product-paths.json) / [実ファイル](seven-resistance-files.json)。

通常出撃→通常接触→初手5枚Customから自己詳細を実操作で開き、七耐性とキュウカゼ互換欄を表示した。値は隔離fixtureで、製品性能ではない。閉鎖focus・再表示・手札/選択/cost/frame・保存bytes不変を確認し、元の基礎耐性を復元した。[自己詳細](native-status.json)。

|明示データ|FHD|4K|4:3|
|---|---|---|---|
|金|[画像](metal-fhd.png)|[画像](metal-4k.png)|[画像](metal-4by3.png)|
|木|[画像](wood-fhd.png)|[画像](wood-4k.png)|[画像](wood-4by3.png)|
|陰|[画像](yin-fhd.png)|[画像](yin-4k.png)|[画像](yin-4by3.png)|
|陽|[画像](yang-fhd.png)|[画像](yang-4k.png)|[画像](yang-4by3.png)|
|七耐性と旧風欄|[画像](seven-resistances-fhd.png)|[画像](seven-resistances-4k.png)|[画像](seven-resistances-4by3.png)|

各組は同論理frame・状態、Canvas更新済み・実Screenと2RT寸法確認で取得した。旧版が拒否する新属性fixtureのため、存在しない旧画面をbeforeとして作っていない。画像15枚のSHA・寸法と原寸独立視覚レビュー、最終12sourceの独立コードレビューは必須指摘0。[画像manifest](image-manifest.json) / [レビュー](independent-review.json)。

最終v4で関連22件38.7秒・全3734件287.75秒が成功。失敗/skip/inconclusive0、console error0。実640×360/2RT・専用TMPDIR/saveguard・新RunStarted/Finishedと最終source一致を確認。警告は[分類](warning-classification.json)して抑制していない。[関連](related-tests.json) / [全件](full-tests.json) / [cleanup](cleanup.json)。

自己詳細の初回閉鎖では、Navigation.Noneのポインター操作なのにクリック先focusを期待した検証条件で停止した。開く前は既に無効な出撃ボタンが選択され、既存Managerは有効なPreviousFocusだけを戻すためnullになる。製品コードは変えず、標準EventSystemで有効な選択を用意して同じ実ポインター入口・閉鎖・再表示を再確認した。初回のin-memory保存基準はreceipt前に失われたため、その区間のbytes不変は成功扱いにしない。改めて記録した再確認cycleで保存bytes不変と元耐性復元を確認。[最初の停止](native-status-first-focus-failure.json)。

v1のformatter参照、v2のテストhelper名と型名衝突のcompile失敗を修正。v3はテスト前に未知Modifier上書きの検証不足を発見し、待受Pythonだけを止め、既存描画job完了後にv4へ更新した。v3のテスト成功を流用していない。長いreload・初回helper・Play再接続は同じEditorの既存jobを回収し、Play/テストを重複投入しなかった。最終全件の180秒待受timeoutは同じ実行のtest_statusで回収し、再送しなかった。起動時UnityAI/Pipeline/CLI等は[履歴](native-startup-history.json)に分離する。

今回、属性別の基礎相性の自動導出は未接続。−50/+50・通常敵0・陰陽の規則は決定済みで次の独立範囲。旧Wind定義の対応表と進行中旧checkpoint方針のみS12未決。既存製品の属性/タグ/性能、旧保存の意味は変更していない。S09/S10/S11/D05、製品データ割当、Custom変身固有詳細の新属性native・極端長文、実build/物理入力/聴感/全HotReload/製品NPC等の未確認、歴史的UGUI/Pipeline/CLI/UnityAIと登録外Sprite2095は解決済みにしない。[範囲](../../../Planning/外界探索入口の実装段階.md)。
