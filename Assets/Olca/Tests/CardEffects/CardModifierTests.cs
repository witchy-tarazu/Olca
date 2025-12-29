using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.CardEffects
{
    [TestFixture]
    public class CardModifierTests
    {
        private BattleSystem battle;
        private PlayerCharacter player;
        private List<Enemy> enemies;
        private Deck deck;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            enemies = new List<Enemy>
            {
                new Enemy("slime1", "スライム1", 100, ElementType.None, new Vector2Int(3, 0)),
                new Enemy("slime2", "スライム2", 100, ElementType.None, new Vector2Int(4, 0)),
                new Enemy("slime3", "スライム3", 100, ElementType.None, new Vector2Int(5, 0))
            };

            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 80, "前方1マス攻撃")
            };
            deck = new Deck(cards);
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());
        }

        [Test]
        public void アタック10でダメージが10増加する()
        {
            // Arrange
            var baseCard = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 80, "攻撃");
            var attackPlus10 = new Card("atk+10", "アタック+10", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "威力+10",
                modifier: CardModifier.CreateAttackBonus(10));

            // Act
            var tunedCard = baseCard.ApplyTuning(attackPlus10);

            // Assert
            Assert.AreEqual(90, tunedCard.GetFinalPower(), "威力が80+10=90になるべき");
        }

        [Test]
        public void 複数の調律効果を重ねられる()
        {
            // Arrange
            var baseCard = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "単体攻撃");
            var attackPlus10 = new Card("atk+10", "アタック+10", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "威力+10",
                modifier: CardModifier.CreateAttackBonus(10));
            var attackPlus30 = new Card("atk+30", "アタック+30", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "威力+30",
                modifier: CardModifier.CreateAttackBonus(30));

            // Act
            var tunedCard = baseCard.ApplyTuning(attackPlus10).ApplyTuning(attackPlus30);

            // Assert
            Assert.AreEqual(80, tunedCard.GetFinalPower(), "威力が40+10+30=80になるべき");
        }

        [Test]
        public void スプレッド効果を調律で付与できる()
        {
            // Arrange
            var baseCard = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 40, "単体攻撃");
            var spreadModifier = CardModifier.CreateAdditionalEffect(AdditionalEffectType.Spread);
            var spreadCard = new Card("spread", "スプレッド", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "スプレッド効果",
                modifier: spreadModifier);

            // Act
            var tunedCard = baseCard.ApplyTuning(spreadCard);
            var effect = new ModifiableDamageEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, tunedCard);

            int enemy1HpBefore = enemies[0].CurrentHp;
            int enemy2HpBefore = enemies[1].CurrentHp;

            effect.Execute(context);

            // Assert
            // 着弾地点(3,0) + 前方(4,0) にダメージが入るべき
            Assert.Less(enemies[0].CurrentHp, enemy1HpBefore, "最前列の敵がダメージを受けるべき");
            Assert.Less(enemies[1].CurrentHp, enemy2HpBefore, "2番目の敵もスプレッド効果でダメージを受けるべき");
        }

        [Test]
        public void 貫通効果を調律で付与できる()
        {
            // Arrange
            var baseCard = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 30, "単体攻撃");
            var penetrationModifier = CardModifier.CreateAdditionalEffect(AdditionalEffectType.Penetration);
            var penetrationCard = new Card("penetration", "貫通", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "貫通効果",
                modifier: penetrationModifier);

            // Act
            var tunedCard = baseCard.ApplyTuning(penetrationCard);
            var effect = new ModifiableDamageEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, tunedCard);

            effect.Execute(context);

            // Assert
            // 全ての敵が貫通ダメージを受けるべき
            Assert.AreEqual(70, enemies[0].CurrentHp, "1番目の敵が30ダメージを受けるべき");
            Assert.AreEqual(70, enemies[1].CurrentHp, "2番目の敵も貫通で30ダメージを受けるべき");
            Assert.AreEqual(70, enemies[2].CurrentHp, "3番目の敵も貫通で30ダメージを受けるべき");
        }

        [Test]
        public void 多段ヒット効果を調律で付与できる()
        {
            // Arrange
            var baseCard = new Card("cannon", "キャノン", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 15, "単体攻撃");
            var multiHitModifier = new CardModifier();
            multiHitModifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.MultiHit,
                HitCount = 4
            });
            var multiHitCard = new Card("multi", "多段ヒット", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "4ヒット",
                modifier: multiHitModifier);

            // Act
            var tunedCard = baseCard.ApplyTuning(multiHitCard);
            var effect = new ModifiableDamageEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, tunedCard);

            effect.Execute(context);

            // Assert
            // 15ダメージ × 4ヒット = 60ダメージ
            Assert.AreEqual(40, enemies[0].CurrentHp, "15×4=60ダメージを受けるべき");
        }

        [Test]
        public void ソードにスプレッド効果を付与すると範囲が拡大する()
        {
            // Arrange: ソード（前方1マス攻撃）
            var sword = new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 50, "前方1マス");
            var spreadModifier = CardModifier.CreateAdditionalEffect(AdditionalEffectType.Spread);
            var spreadCard = new Card("spread", "スプレッド", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.None, 0, "スプレッド",
                modifier: spreadModifier);

            // Act: ソード + スプレッド
            var tunedSword = sword.ApplyTuning(spreadCard);
            var effect = new ModifiableDamageEffect(DamageTargetType.FrontTile);
            var context = new BattleContext(battle, tunedSword);

            int enemy1HpBefore = enemies[0].CurrentHp;
            int enemy2HpBefore = enemies[1].CurrentHp;

            effect.Execute(context);

            // Assert
            // 前方1マス(3,0)に攻撃 → スプレッドで(2,0)(3,0)(4,0)に拡散
            Assert.Less(enemies[0].CurrentHp, enemy1HpBefore, "前方の敵がダメージを受けるべき");
            Assert.Less(enemies[1].CurrentHp, enemy2HpBefore, "スプレッド効果で2番目の敵もダメージを受けるべき");
        }
    }
}
