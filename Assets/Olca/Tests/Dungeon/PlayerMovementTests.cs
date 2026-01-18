using NUnit.Framework;
using Olca.Core.Logic.Dungeon;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class PlayerMovementTests
    {
        [Test]
        public void Update_プレイヤーが基本速度で前進する()
        {
            // Arrange
            var movement = new PlayerMovement(initialPosition: 0f);

            // Act
            movement.Update(deltaTime: 1.0f);

            // Assert
            // 基本速度は2.0マス/秒なので、1秒で2.0マス進む
            Assert.AreEqual(2.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void StartDash_ダッシュ速度で前進する()
        {
            // Arrange
            var movement = new PlayerMovement(initialPosition: 0f);

            // Act
            movement.StartDash();
            movement.Update(deltaTime: 1.0f);

            // Assert
            // ダッシュ速度は5.0マス/秒なので、1秒で5.0マス進む
            Assert.AreEqual(5.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void StopDash_通常速度に戻る()
        {
            // Arrange
            var movement = new PlayerMovement(initialPosition: 0f);
            movement.StartDash();
            movement.Update(1.0f);  // ダッシュで5マス進む

            float positionAfterDash = movement.CurrentPosition;  // 5.0

            // Act
            movement.StopDash();
            movement.Update(1.0f);  // 通常速度で2マス進む

            // Assert
            Assert.AreEqual(positionAfterDash + 2.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void Jump_高さが上昇する()
        {
            // Arrange
            var movement = new PlayerMovement();

            // Act
            movement.Jump();
            movement.Update(0.1f);  // ジャンプ開始直後

            // Assert
            Assert.Greater(movement.Height, 0f, "ジャンプして高さが上がっている");
        }

        [Test]
        public void Jump_放物線を描いて着地する()
        {
            // Arrange
            var movement = new PlayerMovement();
            movement.Jump();

            // Act: ジャンプ時間（0.5秒）経過させる
            for (int i = 0; i < 6; i++)
            {
                movement.Update(0.1f);
            }

            // Assert
            Assert.AreEqual(0f, movement.Height, 0.01f, "ジャンプ後に着地している");
            Assert.IsFalse(movement.IsJumping, "ジャンプ状態が終了している");
        }

        [Test]
        public void Pause_移動が停止する()
        {
            // Arrange
            var movement = new PlayerMovement();
            movement.Update(0.5f);  // 1マス進む
            float positionBeforePause = movement.CurrentPosition;

            // Act
            movement.Pause();
            movement.Update(1.0f);

            // Assert
            Assert.AreEqual(positionBeforePause, movement.CurrentPosition, "一時停止中は移動しない");
        }

        [Test]
        public void Resume_移動が再開される()
        {
            // Arrange
            var movement = new PlayerMovement();
            movement.Pause();
            movement.Update(1.0f);  // 停止中なので移動しない

            float positionDuringPause = movement.CurrentPosition;  // 0.0

            // Act
            movement.Resume();
            movement.Update(1.0f);

            // Assert
            Assert.Greater(movement.CurrentPosition, positionDuringPause, "再開後に移動している");
        }

        [Test]
        public void SetPosition_位置を変更できる()
        {
            // Arrange
            var movement = new PlayerMovement(0f);

            // Act
            movement.SetPosition(10f);

            // Assert
            Assert.AreEqual(10f, movement.CurrentPosition);
        }
    }
}
