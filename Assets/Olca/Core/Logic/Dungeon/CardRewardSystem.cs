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
        private readonly List<Card> obtainedCards;  // 入手したカード（クリア報酬用）

        private const int REWARD_CHOICE_COUNT = 3;  // 選択肢の枚数
        private const float NORMAL_ENEMY_DROP_RATE = 0.6f;  // 通常敵のドロップ率（60%）

        public CardRewardSystem(Deck initialDeck, IRandomProvider random)
        {
            // 初期デッキをコピー（元のデッキは変更しない）
            dungeonDeck = new Deck(new List<Card>(initialDeck.GetAllCards()));
            this.random = random;
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

            // 通常敵は60%の確率でドロップ
            float roll = random.Range(0f, 1f);
            return roll < NORMAL_ENEMY_DROP_RATE;
        }

        /// <summary>
        /// カード選択肢を生成
        /// </summary>
        public List<Card> GenerateCardChoices(Enemy defeatedEnemy)
        {
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
        /// カードを生成（仮実装）
        /// </summary>
        private Card GenerateCard(CardRarity targetRarity)
        {
            // TODO: CardMasterから適切なカードを取得
            // 今回は仮実装として、ダミーカードを生成

            string[] cardIds = { "sword", "heal", "shield", "fire", "ice" };
            int randomIndex = random.Range(0, cardIds.Length);
            string cardId = cardIds[randomIndex];

            return new Card(
                id: cardId,
                name: $"{cardId}_card",
                cost: 1,
                rarity: targetRarity,
                category: CardCategory.Attack,
                element: ElementType.None,
                series: CardSeries.Sword,
                power: 30,
                description: "Test card"
            );
        }

        /// <summary>
        /// カードを選択してデッキに追加
        /// </summary>
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
