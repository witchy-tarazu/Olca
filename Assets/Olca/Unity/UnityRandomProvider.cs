using Olca.Core.Interfaces;

namespace Olca.Unity
{
    /// <summary>
    /// 描画用乱数から独立したゲーム用乱数プロバイダー
    /// </summary>
    public class UnityRandomProvider : IRandomProvider
    {
        private readonly System.Random random;
        public UnityRandomProvider() : this(System.Environment.TickCount) { }
        public UnityRandomProvider(int seed) { random = new System.Random(seed); }
        public int Range(int min, int max)
        {
            return random.Next(min, max);
        }

        public float Range(float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        public float Value => (float)random.NextDouble();
    }
}
