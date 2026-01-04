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
    /// </summary>
    public class DelayedEffect
    {
        /// <summary>残り時間（秒）</summary>
        public float RemainingTime { get; set; }

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

        /// <summary>
        /// コンストラクタ（投擲物用）
        /// </summary>
        public DelayedEffect(float delayTime, Vector2Int targetPosition, bool cancelOnHole = true)
        {
            RemainingTime = delayTime;
            EffectType = DelayedEffectType.Projectile;
            TargetPosition = targetPosition;
            CancelOnHole = cancelOnHole;
        }

        /// <summary>
        /// コンストラクタ（前隙・後隙用）
        /// </summary>
        public DelayedEffect(DelayedEffectType effectType, float delayTime)
        {
            EffectType = effectType;
            RemainingTime = delayTime;
            TargetPosition = new Vector2Int(0, 0); // 未使用
            CancelOnHole = false; // 未使用
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
