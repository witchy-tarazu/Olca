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
