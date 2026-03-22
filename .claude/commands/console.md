Olcaプロジェクトの**Unityコンソールエラーを診断・修正**します。

## 対象
$ARGUMENTS

（引数が空の場合は、現在のコンソール全エラー・警告を対象とする）

## 実行手順（この順序で必ず実行すること）

### Step 1: コンソールログを取得
`mcp__UnityMCP__read_console` ツールで以下を取得：
- `types: ["error", "warning"]`
- `count: "50"`
- `include_stacktrace: true`

### Step 2: エラーの分類と分析
取得したログから：
1. **コンパイルエラー**と**ランタイムエラー**を分類
2. エラーメッセージからファイルパス・行番号を抽出
3. 該当ファイルを読み込み、問題箇所を特定

優先順位：コンパイルエラー → ランタイムエラー → 警告

### Step 3: 修正実行
根本原因を特定してコードを修正する。修正時の注意：
- Core層（`Assets/Olca/Core/`）に UnityEngine 依存を混入させない
- Coroutine（`IEnumerator`, `yield return`, `StartCoroutine`）は禁止。UniTask/async-await に置き換える
- `.meta` ファイルは絶対に編集しない

### Step 4: 修正の検証
修正後、再度 `mcp__UnityMCP__read_console` でログを取得し、エラーが解消されたことを確認する。

## 出力形式

日本語で以下を報告：
1. 検出したエラー・警告の一覧
2. 各エラーの原因と修正内容
3. 修正したファイル一覧
4. 残存する問題（あれば）

---

Task toolを使い、`subagent_type: "general-purpose"` で診断・修正を実行してください。
