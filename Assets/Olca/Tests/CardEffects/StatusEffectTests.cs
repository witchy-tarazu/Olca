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
        public void TickStatusは失効するフレームでも有効時間分だけ返す()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 500, 10));

            int damage = enemy.TickStatus(1000); // 消滅

            Assert.IsNull(enemy.ActiveStatus, "消滅しているはず");
            Assert.AreEqual(5, damage);
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

        private static IEnumerable<int> SplitSecond(int split)
        {
            if (split == 0) { yield return 1000; yield break; }
            if (split == 1)
            {
                for (int i = 0; i < 62; i++) yield return 16;
                yield return 8; yield break;
            }
            foreach (int step in new[] { 7, 123, 400, 470 }) yield return step;
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void 同じ一秒の毒ダメージは時間分割に依存しない(int split)
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 10));
            int total = 0;
            foreach (int step in SplitSecond(split)) total += enemy.TickStatus(step);
            Assert.AreEqual(10, total);
            Assert.IsNull(enemy.ActiveStatus);
            Assert.AreEqual(0, enemy.TickStatus(1000));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void 有効時間を超えるTickも分割によらず残時間まで適用する(int split)
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 500, 10));
            int total = 0;
            foreach (int step in SplitSecond(split)) total += enemy.TickStatus(step);
            Assert.AreEqual(5, total);
            Assert.IsNull(enemy.ActiveStatus);
        }

        [Test]
        public void 失効後の再付与へ前の端数を持ち越さない()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 500, 3));
            Assert.AreEqual(1, enemy.TickStatus(500));
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 3));
            Assert.AreEqual(0, enemy.TickStatus(167));
            Assert.AreEqual(3, enemy.TickStatus(833));
        }

        [TestCase(StatusEffectType.Poison)]
        [TestCase(StatusEffectType.Paralyze)]
        [TestCase(StatusEffectType.None)]
        public void 上書きは古い端数と効果を残さず新しい状態だけ進める(StatusEffectType replacement)
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 2000, 3));
            Assert.AreEqual(0, enemy.TickStatus(333));
            enemy.ApplyStatus(new StatusEffect(replacement, 1000, 3));
            Assert.AreEqual(0, enemy.TickStatus(1));
            Assert.AreEqual(replacement == StatusEffectType.Poison ? 3 : 0, enemy.TickStatus(999));
            Assert.IsNull(enemy.ActiveStatus);
        }

        [Test]
        public void 敵ごとの端数は独立する()
        {
            var other = new Enemy("other", "別敵", 100, ElementType.None, new Vector2Int(4, 0));
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 3));
            other.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 3));
            Assert.AreEqual(0, enemy.TickStatus(333));
            Assert.AreEqual(0, other.TickStatus(1));
            Assert.AreEqual(1, enemy.TickStatus(1));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void 非正の経過時間は毒の時間と端数を変更しない(int elapsed)
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 3));
            enemy.TickStatus(333);
            Assert.AreEqual(0, enemy.TickStatus(elapsed));
            Assert.AreEqual(667, enemy.ActiveStatus.RemainingMs);
            Assert.AreEqual(1, enemy.TickStatus(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void pauseとcustomは毒の時間と端数を停止する(bool custom)
        {
            enemy.ApplyStun(60000);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 3));
            battle.Tick(333);
            if (custom)
            {
                battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
                battle.OpenCustomScreen(new MockRandomProvider(0));
            }
            else battle.Pause();
            battle.Tick(10000);
            Assert.AreEqual(100, enemy.CurrentHp);
            Assert.AreEqual(667, enemy.ActiveStatus.RemainingMs);
            if (custom) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
            else battle.Resume();
            battle.Tick(1);
            Assert.AreEqual(99, enemy.CurrentHp);
            Assert.AreEqual(100, player.CurrentHp);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void 毒でHPがゼロになると死亡と勝利は一度だけ通知される(int split)
        {
            enemy.ApplyStun(60000);
            enemy.TakeDamage(95, ElementType.None);
            int deaths = 0, endings = 0;
            enemy.OnDeath += () => deaths++;
            battle.OnBattleEnd += _ => endings++;
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 10));
            foreach (int step in SplitSecond(split)) battle.Tick(step);
            battle.Tick(1000);
            Assert.AreEqual(0, enemy.CurrentHp);
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(1, endings);
            Assert.AreEqual(BattleState.Victory, battle.State);
            Assert.AreEqual(100, player.CurrentHp);
        }
    }
}
