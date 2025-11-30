using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Card
{
    /// <summary>
    /// CustomScreenSystemのテスト
    /// </summary>
    [TestFixture]
    public class CustomScreenSystemTests
    {
        private MockRandomProvider random;
        private Deck deck;
        private CustomScreenSystem customScreen;

        [SetUp]
        public void SetUp()
        {
            random = new MockRandomProvider(0);

            // テスト用カードを作成
            var cards = new List<Card>
            {
                CreateTestCard("card1", "ソード", 3, CardSeries.Sword),
                CreateTestCard("card2", "ワイドソード", 4, CardSeries.Sword),
                CreateTestCard("card3", "ロングソード", 5, CardSeries.Sword),
                CreateTestCard("card4", "ボム", 3, CardSeries.Bomb),
                CreateTestCard("card5", "ヒール", 2, CardSeries.None),
            };

            deck = new Deck(cards);
            customScreen = new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 10);
        }

        private Card CreateTestCard(string id, string name, int cost, CardSeries series)
        {
            return new Card(
                id,
                name,
                cost,
                CardRarity.Normal,
                CardCategory.Attack,
                ElementType.None,
                series,
                10,
                "テストカード"
            );
        }

        [Test]
        public void Open_手札が上限までドローされる()
        {
            // Act
            customScreen.Open(random);

            // Assert
            Assert.IsTrue(customScreen.IsOpen);
            Assert.AreEqual(5, customScreen.CurrentHand.Count);
        }

        [Test]
        public void TrySelectCard_コスト内ならカードを選択できる()
        {
            // Arrange
            customScreen.Open(random);
            var card = customScreen.CurrentHand[0];

            // Act
            bool result = customScreen.TrySelectCard(card);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(1, customScreen.SelectedCards.Count);
            Assert.Contains(card, (System.Collections.ICollection)customScreen.SelectedCards);
        }

        [Test]
        public void TrySelectCard_同名カードは選択できない()
        {
            // Arrange
            var cards = new List<Card>
            {
                CreateTestCard("card1", "ソード", 3, CardSeries.Sword),
                CreateTestCard("card2", "ソード", 3, CardSeries.Sword), // 同名
            };
            deck = new Deck(cards);
            customScreen = new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 10);
            customScreen.Open(random);

            var card1 = customScreen.CurrentHand[0];
            var card2 = customScreen.CurrentHand[1];

            // Act
            customScreen.TrySelectCard(card1);
            bool result = customScreen.TrySelectCard(card2);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(1, customScreen.SelectedCards.Count);
        }

        [Test]
        public void TrySelectCard_系統ボーナスでコストが減る()
        {
            // Arrange
            customScreen.Open(random);

            // ソード系のカードを3枚選択（コスト3, 4, 5 → 3, 3, 4 = 10）
            var sword1 = customScreen.CurrentHand[0]; // ソード (3)
            var sword2 = customScreen.CurrentHand[1]; // ワイドソード (4 → 3)
            var sword3 = customScreen.CurrentHand[2]; // ロングソード (5 → 4)

            // Act
            customScreen.TrySelectCard(sword1);
            int costAfter1 = customScreen.RemainingCost;

            customScreen.TrySelectCard(sword2);
            int costAfter2 = customScreen.RemainingCost;

            customScreen.TrySelectCard(sword3);
            int costAfter3 = customScreen.RemainingCost;

            // Assert
            Assert.AreEqual(7, costAfter1); // 10 - 3 = 7
            Assert.AreEqual(4, costAfter2); // 7 - 3 = 4 (系統ボーナスで-1)
            Assert.AreEqual(0, costAfter3); // 4 - 4 = 0 (系統ボーナスで-1)
        }

        [Test]
        public void TrySelectCard_コストオーバーなら選択できない()
        {
            // Arrange
            customScreen = new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 5);
            customScreen.Open(random);

            var card = CreateTestCard("expensive", "高コスト", 6, CardSeries.None);
            deck.AddCard(card);

            // Act
            bool result = customScreen.TrySelectCard(card);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void Confirm_未選択カードが破棄される()
        {
            // Arrange
            customScreen.Open(random);
            var card = customScreen.CurrentHand[0];
            customScreen.TrySelectCard(card);

            // Act
            customScreen.Confirm();

            // Assert
            Assert.IsFalse(customScreen.IsOpen);
            Assert.AreEqual(0, customScreen.CurrentHand.Count); // 手札はクリア
        }

        [Test]
        public void Cancel_選択カードが手札に戻る()
        {
            // Arrange
            customScreen.Open(random);
            var card = customScreen.CurrentHand[0];
            customScreen.TrySelectCard(card);

            // Act
            customScreen.Cancel();

            // Assert
            Assert.IsFalse(customScreen.IsOpen);
            Assert.AreEqual(0, customScreen.SelectedCards.Count);
        }
    }
}
