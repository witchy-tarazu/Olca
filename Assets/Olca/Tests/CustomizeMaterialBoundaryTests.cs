using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Olca.Application.Battle;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests
{
    public class CustomizeMaterialBoundaryTests
    {
        private SimpleCardFactory factory;
        private SimpleFusionRecipeProvider recipes;

        [SetUp]
        public void SetUp()
        {
            factory = new SimpleCardFactory();
            recipes = new SimpleFusionRecipeProvider();
        }

        // DungeonBattleControllerと同じ3サービス・実ファクトリ・実レシピを使用する。
        private CustomScreenSystem Open(params Card[] cards)
        {
            var model = new CustomScreenSystem(new Deck(cards.ToList()),
                exceedSystem: new ExceedSystem(factory), tuningSystem: new TuningSystem(),
                fusionSystem: new FusionSystem(recipes, factory));
            model.Open(new Card[0], new MockRandomProvider(0));
            return model;
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void 超越_同参照素材の実在枚数を確認する(int copies, bool allowed)
        {
            var card = factory.CreateById(SimpleCardFactory.Sword);
            var other = factory.CreateById(SimpleCardFactory.Heal);
            var cards = Enumerable.Repeat(card, copies).Concat(new[] { other }).ToArray();
            var model = Open(cards);
            model.TrySelectCard(other);
            var hand = model.CurrentHand.ToArray();
            var selection = model.SelectedCards.ToArray();
            int events = 0;
            model.OnExceeded += _ => events++;
            var result = model.ExecuteExceed(card, card);
            Assert.AreEqual(allowed, result.IsSuccess);
            Assert.AreEqual(allowed ? 1 : 0, events);
            CollectionAssert.AreEqual(selection, model.SelectedCards);
            if (allowed)
            {
                Assert.AreEqual(1, model.CurrentHand.Count);
                Assert.AreEqual(ExceedLevel.Lv1, model.CurrentHand[0].ExceedLevel);
            }
            else
            {
                Assert.AreEqual("指定カードが手札にありません", result.ErrorMessage);
                CollectionAssert.AreEqual(hand, model.CurrentHand);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 全サービス構成_異段階の同IDを融合候補として許可しない(bool reverse)
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var b = factory.CreateExceeded(a, ExceedLevel.Lv1);
            var bomb = factory.CreateById(SimpleCardFactory.Bomb);
            var model = Open(a, b, bomb);
            var first = reverse ? b : a;
            var second = reverse ? a : b;
            Assert.IsFalse(model.CanExceed(first, second));
            Assert.IsFalse(model.IsCompatibleWithCurrentSelection(new[] { first }, second));
            Assert.IsTrue(model.IsCompatibleWithCurrentSelection(new[] { first }, bomb));
            Assert.IsFalse(model.ExecuteExceed(first, second).IsSuccess);
            Assert.IsFalse(model.ExecuteFusion(new[] { first, second }).IsSuccess);
            CollectionAssert.AreEqual(new[] { a, b, bomb }, model.CurrentHand);
            Assert.AreEqual(0, model.SelectedCards.Count);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        public void 全サービス構成_同段階超越を維持する(ExceedLevel level)
        {
            var original = factory.CreateById(SimpleCardFactory.Sword);
            var a = level == ExceedLevel.Normal ? original : factory.CreateExceeded(original, level);
            var b = level == ExceedLevel.Normal ? factory.CreateById(SimpleCardFactory.Sword) : factory.CreateExceeded(original, level);
            var model = Open(a, b);
            Assert.IsTrue(model.IsCompatibleWithCurrentSelection(new[] { a }, b));
            var result = model.ExecuteExceed(a, b);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual((ExceedLevel)((int)level + 1), result.ResultCard.ExceedLevel);
            Assert.AreEqual(1, model.CurrentHand.Count);
        }

        [Test]
        public void 全サービス構成_上限段階2枚を融合候補にしない()
        {
            var card = factory.CreateExceeded(factory.CreateById(SimpleCardFactory.Sword), ExceedLevel.Lv3);
            var other = factory.CreateExceeded(factory.CreateById(SimpleCardFactory.Sword), ExceedLevel.Lv3);
            var model = Open(card, other);
            Assert.IsFalse(model.IsCompatibleWithCurrentSelection(new[] { card }, other));
            Assert.IsFalse(model.ExecuteExceed(card, other).IsSuccess);
            Assert.AreEqual(2, model.CurrentHand.Count);
        }

        [Test]
        public void 融合候補_部分一致でも必要枚数を超えない()
        {
            Assert.IsTrue(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Sword }));
            Assert.IsTrue(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Bomb }));
            Assert.IsTrue(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Bomb, SimpleCardFactory.Sword }));
            Assert.IsFalse(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Sword, SimpleCardFactory.Sword }));
            Assert.IsFalse(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Bomb, SimpleCardFactory.Bomb }));
            Assert.IsFalse(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Sword, SimpleCardFactory.Bomb, SimpleCardFactory.Sword }));
            Assert.IsFalse(recipes.HasRecipeContaining(null));
            Assert.IsFalse(recipes.HasRecipeContaining(new string[0]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 全サービス構成_正規融合は素材順によらず成功する(bool reverse)
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var b = factory.CreateById(SimpleCardFactory.Bomb);
            var model = Open(a, b);
            var materials = reverse ? new[] { b, a } : new[] { a, b };
            Assert.IsTrue(model.IsCompatibleWithCurrentSelection(new[] { materials[0] }, materials[1]));
            var result = model.ExecuteFusion(materials);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(SimpleCardFactory.FireSword, result.ResultCard.Id);
            CollectionAssert.AreEqual(new[] { result.ResultCard }, model.CurrentHand);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 融合_余分な同ID素材を消費せず拒否する(bool reverse)
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var b = factory.CreateById(SimpleCardFactory.Sword);
            var bomb = factory.CreateById(SimpleCardFactory.Bomb);
            var model = Open(a, b, bomb);
            var materials = reverse ? new[] { bomb, a, b } : new[] { a, b, bomb };
            Assert.IsNull(recipes.FindResultCardId(materials.Select(c => c.Id).ToArray()));
            Assert.IsFalse(model.ExecuteFusion(materials).IsSuccess);
            CollectionAssert.AreEqual(new[] { a, b, bomb }, model.CurrentHand);
        }
    }
}
