using Olca.Core.Interfaces;
using System.Collections.Generic;

namespace Olca.Tests.Mocks
{
    /// <summary>
    /// テスト用のロガー
    /// ログを記録して後から検証できる
    /// </summary>
    public class MockLogger : IGameLogger
    {
        public List<string> InfoLogs { get; } = new List<string>();
        public List<string> WarningLogs { get; } = new List<string>();
        public List<string> ErrorLogs { get; } = new List<string>();

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

        /// <summary>
        /// すべてのログをクリア
        /// </summary>
        public void Clear()
        {
            InfoLogs.Clear();
            WarningLogs.Clear();
            ErrorLogs.Clear();
        }
    }
}
