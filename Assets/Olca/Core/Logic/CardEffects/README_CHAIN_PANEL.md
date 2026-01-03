# カード効果システム: チェイン効果と全パネル変化の実装

## 概要

以下の2つの新機能を実装しました:

1. **チェイン効果システム**: ヒット時に別のカードを連鎖発動する仕組み
2. **全パネル変化効果**: 敵の有無に関わらず画面全体のパネルを変化させる効果

---

## 1. チェイン効果システム

### 1.1. 概要

チェイン効果は、カード効果がヒットした際に、別のカード（またはカード自身）を連鎖発動する仕組みです。

**代表的な使用例:**
- **スプレッドガン**: ヒット時に爆風カードを発動（爆風カードには独自のダメージ・追加効果を設定可能）
- **三段突き**: ヒット時に自分自身を再発動（最大3回まで）
- **毒矢**: ヒット時に毒状態付与カードを発動

### 1.2. AdditionalEffectType の追加

```csharp
public enum AdditionalEffectType
{
    // ... 既存の効果 ...

    /// <summary>別のカードを連鎖発動（ChainCardIdで指定されたカードを実行）</summary>
    ChainCard
}
```

### 1.3. AdditionalEffect クラスの拡張

```csharp
public class AdditionalEffect
{
    public AdditionalEffectType EffectType { get; set; }
    public int Power { get; set; }
    public float Duration { get; set; }
    public int HitCount { get; set; }
    public DerivativeTrigger Trigger { get; set; }

    /// <summary>
    /// チェイン発動するカードID
    /// EffectTypeがChainCardの場合に使用
    /// null の場合は自分自身を再発動
    /// </summary>
    public string ChainCardId { get; set; }
}
```

### 1.4. 使用例

#### 例1: スプレッドガン（爆風カードを発動）

**メインカード: spread_gun**
```csharp
var spreadGunModifier = new CardModifier();
spreadGunModifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.ChainCard,
    ChainCardId = "explosion_wave",  // 爆風カードのID
    Trigger = DerivativeTrigger.OnHit
});

var spreadGun = new Card(
    "spread_gun", "スプレッドガン", 2, CardRarity.Normal,
    CardCategory.Attack, ElementType.None, CardSeries.None,
    30, "着弾地点から爆風を発生させる",
    modifier: spreadGunModifier,
    targetType: DamageTargetType.Single,
    recoveryTime: 0.4f
);
```

**爆風カード: explosion_wave**
```csharp
var explosionModifier = new CardModifier();
explosionModifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.PanelIce,  // 爆風で氷パネル化
    Trigger = DerivativeTrigger.Immediate
});

var explosionWave = new Card(
    "explosion_wave", "爆風", 0, CardRarity.None,
    CardCategory.Attack, ElementType.Fire, CardSeries.None,
    10, "爆風ダメージ",
    modifier: explosionModifier,
    targetType: DamageTargetType.FrontTile  // 前方1マス
);
```

**この設定により:**
- スプレッドガン本体: 30ダメージ（単体）
- 爆風: 10ダメージ（前方1マス範囲）+ 氷パネル化

#### 例2: 三段突き（自分自身を再発動）

```csharp
var tripleSwordModifier = new CardModifier();
tripleSwordModifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.ChainCard,
    ChainCardId = null,  // null = 自分自身を再発動
    HitCount = 2,  // 再発動回数（合計3回攻撃）
    Trigger = DerivativeTrigger.OnHit
});

var tripleSword = new Card(
    "triple_sword", "三段突き", 3, CardRarity.Limited,
    CardCategory.Attack, ElementType.None, CardSeries.Sword,
    20, "3回連続で攻撃",
    modifier: tripleSwordModifier,
    recoveryTime: 0.8f,
    extendRecoveryDuringDerivative: true  // 3回目まで硬直
);
```

---

## 2. 全パネル変化効果

### 2.1. 概要

敵の有無に関わらず、画面全体（6×1グリッド）のパネルタイプを変更する効果です。

### 2.2. AdditionalEffectType の追加

```csharp
public enum AdditionalEffectType
{
    // ... 既存の効果 ...

    // ========== パネル変化（画面全体） ==========
    /// <summary>画面全体のパネルをIceに変化</summary>
    PanelIceAll,

    /// <summary>画面全体のパネルをMagmaに変化</summary>
    PanelMagmaAll,

    /// <summary>画面全体のパネルをGrassに変化</summary>
    PanelGrassAll,

    /// <summary>画面全体のパネルをCrackedに変化</summary>
    PanelCrackedAll,

    /// <summary>画面全体のパネルをHoleに変化</summary>
    PanelHoleAll
}
```

