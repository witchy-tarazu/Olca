using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Tests.Mocks
{
    /// <summary>
    /// テスト用のシンプルなカードプール
    /// 指定されたカードリストからランダムに取得する
    /// </summary>
    public class SimpleCardPool : IWeightedCardPool
    {
        private readonly List<Card> cards;
        private readonly System.Func<Card, int> weight;

        public SimpleCardPool(List<Card> cards, System.Func<Card, int> weight = null)
        {
            this.cards = cards ?? new List<Card>();
            this.weight = weight;
        }

        /// <summary>
        /// 指定レアリティのカードをランダムに取得する
        /// 該当レアリティがない場合は全カードから選ぶ
        /// </summary>
        public Card GetRandomCard(CardRarity rarity, IRandomProvider random)
        {
            var pool = GetCandidates(rarity);

            if (pool.Count == 0) return null;

            int index = random.Range(0, pool.Count);
            return pool[index];
        }
        public int GetWeight(Card card) => weight?.Invoke(card) ?? 1;

        public IReadOnlyList<Card> GetCandidates(CardRarity rarity)
        {
            var filtered = cards.Where(c => c != null && c.Rarity == rarity).ToList();
            return filtered.Count > 0 ? filtered : cards;
        }
    }
}
