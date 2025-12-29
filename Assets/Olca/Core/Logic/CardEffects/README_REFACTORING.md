# カード効果システム リファクタリング完了

## 概要
調律システム（CardModifier）の導入により、個別のEffectクラスを削減し、柔軟な効果の組み合わせを実現しました。

## 削除されたEffectクラス

以下のクラスは **ModifiableDamageEffect** で代替可能となったため削除されました：

1. ✅ **CannonEffect.cs** - 削除済み
   - 置き換え: `new ModifiableDamageEffect(DamageTargetType.Single)`

2. ✅ **SwordEffect.cs** - 削除済み
   - 置き換え: `new ModifiableDamageEffect(DamageTargetType.FrontTile)`

3. ✅ **SpreadGunEffect.cs** - 削除済み
   - 置き換え: 単体攻撃 + `CardModifier.CreateAdditionalEffect(AdditionalEffectType.Spread)`

4. ✅ **VulcanEffect.cs** - 削除済み
   - 置き換え: 単体攻撃 + `AdditionalEffectType.MultiHit`

5. ✅ **DrillArmEffect.cs** - 削除済み
   - 置き換え: 単体攻撃 + `AdditionalEffectType.Penetration`

6. ✅ **IcePanelEffect.cs** - 削除済み
   - 置き換え: `CardModifier.CreateAdditionalEffect(AdditionalEffectType.PanelIce)`

## 残されたEffectクラス

### ModifiableDamageEffect.cs（新規）
すべての攻撃カードのベースとなる汎用効果クラス。
CardModifierの追加効果を自動的に適用します。

**対応する追加効果：**
- Spread（スプレッド）: 着弾地点から前後1マスに拡散
- Penetration（貫通）: 前方の敵全てに貫通
- MultiHit（多段ヒット）: 同じ敵に複数回ヒット
- PanelIce/Magma: パネル変化効果

### DamageEffect.cs（既存）
基本的なダメージ効果。互換性のために保持。

### HealEffect.cs（既存）
回復効果。攻撃とは異なるロジックのため専用クラスとして保持。

## 使用例

### 基本的な攻撃カード
```csharp
// キャノン（単体攻撃）
var cannon = new Card("cannon", "キャノン", 1, CardRarity.Normal,
    CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "単体攻撃");
var effect = new ModifiableDamageEffect(DamageTargetType.Single);
```

### 調律による効果追加
```csharp
// ソード + アタック+10
var sword = new Card("sword", "ソード", ..., power: 80);
var attackPlus = new Card("atk+10", "アタック+10", ...,
    modifier: CardModifier.CreateAttackBonus(10));
var tunedSword = sword.ApplyTuning(attackPlus); // 威力90

// ソード + スプレッド効果
var spreadMod = CardModifier.CreateAdditionalEffect(AdditionalEffectType.Spread);
var spreadCard = new Card("spread", "スプレッド", ..., modifier: spreadMod);
var finalSword = tunedSword.ApplyTuning(spreadCard);
// → 威力90 + スプレッド効果
```

### 複雑な組み合わせ
```csharp
// キャノン + 貫通 + 多段ヒット
var cannon = new Card("cannon", "キャノン", ..., power: 20);

var penetration = CardModifier.CreateAdditionalEffect(AdditionalEffectType.Penetration);
var multiHit = new CardModifier();
multiHit.AdditionalEffects.Add(new AdditionalEffect {
    EffectType = AdditionalEffectType.MultiHit,
    HitCount = 3
});

var tunedCannon = cannon
    .ApplyTuning(new Card(..., modifier: penetration))
    .ApplyTuning(new Card(..., modifier: multiHit));
// → 威力20 × 3ヒット、全敵貫通
```

## CardEffectFactory の更新

`CardEffectFactory.CreateEffect(Card)` メソッドが更新され、
カテゴリに基づいて自動的に適切な効果を生成します：

- **Attack**: ModifiableDamageEffect (調律効果自動適用)
- **Defense**: HealEffect
- **Field**: ModifiableDamageEffect (パネル変化等)
- **Support**: NullEffect (調律専用)

## 利点

1. ✅ **コード削減**: 6つのEffectクラスを削除
2. ✅ **柔軟性**: 無限の効果の組み合わせが可能
3. ✅ **保守性**: 新しい効果は AdditionalEffectType に追加するだけ
4. ✅ **再利用性**: 既存のカードに新しい効果を簡単に付与可能

## テスト

以下のテストファイルで動作確認済み：
- CardEffectSystemTests.cs（基本効果）
- CardModifierTests.cs（調律システム）

---

更新日: 2025-12-29
