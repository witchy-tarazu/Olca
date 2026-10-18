using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// カード報酬管理システム
    /// バトル後のカードドロップと選択を管理
    /// </summary>
    public class CardRewardSystem
    {
        private readonly Deck dungeonDeck;  // 攻略中のデッキ
        private readonly IRandomProvider random;
        private readonly ICardPool cardPool;  // カードプール（nullの場合はフォールバック生成）
        private readonly List<Card> obtainedCards;  // 入手したカード（クリア報酬用）

        private const int REWARD_CHOICE_COUNT = 3;  // 選択肢の枚数
        private const float NORMAL_ENEMY_DROP_RATE = 0.5f;  // 雑魚戦の基本ドロップ率（50%）

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="initialDeck">初期デッキ（コピーして管理）</param>
        /// <param name="random">乱数プロバイダー</param>
        /// <param name="cardPool">カードプール（省略時はダミーカードを生成）</param>
        public CardRewardSystem(Deck initialDeck, IRandomProvider random, ICardPool cardPool = null)
        {
            // 初期デッキをコピー（元のデッキは変更しない）
            dungeonDeck = new Deck(new List<Card>(initialDeck.GetAllCards()));
            this.random = random;
            this.cardPool = cardPool;
            obtainedCards = new List<Card>();
        }

        /// <summary>
        /// カードドロップ判定
        /// </summary>
        public bool ShouldDropCard(Enemy defeatedEnemy)
        {
            if (defeatedEnemy == null) return false;

            // ボスは100%ドロップ
            if (defeatedEnemy.EnemyId == "boss" || defeatedEnemy.EnemyId.Contains("boss"))
            {
                return true;
            }

            // 雑魚戦は戦闘ごとに1回、基本50%の確率でドロップ
            float roll = random.Range(0f, 1f);
            return roll < NORMAL_ENEMY_DROP_RATE;
        }

        /// <summary>
        /// カード選択肢を生成
        /// </summary>
        public List<Card> GenerateCardChoices(Enemy defeatedEnemy)
        {
            if (cardPool != null)
            {
                if (TryGenerateConfiguredChoices(defeatedEnemy, out var configured)) return configured;
                throw new InvalidOperationException("報酬プールの有効候補が不足しています。通常は異なる3候補、ボスは1候補以上が必要です。");
            }
            var choices = new List<Card>();

            // ボスの場合は1枚のみ（高レアリティ）
            if (defeatedEnemy.EnemyId == "boss" || defeatedEnemy.EnemyId.Contains("boss"))
            {
                Card bossCard = GenerateCard(CardRarity.Limited);  // ボスからはリミテッド
                choices.Add(bossCard);
            }
            // 通常敵の場合は3枚（ノーマル中心）
            else
            {
                for (int i = 0; i < REWARD_CHOICE_COUNT; i++)
                {
                    Card card = GenerateCard(CardRarity.Normal);
                    choices.Add(card);
                }
            }

            return choices;
        }

        /// <summary>
        /// 設定済み候補から重複なしで抽選する。候補不足時は乱数を消費せず失敗する。
        /// 現行の均等抽選基盤。製品プールの重み付き抽選は別途設定が必要。
        /// </summary>
        public bool TryGenerateConfiguredChoices(Enemy defeatedEnemy, out List<Card> choices)
        {
            choices = new List<Card>();
            if (cardPool == null || defeatedEnemy == null) return false;
            bool boss = defeatedEnemy.EnemyId == "boss" || defeatedEnemy.EnemyId.Contains("boss");
            var definitions = cardPool.GetCandidates(boss ? CardRarity.Limited : CardRarity.Normal);
            var candidates = new List<Card>();
            var seen = new HashSet<(string, ExceedLevel)>();
            if (definitions != null)
            {
                foreach (var card in definitions)
                {
                    if (card != null && !string.IsNullOrEmpty(card.Id) && seen.Add((card.Id, card.ExceedLevel)))
                        candidates.Add(card);
                }
            }
            if (candidates.Count < (boss ? 1 : REWARD_CHOICE_COUNT)) return false;
            int count = boss ? Math.Min(REWARD_CHOICE_COUNT, candidates.Count) : REWARD_CHOICE_COUNT;
            for (int i = 0; i < count; i++)
            {
                int index = random.Range(0, candidates.Count);
                choices.Add(candidates[index].Clone());
                candidates.RemoveAt(index);
            }
            return true;
        }

        /// <summary>
        /// カードを生成する
        /// カードプールが設定されている場合はそこから取得し、なければフォールバックカードを生成する
        /// </summary>
        private Card GenerateCard(CardRarity targetRarity)
        {
            // フォールバック: カードプール未設定時はダミーカードを生成
            string[] cardIds = { "sword", "heal", "shield", "fire", "ice" };
            int randomIndex = random.Range(0, cardIds.Length);
            string cardId = cardIds[randomIndex];

            return CreateFallbackCard(cardId, targetRarity);
        }

        public static Card CreateFallbackCard(string cardId, CardRarity targetRarity)
        {
            return new Card(
                id: cardId,
                name: $"{cardId}_card",
                cost: 1,
                rarity: targetRarity,
                category: CardCategory.Attack,
                element: ElementType.None,
                series: CardSeries.Sword,
                power: 30,
                description: "Fallback card"
            );
        }

        /// <summary>
        /// カードを選択してデッキに追加
        /// </summary>
        internal void RestoreObtained(IEnumerable<Card> history)
        {
            obtainedCards.Clear(); obtainedCards.AddRange(history);
        }

        public void SelectCard(Card selectedCard)
        {
            if (selectedCard == null)
            {
                throw new ArgumentNullException(nameof(selectedCard));
            }

            dungeonDeck.AddCard(selectedCard);
            obtainedCards.Add(selectedCard);

            OnCardAdded?.Invoke(selectedCard);
        }

        /// <summary>
        /// 攻略中のデッキを取得
        /// </summary>
        public Deck GetCurrentDeck() => dungeonDeck;

        /// <summary>
        /// 入手したカードのリスト（クリア報酬用）
        /// </summary>
        public IReadOnlyList<Card> ObtainedCards => obtainedCards.AsReadOnly();

        /// <summary>
        /// カード追加イベント
        /// </summary>
        public event Action<Card> OnCardAdded;
    }
}
