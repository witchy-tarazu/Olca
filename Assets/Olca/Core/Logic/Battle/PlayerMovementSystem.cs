using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// プレイヤー移動システム
    /// 入力処理、衝突判定、Ice滑り、Grass クールダウンを管理
    /// 整数ベース（ミリ秒）で時間を管理し、浮動小数点の累積誤差を回避
    /// </summary>
    /// <summary>調整可能な暫定移動時間。占有は中間、奇数は後半側のフレームへ切り上げる。</summary>
    public sealed class NormalMoveTiming
    {
        public const int DefaultNormalFrames = 9;
        public const int DefaultGrassFrames = 12;
        public int NormalFrames { get; }
        public int GrassFrames { get; }
        public NormalMoveTiming(int normalFrames = DefaultNormalFrames, int grassFrames = DefaultGrassFrames)
        {
            if (normalFrames <= 0 || grassFrames <= 0) throw new ArgumentOutOfRangeException(nameof(normalFrames));
            NormalFrames = normalFrames; GrassFrames = grassFrames;
        }
        public static int Midpoint(int totalFrames) => totalFrames / 2 + totalFrames % 2;
    }

    public class PlayerMovementSystem
    {
        private readonly PlayerCharacter player;
        private readonly BattleField field;
        private readonly IGameLogger logger;
        private readonly Func<bool> canStartNormalMove;

        public NormalMoveTiming Timing { get; }
        public bool IsNormalMoving { get; private set; }
        public Vector2Int NormalMoveFrom { get; private set; }
        public Vector2Int NormalMoveTo { get; private set; }
        public int NormalMoveDurationFrames { get; private set; }
        public int NormalMoveCommitFrame => NormalMoveTiming.Midpoint(NormalMoveDurationFrames);
        public float NormalMoveProgress => IsNormalMoving ? (float)(normalMoveUnits / (NormalMoveDurationFrames * 50d)) : 1f;
        private long normalMoveUnits;
        private bool normalMoveCommitted;

        private FrameCountdown moveCooldown; // 移動クールダウン（ミリ秒）
        private Vector2Int? pendingSlideDirection; // Ice滑り中の方向

        private const int GRASS_MOVE_DELAY_MS = 200; // Grassパネルから移動時のクールダウン（ミリ秒）
        private const int NORMAL_MOVE_DELAY_MS = 150; // 通常移動時のクールダウン（ミリ秒、押しっぱなし対応）

        /// <summary>移動可能か（クールダウンと滑り状態をチェック）</summary>
        public bool CanMove => !IsNormalMoving && !moveCooldown.IsActive && !IsSliding && (canStartNormalMove?.Invoke() ?? true);

        /// <summary>Ice滑り中か</summary>
        public bool IsSliding => player.TerrainState.ReceivesPanelEffects && pendingSlideDirection.HasValue;

        /// <summary>現在のクールダウン時間（ミリ秒）</summary>
        public int CurrentCooldownMs => moveCooldown.Milliseconds;

        /// <summary>現在のクールダウン時間（秒）- 互換性用</summary>
        public float CurrentCooldown => moveCooldown.Milliseconds / 1000f;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public PlayerMovementSystem(PlayerCharacter player, BattleField field, IGameLogger logger = null, Func<bool> canStartNormalMove = null, NormalMoveTiming timing = null)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.field = field ?? throw new ArgumentNullException(nameof(field));
            this.logger = logger;
            this.canStartNormalMove = canStartNormalMove;
            Timing = timing ?? new NormalMoveTiming();
        }

        /// <summary>
        /// 移動システムを更新（毎フレーム呼び出す）- 整数ベース版
        /// </summary>
        public void Tick(int deltaTimeMs, IInputProvider input, List<Enemy> enemies, bool logicalFrame = false)
        {
            CancelSlideWhileFloating();

            // クールダウン更新
            if (logicalFrame) moveCooldown.TickFrame(); else moveCooldown.Tick(deltaTimeMs);

            if (IsNormalMoving)
            {
                AdvanceNormalMove(logicalFrame ? 50L : deltaTimeMs * 3L, enemies);
                return; // 動作中に届いた入力は予約しない。終了フレームも次の動作を開始しない。
            }

            // Ice滑り処理が優先
            if (pendingSlideDirection.HasValue)
            {
                ProcessIceSlide(enemies);
                return;
            }

            if (!CanMove || input == null) return;

            // 入力から移動方向を決定（現在は横移動のみ）
            // Horizontalを使用して押しっぱなし移動に対応
            Vector2Int direction = Vector2Int.Zero;

            float horizontal = input is IHorizontalTapInput tap && tap.HorizontalTap != 0 ? tap.HorizontalTap : input.Horizontal;
            if (horizontal > 0.5f) direction = new Vector2Int(1, 0); // 右
            else if (horizontal < -0.5f) direction = new Vector2Int(-1, 0); // 左

            if (direction != Vector2Int.Zero)
            {
                if (logicalFrame)
                {
                    if (TryStartNormalMove(direction, enemies)) AdvanceNormalMove(50L, enemies);
                }
                else TryMove(direction, enemies); // 旧ms入口の即時移動契約は維持する。
            }
        }

        /// <summary>新しい通常移動を開始する。開始時は移動元を占有し、進行は論理更新だけで行う。</summary>
        public bool TryStartNormalMove(Vector2Int direction, List<Enemy> enemies)
        {
            if (!CanMove || player.IsDead || player.IsActionPrevented || Math.Abs((long)direction.X) + Math.Abs((long)direction.Y) != 1) return false;
            var target = new Vector2Int(player.GridPosition.X + direction.X, player.GridPosition.Y + direction.Y);
            if (!CanEnterNormalTarget(target, enemies)) return false;
            NormalMoveFrom = player.GridPosition; NormalMoveTo = target;
            NormalMoveDurationFrames = field.GetPanelType(NormalMoveFrom) == PanelType.Grass && player.TerrainState.ReceivesPanelEffects
                ? Timing.GrassFrames : Timing.NormalFrames;
            normalMoveUnits = 0; normalMoveCommitted = false; IsNormalMoving = true;
            return true;
        }
        private bool CanEnterNormalTarget(Vector2Int target, List<Enemy> enemies)
        {
            if (field.IsInBounds(target) && field.CanMoveTo(target, player.TerrainState) && !IsEnemyAt(target, enemies)) return true;
            OnMoveBlocked?.Invoke(target); return false;
        }
        private void AdvanceNormalMove(long units, List<Enemy> enemies)
        {
            if (player.IsDead || player.IsActionPrevented || player.GridPosition != (normalMoveCommitted ? NormalMoveTo : NormalMoveFrom))
            { InterruptNormalMove(); return; }
            normalMoveUnits = Math.Min(NormalMoveDurationFrames * 50L, normalMoveUnits + Math.Max(0, units));
            if (!normalMoveCommitted && normalMoveUnits >= NormalMoveCommitFrame * 50L)
            {
                if (!CanEnterNormalTarget(NormalMoveTo, enemies)) { InterruptNormalMove(); return; }
                normalMoveCommitted = true;
                player.MoveTo(NormalMoveTo);
                OnMoved?.Invoke(NormalMoveTo, NormalMoveFrom);
                if (!IsNormalMoving) return;
                if (field.GetPanelType(NormalMoveTo) == PanelType.Ice && player.TerrainState.ReceivesPanelEffects)
                    pendingSlideDirection = new Vector2Int(NormalMoveTo.X - NormalMoveFrom.X, NormalMoveTo.Y - NormalMoveFrom.Y);
            }
            if (normalMoveUnits >= NormalMoveDurationFrames * 50L) IsNormalMoving = false;
        }
        /// <summary>確定前は移動元、確定後は移動先に留める。予約入力や位置の巻戻しは作らない。</summary>
        public void InterruptNormalMove() { IsNormalMoving = false; normalMoveUnits = 0; }

        /// <summary>
        /// 移動を試みる（公開API）
        /// </summary>
        /// <returns>移動成功したらtrue</returns>
        public bool TryMove(Vector2Int direction, List<Enemy> enemies)
        {
            // カード動作中の能動入力だけを拒否する。氷滑り・効果による移動は内部経路を維持。
            if (IsNormalMoving || !(canStartNormalMove?.Invoke() ?? true)) return false;
            CancelSlideWhileFloating();

            // クールダウン中は移動不可（Ice滑り中も含む）
            if (moveCooldown.IsActive)
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
            if (!field.CanMoveTo(newPosition, player.TerrainState))
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

            // Grassパネルからの移動は長めのクールダウン、それ以外は通常のクールダウン
            PanelType currentPanel = field.GetPanelType(player.GridPosition);
            if (currentPanel == PanelType.Grass && player.TerrainState.ReceivesPanelEffects)
            {
                moveCooldown.SetMilliseconds(GRASS_MOVE_DELAY_MS);
                logger?.LogInfo("Grassパネルから移動: 長めのクールダウン追加");
            }
            else
            {
                moveCooldown.SetMilliseconds(NORMAL_MOVE_DELAY_MS);
                logger?.LogInfo("通常移動: クールダウン追加");
            }

            // 移動実行
            player.MoveTo(newPosition);
            OnMoved?.Invoke(newPosition, oldPosition);
            logger?.LogInfo($"プレイヤー移動: {oldPosition} → {newPosition}");

            // Iceパネルなら滑り開始
            PanelType newPanel = field.GetPanelType(newPosition);
            if (newPanel == PanelType.Ice && player.TerrainState.ReceivesPanelEffects)
            {
                pendingSlideDirection = direction;
                logger?.LogInfo("Iceパネル: 滑り開始");
            }

            return true;
        }

        private void CancelSlideWhileFloating()
        {
            // 滑り中に付与された場合も次の移動判定から地形の強制移動を止める。
            if (player.TerrainState.IsFloating) pendingSlideDirection = null;
        }

        /// <summary>Ice滑り処理（同方向に追加移動）</summary>
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
