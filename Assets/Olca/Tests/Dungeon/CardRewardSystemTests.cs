using NUnit.Framework;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Domain;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class CardRewardSystemTests
    {
        private MockRandomProvider mockRandom;

        [SetUp]
        public void SetUp()
        {
            mockRandom = new MockRandomProvider(0);
        }

        private Deck CreateTestDeck()
        {
            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃")
            };

            return new Deck(cards);
        }

        [Test]
        public void ShouldDropCard_ボスは100パーセントドロップする()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(10, 0));

            // Act
            bool shouldDrop = rewardSystem.ShouldDropCard(boss);

            // Assert
            Assert.IsTrue(shouldDrop, "ボスは必ずカードをドロップする");
        }

        [Test]
        public void GenerateCardChoices_通常敵は3枚から選択()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(5, 0));

            // Act
            var choices = rewardSystem.GenerateCardChoices(enemy);

            // Assert
            Assert.AreEqual(3, choices.Count, "通常敵からは3枚の選択肢");
        }

        [Test]
        public void GenerateCardChoices_ボスは1枚のみ()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(10, 0));

            // Act
            var choices = rewardSystem.GenerateCardChoices(boss);

            // Assert
            Assert.AreEqual(1, choices.Count, "ボスからは1枚の選択肢");
            Assert.AreEqual(CardRarity.Limited, choices[0].Rarity, "ボスからはリミテッドカード");
        }

        [Test]
        public void SelectCard_デッキにカードが追加される()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var newCard = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            int initialDeckSize = rewardSystem.GetCurrentDeck().TotalCount;

            // Act
            rewardSystem.SelectCard(newCard);

            // Assert
            Assert.AreEqual(initialDeckSize + 1, rewardSystem.GetCurrentDeck().TotalCount,
                "デッキに1枚追加されている");
        }

        [Test]
        public void SelectCard_入手カードリストに追加される()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var newCard = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            // Act
            rewardSystem.SelectCard(newCard);

            // Assert
            Assert.AreEqual(1, rewardSystem.ObtainedCards.Count, "入手カードリストに追加されている");
            Assert.AreEqual(newCard, rewardSystem.ObtainedCards[0]);
        }

        [Test]
        public void SelectCard_イベントが発火する()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var newCard = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            bool eventFired = false;
            Card receivedCard = null;

            rewardSystem.OnCardAdded += (card) =>
            {
                eventFired = true;
                receivedCard = card;
            };

            // Act
            rewardSystem.SelectCard(newCard);

            // Assert
            Assert.IsTrue(eventFired, "OnCardAddedイベントが発火した");
            Assert.AreEqual(newCard, receivedCard);
        }
    }
}
