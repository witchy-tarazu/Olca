using System.Collections.Generic;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests.CardTests
{
    /// <summary>
    /// FusionSystemのユニットテスト
    /// </summary>
    [TestFixture]
    public class FusionSystemTests
    {
        private MockCardFactory cardFactory;
        private MockFusionRecipeProvider recipeProvider;
        private FusionSystem fusionSystem;

        private static Card MakeCard(string id)
        {
            return new Card(id, id + "Card", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 10, "");
        }

        [SetUp]
        public void SetUp()
        {
            cardFactory = new MockCardFactory();
            recipeProvider = new MockFusionRecipeProvider();
            fusionSystem = new FusionSystem(recipeProvider, cardFactory);

            // 結果カードを登録
            cardFactory.Register("dragon", new Card("dragon", "ドラゴン", 5, CardRarity.Normal,
                CardCategory.Attack, ElementType.Fire, CardSeries.None, 50, ""));
        }

        // ===== Validate =====

        [Test]
        public void Validate_正常ケース_成功()
        {
            recipeProvider.Register("dragon", "sword", "shield");
            var materials = new List<Card> { MakeCard("sword"), MakeCard("shield") };

            var result = fusionSystem.Validate(materials);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual("dragon", result.ResultCardId);
        }

        [Test]
        public void Validate_素材1枚_失敗()
        {
            var result = fusionSystem.Validate(new List<Card> { MakeCard("sword") });

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("2枚以上", result.ErrorMessage);
        }

        [Test]
        public void Validate_null素材リスト_失敗()
        {
            var result = fusionSystem.Validate(null);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Validate_nullカード含む_失敗()
        {
            var materials = new List<Card> { MakeCard("sword"), null };

            var result = fusionSystem.Validate(materials);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("null", result.ErrorMessage);
        }

        [Test]
        public void Validate_レシピ不一致_失敗()
        {
            var materials = new List<Card> { MakeCard("sword"), MakeCard("bomb") };

            var result = fusionSystem.Validate(materials);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("レシピ", result.ErrorMessage);
        }

        [Test]
        public void Validate_素材順序不問_成功()
        {
            recipeProvider.Register("dragon", "sword", "shield");
            // 順序を逆に
            var materials = new List<Card> { MakeCard("shield"), MakeCard("sword") };

            var result = fusionSystem.Validate(materials);

            Assert.IsTrue(result.IsValid);
        }

        // ===== Execute =====

        [Test]
        public void Execute_融合カードを返す()
        {
            recipeProvider.Register("dragon", "sword", "shield");
            var materials = new List<Card> { MakeCard("sword"), MakeCard("shield") };

            var result = fusionSystem.Execute(materials);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.ResultCard);
            Assert.AreEqual("dragon", result.ResultCard.Id);
        }

        [Test]
        public void Execute_バリデーション失敗時はエラー()
        {
            var materials = new List<Card> { MakeCard("sword"), MakeCard("unknown") };

            var result = fusionSystem.Execute(materials);

            Assert.IsFalse(result.IsSuccess);
        }

        [Test]
        public void Execute_FactoryでカードNotFound_失敗()
        {
            recipeProvider.Register("nonexistent", "sword", "shield");
            // "nonexistent" をfactoryに登録しない
            var materials = new List<Card> { MakeCard("sword"), MakeCard("shield") };

            var result = fusionSystem.Execute(materials);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("生成に失敗", result.ErrorMessage);
        }

        [Test]
        public void Execute_3素材の融合()
        {
            var triCard = new Card("tri", "トリカード", 6, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.None, 80, "");
            cardFactory.Register("tri", triCard);
            recipeProvider.Register("tri", "a", "b", "c");

            var materials = new List<Card> { MakeCard("a"), MakeCard("b"), MakeCard("c") };

            var result = fusionSystem.Execute(materials);

            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual("tri", result.ResultCard.Id);
        }

        [Test]
        public void Execute_FactoryNull_例外()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new FusionSystem(recipeProvider, null));
        }

        [Test]
        public void Execute_RecipeProviderNull_例外()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new FusionSystem(null, cardFactory));
        }
    }
}
