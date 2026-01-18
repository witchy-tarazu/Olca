using NUnit.Framework;
using Olca.Core.Interfaces;

namespace Olca.Tests
{
    /// <summary>
    /// モッククラスの動作確認テスト
    /// </summary>
    public class MockProviderTests
    {
        /// <summary>
        /// テスト用の固定値を返す乱数プロバイダー
        /// </summary>
        private class MockRandomProvider : IRandomProvider
        {
            private int fixedIntValue;
            private float fixedFloatValue;

            public MockRandomProvider(int fixedIntValue = 0, float fixedFloatValue = 0.5f)
            {
                this.fixedIntValue = fixedIntValue;
                this.fixedFloatValue = fixedFloatValue;
            }

            public void SetFixedValue(int value)
            {
                fixedIntValue = value;
            }

            public void SetFixedValue(float value)
            {
                fixedFloatValue = value;
            }

            public int Range(int min, int max)
            {
                return System.Math.Max(min, System.Math.Min(fixedIntValue, max - 1));
            }

            public float Range(float min, float max)
            {
                return System.Math.Max(min, System.Math.Min(fixedFloatValue, max));
            }

            public float Value => fixedFloatValue;
        }

        /// <summary>
        /// テスト用の時間プロバイダー
        /// 整数ベース（ミリ秒）で時間を管理
        /// </summary>
        private class MockTimeProvider : ITimeProvider
        {
            private int deltaTimeMs;
            private long timeMs;

            public MockTimeProvider(int deltaTimeMs = 16)  // 16ms ≈ 60fps
            {
                this.deltaTimeMs = deltaTimeMs;
                this.timeMs = 0;
            }

            // 整数ベースのプロパティ（推奨）
            public int DeltaTimeMs => deltaTimeMs;
            public long TimeMs => timeMs;
            public int UnscaledDeltaTimeMs => deltaTimeMs;

            // float版（後方互換・View層用）
            public float DeltaTime => deltaTimeMs / 1000f;
            public float Time => timeMs / 1000f;
            public float UnscaledDeltaTime => deltaTimeMs / 1000f;

            public void SetDeltaTime(int valueMs)
            {
                deltaTimeMs = valueMs;
            }

            public void SetDeltaTime(float value)
            {
                deltaTimeMs = (int)(value * 1000f);
            }

            public void Advance(int milliseconds)
            {
                timeMs += milliseconds;
            }

            public void Advance(float seconds)
            {
                timeMs += (long)(seconds * 1000f);
            }

            public void Reset()
            {
                timeMs = 0;
            }
        }

        /// <summary>
        /// テスト用の入力プロバイダー
        /// </summary>
        private class MockInputProvider : IInputProvider
        {
            public float Horizontal { get; set; }
            public float HorizontalDown { get; set; }
            public float Vertical { get; set; }
            public bool JumpButtonDown { get; set; }
            public bool DashButton { get; set; }
            public bool CardUseButtonDown { get; set; }
            public bool BasicAttackButton { get; set; }
            public float BasicAttackButtonHoldTime { get; set; }
            public bool CustomScreenButtonDown { get; set; }

            public void Reset()
            {
                Horizontal = 0f;
                HorizontalDown = 0f;
                Vertical = 0f;
                JumpButtonDown = false;
                DashButton = false;
                CardUseButtonDown = false;
                BasicAttackButton = false;
                BasicAttackButtonHoldTime = 0f;
                CustomScreenButtonDown = false;
            }

            public void PressJumpButton()
            {
                JumpButtonDown = true;
            }

            public void PressCustomScreenButton()
            {
                CustomScreenButtonDown = true;
            }
        }

        /// <summary>
        /// テスト用のロガー
        /// </summary>
        private class MockLogger : IGameLogger
        {
            public System.Collections.Generic.List<string> InfoLogs { get; } = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.List<string> WarningLogs { get; } = new System.Collections.Generic.List<string>();
            public System.Collections.Generic.List<string> ErrorLogs { get; } = new System.Collections.Generic.List<string>();

            public void LogInfo(string message)
            {
                InfoLogs.Add(message);
            }

            public void LogWarning(string message)
            {
                WarningLogs.Add(message);
            }

            public void LogError(string message)
            {
                ErrorLogs.Add(message);
            }

            public void Clear()
            {
                InfoLogs.Clear();
                WarningLogs.Clear();
                ErrorLogs.Clear();
            }
        }

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
