using System;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// カスタムゲージシステム
    /// リアルタイムに溜まり、満タンでカスタム画面を開ける
    /// 整数ベース（ミリ秒）で時間を管理し、浮動小数点の累積誤差を回避
    /// </summary>
    public class CustomGaugeSystem
    {
        private long gaugeRemainder;
        private long currentGaugeMs; // 0 ~ MAX_GAUGE_MS
        private readonly int gaugeSpeedPerSecond; // 1秒あたりの増加量（ミリ秒単位で計算時に使用）

        /// <summary>ゲージの最大値（ミリ秒単位）- 10秒相当</summary>
        public const int MAX_GAUGE_MS = 10000;

        /// <summary>ゲージの最大値（旧インターフェース互換）</summary>
        public const float MAX_GAUGE = 10.0f;

        /// <summary>現在のゲージ量（ミリ秒単位）</summary>
        public long CurrentGaugeMs => currentGaugeMs;

        /// <summary>現在のゲージ量（0.0～MAX_GAUGE）- 互換性用</summary>
        public float CurrentGauge => currentGaugeMs / 1000f;

        /// <summary>現在のゲージ量（正規化: 0.0～1.0）View表示用</summary>
        public float NormalizedGauge => (float)currentGaugeMs / MAX_GAUGE_MS;

        /// <summary>ゲージが満タンか</summary>
        public bool IsFull => currentGaugeMs >= MAX_GAUGE_MS;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="gaugeSpeedPerSecond">ゲージの溜まる速度（1秒あたりの増加量、デフォルト2.0）</param>
        public CustomGaugeSystem(float gaugeSpeedPerSecond = 2.0f)
        {
            // 1秒あたりの増加量をミリ秒単位に変換して保持
            this.gaugeSpeedPerSecond = (int)(gaugeSpeedPerSecond * 1000f);
            this.currentGaugeMs = 0;
        }

        /// <summary>
        /// ゲージを更新（毎フレーム呼び出す）- 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">前フレームからの経過時間（ミリ秒）</param>
        public void Update(int deltaTimeMs) => Advance(deltaTimeMs * 3L);

        /// <summary>60Hzの一フレーム。1/3000秒単位の分子を保持し、msへの丸めを累積しない。</summary>
        public void UpdateFrame() => Advance(50);

        private void Advance(long timeUnits)
        {
            if (IsFull) return;

            // 旧ms入口と論理フレーム入口を同じ精度で積算する。
            long numerator = gaugeRemainder + (long)gaugeSpeedPerSecond * timeUnits;
            currentGaugeMs += numerator / 3000;
            gaugeRemainder = numerator % 3000;

            if (currentGaugeMs >= MAX_GAUGE_MS)
            {
                currentGaugeMs = MAX_GAUGE_MS;
                gaugeRemainder = 0;
                OnGaugeFull?.Invoke();
            }

            OnGaugeChanged?.Invoke(NormalizedGauge);
        }

        /// <summary>
        /// ゲージをリセット（カスタム画面を閉じた後に呼ぶ）
        /// </summary>
        public void Reset()
        {
            currentGaugeMs = 0;
            gaugeRemainder = 0;
            OnGaugeChanged?.Invoke(NormalizedGauge);
        }

        /// <summary>
        /// ゲージを任意の値に設定（デバッグ用）- ミリ秒版
        /// </summary>
        /// <param name="valueMs">設定する値（ミリ秒、0～MAX_GAUGE_MS）</param>
        public void SetGaugeMs(long valueMs)
        {
            currentGaugeMs = Math.Max(0, Math.Min(valueMs, MAX_GAUGE_MS));
            gaugeRemainder = 0;
            OnGaugeChanged?.Invoke(NormalizedGauge);

            if (IsFull)
            {
                OnGaugeFull?.Invoke();
            }
        }

        /// <summary>
        /// ゲージを任意の値に設定（デバッグ用）- float版（後方互換）
        /// </summary>
        /// <param name="value">設定する値（0.0～MAX_GAUGE）</param>
        public void SetGauge(float value)
        {
            SetGaugeMs((long)(value * 1000f));
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

namespace Olca.Core.Logic.Battle
{
    /// <summary>満タン演出のフレーム選択。参照回数や描画fpsでは進めない。</summary>
    public sealed class GaugeSpritePlayback
    {
        public const int FrameCount=8, FramesPerSprite=6;
        private long? start;
        public int Sample(bool full,long logicalFrame)
        {
            if(!full){Reset();return -1;}
            if(!start.HasValue||logicalFrame<start.Value)start=logicalFrame;
            return (int)((logicalFrame-start.Value)/FramesPerSprite%FrameCount);
        }
        public void Reset(){start=null;}
    }
}
