using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;

namespace Olca.Tests.CardTests
{
    /// <summary>
    /// TuningSystemのユニットテスト
    /// </summary>
    [TestFixture]
    public class TuningSystemTests
    {
        private TuningSystem tuningSystem;

        private static Card MakeAttackCard(string id = "sword", int power = 10)
        {
            return new Card(id, id + "Card", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, power, "");
        }

        private static Card MakeTuningCard(string id = "tuning01")
        {
            var modifier = new CardModifier { BonusDamage = 10 };
            return new Card(id, "調律カード", 1, CardRarity.Normal, CardCategory.Tuning,
                ElementType.None, CardSeries.None, 0, "", modifier: modifier);
        }

        [SetUp]
        public void SetUp()
        {
            tuningSystem = new TuningSystem();
        }

        // ===== Validate =====

        [Test]
        public void Validate_正常ケース_成功()
        {
            var result = tuningSystem.Validate(MakeAttackCard(), MakeTuningCard());

            Assert.IsTrue(result.IsValid);
        }

        [Test]
        public void Validate_nullカード_失敗()
        {
            var result = tuningSystem.Validate(null, MakeTuningCard());

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Validate_Tuningカード以外を調律カードに指定_失敗()
        {
            var notTuning = MakeAttackCard("another");

            var result = tuningSystem.Validate(MakeAttackCard(), notTuning);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("調律カード", result.ErrorMessage);
        }

        [Test]
        public void Validate_調律カードへの調律_失敗()
        {
            var tuningA = MakeTuningCard("tuningA");
            var tuningB = MakeTuningCard("tuningB");

            var result = tuningSystem.Validate(tuningA, tuningB);

            Assert.IsFalse(result.IsValid);
        }

        // ===== Execute =====

        [Test]
        public void Execute_調律効果が適用された新カードを返す()
        {
            var target = MakeAttackCard("sword", power: 10);
            var tuning = MakeTuningCard();

            var result = tuningSystem.Execute(target, tuning);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.ResultCard);
            // 元のカードは変更されない
            Assert.AreEqual(10, target.GetFinalPower());
            // 結果カードにボーナスダメージが乗る
            Assert.AreEqual(20, result.ResultCard.GetFinalPower());
        }

        [Test]
        public void Execute_元カードはイミュータブル()
        {
            var target = MakeAttackCard("sword", power: 15);
            var tuning = MakeTuningCard();

            var result = tuningSystem.Execute(target, tuning);

            // 同じIDだが別インスタンス
            Assert.AreNotSame(target, result.ResultCard);
            Assert.AreEqual(target.Id, result.ResultCard.Id);
        }

        [Test]
        public void Execute_バリデーション失敗時はエラーを返す()
        {
            var result = tuningSystem.Execute(MakeAttackCard(), MakeAttackCard("another"));

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.ErrorMessage);
        }
    }
}
