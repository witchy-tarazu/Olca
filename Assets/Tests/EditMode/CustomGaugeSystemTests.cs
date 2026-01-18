using NUnit.Framework;
using Olca.Core.Logic.Battle;

namespace Olca.Tests
{
    /// <summary>
    /// CustomGaugeSystemのテスト
    /// 整数ベース（ミリ秒）での時間管理をテスト
    /// </summary>
    public class CustomGaugeSystemTests
    {
        [Test]
        public void Update_ゲージが溜まる()
        {
            // Arrange: 1秒あたり1.0ずつゲージが溜まる
            var gauge = new CustomGaugeSystem(gaugeSpeedPerSecond: 1.0f);

            // Act: 500ms経過
            gauge.Update(deltaTimeMs: 500);

            // Assert: 0.5秒分 = 0.5ゲージ
            Assert.AreEqual(0.5f, gauge.CurrentGauge, 0.01f);
        }

        [Test]
        public void Update_ゲージが満タンになる()
        {
            // Arrange: 1秒あたり1.0ずつゲージが溜まる
            var gauge = new CustomGaugeSystem(gaugeSpeedPerSecond: 1.0f);
            bool eventFired = false;
            gauge.OnGaugeFull += () => eventFired = true;

            // Act: 10000ms（10秒）経過（MAX_GAUGE = 10.0に到達）
            gauge.Update(deltaTimeMs: 10000);

            // Assert
            Assert.IsTrue(gauge.IsFull);
            Assert.AreEqual(CustomGaugeSystem.MAX_GAUGE, gauge.CurrentGauge, 0.01f);
            Assert.IsTrue(eventFired);
        }

        [Test]
        public void Reset_ゲージが0に戻る()
        {
            // Arrange
            var gauge = new CustomGaugeSystem(gaugeSpeedPerSecond: 1.0f);
            gauge.Update(deltaTimeMs: 800);  // 800ms経過

            // Act
            gauge.Reset();

            // Assert
            Assert.AreEqual(0f, gauge.CurrentGauge, 0.01f);
            Assert.IsFalse(gauge.IsFull);
        }

        [Test]
        public void Update_満タン後は増加しない()
        {
            // Arrange
            var gauge = new CustomGaugeSystem(gaugeSpeedPerSecond: 1.0f);
            gauge.Update(deltaTimeMs: 10000);  // 10000ms（10秒）で満タン

            // Act: さらに500ms更新
            gauge.Update(deltaTimeMs: 500);

            // Assert: 満タンのまま
            Assert.AreEqual(CustomGaugeSystem.MAX_GAUGE, gauge.CurrentGauge, 0.01f);
        }
    }
}
