# Tests Layer

ユニットテスト用のコードです。

## 構成

- **Battle/**: バトルシステムのテスト
- **Card/**: カードシステムのテスト
- **Mocks/**: テスト用のモッククラス

## テスト実施方法

Unity Test Runnerを使用してテストを実行します。

```
Window > General > Test Runner
```

## モッククラス

テスト用に以下のモッククラスを用意しています:

- MockRandomProvider: 固定値を返す乱数
- MockTimeProvider: 制御可能な時間
- MockInputProvider: テスト用の入力
- MockLogger: ログを記録するロガー
