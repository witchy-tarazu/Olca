namespace Olca.Core.Domain
{
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
        public ObstaclePenaltyType PenaltyType { get; }
        public int PenaltyValue { get; }           // ダメージ量、破棄枚数など
        public string StatusEffectId { get; }      // 状態異常ID（PenaltyTypeがStatusEffectの場合）

        /// <summary>
        /// 即座ダメージ障害物
        /// </summary>
        public Obstacle(string obstacleId, string name, int damage)
        {
            ObstacleId = obstacleId;
            Name = name;
            PenaltyType = ObstaclePenaltyType.ImmediateDamage;
            PenaltyValue = damage;
            StatusEffectId = null;
        }

        /// <summary>
        /// 状態異常障害物
        /// </summary>
        public Obstacle(string obstacleId, string name, string statusEffectId)
        {
            ObstacleId = obstacleId;
            Name = name;
            PenaltyType = ObstaclePenaltyType.StatusEffect;
            PenaltyValue = 0;
            StatusEffectId = statusEffectId;
        }

        /// <summary>
        /// カード破棄障害物
        /// </summary>
        public static Obstacle CreateCardDiscardObstacle(string obstacleId, string name, int discardCount)
        {
            return new Obstacle(obstacleId, name, ObstaclePenaltyType.CardDiscard, discardCount);
        }

        // 内部用コンストラクタ（カード破棄用）
        private Obstacle(string obstacleId, string name, ObstaclePenaltyType penaltyType, int penaltyValue)
        {
            ObstacleId = obstacleId;
            Name = name;
            PenaltyType = penaltyType;
            PenaltyValue = penaltyValue;
            StatusEffectId = null;
        }
    }
}
