namespace Olca.Core.Interfaces
{
    /// <summary>
    /// ログ出力のインターフェース
    /// Unity非依存のロジックでログを出力するための抽象化
    /// </summary>
    public interface IGameLogger
    {
        /// <summary>
        /// 情報ログを出力
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        void LogInfo(string message);

        /// <summary>
        /// 警告ログを出力
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        void LogWarning(string message);

        /// <summary>
        /// エラーログを出力
        /// </summary>
        /// <param name="message">ログメッセージ</param>
        void LogError(string message);
    }
}
