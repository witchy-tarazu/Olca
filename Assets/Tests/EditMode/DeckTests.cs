using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using System.Collections.Generic;

namespace Olca.Tests
{
    /// <summary>
    /// Deckのテスト
    /// </summary>
    public class DeckTests
    {
        private class MockRandomProvider : IRandomProvider
        {
            private int fixedValue;

            public MockRandomProvider(int fixedValue = 0)
            {
                this.fixedValue = fixedValue;
            }

            public int Range(int min, int max)
            {
                return fixedValue;
            }

            public float Range(float min, float max)
            {
                return fixedValue;
            }

            public float Value => 0.5f;
        }

        [Test]
        public void AddCard_カードが追加される()
        {
            // Arrange
            var deck = new Deck();
            var card = new Card("card_01", "テストカード", 3, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, 50, "説明");

            // Act
            deck.AddCard(card);

            // Assert
            Assert.AreEqual(1, deck.TotalCount);
        }

        [Test]
        public void DrawCards_指定枚数ドローできる()
        {
            // Arrange
            var cards = new List<Card>
            {
                new Card("card_01", "カード1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, 10, ""),
                new Card("card_02", "カード2", 2, CardRarity.Normal, CardCategory.Attack, ElementType.None, 20, ""),
                new Card("card_03", "カード3", 3, CardRarity.Normal, CardCategory.Attack, ElementType.None, 30, "")
            };
            var deck = new Deck(cards);
            var random = new MockRandomProvider(0);

            // Act
            var drawnCards = deck.DrawCards(2, random);

            // Assert
            Assert.AreEqual(2, drawnCards.Count);
            Assert.AreEqual(1, deck.RemainingCount); // 3 - 2 = 1
        }

        [Test]
        public void DrawCards_デッキが空になったらドローできない()
        {
            // Arrange
            var cards = new List<Card>
            {
                new Card("card_01", "カード1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, 10, "")
            };
            var deck = new Deck(cards);
            var random = new MockRandomProvider(0);

            // Act
            deck.DrawCards(1, random); // 1枚ドロー
            var secondDraw = deck.DrawCards(1, random); // もう1枚ドローしようとする

            // Assert
            Assert.AreEqual(0, secondDraw.Count); // ドローできない
            Assert.IsTrue(deck.IsEmpty);
        }

        [Test]
        public void ResetDrawHistory_再度ドロー可能になる()
        {
            // Arrange
            var cards = new List<Card>
            {
                new Card("card_01", "カード1", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, 10, "")
            };
            var deck = new Deck(cards);
            var random = new MockRandomProvider(0);

            // Act
            deck.DrawCards(1, random);
            deck.ResetDrawHistory();
            var secondDraw = deck.DrawCards(1, random);

            // Assert
            Assert.AreEqual(1, secondDraw.Count);
        }
    }
}
