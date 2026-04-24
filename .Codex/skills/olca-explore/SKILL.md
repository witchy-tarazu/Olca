---
name: olca-explore
description: Olcaプロジェクトのコードベースを調査する。既存パターン確認、依存関係把握、テストカバレッジ確認、実装前の影響範囲調査、仕様とコードの対応確認が必要なときに使う。
---

# Olca Explore

Olcaプロジェクトの既存コードと仕様を調査し、実装判断に使える形で報告する。

## プロジェクト構造

```text
Assets/Olca/
├── Core/Logic/      # Unity非依存ゲームロジック
├── Core/Domain/     # ドメインモデル
├── Core/Interfaces/ # Unity固有機能の抽象化
├── Application/     # Controller / UseCase
├── Presentation/    # Unity依存のView
├── Unity/           # Unity APIラッパー
└── Tests/           # NUnit / EditMode tests
```

## 調査方針

- `rg` / `rg --files` を優先して関連ファイルを探す。
- 既存の命名、名前空間、テスト配置、依存方向を確認してから提案する。
- Core層に `UnityEngine`、`MonoBehaviour`、Unity型、Unity属性、Coroutineが混入していないか確認する。
- テストがあるか、変更対象に対して不足しているテストがあるかを明示する。
- 仕様確認が必要な場合は `Planning/バトルシステムについて`、`Planning/ダンジョンについて`、`Planning/ビジュアル・アート/` を使い分ける。

## 出力形式

- 調査対象
- 関連ファイルと役割
- 既存パターン
- テスト状況
- 実装時の注意点
- 未確認事項

ファイルパスとクラス名、メソッド名をできるだけ具体的に含める。
