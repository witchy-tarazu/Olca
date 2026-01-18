using Olca.Core.Interfaces;

namespace Olca.Tests.Mocks
{
    /// <summary>
    /// テスト用の時間プロバイダー
    /// 整数ベース（ミリ秒）で時間を管理
    /// </summary>
    public class MockTimeProvider : ITimeProvider
    {
        private int deltaTimeMs;
        private long timeMs;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="deltaTimeMs">デルタタイム（ミリ秒）。デフォルト16ms（約60fps）</param>
        public MockTimeProvider(int deltaTimeMs = 16)
        {
            this.deltaTimeMs = deltaTimeMs;
            this.timeMs = 0;
        }

        /// <summary>
        /// 前フレームからの経過時間（ミリ秒）
        /// </summary>
        public int DeltaTimeMs => deltaTimeMs;

        /// <summary>
        /// ゲーム開始からの経過時間（ミリ秒）
        /// </summary>
        public long TimeMs => timeMs;

        /// <summary>
        /// スケールされていない前フレームからの経過時間（ミリ秒）
        /// </summary>
        public int UnscaledDeltaTimeMs => deltaTimeMs;

        /// <summary>
        /// 前フレームからの経過時間（秒）- View層での互換性用
        /// </summary>
        public float DeltaTime => deltaTimeMs / 1000f;

        /// <summary>
        /// ゲーム開始からの経過時間（秒）- View層での互換性用
        /// </summary>
        public float Time => timeMs / 1000f;

        /// <summary>
        /// スケールされていない前フレームからの経過時間（秒）- View層での互換性用
        /// </summary>
        public float UnscaledDeltaTime => deltaTimeMs / 1000f;

        /// <summary>
        /// デルタタイムを設定（ミリ秒）
        /// </summary>
        public void SetDeltaTimeMs(int value)
        {
            deltaTimeMs = value;
        }

        /// <summary>
        /// デルタタイムを設定（秒）- 後方互換用
        /// </summary>
        public void SetDeltaTime(float value)
        {
            deltaTimeMs = (int)(value * 1000f);
        }

        /// <summary>
        /// 時間を進める（ミリ秒）
        /// </summary>
        public void AdvanceMs(int milliseconds)
        {
            timeMs += milliseconds;
        }

        /// <summary>
        /// 時間を進める（秒）- 後方互換用
        /// </summary>
        public void Advance(float seconds)
        {
            timeMs += (long)(seconds * 1000f);
        }

        /// <summary>
        /// 時間をリセット
        /// </summary>
        public void Reset()
        {
            timeMs = 0;
        }
    }
}
