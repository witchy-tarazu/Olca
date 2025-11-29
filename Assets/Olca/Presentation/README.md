# Presentation Layer

Unity依存のView層です。

## 構成

- **Battle/**: バトル画面のView
- **Dungeon/**: ダンジョン探索のView
- **UI/**: UIコンポーネント
- **Effects/**: エフェクト

## 実装ガイドライン

- MonoBehaviourを使用可能
- ロジックは含めない（表示のみ）
- ロジック層から状態を取得して表示に反映
