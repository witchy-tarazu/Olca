using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;
using Card = Olca.Core.Domain.Card;

namespace Olca.Tests.Card
{
    /// <summary>
    /// CardUsageSystemのテスト
    /// </summary>
    [TestFixture]
    public class CardUsageSystemTests
    {
        private BattleSystem battleSystem;
        private CardUsageSystem cardUsage;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            logger = new MockLogger();

            var player = new PlayerCharacter(
                name: "テストプレイヤー",
                maxHp: 100,
                attackPower: 10
            );

            var enemies = new List<Enemy>
            {
                new Enemy("test_enemy", "テスト敵", 50, ElementType.None, new Vector2Int(4, 0))
            };

            var deck = new Deck();

            battleSystem = new BattleSystem(player, enemies, deck, logger: logger);
            cardUsage = battleSystem.CardUsage;
        }

        private Card CreateTestCard(string id, string name, int cost = 3)
        {
            return new Card(
                id,
                name,
                cost,
                CardRarity.Normal,
                CardCategory.Attack,
                ElementType.None,
                CardSeries.None,
                10,
                "テストカード"
            );
        }

        [Test]
        public void EnqueueCards_カードがキューに追加される()
        {
            // Arrange
            var cards = new List<Card>
            {
                CreateTestCard("card1", "カード1"),
                CreateTestCard("card2", "カード2"),
                CreateTestCard("card3", "カード3"),
            };

            // Act
            cardUsage.EnqueueCards(cards);

            // Assert
            Assert.AreEqual(3, cardUsage.RemainingCount);
            Assert.AreEqual(cards[0], cardUsage.CurrentCard);
        }

        [Test]
        public void TryUseCurrentCard_カードを順番に使用できる()
        {
            // Arrange
            var cards = new List<Card>
            {
                CreateTestCard("card1", "カード1"),
                CreateTestCard("card2", "カード2"),
            };
            cardUsage.EnqueueCards(cards);

            // Act
            bool result1 = cardUsage.TryUseCurrentCard();
            var currentAfter1 = cardUsage.CurrentCard;

            bool result2 = cardUsage.TryUseCurrentCard();

            // Assert
            Assert.IsTrue(result1);
            Assert.AreEqual(cards[1], currentAfter1);

            Assert.IsTrue(result2);
            Assert.IsTrue(cardUsage.IsEmpty);
        }

        [Test]
        public void TryUseCurrentCard_キューが空なら使用できない()
        {
            // Act
            bool result = cardUsage.TryUseCurrentCard();

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void SkipCurrentCard_カードをスキップできる()
        {
            // Arrange
            var cards = new List<Card>
            {
                CreateTestCard("card1", "カード1"),
                CreateTestCard("card2", "カード2"),
            };
            cardUsage.EnqueueCards(cards);

            // Act
            cardUsage.SkipCurrentCard();

            // Assert
            Assert.AreEqual(cards[1], cardUsage.CurrentCard);
            Assert.AreEqual(1, cardUsage.RemainingCount);
        }

        [Test]
        public void Clear_キューがクリアされる()
        {
            // Arrange
            var cards = new List<Card>
            {
                CreateTestCard("card1", "カード1"),
                CreateTestCard("card2", "カード2"),
            };
            cardUsage.EnqueueCards(cards);

            // Act
            cardUsage.Clear();

            // Assert
            Assert.IsTrue(cardUsage.IsEmpty);
            Assert.AreEqual(0, cardUsage.RemainingCount);
        }

        [Test]
        public void OnCardUsed_カード使用時にイベントが発火する()
        {
            // Arrange
            var cards = new List<Card>
            {
                CreateTestCard("card1", "カード1"),
            };
            cardUsage.EnqueueCards(cards);

            Card usedCard = null;
            cardUsage.OnCardUsed += card => usedCard = card;

            // Act
            cardUsage.TryUseCurrentCard();

            // Assert
            Assert.IsNotNull(usedCard);
            Assert.AreEqual(cards[0], usedCard);
        }
    }
}
