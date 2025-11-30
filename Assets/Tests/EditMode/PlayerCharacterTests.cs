using NUnit.Framework;
using Olca.Core.Domain;

namespace Olca.Tests
{
    /// <summary>
    /// PlayerCharacterのテスト
    /// </summary>
    public class PlayerCharacterTests
    {
        [Test]
        public void TakeDamage_HPが減少する()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100);

            // Act
            player.TakeDamage(30);

            // Assert
            Assert.AreEqual(70, player.CurrentHp);
        }

        [Test]
        public void TakeDamage_HPが0以下になったら_IsDeadがtrueになる()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100);

            // Act
            player.TakeDamage(150);

            // Assert
            Assert.IsTrue(player.IsDead);
            Assert.AreEqual(0, player.CurrentHp);
        }

        [Test]
        public void Heal_HPが回復する()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100);
            player.TakeDamage(50);

            // Act
            player.Heal(30);

            // Assert
            Assert.AreEqual(80, player.CurrentHp);
        }

        [Test]
        public void Heal_最大HP以上には回復しない()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100);
            player.TakeDamage(20);

            // Act
            player.Heal(50);

            // Assert
            Assert.AreEqual(100, player.CurrentHp);
        }

        [Test]
        public void HpRatio_正しい割合を返す()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100);
            player.TakeDamage(25);

            // Act
            float ratio = player.HpRatio;

            // Assert
            Assert.AreEqual(0.75f, ratio, 0.01f);
        }

        [Test]
        public void OnDeath_HPが0になったら発火する()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100);
            bool eventFired = false;
            player.OnDeath += () => eventFired = true;

            // Act
            player.TakeDamage(100);

            // Assert
            Assert.IsTrue(eventFired);
        }
    }
}
