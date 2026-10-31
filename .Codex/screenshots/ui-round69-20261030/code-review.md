# 独立コードレビュー

既存equipment_save_reviewが4ファイルの最終差分を読み取り確認。今回範囲で具体的な未解決指摘0。MCP/編集/テスト起動は行っていない。

- CoreのIsKnown/RecipeRowsを公開根拠にし、同名hashと旧hash欠損の一意判定、配列index対応を維持。
- 未入手カードはmini/性能入口なし。未公開レシピはResult自体null。公開結果の絵とカード本体の入手登録を混同しない。
- 既知Cardだけminiを借用、既存ForCard resolverを利用。別定義の取得や所有権変更なし。
- CardTextは既知判定後の表示整形だけ。★段階/rarity/実効cost/威力/説明を保持し、登録/計算/hash/saveを変更しない。
- 面材とカテゴリ下線は非入力。callback/ページ/readonly所有寿命/focus/pending/retryを維持。
- 図鑑限定readonlyはheader下端145→読み面上端160→本文上端180。左右gutter80、本文8%、背景高viewport+40、下余白20。戻る位置と他readonlyは変更しない。
- 本文枠500に旧fixtureが収まった6失敗は実測492を確認。性能fixtureだけ220へ増やし、実overflowをassert。全文一致・readonly・旧操作拒否・focus・短文入口なしの確認は緩和していない。
- private fixtureが裏のRecipePerformanceを測った箇所は実readonly優先へ訂正し、1429/1429文字を確認。製品修正と区別。
