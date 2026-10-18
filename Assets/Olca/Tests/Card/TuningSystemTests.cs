using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
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
        private static TuningEffectDefinition Bonus(string id, int maximum, params int[] values)
            => new TuningEffectDefinition(id, maximum, values.Select((v, i) => new { v, i })
                .ToDictionary(x => x.i + 1, x => new CardModifier { BonusDamage = x.v }));
        private static Card Material(string id, TuningGrantDefinition definition)
            => new Card(id, id, 1, CardRarity.Normal, CardCategory.Tuning, ElementType.None, CardSeries.None, 0, "test")
                .WithTuningDefinition(definition);

        [Test]
        public void 総回数で各付与を再評価し同じ効果も個別合算して効果別上限で止まる()
        {
            var system = new TuningSystem(); var target = MakeAttackCard();
            var effect = Bonus("a", 2, 3, 8);
            var a = Material("a", new TuningGrantDefinition("a", new[] { effect }, 2));
            var b = Material("b", new TuningGrantDefinition("b", new[] { Bonus("b", 4, 1, 4, 9, 16) }, -1));
            var one = system.Execute(target, a).ResultCard;
            var two = system.Execute(one, a).ResultCard;
            Assert.AreEqual(26, two.GetFinalPower()); Assert.AreEqual(2, two.TuningHistory.Count);
            var three = system.Execute(two, b).ResultCard;
            Assert.AreEqual(35, three.GetFinalPower()); Assert.AreEqual(5, three.GetFinalCost());
            var four = system.Execute(three, a).ResultCard; var five = system.Execute(four, a).ResultCard;
            Assert.AreEqual(58, five.GetFinalPower()); Assert.AreEqual(5, five.TuningCount);
            Assert.AreEqual(13, one.GetFinalPower()); Assert.AreEqual(10, target.GetFinalPower());
        }

        [Test]
        public void 一回で複数効果を付けても総回数は一増加し固定コストと専用0は段階で変わらない()
        {
            var status = new TuningEffectDefinition("poison", 2, new Dictionary<int, CardModifier> {
                {1, new CardModifier { AdditionalEffects = new List<AdditionalEffect>{ new AdditionalEffect { EffectType=AdditionalEffectType.Poison,Power=2,Duration=1 } } }},
                {2, new CardModifier { AdditionalEffects = new List<AdditionalEffect>{ new AdditionalEffect { EffectType=AdditionalEffectType.Poison,Power=5,Duration=3 } } }} });
            var m = Material("multi", new TuningGrantDefinition("multi", new[] { Bonus("bonus",1,7), status }, 2));
            var system = new TuningSystem(); var one = system.Execute(MakeAttackCard(), m).ResultCard;
            Assert.AreEqual(1, one.TuningCount); Assert.AreEqual(4, one.GetFinalCost());
            var two = system.Execute(one, m).ResultCard;
            Assert.AreEqual(2, two.Modifier.AdditionalEffects.Count); Assert.IsTrue(two.Modifier.AdditionalEffects.All(e=>e.Power==5&&e.Duration==3));
            Assert.AreEqual(24, two.GetFinalPower()); Assert.AreEqual(6, two.GetFinalCost());
            var zero = system.Execute(two, Material("zero", new TuningGrantDefinition("zero", fixedCostAdjustment:100,zeroCost:true))).ResultCard;
            Assert.AreEqual(0, zero.GetFinalCost()); Assert.AreEqual(24, zero.GetFinalPower());
        }

        [Test]
        public void 定義とCloneとプレビュー結果の編集は別の評価に漏れない()
        {
            var value = new CardModifier { BonusDamage=6 }; var stages = new Dictionary<int,CardModifier>{{1,value}};
            var effect = new TuningEffectDefinition("fixed",1,stages); var list = new List<TuningEffectDefinition>{effect};
            var grant = new TuningGrantDefinition("g",list); value.BonusDamage=99;stages.Clear();list.Clear();
            effect.TryGetStage(1,out var exported);exported.BonusDamage=888;
            var system = new TuningSystem();var first=system.Execute(MakeAttackCard(),Material("g",grant)).ResultCard;
            var copy=first.Clone();copy.Modifier.BonusDamage=777;
            Assert.AreEqual(16,first.GetFinalPower());
            var second=system.Execute(copy,Material("cost",new TuningGrantDefinition("cost",fixedCostAdjustment:1))).ResultCard;
            Assert.AreEqual(16,second.GetFinalPower());Assert.AreEqual(1,first.TuningHistory.Count);Assert.AreEqual(2,second.TuningHistory.Count);
        }

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(2)]
        public void 不正上限または必要段階欠落はUndefinedで元カードを変更しない(int maximum)
        {
            var system=new TuningSystem();var target=MakeAttackCard();
            var material=Material("g",new TuningGrantDefinition("g",new[]{Bonus("x",maximum,3)}));
            var first=system.Execute(target,material);
            var result=maximum==2?system.Execute(first.ResultCard,material):first;
            Assert.IsTrue(result.IsUndefined);Assert.IsNull(result.ResultCard);Assert.AreEqual(0,target.TuningCount);
        }

        [Test]
        public void 未定義素材と旧履歴不足は推測せず使用時コピーも再調律しない()
        {
            var system=new TuningSystem();var target=MakeAttackCard();var legacy=MakeTuningCard();
            Assert.IsTrue(system.Execute(target,legacy).IsUndefined);
            var old=target.ApplyTuning(legacy);
            var material=Material("g",new TuningGrantDefinition("g",new[]{Bonus("a",1,3)}));
            Assert.IsTrue(system.Execute(old,material).IsUndefined);
            var tuned=system.Execute(target,material).ResultCard;
            var used=tuned.WithPowerMultiplier(2);Assert.AreEqual(26,used.GetFinalPower());Assert.AreEqual(13,tuned.GetFinalPower());
            var chain=tuned.CreateSelfChainCopy(100);Assert.AreEqual(10,chain.GetFinalPower());
            Assert.IsTrue(system.Execute(used,material).IsUndefined);Assert.IsTrue(system.Execute(chain,material).IsUndefined);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 不完全な元効果または最終コスト溢れはUndefinedで返す(bool overflow)
        {
            var target = MakeAttackCard();
            if (!overflow) target.Modifier.AdditionalEffects = null;
            var result = new TuningSystem().Execute(target, Material("cost",
                new TuningGrantDefinition("cost", fixedCostAdjustment: overflow ? int.MaxValue : 1)));
            Assert.IsTrue(result.IsUndefined);
            Assert.IsNull(result.ResultCard);
            Assert.AreEqual(0, target.TuningCount);
            Assert.IsEmpty(target.TuningHistory);
        }

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
            tuningSystem = new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier);
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
