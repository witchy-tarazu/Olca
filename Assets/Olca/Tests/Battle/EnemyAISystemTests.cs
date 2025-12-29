using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class EnemyAISystemTests
    {
        private EnemyAISystem aiSystem;
        private PlayerCharacter player;
        private BattleField field;
        private MockRandomProvider random;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            logger = new MockLogger();
            aiSystem = new EnemyAISystem(logger);
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            field = new BattleField();
            random = new MockRandomProvider();
        }

        [Test]
        public void 敵を登録できる()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };

            // Act
            aiSystem.RegisterEnemy(enemy, actions);

            // Assert
            Assert.AreEqual(1, aiSystem.AIBehaviors.Count, "敵が1体登録されるべき");
        }

        [Test]
        public void 敵が登録解除できる()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };
            aiSystem.RegisterEnemy(enemy, actions);

            // Act
            aiSystem.UnregisterEnemy(enemy);

            // Assert
            Assert.AreEqual(0, aiSystem.AIBehaviors.Count, "敵が登録解除されるべき");
        }

        [Test]
        public void 隣接した敵がプレイヤーを攻撃する()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(0.1f, player, field, random);

            // Assert
            Assert.Less(player.CurrentHp, initialHp, "プレイヤーがダメージを受けるべき");
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "10ダメージを受けるべき");
        }

        [Test]
        public void 離れた敵は攻撃しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(0, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(5, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(0.1f, player, field, random);

            // Assert
            Assert.AreEqual(initialHp, player.CurrentHp, "離れているので攻撃されないべき");
        }

        [Test]
        public void クールダウン中は行動しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃
            aiSystem.Tick(0.1f, player, field, random);
            int hpAfterFirstAttack = player.CurrentHp;

            // Act: クールダウン中に再度更新
            aiSystem.Tick(0.1f, player, field, random);

            // Assert
            Assert.AreEqual(hpAfterFirstAttack, player.CurrentHp, "クールダウン中は攻撃しないべき");
        }

        [Test]
        public void クールダウン後に再度行動できる()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 0.5f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃
            aiSystem.Tick(0.1f, player, field, random);
            int hpAfterFirstAttack = player.CurrentHp;

            // Act: クールダウン時間経過
            aiSystem.Tick(0.6f, player, field, random);

            // Assert
            Assert.Less(player.CurrentHp, hpAfterFirstAttack, "クールダウン後に再攻撃するべき");
            Assert.AreEqual(hpAfterFirstAttack - 10, player.CurrentHp, "再度10ダメージを受けるべき");
        }

        [Test]
        public void 死亡した敵は行動しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 10, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 0.1f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 敵を倒す
            enemy.TakeDamage(10, ElementType.None);
            int hpBeforeUpdate = player.CurrentHp;

            // Act
            aiSystem.Tick(0.2f, player, field, random);

            // Assert
            Assert.AreEqual(hpBeforeUpdate, player.CurrentHp, "死んだ敵は攻撃しないべき");
        }

        [Test]
        public void 複数の敵が同時に行動できる()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy1 = new Enemy("slime1", "スライム1", 50, ElementType.None, new Vector2Int(3, 0));
            var enemy2 = new Enemy("slime2", "スライム2", 50, ElementType.None, new Vector2Int(1, 0));

            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 5, cooldown: 1.0f)
            };

            aiSystem.RegisterEnemy(enemy1, actions);
            aiSystem.RegisterEnemy(enemy2, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(0.1f, player, field, random);

            // Assert
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "2体の敵が同時に攻撃するべき（5+5=10ダメージ）");
        }

        [Test]
        public void 死亡したプレイヤーは攻撃されない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            player.TakeDamage(90); // HP 10 残す
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 20, cooldown: 0.1f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃でプレイヤーを倒す
            aiSystem.Tick(0.1f, player, field, random);
            Assert.IsTrue(player.IsDead, "プレイヤーが死亡するべき");

            // Act: 死んだプレイヤーに再度攻撃を試みる
            int hpAfterDeath = player.CurrentHp;
            aiSystem.Tick(0.2f, player, field, random);

            // Assert
            Assert.AreEqual(hpAfterDeath, player.CurrentHp, "死んだプレイヤーは攻撃されないべき");
        }

        [Test]
        public void 空のアクションリストで登録するとエラー()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var emptyActions = new List<IEnemyAIAction>();

            // Act & Assert
            Assert.Throws<System.ArgumentException>(() =>
            {
                aiSystem.RegisterEnemy(enemy, emptyActions);
            }, "空のアクションリストは許可されないべき");
        }

        [Test]
        public void nullの敵を登録するとエラー()
        {
            // Arrange
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };

            // Act & Assert
            Assert.Throws<System.ArgumentNullException>(() =>
            {
                aiSystem.RegisterEnemy(null, actions);
            }, "null敵は許可されないべき");
        }
    }
}
