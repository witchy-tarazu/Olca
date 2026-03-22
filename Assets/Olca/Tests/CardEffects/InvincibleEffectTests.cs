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
    /// 無敵効果のテスト
    /// </summary>
    [TestFixture]
    public class InvincibleEffectTests
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
                new Card("invincible", "無敵", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.Canon, 0, "無敵"),
            };
            deck = new Deck(cards);
            battle = new BattleSystem(player, enemies, deck, logger: new MockLogger());
        }

        [Test]
        public void SetInvincibleでIsInvincibleがtrueになる()
        {
            player.SetInvincible(3000);

            Assert.IsTrue(player.IsInvincible);
            Assert.AreEqual(3000, player.InvincibleDurationMs);
        }

        [Test]
        public void 無敵中はTakeDamageでHPが減少しない()
        {
            player.SetInvincible(3000);
            int initialHp = player.CurrentHp;

            player.TakeDamage(50);

            Assert.AreEqual(initialHp, player.CurrentHp, "無敵中はダメージを受けないはず");
        }

        [Test]
        public void TickInvincibleで無敵時間が減少する()
        {
            player.SetInvincible(3000);

            player.TickInvincible(1000);

            Assert.AreEqual(2000, player.InvincibleDurationMs);
            Assert.IsTrue(player.IsInvincible);
        }

        [Test]
        public void TickInvincibleで無敵が切れた後はダメージを受ける()
        {
            player.SetInvincible(1000);
            player.TickInvincible(1000); // 無敵解除

            Assert.IsFalse(player.IsInvincible);

            int initialHp = player.CurrentHp;
            player.TakeDamage(20);

            Assert.AreEqual(initialHp - 20, player.CurrentHp, "無敵解除後はダメージを受けるはず");
        }

        [Test]
        public void InvincibleEffectでプレイヤーに無敵が付与される()
        {
            var effect = new InvincibleEffect(3000);
            var card = new Card("invincible", "無敵", 1, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.Canon, 0, "無敵");
            var context = new BattleContext(battle, card);

            effect.Execute(context);

            Assert.IsTrue(player.IsInvincible);
            Assert.AreEqual(3000, player.InvincibleDurationMs);
        }

        [Test]
        public void BattleSystemTickで無敵時間がカウントダウンされる()
        {
            player.SetInvincible(2000);

            battle.Tick(1000);

            Assert.AreEqual(1000, player.InvincibleDurationMs);
        }

        [Test]
        public void InvincibleEffectの時間が0以下は例外をスローする()
        {
            Assert.Throws<System.ArgumentException>(() => new InvincibleEffect(0));
        }
    }
}
