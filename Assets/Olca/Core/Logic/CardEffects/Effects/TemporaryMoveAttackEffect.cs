using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// 一時移動してから攻撃するカード効果
    /// </summary>
    public class TemporaryMoveAttackEffect : ICardEffect, IAsyncCardEffect
    {
        private readonly Vector2Int moveDirection;
        private readonly int moveDistance;
        private readonly float moveMotionTime;
        private readonly float attackMotionTime;
        private readonly MovePenetrationMode penetrationMode;

        public TemporaryMoveAttackEffect(
            Vector2Int direction,
            int distance,
            float moveMotionTime,
            float attackMotionTime,
            MovePenetrationMode penetrationMode)
        {
            this.moveDirection = direction;
            this.moveDistance = distance;
            this.moveMotionTime = moveMotionTime;
            this.attackMotionTime = attackMotionTime;
            this.penetrationMode = penetrationMode;
        }


        /// <summary>
        /// 同期実行（非推奨: 非同期版を使用してください）
        /// </summary>
        public void Execute(BattleContext context)
        {
            // 警告: この効果は非同期処理を使用するため、同期実行では正しく動作しません
            // CardUsageSystem.TryUseCurrentCardAsync() を使用してください
            throw new NotSupportedException(
                "TemporaryMoveAttackEffectは非同期専用です。CardUsageSystem.TryUseCurrentCardAsync()を使用してください。");
        }

        public async UniTask ExecuteAsync(
            BattleContext context,
            CancellationToken ct)
        {
            var player = context.Player;
            var field = context.Field;
            var enemies = context.Enemies;
            var originalPosition = player.GridPosition;

            // 1. 移動先を計算
            Vector2Int targetPosition = CalculateTargetPosition(
                originalPosition,
                field,
                enemies,
                player.TerrainState);

            // 移動先が元の位置と同じ場合（移動失敗）
            if (targetPosition == originalPosition)
            {
                // Blockingモードの場合は効果なし
                if (penetrationMode == MovePenetrationMode.Blocking)
                {
                    return;
                }
            }

            bool attackResolved = false;
            try
            {
                // 2. パネル保護を設定（一時移動中は移動元を保護）
                if (targetPosition != originalPosition)
                {
                    field.SetProtectedPosition(originalPosition);
                }

                // 3. 一時移動実行
                if (targetPosition != originalPosition)
                {
                    player.MoveTemporarily(targetPosition);
                }

                // 4. 移動モーション待機
                if (moveMotionTime > 0)
                {
                    await context.Battle.WaitForActionAsync(moveMotionTime, ct);
                }

                // 5. 攻撃実行（移動先の位置で）
                var targetEnemy = GetEnemyAtPosition(
                    player.GridPosition,
                    enemies);

                if (targetEnemy != null && !targetEnemy.IsDead)
                {
                    int damage = context.UsedCard.GetFinalPower();
                    ElementType attackElement = context.UsedCard.GetFinalElement();
                    targetEnemy.TakeDamage(damage, attackElement, context.UsedCard.Properties, context.UsedCard.PoiseDamage);
                }

                attackResolved = true;

                // 6. 攻撃モーション待機
                if (attackMotionTime > 0)
                {
                    await context.Battle.WaitForActionAsync(attackMotionTime, ct);
                }
            }
            catch (OperationCanceledException) when (attackResolved)
            {
                // 攻撃判定後の待機中断は使用済み。適用済み効果の再実行を防ぐ。
                // 判定前の中断だけを上位へ伝え、既存の使用失敗処理に任せる。
            }
            finally
            {
                // 7. 元の位置に戻る（必ず実行）
                if (player.IsInTemporaryMove)
                {
                    player.ReturnToOriginalPosition();
                }

                // 8. パネル保護を解除
                field.SetProtectedPosition(null);
            }
        }

        /// <summary>
        /// 移動先の位置を計算（貫通モードを考慮）
        /// </summary>
        private Vector2Int CalculateTargetPosition(
            Vector2Int start,
            BattleField field,
            IReadOnlyList<Enemy> enemies,
            BattleTerrainState terrain)
        {
            Vector2Int currentPos = start;

            for (int i = 0; i < moveDistance; i++)
            {
                Vector2Int nextPos = new Vector2Int(
                    currentPos.X + moveDirection.X,
                    currentPos.Y + moveDirection.Y);

                // グリッド範囲外チェック
                if (!field.IsInBounds(nextPos))
                {
                    return currentPos;
                }

                // 移動可能パネルかチェック
                if (!field.CanMoveTo(nextPos, terrain))
                {
                    return currentPos;
                }

                // 敵がいるかチェック
                bool enemyExists = IsEnemyAt(nextPos, enemies);

                if (enemyExists)
                {
                    // Blockingモード: 敵で止まる
                    if (penetrationMode == MovePenetrationMode.Blocking)
                    {
                        return currentPos;
                    }
                    // Invincibleモード: 敵を無視して通過
                }

                currentPos = nextPos;
            }

            return currentPos;
        }

        private bool IsEnemyAt(Vector2Int position, IReadOnlyList<Enemy> enemies)
        {
            foreach (var enemy in enemies)
            {
                if (enemy.GridPosition == position && !enemy.IsDead)
                {
                    return true;
                }
            }
            return false;
        }

        private Enemy GetEnemyAtPosition(
            Vector2Int position,
            IReadOnlyList<Enemy> enemies)
        {
            foreach (var enemy in enemies)
            {
                if (enemy.GridPosition == position && !enemy.IsDead)
                {
                    return enemy;
                }
            }
            return null;
        }
    }
}
