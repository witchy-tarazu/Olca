using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// 指定位置に障害物（ロックキューブ等）を設置するカード効果
    /// </summary>
    public class ObstacleEffect : ICardEffect
    {
        private readonly Vector2Int offset;
        private readonly int obstacleHp;
        private readonly string obstacleId;

        /// <param name="offset">プレイヤー位置からの相対座標（例: 前方1マスは (1,0)）</param>
        /// <param name="obstacleHp">障害物の耐久値</param>
        /// <param name="obstacleId">障害物ID</param>
        public ObstacleEffect(Vector2Int offset, int obstacleHp, string obstacleId = "rock_cube")
        {
            if (obstacleHp <= 0) throw new ArgumentException("耐久値は1以上である必要があります", nameof(obstacleHp));
            if (string.IsNullOrEmpty(obstacleId)) throw new ArgumentException("障害物IDが無効です", nameof(obstacleId));
            this.offset = offset;
            this.obstacleHp = obstacleHp;
            this.obstacleId = obstacleId;
        }

        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var playerPos = context.GetEffectivePlayerPosition();
            var targetPos = new Vector2Int(playerPos.X + offset.X, playerPos.Y + offset.Y);

            // 設置不可の場合はスキップ
            if (!context.Field.CanPlaceObstacle(targetPos)) return;

            var obstacle = new Battle.FieldObstacle(obstacleId, obstacleHp);
            context.Field.PlaceObstacle(targetPos, obstacle);
        }
    }
}
