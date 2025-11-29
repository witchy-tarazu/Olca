namespace Olca.Core.Interfaces
{
    /// <summary>
    /// 時間情報のインターフェース
    /// Unity非依存のロジックで時間を扱うための抽象化
    /// </summary>
    public interface ITimeProvider
    {
        /// <summary>
        /// 前フレームからの経過時間（秒）
        /// </summary>
        float DeltaTime { get; }

        /// <summary>
        /// ゲーム開始からの経過時間（秒）
        /// </summary>
        float Time { get; }

        /// <summary>
        /// スケールされていない前フレームからの経過時間（秒）
        /// </summary>
        float UnscaledDeltaTime { get; }
    }
}
