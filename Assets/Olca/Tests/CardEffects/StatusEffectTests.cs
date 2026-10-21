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
        [TestCase(StatusEffectType.Poison, 0, 60)]
        [TestCase(StatusEffectType.Poison, 50, 30)]
        [TestCase(StatusEffectType.Poison, 99, 0)]
        [TestCase(StatusEffectType.Poison, 100, 0)]
        [TestCase(StatusEffectType.Poison, 150, 0)]
        [TestCase(StatusEffectType.Paralyze, 50, 30)]
        [TestCase(StatusEffectType.Paralyze, 100, 0)]
        public void 敵定義の時間耐性は付与時にフレームを切り捨て元の効果を変更しない(StatusEffectType type, int resistance, int frames)
        {
            var target = new Enemy("resistant", "検証", 100, ElementType.None, new Vector2Int(1, 0),
                statusResistances: new TimedStatusResistances(poison: resistance, paralyze: resistance));
            var source = new StatusEffect(type, 1000, 60);
            target.ApplyStatus(source);
            Assert.AreEqual(60, source.RemainingFrames);
            Assert.AreEqual(frames > 0, target.ActiveStatus != null);
            if (frames == 0) return;
            Assert.AreEqual(frames, target.ActiveStatus.RemainingFrames);
            Assert.AreEqual(60, target.ActiveStatus.DamagePerTick, "耐性は毒ダメージ量を変えない");
            int damage = 0;
            for (int i = 0; i < frames; i++) damage += target.TickStatusFrame();
            Assert.AreEqual(type == StatusEffectType.Poison ? frames : 0, damage, "最終フレームも毒ダメージを計上する");
            Assert.IsNull(target.ActiveStatus);
            if (resistance > 0) Assert.AreEqual(60, source.RemainingFrames, "短縮した対象の更新を付与元へ返さない");
        }

        [Test]
        public void 状態耐性は加算し生成側の軽減とも乗算せず別状態や属性へ漏らさない()
        {
            var values = TimedStatusResistances.Combine(new TimedStatusResistances(20, 40), new TimedStatusResistances(30, 60));
            var target = new Enemy("e", "検証", 100, ElementType.Fire, Vector2Int.Zero,
                statusResistances: values);
            target.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 7, durationResistancePercent: 20));
            Assert.AreEqual(18, target.ActiveStatus.RemainingFrames, "20+30+20=70%の軽減");
            var active = target.ActiveStatus;
            target.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
            Assert.AreSame(active, target.ActiveStatus, "無効付与は既存毒を上書きしない");
            target.TakeDamage(10, ElementType.Fire);
            Assert.AreEqual(90, target.CurrentHp, "時間耐性は属性HPダメージへ作用しない");
            Assert.AreEqual(int.MaxValue, TimedStatusResistances.Combine(new TimedStatusResistances(int.MaxValue),
                new TimedStatusResistances(int.MaxValue)).Poison);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new TimedStatusResistances(-1));
        }

        [Test]
        public void 耐性で短縮した麻痺の終了後に通常AIが攻撃を再開する()
        {
            var target = new Enemy("e", "検証", 100, ElementType.None, new Vector2Int(1, 0),
                statusResistances: new TimedStatusResistances(paralyze: 50));
            var player = new PlayerCharacter(100);
            var current = new BattleSystem(player, new List<Enemy> { target }, new Deck());
            target.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
            for (int i = 0; i < 30; i++) current.TickFrame(17);
            Assert.IsFalse(target.IsParalyzed); Assert.AreEqual(100, player.CurrentHp);
            current.TickFrame(17); Assert.AreEqual(90, player.CurrentHp);
        }

        [Test]
        public void 耐性で短縮した毒を再付与してもダメージ積算と後段解除の順序を維持する()
        {
            var target = new Enemy("e", "検証", 100, ElementType.None, new Vector2Int(3, 0),
                statusResistances: new TimedStatusResistances(poison: 50));
            var current = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { target }, new Deck());
            target.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 2000, 3));
            for (int i = 0; i < 19; i++) current.TickFrame(17);
            Assert.AreEqual(100, target.CurrentHp);
            current.RunLogicalFrame(() => {
                target.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 2000, 3));
                current.TickFrame(17);
            });
            Assert.AreEqual(99, target.CurrentHp, "再付与で20フレーム目の毒を遅延しない");
            Assert.AreEqual(60, target.ActiveStatus.RemainingFrames, "付与フレームで新しい時間を減らさない");
            current.RunLogicalFrame(() => {
                target.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
                target.ClearStatus(StatusEffectType.Paralyze);
                current.TickFrame(17);
            });
            Assert.IsFalse(target.IsParalyzed); Assert.IsTrue(target.IsPoisoned);
        }

        [Test]
        public void 隣接敵は麻痺の残時間中に攻撃せず解除した後に基本攻撃を再開する()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
            battle.Tick(999);
            Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(1, enemy.ActiveStatus.RemainingMs);
            battle.Tick(1);
            Assert.IsFalse(enemy.IsParalyzed); Assert.AreEqual(90, player.CurrentHp);
        }

        [TestCase(BattlePauseReason.CustomScreen)]
        [TestCase(BattlePauseReason.User)]
        public void 麻痺残時間と攻撃はカスタムまたはユーザーポーズ中には進まない(BattlePauseReason reason)
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
            battle.Pause(reason); battle.Tick(2000);
            Assert.AreEqual(1000, enemy.ActiveStatus.RemainingMs); Assert.AreEqual(100, player.CurrentHp);
            battle.Resume(reason); battle.Tick(999);
            Assert.AreEqual(100, player.CurrentHp); battle.Tick(1); Assert.AreEqual(90, player.CurrentHp);
        }

        [Test]
        public void 麻痺中の被ダメージは解除せず再付与は新しい残時間で上書きする()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000)); battle.Tick(100);
            enemy.TakeDamage(1, ElementType.None);
            Assert.IsTrue(enemy.IsParalyzed); Assert.AreEqual(900, enemy.ActiveStatus.RemainingMs); Assert.AreEqual(99, enemy.CurrentHp);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 500));
            Assert.AreEqual(500, enemy.ActiveStatus.RemainingMs);
            battle.Tick(499); Assert.AreEqual(100, player.CurrentHp);
            battle.Tick(1); Assert.AreEqual(90, player.CurrentHp);
        }

        [Test]
        public void 麻痺は既存AIの攻撃受付だけを止めクールダウン値を変更しない()
        {
            var ai = new EnemyAIBehavior(enemy, new List<IEnemyAIAction> { new BasicAttackAction(10, 2000) });
            var context = new AIContext(enemy, player, battle.Field, new MockRandomProvider(), logger);
            ai.Update(0, context); Assert.AreEqual(2000, ai.CurrentCooldownMs);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 3000));
            ai.Update(2000, context); Assert.AreEqual(0, ai.CurrentCooldownMs); Assert.IsFalse(ai.CanAct);
            player.TickInvincible(3000); enemy.TickStatus(3000); ai.Update(0, context);
            Assert.AreEqual(80, player.CurrentHp);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 勝敗通知前に敵と召喚物の麻痺だけを解除する(bool defeat)
        {
            var summon = new Enemy("summon", "召喚", 100, ElementType.None, new Vector2Int(4, 0), isSummoned: true);
            var poisoned = new Enemy("poisoned", "毒の召喚", 100, ElementType.None, new Vector2Int(5, 0), isSummoned: true);
            battle.AddSummon(summon, new List<IEnemyAIAction> { new BasicAttackAction() });
            battle.AddSummon(poisoned, new List<IEnemyAIAction> { new BasicAttackAction() });
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000)); summon.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
            var poison = new StatusEffect(StatusEffectType.Poison, 2000, 1); poisoned.ApplyStatus(poison);
            int notified = 0; battle.OnBattleEnd += _ =>
            {
                notified++; Assert.IsNull(enemy.ActiveStatus); Assert.IsNull(summon.ActiveStatus);
                Assert.AreSame(poison, poisoned.ActiveStatus, "毒などの持越し規則を麻痺と一律にしない");
            };
            if (defeat) player.TakeDamage(100); else enemy.TakeDamage(100, ElementType.None);
            Assert.AreEqual(1, notified); Assert.IsFalse(enemy.IsParalyzed); Assert.IsFalse(summon.IsParalyzed);
        }

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
        public void 異種の状態異常は併存する()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 3000));
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 5000, 5));

            Assert.IsTrue(enemy.IsParalyzed, "毒付与で麻痺を失わない");
            Assert.IsTrue(enemy.IsPoisoned, "麻痺中も毒を保持する");
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
        public void 同じ毒の更新も別状態の付与も既存毒の積算を失わない(StatusEffectType replacement)
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 2000, 3));
            Assert.AreEqual(0, enemy.TickStatus(333));
            enemy.ApplyStatus(new StatusEffect(replacement, 1000, 3));
            Assert.AreEqual(1, enemy.TickStatus(1));
            Assert.AreEqual(2, enemy.TickStatus(999));
            Assert.AreEqual(replacement != StatusEffectType.Poison, enemy.IsPoisoned);
            Assert.IsFalse(enemy.IsParalyzed, "追加した麻痺だけ期限切れになる");
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
        public void 毒単独はHP1で止まり後続の通常攻撃で死亡と勝利を通知する(int split)
        {
            enemy.ApplyStun(60000);
            enemy.TakeDamage(95, ElementType.None);
            int deaths = 0, endings = 0;
            enemy.OnDeath += () => deaths++;
            battle.OnBattleEnd += _ => endings++;
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 10));
            foreach (int step in SplitSecond(split)) battle.Tick(step);
            battle.Tick(1000);
            Assert.AreEqual(1, enemy.CurrentHp);
            Assert.AreEqual(0, deaths);
            Assert.AreEqual(0, endings);
            Assert.IsFalse(battle.IsGameOver);
            enemy.TakeDamage(1, ElementType.None);
            Assert.AreEqual(0, enemy.CurrentHp);
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(1, endings);
            Assert.AreEqual(BattleState.Victory, battle.State);
            Assert.AreEqual(100, player.CurrentHp);
        }
        [TestCase(0)]
        [TestCase(16)]
        [TestCase(1000)]
        public void 毒のHP1下限は互換msではなく論理フレームで進行する(int compatibleMs)
        {
            enemy.ApplyStun(60000);
            enemy.TakeDamage(95, ElementType.None);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 60));
            for (int frame = 0; frame < 60; frame++)
            {
                battle.TickFrame(compatibleMs);
                Assert.AreEqual(System.Math.Max(1, 4 - frame), enemy.CurrentHp);
                Assert.IsFalse(battle.IsGameOver);
            }
            Assert.IsNull(enemy.ActiveStatus);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, int.MaxValue));
            battle.TickFrame(compatibleMs);
            Assert.AreEqual(1, enemy.CurrentHp, "HP1で大きな毒を受けても倒れない");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 毒と通常攻撃が同じ更新内に重なれば通常攻撃で倒れる(bool attackFirst)
        {
            enemy.ApplyStun(60000);
            enemy.TakeDamage(98, ElementType.None);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 60));
            int deaths = 0;
            enemy.OnDeath += () => deaths++;
            battle.RunLogicalFrame(() => {
                if (attackFirst) enemy.TakeDamage(1, ElementType.None);
                battle.TickFrame(17);
                if (!attackFirst) enemy.TakeDamage(1, ElementType.None);
            });
            Assert.AreEqual(0, enemy.CurrentHp);
            Assert.AreEqual(1, deaths);
            Assert.AreEqual(BattleState.Victory, battle.State);
            enemy.TakeDamage(1, ElementType.None);
            Assert.AreEqual(1, deaths, "死亡済みへの追加攻撃で通知を繰り返さない");
        }
        [TestCase(false)]
        [TestCase(true)]
        public void 毒カード再使用は積算を維持しポーズとカスタム中は停止する(bool custom)
        {
            enemy.ApplyStun(60000);
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect {
                EffectType = AdditionalEffectType.Poison, Trigger = DerivativeTrigger.OnDamage,
                Duration = 1f, Power = 3 });
            var card = new Card("fixture-poison", "検証用毒", 1, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.Canon, 1, "検証", DamageTargetType.Single, modifier: modifier);
            System.Action use = () => {
                battle.CardUsage.EnqueueCards(new List<Card> { card });
                Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());
                Assert.AreEqual(60, enemy.ActiveStatus.RemainingFrames);
            };
            use();
            for (int repeat = 0; repeat < 3; repeat++)
            {
                int hp = enemy.CurrentHp;
                int remaining = enemy.ActiveStatus.RemainingFrames;
                for (int frame = 0; frame < 19; frame++) battle.TickFrame(17);
                Assert.AreEqual(hp, enemy.CurrentHp);
                if (custom)
                {
                    battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
                    battle.OpenCustomScreen(new MockRandomProvider(0));
                }
                else battle.Pause();
                for (int frame = 0; frame < 120; frame++) battle.TickFrame(1000);
                Assert.AreEqual(hp, enemy.CurrentHp);
                Assert.AreEqual(remaining - 19, enemy.ActiveStatus.RemainingFrames);
                if (custom) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
                else battle.Resume();
                use();
                hp = enemy.CurrentHp;
                battle.TickFrame(17);
                Assert.AreEqual(hp - 1, enemy.CurrentHp, "再付与の直後も元の20フレーム目に毒ダメージ");
                Assert.AreEqual(59, enemy.ActiveStatus.RemainingFrames);
            }
            for (int frame = 0; frame < 59; frame++) battle.TickFrame(17);
            Assert.IsNull(enemy.ActiveStatus);
            use();
            int afterRestart = enemy.CurrentHp;
            for (int frame = 0; frame < 19; frame++) battle.TickFrame(17);
            Assert.AreEqual(afterRestart, enemy.CurrentHp, "失効後の新規付与は最初から積算する");
            battle.TickFrame(17);
            Assert.AreEqual(afterRestart - 1, enemy.CurrentHp);
        }
        [TestCase(100, false, StatusEffectType.Poison)]
        [TestCase(100, true, StatusEffectType.Poison)]
        [TestCase(100, false, StatusEffectType.Paralyze)]
        [TestCase(100, true, StatusEffectType.Paralyze)]
        [TestCase(101, false, StatusEffectType.Poison)]
        [TestCase(101, true, StatusEffectType.Poison)]
        [TestCase(int.MaxValue, false, StatusEffectType.Paralyze)]
        [TestCase(int.MaxValue, true, StatusEffectType.Paralyze)]
        public void 無効な再付与はカードで付与した毒の状態と残時間と積算を変えない(int resistance, bool logical, StatusEffectType type)
        {
            enemy.ApplyStun(60000);
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect {
                EffectType = AdditionalEffectType.Poison, Trigger = DerivativeTrigger.OnDamage,
                Duration = 2f, Power = 3 });
            var card = new Card("fixture-poison", "検証用毒", 1, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.Canon, 1, "検証", DamageTargetType.Single, modifier: modifier);
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());
            if (logical) for (int frame = 0; frame < 19; frame++) battle.TickFrame(17);
            else battle.Tick(333);
            var original = enemy.ActiveStatus;
            int frames = original.RemainingFrames, ms = original.RemainingMs, hp = enemy.CurrentHp;
            for (int repeat = 0; repeat < 3; repeat++)
                enemy.ApplyStatus(new StatusEffect(type, 5000, 60, durationResistancePercent: resistance));
            Assert.AreSame(original, enemy.ActiveStatus);
            Assert.AreEqual(frames, original.RemainingFrames);
            Assert.AreEqual(ms, original.RemainingMs);
            Assert.AreEqual(hp, enemy.CurrentHp);
            // 無効な付与の後でも、有効な同種再付与で時間を更新でき、進行は保持される。
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 3));
            Assert.AreEqual(60, enemy.ActiveStatus.RemainingFrames);
            if (logical) battle.TickFrame(17); else battle.Tick(1);
            Assert.AreEqual(hp - 1, enemy.CurrentHp);
        }

        [TestCase(0, 120)]
        [TestCase(99, 1)]
        [TestCase(100, 0)]
        [TestCase(101, 0)]
        [TestCase(int.MaxValue, 0)]
        public void 時間耐性の境界は付与可否へ反映され属性耐性と独立する(int resistance, int expectedFrames)
        {
            var incoming = new StatusEffect(StatusEffectType.Paralyze, 2000, durationResistancePercent: resistance);
            Assert.AreEqual(expectedFrames, incoming.RemainingFrames);
            enemy.ApplyStatus(incoming);
            if (expectedFrames == 0) Assert.IsNull(enemy.ActiveStatus);
            else Assert.AreSame(incoming, enemy.ActiveStatus);
            Assert.AreSame(ElementResistances.Neutral, enemy.Resistances);
        }

        [Test]
        public void 切捨てで0フレームの効果は既存麻痺を解除しない()
        {
            var original = new StatusEffect(StatusEffectType.Paralyze, 1000);
            enemy.ApplyStatus(original);
            battle.TickFrame(17);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1, 60, durationResistancePercent: 99));
            Assert.AreSame(original, enemy.ActiveStatus);
            Assert.AreEqual(59, original.RemainingFrames);
            Assert.IsTrue(enemy.IsParalyzed);
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new StatusEffect(StatusEffectType.Poison, 1000, durationResistancePercent: -1));
        }
        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void 通常カードの複数状態付与は順序によらず保持され停止再開と個別解除を維持する(bool poisonFirst, bool clearPoison)
        {
            var modifier = new CardModifier();
            var poison = new AdditionalEffect { EffectType=AdditionalEffectType.Poison, Trigger=DerivativeTrigger.OnDamage, Duration=2f, Power=60 };
            var paralysis = new AdditionalEffect { EffectType=AdditionalEffectType.Paralyze, Trigger=DerivativeTrigger.OnDamage, Duration=1f };
            modifier.AdditionalEffects.Add(poisonFirst ? poison : paralysis);
            modifier.AdditionalEffects.Add(poisonFirst ? paralysis : poison);
            var card = new Card("fixture-coexist", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 1, "検証", DamageTargetType.Single, modifier: modifier);
            battle.RunLogicalFrame(() => {
                battle.CardUsage.EnqueueCards(new List<Card> { card });
                Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());
                Assert.IsFalse(enemy.IsParalyzed, "同フレーム途中に麻痺を有効化しない");
                battle.TickFrame(17);
            });
            Assert.IsTrue(enemy.IsParalyzed); Assert.IsTrue(enemy.IsPoisoned);
            Assert.AreEqual(120, enemy.GetStatus(StatusEffectType.Poison).RemainingFrames);
            Assert.AreEqual(60, enemy.GetStatus(StatusEffectType.Paralyze).RemainingFrames);
            int hp = enemy.CurrentHp;
            for (int i=0;i<6;i++) battle.TickFrame(17);
            Assert.AreEqual(hp-6, enemy.CurrentHp, "麻痺でも毒は進行する");
            battle.Pause(BattlePauseReason.User);
            for(int i=0;i<20;i++) battle.TickFrame(17);
            battle.Resume(BattlePauseReason.User);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE); battle.OpenCustomScreen(new MockRandomProvider());
            for(int i=0;i<20;i++) battle.TickFrame(17);
            Assert.AreEqual(114, enemy.GetStatus(StatusEffectType.Poison).RemainingFrames);
            Assert.AreEqual(54, enemy.GetStatus(StatusEffectType.Paralyze).RemainingFrames);
            battle.CustomScreen.Close();
            var cleared = clearPoison ? StatusEffectType.Poison : StatusEffectType.Paralyze;
            battle.RunLogicalFrame(() => {
                enemy.ClearStatus(cleared); enemy.ApplyStatus(new StatusEffect(cleared,1000,60));
                battle.TickFrame(17);
            });
            Assert.IsNull(enemy.GetStatus(cleared), "同フレーム再付与より後段解除を優先する");
            Assert.AreEqual(hp-7, enemy.CurrentHp, "解除フレームにも毒を反映する");
            var retained = clearPoison ? StatusEffectType.Paralyze : StatusEffectType.Poison;
            Assert.AreEqual(clearPoison ? 53 : 113, enemy.GetStatus(retained).RemainingFrames);
            battle.TickFrame(17);
            Assert.AreEqual(hp-(clearPoison ? 7 : 8), enemy.CurrentHp);
        }

        [Test]
        public void 併存からの戦闘終了は麻痺だけ解除し毒と別種に触れない()
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison,2000,3));
            Assert.AreEqual(0,enemy.TickStatus(333));
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,2000));
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,2000));
            battle.Player.TakeDamage(9999);
            Assert.IsTrue(battle.IsGameOver); Assert.IsFalse(enemy.IsParalyzed);
            Assert.IsTrue(enemy.IsPoisoned); Assert.IsNotNull(enemy.GetStatus(StatusEffectType.Blind));
            Assert.AreEqual(1,enemy.TickStatus(1), "戦闘終了解除で毒の端数を消さない");
        }

        [Test]
        public void 敵併存状態の論理トレースは描画速度と追いつきに依存しない()
        {
            List<string> Trace(int fps) {
                var e=new Enemy("fixture","検証",1000,ElementType.None,new Vector2Int(3,0));
                var b=new BattleSystem(new PlayerCharacter(100),new List<Enemy>{e},new Deck());
                e.ApplyStatus(new StatusEffect(StatusEffectType.Poison,2000,3));
                e.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000));
                var clock=new Olca.Core.Logic.Integration.LogicalFrameClock(); var trace=new List<string>();long wall=0;
                void Tick(long f,int ms) {
                    if(f==10)b.Pause(BattlePauseReason.User); if(f==20)b.Resume(BattlePauseReason.User);
                    if(f==30){b.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);b.OpenCustomScreen(new MockRandomProvider());}
                    if(f==40)b.CustomScreen.Close();
                    b.RunLogicalFrame(()=>{
                        if(f==50)e.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,500));
                        if(f==60)e.ClearStatus(StatusEffectType.Paralyze);
                        b.TickFrame(ms);
                    });
                    trace.Add(f+":"+e.CurrentHp+":"+e.GetStatus(StatusEffectType.Poison)?.RemainingFrames+":"+e.GetStatus(StatusEffectType.Paralyze)?.RemainingFrames);
                }
                if(fps==0){clock.Accumulate(Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond*3/2);clock.Drain(Tick);}
                else for(int i=1;clock.Frame<90;i++){long next=(long)i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(next-wall);wall=next;clock.Drain(Tick);}
                return trace.GetRange(0,90);
            }
            var expected=Trace(60); CollectionAssert.AreEqual(expected,Trace(30));CollectionAssert.AreEqual(expected,Trace(120));CollectionAssert.AreEqual(expected,Trace(0));
        }

    }
}


