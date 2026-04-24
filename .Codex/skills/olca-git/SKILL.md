---
name: olca-git
description: OlcaプロジェクトのGit操作を安全に行う。差分確認、状態確認、コミット準備、コミットメッセージ作成、変更ファイル整理、push前確認が必要なときに使う。
---

# Olca Git

OlcaプロジェクトのGit操作を、安全ルールを守って実行または補助する。

## 基本確認

- `git status --short`
- `git diff --stat`
- `git diff -- <path>`
- `git log --oneline -5`

Gitのdubious ownershipが出る場合は、勝手にglobal設定を変更しない。必要ならユーザーに確認する。

## 安全ルール

- `.meta` ファイルはユーザーが明示しない限りステージしない。
- `.env`、認証情報、個人設定を含むファイルはコミットしない。
- `force push` は実行しない。
- `git reset --hard` は実行しない。
- pushはユーザーが明示的に依頼した場合だけ実行する。
- ユーザーや他ツールの未コミット変更を巻き戻さない。

## コミットメッセージ

Codexで作成する場合はClaude署名を使わない。

```text
<種別>: <日本語の概要>

## 実装内容
- 変更点1
- 変更点2

Generated with Codex
```

種別は `feat`、`fix`、`refactor`、`test`、`docs` を基本にする。

## 報告形式

- 実行したGit操作
- 変更概要
- ステージ・コミット・pushの有無
- 注意点
