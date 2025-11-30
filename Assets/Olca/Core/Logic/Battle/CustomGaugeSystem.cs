using System;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// カスタムゲージシステム
    /// リアルタイムに溜まり、満タンでカスタム画面を開ける
    /// </summary>
    public class CustomGaugeSystem
    {
        private float currentGauge; // 0.0 ~ 1.0
        private readonly float gaugeSpeed;

        /// <summary>現在のゲージ量（0.0～1.0）</summary>
        public float CurrentGauge => currentGauge;

        /// <summary>ゲージが満タンか</summary>
        public bool IsFull => currentGauge >= 1.0f;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="gaugeSpeed">ゲージの溜まる速度（1.0で1秒で満タン）</param>
        public CustomGaugeSystem(float gaugeSpeed = 1.0f)
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

            if (currentGauge >= 1.0f)
            {
                currentGauge = 1.0f;
                OnGaugeFull?.Invoke();
            }

            OnGaugeChanged?.Invoke(currentGauge);
        }

        /// <summary>
        /// ゲージをリセット（カスタム画面を閉じた後に呼ぶ）
        /// </summary>
        public void Reset()
        {
            currentGauge = 0f;
            OnGaugeChanged?.Invoke(currentGauge);
        }

        /// <summary>
        /// ゲージを任意の値に設定（デバッグ用）
        /// </summary>
        public void SetGauge(float value)
        {
            currentGauge = Math.Max(0f, Math.Min(value, 1.0f));
            OnGaugeChanged?.Invoke(currentGauge);

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
