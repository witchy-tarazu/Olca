using System;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// カスタムゲージシステム
    /// リアルタイムに溜まり、満タンでカスタム画面を開ける
    /// </summary>
    public class CustomGaugeSystem
    {
        private float currentGauge; // 0.0 ~ MAX_GAUGE
        private readonly float gaugeSpeed;

        /// <summary>ゲージの最大値</summary>
        public const float MAX_GAUGE = 10.0f;

        /// <summary>現在のゲージ量（0.0～MAX_GAUGE）</summary>
        public float CurrentGauge => currentGauge;

        /// <summary>現在のゲージ量（正規化: 0.0～1.0）View表示用</summary>
        public float NormalizedGauge => currentGauge / MAX_GAUGE;

        /// <summary>ゲージが満タンか</summary>
        public bool IsFull => currentGauge >= MAX_GAUGE;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="gaugeSpeed">ゲージの溜まる速度（1秒あたりの増加量、デフォルト2.0）</param>
        public CustomGaugeSystem(float gaugeSpeed = 2.0f)
        {
            this.gaugeSpeed = gaugeSpeed;
            this.currentGauge = 0f;
        }

        /// <summary>
        /// ゲージを更新（毎フレーム呼び出す）
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過時間</param>
        public void Update(float deltaTime)
        {
            if (IsFull) return;

            currentGauge += gaugeSpeed * deltaTime;

            if (currentGauge >= MAX_GAUGE)
            {
                currentGauge = MAX_GAUGE;
                OnGaugeFull?.Invoke();
            }

            OnGaugeChanged?.Invoke(NormalizedGauge);
        }

        /// <summary>
        /// ゲージをリセット（カスタム画面を閉じた後に呼ぶ）
        /// </summary>
        public void Reset()
        {
            currentGauge = 0f;
            OnGaugeChanged?.Invoke(NormalizedGauge);
        }

        /// <summary>
        /// ゲージを任意の値に設定（デバッグ用）
        /// </summary>
        /// <param name="value">設定する値（0.0～MAX_GAUGE）</param>
        public void SetGauge(float value)
        {
            currentGauge = Math.Max(0f, Math.Min(value, MAX_GAUGE));
            OnGaugeChanged?.Invoke(NormalizedGauge);

            if (IsFull)
            {
                OnGaugeFull?.Invoke();
            }
        }

        /// <summary>
        /// ゲージ変更時のイベント
        /// </summary>
        public event Action<float> OnGaugeChanged;

        /// <summary>
        /// ゲージ満タン時のイベント
        /// </summary>
        public event Action OnGaugeFull;
    }
}
