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

        [TestCase("name")]
        [TestCase("power")]
        [TestCase("rarity")]
        [TestCase("cost")]
        [TestCase("series")]
        [TestCase("timing")]
        [TestCase("modifier")]
        public void 同ID同段階でも元定義が異なる二枚は両順序で拒否する(string difference)
        {
            var a = new Olca.Application.Battle.SimpleCardFactory().CreateById("sword");
            var b = new Card(a.Id, difference == "name" ? "sword_card" : a.Name,
                a.Cost + (difference == "cost" ? 1 : 0), difference == "rarity" ? CardRarity.Limited : a.Rarity,
                a.Category, a.Element, difference == "series" ? CardSeries.None : a.Series,
                a.Power + (difference == "power" ? 1 : 0), a.Description,
                targetType: a.TargetType, modifier: difference == "modifier" ? new CardModifier { BonusDamage = 1 } : a.Modifier.Clone(),
                subCategory: a.SubCategory, properties: a.Properties, poiseDamage: a.PoiseDamage,
                hitInvincibilityMs: a.HitInvincibilityMs, startupFrames: difference == "timing" ? 9 : a.StartupFrames,
                recoveryFrames: a.RecoveryFrames, recoveryCancelAfterFrames: a.RecoveryCancelAfterFrames);
            foreach (var pair in new[] { new[] { a, b }, new[] { b, a } })
            {
                Assert.IsFalse(exceedSystem.Validate(pair[0], pair[1]).IsValid, difference);
                Assert.IsFalse(exceedSystem.Execute(pair[0], pair[1]).IsSuccess, difference);
            }
        }

        [Test]
        public void 調律履歴が違っても同じ元定義なら超越でき生成先は標準状態になる()
        {
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var a = factory.CreateById("sword");
            var tuning = new TuningSystem(factory.GetTuningDefinition);
            var b = tuning.Execute(a.Clone(), factory.CreateById("tuning")).ResultCard;
            var before = CollectionDisplay.DefinitionHash(b);
            var system = new ExceedSystem(factory);
            Assert.AreNotEqual(CollectionDisplay.DefinitionHash(a), before);
            foreach (var pair in new[] { new[] { a, b }, new[] { b, a } })
            {
                var result = system.Execute(pair[0], pair[1]);
                Assert.IsTrue(result.IsSuccess, result.ErrorMessage);
                Assert.AreEqual(45, result.ResultCard.GetFinalPower());
                Assert.AreEqual(0, result.ResultCard.TuningCount);
            }
            Assert.AreEqual(before, CollectionDisplay.DefinitionHash(b), "素材は変更しない");
        }

        [Test]
        public void 旧調律APIも元定義の固有効果を保持して同じカードを誤拒否しない()
        {
            var a = new Card("same", "同じカード", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 20, "", modifier:new CardModifier { BonusDamage = 2 });
            var source = new Card("t", "調律", 1, CardRarity.Normal, CardCategory.Tuning,
                ElementType.None, CardSeries.None, 0, "", modifier:new CardModifier { BonusDamage = 10 });
            var tuned = a.ApplyTuning(source).ApplyTuning(source);
            Assert.AreEqual(42, tuned.GetFinalPower());
            Assert.IsTrue(exceedSystem.Validate(a, tuned).IsValid);
            Assert.AreEqual(22, a.GetFinalPower());
        }

        [Test]
        public void 同ID別定義の保存復帰後も判定が変わらず旧定義のハッシュも維持する()
        {
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var a = factory.CreateById("sword");
            var b = new Card(a.Id, "sword_card", a.Cost, a.Rarity, a.Category, a.Element, a.Series, a.Power, "Fallback");
            var definitions = new Olca.Core.Logic.Dungeon.CheckpointDefinitions("exceed-test", DungeonConfig.CreateTestDungeon());
            definitions.RegisterCard("product", a); definitions.RegisterCard("fallback", b);
            var grant = factory.GetTuningDefinition(factory.CreateById("tuning"));
            definitions.RegisterGrant("tuning", grant);
            var tuned = TuningEvaluator.Evaluate(a, grant).ResultCard;
            var data = new[] { definitions.CaptureKnownCard("one", a), definitions.CaptureKnownCard("two", a.Clone()), definitions.CaptureKnownCard("three", b), definitions.CaptureCard("four", "product", tuned, "tuning") };
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(CheckpointCardDto[]));
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, data); stream.Position = 0;
                data = (CheckpointCardDto[])serializer.ReadObject(stream);
            }
            var restore = typeof(Olca.Core.Logic.Dungeon.CheckpointDefinitions).GetMethod("RestoreCard", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var restored = System.Array.ConvertAll(data, dto => (Card)restore.Invoke(definitions, new object[] { dto }));
            Assert.AreEqual(CollectionDisplay.DefinitionHash(a), data[0].ValueHash);
            Assert.AreEqual(CollectionDisplay.DefinitionHash(b), data[2].ValueHash);
            Assert.IsTrue(exceedSystem.Validate(restored[0], restored[1]).IsValid);
            Assert.IsFalse(exceedSystem.Validate(restored[0], restored[2]).IsValid);
            Assert.AreEqual(CollectionDisplay.DefinitionHash(tuned), data[3].ValueHash);
            Assert.IsTrue(exceedSystem.Validate(restored[0], restored[3]).IsValid);
            Assert.AreEqual(1, restored[3].TuningCount);
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
