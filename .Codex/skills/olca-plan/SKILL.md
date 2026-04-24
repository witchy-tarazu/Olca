---
name: olca-plan
description: Olcaプロジェクトの実装計画を立案する。新機能追加、リファクタリング、システム設計、コードを書く前の設計整理、影響範囲調査、テスト設計が必要なときに使う。
---

# Olca Plan

Olcaプロジェクト（Unity + C# カードアクションRPG）の実装計画を作る。

## 前提

- 会話、報告、コメントは日本語を基本にする。
- 実装前に `Planning/` 配下の関連仕様を確認する。
- Core層はUnity非依存にする。
- Coroutine（`IEnumerator`、`yield return`、`StartCoroutine`）は禁止し、UniTaskまたはasync/awaitを使う。
- ロジックとViewを分離する。Core層からUI更新やUnity APIを呼ばない。
- 依存性注入はコンストラクタインジェクションを優先する。
- Unity固有機能は `IRandomProvider`、`ITimeProvider`、`IInputProvider`、`IGameLogger` などのインターフェース経由にする。

## 計画手順

1. 関連する仕様書と既存実装を確認する。
2. Core / Application / Presentation / Unity / Tests のどこに変更が必要か分ける。
3. Core層のモデル、サービス、値オブジェクト、インターフェースを先に設計する。
4. Core層のNUnitテストを設計する。
5. Application層のControllerやUseCaseを設計する。
6. Presentation層のViewとUnity層のラッパーを設計する。
7. UnityMCPでのコンソール確認とEditModeテスト実行まで計画に入れる。

## 出力形式

以下を日本語で簡潔にまとめる。

- 影響ファイル一覧: 新規作成、修正、テストを分ける。
- クラス設計: 責務、主要メソッド、必要なら短いシグネチャ例を示す。
- テスト設計: Core層中心にテストケース名と検証内容を示す。
- 実装順序: Core、Tests、Application、Presentation、Unity確認の順にする。
- 注意点: アーキテクチャ違反、Unity依存、名前空間競合、metaファイル非編集など。
