using Olca.Core.Interfaces;

namespace Olca.Unity
{
    /// <summary>
    /// UnityEngine.Timeを使用した時間プロバイダー
    /// </summary>
    public class UnityTimeProvider : ITimeProvider
    {
        public float DeltaTime => UnityEngine.Time.deltaTime;

        public float Time => UnityEngine.Time.time;

        public float UnscaledDeltaTime => UnityEngine.Time.unscaledDeltaTime;
    }
}
