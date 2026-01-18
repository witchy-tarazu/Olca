using System;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// 障害物処理システム
    /// 障害物との接触判定とペナルティ適用を管理
    /// </summary>
    public class ObstacleSystem
    {
        private readonly IGameLogger logger;
        private const float OBSTACLE_CONTACT_DISTANCE = 1.0f;  // 障害物との接触距離

        public ObstacleSystem(IGameLogger logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// 障害物との接触判定
        /// ジャンプ中は回避可能
        /// </summary>
        public ObstacleContactResult CheckObstacleContact(
            MapCell cell,
            float playerPosition,
            float playerHeight)
        {
            if (cell == null || cell.Type != CellType.Obstacle)
            {
                return new ObstacleContactResult { IsContact = false };
            }

            // セル位置とプレイヤー位置の距離をチェック
            float distance = Math.Abs(cell.Index - playerPosition);
            if (distance > OBSTACLE_CONTACT_DISTANCE)
            {
                return new ObstacleContactResult { IsContact = false };
            }

            // ジャンプで回避可能か判定
            if (playerHeight > 0.1f)  // ジャンプ中（地面より高い位置）
            {
                logger?.LogInfo($"障害物をジャンプで回避: {cell.Obstacle.ObstacleId}");
                return new ObstacleContactResult
                {
                    IsContact = true,
                    IsAvoided = true,
                    Obstacle = cell.Obstacle
                };
            }

            // 回避失敗 → ペナルティ適用
            logger?.LogInfo($"障害物に接触: {cell.Obstacle.ObstacleId}, ペナルティ: {cell.Obstacle.PenaltyType}");
            return new ObstacleContactResult
            {
                IsContact = true,
                IsAvoided = false,
                Obstacle = cell.Obstacle
            };
        }

        /// <summary>
        /// ペナルティ情報を生成
        /// </summary>
        public PenaltyInfo CreatePenaltyInfo(Obstacle obstacle)
        {
            if (obstacle == null)
            {
                throw new ArgumentNullException(nameof(obstacle));
            }

            return new PenaltyInfo
            {
                PenaltyType = obstacle.PenaltyType,
                Damage = obstacle.PenaltyType == ObstaclePenaltyType.ImmediateDamage
                    ? obstacle.PenaltyValue
                    : 0,
                StatusEffectId = obstacle.StatusEffectId,
                CardDiscardCount = obstacle.PenaltyType == ObstaclePenaltyType.CardDiscard
                    ? obstacle.PenaltyValue
                    : 0
            };
        }
    }

    /// <summary>
    /// 障害物接触結果
    /// </summary>
    public class ObstacleContactResult
    {
        /// <summary>障害物と接触したか</summary>
        public bool IsContact { get; set; }

        /// <summary>ジャンプで回避できたか</summary>
        public bool IsAvoided { get; set; }

        /// <summary>接触した障害物</summary>
        public Obstacle Obstacle { get; set; }
    }

    /// <summary>
    /// ペナルティ情報
    /// </summary>
    public class PenaltyInfo
    {
        /// <summary>ペナルティ種別</summary>
        public ObstaclePenaltyType PenaltyType { get; set; }

        /// <summary>即時ダメージ量</summary>
        public int Damage { get; set; }

        /// <summary>状態異常ID</summary>
        public string StatusEffectId { get; set; }

        /// <summary>カード破棄枚数</summary>
        public int CardDiscardCount { get; set; }
    }
}
