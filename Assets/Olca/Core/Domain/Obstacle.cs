using System;

namespace Olca.Core.Domain
{
    /// <summary>既存障害物の接触後停止。未指定の旧データは停止なしを維持する。</summary>
    public sealed class ObstacleControl
    {
        public int DurationFrames { get; }
        public bool IsRestraint { get; }
        public int DamagePerPulse { get; }
        public int DamageIntervalFrames { get; }
        public ObstacleControl(int durationFrames, bool isRestraint = false, int damagePerPulse = 0, int damageIntervalFrames = 0)
        {
            if (durationFrames <= 0 || damagePerPulse < 0 || damageIntervalFrames < 0 ||
                (damagePerPulse > 0 && (!isRestraint || damageIntervalFrames <= 0)))
                throw new ArgumentException("正の停止時間と、拘束中ダメージには非負量・正の間隔が必要です");
            DurationFrames = durationFrames; IsRestraint = isRestraint;
            DamagePerPulse = damagePerPulse; DamageIntervalFrames = damageIntervalFrames;
        }
    }

    /// <summary>
    /// 障害物のペナルティ種類
    /// </summary>
    public enum ObstaclePenaltyType
    {
        ImmediateDamage,    // 即座ダメージ
        StatusEffect,       // 状態異常
        CardDiscard         // カード破棄
    }

    /// <summary>
    /// 障害物データ
    /// </summary>
    public class Obstacle
    {
        public string ObstacleId { get; }
        public string Name { get; }
        public ObstacleControl Control { get; }
        public ObstaclePenaltyType PenaltyType { get; }
        public int PenaltyValue { get; }           // ダメージ量、破棄枚数など
        public string StatusEffectId { get; }      // 状態異常ID（PenaltyTypeがStatusEffectの場合）

        /// <summary>
        /// 即座ダメージ障害物
        /// </summary>
        public Obstacle(string obstacleId, string name, int damage, ObstacleControl control = null)
        {
            Control = control;
            ObstacleId = obstacleId;
            Name = name;
            PenaltyType = ObstaclePenaltyType.ImmediateDamage;
            PenaltyValue = damage;
            StatusEffectId = null;
        }

        /// <summary>
        /// 状態異常障害物
        /// </summary>
        public Obstacle(string obstacleId, string name, string statusEffectId, ObstacleControl control = null)
        {
            Control = control;
            ObstacleId = obstacleId;
            Name = name;
            PenaltyType = ObstaclePenaltyType.StatusEffect;
            PenaltyValue = 0;
            StatusEffectId = statusEffectId;
        }

        /// <summary>
        /// カード破棄障害物
        /// </summary>
        public static Obstacle CreateCardDiscardObstacle(string obstacleId, string name, int discardCount, ObstacleControl control = null)
        {
            return new Obstacle(obstacleId, name, ObstaclePenaltyType.CardDiscard, discardCount, control);
        }

        // 内部用コンストラクタ（カード破棄用）
        private Obstacle(string obstacleId, string name, ObstaclePenaltyType penaltyType, int penaltyValue, ObstacleControl control)
        {
            Control = control;
            ObstacleId = obstacleId;
            Name = name;
            PenaltyType = penaltyType;
            PenaltyValue = penaltyValue;
            StatusEffectId = null;
        }
    }
}
