# 開発コマンド・ワークフロー

## テスト実行
- **Unity Test Runner**を使用: `Window > General > Test Runner`
- `dotnet test`コマンドはAIが実行しない（ユーザーが担当）

## コンパイルエラー確認（AIの作業）
```
mcp__UnityMCP__read_console → エラーログ確認
```

## ファイル操作（AIの作業）
- C#ファイル作成: `mcp__UnityMCP__create_script` または `Write`ツール
- `.meta`ファイル生成: **ユーザーがUnityエディタにフォーカスすると自動生成**

## git操作
- プラットフォーム: Windows / bash
- 通常のgitコマンドを使用

## Unity操作（ユーザーが担当）
- `.meta`ファイル生成
- テスト実行・結果確認
- Unity Editorでの動作確認
- ビルド・実機確認

## 新機能実装フロー
1. `Planning/`配下の仕様書を確認
2. **Core層**でロジック実装（Unity非依存）
3. **ユニットテスト**を作成
4. **Application層**でController実装
5. **Presentation層**でView実装
6. Unity Editorで動作確認（ユーザー）

## 仕様書の場所
- ゲームロジック: `Planning/バトルシステムについて`, `Planning/ダンジョンについて`
- ビジュアル/UI: `Planning/ビジュアル・アート/`
