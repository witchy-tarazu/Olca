namespace Olca.Core.Interfaces
{
    /// <summary>
    /// 乱数生成のインターフェース
    /// Unity非依存のロジックで乱数を使用するための抽象化
    /// </summary>
    public interface IRandomProvider
    {
        /// <summary>
        /// 指定範囲内のランダムな整数を取得
        /// </summary>
        /// <param name="min">最小値（含む）</param>
        /// <param name="max">最大値（含まない）</param>
        /// <returns>min以上max未満のランダムな整数</returns>
        int Range(int min, int max);

        /// <summary>
        /// 指定範囲内のランダムな浮動小数点数を取得
        /// </summary>
        /// <param name="min">最小値</param>
        /// <param name="max">最大値</param>
        /// <returns>min以上max以下のランダムな浮動小数点数</returns>
        float Range(float min, float max);

        /// <summary>
        /// 0.0以上1.0未満のランダムな浮動小数点数を取得
        /// </summary>
        float Value { get; }
    }
}
