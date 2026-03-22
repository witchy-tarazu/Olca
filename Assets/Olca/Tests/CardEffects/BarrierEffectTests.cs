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
    /// バリア効果のテスト
    /// </summary>
    [TestFixture]
    public class BarrierEffectTests
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
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0))
            };
            var cards = new List<Card>
            {
                new Card("barrier_shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.Canon, 30, "バリア"),
            };
            deck = new Deck(cards);
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());
        }

        [Test]
        public void BarrierEffectでバリアが付与される()
        {
            var effect = new BarrierEffect(30);
            var card = new Card("barrier_shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.Canon, 30, "バリア");
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            Assert.AreEqual(30, player.BarrierHp);
            Assert.IsTrue(player.HasBarrier);
        }

        [Test]
        public void バリアがある場合ダメージを吸収する()
        {
            player.SetBarrier(30);
            int initialHp = player.CurrentHp;

            player.TakeDamage(20); // バリア30 > ダメージ20

            Assert.AreEqual(initialHp, player.CurrentHp, "HPは変化しないはず");
            Assert.AreEqual(10, player.BarrierHp, "バリアが20減るはず");
        }

        [Test]
        public void バリアを超えるダメージは超過分がHPに入る()
        {
            player.SetBarrier(20);
            int initialHp = player.CurrentHp;

            player.TakeDamage(30); // バリア20を超える30ダメージ

            Assert.AreEqual(0, player.BarrierHp, "バリアは0になるはず");
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "超過10ダメージがHPに入るはず");
        }

        [Test]
        public void バリア以下のダメージではHPが減らない()
        {
            player.SetBarrier(50);
            int initialHp = player.CurrentHp;

            player.TakeDamage(50);

            Assert.AreEqual(0, player.BarrierHp);
            Assert.AreEqual(initialHp, player.CurrentHp, "HPは変化しないはず");
        }

        [Test]
        public void バリアが割れた後の攻撃は通常通りHPに入る()
        {
            player.SetBarrier(10);

            player.TakeDamage(10); // バリアを割る
            int hpAfterBarrierBreak = player.CurrentHp;
            player.TakeDamage(15); // バリアなし

            Assert.AreEqual(hpAfterBarrierBreak - 15, player.CurrentHp, "バリアなしで15ダメージ入るはず");
        }

        [Test]
        public void CardEffectFactoryでbarrierカードはBarrierEffectが生成される()
        {
            var card = new Card("barrier_shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.Canon, 30, "バリア");
            var effect = CardEffectFactory.CreateEffect(card);

            Assert.IsInstanceOf<BarrierEffect>(effect);
        }

        [Test]
        public void BarrierEffectのHPが0以下は例外をスローする()
        {
            Assert.Throws<System.ArgumentException>(() => new BarrierEffect(0));
        }
    }
}
