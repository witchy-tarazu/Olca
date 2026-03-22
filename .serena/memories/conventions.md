# コーディング規約

## 言語設定
- チャット・コメント: **日本語**
- 変数名・関数名・クラス名: **英語**

## 命名規則
| 種別 | 規則 | 例 |
|------|------|-----|
| クラス名 | PascalCase | `BattleSystem`, `CardDeck` |
| メソッド名 | PascalCase | `DrawCard`, `TakeDamage` |
| privateフィールド | camelCase | `playerHp`, `customGauge` |
| publicプロパティ | PascalCase | `PlayerHp`, `IsGameOver` |
| インターフェース | `I`プレフィックス | `IDeck`, `IRandomProvider` |
| 定数 | UPPER_SNAKE_CASE | `MAX_HAND_SIZE` |

## コメント規約
```csharp
/// <summary>
/// カードをデッキからドローします（日本語XMLコメント）
/// </summary>
/// <param name="count">ドローする枚数</param>
public List<Card> DrawCards(int count) { ... }
```

## コード品質
- null安全性: null許容型（`?`）を適切に使用
- 不変性: 可能な限り`readonly`を使用
- SOLID原則: 特に単一責任・DIP（依存性逆転）を重視

## 禁止事項（Core層）
- `MonoBehaviour`の継承
- `Coroutine`/`IEnumerator`の使用
- `UnityEngine.Random`（`IRandomProvider`を使う）
- `Transform`、`GameObject`への依存
- Unity固有属性（`[SerializeField]`等）

## テストパターン
```csharp
[TestFixture]
public class BattleSystemTests
{
    [Test]
    public void メソッド名_条件_期待結果()
    {
        // Arrange / Act / Assert パターン
    }
}
```

## deltaTime渡し方パターン
```csharp
// Core層
public void Tick(float deltaTime) { ... }

// Application/Presentation層
private void Update() { coreSystem.Tick(Time.deltaTime); }
```

## パフォーマンス指針

### メモリ管理
- **GC回避**: `new`を頻繁に呼ばない。オブジェクトプールを活用
- **struct活用**: 小さいデータ構造は`struct`を検討
- **配列再利用**: `List.Clear()`で再利用

### Update最適化
イベント駆動を優先し、Update内でのポーリングを避ける:
```csharp
// ❌ 悪い例: 毎フレームチェック
private void Update() { if (battleSystem.IsGaugeFull()) OpenCustomScreen(); }

// ✅ 良い例: イベント駆動
private void Start() { battleSystem.OnGaugeFull += OpenCustomScreen; }
```

## デバッグとログ

ILoggerをコンストラクタインジェクションで注入し、Core層でもログ出力を可能にする:
```csharp
// Core層
public class BattleSystem
{
    private readonly ILogger logger;
    public BattleSystem(ILogger logger) { this.logger = logger; }

    public void TakeDamage(int damage)
    {
        logger.LogInfo($"プレイヤーが{damage}ダメージを受けました");
        playerHp -= damage;
    }
}
```

## ScriptableObjectの使い方
- ✅ データ定義（cardName, cost, damage等のフィールド）として使用可能
- ❌ ロジックメソッド（`ExecuteEffect(BattleSystem battle)`等）は含めない
