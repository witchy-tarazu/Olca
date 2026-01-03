using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Domain.Master;
using Olca.Core.Logic.CardEffects.Effects;

namespace Olca.Tests.Domain
{
    [TestFixture]
    public class CardMasterParseTests
    {
        [Test]
        public void CardMasterから追加効果を正しくパースできる()
        {
            // Arrange
            var master = new CardMaster
            {
                Id = "test_card",
                Name = "テストカード",
                Cost = 1,
                Rarity = CardRarity.Normal,
                Category = CardCategory.Attack,
                Element = ElementType.Fire,
                Series = SeriesType.None,
                Power = 50,
                Description = "テスト",
                TargetType = DamageTargetType.Single,
                AdditionalEffectTypes = "MultiHit,PanelIce",
                AdditionalEffectPower = 0,
                AdditionalEffectDuration = 0.0f,
                AdditionalEffectHitCount = 3,
                ChainCardId = "",
                DerivativeTrigger = "Immediate,OnDamage"
            };

            // Act
            var card = Card.FromMaster(master);

            // Assert
            Assert.IsNotNull(card.Modifier, "Modifierが生成されるべき");
            Assert.AreEqual(2, card.Modifier.AdditionalEffects.Count, "2つの追加効果が設定されるべき");

            // 1つ目: MultiHit (Immediate)
            var multiHit = card.Modifier.AdditionalEffects[0];
            Assert.AreEqual(AdditionalEffectType.MultiHit, multiHit.EffectType);
            Assert.AreEqual(3, multiHit.HitCount);
            Assert.AreEqual(DerivativeTrigger.Immediate, multiHit.Trigger);

            // 2つ目: PanelIce (OnDamage)
            var panelIce = card.Modifier.AdditionalEffects[1];
            Assert.AreEqual(AdditionalEffectType.PanelIce, panelIce.EffectType);
            Assert.AreEqual(DerivativeTrigger.OnDamage, panelIce.Trigger);
        }

        [Test]
        public void ChainCardIdが設定されている場合は別カードを発動()
        {
            // Arrange
            var master = new CardMaster
            {
                Id = "spread_gun",
                Name = "スプレッドガン",
                Cost = 2,
                Rarity = CardRarity.Limited,
                Category = CardCategory.Attack,
                Element = ElementType.None,
                Series = SeriesType.None,
                Power = 30,
                Description = "爆風",
                TargetType = DamageTargetType.Single,
                AdditionalEffectTypes = "ChainCard",
                ChainCardId = "explosion_wave",
                AdditionalEffectHitCount = 1,
                DerivativeTrigger = "OnHit"
            };

            // Act
            var card = Card.FromMaster(master);

            // Assert
            Assert.AreEqual(1, card.Modifier.AdditionalEffects.Count);
            var chainEffect = card.Modifier.AdditionalEffects[0];
            Assert.AreEqual(AdditionalEffectType.ChainCard, chainEffect.EffectType);
            Assert.AreEqual("explosion_wave", chainEffect.ChainCardId);
            Assert.AreEqual(DerivativeTrigger.OnHit, chainEffect.Trigger);
        }

        [Test]
        public void ChainCardIdが空の場合は自分自身を再発動()
        {
            // Arrange
            var master = new CardMaster
            {
                Id = "double_sword",
                Name = "ダブルソード",
                Cost = 1,
                Rarity = CardRarity.Normal,
                Category = CardCategory.Attack,
                Element = ElementType.None,
                Series = SeriesType.Sword,
                Power = 25,
                Description = "2回攻撃",
                TargetType = DamageTargetType.Single,
                AdditionalEffectTypes = "ChainCard",
                ChainCardId = "",
                AdditionalEffectHitCount = 1,
                DerivativeTrigger = "OnHit"
            };

            // Act
            var card = Card.FromMaster(master);

            // Assert
            var chainEffect = card.Modifier.AdditionalEffects[0];
            Assert.AreEqual(AdditionalEffectType.ChainCard, chainEffect.EffectType);
            Assert.IsTrue(string.IsNullOrEmpty(chainEffect.ChainCardId), "ChainCardIdが空の場合は自分自身を再発動");
        }

        [Test]
        public void DerivativeTriggerの文字列を正しくパースできる()
        {
            // Arrange & Act & Assert
            var testCases = new[]
            {
                ("Immediate", DerivativeTrigger.Immediate),
                ("OnHit", DerivativeTrigger.OnHit),
                ("OnDamage", DerivativeTrigger.OnDamage),
                ("AfterActionIfHit", DerivativeTrigger.AfterActionIfHit),
                ("AfterActionIfDamaged", DerivativeTrigger.AfterActionIfDamaged)
            };

            foreach (var (triggerStr, expected) in testCases)
            {
                var master = new CardMaster
                {
                    Id = $"test_{triggerStr}",
                    Name = "テスト",
                    Cost = 1,
                    Rarity = CardRarity.Normal,
                    Category = CardCategory.Attack,
                    Element = ElementType.None,
                    Series = SeriesType.None,
                    Power = 10,
                    Description = "テスト",
                    TargetType = DamageTargetType.Single,
                    AdditionalEffectTypes = "PanelIce",
                    DerivativeTrigger = triggerStr
                };

                var card = Card.FromMaster(master);
                Assert.AreEqual(expected, card.Modifier.AdditionalEffects[0].Trigger,
                    $"{triggerStr} が正しくパースされるべき");
            }
        }

