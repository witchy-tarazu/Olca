using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Domain.Master;
using Olca.Core.Logic.CardEffects.Effects;

namespace Olca.Tests.Domain
{
    [TestFixture]
    public class CardMasterParseTests
    {
        private static string TimingHash(Card card) => (string)typeof(Olca.Core.Logic.Dungeon.DungeonCheckpointSession).Assembly
            .GetType("Olca.Core.Logic.Dungeon.CheckpointValues").GetMethod("CardHash",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{card});
        [Test]
        public void 未指定の前後隙は旧秒値と既存製品ソードの保存ハッシュを維持する()
        {
            var master=new CardMaster{Id="legacy-time",Name="検証",Category=CardCategory.Attack,Series=SeriesType.Sword,StartupTime=.011f,RecoveryTime=.101f};
            var card=Card.FromMaster(master);Assert.IsNull(card.StartupFrames);Assert.IsNull(card.RecoveryFrames);Assert.IsNull(card.RecoveryCancelAfterFrames);Assert.AreEqual(.011f,card.StartupTime);Assert.AreEqual(.101f,card.RecoveryTime);
            var sword=new Olca.Application.Battle.SimpleCardFactory().CreateById("sword");
            // 68b40a8で保存した未加工ソードの値。任意の省略フィールド追加で変えない。
            Assert.AreEqual("1218C1F2DB25767F19997CB95FC15F3A97EEEBBCFFA7E0C2036D866E68B60E55",TimingHash(sword));
            Assert.AreNotEqual(TimingHash(sword),TimingHash(sword.WithAttackTiming(new PlayerAttackTiming(0,0))));
        }
        [TestCase(-1,12,6)] [TestCase(3,-1,0)] [TestCase(3,12,-1)] [TestCase(3,12,13)]
        public void 不正な製品時間はマスターからカードを公開する前に拒否する(int startup,int recovery,int cancel)
        {
            var master=new CardMaster{Id="bad",Category=CardCategory.Attack,Series=SeriesType.Sword,StartupFrames=startup,RecoveryFrames=recovery,RecoveryCancelAfterFrames=cancel,IsInstantEffect=true};
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>Card.FromMaster(master));
        }
        [Test]
        public void 非有限秒値と後隙を超える取消を拒否し明示ゼロと省略を区別する()
        {
            var master=new CardMaster{Id="bad",Category=CardCategory.Attack,Series=SeriesType.Sword,StartupTime=float.NaN};Assert.Throws<System.ArgumentOutOfRangeException>(()=>Card.FromMaster(master));
            master.StartupTime=0;master.RecoveryTime=float.PositiveInfinity;Assert.Throws<System.ArgumentOutOfRangeException>(()=>Card.FromMaster(master));
            master.RecoveryTime=.1f;master.RecoveryCancelAfterFrames=7;Assert.Throws<System.ArgumentOutOfRangeException>(()=>Card.FromMaster(master));
            master.RecoveryCancelAfterFrames=0;master.StartupTime=9;master.StartupFrames=0;master.RecoveryFrames=0;var zero=Card.FromMaster(master);Assert.AreEqual(0,zero.StartupFrames);Assert.AreEqual(9,zero.StartupTime);
        }
        [Test]
        public void 製品時間は複製調律倍率派生超越で保持され外部辞書と対象テンプレートを共有しない()
        {
            var values=new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"sword",new PlayerAttackTiming(3,12,6)}};
            var factory=new Olca.Application.Battle.SimpleCardFactory(cardTimings:values);values.Clear();var sword=factory.CreateById("sword");var tune=new Card("t","t",1,CardRarity.Normal,CardCategory.Tuning,ElementType.None,CardSeries.None,0,"検証");
            foreach(var card in new[]{sword.Clone(),sword.ApplyTuning(tune),sword.WithPowerMultiplier(2),sword.CreateSelfChainCopy(50),factory.CreateExceeded(sword,ExceedLevel.Lv1)}){
                Assert.AreEqual(3,card.StartupFrames);Assert.AreEqual(12,card.RecoveryFrames);Assert.AreEqual(6,card.RecoveryCancelAfterFrames);
            }
            Assert.IsNull(new Olca.Application.Battle.SimpleCardFactory().CreateById("sword").StartupFrames);
            Assert.Throws<System.ArgumentException>(()=>new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"missing",new PlayerAttackTiming()}}));
            Assert.Throws<System.ArgumentException>(()=>new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"sword",null}}));
        }
        [Test]
        public void 融合は結果製品の時間を使い取消は元素材の時間へ戻す()
        {
            var factory=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"sword",new PlayerAttackTiming(3,12,6)},{"bomb",new PlayerAttackTiming(2,9,3)},{"fire_sword",new PlayerAttackTiming(8,16,5)}});
            var sword=factory.CreateById("sword");var bomb=factory.CreateById("bomb");var screen=new Olca.Core.Logic.Battle.CustomScreenSystem(new Deck(new System.Collections.Generic.List<Card>{sword,bomb}),fusionSystem:new Olca.Core.Logic.Battle.FusionSystem(new Olca.Application.Battle.SimpleFusionRecipeProvider(),factory));screen.Open(new Card[0],new Olca.Tests.Mocks.MockRandomProvider());
            var fusion=screen.ExecuteFusion(new[]{sword,bomb});Assert.IsTrue(fusion.IsSuccess);Assert.AreEqual(8,fusion.ResultCard.StartupFrames);Assert.AreEqual(16,fusion.ResultCard.RecoveryFrames);Assert.AreEqual(5,fusion.ResultCard.RecoveryCancelAfterFrames);
            Assert.IsTrue(screen.TryUndoCustomization());foreach(var card in screen.CurrentHand){Assert.AreEqual(card.Id=="sword"?3:2,card.StartupFrames);Assert.AreEqual(card.Id=="sword"?6:3,card.RecoveryCancelAfterFrames);}
        }

        [TestCase(0)][TestCase(7)]
        public void 明示削り量はマスターと複製調律倍率派生超越でHP威力と独立して維持する(int impact)
        {
            var master = new CardMaster { Id = "impact", Name = "検証", Category = CardCategory.Attack,
                Series = SeriesType.Sword, Power = 10 };
            Assert.IsNull(Card.FromMaster(master).PoiseDamage);
            master.PoiseDamage = impact; var card = Card.FromMaster(master);
            var material = new Card("t", "検証", 1, CardRarity.Normal, CardCategory.Tuning,
                ElementType.None, CardSeries.None, 0, "検証", poiseDamage: 99);
            foreach (var copy in new[] { card.Clone(), card.WithPowerMultiplier(2), card.CreateSelfChainCopy(50),
                card.ApplyTuning(material), new Olca.Application.Battle.SimpleCardFactory().CreateExceeded(card, ExceedLevel.Lv1) })
                Assert.AreEqual(impact, copy.PoiseDamage);
            master.PoiseDamage = -1;
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Card.FromMaster(master));
        }

        [Test]
        public void 複合性質はマスターから複製調律倍率自己派生超越へ維持し未指定は無しになる()
        {
            var master = new CardMaster { Id = "property", Name = "検証", Category = CardCategory.Attack,
                Element = ElementType.Fire, Series = SeriesType.Sword, Power = 10 };
            Assert.AreEqual(AttackProperty.None, Card.FromMaster(master).Properties, "系統から性質を推測しない");
            master.Properties = AttackProperty.Sword | AttackProperty.Break | AttackProperty.Wind;
            var card = Card.FromMaster(master);
            var material = new Card("t", "検証", 1, CardRarity.Normal, CardCategory.Tuning,
                ElementType.None, CardSeries.None, 0, "検証", properties: AttackProperty.Break);
            foreach (var copy in new[] { card, card.Clone(), card.WithPowerMultiplier(2), card.CreateSelfChainCopy(50),
                card.ApplyTuning(material), new Olca.Application.Battle.SimpleCardFactory().CreateExceeded(card, ExceedLevel.Lv1) })
            {
                Assert.AreEqual(AttackProperty.Sword | AttackProperty.Break | AttackProperty.Wind, copy.Properties);
                Assert.AreEqual(ElementType.Fire, copy.Element);
            }
            var plain = new Card("p", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "検証");
            Assert.AreEqual(AttackProperty.None, plain.ApplyTuning(material).Properties, "素材の性質は追加仕様なしで付与しない");
            master.Properties = (AttackProperty)8;
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Card.FromMaster(master));
        }

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
