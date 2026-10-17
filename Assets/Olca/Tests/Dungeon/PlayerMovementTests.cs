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

            // Act - 整数ベース版: 1000ms = 1秒
            movement.Update(deltaTimeMs: 1000);

            // Assert
            // 基本速度は10.0マス/秒なので、1秒で10.0マス進む
            Assert.AreEqual(10.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void StartDash_ダッシュ速度で前進する()
        {
            // Arrange
            var movement = new PlayerMovement(initialPosition: 0f);

            // Act
            movement.StartDash();
            movement.Update(deltaTimeMs: 1000);  // 1000ms = 1秒

            // Assert
            // ダッシュ速度は20.0マス/秒なので、1秒で20.0マス進む
            Assert.AreEqual(20.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void StopDash_通常速度に戻る()
        {
            // Arrange
            var movement = new PlayerMovement(initialPosition: 0f);
            movement.StartDash();
            movement.Update(deltaTimeMs: 1000);  // ダッシュで20マス進む

            float positionAfterDash = movement.CurrentPosition;  // 20.0

            // Act
            movement.StopDash();
            movement.Update(deltaTimeMs: 1000);  // 通常速度で10マス進む

            // Assert
            Assert.AreEqual(positionAfterDash + 10.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void Jump_高さが上昇する()
        {
            // Arrange
            var movement = new PlayerMovement();

            // Act
            movement.Jump();
            movement.Update(deltaTimeMs: 100);  // 100ms = 0.1秒、ジャンプ開始直後

            // Assert
            Assert.Greater(movement.Height, 0f, "ジャンプして高さが上がっている");
        }

        [Test]
        public void Jump_最大高度は2マス分まで上がる()
        {
            // Arrange
            var movement = new PlayerMovement();

            // Act
            movement.Jump();
            movement.Update(deltaTimeMs: 250);  // ジャンプ時間の中間が最大高度

            // Assert
            Assert.AreEqual(2.0f, movement.Height, 0.01f, "ジャンプ最大高度が2マス分になっている");
        }

        [Test]
        public void Jump_放物線を描いて着地する()
        {
            // Arrange
            var movement = new PlayerMovement();
            movement.Jump();

            // Act: ジャンプ時間（0.5秒 = 500ms）経過させる
            for (int i = 0; i < 6; i++)
            {
                movement.Update(deltaTimeMs: 100);  // 100ms × 6 = 600ms
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
            movement.Update(deltaTimeMs: 500);  // 500ms = 0.5秒、1マス進む
            float positionBeforePause = movement.CurrentPosition;

            // Act
            movement.Pause();
            movement.Update(deltaTimeMs: 1000);  // 1000ms = 1秒

            // Assert
            Assert.AreEqual(positionBeforePause, movement.CurrentPosition, "一時停止中は移動しない");
        }

        [Test]
        public void Resume_移動が再開される()
        {
            // Arrange
            var movement = new PlayerMovement();
            movement.Pause();
            movement.Update(deltaTimeMs: 1000);  // 停止中なので移動しない

            float positionDuringPause = movement.CurrentPosition;  // 0.0

            // Act
            movement.Resume();
            movement.Update(deltaTimeMs: 1000);

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
