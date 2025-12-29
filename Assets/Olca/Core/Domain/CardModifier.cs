using System.Collections.Generic;

namespace Olca.Core.Domain
{
    /// <summary>
    /// カード調律による追加効果
    /// アタック+10やファイア+20などの効果を表現
    /// </summary>
    public class CardModifier
    {
        /// <summary>追加ダメージ</summary>
        public int BonusDamage { get; set; }

        /// <summary>ダメージ倍率（1.0 = 等倍）</summary>
        public float DamageMultiplier { get; set; }

        /// <summary>追加効果リスト</summary>
        public List<AdditionalEffect> AdditionalEffects { get; set; }

        /// <summary>属性上書き（nullなら元の属性を使用）</summary>
        public ElementType? ElementOverride { get; set; }

        public CardModifier()
        {
            BonusDamage = 0;
            DamageMultiplier = 1.0f;
            AdditionalEffects = new List<AdditionalEffect>();
            ElementOverride = null;
        }

        /// <summary>
        /// 別の調律効果と合成する
        /// </summary>
        public CardModifier Merge(CardModifier other)
        {
            if (other == null) return this;

            var merged = new CardModifier
            {
                BonusDamage = this.BonusDamage + other.BonusDamage,
                DamageMultiplier = this.DamageMultiplier * other.DamageMultiplier,
                ElementOverride = other.ElementOverride ?? this.ElementOverride,
                AdditionalEffects = new List<AdditionalEffect>(this.AdditionalEffects)
            };

            merged.AdditionalEffects.AddRange(other.AdditionalEffects);
            return merged;
        }

        /// <summary>
        /// アタック+N を作成
        /// </summary>
        public static CardModifier CreateAttackBonus(int bonus)
        {
            return new CardModifier { BonusDamage = bonus };
        }

        /// <summary>
        /// 属性+N を作成（特定属性にのみ有効）
        /// </summary>
        public static CardModifier CreateElementBonus(ElementType requiredElement, int bonus)
        {
            return new CardModifier
            {
                BonusDamage = bonus,
                // TODO: 属性制限の実装
            };
        }

        /// <summary>
        /// 追加効果（スプレッドガン効果など）を作成
        /// </summary>
        public static CardModifier CreateAdditionalEffect(AdditionalEffectType effectType, int power = 0)
        {
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = effectType,
                Power = power
            });
            return modifier;
        }
    }

    /// <summary>
    /// 追加効果の種類
    /// </summary>
    public enum AdditionalEffectType
    {
        /// <summary>なし</summary>
        None,

        /// <summary>スプレッド効果（着弾地点から前後1マスに爆風）</summary>
        Spread,

        /// <summary>貫通効果</summary>
        Penetration,

        /// <summary>多段ヒット効果</summary>
        MultiHit,

        /// <summary>状態異常: 麻痺</summary>
        Paralyze,

        /// <summary>状態異常: 毒</summary>
        Poison,

        /// <summary>パネル変化: Ice</summary>
        PanelIce,

        /// <summary>パネル変化: Magma</summary>
        PanelMagma
    }

    /// <summary>
    /// 追加効果の詳細
    /// </summary>
    public class AdditionalEffect
    {
        /// <summary>効果の種類</summary>
        public AdditionalEffectType EffectType { get; set; }

        /// <summary>効果の威力（ダメージなど）</summary>
        public int Power { get; set; }

        /// <summary>効果の持続時間（秒）</summary>
        public float Duration { get; set; }

        /// <summary>ヒット回数（多段ヒットの場合）</summary>
        public int HitCount { get; set; }
    }
}
