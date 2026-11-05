# 拠点の探索デッキ編成・独立保存

通常Bootstrap拠点の「タンサク デッキ」から、永続所持カードを消費せず20枚・共通構築制限で編成し、探索用に複数保存・編集・取消・削除・再読込する製品経路を接続した。既存の編成表示・専用mini/詳細・名前16文字/表示幅・共通ConfirmationDialogとViewManagerの寿命を再利用。元背景/JikiTourai/キャラ比率・60Hz/既存Dungeon計算は変更しない。

保存は `progress/exploration-decks/{generation または legacy}/decks.json` のVersion1。所持・Dungeonデッキ・checkpointを保存DTOへ複製しない。未作成の旧保存は空の一覧を無書込で開く。未知ID/定義キーを消さず、未解決/所持超過/20枚外を新規保存しない。atomic replace/backup・revision競合検出・書換後エラーの同一bytes判定と同一計画再試行を使う。[設計](../../../Planning/外界探索入口の実装段階.md)。

[製品入口の実操作](native-product-paths.json)：通常起動が作るスターター20枚を使い、デッキ直接登録や所持配布fixtureで代替していない。新規取消、複数作成、保存/削除の失敗→再試行、破棄/削除取消focus、詳細、再読込、同frame旧Chrome拒否、自然Destroy再生成、host/Single再入を確認。通常編集区間は旧保存bytes・所持・Dungeon/外界Coreが不変。host Suspendは従来の別保存境界として分離。ボタンは実EventSystem Raycast、名前はInputField通知、拠点のpad Pause除外規則は既存delegateで確認し、未対応の入力を新設していない。物理入力/IMEの成功とは扱わない。

保存した探索デッキの装備先指定・探索手札への登録/切替/再装填・使用・S09は今回未接続。新しい製品地形や外界戦闘の完成を意味しない。初期の静的指摘（閉鎖frameと入口配置）はv5で解消し、v2の予備15成功やv3 compileを最終結果へ流用していない。[履歴/限界](history-and-limits.json)。

15枚は各状態の同論理frame・保存/所持/編成状態を固定し、Canvas更新と実RT寸法を確認して取得。[manifest](image-manifest.json)。旧版と同状態のbefore/after撮影を行ったと装わず、新機能の実状態を示す。

|状態|FHD|4K|4:3|
|---|---|---|---|
|base|[画像](base-fhd.png)|[画像](base-4k.png)|[画像](base-4by3.png)|
|empty-list|[画像](empty-list-fhd.png)|[画像](empty-list-4k.png)|[画像](empty-list-4by3.png)|
|new-editor|[画像](new-editor-fhd.png)|[画像](new-editor-4k.png)|[画像](new-editor-4by3.png)|
|editor|[画像](editor-fhd.png)|[画像](editor-4k.png)|[画像](editor-4by3.png)|
|saved-list|[画像](saved-list-fhd.png)|[画像](saved-list-4k.png)|[画像](saved-list-4by3.png)|

最終v5で保存回帰15/Application556/Integration620、同Editor反復160、全3719件成功（326.33秒）。実640×360/2RT・専用TMPDIR/saveguard、8source hash一致。[全件](full-tests.json) / [保存回帰](new-save-tests.json) / [反復](same-editor-regression.json) / [コードレビュー](independent-code-review.md) / [原寸視覚レビュー](visual-review.json)。console error0、警告は[分類](warning-classification.json)し抑制なし。[終了/資源/入力/保存cleanup](cleanup.json)。

実build・物理入力/聴感・全HotReload・製品stat/Itemskip/NPC/極端な旧長名、歴史的UGUI/Pipeline/CLI/UnityAI/登録外Sprite2095は独立した未確認として保持する。
