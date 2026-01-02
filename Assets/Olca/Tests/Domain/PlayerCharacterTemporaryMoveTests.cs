using NUnit.Framework;
using System;
using Olca.Core.Domain;

namespace Olca.Tests.Domain
{
    /// <summary>
    /// PlayerCharacterの一時移動機能のテスト
    /// </summary>
    public class PlayerCharacterTemporaryMoveTests
    {
        [Test]
        public void MoveTemporarily_GridPositionが一時位置を返す()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0));

            // Act
            player.MoveTemporarily(new Vector2Int(2, 0));

            // Assert
            Assert.AreEqual(new Vector2Int(2, 0), player.GridPosition);
            Assert.AreEqual(new Vector2Int(0, 0), player.GetLogicalPosition());
        }

        [Test]
        public void MoveTemporarily_IsInTemporaryMoveがtrueになる()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0));

            // Act
            player.MoveTemporarily(new Vector2Int(2, 0));

            // Assert
            Assert.IsTrue(player.IsInTemporaryMove);
        }

        [Test]
        public void ReturnToOriginalPosition_元の位置に戻る()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0));
            player.MoveTemporarily(new Vector2Int(2, 0));

            // Act
            player.ReturnToOriginalPosition();

            // Assert
            Assert.AreEqual(new Vector2Int(0, 0), player.GridPosition);
            Assert.IsFalse(player.IsInTemporaryMove);
        }

        [Test]
        public void MoveTemporarily_二重呼び出しで例外()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0));
            player.MoveTemporarily(new Vector2Int(2, 0));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
            {
                player.MoveTemporarily(new Vector2Int(3, 0));
            });
        }

        [Test]
        public void ReturnToOriginalPosition_一時移動中でない場合は例外()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0));

            // Act & Assert
            Assert.Throws<InvalidOperationException>(() =>
            {
                player.ReturnToOriginalPosition();
            });
        }

        [Test]
        public void MoveTemporarily_OnPositionChangedイベントが発火する()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0));
            Vector2Int? firedPosition = null;
            player.OnPositionChanged += pos => firedPosition = pos;

            // Act
            player.MoveTemporarily(new Vector2Int(2, 0));

            // Assert
            Assert.AreEqual(new Vector2Int(2, 0), firedPosition);
        }

        [Test]
        public void ReturnToOriginalPosition_OnPositionChangedイベントが発火する()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0));
            player.MoveTemporarily(new Vector2Int(2, 0));
            Vector2Int? firedPosition = null;
            player.OnPositionChanged += pos => firedPosition = pos;

            // Act
            player.ReturnToOriginalPosition();

            // Assert
            Assert.AreEqual(new Vector2Int(0, 0), firedPosition);
        }

        [Test]
        public void GetLogicalPosition_一時移動中でも元の位置を返す()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(1, 0));
            player.MoveTemporarily(new Vector2Int(3, 0));

            // Act
            var logicalPosition = player.GetLogicalPosition();

            // Assert
            Assert.AreEqual(new Vector2Int(1, 0), logicalPosition);
            Assert.AreEqual(new Vector2Int(3, 0), player.GridPosition);
        }

        [Test]
        public void MoveTemporarily_異なる位置に一時移動できる()
        {
            // Arrange
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(1, 0));

            // Act
            player.MoveTemporarily(new Vector2Int(5, 0));

            // Assert
            Assert.AreEqual(new Vector2Int(5, 0), player.GridPosition);
            Assert.AreEqual(new Vector2Int(1, 0), player.GetLogicalPosition());
            Assert.IsTrue(player.IsInTemporaryMove);
        }
    }
}
