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
    /// <summary>
    /// 追加効果の種類
    /// </summary>
    public enum AdditionalEffectType
    {
        /// <summary>なし</summary>
        None,

        // ========== 基本的な攻撃効果 ==========
        /// <summary>スプレッド効果（着弾地点から前後1マスに爆風）【ヒット時派生】</summary>
        Spread,

        /// <summary>貫通効果</summary>
        Penetration,

        /// <summary>多段ヒット効果</summary>
        MultiHit,

        // ========== 状態異常（ダメージ時派生） ==========
        /// <summary>状態異常: 麻痺（ダメージを与えた敵を麻痺状態にする）【ダメージ時派生】</summary>
        Paralyze,

        /// <summary>状態異常: 毒（ダメージを与えた敵を毒状態にする）【ダメージ時派生】</summary>
        Poison,

        /// <summary>状態異常: 混乱（ダメージを与えた敵を混乱状態にする）【ダメージ時派生】</summary>
        Confusion,

        /// <summary>状態異常: 盲目（ダメージを与えた敵を盲目状態にする）【ダメージ時派生】</summary>
        Blind,

        // ========== パネル変化（対象のパネルのみ） ==========
        /// <summary>パネル変化: Ice</summary>
        PanelIce,

        /// <summary>パネル変化: Magma</summary>
        PanelMagma,

        /// <summary>パネル変化: Grass</summary>
        PanelGrass,

        /// <summary>パネル変化: Cracked（ヒビ割れ）</summary>
        PanelCracked,

        /// <summary>パネル変化: Hole（穴）</summary>
        PanelHole,

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
        PanelHoleAll,

        // ========== チェイン効果 ==========
        /// <summary>別のカードを連鎖発動（ChainCardIdで指定されたカードを実行）</summary>
        ChainCard
    }

    /// <summary>
    /// 追加効果の詳細
    /// </summary>
    /// <summary>
    /// 追加効果の詳細（派生効果を含む）
    /// </summary>
    public class AdditionalEffect
    {
        /// <summary>効果の種類</summary>
        public AdditionalEffectType EffectType { get; set; }

        /// <summary>効果の威力（ダメージなど）</summary>
        public int Power { get; set; }

        /// <summary>効果の持続時間（秒）- 状態異常の場合に使用</summary>
        public float Duration { get; set; }

        /// <summary>ヒット回数（多段ヒットの場合）</summary>
        public int HitCount { get; set; }

        /// <summary>
        /// 派生効果のトリガータイミング
        /// ヒット時に派生するか、ダメージ時に派生するかを指定
        /// </summary>
        public DerivativeTrigger Trigger { get; set; }

        /// <summary>
        /// チェイン発動するカードID
        /// EffectTypeがChainCardの場合に使用
        /// null の場合は自分自身を再発動
        /// </summary>
        public string ChainCardId { get; set; }

        /// <summary>
        /// チェイン効果の発動位置
        /// EffectTypeがChainCardの場合に使用
        /// </summary>
        public ChainEffectOrigin EffectOrigin { get; set; }
    }

    /// <summary>
    /// 派生効果のトリガータイミング
    /// </summary>
    public enum DerivativeTrigger
    {
        /// <summary>即座に発動（派生なし）</summary>
        Immediate,

        /// <summary>ヒット時に即座に派生（例: スプレッドガン→爆風）</summary>
        OnHit,

        /// <summary>ダメージを与えた時に即座に派生（例: ポイズンアロー→毒状態）</summary>
        OnDamage,

        /// <summary>行動終了時にヒット条件を満たしていれば派生（例: フレイムボム→全体マグマ化）</summary>
        AfterActionIfHit,

        /// <summary>行動終了時にダメージ条件を満たしていれば派生（例: アイスバースト→氷パネル化）</summary>
        AfterActionIfDamaged
    }

    /// <summary>
    /// チェイン効果の発動位置
    /// </summary>
    public enum ChainEffectOrigin
    {
        /// <summary>プレイヤー位置から発動（例: 三段突き - プレイヤー位置から連続攻撃）</summary>
        PlayerPosition,

        /// <summary>ヒット位置から発動（例: スプレッドガン - 着弾地点から爆風）</summary>
        HitPosition
    }
}
