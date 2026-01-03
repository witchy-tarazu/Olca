# カード効果システム: 派生効果と硬直時間の実装

## 概要

「カード効果について」の仕様（5章: 派生効果、6章: 硬直時間）に対応するため、以下の機能を実装しました。

---

## 1. 硬直時間システム

### 1.1. CardMasterに追加したフィールド

```csharp
/// <summary>
/// カード使用後の硬直時間（秒）
/// この時間中、プレイヤーは移動や他のカード使用ができない
/// </summary>
public float RecoveryTime { get; set; }

/// <summary>
/// 派生効果中も硬直を継続するか
/// true: 派生効果終了まで硬直が続く（例: 三段突き）
/// false: 派生効果中でも動ける（例: スプレッドガン）
/// </summary>
public bool ExtendRecoveryDuringDerivative { get; set; }
```

### 1.2. Cardクラスへの対応

- `RecoveryTime` プロパティを追加（デフォルト: 0.5秒）
- `ExtendRecoveryDuringDerivative` プロパティを追加（デフォルト: false）
- コンストラクタ、ApplyTuning()、FromMaster()、Clone() すべてに対応

### 1.3. 使用例

```csharp
// CardMasterデータ例1: スプレッドガン
{
    Id: "spread_gun",
    Name: "スプレッドガン",
    Power: 20,
    RecoveryTime: 0.4f,  // 硬直時間: 0.4秒
    ExtendRecoveryDuringDerivative: false  // 爆風中も動ける
}

// CardMasterデータ例2: 三段突き
{
    Id: "triple_slash",
    Name: "三段突き",
    Power: 30,
    RecoveryTime: 0.6f,  // 硬直時間: 0.6秒
    ExtendRecoveryDuringDerivative: true  // 3回目の攻撃まで硬直
}
```

---

## 2. 派生効果システム

### 2.1. 派生効果の種類

**ヒット時派生** と **ダメージ時派生** の2種類があります。

#### ヒット時派生（OnHit）
- **概要**: 命中時に別の攻撃に派生する効果
- **例**: スプレッドガン（着弾地点から前後1マスに爆風が広がる）

#### ダメージ時派生（OnDamage）
- **概要**: ダメージを与えた際に追加効果が発動
- **例**: ポイズンアロー（ダメージを与えた敵に毒状態を付与）

### 2.2. AdditionalEffect クラスの拡張

```csharp
public class AdditionalEffect
{
    public AdditionalEffectType EffectType { get; set; }
    public int Power { get; set; }
    public float Duration { get; set; }
    public int HitCount { get; set; }

    /// <summary>
    /// 派生効果のトリガータイミング
    /// </summary>
    public DerivativeTrigger Trigger { get; set; }
}

public enum DerivativeTrigger
{
    Immediate,  // 即座に発動（派生なし）
    OnHit,      // ヒット時に派生
    OnDamage    // ダメージ時に派生
}
```

### 2.3. AdditionalEffectType の拡張

新たに以下の効果タイプを追加しました：

```csharp
public enum AdditionalEffectType
{
    // ========== 基本的な攻撃効果 ==========
    None,
    Spread,         // スプレッド効果【ヒット時派生】
    Penetration,    // 貫通効果
    MultiHit,       // 多段ヒット効果

    // ========== 状態異常（ダメージ時派生） ==========
    Paralyze,       // 麻痺【ダメージ時派生】
    Poison,         // 毒【ダメージ時派生】
    Confusion,      // 混乱【ダメージ時派生】NEW
    Blind,          // 盲目【ダメージ時派生】NEW

    // ========== パネル変化 ==========
    PanelIce,
    PanelMagma,
    PanelGrass,     // NEW
    PanelCracked,   // NEW
    PanelHole       // NEW
}
```

### 2.4. 使用例

#### 例1: スプレッドガン（ヒット時派生）

