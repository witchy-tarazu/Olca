# テストコード作成ルール

## 基本方針

- Core層のロジックは**100%テスト可能**であること（NUnit使用）
- Presentation層はテスト対象外（ロジックを含めないため）
- テスト実行はAIがUnityMCP経由で行う（`dotnet test`は使用しない）

## テスト実行タイミング

**以下のタイミングで必ずテストを実行すること:**
- 実装完了後
- コンパイルエラー修正後

実行方法: `mcp__UnityMCP__Unity_RunCommand` でTestRunner APIを呼び出す:
```csharp
using UnityEditor.TestTools.TestRunner.Api;
var api = ScriptableObject.CreateInstance<TestRunnerApi>();
var filter = new Filter { testMode = TestMode.EditMode };
api.Execute(new ExecutionSettings(filter));
```

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

`Tests/Mocks/`配下のモッククラスを使用する:

```csharp
// 乱数を固定値にする
var mockRandom = new MockRandomProvider(fixedValue: 3);

// 時間を固定値にする
var mockTime = new MockTimeProvider(deltaTime: 0.1f);

// 入力をモックする
var mockInput = new MockInputProvider();
mockInput.SetKeyDown(GameKey.Attack, true);
```

## 名前空間

型名との競合を避ける命名にすること:
- ❌ `namespace Olca.Tests.Card`（Card型と競合）
- ✅ `namespace Olca.Tests.CardTests`
- ✅ `namespace Olca.Tests.Battle`

## アセンブリ

テストコードは`Olca.Tests.asmdef`に属すること。
