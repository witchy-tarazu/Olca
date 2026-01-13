using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.CardTests
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
                CreateTestCard("card6", "シールド", 2, CardSeries.None),
            };

            deck = new Deck(cards);
            customScreen = new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 10);
        }

        // Open()メソッド用の空リストヘルパー
        private List<Card> EmptyRemainingCards() => new List<Card>();

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
            customScreen.Open(EmptyRemainingCards(), random);

            // Assert
            Assert.IsTrue(customScreen.IsOpen);
            Assert.AreEqual(5, customScreen.CurrentHand.Count);
        }

        [Test]
        public void TrySelectCard_コスト内ならカードを選択できる()
        {
            // Arrange
            customScreen.Open(EmptyRemainingCards(), random);
            var card = customScreen.CurrentHand[0];

            // Act
            bool result = customScreen.TrySelectCard(card);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(1, customScreen.SelectedCards.Count);
            Assert.Contains(card, (System.Collections.ICollection)customScreen.SelectedCards);
        }

        [Test]
        public void TrySelectCard_系統ボーナスでコストが減る()
        {
            // Arrange
            customScreen.Open(EmptyRemainingCards(), random);

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
            customScreen.Open(EmptyRemainingCards(), random);

            var card = CreateTestCard("expensive", "高コスト", 6, CardSeries.None);
            deck.AddCard(card);

            // Act
            bool result = customScreen.TrySelectCard(card);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void Confirm_未選択カードは次回も手札に残る()
        {
            // Arrange: 初回Open（5枚ドロー）
            customScreen.Open(EmptyRemainingCards(), random);
            var card = customScreen.CurrentHand[0];
            customScreen.TrySelectCard(card);

            // Act: 1回目の確定（1枚選択）
            customScreen.Confirm();

            // Assert: 確定直後は未選択の4枚が手札に残っている
            Assert.IsFalse(customScreen.IsOpen);
            Assert.AreEqual(4, customScreen.CurrentHand.Count);

            // Act: 2回目のOpen
            customScreen.Open(EmptyRemainingCards(), random);

            // Assert: 前回の未選択カード4枚 + 新規ドロー1枚 = 5枚
            // （仕様: 選択しなかったカードは次回も手札に残り、手札上限まで追加ドロー）
            Assert.AreEqual(5, customScreen.CurrentHand.Count);
        }

        [Test]
        public void Cancel_選択カードが手札に戻る()
        {
            // Arrange
            customScreen.Open(EmptyRemainingCards(), random);
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
