# テストコード作成ルール

## 基本方針

- Core層のロジックはNUnitでテスト可能にする。
- Presentation層はロジックを含めず、原則としてテスト対象外にする。
- テスト実行はUnityMCP経由で行い、`dotnet test` は使用しない。

## テスト実行タイミング

以下のタイミングで必ずテストを実行する。

- 実装完了後
- コンパイルエラー修正後

CodexではUnityMCPの `run_tests` を使い、EditModeテストを実行する。

## 命名規則

```csharp
// クラス名: テスト対象クラス名 + "Tests"
[TestFixture]
public class BattleSystemTests { ... }

// メソッド名: メソッド名_条件_期待結果（日本語）
[Test]
public void TakeDamage_HPが0以下になったら_GameOverになる() { ... }
```

## AAA パターン

```csharp
[Test]
public void TakeDamage_HPが0以下になったら_GameOverになる()
{
    // Arrange（準備）
    var player = new PlayerCharacter(maxHp: 100);
    var battleSystem = new BattleSystem(player);

    // Act（実行）
    battleSystem.TakeDamage(150);

    // Assert（検証）
    Assert.IsTrue(battleSystem.IsGameOver);
    Assert.AreEqual(0, player.CurrentHp);
}
```

## モックの使い方

`Tests/Mocks/` 配下のモッククラスを使用する。

```csharp
var mockRandom = new MockRandomProvider(fixedValue: 3);
var mockTime = new MockTimeProvider(deltaTime: 0.1f);
var mockInput = new MockInputProvider();
mockInput.SetKeyDown(GameKey.Attack, true);
```

## 名前空間

型名との競合を避ける。

- 避ける: `namespace Olca.Tests.Card`
- 推奨: `namespace Olca.Tests.CardTests`
- 推奨: `namespace Olca.Tests.Battle`

## アセンブリ

テストコードは `Olca.Tests.asmdef` に属すること。
