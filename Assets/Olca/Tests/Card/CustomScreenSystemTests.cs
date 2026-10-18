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

        // ゲーム全体仕様第5節: 使用順の変更は末尾からの取り消しで行う。
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, true)]
        public void 選択解除_末尾だけ成功し拒否時は順序コスト手札イベントを変更しない(int index, bool allowed)
        {
            var cards = new[] {
                CreateTestCard("a", "A", 3, CardSeries.Sword),
                CreateTestCard("b", "B", 3, CardSeries.Bomb),
                CreateTestCard("c", "C", 3, CardSeries.Sword)
            };
            var cs = new CustomScreenSystem(new Deck(cards.ToList()));
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in cards) Assert.IsTrue(cs.TrySelectCard(card));
            var hand = cs.CurrentHand.ToArray();
            int deselected = 0, selected = 0, failed = 0;
            cs.OnCardDeselected += _ => deselected++;
            cs.OnCardSelected += _ => selected++;
            cs.OnSelectionFailed += _ => failed++;
            Assert.AreEqual(allowed, cs.TryDeselectCard(cards[index]));
            Assert.AreEqual(allowed ? 6 : 9, cs.GetTotalUsedCost());
            Assert.AreEqual(allowed ? 1 : 0, deselected);
            Assert.AreEqual(0, selected);
            Assert.AreEqual(0, failed);
            if (!allowed)
            {
                CollectionAssert.AreEqual(cards, cs.SelectedCards);
                CollectionAssert.AreEqual(hand, cs.CurrentHand);
            }
            else
            {
                Assert.IsTrue(cs.TrySelectCard(cards[index]));
                CollectionAssert.AreEqual(cards, cs.SelectedCards);
                Assert.AreEqual(9, cs.GetTotalUsedCost());
            }
        }

        [Test]
        public void 末尾解除を繰り返すと全解除でき同参照の先頭を取り違えない()
        {
            var a = CreateTestCard("a", "A", 3, CardSeries.Sword);
            var b = CreateTestCard("b", "B", 3, CardSeries.Bomb);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, a }));
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in new[] { a, b, a }) Assert.IsTrue(cs.TrySelectCard(card));
            var events = new List<Card>();
            cs.OnCardDeselected += card => events.Add(card);
            Assert.IsTrue(cs.TryDeselectCard(a));
            CollectionAssert.AreEqual(new[] { a, b }, cs.SelectedCards);
            Assert.AreEqual(6, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.TryDeselectCard(b));
            Assert.IsTrue(cs.TryDeselectCard(a));
            Assert.IsFalse(cs.TryDeselectCard(a));
            Assert.AreEqual(0, cs.SelectedCards.Count);
            Assert.AreEqual(0, cs.GetTotalUsedCost());
            Assert.AreEqual(10, cs.RemainingCost);
            CollectionAssert.AreEqual(new[] { a, b, a }, events);
            Assert.AreEqual(3, cs.CurrentHand.Count);
        }

        [Test]
        public void 連続選択_割引で上限まで選択でき割引後も最低一とする()
        {
            var cards = new[] {
                CreateTestCard("a", "A", 3, CardSeries.Sword),
                CreateTestCard("b", "B", 3, CardSeries.Sword),
                CreateTestCard("c", "C", 0, CardSeries.Sword)
            };
            var cs = new CustomScreenSystem(new Deck(cards.ToList()), maxCost: 6);
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in cards) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.AreEqual(6, cs.GetTotalUsedCost());
            Assert.AreEqual(0, cs.RemainingCost);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void 連続割引_候補と合計は最低一で予算超過を拒否する(int baseCost)
        {
            var a = CreateTestCard("a", "A", 1, CardSeries.Sword);
            var b = CreateTestCard("b", "B", baseCost, CardSeries.Sword);
            var c = CreateTestCard("c", "C", baseCost, CardSeries.Sword);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: 2);
            cs.Open(EmptyRemainingCards(), random);
            Assert.IsTrue(cs.TrySelectCard(a));
            Assert.IsTrue(cs.TrySelectCard(b));
            Assert.AreEqual(2, cs.GetTotalUsedCost());
            Assert.AreEqual(0, cs.RemainingCost);
            Assert.IsFalse(cs.TrySelectCard(c));
            CollectionAssert.AreEqual(new[] { a, b }, cs.SelectedCards);
        }

        [Test]
        public void 中間解除を拒否し末尾から選び直した時だけ予算を再判定する()
        {
            var a = CreateTestCard("a", "A", 3, CardSeries.Sword);
            var b = CreateTestCard("b", "B", 0, CardSeries.Bomb);
            var c = CreateTestCard("c", "C", 3, CardSeries.Bomb);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: 5);
            cs.Open(EmptyRemainingCards(), random);
            foreach (var card in new[] { a, b, c }) Assert.IsTrue(cs.TrySelectCard(card));
            Assert.IsFalse(cs.TryDeselectCard(b));
            Assert.AreEqual(5, cs.GetTotalUsedCost());
            Assert.IsTrue(cs.CanConfirm());
            Assert.IsTrue(cs.TryDeselectCard(c));
            Assert.IsTrue(cs.TryDeselectCard(b));
            Assert.IsFalse(cs.TrySelectCard(c), "系統割引がなくなり3+3で予算超過");
            Assert.IsTrue(cs.TrySelectCard(b));
            Assert.IsTrue(cs.TrySelectCard(c));
            cs.Confirm();
            CollectionAssert.AreEqual(new[] { a, b, c }, cs.AvailableCards);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 異段階超越_両順序で手札と選択を変更せず候補にしない(bool reverse)
        {
            var a = new Card("sword", "ソード", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "", exceedLevel: ExceedLevel.Normal);
            var b = new Card("sword", "ソード★", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 20, "", exceedLevel: ExceedLevel.Lv1);
            var selected = CreateTestCard("other", "選択済み", 1, CardSeries.Bomb);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { a, b, selected }), exceedSystem: new ExceedSystem(new MockCardFactory()));
            cs.Open(EmptyRemainingCards(), random);
            cs.TrySelectCard(selected);
            var hand = cs.CurrentHand.ToArray();
            var selection = cs.SelectedCards.ToArray();
            var first = reverse ? b : a;
            var second = reverse ? a : b;
            int events = 0;
            cs.OnExceeded += _ => events++;
            Assert.IsFalse(cs.CanExceed(first, second));
            Assert.IsFalse(cs.IsCompatibleWithCurrentSelection(new[] { first }, second));
            var result = cs.ExecuteExceed(first, second);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual("同じ超越段階のカード2枚が必要です", result.ErrorMessage);
            CollectionAssert.AreEqual(hand, cs.CurrentHand);
            CollectionAssert.AreEqual(selection, cs.SelectedCards);
            Assert.AreEqual(0, events);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv3)]
        public void 同段階同参照の2枚_上限以外は候補と実行が一致する(ExceedLevel level)
        {
            var card = new Card("sword", "ソード", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "", exceedLevel: level);
            var cs = new CustomScreenSystem(new Deck(new List<Card> { card, card }), exceedSystem: new ExceedSystem(new MockCardFactory()));
            cs.Open(EmptyRemainingCards(), random);
            Assert.AreEqual(2, cs.CurrentHand.Count);
            bool allowed = level < ExceedLevel.Lv3;
            Assert.AreEqual(allowed, cs.CanExceed(card, card));
            Assert.AreEqual(allowed, cs.IsCompatibleWithCurrentSelection(new[] { card }, card));
            var result = cs.ExecuteExceed(card, card);
            Assert.AreEqual(allowed, result.IsSuccess);
            Assert.AreEqual(allowed ? 1 : 2, cs.CurrentHand.Count);
            if (allowed) Assert.AreEqual((ExceedLevel)((int)level + 1), cs.CurrentHand[0].ExceedLevel);
            else StringAssert.Contains("最上位", result.ErrorMessage);
            Assert.IsFalse(cs.CanExceed(null, card));
            Assert.IsFalse(cs.CanExceed(card, null));
            Assert.IsFalse(cs.IsCompatibleWithCurrentSelection(new[] { card }, null));
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
