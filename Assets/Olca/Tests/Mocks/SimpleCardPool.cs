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
    public class SimpleCardPool : ICardPool
    {
        private readonly List<Card> cards;

        public SimpleCardPool(List<Card> cards)
        {
            this.cards = cards ?? new List<Card>();
        }

        /// <summary>
        /// 指定レアリティのカードをランダムに取得する
        /// 該当レアリティがない場合は全カードから選ぶ
        /// </summary>
        public Card GetRandomCard(CardRarity rarity, IRandomProvider random)
        {
            var filtered = cards.Where(c => c.Rarity == rarity).ToList();
            var pool = filtered.Count > 0 ? filtered : cards;

            if (pool.Count == 0) return null;

            int index = random.Range(0, pool.Count);
            return pool[index];
        }
    }
}
