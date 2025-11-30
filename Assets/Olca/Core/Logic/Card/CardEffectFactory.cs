using System;
using System.Collections.Generic;

namespace Olca.Core.Logic.Card
{
    /// <summary>
    /// カード効果のファクトリ
    /// カードIDから対応する効果を生成
    /// </summary>
    public class CardEffectFactory
    {
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
