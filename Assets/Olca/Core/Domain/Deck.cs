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

namespace Olca.Core.Domain
{
    /// <summary>
    /// 所持カードからの新規出撃だけに適用する共通構築制限。
    /// 非装備デッキの保存、攻略中の増減、チェックポイント復元では呼び出さない。
    /// </summary>
    public static class DepartureDeckRules
    {
        public static bool TryCreate(IReadOnlyList<Card> cards,
            IReadOnlyDictionary<(string Id, ExceedLevel Level), int> ownedCounts,
            out Deck deck, out string error)
        {
            deck = null; error = null;
            if (cards == null || ownedCounts == null) { error = "デッキと所持枚数が必要です"; return false; }
            if (cards.Count != 20) { error = "出撃デッキは20枚必要です"; return false; }
            if (cards.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) ||
                !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.ExceedLevel)))
            { error = "カード定義が不正です"; return false; }
            if (cards.GroupBy(c => c.Id).Any(g => g.Select(c => c.Rarity).Distinct().Count() != 1))
            { error = "同じカードIDのレギュレーションが一致しません"; return false; }
            foreach (var group in cards.GroupBy(c => (c.Id, c.ExceedLevel)))
            {
                int count = group.Count();
                if (!ownedCounts.TryGetValue(group.Key, out int owned) || owned < count)
                { error = "登録枚数に対して所持枚数が不足しています"; return false; }
                int limit = group.First().Rarity == CardRarity.Normal ? 4 : 1;
                if (count > limit) { error = "同一カードの構築上限を超えています"; return false; }
            }
            if (cards.Count(c => c.Rarity == CardRarity.Limited) > 5 || cards.Count(c => c.Rarity == CardRarity.Ultimate) > 1)
            { error = "レギュレーションの合計上限を超えています"; return false; }
            // 所持テンプレートを繰り返し指定しても、攻略中は一枚ずつ別個体にする。
            deck = new Deck(cards.Select(c => c.Clone()).ToList());
            return true;
        }
    }
}


namespace Olca.Core.Domain
{
    /// <summary>攻略中の獲得履歴に対する選択検証。永続所持への付与や換算は行わない。</summary>
    public static class CarryHomeSelectionRules
    {
        public static bool TryValidate(IReadOnlyList<Card> obtained, IReadOnlyList<Card> selected,
            out IReadOnlyList<Card> validated, out string error)
        {
            validated = null; error = null;
            if (obtained == null || selected == null) { error = "獲得履歴と選択が必要です"; return false; }
            if (selected.Count > 3) { error = "持ち帰りは最大3枚です"; return false; }
            var result = selected.ToArray();
            if (result.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) ||
                !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.ExceedLevel)))
            { error = "カード定義が不正です"; return false; }
            // 同名の持込カードや別攻略・復帰前の個体を混ぜず、現在の履歴の個体だけを受け取る。
            if (result.Any(c => !obtained.Any(candidate => ReferenceEquals(candidate, c))))
            { error = "攻略中に獲得したカードだけを選択できます"; return false; }
            if (result.Any(c => c.ExceedLevel == ExceedLevel.Lv3))
            { error = "★★★カードは持ち帰れません"; return false; }
            // 出撃デッキの既存識別と同じく、レベル違いは別カードとして扱う。
            if (result.GroupBy(c => (c.Id, c.ExceedLevel)).Any(g => g.Count() > 1))
            { error = "同じカードは1枚までです"; return false; }
            validated = Array.AsReadOnly(result);
            return true;
        }
    }
}
