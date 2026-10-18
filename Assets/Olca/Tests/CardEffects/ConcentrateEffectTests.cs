using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.CardEffects
{
    /// <summary>
    /// 集中効果のテスト
    /// </summary>
    [TestFixture]
    public class ConcentrateEffectTests
    {
        private BattleSystem battle;
        private PlayerCharacter player;
        private Deck deck;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 100, ElementType.None, new Vector2Int(3, 0))
            };
            var cards = new List<Card>
            {
                new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 10, "攻撃"),
            };
            deck = new Deck(cards);
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());
        }

        [Test]
        public void ConcentrateEffectでNextCardMultiplierが設定される()
        {
            var effect = new ConcentrateEffect(1.5f);
            var card = new Card("concentrate", "集中", 1, CardRarity.Normal, CardCategory.Support, ElementType.None, CardSeries.Canon, 0, "集中");
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            Assert.AreEqual(1.5f, player.NextCardMultiplier, 0.001f);
            Assert.IsTrue(player.IsConcentrating);
        }

        [Test]
        public void ConsumeConcentrateBuffで倍率が取得されリセットされる()
        {
            player.SetConcentrateBuff(2.0f);

            float val = player.ConsumeConcentrateBuff();

            Assert.AreEqual(2.0f, val, 0.001f);
            Assert.AreEqual(1.0f, player.NextCardMultiplier, 0.001f, "消費後は1.0にリセットされるはず");
            Assert.IsFalse(player.IsConcentrating);
        }

        [Test]
        public void 集中バフがない状態ではConsumeで1_0fが返る()
        {
            float val = player.ConsumeConcentrateBuff();

            Assert.AreEqual(1.0f, val, 0.001f);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void カード使用で次の攻撃だけ強化し元カードを保持する(bool asyncUse)
        {
            var attack = new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 10, "攻撃", modifier: CardModifier.CreateAttackBonus(10));
            player.SetConcentrateBuff(1.5f);
            battle.CardUsage.EnqueueCards(new List<Card> { attack, attack });

            if (asyncUse) Assert.IsTrue(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());
            else Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());

            Assert.AreEqual(70, battle.Enemies[0].CurrentHp, "調律込み20の1.5倍");
            Assert.IsFalse(player.IsConcentrating);
            Assert.AreEqual(20, attack.GetFinalPower(), "元カードの威力は不変");
            Assert.AreEqual(10, attack.Modifier.BonusDamage);
            battle.CardUsage.TryUseCurrentCard();
            Assert.AreEqual(50, battle.Enemies[0].CurrentHp, "2枚目は通常威力");
        }

        [TestCase(CardCategory.Defense)]
        [TestCase(CardCategory.Support)]
        [TestCase(CardCategory.Field)]
        public void 攻撃以外のカードでは集中を消費しない(CardCategory category)
        {
            player.SetConcentrateBuff(1.5f);
            var card = new Card("non_attack", "補助", 1, CardRarity.Normal, category,
                ElementType.None, CardSeries.None, 0, "補助");
            battle.CardUsage.EnqueueCards(new List<Card> { card });

            Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());

            Assert.AreEqual(1.5f, player.NextCardMultiplier);
        }

        [Test]
        public void 集中カードから次の攻撃まで使用経路で適用される()
        {
            var concentrate = new Card("concentrate", "集中", 1, CardRarity.Normal, CardCategory.Support,
                ElementType.None, CardSeries.None, 50, "次の攻撃を1.5倍");
            var attack = new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 10, "攻撃");
            battle.CardUsage.EnqueueCards(new List<Card> { concentrate, attack });

            battle.CardUsage.TryUseCurrentCard();
            Assert.IsTrue(player.IsConcentrating);
            battle.CardUsage.TryUseCurrentCard();

            Assert.AreEqual(85, battle.Enemies[0].CurrentHp);
            Assert.IsFalse(player.IsConcentrating);
        }

        [Test]
        public void スキップと空キューでは集中を消費しない()
        {
            player.SetConcentrateBuff(1.5f);
            var card = new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 10, "攻撃");
            battle.CardUsage.EnqueueCards(new List<Card> { card });

            Assert.IsTrue(battle.CardUsage.SkipCurrentCard());
            Assert.IsFalse(battle.CardUsage.TryUseCurrentCard());

            Assert.AreEqual(1.5f, player.NextCardMultiplier);
        }

        [Test]
        public void ConcentrateEffectの倍率が1_0以下は例外をスローする()
        {
            Assert.Throws<System.ArgumentException>(() => new ConcentrateEffect(1.0f));
            Assert.Throws<System.ArgumentException>(() => new ConcentrateEffect(0.5f));
        }
    }
}