namespace Olca.Tests.CardEffects
{
    [TestFixture]
    public class PoisonHealingFrameTests
    {
        private static readonly int[][] Orders={new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}};
        [TestCase(5,10,10,0,5)][TestCase(5,10,0,0,1)][TestCase(5,10,2,0,1)]
        [TestCase(5,10,10,5,0)][TestCase(5,10,10,4,1)][TestCase(95,10,100,0,100)]
        [TestCase(1,int.MaxValue,int.MaxValue,0,1)][TestCase(5,0,int.MaxValue,0,100)]
        [TestCase(5,int.MaxValue,0,int.MaxValue,0)]
        public void プレイヤーの毒回復通常攻撃を全順序で集計しHPを一度通知する(int hp,int poison,int heal,int damage,int expected)
        {
            foreach(var order in Orders){
                var p=new PlayerCharacter(100);p.SetHp(hp);
                var enemy=new Enemy("e","敵",100,ElementType.None,new Vector2Int(5,0));
                var battle=new BattleSystem(p,new List<Enemy>{enemy},new Deck());
                int notices=0,deaths=0,ends=0;
                p.OnHpChanged+=(_,__)=>{notices++;Assert.AreEqual(expected,p.CurrentHp);};p.OnDeath+=()=>deaths++;battle.OnBattleEnd+=_=>ends++;
                battle.RunLogicalFrame(()=>{foreach(int action in order){
                    if(action==0)p.TakePoisonDamage(poison);else if(action==1)p.Heal(heal);else if(damage>0)p.TakeDamage(damage,poiseDamage:0);
                }});
                Assert.AreEqual(expected,p.CurrentHp);Assert.AreEqual(1,notices);Assert.AreEqual(expected==0?1:0,deaths);Assert.AreEqual(deaths,ends);
            }
        }
        [TestCase(5,10,0,5)][TestCase(5,0,0,1)][TestCase(5,2,0,1)]
        [TestCase(5,10,5,0)][TestCase(5,10,4,1)][TestCase(95,100,0,100)]
        public void 敵の通常毒更新と回復も全順序で集計して死亡通知を一度だけ出す(int hp,int heal,int damage,int expected)
        {
            foreach(var order in Orders){
                var p=new PlayerCharacter(100);var enemy=new Enemy("e","敵",100,ElementType.None,new Vector2Int(5,0));
                var battle=new BattleSystem(p,new List<Enemy>{enemy},new Deck());enemy.TakeDamage(100-hp,ElementType.None);enemy.ApplyStun(60000);
                enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison,1000,600));
                int poisonDamage=0;enemy.OnDamaged+=(amount,_)=>poisonDamage+=amount;
                int notices=0,deaths=0,ends=0;enemy.OnHpChanged+=(_,__)=>{notices++;Assert.AreEqual(expected,enemy.CurrentHp);};enemy.OnDeath+=()=>deaths++;battle.OnBattleEnd+=_=>ends++;
                battle.RunLogicalFrame(()=>{foreach(int action in order){
                    if(action==0)battle.TickFrame(17);else if(action==1)enemy.Heal(heal);else if(damage>0)enemy.TakeDamage(damage,ElementType.None,poiseDamage:0);
                }});
                Assert.AreEqual(expected,enemy.CurrentHp);Assert.AreEqual(1,notices);Assert.AreEqual(expected==0?1:0,deaths);Assert.AreEqual(deaths,ends);
                Assert.AreEqual(System.Math.Min(10,hp+heal-1)+damage,poisonDamage,"毒の通知量も回復を含めて確定する");
            }
        }
        [Test] public void 通常AIの受理ヒットによる回復と毒を同じフレームへ集計する()
        {
            var p=new PlayerCharacter(100,new Vector2Int(0,0));var enemy=new Enemy("e","敵",100,ElementType.None,new Vector2Int(5,0));
            var battle=new BattleSystem(p,new List<Enemy>{enemy},new Deck());enemy.TakeDamage(95,ElementType.None);
            battle.AddSummon(new Enemy("healer","検証AI",10,ElementType.None,new Vector2Int(1,0),isSummoned:true),
                new List<IEnemyAIAction>{new BasicAttackAction(1,100000,poiseDamage:0,onHit:_=>enemy.Heal(10))});
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison,1000,600));battle.TickFrame(17);
            Assert.AreEqual(5,enemy.CurrentHp);Assert.AreEqual(99,p.CurrentHp);Assert.IsFalse(battle.IsGameOver);
        }
        [TestCase(false)][TestCase(true)]
        public void 通常のヒールカードと毒を同じフレームに受けても処理順でHPが変わらない(bool poisonFirst)
        {
            var p=new PlayerCharacter(100);p.SetHp(5);
            var heal=new Card("heal","回復",1,CardRarity.Normal,CardCategory.Defense,ElementType.None,CardSeries.None,10,"fixture");
            var battle=new BattleSystem(p,new List<Enemy>{new Enemy("e","敵",100,ElementType.None,new Vector2Int(5,0))},new Deck(new List<Card>{heal}));
            battle.CardUsage.EnqueueCards(new List<Card>{heal});int notices=0;p.OnHpChanged+=(_,__)=>notices++;
            battle.RunLogicalFrame(()=>{
                if(poisonFirst)p.TakePoisonDamage(10);
                Assert.IsTrue(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());
                if(!poisonFirst)p.TakePoisonDamage(10);
            });
            Assert.AreEqual(5,p.CurrentHp);Assert.AreEqual(1,notices);Assert.AreEqual(0,battle.CardUsage.RemainingCount);Assert.AreEqual(10,heal.Power);
        }

        [Test] public void 確定済みの敵死亡は回復で復活せず負値は拒否する()
        {
            var e=new Enemy("e","敵",5,ElementType.None,new Vector2Int(1,0));int deaths=0;e.OnDeath+=()=>deaths++;
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>e.Heal(-1));e.TakeDamage(5,ElementType.None);e.Heal(10);
            Assert.AreEqual(0,e.CurrentHp);Assert.AreEqual(1,deaths);
        }
    }
}


