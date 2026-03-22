Olcaプロジェクトの**アーキテクチャ準拠レビュー**を実行します。

## レビュー対象
$ARGUMENTS

（引数が空の場合は、直近の変更ファイル全体を対象とする）

## レビューチェックリスト（全項目を必ず確認すること）

### 1. Core層の純粋性チェック（最重要）
`Assets/Olca/Core/` 配下のファイルについて：
- [ ] `UnityEngine` の using が含まれていないか
- [ ] `MonoBehaviour` を継承していないか
- [ ] `Transform`、`GameObject`、`Vector3` 等のUnity型を使っていないか
- [ ] `SerializeField` 等のUnity属性を使っていないか

### 2. Coroutine禁止チェック
- [ ] `IEnumerator` が使われていないか
- [ ] `yield return` が使われていないか
- [ ] `StartCoroutine` が使われていないか
→ 違反があれば UniTask / async-await への置き換えを提案する

### 3. レイヤー分離チェック
- [ ] Core層（ロジック）がUI更新・表示処理を呼び出していないか
- [ ] Presentation層（View）にビジネスロジックが含まれていないか
- [ ] Application層（Controller）が薄く保たれているか

### 4. テストチェック（Core層）
- [ ] 新規または変更された Core層クラスに対応するテストが `Assets/Olca/Tests/` に存在するか
- [ ] テストが「Arrange → Act → Assert」の形式になっているか

### 5. コーディング規約チェック
- [ ] クラス名・メソッド名が PascalCase か
- [ ] private フィールドが camelCase か
- [ ] インターフェース名が `I` で始まるか
- [ ] 日本語コメントが適切に付いているか（ロジックの説明がある場所）

### 6. 名前空間競合チェック
- [ ] `Olca.Tests.Card` のような型名と競合する名前空間になっていないか
- [ ] `Vector2Int` 等で Unity型と独自型が混在していないか

## 出力形式

以下の形式で日本語レポートを作成：

```
## レビュー結果: [対象ファイル名]

### ✅ 問題なし
- （通過した項目）

### ⚠️ 警告
- （改善推奨だが動作に影響しない問題）

### ❌ 違反（要修正）
- （アーキテクチャ違反、修正が必要な問題）

### 修正提案
（❌ 項目に対する具体的な修正コード）
```

---

Task toolを使い、`subagent_type: "general-purpose"` でレビューを実行してください。
