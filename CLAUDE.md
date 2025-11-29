# CLAUDE.md - Unity + C# ゲーム開発プロジェクト設定

## プロジェクト概要
本プロジェクトは、Unity + C#で開発するカードアクションRPGです。
「ローグライク×アクション×デッキ構築」を組み合わせた独自のゲームシステムを実装します。

## コミュニケーション言語
**日本語でのやり取りを基本とします。**
- チャット上の会話は日本語で行ってください
- コード内のコメントも日本語で記述してください
- 変数名・関数名・クラス名は英語（PascalCase/camelCase）を使用してください

---

## アーキテクチャ原則

### 1. ゲームロジックと表示の完全分離
**重要:** ゲームロジックはUIや表示から完全に独立させ、ロジックのみで動作可能にすること。

#### 実装指針:
```csharp
// ✅ 良い例: ロジックとビューが分離されている
public class BattleLogic
{
    private int playerHp;
    private List<Card> deck;

    public int GetPlayerHp() => playerHp;
    public void TakeDamage(int damage)
    {
        playerHp -= damage;
        // ロジックのみ。UI更新は呼び出さない
    }
}

public class BattleView : MonoBehaviour
{
    [SerializeField] private Text hpText;
    private BattleLogic logic;

    private void Update()
    {
        // ロジックから状態を取得して表示
        hpText.text = $"HP: {logic.GetPlayerHp()}";
    }
}

// ❌ 悪い例: ロジックがUIに依存している
public class BattleManager : MonoBehaviour
{
    private int playerHp;
    [SerializeField] private Text hpText;

    public void TakeDamage(int damage)
    {
        playerHp -= damage;
        hpText.text = $"HP: {playerHp}"; // ロジックとUIが混在
    }
}
```

#### レイヤー構造:
- **Core Layer (ロジック層)**: Unity非依存の純粋なC#クラス
- **Presentation Layer (表示層)**: MonoBehaviourを継承したViewクラス
- **Application Layer (制御層)**: ロジックとビューを繋ぐController/Presenterクラス

---

### 2. Unity固有機能への依存の最小化
**ゲームロジックはUnity固有の仕組み（MonoBehaviour、Coroutine等）に極力依存しないこと。**

#### 依存を避けるべきUnity固有要素:
- `MonoBehaviour`
- `Transform`、`GameObject`
- `Coroutine`
- `UnityEngine.Random`
- Unity固有の属性（`[SerializeField]`等）

#### 推奨する実装パターン:

##### ✅ 良い例: Unity非依存のロジック
```csharp
// Core/Logic/CardSystem.cs
namespace Olca.Core.Logic
{
    public class CardSystem
    {
        private readonly IDeck deck;
        private readonly IRandomProvider random;

        public CardSystem(IDeck deck, IRandomProvider random)
        {
            this.deck = deck;
            this.random = random;
        }

        public List<Card> DrawCards(int count)
        {
            return deck.DrawRandom(count, random);
        }
    }

    // 乱数のインターフェース
    public interface IRandomProvider
    {
        int Range(int min, int max);
    }
}

// Unity/RandomProvider.cs (Unity側の実装)
namespace Olca.Unity
{
    public class UnityRandomProvider : IRandomProvider
    {
        public int Range(int min, int max)
        {
            return UnityEngine.Random.Range(min, max);
        }
    }
}

// Tests/MockRandomProvider.cs (テスト用)
namespace Olca.Tests
{
    public class MockRandomProvider : IRandomProvider
    {
        private int fixedValue;

        public MockRandomProvider(int fixedValue)
        {
            this.fixedValue = fixedValue;
        }

        public int Range(int min, int max) => fixedValue;
    }
}
```

##### ✅ 良い例: コルーチンを使わない非同期処理
```csharp
// Core/Logic/BattleSystem.cs
public class BattleSystem
{
    private float customGauge;
    private readonly float gaugeSpeed;

    // deltaTimeを外部から受け取る
    public void UpdateGauge(float deltaTime)
    {
        customGauge += gaugeSpeed * deltaTime;
        if (customGauge >= 1.0f)
        {
            customGauge = 1.0f;
            OnGaugeFull?.Invoke();
        }
    }

    public event Action OnGaugeFull;
}

// Unity/BattleController.cs
public class BattleController : MonoBehaviour
{
    private BattleSystem battleSystem;

    private void Update()
    {
        // Unity側でdeltaTimeを渡す
        battleSystem.UpdateGauge(Time.deltaTime);
    }
}
```

