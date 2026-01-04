# カード前隙・後隙システム設計書

## 概要

すべてのカードに**前隙（Startup Time）**と**後隙（Recovery Time）**を導入し、大技と小技の使い分けを可能にするシステム。

## 目的

- **ゲームバランス調整**: 前隙が長い大技と、前隙が短い小技を差別化
- **戦略性の向上**: プレイヤーが状況に応じて適切なカードを選択できるように
- **隙の可視化**: モーション中のヒット判定タイミングを明確化

## モーション構造

### 基本フロー

```
カード発動 → [前隙] → ヒット判定・ダメージ → [後隙] → 行動可能
```

### フェーズ詳細

#### 1. 前隙（Startup Time）
- **定義**: カード発動からヒット判定が出るまでの時間
- **目的**: 技の「出の速さ」を制御
- **特性**:
  - 前隙中はダメージ判定が発生しない
  - キャラクターは硬直状態
  - 前隙が長いほど「隙の大きい大技」
  - 前隙が短いほど「出の速い小技」

#### 2. ヒット判定・ダメージ処理
- **定義**: 前隙終了後、実際にダメージ計算が行われる
- **派生効果**: OnHit/OnDamage効果がこのタイミングで発動

#### 3. 後隙（Recovery Time）
- **定義**: ヒット判定後、次の行動が可能になるまでの時間
- **目的**: 技の「硬直時間」を制御
- **特性**:
  - 後隙中もキャラクターは硬直状態
  - 後隙が長いほど「硬直が大きい技」

## データ構造

### Card クラスの拡張

```csharp
public class Card
{
    /// <summary>前隙時間（秒） - カード発動からヒット判定が出るまでの時間</summary>
    public float StartupTime { get; }  // デフォルト: 0秒（マスターデータで個別設定）

    /// <summary>後隙時間（秒） - ヒット判定後、行動可能になるまでの時間</summary>
    public float RecoveryTime { get; }  // デフォルト: 0秒（マスターデータで個別設定）

    /// <summary>暗転系カード（前隙・後隙なしで即座に発動）</summary>
    public bool IsInstantEffect { get; }  // デフォルト: false
}
```

### DelayedEffect の拡張

```csharp
public enum DelayedEffectType
{
    Startup,    // 前隙（カード発動からヒット判定まで）
    Recovery,   // 後隙（ヒット判定後から行動可能まで）
    Projectile  // 投擲物の着弾（穴パネルチェックあり）
}

public class DelayedEffect
{
    public DelayedEffectType EffectType { get; set; }
    public float RemainingTime { get; set; }
    public Action ExecuteAction { get; set; }
}
```

## 実装フロー

### 1. 通常カードの処理フロー

```csharp
Execute(context)
{
    if (card.IsInstantEffect)
    {
        // 暗転系: 即座に発動
        ExecuteCardEffect(context);
        return;
    }

    if (card.StartupTime > 0)
    {
        // 前隙を遅延登録
        RegisterStartupEffect(() => {
            // 前隙終了後にヒット判定
            ExecuteCardEffect(context);

            // 後隙を遅延登録
            if (card.RecoveryTime > 0)
                RegisterRecoveryEffect();
        });
    }
    else
    {
        // 前隙なし: 即座にヒット判定
        ExecuteCardEffect(context);

        // 後隙を遅延登録
        if (card.RecoveryTime > 0)
            RegisterRecoveryEffect();
    }
}
```

### 2. 投擲カードの処理フロー

```
カード発動
  ↓
前隙（StartupTime: 0.3秒）
  ↓
投擲物登録 + ヒット判定なし
  ↓
後隙（RecoveryTime: 0.5秒）
  ↓
行動可能（0.8秒経過時点）
  ↓
（別スレッド）
着弾遅延（DelayTime: 1.0秒）
  ↓
着弾・爆風（1.8秒経過時点）
```

**重要**: 投擲カードは後隙終了後（0.8秒後）に次の行動が可能。着弾（1.8秒後）を待たない。

## 具体例

### 例1: 通常攻撃（ソード）

```csharp
var sword = new Card(
    "sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃",
    DamageTargetType.Single,
    startupTime: 0.2f,   // 前隙0.2秒（出が速い）
    recoveryTime: 0.3f   // 後隙0.3秒（硬直短い）
);
```

