using NUnit.Framework;
using Olca.Tests.Mocks;

namespace Olca.Tests
{
    /// <summary>
    /// モッククラスの動作確認テスト
    /// </summary>
    public class MockProviderTests
    {
        [Test]
        public void MockRandomProvider_固定値を返す()
        {
            // Arrange
            var random = new MockRandomProvider(fixedIntValue: 5);

            // Act
            int result1 = random.Range(0, 10);
            int result2 = random.Range(0, 10);

            // Assert
            Assert.AreEqual(5, result1);
            Assert.AreEqual(5, result2, "常に同じ値を返す");
        }

        [Test]
        public void MockTimeProvider_時間を進められる()
        {
            // Arrange
            var time = new MockTimeProvider();

            // Act
            time.Advance(1.5f);

            // Assert
            Assert.AreEqual(1.5f, time.Time);
        }

        [Test]
        public void MockInputProvider_入力をシミュレートできる()
        {
            // Arrange
            var input = new MockInputProvider();

            // Act
            input.PressJumpButton();

            // Assert
            Assert.IsTrue(input.JumpButtonDown);
        }

        [Test]
        public void MockLogger_ログを記録できる()
        {
            // Arrange
            var logger = new MockLogger();

            // Act
            logger.LogInfo("テストメッセージ");
            logger.LogError("エラー");

            // Assert
            Assert.AreEqual(1, logger.InfoLogs.Count);
            Assert.AreEqual(1, logger.ErrorLogs.Count);
            Assert.AreEqual("テストメッセージ", logger.InfoLogs[0]);
        }
    }
}
