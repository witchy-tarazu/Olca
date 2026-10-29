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

## Unity MCP の実行条件と期限超過

- テスト開始直前に、実際のGameView／RenderTextureが640×360であることを確認する。サイズ変更要求を送っただけでは確認済みにしない。
- 検証Editorの再起動後は既存の専用一時保存先も復元し、`Path.GetTempPath()`が保存先ガード内であることを確認する。ガード拒否を期待値変更やガード解除で回避しない。
- FHD／4Kの実画面検証とテスト実行条件を分け、画像取得後に高解像度のまま同期テストを開始しない。
- 長時間の関連テスト・全件テストは`async_tests: true`と十分な`timeout`（全件は600秒）で一度だけ起動し、同じ実行を追跡する。
- 呼出側の期限超過はUnity側の終了を意味しない。実行状態と終了通知を確認するまで、重複起動や不用意な再起動をしない。
- 状態ファイルは今回の実行・条件に対応するか確認する。前回の成功記録や、結果収集を取り消した実行を今回の成功として報告しない。

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
