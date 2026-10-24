# Olca

**Olca（オルカ）** は「ローグライク×アクション×デッキ構築」を組み合わせたカードアクションRPGです。

## 特徴

- **リアルタイムバトル**: 6x1グリッド上での高速戦闘
- **カスタム画面**: カスタムゲージが満タンになると手札から戦術を組み立て
- **ダンジョン探索**: 自動スクロールアクション（ジャンプ・ダッシュ）
- **デッキ構築**: 戦闘中にカードを獲得してデッキを強化

## 技術スタック

- **Unity**: 6000.3.26f1 (Unity 6、changeset `de8167e7440c`。現在の検証済みバージョン)
- **C#**: .NET Standard 2.1
- **アーキテクチャ**: Core層（Unity非依存）+ Application層 + Presentation層
- **テスト**: NUnit

## プロジェクト構成

```
Assets/Olca/
├── Core/              # Unity非依存のゲームロジック
│   ├── Domain/       # ドメインモデル（Card, Deck, PlayerCharacter等）
│   ├── Logic/        # ビジネスロジック（BattleSystem, CardEffectSystem等）
│   └── Interfaces/   # インターフェース定義
├── Application/      # ロジックとビューの仲介
├── Presentation/     # Unity依存のView層
├── Unity/            # Unity固有機能のラッパー
└── Tests/            # ユニットテスト

Planning/             # 仕様書・設計ドキュメント
```

## ドキュメント

- [環境移行チェックポイント](Tools/Migration/README.md) - ignore対象のProjectSettings復元、検証条件、移行対象外データ
- [フォント復元後のFHD／4K画像](.Codex/screenshots/font-restore-20261023/README.md)

### 開発者向け
- [CLAUDE.md](CLAUDE.md) - アーキテクチャ原則・コーディング規約
- [Planning/開発状況.md](Planning/開発状況.md) - 現在の開発状況

### 仕様書
- [バトルシステムについて](Planning/バトルシステムについて)
- [ダンジョンについて](Planning/ダンジョンについて)
- [カード効果について](Planning/カード効果について)
- [敵の行動について](Planning/敵の行動について.md)

### 実装ドキュメント
- [前隙・後隙システム設計書](Assets/Olca/Core/Logic/CardEffects/README_STARTUP_RECOVERY.md)
- [チェイン・パネル変化設計書](Assets/Olca/Core/Logic/CardEffects/README_CHAIN_PANEL.md)
- [派生効果設計書](Assets/Olca/Core/Logic/CardEffects/README_DERIVATIVE_RECOVERY.md)
- [マスターデータ設計書](Assets/Olca/Core/Logic/CardEffects/README_MASTER_DATA.md)

## 開発原則

### 1. ロジックと表示の完全分離
ゲームロジックはUIや表示から完全に独立させ、ロジックのみで動作可能にする。

- **Core層**: Unity非依存の純粋C#クラス（100%テスト可能）
- **Application層**: ロジックとビューを繋ぐController
- **Presentation層**: MonoBehaviourを継承したView

### 2. Unity固有機能への依存の最小化
`MonoBehaviour`, `Coroutine`, `UnityEngine.Random` に依存せず、インターフェースで抽象化。

### 3. テスト駆動開発
Core層のロジックはすべてNUnitでテスト。モックを使用してUnity非依存のテストを実現。

## テスト実行

```bash
# Unity Test Runnerで実行
# Window > General > Test Runner
```

## ライセンス

未定
