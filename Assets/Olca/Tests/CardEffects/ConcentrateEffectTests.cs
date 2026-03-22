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

        [Test]
        public void ConcentrateEffectの倍率が1_0以下は例外をスローする()
        {
            Assert.Throws<System.ArgumentException>(() => new ConcentrateEffect(1.0f));
            Assert.Throws<System.ArgumentException>(() => new ConcentrateEffect(0.5f));
        }
    }
}
