using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Interfaces;

namespace Olca.Core.Domain
{
    /// <summary>
    /// デッキ
    /// </summary>
    public class Deck
    {
        private readonly List<Card> cards;
        private readonly List<Card> drawnCards; // 既にドローしたカード

        /// <summary>デッキ内のカード総数</summary>
        public int TotalCount => cards.Count;

        /// <summary>まだドローしていないカードの枚数</summary>
        public int RemainingCount => cards.Count - drawnCards.Count;

        /// <summary>すべてドローしたか</summary>
        public bool IsEmpty => RemainingCount == 0;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="initialCards">初期カードリスト</param>
        public Deck(List<Card> initialCards = null)
        {
            cards = initialCards != null ? new List<Card>(initialCards) : new List<Card>();
            drawnCards = new List<Card>();
        }

        /// <summary>
        /// カードを追加
        /// </summary>
        public void AddCard(Card card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            cards.Add(card);
        }

        /// <summary>
        /// 複数のカードを追加
        /// </summary>
        public void AddCards(IEnumerable<Card> cardsToAdd)
        {
            if (cardsToAdd == null) throw new ArgumentNullException(nameof(cardsToAdd));
            cards.AddRange(cardsToAdd);
        }

        /// <summary>
        /// カードをランダムにドローする
        /// </summary>
        /// <param name="count">ドローする枚数</param>
        /// <param name="random">乱数プロバイダー</param>
        /// <returns>ドローしたカードのリスト</returns>
        public List<Card> DrawCards(int count, IRandomProvider random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (count < 0) throw new ArgumentException("ドロー枚数は0以上である必要があります", nameof(count));

            var result = new List<Card>();

            // まだドローしていないカードを取得
            var availableCards = cards.Where(c => !drawnCards.Contains(c)).ToList();

            // ドロー枚数を調整（残りカード数を超えない）
            int actualDrawCount = Math.Min(count, availableCards.Count);

            for (int i = 0; i < actualDrawCount; i++)
            {
                // ランダムにカードを選択
                int index = random.Range(0, availableCards.Count);
                Card drawnCard = availableCards[index];

                result.Add(drawnCard);
                drawnCards.Add(drawnCard);
                availableCards.RemoveAt(index);
            }

            return result;
        }

        /// <summary>
        /// デッキをシャッフル（ドロー履歴をリセット）
        /// </summary>
        public void Shuffle(IRandomProvider random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            drawnCards.Clear();

            // Fisher-Yatesシャッフル
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = random.Range(0, i + 1);
                var temp = cards[i];
                cards[i] = cards[j];
                cards[j] = temp;
            }
        }

        /// <summary>
        /// ドロー履歴をリセット（再度すべてのカードをドロー可能にする）
        /// </summary>
        public void ResetDrawHistory()
        {
            drawnCards.Clear();
        }

        /// <summary>
        /// デッキ内のすべてのカードを取得（読み取り専用）
        /// </summary>
        public IReadOnlyList<Card> GetAllCards()
        {
            return cards.AsReadOnly();
        }

        /// <summary>
        /// まだドローしていないカードを取得（読み取り専用）
        /// </summary>
        public IReadOnlyList<Card> GetRemainingCards()
        {
            return cards.Where(c => !drawnCards.Contains(c)).ToList().AsReadOnly();
        }
    }
}
