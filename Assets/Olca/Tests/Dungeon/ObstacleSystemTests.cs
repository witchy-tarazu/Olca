using NUnit.Framework;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Domain;
using Olca.Tests.Mocks;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class ObstacleSystemTests
    {
        private MockLogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockLogger = new MockLogger();
        }

        [Test]
        public void CheckObstacleContact_障害物セルでない場合_接触しない()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var emptyCell = new MapCell(0);  // 空白マス

            // Act
            var result = obstacleSystem.CheckObstacleContact(emptyCell, 0f, 0f);

            // Assert
            Assert.IsFalse(result.IsContact, "空セルでは接触しない");
        }

        [Test]
        public void CheckObstacleContact_障害物から離れている_接触しない()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("spike", "トゲ", 10);  // 即時ダメージ障害物
            var obstacleCell = new MapCell(5, obstacle, height: 0f);

            // Act: プレイヤーは位置0（障害物は位置5）
            var result = obstacleSystem.CheckObstacleContact(obstacleCell, 0f, 0f);

            // Assert
            Assert.IsFalse(result.IsContact, "遠すぎて接触しない");
        }

        [Test]
        public void CheckObstacleContact_ジャンプ中_回避できる()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("spike", "トゲ", 10);  // 即時ダメージ障害物
            var obstacleCell = new MapCell(5, obstacle, height: 0f);

            // Act: プレイヤーは位置5.5でジャンプ中（高さ1.0）
            var result = obstacleSystem.CheckObstacleContact(obstacleCell, 5.5f, 1.0f);

            // Assert
            Assert.IsTrue(result.IsContact, "接触範囲内");
            Assert.IsTrue(result.IsAvoided, "ジャンプで回避");
            Assert.AreEqual(obstacle, result.Obstacle);
        }

        [Test]
        public void CheckObstacleContact_ジャンプしていない_回避失敗()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("spike", "トゲ", 10);  // 即時ダメージ障害物
            var obstacleCell = new MapCell(5, obstacle, height: 0f);

            // Act: プレイヤーは位置5.5で地上（高さ0）
            var result = obstacleSystem.CheckObstacleContact(obstacleCell, 5.5f, 0f);

            // Assert
            Assert.IsTrue(result.IsContact, "接触範囲内");
            Assert.IsFalse(result.IsAvoided, "回避失敗");
            Assert.AreEqual(obstacle, result.Obstacle);
        }

        [Test]
        public void CreatePenaltyInfo_即時ダメージ_ダメージ情報を生成()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("spike", "トゲ", 10);  // 即時ダメージ障害物

            // Act
            var penalty = obstacleSystem.CreatePenaltyInfo(obstacle);

            // Assert
            Assert.AreEqual(ObstaclePenaltyType.ImmediateDamage, penalty.PenaltyType);
            Assert.AreEqual(10, penalty.Damage);
            Assert.AreEqual(0, penalty.CardDiscardCount);
        }

        [Test]
        public void CreatePenaltyInfo_状態異常_状態異常情報を生成()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("poison", "毒沼", "poison_effect");  // 状態異常障害物

            // Act
            var penalty = obstacleSystem.CreatePenaltyInfo(obstacle);

            // Assert
            Assert.AreEqual(ObstaclePenaltyType.StatusEffect, penalty.PenaltyType);
            Assert.AreEqual(0, penalty.Damage);
            Assert.AreEqual("poison_effect", penalty.StatusEffectId);
        }

        [Test]
        public void CreatePenaltyInfo_カード破棄_破棄枚数情報を生成()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = Obstacle.CreateCardDiscardObstacle("trap", "罠", 2);  // カード破棄障害物

            // Act
            var penalty = obstacleSystem.CreatePenaltyInfo(obstacle);

            // Assert
            Assert.AreEqual(ObstaclePenaltyType.CardDiscard, penalty.PenaltyType);
            Assert.AreEqual(2, penalty.CardDiscardCount);
            Assert.AreEqual(0, penalty.Damage);
        }
    }
}
