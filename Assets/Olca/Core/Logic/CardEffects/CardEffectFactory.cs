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
            switch (card.Category)
            {
                case CardCategory.Attack:
                    return new DamageEffect(card.Power, DamageTargetType.Single);

                case CardCategory.Defense:
                    return new HealEffect(card.Power);

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
