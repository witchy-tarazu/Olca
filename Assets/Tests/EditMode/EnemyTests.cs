using NUnit.Framework;
using Olca.Core.Domain;

namespace Olca.Tests
{
    /// <summary>
    /// Enemyのテスト
    /// </summary>
    public class EnemyTests
    {
        [Test]
        public void TakeDamage_通常ダメージ()
        {
            // Arrange
            var enemy = new Enemy("slime_01", "スライム", maxHp: 50, ElementType.None, new Vector2Int(3, 0));

            // Act
            enemy.TakeDamage(20, ElementType.None);

            // Assert
            Assert.AreEqual(30, enemy.CurrentHp);
        }

        [Test]
        public void TakeDamage_属性相性で2倍ダメージ()
        {
            // Arrange
            var enemy = new Enemy("fire_enemy", "火の敵", maxHp: 100, ElementType.Fire, new Vector2Int(4, 0));

            // Act
            enemy.TakeDamage(30, ElementType.Water); // 水→火は2倍

            // Assert
            Assert.AreEqual(40, enemy.CurrentHp); // 30 * 2 = 60ダメージ
        }

        [Test]
        public void TakeDamage_属性相性なしは1倍()
        {
            // Arrange
            var enemy = new Enemy("fire_enemy", "火の敵", maxHp: 100, ElementType.Fire, new Vector2Int(4, 0));

            // Act
            enemy.TakeDamage(30, ElementType.Earth); // 土→火は相性なし

            // Assert
            Assert.AreEqual(70, enemy.CurrentHp); // 30 * 1 = 30ダメージ
        }

        [Test]
        public void IsDead_HPが0以下でtrue()
        {
            // Arrange
            var enemy = new Enemy("slime_01", "スライム", maxHp: 50, ElementType.None, new Vector2Int(3, 0));

            // Act
            enemy.TakeDamage(60, ElementType.None);

            // Assert
            Assert.IsTrue(enemy.IsDead);
            Assert.AreEqual(0, enemy.CurrentHp);
        }

        [Test]
        public void MoveTo_位置が変更される()
        {
            // Arrange
            var enemy = new Enemy("slime_01", "スライム", maxHp: 50, ElementType.None, new Vector2Int(3, 0));

            // Act
            enemy.MoveTo(new Vector2Int(2, 0));

            // Assert
            Assert.AreEqual(new Vector2Int(2, 0), enemy.GridPosition);
        }
    }
}
