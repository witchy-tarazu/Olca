---
name: olca-review
description: Olcaプロジェクトのコードレビューを行う。Core層のUnity非依存、Coroutine禁止、レイヤー分離、テスト不足、命名規則、名前空間競合、直近変更のリスク確認が必要なときに使う。
---

# Olca Review

Olcaプロジェクトの変更をアーキテクチャ準拠の観点でレビューする。

## レビュー対象

- ユーザーが指定したファイル、または直近の変更ファイル。
- 対象が不明な場合は `git diff --name-only` 相当の変更範囲を確認する。ただしGitのsafe.directory問題が出る場合は通常のファイル調査へ切り替える。

## チェックリスト

### Core層の純粋性

- `Assets/Olca/Core/` 配下に `using UnityEngine` がない。
- `MonoBehaviour`、`Transform`、`GameObject`、`Vector2/3`、`SerializeField` などUnity依存がない。
- Unity機能はインターフェース越しに扱う。

### Coroutine禁止

- `IEnumerator`、`yield return`、`StartCoroutine` がない。
- 必要ならUniTaskまたはasync/awaitへの置き換えを提案する。

### レイヤー分離

- Core層がUI更新や表示処理を呼ばない。
- Presentation層にビジネスロジックを置かない。
- Application層はロジックとViewの仲介に留める。

### テスト

- 新規または変更されたCore層クラスに対応するテストが `Assets/Olca/Tests/` にある。
- テストはArrange / Act / Assertが読み取れる。
- テスト名は `メソッド名_条件_期待結果` を基本にする。

### 命名とコメント

- クラス名、メソッド名はPascalCase。
- privateフィールド、ローカル変数はcamelCase。
- インターフェース名は `I` で始める。
- 複雑なロジックには日本語コメントを置く。
- `Olca.Tests.Card` のような型名と競合する名前空間を避ける。

## 出力形式

レビューでは問題点を先に出す。重要度順に、ファイルパスと行番号を含める。

```text
## レビュー結果: [対象]

### ❌ 違反（要修正）
- ...

### ⚠️ 警告
- ...

### ✅ 問題なし
- ...

### 修正提案
- ...
```