namespace Olca.Tests.CardEffects
{
    [TestFixture]
    public class NamedEnemyPoisonTests
    {
        private Enemy enemy;
        private BattleSystem battle;
        private void Setup(int resistance=0,int hp=500)
        {
            enemy=new Enemy("target","検証",hp,ElementType.None,new Vector2Int(2,0),statusResistances:new TimedStatusResistances(poison:resistance),poiseDefinition:new EnemyPoiseDefinition(100,3));
            battle=new BattleSystem(new PlayerCharacter(500,new Vector2Int(1,0)),new List<Enemy>{enemy},new Deck());enemy.ApplyStun(60000);
        }
        private static StatusEffect Poison(string kind,int damage=7,int interval=3,int duration=1000,int resistance=0)
            =>new StatusEffect(StatusEffectType.Poison,duration,damage,resistance,kind,interval);
        private void Frames(int count){for(int i=0;i<count;i++)battle.TickFrame(17);}
        [Test]
        public void 毒と上位毒は独立間隔で終端にも発生し同種だけ更新する()
        {
            Setup();enemy.ApplyStatus(Poison("poison",7,3,100));enemy.ApplyStatus(Poison("venom",11,4,200));
            Frames(2);Assert.AreEqual(500,enemy.CurrentHp);Assert.AreEqual(1,enemy.FramesUntilPoisonDamage("poison"));Assert.AreEqual(2,enemy.FramesUntilPoisonDamage("venom"));
            Frames(1);Assert.AreEqual(493,enemy.CurrentHp);Frames(1);Assert.AreEqual(482,enemy.CurrentHp);Frames(2);
            Assert.AreEqual(475,enemy.CurrentHp);Assert.IsNull(enemy.GetPoison("poison"));Assert.AreEqual(6,enemy.GetPoison("venom").RemainingFrames);
            Frames(6);Assert.AreEqual(453,enemy.CurrentHp);Assert.IsFalse(enemy.IsPoisoned);Assert.IsNull(enemy.GetStatus(StatusEffectType.Poison));
        }
        [Test]
        public void 再付与は残時間だけを新期間へ更新し別毒と次回発生までの時計を保持する()
        {
            Setup();enemy.ApplyStatus(Poison("poison",7,6));enemy.ApplyStatus(Poison("venom",11,9));Frames(2);
            battle.RunLogicalFrame(()=>{enemy.ApplyStatus(Poison("poison",7,6,200));battle.TickFrame(17);});
            Assert.AreEqual(12,enemy.GetPoison("poison").RemainingFrames);Assert.AreEqual(3,enemy.FramesUntilPoisonDamage("poison"));Assert.AreEqual(6,enemy.FramesUntilPoisonDamage("venom"));
            Frames(3);Assert.AreEqual(493,enemy.CurrentHp);Frames(3);Assert.AreEqual(482,enemy.CurrentHp);
        }
        [TestCase(false)] [TestCase(true)]
        public void 期限成立フレームの前後どちらで再付与しても時計を戻さず最終発生は一回(bool afterTick)
        {
            Setup();enemy.ApplyStatus(Poison("poison",7,3,100));Frames(5);
            battle.RunLogicalFrame(()=>{if(!afterTick)enemy.ApplyStatus(Poison("poison",7,3,100));battle.TickFrame(17);if(afterTick)enemy.ApplyStatus(Poison("poison",7,3,100));});
            Assert.AreEqual(486,enemy.CurrentHp);Assert.AreEqual(6,enemy.GetPoison("poison").RemainingFrames);Assert.AreEqual(3,enemy.FramesUntilPoisonDamage("poison"));Frames(3);Assert.AreEqual(479,enemy.CurrentHp);
        }
        [TestCase(false)] [TestCase(true)]
        public void 共通解除は発生フレームの既存毒量を反映してから全毒と新規予約を取り除く(bool clearAfter)
        {
            Setup();enemy.ApplyStatus(Poison("poison",7,3));enemy.ApplyStatus(Poison("venom",11,3));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,1000));Frames(2);
            battle.RunLogicalFrame(()=>{enemy.ApplyStatus(Poison("third",13,2));if(!clearAfter)enemy.ClearStatus(StatusEffectType.Poison);battle.TickFrame(17);if(clearAfter)enemy.ClearStatus(StatusEffectType.Poison);});
            Assert.AreEqual(482,enemy.CurrentHp);Assert.IsFalse(enemy.IsPoisoned);Assert.IsNull(enemy.GetPoison("third"));Assert.IsTrue(enemy.IsParalyzed);Assert.IsNotNull(enemy.GetStatus(StatusEffectType.Blind));Frames(12);Assert.AreEqual(482,enemy.CurrentHp);
        }
        [Test]
        public void 全毒の付与は次フレームから有効で同フレーム解除なら発生しない()
        {
            Setup();battle.RunLogicalFrame(()=>{enemy.ApplyStatus(Poison("poison",7,1));enemy.ApplyStatus(Poison("venom",11,1));battle.TickFrame(17);});
            Assert.AreEqual(500,enemy.CurrentHp);Assert.AreEqual(60,enemy.GetPoison("poison").RemainingFrames);Frames(1);Assert.AreEqual(482,enemy.CurrentHp);
            enemy.ClearStatus(StatusEffectType.Poison);battle.RunLogicalFrame(()=>{enemy.ApplyStatus(Poison("poison",7,1));battle.TickFrame(17);enemy.ClearStatus(StatusEffectType.Poison);});Frames(3);Assert.AreEqual(482,enemy.CurrentHp);
        }
        [Test]
        public void 共通時間耐性は各毒の期間だけを加算軽減し短い毒と完全無効で既存時計を変えない()
        {
            Setup(25);var a=Poison("poison",2,3,resistance:25);var b=Poison("venom",3,4,resistance:25);enemy.ApplyStatus(a);enemy.ApplyStatus(b);
            Assert.AreEqual(30,enemy.GetPoison("poison").RemainingFrames);Frames(30);Assert.AreEqual(459,enemy.CurrentHp);Assert.IsFalse(enemy.IsPoisoned);Assert.AreEqual(45,a.RemainingFrames);Assert.AreEqual(45,b.RemainingFrames);
            enemy.ApplyStatus(Poison("poison",7,6));Frames(2);var current=enemy.GetPoison("poison");enemy.ApplyStatus(Poison("poison",99,1,resistance:100));Assert.AreSame(current,enemy.GetPoison("poison"));Assert.AreEqual(4,enemy.FramesUntilPoisonDamage("poison"));
            enemy.ApplyStatus(Poison("short",99,6,1));Frames(1);Assert.IsNull(enemy.GetPoison("short"));Assert.AreEqual(459,enemy.CurrentHp);
        }
        [Test]
        public void 同じ定義を複数敵と複数毒へ渡しても残時間と間隔を共有しない()
        {
            Setup();var definition=Poison("poison",7,3);var other=new Enemy("other","検証",500,ElementType.None,new Vector2Int(4,0));enemy.ApplyStatus(definition);other.ApplyStatus(definition);
            Frames(2);Assert.AreEqual(60,definition.RemainingFrames);Assert.AreEqual(60,other.GetPoison("poison").RemainingFrames);Assert.AreEqual(1,enemy.FramesUntilPoisonDamage("poison"));Assert.AreEqual(3,other.FramesUntilPoisonDamage("poison"));
        }
        [TestCase(false)] [TestCase(true)]
        public void ポーズとカスタム中は全毒の時計が止まり麻痺と盲目を失わず復帰する(bool custom)
        {
            Setup();enemy.ApplyStatus(Poison("poison",7,3));enemy.ApplyStatus(Poison("venom",11,4));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000));Frames(2);
            if(custom){battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());}else battle.Pause(BattlePauseReason.User);
            Frames(30);Assert.AreEqual(500,enemy.CurrentHp);Assert.AreEqual(58,enemy.GetPoison("venom").RemainingFrames);Assert.AreEqual(1,enemy.FramesUntilPoisonDamage("poison"));
            if(custom)battle.CustomScreen.Close();else battle.Resume(BattlePauseReason.User);Frames(1);Assert.AreEqual(493,enemy.CurrentHp);Frames(1);Assert.AreEqual(482,enemy.CurrentHp);Assert.IsTrue(enemy.IsParalyzed);
        }
        [Test]
        public void 同時毒を回復と通常被弾へ合算し毒だけでは倒さず強靭度や無敵を発生させない()
        {
            foreach(bool attackFirst in new[]{false,true}){
                Setup(hp:5);enemy.ApplyStatus(Poison("poison",7,1));enemy.ApplyStatus(Poison("venom",11,1));
                battle.RunLogicalFrame(()=>{enemy.Heal(20);if(attackFirst)enemy.TakeDamage(4,ElementType.None,poiseDamage:0);battle.TickFrame(17);if(!attackFirst)enemy.TakeDamage(4,ElementType.None,poiseDamage:0);});
                Assert.AreEqual(3,enemy.CurrentHp);Assert.AreEqual(0,enemy.AccumulatedPoiseDamage);Assert.IsFalse(enemy.IsInvincible);
            }
            Setup(hp:5);enemy.ApplyStatus(Poison("poison",int.MaxValue,1));enemy.ApplyStatus(Poison("venom",int.MaxValue,1));
            battle.RunLogicalFrame(()=>{enemy.Heal(int.MaxValue);battle.TickFrame(17);});Assert.AreEqual(1,enemy.CurrentHp,"合計をintへ切り詰めてHP5を残さない");
            enemy.TakeDamage(1,ElementType.None);Assert.IsTrue(enemy.IsDead);Assert.IsTrue(battle.IsGameOver);
        }
        [Test]
        public void 旧毎秒毒と明示間隔の同種は二重スロットにせず切替と別状態解除でも時計を保つ()
        {
            Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison,2000,60));Frames(10);Assert.AreEqual(490,enemy.CurrentHp);
            enemy.ApplyStatus(Poison("poison",7,3,2000));Assert.AreEqual(50,enemy.FramesUntilPoisonDamage("poison"));Frames(49);Assert.AreEqual(490,enemy.CurrentHp);Frames(1);Assert.AreEqual(483,enemy.CurrentHp);Frames(3);Assert.AreEqual(476,enemy.CurrentHp);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,1000));enemy.ClearStatus(StatusEffectType.Blind);Assert.AreEqual(3,enemy.FramesUntilPoisonDamage("poison"));
        }
        [Test]
        public void 同種の新しい外部量間隔は次回までの残カウントを保ち以後に採用する()
        {
            Setup();enemy.ApplyStatus(Poison("poison",7,6));Frames(2);enemy.ApplyStatus(Poison("poison",11,3));Assert.AreEqual(4,enemy.FramesUntilPoisonDamage("poison"));Frames(3);Assert.AreEqual(500,enemy.CurrentHp);Frames(1);Assert.AreEqual(489,enemy.CurrentHp);Frames(3);Assert.AreEqual(478,enemy.CurrentHp);
        }
        private static Card MasterCard(string kind="venom",int? interval=3)
        {
            return Card.FromMaster(new Olca.Core.Domain.Master.CardMaster{Id="master-poison",Name="検証",Category=CardCategory.Attack,Power=1,AdditionalEffectTypes="Poison",DerivativeTrigger="OnDamage",AdditionalEffectPower=7,AdditionalEffectDuration=1,PoisonKind=kind,PoisonIntervalFrames=interval});
        }
        [Test]
        public void マスター種別間隔は通常カード使用へ届き加工複製で保持される()
        {
            Setup();var card=MasterCard();battle.RunLogicalFrame(()=>{battle.CardUsage.EnqueueCards(new List<Card>{card});Assert.IsTrue(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());battle.TickFrame(17);});
            Assert.AreEqual(499,enemy.CurrentHp);Assert.IsNotNull(enemy.GetPoison("venom"));Frames(3);Assert.AreEqual(492,enemy.CurrentHp);
            foreach(var copy in new[]{card.Clone(),card.ApplyTuning(new Card("tune","調律",1,CardRarity.Normal,CardCategory.Tuning,ElementType.None,CardSeries.None,0,"検証")),card.WithPowerMultiplier(2)}){Assert.AreEqual("venom",copy.Modifier.AdditionalEffects[0].PoisonKind);Assert.AreEqual(3,copy.Modifier.AdditionalEffects[0].PoisonIntervalFrames);}
            Assert.Throws<System.ArgumentException>(()=>MasterCard("venom",null));Assert.Throws<System.ArgumentException>(()=>MasterCard("",3));Assert.Throws<System.ArgumentException>(()=>MasterCard("poison",0));
            Assert.Throws<System.ArgumentException>(()=>Poison("venom",-1,3));
        }
        [Test]
        public void 未指定の旧毒カードのhashを維持し新しい種別や間隔だけを定義照合へ加える()
        {
            string Hash(Card card)=>(string)typeof(Card).Assembly.GetType("Olca.Core.Logic.Dungeon.CheckpointValues").GetMethod("CardHash",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{card});
            var m=new CardModifier();m.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Poison,Trigger=DerivativeTrigger.OnDamage,Power=3,Duration=2});var legacy=new Card("legacy-poison","旧毒",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,10,"旧互換",modifier:m);
            Assert.AreEqual("A7A58D016E0B1011DA40CD733E0C703791D78D9EB42E1D683B435EF3D12F4A65",Hash(legacy));Assert.AreNotEqual(Hash(MasterCard()),Hash(MasterCard("poison")));Assert.AreNotEqual(Hash(MasterCard()),Hash(MasterCard("venom",4)));
        }
        [Test]
        public void 新しい毒定義のファイル再開は開始境界で敵状態を復元せず定義変更なら元保存を保持する()
        {
            var root=MasterCard();var config=DungeonConfig.CreateTestDungeon(10);config.MapCells[3]=new MapCell(3,new Enemy("target","検証",500,ElementType.None,new Vector2Int(3,0)));
            var deck=new Deck(new List<Card>{root,root.Clone(),root.Clone(),root.Clone(),root.Clone()});
            var defaults=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(config,deck);
            string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-enemy-poison-"+System.Guid.NewGuid().ToString("N"));
            try {
                var file=new Olca.Unity.FileCheckpointStore(defaults.Definitions,directory);var session=new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defaults.Definitions,file);
                var game=new Olca.Core.Logic.Integration.DungeonBattleManager(config,new PlayerCharacter(500),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
                for(int i=0;game.CurrentBattle==null&&i<100;i++)game.TickFrame(17);Assert.IsNotNull(game.CurrentBattle);var old=game.CurrentBattle;old.Enemies[0].ApplyStun(60000);old.CustomScreen.Close();
                old.CardUsage.EnqueueCards(new List<Card>{root});Assert.IsTrue(old.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());old.TickFrame(17);Assert.IsNotNull(old.Enemies[0].GetPoison("venom"));
                var saved=System.IO.File.ReadAllBytes(file.ActivePath);Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);
                Assert.IsTrue(game.SuspendForCheckpoint());restored.ActivateCheckpoint();int remaining=old.Enemies[0].GetPoison("venom").RemainingFrames;game.TickFrame(17);Assert.AreEqual(remaining,old.Enemies[0].GetPoison("venom").RemainingFrames);
                Assert.IsFalse(restored.CurrentBattle.Enemies[0].IsPoisoned);Assert.AreEqual(500,restored.CurrentBattle.Enemies[0].CurrentHp);CollectionAssert.AreEqual(saved,System.IO.File.ReadAllBytes(file.ActivePath));
                var changed=MasterCard("venom",4);var definitions=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(defaults.Definitions.CreateInitialMap(),new Deck(new List<Card>{changed})).Definitions;
                Assert.IsNull(new Olca.Unity.FileCheckpointStore(definitions,directory).Read());CollectionAssert.AreEqual(saved,System.IO.File.ReadAllBytes(file.ActivePath));
            }finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }
        [Test]
        public void 旧毎秒入口の最大期間と量でも中間乗算を桁溢れさせない()
        {
            Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison,int.MaxValue,int.MaxValue));
            Assert.AreEqual(int.MaxValue,enemy.TickStatus(int.MaxValue));Assert.IsFalse(enemy.IsPoisoned);
            Assert.Throws<System.ArgumentException>(()=>new StatusEffect(StatusEffectType.Poison,1000,-1));
        }
        [Test]
        public void 複数毒の論理トレースは描画頻度と追いつきで一致する()
        {
            List<string> Trace(int fps){Setup();enemy.ApplyStatus(Poison("poison",2,3));enemy.ApplyStatus(Poison("venom",3,4));var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();var result=new List<string>();long wall=0;
                void Tick(long frame,int ms){if(frame==8)battle.Pause(BattlePauseReason.User);if(frame==14)battle.Resume(BattlePauseReason.User);battle.RunLogicalFrame(()=>{if(frame==20)enemy.ApplyStatus(Poison("poison",2,3));battle.TickFrame(ms);if(frame==30)enemy.ClearStatus(StatusEffectType.Poison);});result.Add(enemy.CurrentHp+":"+enemy.FramesUntilPoisonDamage("poison")+":"+enemy.FramesUntilPoisonDamage("venom"));}
                if(fps==0){clock.Accumulate(Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond);clock.Drain(Tick);}else for(int i=1;clock.Frame<60;i++){long next=(long)i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(next-wall);wall=next;clock.Drain(Tick);}return result;}
            CollectionAssert.AreEqual(Trace(60),Trace(30));CollectionAssert.AreEqual(Trace(60),Trace(120));CollectionAssert.AreEqual(Trace(60),Trace(0));
        }
    }
}
