using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// プレイヤー移動システム
    /// 入力処理、衝突判定、Ice滑り、Grass クールダウンを管理
    /// </summary>
    public class PlayerMovementSystem
    {
        private readonly PlayerCharacter player;
        private readonly BattleField field;
        private readonly IGameLogger logger;

        private float moveCooldown; // 移動クールダウン（Grass効果用）
        private Vector2Int? pendingSlideDirection; // Ice滑り中の方向

        private const float GRASS_MOVE_DELAY = 0.2f; // Grassパネルから移動時のクールダウン

        /// <summary>移動可能か（クールダウンと滑り状態をチェック）</summary>
        public bool CanMove => moveCooldown <= 0f && !pendingSlideDirection.HasValue;

        /// <summary>Ice滑り中か</summary>
        public bool IsSliding => pendingSlideDirection.HasValue;

        /// <summary>現在のクールダウン時間</summary>
        public float CurrentCooldown => moveCooldown;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public PlayerMovementSystem(PlayerCharacter player, BattleField field, IGameLogger logger = null)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.field = field ?? throw new ArgumentNullException(nameof(field));
            this.logger = logger;
        }

        /// <summary>
        /// 移動システムを更新（毎フレーム呼び出す）
        /// </summary>
        public void Tick(float deltaTime, IInputProvider input, List<Enemy> enemies)
        {
            // クールダウン更新
            if (moveCooldown > 0f)
            {
                moveCooldown -= deltaTime;
                if (moveCooldown < 0f)
                {
                    moveCooldown = 0f;
                }
            }

            // Ice滑り処理が優先
            if (pendingSlideDirection.HasValue)
            {
                ProcessIceSlide(enemies);
                return;
            }

            if (!CanMove) return;

            // 入力から移動方向を決定（現在は横移動のみ）
            Vector2Int direction = Vector2Int.Zero;

            if (input.Horizontal > 0.5f) direction = new Vector2Int(1, 0); // 右
            else if (input.Horizontal < -0.5f) direction = new Vector2Int(-1, 0); // 左

            if (direction != Vector2Int.Zero)
            {
                TryMove(direction, enemies);
            }
        }

        /// <summary>
        /// 移動を試みる（公開API）
        /// </summary>
        /// <returns>移動成功したらtrue</returns>
        public bool TryMove(Vector2Int direction, List<Enemy> enemies)
        {
            // クールダウン中は移動不可（Ice滑り中も含む）
            if (moveCooldown > 0f)
            {
                logger?.LogInfo("クールダウン中のため移動不可");
                return false;
            }

            // Ice滑り中は通常の移動入力を無視
            if (pendingSlideDirection.HasValue)
            {
                logger?.LogInfo("Ice滑り中のため移動不可");
                return false;
            }

            return TryMoveInternal(direction, enemies);
        }

        /// <summary>
        /// 移動を試みる（内部用）
        /// </summary>
        private bool TryMoveInternal(Vector2Int direction, List<Enemy> enemies)
        {
            Vector2Int oldPosition = player.GridPosition;
            Vector2Int newPosition = new Vector2Int(
                player.GridPosition.X + direction.X,
                player.GridPosition.Y + direction.Y
            );

            // グリッド範囲チェック
            if (!field.IsInBounds(newPosition))
            {
                logger?.LogInfo($"グリッド範囲外への移動をブロック: {newPosition}");
                OnMoveBlocked?.Invoke(newPosition);
                return false;
            }

            // 移動可能性チェック（Holeパネル等）
            if (!field.CanMoveTo(newPosition))
            {
                logger?.LogInfo($"移動不可パネルへの移動をブロック: {newPosition}");
                OnMoveBlocked?.Invoke(newPosition);
                return false;
            }

            // 敵との衝突チェック
            if (IsEnemyAt(newPosition, enemies))
            {
                logger?.LogInfo($"敵がいる位置への移動をブロック: {newPosition}");
                OnMoveBlocked?.Invoke(newPosition);
                return false;
            }

            // Grassパネルからの移動はクールダウン追加
            PanelType currentPanel = field.GetPanelType(player.GridPosition);
            if (currentPanel == PanelType.Grass)
            {
                moveCooldown = GRASS_MOVE_DELAY;
                logger?.LogInfo("Grassパネルから移動: クールダウン追加");
            }

            // 移動実行
            player.MoveTo(newPosition);
            OnMoved?.Invoke(newPosition, oldPosition);
            logger?.LogInfo($"プレイヤー移動: {oldPosition} → {newPosition}");

            // Iceパネルなら滑り開始
            PanelType newPanel = field.GetPanelType(newPosition);
            if (newPanel == PanelType.Ice)
            {
                pendingSlideDirection = direction;
                logger?.LogInfo("Iceパネル: 滑り開始");
            }

            return true;
        }

        /// <summary>
        /// Ice滑り処理（同方向に追加移動）
        /// </summary>
        private void ProcessIceSlide(List<Enemy> enemies)
        {
            if (!pendingSlideDirection.HasValue) return;

            Vector2Int direction = pendingSlideDirection.Value;
            bool slideSucceeded = TryMoveInternal(direction, enemies);

            // 滑りが失敗したら停止
            if (!slideSucceeded)
            {
                pendingSlideDirection = null;
                logger?.LogInfo("Ice滑り終了: 障害物に衝突");
                return;
            }

            // 新しい位置がIceでなければ停止
            PanelType newPanel = field.GetPanelType(player.GridPosition);
            if (newPanel != PanelType.Ice)
            {
                pendingSlideDirection = null;
                logger?.LogInfo("Ice滑り終了: 通常パネルに到達");
            }
        }

        /// <summary>
        /// 指定位置に敵がいるかチェック
        /// </summary>
        private bool IsEnemyAt(Vector2Int position, List<Enemy> enemies)
        {
            foreach (var enemy in enemies)
            {
                if (!enemy.IsDead && enemy.GridPosition.Equals(position))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 移動成功時のイベント (newPosition, oldPosition)
        /// </summary>
        public event Action<Vector2Int, Vector2Int> OnMoved;

        /// <summary>
        /// 移動失敗時のイベント
        /// </summary>
        public event Action<Vector2Int> OnMoveBlocked;
    }
}