```csharp
var spreadModifier = new CardModifier();
spreadModifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.Spread,
    Power = 10,  // 爆風のダメージ
    Trigger = DerivativeTrigger.OnHit  // ヒット時に派生
});

var spreadGun = new Card(
    "spread_gun", "スプレッドガン", 2, CardRarity.Normal,
    CardCategory.Attack, ElementType.None, CardSeries.None, 20, "着弾地点から爆風",
    modifier: spreadModifier,
    recoveryTime: 0.4f,
    extendRecoveryDuringDerivative: false  // 爆風中も動ける
);
```

#### 例2: ポイズンアロー（ダメージ時派生）

```csharp
var poisonModifier = new CardModifier();
poisonModifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.Poison,
    Power = 5,        // 継続ダメージ（毎秒）
    Duration = 3.0f,  // 3秒間毒状態
    Trigger = DerivativeTrigger.OnDamage  // ダメージ時に派生
});

var poisonArrow = new Card(
    "poison_arrow", "ポイズンアロー", 3, CardRarity.Limited,
    CardCategory.Attack, ElementType.None, CardSeries.None, 15, "毒を付与する矢",
    modifier: poisonModifier,
    recoveryTime: 0.5f
);
```

---

## 3. 実装の注意点

### 3.1. ModifiableCardEffectでの派生処理

現在の `ModifiableCardEffect.cs` では、`AdditionalEffect` を即座に適用していますが、
新仕様に対応するためには以下の変更が必要です：

```csharp
// 【TODO】将来的な実装
private void ApplyAdditionalEffect(
    BattleContext context,
    AdditionalEffect effect,
    ref List<Enemy> targets,
    int damage,
    ElementType element)
{
    switch (effect.Trigger)
    {
        case DerivativeTrigger.Immediate:
            // 即座に発動
            ExecuteEffect(effect, context, targets, damage, element);
            break;

        case DerivativeTrigger.OnHit:
            // ヒット時に派生（敵に命中した後に発動）
            context.RegisterOnHitCallback(() =>
            {
                ExecuteEffect(effect, context, targets, damage, element);
            });
            break;

        case DerivativeTrigger.OnDamage:
            // ダメージ時に派生（ダメージを与えた後に発動）
            context.RegisterOnDamageCallback((damagedEnemy) =>
            {
                ApplyStatusEffect(effect, damagedEnemy);
            });
            break;
    }
}
```

### 3.2. 硬直時間の制御

**Application層** (BattleController等) で硬直時間を管理する必要があります：

```csharp
public class BattleController : MonoBehaviour
{
    private float recoveryTimer = 0f;
    private bool isRecovering = false;

    public void UseCard(Card card)
    {
        // カード効果を実行
        var effect = CardEffectFactory.CreateEffect(card);
        effect.Execute(battleContext);

        // 硬直時間を設定
        recoveryTimer = card.RecoveryTime;
        isRecovering = true;

        // 派生効果中の硬直を考慮
        if (card.ExtendRecoveryDuringDerivative)
        {
            // 派生効果の終了を監視して硬直時間を延長
            // TODO: 実装
        }
    }

    private void Update()
    {
        if (isRecovering)
        {
            recoveryTimer -= Time.deltaTime;
            if (recoveryTimer <= 0f)
            {
                isRecovering = false;
                // 硬直解除
            }
        }
    }
}
```

---

## 4. まとめ

### 実装済み
- ✅ CardMasterに `RecoveryTime` と `ExtendRecoveryDuringDerivative` を追加
- ✅ Cardクラスに硬直時間プロパティを追加
- ✅ AdditionalEffectに `DerivativeTrigger` を追加
- ✅ AdditionalEffectTypeに状態異常とパネル変化を追加

### 今後の実装予定
- ⏳ ModifiableCardEffectでの派生効果の実装（OnHit/OnDamage分岐）
- ⏳ Application層での硬直時間制御
- ⏳ 状態異常システム（麻痺、毒、混乱、盲目）の実装
- ⏳ BattleContextへのコールバック機能追加

---

更新日: 2026-01-03
