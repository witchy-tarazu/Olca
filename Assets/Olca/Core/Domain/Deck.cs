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
        /// デッキ全体からランダムにカードを破棄する（ドロー済みカードも対象）。
        /// 要求枚数が総数を超える場合は、存在する枚数まで破棄する。
        /// </summary>
        public int DiscardRandomCards(int count, IRandomProvider random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (count < 0) throw new ArgumentException("破棄枚数は0以上である必要があります", nameof(count));

            int discardCount = Math.Min(count, cards.Count);
            for (int i = 0; i < discardCount; i++)
            {
                int index = random.Range(0, cards.Count);
                Card discardedCard = cards[index];
                cards.RemoveAt(index);
                drawnCards.Remove(discardedCard);
            }

            return discardCount;
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
        /// 検証済みチェックポイントの初回手札をドロー済みとして復元する
        /// </summary>
        internal void RestoreCheckpointOrder(IEnumerable<Card> order, IEnumerable<Card> firstHand)
        {
            var restored = order.ToList();
            var hand = firstHand.ToList();
            if (restored.Any(c => c == null) || restored.Distinct().Count() != restored.Count || hand.Any(c => !restored.Contains(c)))
                throw new ArgumentException("保存デッキの個体参照が不正です");
            cards.Clear(); cards.AddRange(restored);
            RestoreCheckpointDrawn(hand);
        }

        internal void RestoreCheckpointDrawn(IEnumerable<Card> firstHand)
        {
            var restored = firstHand.ToList();
            if (restored.Distinct().Count() != restored.Count || restored.Any(card => !cards.Contains(card)))
                throw new ArgumentException("初回手札とデッキ個体が一致しません");
            drawnCards.Clear();
            drawnCards.AddRange(restored);
        }

        /// <summary>ドロー履歴をリセット（再度すべてのカードをドロー可能にする）</summary>
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