**モーション**: `0.2秒前隙 → ヒット → 0.3秒後隙 → 行動可能`
**合計硬直**: 0.5秒

### 例2: 大技（大剣）

```csharp
var greatsword = new Card(
    "greatsword", "大剣", 3, CardRarity.Rare, CardCategory.Attack,
    ElementType.None, CardSeries.Sword, 100, "強力な一撃",
    DamageTargetType.Single,
    startupTime: 0.7f,   // 前隙0.7秒（出が遅い）
    recoveryTime: 0.8f   // 後隙0.8秒（硬直大きい）
);
```

**モーション**: `0.7秒前隙 → ヒット → 0.8秒後隙 → 行動可能`
**合計硬直**: 1.5秒（威力が高い分、隙も大きい）

### 例3: 小技（ナイフ）

```csharp
var knife = new Card(
    "knife", "ナイフ", 1, CardRarity.Normal, CardCategory.Attack,
    ElementType.None, CardSeries.None, 15, "素早い攻撃",
    DamageTargetType.Single,
    startupTime: 0.1f,   // 前隙0.1秒（非常に速い）
    recoveryTime: 0.2f   // 後隙0.2秒（硬直最小）
);
```

**モーション**: `0.1秒前隙 → ヒット → 0.2秒後隙 → 行動可能`
**合計硬直**: 0.3秒（威力は低いが連続で使える）

### 例4: 暗転系（ダークシールド）

```csharp
var darkShield = new Card(
    "dark_shield", "ダークシールド", 2, CardRarity.Rare, CardCategory.Support,
    ElementType.Dark, CardSeries.None, 0, "即座にバリア展開",
    DamageTargetType.Single,
    startupTime: 0.5f,   // ← 無視される
    recoveryTime: 0.5f,  // ← 無視される
    isInstantEffect: true  // 即座発動フラグ
);
```

**モーション**: `即座発動（前隙・後隙なし）`
**合計硬直**: 0秒

### 例5: 投擲カード（ボム）

```csharp
var modifier = new CardModifier();
modifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.DelayedProjectile,
    DelayTime = 1.0f,           // 着弾遅延1.0秒
    TargetOffset = new Vector2Int(3, 0),
    ChainCardId = "explosion"
});

var bomb = new Card(
    "bomb", "ボム", 2, CardRarity.Normal, CardCategory.Attack,
    ElementType.Fire, CardSeries.None, 0, "投擲爆弾",
    DamageTargetType.Single,
    modifier: modifier,
    startupTime: 0.3f,   // 前隙0.3秒（投げるモーション）
    recoveryTime: 0.5f   // 後隙0.5秒（投げた後の硬直）
);
```

**モーション**:
```
0.3秒前隙 → 投擲登録 → 0.5秒後隙 → 行動可能（0.8秒経過）
                                  ↓
                           1.0秒着弾遅延 → 着弾・爆風（1.8秒経過）
```

**重要ポイント**:
- 0.8秒後には次のカードが使える（着弾を待たない）
- 着弾は1.8秒後に独立して発生

## DelayedEffectSystem の役割

### 3種類の遅延効果を統一管理

```csharp
public void Update(float deltaTime)
{
    foreach (var effect in activeEffects)
    {
        effect.RemainingTime -= deltaTime;

        if (effect.RemainingTime <= 0)
        {
            // Projectileタイプのみ穴パネルチェック
            if (effect.EffectType == DelayedEffectType.Projectile &&
                effect.CancelOnHole &&
                battleSystem.Field.IsHole(effect.TargetPosition))
            {
                effect.OnCancelled?.Invoke();
            }
            else
            {
                effect.Execute();
            }
        }
    }
}
```

## テストケース

### 1. 前隙がある場合、即座にダメージは発生しない
```csharp
var sword = new Card(..., startupTime: 0.5f);
effect.Execute(context);
Assert.AreEqual(100, enemy.CurrentHp); // 前隙中はダメージなし
```

### 2. 前隙経過後にダメージが発生する
```csharp
effect.Execute(context);
battle.DelayedEffects.Update(0.5f); // 前隙終了
Assert.AreEqual(70, enemy.CurrentHp); // ダメージ発生
```

### 3. 前隙と後隙が両方登録される
```csharp
effect.Execute(context);
Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount); // 前隙登録

battle.DelayedEffects.Update(0.3f); // 前隙終了
Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount); // 後隙登録

battle.DelayedEffects.Update(0.5f); // 後隙終了
Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount); // 完了
```

