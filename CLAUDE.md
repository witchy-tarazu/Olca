# CLAUDE.md - Unity + C# ゲーム開発プロジェクト設定

## プロジェクト概要
本プロジェクトは、Unity + C#で開発するカードアクションRPGです。
「ローグライク×アクション×デッキ構築」を組み合わせた独自のゲームシステムを実装します。

## コミュニケーション言語
**日本語でのやり取りを基本とします。**
- チャット上の会話は日本語で行ってください
- コード内のコメントも日本語で記述してください
- 変数名・関数名・クラス名は英語（PascalCase/camelCase）を使用してください

---

## 開発フロー

### 新機能実装時の手順
1. **Planning/配下の仕様書を確認**
2. **Core層でロジックを実装**（Unity非依存）
3. **ユニットテストを作成**
4. **TestRunnerでテスト実行**（UnityMCP経由）
5. **Application層でControllerを実装**
6. **Presentation層でViewを実装**
7. **Unity Editorで動作確認**

### コードレビューチェックリスト
- [ ] Core層にUnity依存コードが含まれていないか
- [ ] ロジックとビューが分離されているか
- [ ] 適切な日本語コメントが付いているか
- [ ] ユニットテストが書かれているか（Core層）
- [ ] SOLID原則に違反していないか

---

## Unity Editor操作とテスト実行のルール

`.meta`ファイルの生成とビルド・実機確認はユーザー側が担当。テスト実行とコンソールログ確認はAIがUnityMCP経由で行う。

### 1. .metaファイルの生成
- AIは`.meta`ファイルの生成を試みない
- C#ファイル新規作成後のmeta生成はユーザーが担当（Unity EditorがAutomatic生成）

### 2. ユニットテストの実行
**AIがUnityMCP経由でTestRunnerを実行する。`dotnet test`コマンドは使用しない。**

テストを実行すべきタイミング:
- 実装完了後
- コンパイルエラー修正後

実行方法: `mcp__UnityMCP__Unity_RunCommand` でTestRunner APIを呼び出す:
```csharp
using UnityEditor.TestTools.TestRunner.Api;
// RunnerApi.Execute() で EditMode テストを実行
```

### 3. コンパイルエラーの確認とテスト実行のワークフロー
**コンパイルエラー修正・実装完了後は以下のフローに従うこと。**

```
【実装・修正時のフロー】
1. mcp__UnityMCP__read_console でコンソールログ確認
2. エラーがあれば該当ファイルを読み込み修正
3. 再度 mcp__UnityMCP__read_console でエラー解消を確認
4. mcp__UnityMCP__Unity_RunCommand でテストを実行
5. テスト結果を確認・報告
```

### 4. AIの役割
- C#コードの作成・編集
- テストコードの作成・編集
- コードレビューとアドバイス
- アーキテクチャ設計の提案
- **MCP経由でのUnityコンソールログ確認**
- **MCP経由でのTestRunner実行・結果確認**

---

## 参考リンク
- [Planning/バトルシステムについて](Planning/バトルシステムについて)
- [Planning/ダンジョンについて](Planning/ダンジョンについて)
- アーキテクチャルール: `.claude/rules/architecture.md`
- テストコード作成ルール: `.claude/rules/testing.md`
- アーキテクチャ詳細: `.serena/memories/architecture.md`
- コーディング規約詳細: `.serena/memories/conventions.md`
