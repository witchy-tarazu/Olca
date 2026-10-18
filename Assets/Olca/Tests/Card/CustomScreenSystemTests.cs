using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;
using System.Linq;

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
            customScreen.Close(); // アニメーション完了を模擬

            // Assert: 閉じた後は未選択の4枚が手札に残っている
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

        private static object[][] SeriesCostCases => new[]
        {
            new object[] { CardSeries.Sword, CardSeries.Sword, CardSeries.Sword, 7 },
            new object[] { CardSeries.Sword, CardSeries.Bomb, CardSeries.Sword, 9 },
            new object[] { CardSeries.None, CardSeries.None, CardSeries.None, 7 },
            new object[] { CardSeries.None, CardSeries.Bomb, CardSeries.None, 8 }
        };

        [TestCaseSource(nameof(SeriesCostCases))]
        public void 選択コスト_連続系列のみ割引しNoneは既存扱いを維持する(
            CardSeries first, CardSeries second, CardSeries third, int expected)
        {
            var cards = new[] {
                CreateTestCard("a", "A", 3, first), CreateTestCard("b", "B", 3, second),
                CreateTestCard("c", "C", 3, third)
            };
            var cs = new CustomScreenSystem(new Deck(cards.ToList()));
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in cards) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.AreEqual(expected, cs.GetTotalUsedCost());
            Assert.AreEqual(cs.MaxCost - expected, cs.RemainingCost);
            List<Card> confirmed = null;
            cs.OnConfirmed += result => confirmed = new List<Card>(result);
            cs.Confirm();
            cs.Close();
            CollectionAssert.AreEqual(cards, confirmed);
            CollectionAssert.AreEqual(cards, cs.AvailableCards);
        }

        [TestCase(8, false)]
        [TestCase(9, true)]
        public void 非連続の選択可否_割引なしの合計と一致する(int limit, bool allowed)
        {
            var a = CreateTestCard("a", "A", 3, CardSeries.Sword);
            var b = CreateTestCard("b", "B", 3, CardSeries.Bomb);
            var c = CreateTestCard("c", "C", 3, CardSeries.Sword);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: limit);
            cs.Open(EmptyRemainingCards(), random);
            Assert.IsTrue(cs.TrySelectCard(a));
            Assert.IsTrue(cs.TrySelectCard(b));
            Assert.AreEqual(allowed, cs.TrySelectCard(c));
            Assert.AreEqual(allowed ? 9 : 6, cs.GetTotalUsedCost());
            Assert.AreEqual(!allowed, cs.CurrentHand.Contains(c));
            Assert.AreEqual(allowed ? 3 : 2, cs.SelectedCards.Count);
        }

        [TestCase(0, 6, 8)]
        [TestCase(1, 5, 8)]
        [TestCase(2, 6, 9)]
        public void 選択解除と再選択_現在の順序で割引を再計算する(int removedIndex, int afterRemoval, int afterReselect)
        {
            var cards = new[] {
                CreateTestCard("a", "A", 3, CardSeries.Sword),
                CreateTestCard("b", "B", 3, CardSeries.Bomb),
                CreateTestCard("c", "C", 3, CardSeries.Sword)
            };
            var cs = new CustomScreenSystem(new Deck(cards.ToList()));
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in cards) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.IsTrue(cs.TryDeselectCard(cards[removedIndex]));
            Assert.AreEqual(afterRemoval, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.TrySelectCard(cards[removedIndex]));
            Assert.AreEqual(afterReselect, cs.GetTotalUsedCost());
            Assert.AreEqual(10 - afterReselect, cs.RemainingCost);
            Assert.AreSame(cards[removedIndex], cs.SelectedCards.Last());
        }

        [Test]
        public void 連続選択_割引で上限まで選択でき負のコストにしない()
        {
            var cards = new[] {
                CreateTestCard("a", "A", 3, CardSeries.Sword),
                CreateTestCard("b", "B", 3, CardSeries.Sword),
                CreateTestCard("c", "C", 0, CardSeries.Sword)
            };
            var cs = new CustomScreenSystem(new Deck(cards.ToList()), maxCost: 5);
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in cards) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.AreEqual(5, cs.GetTotalUsedCost());
            Assert.AreEqual(0, cs.RemainingCost);
        }

        [Test]
        public void 中間解除で割引が消えて上限超過_修正するまで確定しない()
        {
            var a = CreateTestCard("a", "A", 3, CardSeries.Sword);
            var b = CreateTestCard("b", "B", 0, CardSeries.Bomb);
            var c = CreateTestCard("c", "C", 3, CardSeries.Bomb);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: 5);
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in new[] { a, b, c }) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.AreEqual(5, cs.GetTotalUsedCost());
            cs.TryDeselectCard(b);
            Assert.AreEqual(6, cs.GetTotalUsedCost());
            Assert.IsFalse(cs.CanConfirm());
            int confirmed = 0;
            cs.OnConfirmed += _ => confirmed++;
            cs.Confirm();
            Assert.AreEqual(0, confirmed);
            Assert.AreEqual(2, cs.SelectedCards.Count);
            Assert.IsTrue(cs.IsOpen);
            cs.TryDeselectCard(c);
            Assert.IsTrue(cs.CanConfirm());
            cs.Confirm();
            Assert.AreEqual(1, confirmed);
            CollectionAssert.AreEqual(new[] { a }, cs.AvailableCards);
        }

        // ===== カスタマイズ操作（超越・調律・融合）=====

        private CustomScreenSystem CreateCustomScreenWithSystems()
        {
            var mockFactory = new MockCardFactory();
            var mockRecipe = new MockFusionRecipeProvider();
            var exceed = new ExceedSystem(mockFactory);
            var tuning = new TuningSystem();
            var fusion = new FusionSystem(mockRecipe, mockFactory);
            return new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 10,
                exceedSystem: exceed, tuningSystem: tuning, fusionSystem: fusion);
        }

        [Test]
        public void ExecuteExceed_システム未設定時_エラー()
        {
            customScreen.Open(EmptyRemainingCards(), random);
            var cardA = customScreen.CurrentHand[0];
            var cardB = customScreen.CurrentHand[1];

            var result = customScreen.ExecuteExceed(cardA, cardB);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("超越システム", result.ErrorMessage);
        }

        [Test]
        public void ExecuteExceed_同名カード2枚_手札が変化する()
        {
            var cs = CreateCustomScreenWithSystems();
            // 同名カードを2枚手札に持つデッキを作成
            var sameCards = new List<Card>
            {
                CreateTestCard("sword", "ソード", 2, CardSeries.Sword),
                CreateTestCard("sword", "ソード", 2, CardSeries.Sword),
            };
            var testDeck = new Deck(sameCards);
            var mockFactory = new MockCardFactory();
            var cs2 = new CustomScreenSystem(testDeck, maxHandSize: 5, maxCost: 10,
                exceedSystem: new ExceedSystem(mockFactory));
            cs2.Open(EmptyRemainingCards(), random);

            var handBefore = cs2.CurrentHand.Count;
            var cardA = cs2.CurrentHand[0];
            var cardB = cs2.CurrentHand[1];

            var result = cs2.ExecuteExceed(cardA, cardB);

            Assert.IsTrue(result.IsSuccess);
            // 素材2枚→超越カード1枚（手札-1）
            Assert.AreEqual(handBefore - 1, cs2.CurrentHand.Count);
            Assert.IsTrue(cs2.CurrentHand.Contains(result.ResultCard));
        }

        [Test]
        public void ExecuteExceed_手札にないカード_エラー()
        {
            var cs = CreateCustomScreenWithSystems();
            cs.Open(EmptyRemainingCards(), random);
            var outsideCard = CreateTestCard("outside", "外のカード", 1, CardSeries.None);

            var result = cs.ExecuteExceed(outsideCard, cs.CurrentHand[0]);

            Assert.IsFalse(result.IsSuccess);
        }

        [Test]
        public void ExecuteTuning_システム未設定時_エラー()
        {
            customScreen.Open(EmptyRemainingCards(), random);
            var target = customScreen.CurrentHand[0];
            var tuning = customScreen.CurrentHand[1];

            var result = customScreen.ExecuteTuning(target, tuning);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("調律システム", result.ErrorMessage);
        }

        [Test]
        public void ExecuteTuning_調律カード消費_対象カード強化()
        {
            // 調律カードと攻撃カードを含むデッキ
            var modifier = new CardModifier { BonusDamage = 5 };
            var tuningCard = new Card("t1", "チューナー", 1, CardRarity.Normal, CardCategory.Tuning,
                ElementType.None, CardSeries.None, 0, "", modifier: modifier);
            var attackCard = CreateTestCard("atk", "アタック", 2, CardSeries.Sword);

            var testCards = new List<Card> { attackCard, tuningCard };
            var testDeck = new Deck(testCards);
            var cs = new CustomScreenSystem(testDeck, maxHandSize: 5, maxCost: 10,
                tuningSystem: new TuningSystem());
            cs.Open(EmptyRemainingCards(), random);

            var handBefore = cs.CurrentHand.Count;
            var result = cs.ExecuteTuning(cs.CurrentHand[0], cs.CurrentHand[1]);

            Assert.IsTrue(result.IsSuccess);
            // 調律カードが消費されて手札-1
            Assert.AreEqual(handBefore - 1, cs.CurrentHand.Count);
            // 強化されたカードが手札にある
            Assert.IsTrue(cs.CurrentHand.Contains(result.ResultCard));
        }

        [Test]
        public void ExecuteFusion_システム未設定時_エラー()
        {
            customScreen.Open(EmptyRemainingCards(), random);
            var materials = new List<Card> { customScreen.CurrentHand[0], customScreen.CurrentHand[1] };

            var result = customScreen.ExecuteFusion(materials);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("融合システム", result.ErrorMessage);
        }

        [Test]
        public void ExecuteFusion_素材2枚_手札が変化する()
        {
            var mockFactory = new MockCardFactory();
            var resultCard = new Card("dragon", "ドラゴン", 5, CardRarity.Normal,
                CardCategory.Attack, ElementType.Fire, CardSeries.None, 50, "");
            mockFactory.Register("dragon", resultCard);

            var mockRecipe = new MockFusionRecipeProvider();
            mockRecipe.Register("dragon", "card1", "card2");

            var cs = new CustomScreenSystem(deck, maxHandSize: 5, maxCost: 10,
                fusionSystem: new FusionSystem(mockRecipe, mockFactory));
            cs.Open(EmptyRemainingCards(), random);

            var handBefore = cs.CurrentHand.Count;
            var mat1 = cs.CurrentHand.First(c => c.Id == "card1");
            var mat2 = cs.CurrentHand.First(c => c.Id == "card2");

            var result = cs.ExecuteFusion(new List<Card> { mat1, mat2 });

            Assert.IsTrue(result.IsSuccess);
            // 素材2枚→融合カード1枚（手札-1）
            Assert.AreEqual(handBefore - 1, cs.CurrentHand.Count);
            Assert.IsTrue(cs.CurrentHand.Contains(result.ResultCard));
        }
    }
}
