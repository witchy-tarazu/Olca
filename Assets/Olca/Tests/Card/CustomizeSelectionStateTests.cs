using System.Collections.Generic;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;

namespace Olca.Tests.CardTests
{
    [TestFixture]
    public class ExceedSelectionStateTests
    {
        private Card CreateCard(string id, ExceedLevel level = ExceedLevel.Normal)
        {
            return new Card(id, "テストカード", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 100, "テスト", exceedLevel: level);
        }

        [Test]
        public void TryToggle_1枚選択でMaterialAが設定される()
        {
            // Arrange
            var state = new ExceedSelectionState();
            var card = CreateCard("sword");

            // Act
            bool result = state.TryToggle(card);

            // Assert
            Assert.IsTrue(result);
            Assert.AreEqual(card, state.MaterialA);
            Assert.IsNull(state.MaterialB);
            Assert.IsFalse(state.IsReady);
        }

        [Test]
        public void TryToggle_2枚選択でIsReadyがtrueになる()
        {
            // Arrange
            var state = new ExceedSelectionState();
            var cardA = CreateCard("sword");
            var cardB = CreateCard("sword");

            // Act
            state.TryToggle(cardA);
            bool result = state.TryToggle(cardB);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(state.IsReady);
        }

        [Test]
        public void TryToggle_2枚選択後は3枚目を追加できない()
        {
            // Arrange
            var state = new ExceedSelectionState();
            var cardC = CreateCard("sword");

            state.TryToggle(CreateCard("sword"));
            state.TryToggle(CreateCard("sword"));

            // Act
            bool result = state.TryToggle(cardC);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void TryToggle_選択済みカードを再度トグルすると解除される()
        {
            // Arrange
            var state = new ExceedSelectionState();
            var card = CreateCard("sword");

            state.TryToggle(card);

            // Act
            state.TryToggle(card);

            // Assert
            Assert.IsNull(state.MaterialA);
            Assert.IsFalse(state.IsReady);
        }

        [Test]
        public void Reset_選択状態がクリアされる()
        {
            // Arrange
            var state = new ExceedSelectionState();
            state.TryToggle(CreateCard("sword"));
            state.TryToggle(CreateCard("sword"));

            // Act
            state.Reset();

            // Assert
            Assert.IsNull(state.MaterialA);
            Assert.IsNull(state.MaterialB);
            Assert.IsFalse(state.IsReady);
        }

        [Test]
        public void HasExceedablePairs_同名カードが2枚ある手札はtrue()
        {
            // Arrange
            var hand = new List<Card>
            {
                CreateCard("sword"),
                CreateCard("sword"),
                CreateCard("cannon"),
            };

            // Act & Assert
            Assert.IsTrue(ExceedSelectionState.HasExceedablePairs(hand));
        }

        [Test]
        public void HasExceedablePairs_同名カードがない手札はfalse()
        {
            // Arrange
            var hand = new List<Card>
            {
                CreateCard("sword"),
                CreateCard("cannon"),
                CreateCard("bomb"),
            };

            // Act & Assert
            Assert.IsFalse(ExceedSelectionState.HasExceedablePairs(hand));
        }

        [Test]
        public void HasExceedablePairs_nullまたは空はfalse()
        {
            Assert.IsFalse(ExceedSelectionState.HasExceedablePairs(null));
            Assert.IsFalse(ExceedSelectionState.HasExceedablePairs(new List<Card>()));
        }
    }

    [TestFixture]
    public class TuningSelectionStateTests
    {
        private Card CreateCard(string id, CardCategory category = CardCategory.Attack)
        {
            return new Card(id, "テストカード", 1, CardRarity.Normal, category,
                ElementType.None, CardSeries.None, 100, "テスト");
        }

        [Test]
        public void TryToggle_調律カードはTuningCardスロットに設定される()
        {
            // Arrange
            var state = new TuningSelectionState();
            var tuningCard = CreateCard("tuning_1", CardCategory.Tuning);

            // Act
            state.TryToggle(tuningCard);

            // Assert
            Assert.AreEqual(tuningCard, state.TuningCard);
            Assert.IsNull(state.TargetCard);
        }

        [Test]
        public void TryToggle_通常カードはTargetCardスロットに設定される()
        {
            // Arrange
            var state = new TuningSelectionState();
            var attackCard = CreateCard("sword", CardCategory.Attack);

            // Act
            state.TryToggle(attackCard);

            // Assert
            Assert.AreEqual(attackCard, state.TargetCard);
            Assert.IsNull(state.TuningCard);
        }