---

## ディレクトリ構造

```
Assets/
├── Olca/
│   ├── Core/                    # Unity非依存のゲームロジック
│   │   ├── Logic/              # ビジネスロジック
│   │   │   ├── Battle/         # バトルシステム
│   │   │   ├── Card/           # カードシステム
│   │   │   ├── Dungeon/        # ダンジョンシステム
│   │   │   └── Player/         # プレイヤー管理
│   │   ├── Domain/             # ドメインモデル（データ構造）
│   │   │   ├── Card.cs
│   │   │   ├── Deck.cs
│   │   │   ├── PlayerCharacter.cs
│   │   │   └── Enemy.cs
│   │   └── Interfaces/         # インターフェース定義
│   │       ├── IDeck.cs
│   │       ├── IRandomProvider.cs
│   │       └── ITimeProvider.cs
│   │
│   ├── Application/            # ロジックとビューの仲介
│   │   ├── Battle/
│   │   │   ├── BattleController.cs
│   │   │   └── CustomScreenController.cs
│   │   └── Dungeon/
│   │       └── DungeonController.cs
│   │
│   ├── Presentation/           # Unity依存のView層
│   │   ├── Battle/
│   │   │   ├── BattleView.cs
│   │   │   ├── PlayerHpView.cs
│   │   │   └── CustomGaugeView.cs
│   │   └── Dungeon/
│   │       └── DungeonView.cs
│   │
│   ├── Unity/                  # Unity固有機能のラッパー
│   │   ├── UnityRandomProvider.cs
│   │   ├── UnityTimeProvider.cs
│   │   └── UnityInputProvider.cs
│   │
│   └── Tests/                  # ユニットテスト
│       ├── Battle/
│       ├── Card/
│       └── Mocks/              # テスト用モック
│
└── Planning/                   # 仕様書（既存）
    ├── バトルシステムについて
    └── ダンジョンについて
```

---

## コーディング規約

### 命名規則
- **クラス名**: PascalCase（例: `BattleSystem`, `CardDeck`）
- **メソッド名**: PascalCase（例: `DrawCard`, `TakeDamage`）
- **private フィールド**: camelCase（例: `playerHp`, `customGauge`）
- **public プロパティ**: PascalCase（例: `PlayerHp`, `IsGameOver`）
- **インターフェース**: 先頭に`I`（例: `IDeck`, `IRandomProvider`）
- **定数**: UPPER_SNAKE_CASE（例: `MAX_HAND_SIZE`, `DEFAULT_DECK_SIZE`）

### コメント規約
```csharp
/// <summary>
/// カードをデッキからドローします
/// </summary>
/// <param name="count">ドローする枚数</param>
/// <returns>ドローしたカードのリスト</returns>
public List<Card> DrawCards(int count)
{
    // 手札上限をチェック
    int actualDrawCount = Math.Min(count, MAX_HAND_SIZE - currentHandSize);

    // デッキからランダムにドロー
    return deck.DrawRandom(actualDrawCount);
}
```

### コード品質
- **null安全性**: null許容型（`?`）を適切に使用
- **不変性**: 可能な限り`readonly`を使用
- **依存性注入**: コンストラクタインジェクションを優先
- **SOLID原則**: 特に単一責任の原則とDIP（依存性逆転の原則）を重視

---

## テスト方針

### 1. Core層のロジックは100%テスト可能であること
Unity非依存のため、通常のC#ユニットテストフレームワーク（NUnit）でテスト可能。

```csharp
// Tests/Battle/BattleSystemTests.cs
[TestFixture]
public class BattleSystemTests
{
    [Test]
    public void TakeDamage_HPが0以下になったら_GameOverになる()
    {
        // Arrange
        var player = new PlayerCharacter(maxHp: 100);
        var battleSystem = new BattleSystem(player);

        // Act
        battleSystem.TakeDamage(150);

        // Assert
        Assert.IsTrue(battleSystem.IsGameOver);
        Assert.AreEqual(0, player.CurrentHp);
    }
}
```

### 2. Presentation層は最小限に
View層は薄く保ち、ロジックを含めない。表示のみに専念。

