using Olca.Core.Interfaces;

namespace Olca.Tests.Mocks
{
    /// <summary>
    /// テスト用の固定値を返す乱数プロバイダー
    /// </summary>
    public class MockRandomProvider : IRandomProvider
    {
        private int fixedIntValue;
        private float fixedFloatValue;

        public MockRandomProvider(int fixedIntValue = 0, float fixedFloatValue = 0.5f)
        {
            this.fixedIntValue = fixedIntValue;
            this.fixedFloatValue = fixedFloatValue;
        }

        /// <summary>
        /// 固定値を設定
        /// </summary>
        public void SetFixedValue(int value)
        {
            fixedIntValue = value;
        }

        /// <summary>
        /// 固定値を設定
        /// </summary>
        public void SetFixedValue(float value)
        {
            fixedFloatValue = value;
        }

        public int Range(int min, int max)
        {
            // テスト用: 常に固定値を返す（範囲内に収める）
            return System.Math.Max(min, System.Math.Min(fixedIntValue, max - 1));
        }

        public float Range(float min, float max)
        {
            // テスト用: 常に固定値を返す（範囲内に収める）
            return System.Math.Max(min, System.Math.Min(fixedFloatValue, max));
        }

        public float Value => fixedFloatValue;
    }
}
