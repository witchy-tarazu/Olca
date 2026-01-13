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
- **`Coroutine`（使用禁止）**
- `UnityEngine.Random`
- Unity固有の属性（`[SerializeField]`等）

#### **重要: Coroutineの使用禁止**
**このプロジェクトではCoroutineの使用を禁止します。**

- **理由**: Coroutineはテスト困難、Unity依存、デバッグが難しい
- **代替手段**: UniTask または async/await を使用
- **アニメーション**: Animator または UniTask によるTween実装を使用

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

##### ✅ 良い例: Coroutineを使わない処理
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

##### ✅ 良い例: UniTaskを使った非同期処理（将来実装予定）
```csharp
using Cysharp.Threading.Tasks;

// Presentation/Battle/BattleView.cs
public class BattleView : MonoBehaviour
{
    public async UniTask OpenCustomScreenAsync(CancellationToken ct)
    {
        // UniTaskによるアニメーション
        await UniTask.WhenAll(
            AnimateHpPositionAsync(ct),
            AnimateGaugePositionAsync(ct),
            AnimateCustomScreenAsync(ct)
        );
    }

    private async UniTask AnimateHpPositionAsync(CancellationToken ct)
    {
        // DOTweenやUniTask.Delayを使用したアニメーション
        await hpTransform.DOAnchorPosX(960f, 0.3f).ToUniTask(cancellationToken: ct);
    }
}
```

##### ❌ 悪い例: Coroutineの使用（禁止）
```csharp
// ❌ このようなコードは書かないでください
public class BattleView : MonoBehaviour
{
    public void OpenCustomScreen()
    {
        StartCoroutine(AnimateOpen());  // NG: Coroutine使用禁止
    }

    private IEnumerator AnimateOpen()  // NG
    {
        // ...
        yield return null;  // NG
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
    ├── ダンジョンについて
    └── ビジュアル・アート/    # UI/UXデザイン仕様（ビジュアルのみ）
        ├── UIデザイン仕様.md
        └── カスタム画面仕様.md
```

### 仕様書の役割分担（重要）

**Planning/配下の仕様書は役割が明確に分かれています:**

1. **ゲームシステム仕様** (`Planning/バトルシステムについて`, `Planning/ダンジョンについて`)
   - ゲームロジック・ルール・計算式
   - コスト計算、選択制限ルール、シリーズボーナス等
   - **実装時はこちらを参照してロジックを組む**

2. **ビジュアル・UI仕様** (`Planning/ビジュアル・アート/`)
   - 画面レイアウト、アニメーション、演出
   - 色・サイズ・配置などの視覚要素
   - **ゲームロジックは含まない（UI/UXのみ）**

**注意**: カスタム画面の実装時は以下の両方を参照すること:
- ゲームロジック → `Planning/バトルシステムについて` の「4.3. カードコストと選択ルール」
- ビジュアル → `Planning/ビジュアル・アート/カスタム画面仕様.md`

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

## Unity Editor操作とテスト実行のルール

このプロジェクトでは、Unity固有の操作とテスト実行は**ユーザー側で行う**ことを原則とします。

### 1. .metaファイルの生成

**重要:** C#ファイルを新規作成した後、`.meta`ファイルの生成は**ユーザーが行います**。

#### ルール:
- AIは`.meta`ファイルの生成を試みない
- AIは新しいC#ファイルを作成した後、ユーザーに「Unityエディタにフォーカスしてmetaファイルを生成してください」と依頼しない
- ユーザーは必要に応じてUnityエディタにフォーカスし、自動的に`.meta`ファイルを生成する

#### 理由:
- `.meta`ファイルはUnity Editorが自動生成する
- GUIDの重複を避けるため、手動作成は推奨されない
- ユーザーがUnityエディタにフォーカスするだけで自動生成される

### 2. ユニットテストの実行

**重要:** テストの実行は**ユーザーが行います**。

#### ルール:
- AIは`dotnet test`コマンドを実行しない
- AIはテストコードを作成・修正した後、テストの実行をユーザーに依頼しない
- ユーザーが必要に応じてテストを実行し、結果を報告する

#### 理由:
- Unity環境では`.csproj`ファイルが自動生成され、場所が変わる可能性がある
- テスト実行環境（Unity Test Runner vs dotnet test）はユーザーが管理する
- テスト結果の確認タイミングはユーザーが判断する

### 3. コンパイルエラーの確認

**重要:** コンパイルエラーの修正を依頼された際は、**MCP経由でUnityコンソールログを確認すること**。

#### ルール:
- コンパイルエラー修正の依頼を受けたら、まず`mcp__UnityMCP__read_console`ツールでログを確認する
- エラーログから問題箇所を特定し、修正方針を決定する
- 修正後も再度ログを確認し、エラーが解消されたことを検証する

#### ワークフロー:
```
1. ユーザーから「コンパイルエラーを修正して」と依頼される
2. AIが `mcp__UnityMCP__read_console` を実行してエラーログを取得
3. エラー内容を分析し、該当ファイルを読み込む
4. コードを修正する
5. （ユーザーがUnityエディタで確認）
6. 必要に応じて再度ログを確認して検証
```

#### 理由:
- Unityのコンパイルエラーは詳細な情報（ファイル名、行番号、エラー内容）を含む
- ユーザーがエラー内容をコピー＆ペーストする手間を省ける
- リアルタイムのエラー状況を正確に把握できる

### 4. AIの役割

AIは以下に専念します：
- C#コードの作成・編集
- テストコードの作成・編集
- コードレビューとアドバイス
- アーキテクチャ設計の提案
- **MCP経由でのUnityコンソールログ確認**

### 5. ユーザーの役割

ユーザーは以下を担当します：
- Unity Editorでの`.meta`ファイル生成
- テストの実行と結果報告
- Unity Editor上での動作確認
- ビルドと実機確認

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

## 名前空間の競合対策

本プロジェクトでは、Unityと独自実装で同名の型が存在する場合があります。以下のガイドラインに従ってください。

### 競合が発生する型

1. **Vector2Int**: `UnityEngine.Vector2Int` vs `Olca.Core.Domain.Vector2Int`
2. **Card**: 名前空間 `Olca.Tests.Card` vs 型 `Olca.Core.Domain.Card`

### 対処方法

#### 方法1: エイリアスを使用（推奨）

```csharp
using UnityEngine;
using Olca.Core.Domain;
using Vector2Int = Olca.Core.Domain.Vector2Int; // エイリアス定義

public class Example : MonoBehaviour
{
    void Start()
    {
        // Vector2Intは自動的にOlca.Core.Domain.Vector2Intを参照
        var pos = new Vector2Int(3, 5);
    }
}
```

#### 方法2: 完全修飾名を使用

```csharp
var pos = new Olca.Core.Domain.Vector2Int(3, 5);
var unityPos = new UnityEngine.Vector2Int(1, 2);
```

#### 方法3: 名前空間名を変更（テストコードの場合）

型名と競合する名前空間は避ける：
- ❌ `namespace Olca.Tests.Card` ← Card型と競合
- ✅ `namespace Olca.Tests.CardTests` ← 競合なし

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
- 2025-12-30: コンパイルエラー確認のMCP使用ルールを追加
- 2025-12-29: Unity Editor操作とテスト実行のルールを追加
- 2025-11-14: 初版作成