### 2.3. 使用例

#### 例: アイスエイジ（画面全体を氷パネルに）

```csharp
var iceAgeModifier = new CardModifier();
iceAgeModifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.PanelIceAll,
    Trigger = DerivativeTrigger.Immediate
});

var iceAge = new Card(
    "ice_age", "アイスエイジ", 5, CardRarity.Rare,
    CardCategory.Field, ElementType.Ice, CardSeries.None,
    0, "画面全体を氷パネルに変化させる",
    modifier: iceAgeModifier,
    targetType: DamageTargetType.All
);
```

---

## 3. 実装の詳細

### 3.1. BattleContext の拡張

チェイン効果でカードを取得するため、`ICardDatabase` インターフェースを追加しました。

```csharp
public class BattleContext
{
    // ... 既存フィールド ...

    /// <summary>
    /// カードデータベース（チェイン効果でカードを取得する際に使用）
    /// Application層から注入される
    /// </summary>
    public ICardDatabase CardDatabase { get; set; }

    /// <summary>
    /// カードIDからカードを取得（チェイン効果で使用）
    /// </summary>
    public Card GetCardById(string cardId)
    {
        if (CardDatabase == null)
        {
            throw new InvalidOperationException(
                "CardDatabase が設定されていません。");
        }

        return CardDatabase.GetCardById(cardId);
    }
}

public interface ICardDatabase
{
    Card GetCardById(string cardId);
}
```

### 3.2. Application層での実装例

```csharp
// Application層でICardDatabaseを実装
public class MasterDataCardDatabase : ICardDatabase
{
    private readonly MasterMemoryDatabase masterData;

    public MasterDataCardDatabase(MasterMemoryDatabase masterData)
    {
        this.masterData = masterData;
    }

    public Card GetCardById(string cardId)
    {
        var cardMaster = masterData.CardTable.FindById(cardId);
        if (cardMaster == null) return null;

        return Card.FromMaster(cardMaster);
    }
}

// BattleControllerでの使用例
public class BattleController : MonoBehaviour
{
    private MasterDataCardDatabase cardDatabase;

    private void Start()
    {
        cardDatabase = new MasterDataCardDatabase(masterMemoryDatabase);
    }

    public void UseCard(Card card)
    {
        var context = new BattleContext(battleSystem, card);

        // カードデータベースを注入
        context.CardDatabase = cardDatabase;

        var effect = CardEffectFactory.CreateEffect(card);
        effect.Execute(context);
    }
}
```

---

## 4. 注意事項

### 4.1. チェイン効果の無限ループ防止

現在の実装では、無限ループ防止機構は**実装されていません**。

**推奨される対策:**
1. `HitCount` フィールドで再発動回数を制限
2. Application層でチェイン深度を監視
3. 最大チェイン回数を設定（例: 10回まで）

### 4.2. チェインカードへの派生効果の適用

チェインカード（爆風など）にも**Modifierの追加効果が適用されます**。

**例:**
```csharp
// スプレッドガンに調律効果「氷パネル化」を付与
var tuningCard = new Card(...);
tuningCard.Modifier.AdditionalEffects.Add(new AdditionalEffect
{
    EffectType = AdditionalEffectType.PanelIce,
    Trigger = DerivativeTrigger.Immediate
});

var enhancedSpreadGun = spreadGun.ApplyTuning(tuningCard);

// → スプレッドガン本体も爆風も、どちらも氷パネル化が発動
```

この仕様により、調律で爆風の挙動をカスタマイズできます。

---

## 5. まとめ

### 実装済み機能
- ✅ `AdditionalEffectType.ChainCard` の追加
- ✅ `AdditionalEffect.ChainCardId` フィールドの追加
- ✅ 全パネル変化効果（PanelIceAll, PanelMagmaAll等）の追加
- ✅ `BattleContext.ICardDatabase` インターフェースの追加
- ✅ `ModifiableCardEffect` でのチェイン効果実行ロジック
- ✅ チェインカードにも派生効果が適用される仕様

### 今後の実装が推奨される項目
- ⏳ チェイン深度の制限機構（Application層）
- ⏳ チェイン効果のパフォーマンス最適化
- ⏳ チェインカードのエフェクト・モーション制御（Presentation層）

---

更新日: 2026-01-03
