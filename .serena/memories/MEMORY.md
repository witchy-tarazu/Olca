# Olca プロジェクト メモリ

## プロジェクト概要
- **名称**: Olca（オルカ）
- **種別**: カードアクションRPG（ローグライク×アクション×デッキ構築）
- **エンジン**: Unity 6000.0.60f1（Unity 6）
- **言語**: C# / .NET Standard 2.1

## 詳細情報
- [アーキテクチャ・規約](architecture.md)
- [コーディング規約](conventions.md)
- [開発コマンド](suggested_commands.md)

## 重要な注意事項
- **Coroutine使用禁止**（UniTask/async-awaitを使用）
- **Core層にUnity依存コード禁止**
- **コメントは日本語**、変数名・クラス名は英語
- テスト実行・.metaファイル生成・Unity操作はユーザーが担当
- コンパイルエラー確認はMCP（`mcp__UnityMCP__read_console`）を使う

## Serena言語サーバー
- 初期化エラーが発生することがある（`get_symbols_overview`等が失敗する場合はRead/Grep系ツールで代替）
