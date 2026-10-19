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
        public void 同じ毒は積算を引き継ぎ別状態への上書きは古い積算を捨てる(StatusEffectType replacement)
        {
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 2000, 3));
            Assert.AreEqual(0, enemy.TickStatus(333));
            enemy.ApplyStatus(new StatusEffect(replacement, 1000, 3));
            Assert.AreEqual(replacement == StatusEffectType.Poison ? 1 : 0, enemy.TickStatus(1));
            Assert.AreEqual(replacement == StatusEffectType.Poison ? 2 : 0, enemy.TickStatus(999));
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
    }
}