        [Test]
        public void TryToggle_対象と調律カードが揃うとIsReadyがtrueになる()
        {
            // Arrange
            var state = new TuningSelectionState();

            // Act
            state.TryToggle(CreateCard("sword", CardCategory.Attack));
            state.TryToggle(CreateCard("tuning_1", CardCategory.Tuning));

            // Assert
            Assert.IsTrue(state.IsReady);
        }

        [Test]
        public void TryToggle_選択済みカードを再度トグルすると解除される()
        {
            // Arrange
            var state = new TuningSelectionState();
            var card = CreateCard("sword");

            state.TryToggle(card);

            // Act
            state.TryToggle(card);

            // Assert
            Assert.IsNull(state.TargetCard);
        }

        [Test]
        public void IsTuningTarget_調律対象カードはtrue()
        {
            // Arrange
            var state = new TuningSelectionState();
            var target = CreateCard("sword", CardCategory.Attack);
            state.TryToggle(target);

            // Act & Assert
            Assert.IsTrue(state.IsTuningTarget(target));
            Assert.IsFalse(state.IsTuningMaterial(target));
        }

        [Test]
        public void IsTuningMaterial_調律素材カードはtrue()
        {
            // Arrange
            var state = new TuningSelectionState();
            var tuning = CreateCard("tuning_1", CardCategory.Tuning);
            state.TryToggle(tuning);

            // Act & Assert
            Assert.IsTrue(state.IsTuningMaterial(tuning));
            Assert.IsFalse(state.IsTuningTarget(tuning));
        }

        [Test]
        public void Reset_選択状態がクリアされる()
        {
            // Arrange
            var state = new TuningSelectionState();
            state.TryToggle(CreateCard("sword", CardCategory.Attack));
            state.TryToggle(CreateCard("tuning_1", CardCategory.Tuning));

            // Act
            state.Reset();

            // Assert
            Assert.IsNull(state.TargetCard);
            Assert.IsNull(state.TuningCard);
            Assert.IsFalse(state.IsReady);
        }
    }

    [TestFixture]
    public class FusionSelectionStateTests
    {
        private Card CreateCard(string id)
        {
            return new Card(id, "テストカード", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 100, "テスト");
        }

        [Test]
        public void TryToggle_2枚選択でIsReadyがtrueになる()
        {
            // Arrange
            var state = new FusionSelectionState();

            // Act
            state.TryToggle(CreateCard("card_a"));
            state.TryToggle(CreateCard("card_b"));

            // Assert
            Assert.IsTrue(state.IsReady);
            Assert.AreEqual(2, state.Materials.Count);
        }

        [Test]
        public void TryToggle_同じカードをトグルすると選択解除される()
        {
            // Arrange
            var state = new FusionSelectionState();
            var card = CreateCard("card_a");

            // Act
            state.TryToggle(card);
            state.TryToggle(card);

            // Assert
            Assert.AreEqual(0, state.Materials.Count);
            Assert.IsFalse(state.IsReady);
        }

        [Test]
        public void TryToggle_3枚目は拒否する()
        {
            // Arrange
            var state = new FusionSelectionState();

            // Act
            state.TryToggle(CreateCard("card_a"));
            state.TryToggle(CreateCard("card_b"));
            state.TryToggle(CreateCard("card_c"));

            // Assert
            Assert.AreEqual(2, state.Materials.Count);
        }

        [Test]
        public void Reset_素材リストがクリアされる()
        {
            // Arrange
            var state = new FusionSelectionState();
            state.TryToggle(CreateCard("card_a"));
            state.TryToggle(CreateCard("card_b"));

            // Act
            state.Reset();

            // Assert
            Assert.AreEqual(0, state.Materials.Count);
            Assert.IsFalse(state.IsReady);
        }

        [Test]
        public void IsSelected_選択済みカードはtrue_未選択はfalse()
        {
            // Arrange
            var state = new FusionSelectionState();
            var cardA = CreateCard("card_a");
            var cardB = CreateCard("card_b");

            state.TryToggle(cardA);

            // Act & Assert
            Assert.IsTrue(state.IsSelected(cardA));
            Assert.IsFalse(state.IsSelected(cardB));
        }

        [Test]
        public void TryToggle_1枚だけではIsReadyはfalse()
        {
            // Arrange
            var state = new FusionSelectionState();

            // Act
            state.TryToggle(CreateCard("card_a"));

            // Assert
            Assert.IsFalse(state.IsReady);
        }
    }
}
