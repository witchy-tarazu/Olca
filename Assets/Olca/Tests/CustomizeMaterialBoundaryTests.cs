using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Olca.Presentation.CustomScreen;
using Olca.Presentation.CustomScreen.CustomizeMode;
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

        [TestCase(false)]
        [TestCase(true)]
        public void 全サービス構成_三枚目の超越調律候補を拒否し二枚融合の表示と実行を維持する(bool tuning)
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var bomb = factory.CreateById(SimpleCardFactory.Bomb);
            var third = tuning
                ? new Card("tuning", "調律", 1, CardRarity.Normal, CardCategory.Tuning, ElementType.None, CardSeries.None, 1, "検証")
                : factory.CreateById(SimpleCardFactory.Sword);
            var model = Open(a, bomb, third);
            Assert.IsFalse(model.IsCompatibleWithCurrentSelection(new[] { a, bomb }, third));
            WithView(model, (view, mode, button, selected) =>
            {
                view.OnCardClicked(a);
                Assert.IsFalse(button.interactable);
                view.OnCardClicked(bomb);
                Assert.IsTrue(button.interactable);
                view.OnCardClicked(third);
                CollectionAssert.AreEqual(new[] { a, bomb }, selected);
                Assert.IsTrue(button.interactable, "元の有効な二枚融合は実行可能");
                Assert.AreEqual("Fusion", typeof(CustomScreenView).GetMethod("DetectCustomizeOperation", Flags).Invoke(view, null).ToString());
                typeof(CustomScreenView).GetMethod("OnCustomizeExecuteRequested", Flags).Invoke(view, null);
                Assert.AreEqual(2, model.CurrentHand.Count);
                Assert.IsTrue(model.CurrentHand.Contains(third));
                Assert.IsTrue(model.CurrentHand.Any(c => c.Id == SimpleCardFactory.FireSword));
            });
        }

        [Test]
        public void 三枚融合レシピ_部分一致の候補を維持し完全一致でのみ実行できる()
        {
            // 実レシピ提供者の三枚レシピ対応を検証するためのテスト用データ。
            var data = (List<(List<string> materials, string resultId)>)typeof(SimpleFusionRecipeProvider)
                .GetField("recipes", Flags).GetValue(recipes);
            data.Clear();
            data.Add((new List<string> { SimpleCardFactory.Sword, SimpleCardFactory.Bomb, SimpleCardFactory.Heal }, SimpleCardFactory.FireSword));
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var bomb = factory.CreateById(SimpleCardFactory.Bomb);
            var heal = factory.CreateById(SimpleCardFactory.Heal);
            var model = Open(a, bomb, heal);
            Assert.IsTrue(model.IsCompatibleWithCurrentSelection(new[] { a, bomb }, heal));
            WithView(model, (view, mode, button, selected) =>
            {
                view.OnCardClicked(a);
                view.OnCardClicked(bomb);
                Assert.AreEqual(2, selected.Count);
                Assert.IsFalse(button.interactable, "部分一致の二枚では未完成");
                view.OnCardClicked(heal);
                Assert.AreEqual(3, selected.Count);
                Assert.IsTrue(button.interactable);
                typeof(CustomScreenView).GetMethod("OnCustomizeExecuteRequested", Flags).Invoke(view, null);
                Assert.AreEqual(1, model.CurrentHand.Count);
                Assert.AreEqual(SimpleCardFactory.FireSword, model.CurrentHand[0].Id);
            });
        }

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void WithView(CustomScreenSystem model,
            System.Action<CustomScreenView, CustomizeModeView, Button, List<Card>> check)
        {
            var root = new GameObject("CustomizeBoundaryTest", typeof(RectTransform));
            var modeObject = new GameObject("Mode", typeof(RectTransform));
            modeObject.transform.SetParent(root.transform);
            try
            {
                var view = root.AddComponent<CustomScreenView>();
                var mode = modeObject.AddComponent<CustomizeModeView>();
                var button = modeObject.AddComponent<Button>();
                typeof(CustomizeModeView).GetField("executeButton", Flags).SetValue(mode, button);
                typeof(CustomScreenView).GetField("customScreen", Flags).SetValue(view, model);
                typeof(CustomScreenView).GetField("customizeModeView", Flags).SetValue(view, mode);
                mode.Enter();
                var selected = (List<Card>)typeof(CustomScreenView).GetField("customizeSelectedCards", Flags).GetValue(view);
                check(view, mode, button, selected);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
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
