# Core Layer

Unity非依存のゲームロジック層です。

## 構成

- **Logic/**: ビジネスロジック（バトル、カード、ダンジョン等）
- **Domain/**: ドメインモデル（データ構造）
- **Interfaces/**: インターフェース定義

## 重要な原則

- このレイヤーはUnityに一切依存しません
- MonoBehaviour、Transform、Coroutine等のUnity固有機能は使用禁止
- すべてのロジックは純粋なC#で実装
- 100%ユニットテスト可能
