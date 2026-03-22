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
    /// 状態異常システムのテスト
    /// </summary>
    [TestFixture]
    public class StatusEffectTests
    {
        private BattleSystem battle;
        private PlayerCharacter player;
        private Enemy enemy;
        private Deck deck;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            enemy = new Enemy("slime", "スライム", 100, ElementType.None, new Vector2Int(3, 0));
            var enemies = new List<Enemy> { enemy };
            var cards = new List<Card>
            {
                new Card("attack", "攻撃", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Canon, 10, "攻撃"),
            };
            deck = new Deck(cards);
            logger = new MockLogger();
            battle = new BattleSystem(player, enemies, deck, logger: logger);
        }

        // ==================== StatusEffect クラス ====================

        [Test]
        public void Tickで時間が経過する()
        {
            var status = new StatusEffect(StatusEffectType.Paralyze, 3000);
            bool expired = status.Tick(1000);

            Assert.AreEqual(2000, status.RemainingMs);
            Assert.IsFalse(expired, "まだ消滅しないはず");
        }

        [Test]
        public void Tickで時間が0になると消滅する()
        {
            var status = new StatusEffect(StatusEffectType.Paralyze, 1000);
            bool expired = status.Tick(1000);

            Assert.IsTrue(expired, "消滅するはず");
        }

        [Test]
        public void 持続時間0以下は例外をスローする()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new StatusEffect(StatusEffectType.Paralyze, 0));
        }

        // ==================== Enemy への付与・解除 ====================

        [Test]
        public void ApplyStatusで麻痺が付与される()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 3000));

            Assert.IsTrue(enemy.IsParalyzed, "麻痺中のはず");
            Assert.IsNotNull(enemy.ActiveStatus);
            Assert.AreEqual(StatusEffectType.Paralyze, enemy.ActiveStatus.Type);
        }

        [Test]
        public void ApplyStatusで毒が付与される()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 5000, 10));

            Assert.IsTrue(enemy.IsPoisoned, "毒状態のはず");
            Assert.AreEqual(10, enemy.ActiveStatus.DamagePerTick);
        }

        [Test]
        public void 状態異常は上書きされる()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 3000));
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 5000, 5));

            Assert.IsFalse(enemy.IsParalyzed, "麻痺は消えているはず");
            Assert.IsTrue(enemy.IsPoisoned, "毒が上書きされているはず");
        }

        [Test]
        public void 死亡済み敵には状態異常が付与されない()
        {
            enemy.TakeDamage(100, ElementType.None); // 死亡させる
            Assert.IsTrue(enemy.IsDead);

            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 3000));

            Assert.IsNull(enemy.ActiveStatus, "死亡済み敵には付与されないはず");
        }

        [Test]
        public void TickStatusで麻痺が時間経過後に消える()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));

            enemy.TickStatus(1000); // 丁度消滅

            Assert.IsNull(enemy.ActiveStatus, "時間切れで消えるはず");
            Assert.IsFalse(enemy.IsParalyzed);
        }

        [Test]
        public void TickStatusで毒ダメージが返る()
        {
            // 1秒あたり10ダメージの毒
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 5000, 10));

            int damage = enemy.TickStatus(1000); // 1秒経過

            Assert.AreEqual(10, damage, "1秒で10ダメージのはず");
        }

        [Test]
        public void TickStatusで毒ダメージが消滅時は返らない()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 500, 10));

            int damage = enemy.TickStatus(1000); // 消滅

            Assert.IsNull(enemy.ActiveStatus, "消滅しているはず");
        }

        // ==================== BattleSystem.Tick()との統合 ====================

        [Test]
        public void BattleSystemTickで毒ダメージが適用される()
        {
            int initialHp = enemy.CurrentHp;
            // 1秒あたり10ダメージの毒を付与
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 5000, 10));

            battle.Tick(1000); // 1秒経過

            Assert.Less(enemy.CurrentHp, initialHp, "毒ダメージでHPが減るはず");
        }

        // ==================== ModifiableCardEffect との統合 ====================

        [Test]
        public void OnDamageトリガーでPoison付与される()
        {
            // 毒（OnDamageトリガー）を持つカードを作成
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.Poison,
                Trigger = DerivativeTrigger.OnDamage,
                Duration = 5.0f,
                Power = 8
            });

            var card = new Card(
                "poison_arrow", "ポイズンアロー", 1,
                CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon,
                20, "毒矢", DamageTargetType.Single,
                modifier: modifier
            );

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, card);
            effect.Execute(context);

            Assert.IsTrue(enemy.IsPoisoned, "ダメージを与えたので毒が付与されるはず");
            Assert.AreEqual(8, enemy.ActiveStatus.DamagePerTick);
        }

        [Test]
        public void OnDamageトリガーでParalyze付与される()
        {
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect
            {
                EffectType = AdditionalEffectType.Paralyze,
                Trigger = DerivativeTrigger.OnDamage,
                Duration = 3.0f
            });

            var card = new Card(
                "para_shot", "パラショット", 1,
                CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon,
                15, "麻痺弾", DamageTargetType.Single,
                modifier: modifier
            );

            var effect = new ModifiableCardEffect(DamageTargetType.Single);
            var context = new BattleContext(battle, card);
            effect.Execute(context);

            Assert.IsTrue(enemy.IsParalyzed, "ダメージを与えたので麻痺が付与されるはず");
            Assert.AreEqual(3000, enemy.ActiveStatus.RemainingMs);
        }
    }
}
