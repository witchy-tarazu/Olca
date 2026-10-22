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


## Unityなしでの実行

Coreの待機はPlayerLoop非依存のFrameTaskで同じ論理フレーム内に継続する。基本攻撃入力もCoreへ移した。最小Step入口と同一ソース.NET CLIの手順は [Tools/BattleSim](../../../Tools/BattleSim/README.md) を参照。portableビルドではCardMasterの外部DB注釈だけを外し、型やゲームロジックのスタブは作らない。最小シナリオ以外の製品データ/入力/保存復帰の網羅は未完了。