        [Test]
        public void 追加効果が設定されていない場合は空のModifierが生成される()
        {
            // Arrange
            var master = new CardMaster
            {
                Id = "simple_card",
                Name = "シンプルカード",
                Cost = 1,
                Rarity = CardRarity.Normal,
                Category = CardCategory.Attack,
                Element = ElementType.Fire,
                Series = SeriesType.None,
                Power = 40,
                Description = "通常攻撃",
                TargetType = DamageTargetType.Single,
                AdditionalEffectTypes = "", // 空
                DerivativeTrigger = ""
            };

            // Act
            var card = Card.FromMaster(master);

            // Assert
            Assert.IsNotNull(card.Modifier, "Modifierは生成されるべき");
            Assert.AreEqual(0, card.Modifier.AdditionalEffects.Count, "追加効果がない場合は空のリスト");
        }

        [Test]
        public void AdditionalEffectPowerが正しく設定される()
        {
            // Arrange
            var master = new CardMaster
            {
                Id = "power_test",
                Name = "威力テスト",
                Cost = 1,
                Rarity = CardRarity.Normal,
                Category = CardCategory.Attack,
                Element = ElementType.None,
                Series = SeriesType.None,
                Power = 50,
                Description = "テスト",
                TargetType = DamageTargetType.Single,
                AdditionalEffectTypes = "ChainCard",
                ChainCardId = "",
                AdditionalEffectPower = 60, // 60%
                DerivativeTrigger = "OnHit"
            };

            // Act
            var card = Card.FromMaster(master);

            // Assert
            var chainEffect = card.Modifier.AdditionalEffects[0];
            Assert.AreEqual(60, chainEffect.Power, "AdditionalEffectPowerが設定されるべき");
        }

        [Test]
        public void 全パネル変化効果が正しくパースされる()
        {
            // Arrange
            var master = new CardMaster
            {
                Id = "ice_age",
                Name = "アイスエイジ",
                Cost = 3,
                Rarity = CardRarity.Limited,
                Category = CardCategory.Field,
                Element = ElementType.Water,
                Series = SeriesType.None,
                Power = 0,
                Description = "画面全体氷化",
                TargetType = DamageTargetType.Single,
                AdditionalEffectTypes = "PanelIceAll",
                DerivativeTrigger = "Immediate"
            };

            // Act
            var card = Card.FromMaster(master);

            // Assert
            Assert.AreEqual(1, card.Modifier.AdditionalEffects.Count);
            var effect = card.Modifier.AdditionalEffects[0];
            Assert.AreEqual(AdditionalEffectType.PanelIceAll, effect.EffectType);
            Assert.AreEqual(DerivativeTrigger.Immediate, effect.Trigger);
        }

        [Test]
        public void 複数のトリガーを持つ効果を正しくパースできる()
        {
            // Arrange: 効果1はImmediate、効果2はAfterActionIfDamaged
            var master = new CardMaster
            {
                Id = "ice_burst",
                Name = "アイスバースト",
                Cost = 2,
                Rarity = CardRarity.Limited,
                Category = CardCategory.Attack,
                Element = ElementType.Water,
                Series = SeriesType.None,
                Power = 15,
                Description = "3段攻撃+氷化",
                TargetType = DamageTargetType.All,
                AdditionalEffectTypes = "MultiHit,PanelIce",
                AdditionalEffectHitCount = 3,
                DerivativeTrigger = "Immediate,AfterActionIfDamaged"
            };

            // Act
            var card = Card.FromMaster(master);

            // Assert
            Assert.AreEqual(2, card.Modifier.AdditionalEffects.Count);

            // 1つ目: MultiHit (Immediate)
            Assert.AreEqual(AdditionalEffectType.MultiHit, card.Modifier.AdditionalEffects[0].EffectType);
            Assert.AreEqual(DerivativeTrigger.Immediate, card.Modifier.AdditionalEffects[0].Trigger);
            Assert.AreEqual(3, card.Modifier.AdditionalEffects[0].HitCount);

            // 2つ目: PanelIce (AfterActionIfDamaged)
            Assert.AreEqual(AdditionalEffectType.PanelIce, card.Modifier.AdditionalEffects[1].EffectType);
            Assert.AreEqual(DerivativeTrigger.AfterActionIfDamaged, card.Modifier.AdditionalEffects[1].Trigger);
        }
    }
}
