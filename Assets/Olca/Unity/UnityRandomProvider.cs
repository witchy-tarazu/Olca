using Olca.Core.Interfaces;

namespace Olca.Unity
{
    /// <summary>
    /// UnityEngine.Randomを使用した乱数プロバイダー
    /// </summary>
    public class UnityRandomProvider : IRandomProvider
    {
        public int Range(int min, int max)
        {
            return UnityEngine.Random.Range(min, max);
        }

        public float Range(float min, float max)
        {
            return UnityEngine.Random.Range(min, max);
        }

        public float Value => UnityEngine.Random.value;
    }
}