### 4. 暗転系カードは即座に発動する
```csharp
var instant = new Card(..., isInstantEffect: true);
effect.Execute(context);
Assert.AreEqual(50, enemy.CurrentHp); // 即座にダメージ
Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount); // 遅延なし
```

### 5. 前隙と投擲遅延が独立して動作する
```csharp
// 前隙0.2秒 + 投擲遅延1.0秒のボム
effect.Execute(context);

// 0.2秒後: 前隙終了 → 投擲登録 + 後隙登録
battle.DelayedEffects.Update(0.2f);
Assert.AreEqual(2, battle.DelayedEffects.ActiveEffectCount); // 投擲 + 後隙

// 0.3秒後: 後隙終了（投擲は継続）
battle.DelayedEffects.Update(0.3f);
Assert.AreEqual(1, battle.DelayedEffects.ActiveEffectCount); // 投擲のみ

// 0.7秒後: 投擲着弾
battle.DelayedEffects.Update(0.7f);
Assert.AreEqual(80, enemies[1].CurrentHp); // 爆風ダメージ
```

## ゲームバランス設計指針

### 前隙時間の目安

| カードタイプ | 前隙時間 | 説明 |
|------------|---------|------|
| 小技 | 0.1 ~ 0.2秒 | 出が速く連続で使える |
| 通常攻撃 | 0.2 ~ 0.4秒 | バランス型 |
| 大技 | 0.5 ~ 1.0秒 | 隙が大きいが威力も高い |
| 投擲 | 0.3 ~ 0.5秒 | 投げるモーション |
| 暗転系 | 0秒（即座） | 緊急回避・バリア展開 |

### 後隙時間の目安

| カードタイプ | 後隙時間 | 説明 |
|------------|---------|------|
| 小技 | 0.2 ~ 0.3秒 | すぐ次の行動が可能 |
| 通常攻撃 | 0.3 ~ 0.5秒 | バランス型 |
| 大技 | 0.6 ~ 1.2秒 | 硬直が大きい |
| 投擲 | 0.4 ~ 0.6秒 | 投げた後の硬直 |
| 暗転系 | 0秒（即座） | 硬直なし |

### 合計硬直時間の目安

| カードタイプ | 合計硬直 | 例 |
|------------|---------|-----|
| 超小技 | 0.3秒 | ナイフ（前0.1 + 後0.2） |
| 小技 | 0.5秒 | ソード（前0.2 + 後0.3） |
| 通常攻撃 | 0.8秒 | ロングソード（前0.3 + 後0.5） |
| 大技 | 1.5秒 | 大剣（前0.7 + 後0.8） |
| 投擲 | 0.8秒 | ボム（前0.3 + 後0.5） ※着弾は別 |

## 実装ファイル

### Core層
- `Card.cs`: StartupTime, RecoveryTime, IsInstantEffect フィールド追加
- `CardMaster.cs`: 同上（マスターデータ）
- `DelayedEffect.cs`: DelayedEffectType 追加、コンストラクタ拡張
- `DelayedEffectSystem.cs`: Projectileタイプのみ穴パネルチェック

### Logic層
- `ModifiableCardEffect.cs`: Execute() メソッドを前隙・後隙対応に書き換え
  - `Execute()`: 前隙・後隙の登録処理
  - `ExecuteCardEffect()`: 実際のヒット判定・ダメージ処理（前隙後に実行）

### Tests
- `StartupRecoveryTests.cs`: 前隙・後隙のテストケース
  - 前隙がある場合、即座にダメージは発生しない
  - 前隙経過後にダメージが発生する
  - 前隙と後隙が両方登録される
  - 暗転系カードは即座に発動する
  - 前隙がゼロの場合は即座に発動するが後隙は登録される
  - 前隙と投擲遅延が独立して動作する

## まとめ

- **すべてのカード**に前隙・後隙を導入し、大技と小技を差別化
- **暗転系カード**（IsInstantEffect=true）は前隙・後隙なしで即座発動
- **投擲カード**は前隙・後隙と着弾遅延が独立して動作
- **DelayedEffectSystem**で3種類の遅延効果（前隙/後隙/投擲）を統一管理
- 既存の投擲システムと完全互換性を保ちながら、新機能を追加