---

## Unity固有機能の使用ガイドライン

### 使用可能な箇所
- **Presentation層**: MonoBehaviour、SerializeField、Unity UIコンポーネント
- **Application層**: MonoBehaviourを使ったController（ただし薄く保つ）
- **Unity層**: Unity APIのラッパー実装

### 使用禁止な箇所
- **Core層**: 一切のUnity依存コードを含めない
- **Domain層**: Unity型（Vector3、Transform等）を使わない

### 例外的に許可されるケース
- パフォーマンス上Unity APIが必須の場合（Physics、NavMesh等）
  - この場合もインターフェースで抽象化すること

---

## 開発フロー

### 新機能実装時の手順
1. **Planning/配下の仕様書を確認**
2. **Core層でロジックを実装**（Unity非依存）
3. **ユニットテストを作成**してロジックを検証
4. **Application層でControllerを実装**
5. **Presentation層でViewを実装**
6. **Unity Editorで動作確認**

### コードレビューチェックリスト
- [ ] Core層にUnity依存コードが含まれていないか
- [ ] ロジックとビューが分離されているか
- [ ] 適切な日本語コメントが付いているか
- [ ] ユニットテストが書かれているか（Core層）
- [ ] SOLID原則に違反していないか

---

## パフォーマンス指針

### メモリ管理
- **GC回避**: `new`を頻繁に呼ばない。オブジェクトプールを活用
- **struct活用**: 小さいデータ構造は`struct`を検討
- **配列再利用**: `List.Clear()`で再利用

### Update最適化
- **不要なUpdate削除**: 毎フレーム処理が不要ならUpdateを使わない
- **イベント駆動**: 状態変化時のみ処理するようイベントを活用

```csharp
// ❌ 悪い例: 毎フレームチェック
private void Update()
{
    if (battleSystem.IsGaugeFull())
    {
        OpenCustomScreen();
    }
}

// ✅ 良い例: イベント駆動
private void Start()
{
    battleSystem.OnGaugeFull += OpenCustomScreen;
}
```

---

## デバッグとログ

### ログレベル
```csharp
// Core層: ロジックログ（Unityに依存しない）
public class Logger
{
    public static void LogInfo(string message)
    {
        #if UNITY_EDITOR
        UnityEngine.Debug.Log($"[INFO] {message}");
        #else
        Console.WriteLine($"[INFO] {message}");
        #endif
    }

    public static void LogWarning(string message) { /* 同様 */ }
    public static void LogError(string message) { /* 同様 */ }
}
```

### デバッグモード
```csharp
// Core層でもデバッグ情報を出力できるように
public class BattleSystem
{
    private readonly ILogger logger;

    public BattleSystem(ILogger logger)
    {
        this.logger = logger;
    }

    public void TakeDamage(int damage)
    {
        logger.LogInfo($"プレイヤーが{damage}ダメージを受けました");
        playerHp -= damage;
    }
}
```

---

## よくある質問

### Q: MonoBehaviourを使わずにどうやってUpdateループを実現しますか？
A: Application層のControllerがMonoBehaviourを継承し、Core層のロジックに`deltaTime`を渡す設計にします。

```csharp
// Core層
public class BattleSystem
{
    public void Tick(float deltaTime) { /* ロジック */ }
}

// Application層
public class BattleController : MonoBehaviour
{
    private BattleSystem battleSystem;

    private void Update()
    {
        battleSystem.Tick(Time.deltaTime);
    }
}
```

### Q: ScriptableObjectは使ってもいいですか？
A: データ定義としては使用可能ですが、ロジックは含めないでください。

```csharp
// ✅ OK: データのみ
[CreateAssetMenu]
public class CardData : ScriptableObject
{
    public string cardName;
    public int cost;
    public int damage;
}

// ❌ NG: ロジックを含む
[CreateAssetMenu]
public class CardData : ScriptableObject
{
    public string cardName;
    public int cost;

    public void ExecuteEffect(BattleSystem battle) // これはNG
    {
        battle.DealDamage(cost * 10);
    }
}
```

---

## 参考リンク
- [Planning/バトルシステムについて](Planning/バトルシステムについて)
- [Planning/ダンジョンについて](Planning/ダンジョンについて)

---

## 更新履歴
- 2025-11-14: 初版作成
