using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    /// <summary>
    /// Enemy スタン機能 と EnemyAIBehavior のスタン中スキップのテスト
    /// </summary>
    [TestFixture]
    public class EnemyStunTests
    {
        private Enemy enemy;

        [SetUp]
        public void SetUp()
        {
            enemy = new Enemy("slime", "スライム", 100, ElementType.None, new Vector2Int(3, 0));
        }

        [Test]
        public void 明示強靭度は独立削りを蓄積し最大値低下で残り1を保ち再成立で期間を戻す()
        {
            var target = new Enemy("poise", "検証", 100, ElementType.None, Vector2Int.Zero,
                poiseDefinition: new EnemyPoiseDefinition(100, 6));
            int notices = 0; target.OnStaggered += () => notices++;
            target.TakeDamage(1, ElementType.None, poiseDamage: 30);
            target.SetPoise(150); Assert.AreEqual(30, target.AccumulatedPoiseDamage);
            target.SetPoise(20); Assert.AreEqual(19, target.AccumulatedPoiseDamage); Assert.AreEqual(0, notices);
            target.TakeDamage(1, ElementType.None, poiseDamage: 0); Assert.AreEqual(19, target.AccumulatedPoiseDamage);
            target.TakeDamage(1, ElementType.None, poiseDamage: 1); Assert.AreEqual(1, notices);
            Assert.AreEqual(0, target.AccumulatedPoiseDamage); Assert.AreEqual(6, target.StaggerRemainingFrames);
            for (int i = 0; i < 4; i++) target.TickStunFrame(); Assert.AreEqual(2, target.StaggerRemainingFrames);
            target.TakeDamage(1, ElementType.None, poiseDamage: 20); Assert.AreEqual(6, target.StaggerRemainingFrames);
            target.SetPoise(50); Assert.AreEqual(6, target.StaggerRemainingFrames); Assert.AreEqual(2, notices);
            for (int i = 0; i < 6; i++) target.TickStunFrame(); Assert.IsFalse(target.IsStunned);
        }

        [TestCase(50, 95, 7)][TestCase(-100, 80, 7)][TestCase(100, 100, 0)]
        public void 敵の完全耐性は削りを遮断し軽減増幅は独立値を変えない(int resistance, int hp, int impact)
        {
            var target = new Enemy("poise", "検証", 100, ElementType.None, Vector2Int.Zero,
                resistances: new ElementResistances(fire: resistance), poiseDefinition: new EnemyPoiseDefinition(30, 6));
            target.TakeDamage(10, ElementType.Fire, poiseDamage: 7);
            Assert.AreEqual(hp, target.CurrentHp); Assert.AreEqual(impact, target.AccumulatedPoiseDamage);
        }

        [Test]
        public void 強靭度未設定は旧動作を維持し不正定義と負の削りを副作用前に拒否する()
        {
            enemy.TakeDamage(1, ElementType.None, poiseDamage: int.MaxValue);
            Assert.IsNull(enemy.Poise); Assert.IsFalse(enemy.IsStaggered); Assert.AreEqual(0, enemy.AccumulatedPoiseDamage);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnemyPoiseDefinition(0, 6));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new EnemyPoiseDefinition(10, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => enemy.TakeDamage(1, ElementType.None, poiseDamage: -1));
            Assert.AreEqual(99, enemy.CurrentHp);
        }

        [TestCase(false)][TestCase(true)]
        public void 敵AIは明示のけぞり期間を全て停止し終了翌更新に行動する(bool logical)
        {
            var target = new Enemy("poise", "検証", 100, ElementType.None, new Vector2Int(1, 0),
                poiseDefinition: new EnemyPoiseDefinition(10, 6));
            var player = new PlayerCharacter(100); var field = new BattleField();
            var ai = new EnemyAIBehavior(target, new List<IEnemyAIAction> { new BasicAttackAction(1, 0) });
            var context = new AIContext(target, player, field, new MockRandomProvider(), new MockLogger());
            target.TakeDamage(1, ElementType.None, poiseDamage: 10); Assert.IsFalse(ai.CanAct);
            if (logical) for (int i = 0; i < 6; i++) ai.Update(17, context, true);
            else ai.Update(100, context);
            Assert.AreEqual(100, player.CurrentHp); Assert.IsFalse(target.IsStaggered);
            ai.Update(17, context, logical); Assert.AreEqual(99, player.CurrentHp);
        }

        [Test]
        public void 敵の毒ダメージは強靭度を削らず通常フレームで進む()
        {
            var target = new Enemy("poise", "検証", 100, ElementType.None, new Vector2Int(5, 0),
                poiseDefinition: new EnemyPoiseDefinition(1, 6));
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { target }, new Deck(), randomProvider: new MockRandomProvider());
            target.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 60));
            for (int i = 0; i < 10; i++) battle.TickFrame(17);
            Assert.Less(target.CurrentHp, 100); Assert.AreEqual(0, target.AccumulatedPoiseDamage); Assert.IsFalse(target.IsStaggered);
        }

        [TestCase(false)][TestCase(true)]
        public void ガードは前後の基本攻撃と独立削りを防ぎブレイク中断だけ発生元設定に従う(bool interruptible)
        {
            var target = new Enemy("guard", "検証", 100, ElementType.None, new Vector2Int(3, 0),
                poiseDefinition: new EnemyPoiseDefinition(30, 6));
            var player = new PlayerCharacter(100);
            var battle = new BattleSystem(player, new List<Enemy> { target }, new Deck());
            target.Guard.Set(true, interruptible); int hits = 0; target.OnDamaged += (_, __) => hits++;
            foreach (int x in new[] { 2, 4 }) {
                player.MoveTo(new Vector2Int(x, 0)); battle.PlayerBasicAttack(target);
            }
            target.TakeDamage(1, ElementType.None, AttackProperty.Sword, int.MaxValue);
            Assert.AreEqual(100, target.CurrentHp); Assert.AreEqual(0, hits); Assert.AreEqual(0, target.AccumulatedPoiseDamage);
            battle.RunLogicalFrame(() => {
                target.TakeDamage(1, ElementType.None, AttackProperty.Break, 15);
                target.TakeDamage(1, ElementType.None, AttackProperty.Sword | AttackProperty.Break, 15);
                Assert.IsTrue(target.Guard.IsActive, "同フレームの中断判定前はガードを保つ");
            });
            Assert.AreEqual(98, target.CurrentHp); Assert.AreEqual(2, hits); Assert.IsTrue(target.IsStaggered);
            Assert.AreEqual(!interruptible, target.Guard.IsActive);
            player.TakeDamage(1000); Assert.IsFalse(target.Guard.IsActive, "戦闘終了で一時状態を解除する");
        }

        [Test]
        public void 中断行動のない敵はブレイクでガードを失わず毒と状態専用を受ける()
        {
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { enemy }, new Deck());
            enemy.Guard.Set(true, false); int hits = 0; enemy.OnDamaged += (_, __) => hits++;
            enemy.TakeDamage(1, ElementType.None, AttackProperty.Break, int.MaxValue);
            Assert.IsTrue(enemy.Guard.IsActive); Assert.AreEqual(99, enemy.CurrentHp);
            enemy.TakeDamage(0, ElementType.Fire); Assert.AreEqual(2, hits);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 60));
            for (int i = 0; i < 10; i++) battle.TickFrame(17);
            Assert.Less(enemy.CurrentHp, 99); Assert.IsTrue(enemy.Guard.IsActive);
        }

        [Test]
        public void 属性完全無効はブレイクでもガードと削りを変えず一時ガードを標準保存定義に混ぜない()
        {
            var target = new Enemy("guard", "検証", 100, ElementType.None, Vector2Int.Zero,
                resistances: new ElementResistances(fire: 100), poiseDefinition: new EnemyPoiseDefinition(30, 6));
            target.Guard.Set(true, true);
            target.TakeDamage(1, ElementType.Fire, AttackProperty.Break, int.MaxValue);
            Assert.AreEqual(100, target.CurrentHp); Assert.AreEqual(0, target.AccumulatedPoiseDamage); Assert.IsTrue(target.Guard.IsActive);
            var map = DungeonConfig.CreateTestDungeon(6, 30, 100);
            map.MapCells[5].Enemy.Guard.Set(true, false);
            Assert.Throws<System.ArgumentException>(() => Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, new Deck()));
        }

        [TestCase(false)][TestCase(true)]
        public void 死亡確定時はHP通知より前にガードとのけぞりを解除する(bool logical)
        {
            var target = new Enemy("guard", "検証", 100, ElementType.None, new Vector2Int(3, 0),
                poiseDefinition: new EnemyPoiseDefinition(30, 6));
            var survivor = new Enemy("survivor", "検証", 100, ElementType.None, new Vector2Int(5, 0));
            var battle = new BattleSystem(new PlayerCharacter(100), new List<Enemy> { target, survivor }, new Deck());
            target.Guard.Set(true, false);
            target.TakeDamage(1, ElementType.None, AttackProperty.Break, 30);
            Assert.IsTrue(target.Guard.IsActive); Assert.IsTrue(target.IsStaggered);
            int hpNotices = 0, deathNotices = 0;
            target.OnHpChanged += (_, __) => {
                hpNotices++;
                Assert.IsTrue(target.IsDead);
                Assert.IsFalse(target.Guard.IsActive);
                Assert.IsFalse(target.Guard.InterruptOnStagger);
                Assert.AreEqual(0, target.StaggerRemainingFrames);
            };
            target.OnDeath += () => {
                deathNotices++;
                Assert.IsFalse(target.Guard.IsActive); Assert.IsFalse(target.IsStaggered);
            };
            if (logical) battle.RunLogicalFrame(() => target.TakeDamage(200, ElementType.None, AttackProperty.Break, 0));
            else target.TakeDamage(200, ElementType.None, AttackProperty.Break, 0);
            Assert.IsFalse(battle.IsGameOver);
            Assert.AreEqual(1, hpNotices); Assert.AreEqual(1, deathNotices);
        }

        // ========== Enemy.ApplyStun / IsStunned / StunDurationMs ==========

        [Test]
        public void ApplyStun_スタン時間が設定される()
        {
            enemy.ApplyStun(3000);

            Assert.IsTrue(enemy.IsStunned, "スタン中はIsStunnedがtrue");
            Assert.AreEqual(3000, enemy.StunDurationMs);
        }

        [Test]
        public void ApplyStun_より長い時間を指定すると上書きされる()
        {
            enemy.ApplyStun(1000);
            enemy.ApplyStun(5000);

            Assert.AreEqual(5000, enemy.StunDurationMs, "長い方の時間に上書きされる");
        }

        [Test]
        public void ApplyStun_より短い時間を指定しても上書きされない()
        {
            enemy.ApplyStun(5000);
            enemy.ApplyStun(1000);

            Assert.AreEqual(5000, enemy.StunDurationMs, "長い方の時間を維持");
        }

        [Test]
        public void ApplyStun_0以下は無効()
        {
            enemy.ApplyStun(0);

            Assert.IsFalse(enemy.IsStunned, "0ms以下のスタンは付与されない");
        }

        [Test]
        public void IsStunned_初期状態はfalse()
        {
            Assert.IsFalse(enemy.IsStunned);
        }

        // ========== Enemy.TickStun ==========

        [Test]
        public void TickStun_時間経過でスタンが解除される()
        {
            enemy.ApplyStun(1000);

            enemy.TickStun(1000);

            Assert.IsFalse(enemy.IsStunned, "1000ms経過でスタン解除");
        }

        [Test]
        public void TickStun_残り時間より多く経過してもゼロ止まり()
        {
            enemy.ApplyStun(500);

            enemy.TickStun(2000);

            Assert.AreEqual(0, enemy.StunDurationMs, "負の値にならない");
            Assert.IsFalse(enemy.IsStunned);
        }

        [Test]
        public void TickStun_残り時間が残っていればスタン継続()
        {
            enemy.ApplyStun(3000);

            enemy.TickStun(1000);

            Assert.IsTrue(enemy.IsStunned, "まだスタン中");
            Assert.AreEqual(2000, enemy.StunDurationMs);
        }

        // ========== EnemyAIBehavior スタン中スキップ ==========

        [Test]
        public void EnemyAIBehavior_スタン中は行動をスキップする()
        {
            // Arrange
            var logger = new MockLogger();
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var field = new BattleField();
            var random = new MockRandomProvider();
            int attackCount = 0;
            var action = new TestCountAction(() => attackCount++);
            var behavior = new EnemyAIBehavior(enemy, new List<IEnemyAIAction> { action });
            var context = new AIContext(enemy, player, field, random, logger);

            // スタンを付与
            enemy.ApplyStun(3000);

            // Act: スタン中にTick
            behavior.Update(1000, context);

            // Assert
            Assert.AreEqual(0, attackCount, "スタン中は行動しない");
        }

        [Test]
        public void EnemyAIBehavior_スタン中はTickStunが呼ばれる()
        {
            // Arrange
            var logger = new MockLogger();
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var field = new BattleField();
            var random = new MockRandomProvider();
            var action = new TestCountAction(() => { });
            var behavior = new EnemyAIBehavior(enemy, new List<IEnemyAIAction> { action });
            var context = new AIContext(enemy, player, field, random, logger);

            enemy.ApplyStun(3000);

            // Act
            behavior.Update(1000, context);

            // Assert: 3000 - 1000 = 2000ms 残っているはず
            Assert.AreEqual(2000, enemy.StunDurationMs, "スタン時間がカウントダウンされている");
        }

        [Test]
        public void EnemyAIBehavior_スタン解除後に行動できる()
        {
            // Arrange
            var logger = new MockLogger();
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var field = new BattleField();
            var random = new MockRandomProvider();
            int attackCount = 0;
            var action = new TestCountAction(() => attackCount++);
            var behavior = new EnemyAIBehavior(enemy, new List<IEnemyAIAction> { action });
            var context = new AIContext(enemy, player, field, random, logger);

            // 1秒スタン → 1秒経過で解除
            enemy.ApplyStun(1000);
            behavior.Update(1000, context);  // スタン消費
            Assert.IsFalse(enemy.IsStunned, "前提: スタン解除済み");

            // Act: スタン解除後にTick
            behavior.Update(100, context);

            // Assert
            Assert.AreEqual(1, attackCount, "スタン解除後は行動できる");
        }

        /// <summary>
        /// テスト用：呼び出し回数を記録するダミーAIアクション
        /// </summary>
        private class TestCountAction : IEnemyAIAction
        {
            private readonly System.Action onExecute;

            public TestCountAction(System.Action onExecute)
            {
                this.onExecute = onExecute;
            }

            public bool CanExecute(AIContext context) => true;
            public void Execute(AIContext context) => onExecute();
            public int GetCooldownMs() => 0;
            public float GetCooldown() => 0f;
        }
    }
}
