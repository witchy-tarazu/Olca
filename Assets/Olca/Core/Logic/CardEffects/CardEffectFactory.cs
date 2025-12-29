using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.CardEffects.Effects;

namespace Olca.Core.Logic.CardEffects
{
    /// <summary>
    /// カード効果のファクトリ
    /// カードIDから対応する効果を生成
    /// </summary>
    public class CardEffectFactory
    {
        private static readonly Dictionary<string, Func<ICardEffect>> staticEffectCreators
            = new Dictionary<string, Func<ICardEffect>>();

        private readonly Dictionary<string, Func<ICardEffect>> effectCreators;

        public CardEffectFactory()
        {
            effectCreators = new Dictionary<string, Func<ICardEffect>>();
        }

        /// <summary>
        /// カード効果を登録
        /// </summary>
        public void RegisterEffect(string cardId, Func<ICardEffect> creator)
        {
            if (string.IsNullOrEmpty(cardId))
                throw new ArgumentException("カードIDが無効です", nameof(cardId));
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));

            effectCreators[cardId] = creator;
        }

        /// <summary>
        /// カード効果を生成
        /// </summary>
        public ICardEffect CreateEffect(string cardId)
        {
            if (effectCreators.TryGetValue(cardId, out var creator))
            {
                return creator();
            }

            // 登録されていない場合はデフォルト効果（何もしない）
            return new NullEffect();
        }

        /// <summary>
        /// staticメソッド: カード効果を登録
        /// </summary>
        public static void RegisterEffectStatic(string cardId, Func<ICardEffect> creator)
        {
            if (string.IsNullOrEmpty(cardId))
                throw new ArgumentException("カードIDが無効です", nameof(cardId));
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));

            staticEffectCreators[cardId] = creator;
        }

        /// <summary>
        /// staticメソッド: カードから効果を生成
        /// </summary>
        public static ICardEffect CreateEffect(Card card)
        {
            if (card == null) return new NullEffect();

            // 登録済みの効果を検索
            if (staticEffectCreators.TryGetValue(card.Id, out var creator))
            {
                return creator();
            }

            // カテゴリに基づくデフォルト効果
            // ModifiableDamageEffectを使用することで、調律効果（Modifier）が自動的に適用される
            switch (card.Category)
            {
                case CardCategory.Attack:
                    // 攻撃カードは ModifiableDamageEffect を使用
                    // Modifier の追加効果（スプレッド、貫通、多段ヒット等）が自動適用される
                    return new ModifiableDamageEffect(DamageTargetType.Single);

                case CardCategory.Defense:
                    // 回復カードは HealEffect を使用
                    return new HealEffect(card.Power);

                case CardCategory.Field:
                    // フィールド操作カードも ModifiableDamageEffect で対応
                    // PanelIce, PanelMagma 等の効果は Modifier で指定
                    return new ModifiableDamageEffect(DamageTargetType.Single);

                case CardCategory.Support:
                    // サポートカードは単独では何もしない（調律用）
                    return new NullEffect();

                default:
                    return new NullEffect();
            }
        }

        /// <summary>
        /// 何もしない効果（デフォルト）
        /// </summary>
        private class NullEffect : ICardEffect
        {
            public void Execute(BattleContext context)
            {
                // 何もしない
            }
        }
    }
}
