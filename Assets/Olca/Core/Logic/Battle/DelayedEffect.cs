using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 遅延効果のタイプ
    /// </summary>
    public enum DelayedEffectType
    {
        /// <summary>前隙（カード発動からヒット判定まで）</summary>
        Startup,

        /// <summary>後隙（ヒット判定後から行動可能まで）</summary>
        Recovery,

        /// <summary>投擲物の着弾（穴パネルチェックあり）</summary>
        Projectile
    }

    /// <summary>
    /// 個別の遅延効果データ
    /// カードの前隙・後隙、投擲カードなど、時間差で発動する効果を表現する
    /// 整数ベース（ミリ秒）で時間を管理し、浮動小数点の累積誤差を回避
    /// </summary>
    public class DelayedEffect
    {
        /// <summary>残り時間（ミリ秒）</summary>
        private FrameCountdown remaining;
        public int RemainingTimeMs { get => remaining.Milliseconds; set => remaining.SetMilliseconds(value); }
        internal long RegisteredFrame { get; set; }
        public int InitialTimeMs { get; private set; }
        public Vector2Int? SourcePosition { get; internal set; }
        public int BlastRadius { get; internal set; }
        public string ProjectileVisualKey { get; internal set; }
        private long elapsedDisplayUnits;
        // 描画コマは効果自身が実際に受け取った論理時間だけを参照する。
        public long ElapsedDisplayFrames => elapsedDisplayUnits / 50;
        public bool WasExecuted { get; internal set; }
        public long CompletedFrame { get; internal set; } = -1;
        public float Progress => InitialTimeMs <= 0 ? 1f : Math.Max(0f, Math.Min(1f, 1f - (float)RemainingTimeMs / InitialTimeMs));
        internal void Tick(int milliseconds) { remaining.Tick(milliseconds); elapsedDisplayUnits += System.Math.Max(0L, milliseconds * 3L); }
        internal void TickFrame() { remaining.TickFrame(); elapsedDisplayUnits += 50; }

        /// <summary>効果タイプ</summary>
        public DelayedEffectType EffectType { get; set; }

        /// <summary>着弾位置（絶対座標） - Projectileタイプの場合に使用</summary>
        public Vector2Int TargetPosition { get; set; }

        /// <summary>穴パネルで無効化されるか - Projectileタイプの場合に使用</summary>
        public bool CancelOnHole { get; set; }

        /// <summary>発動時に実行する処理</summary>
        public Action ExecuteAction { get; set; }

        /// <summary>キャンセル時に実行する処理（オプション）</summary>
        public Action OnCancelled { get; set; }

        // 実行・落下・戦闘終了のいずれでも、所有している動作待機を一度だけ解除する。
        internal Action OnFinished { get; set; }
        internal void Finish()
        {
            var finish = OnFinished; OnFinished = null; finish?.Invoke();
        }

        /// <summary>
        /// コンストラクタ（投擲物用）- ミリ秒版
        /// </summary>
        public DelayedEffect(int delayTimeMs, Vector2Int targetPosition, bool cancelOnHole = true)
        {
            InitialTimeMs = delayTimeMs; RemainingTimeMs = delayTimeMs;
            EffectType = DelayedEffectType.Projectile;
            TargetPosition = targetPosition;
            CancelOnHole = cancelOnHole;
        }

        /// <summary>
        /// コンストラクタ（前隙・後隙用）- ミリ秒版
        /// </summary>
        public DelayedEffect(DelayedEffectType effectType, int delayTimeMs)
        {
            EffectType = effectType;
            InitialTimeMs = delayTimeMs; RemainingTimeMs = delayTimeMs;
            TargetPosition = new Vector2Int(0, 0); // 未使用
            CancelOnHole = false; // 未使用
        }

        internal static DelayedEffect InFrames(DelayedEffectType type, int frames)
        {
            if (frames < 0) throw new ArgumentOutOfRangeException(nameof(frames));
            var effect = new DelayedEffect(type, 0); effect.remaining.SetFrames(frames); return effect;
        }

        /// <summary>
        /// 効果を実行
        /// </summary>
        public void Execute()
        {
            ExecuteAction?.Invoke();
        }
    }
}
