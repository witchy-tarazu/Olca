using Olca.Core.Interfaces;

namespace Olca.Tests.Mocks
{
    /// <summary>
    /// テスト用の時間プロバイダー
    /// </summary>
    public class MockTimeProvider : ITimeProvider
    {
        private float deltaTime;
        private float time;

        public MockTimeProvider(float deltaTime = 0.016f)
        {
            this.deltaTime = deltaTime;
            this.time = 0f;
        }

        public float DeltaTime => deltaTime;

        public float Time => time;

        public float UnscaledDeltaTime => deltaTime;

        /// <summary>
        /// デルタタイムを設定
        /// </summary>
        public void SetDeltaTime(float value)
        {
            deltaTime = value;
        }

        /// <summary>
        /// 時間を進める（テスト用）
        /// </summary>
        public void Advance(float seconds)
        {
            time += seconds;
        }

        /// <summary>
        /// 時間をリセット
        /// </summary>
        public void Reset()
        {
            time = 0f;
        }
    }
}
