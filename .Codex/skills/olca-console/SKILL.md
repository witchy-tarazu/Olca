---
name: olca-console
description: OlcaプロジェクトのUnityコンソールエラーや警告を診断・修正する。C#編集後のコンパイル確認、UnityMCPでのコンソール確認、EditModeテスト実行、エラー原因調査が必要なときに使う。
---

# Olca Console

UnityMCPを使ってコンソールエラーとテスト結果を確認し、必要なら修正する。

## 必須フロー

C#ファイルを1つでも作成・編集した後は、必ずこの順に実行する。

1. `read_console` で `types: ["error"]` を確認する。
2. エラーがあれば該当ファイルを読み、根本原因を修正する。
3. 再度 `read_console` でエラー解消を確認する。
4. `run_tests` でEditModeテストを実行する。
5. 結果と残存問題を日本語で報告する。

警告やランタイム問題の診断依頼では、`types: ["error", "warning"]`、`count: "50"`、`include_stacktrace: true` を目安に取得する。

## 修正ルール

- Core層にUnity依存を入れない。
- Coroutineは禁止。UniTaskまたはasync/awaitを使う。
- `.meta` ファイルは作成・編集・移動しない。
- `dotnet test` は使わず、UnityMCPのTestRunner経由で確認する。

## 報告形式

- 検出したエラー・警告
- 原因
- 修正内容
- 修正したファイル
- テスト結果
- 残存する問題
