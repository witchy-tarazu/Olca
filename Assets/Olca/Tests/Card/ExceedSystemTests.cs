using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests.CardTests
{
    /// <summary>
    /// ExceedSystemのユニットテスト
    /// </summary>
    [TestFixture]
    public class ExceedSystemTests
    {
        private MockCardFactory cardFactory;
        private ExceedSystem exceedSystem;

        // テスト用カードヘルパー
        private static Card MakeCard(string id, ExceedLevel level = ExceedLevel.Normal)
        {
            return new Card(id, id + "Card", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 10, "", exceedLevel: level);
        }

        [SetUp]
        public void SetUp()
        {
            cardFactory = new MockCardFactory();
            exceedSystem = new ExceedSystem(cardFactory);
        }

        // ===== Validate =====

        [Test]
        public void Validate_同名カード2枚_成功()
        {
            var cardA = MakeCard("sword");
            var cardB = MakeCard("sword");

            var result = exceedSystem.Validate(cardA, cardB);

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_異なるID_失敗()
        {
            var cardA = MakeCard("sword");
            var cardB = MakeCard("shield");

            var result = exceedSystem.Validate(cardA, cardB);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("同名カード", result.ErrorMessage);
        }

        [Test]
        public void Validate_nullカード_失敗()
        {
            var result = exceedSystem.Validate(null, MakeCard("sword"));

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Validate_Lv3カード_失敗()
        {
            var cardA = MakeCard("sword", ExceedLevel.Lv3);
            var cardB = MakeCard("sword", ExceedLevel.Lv3);

            var result = exceedSystem.Validate(cardA, cardB);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("最上位", result.ErrorMessage);
        }

        [Test]
        public void Validate_Lv2カード_成功()
        {
            var cardA = MakeCard("sword", ExceedLevel.Lv2);
            var cardB = MakeCard("sword", ExceedLevel.Lv2);

            var result = exceedSystem.Validate(cardA, cardB);

            Assert.IsTrue(result.IsValid);
        }

        [TestCase(ExceedLevel.Normal, ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Normal, ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Normal, ExceedLevel.Lv3)]
        [TestCase(ExceedLevel.Lv1, ExceedLevel.Lv2)]
        [TestCase(ExceedLevel.Lv1, ExceedLevel.Lv3)]
        [TestCase(ExceedLevel.Lv2, ExceedLevel.Lv3)]
        public void 異段階_両順序とも同じエラーで拒否する(ExceedLevel first, ExceedLevel second)
        {
            var a = MakeCard("sword", first);
            var b = MakeCard("sword", second);
            foreach (var pair in new[] { new[] { a, b }, new[] { b, a } })
            {
                var validation = exceedSystem.Validate(pair[0], pair[1]);
                var result = exceedSystem.Execute(pair[0], pair[1]);
                Assert.IsFalse(validation.IsValid);
                Assert.IsFalse(result.IsSuccess);
                Assert.IsNull(result.ResultCard);
                Assert.AreEqual("同じ超越段階のカード2枚が必要です", validation.ErrorMessage);
                Assert.AreEqual(validation.ErrorMessage, result.ErrorMessage);
            }
            Assert.AreEqual(first, a.ExceedLevel);
            Assert.AreEqual(second, b.ExceedLevel);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        public void 同段階_素材順で生成段階とIDが変わらない(ExceedLevel level)
        {
            var a = MakeCard("sword", level);
            var b = MakeCard("sword", level);
            var ab = exceedSystem.Execute(a, b);
            var ba = exceedSystem.Execute(b, a);
            Assert.IsTrue(ab.IsSuccess);
            Assert.IsTrue(ba.IsSuccess);
            Assert.AreEqual((ExceedLevel)((int)level + 1), ab.ResultCard.ExceedLevel);
            Assert.AreEqual(ab.ResultCard.ExceedLevel, ba.ResultCard.ExceedLevel);
            Assert.AreEqual(ab.ResultCard.Id, ba.ResultCard.Id);
        }

        [Test]
        public void null素材_どちらの順序でも同じエラーを返す()
        {
            var card = MakeCard("sword");
            foreach (var pair in new[] { new[] { card, null }, new[] { null, card }, new Card[] { null, null } })
            {
                var result = exceedSystem.Execute(pair[0], pair[1]);
                Assert.IsFalse(result.IsSuccess);
                Assert.AreEqual("カードがnullです", result.ErrorMessage);
                Assert.IsNull(result.ResultCard);
            }
        }

        // ===== Execute =====

        [Test]
        public void Execute_Normal_Lv1カードが生成される()
        {
            var cardA = MakeCard("sword", ExceedLevel.Normal);
            var cardB = MakeCard("sword", ExceedLevel.Normal);

            var result = exceedSystem.Execute(cardA, cardB);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.ResultCard);
            Assert.AreEqual(ExceedLevel.Lv1, result.ResultCard.ExceedLevel);
        }

        [Test]
        public void Execute_Lv1_Lv2カードが生成される()
        {
            var cardA = MakeCard("sword", ExceedLevel.Lv1);
            var cardB = MakeCard("sword", ExceedLevel.Lv1);

            var result = exceedSystem.Execute(cardA, cardB);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(ExceedLevel.Lv2, result.ResultCard.ExceedLevel);
        }

        [Test]
        public void Execute_Lv2_Lv3カードが生成される()
        {
            var cardA = MakeCard("sword", ExceedLevel.Lv2);
            var cardB = MakeCard("sword", ExceedLevel.Lv2);

            var result = exceedSystem.Execute(cardA, cardB);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(ExceedLevel.Lv3, result.ResultCard.ExceedLevel);
        }

        [Test]
        public void Execute_Lv3_失敗()
        {
            var cardA = MakeCard("sword", ExceedLevel.Lv3);
            var cardB = MakeCard("sword", ExceedLevel.Lv3);

            var result = exceedSystem.Execute(cardA, cardB);

            Assert.IsFalse(result.IsSuccess);
        }

        [Test]
        public void Execute_登録済みカードが返る()
        {
            var exceededCard = new Card("sword_lv1", "ソードLv1", 3, CardRarity.Normal,
                CardCategory.Attack, ElementType.Fire, CardSeries.Sword, 25, "",
                exceedLevel: ExceedLevel.Lv1);
            cardFactory.Register("sword_lv1", exceededCard);

            var cardA = MakeCard("sword", ExceedLevel.Normal);
            var cardB = MakeCard("sword", ExceedLevel.Normal);

            var result = exceedSystem.Execute(cardA, cardB);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("sword_lv1", result.ResultCard.Id);
            Assert.AreEqual(25, result.ResultCard.Power);
        }

        [Test]
        public void Execute_FactoryNull時_失敗()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ExceedSystem(null));
        }
    }
}
