using NUnit.Framework;
using Olca.Core.Logic.Battle;

namespace Olca.Tests
{
    /// <summary>
    /// CustomGaugeSystemのテスト
    /// </summary>
    public class CustomGaugeSystemTests
    {
        [Test]
        public void Update_ゲージが溜まる()
        {
            // Arrange
            var gauge = new CustomGaugeSystem(gaugeSpeed: 1.0f);

            // Act
            gauge.Update(0.5f); // 0.5秒経過

            // Assert
            Assert.AreEqual(0.5f, gauge.CurrentGauge, 0.01f);
        }

        [Test]
        public void Update_ゲージが満タンになる()
        {
            // Arrange
            var gauge = new CustomGaugeSystem(gaugeSpeed: 1.0f);
            bool eventFired = false;
            gauge.OnGaugeFull += () => eventFired = true;

            // Act
            gauge.Update(1.0f); // 1秒経過

            // Assert
            Assert.IsTrue(gauge.IsFull);
            Assert.AreEqual(1.0f, gauge.CurrentGauge, 0.01f);
            Assert.IsTrue(eventFired);
        }

        [Test]
        public void Reset_ゲージが0に戻る()
        {
            // Arrange
            var gauge = new CustomGaugeSystem(gaugeSpeed: 1.0f);
            gauge.Update(0.8f);

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
            var gauge = new CustomGaugeSystem(gaugeSpeed: 1.0f);
            gauge.Update(1.0f); // 満タン

            // Act
            gauge.Update(0.5f); // さらに更新

            // Assert
            Assert.AreEqual(1.0f, gauge.CurrentGauge, 0.01f);
        }
    }
}
