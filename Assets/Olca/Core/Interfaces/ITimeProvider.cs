namespace Olca.Core.Interfaces
{
    /// <summary>
    /// 時間情報のインターフェース
    /// Unity非依存のロジックで時間を扱うための抽象化
    /// 整数ベース（ミリ秒）で管理し、浮動小数点の累積誤差を回避
    /// </summary>
    public interface ITimeProvider
    {
        /// <summary>
        /// 前フレームからの経過時間（ミリ秒）
        /// </summary>
        int DeltaTimeMs { get; }

        /// <summary>
        /// ゲーム開始からの経過時間（ミリ秒）
        /// </summary>
        long TimeMs { get; }

        /// <summary>
        /// スケールされていない前フレームからの経過時間（ミリ秒）
        /// </summary>
        int UnscaledDeltaTimeMs { get; }

        /// <summary>
        /// 前フレームからの経過時間（秒）- View層での互換性用
        /// </summary>
        float DeltaTime { get; }

        /// <summary>
        /// ゲーム開始からの経過時間（秒）- View層での互換性用
        /// </summary>
        float Time { get; }

        /// <summary>
        /// スケールされていない前フレームからの経過時間（秒）- View層での互換性用
        /// </summary>
        float UnscaledDeltaTime { get; }
    }
}
