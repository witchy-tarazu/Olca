using Olca.Core.Interfaces;

namespace Olca.Unity
{
    /// <summary>
    /// UnityEngine.Debugを使用したロガー
    /// </summary>
    public class UnityLogger : IGameLogger
    {
        public void LogInfo(string message)
        {
            UnityEngine.Debug.Log($"[INFO] {message}");
        }

        public void LogWarning(string message)
        {
            UnityEngine.Debug.LogWarning($"[WARNING] {message}");
        }

        public void LogError(string message)
        {
            UnityEngine.Debug.LogError($"[ERROR] {message}");
        }
    }
}
