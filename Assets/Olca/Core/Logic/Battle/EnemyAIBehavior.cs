using System;
using System.Collections.Generic;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 個別の敵のAI行動を管理
    /// クールダウン管理、行動決定、実行を担当
    /// 整数ベース（ミリ秒）で時間を管理し、浮動小数点の累積誤差を回避
    /// </summary>
    public class EnemyAIBehavior
    {
        private readonly Enemy enemy;
        private readonly List<IEnemyAIAction> actions;
        private int currentCooldownMs;

        /// <summary>管理している敵</summary>
        public Enemy Enemy => enemy;

        /// <summary>行動可能か（クールダウン終了・生存中・麻痺していない）</summary>
        public bool CanAct => currentCooldownMs <= 0 && !enemy.IsDead && !enemy.IsParalyzed;

        /// <summary>現在のクールダウン時間（ミリ秒）</summary>
        public int CurrentCooldownMs => currentCooldownMs;

        /// <summary>現在のクールダウン時間（秒）- 互換性用</summary>
        public float CurrentCooldown => currentCooldownMs / 1000f;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="enemy">管理する敵</param>
        /// <param name="actions">AI行動リスト（優先順位順）</param>
        public EnemyAIBehavior(Enemy enemy, List<IEnemyAIAction> actions)
        {
            this.enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));

            if (actions.Count == 0)
                throw new ArgumentException("AI行動リストは空であってはいけません", nameof(actions));

            this.currentCooldownMs = 0;
        }

        /// <summary>
        /// AI更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        /// <param name="context">AI判断に必要な情報</param>
        public void Update(int deltaTimeMs, AIContext context)
        {
            if (enemy.IsDead) return;

            // スタン中はスタン時間をカウントダウンし、行動をスキップ
            if (enemy.IsStunned)
            {
                enemy.TickStun(deltaTimeMs);
                return;
            }

            // クールダウン減少
            if (currentCooldownMs > 0)
            {
                currentCooldownMs -= deltaTimeMs;
                if (currentCooldownMs < 0) currentCooldownMs = 0;
            }

            // 行動可能な場合、行動を決定して実行
            if (CanAct)
            {
                ExecuteAction(context);
            }
        }

        /// <summary>
        /// 行動を決定して実行
        /// 優先順位順に行動を試行し、最初に実行可能な行動を実行
        /// </summary>
        private void ExecuteAction(AIContext context)
        {
            foreach (var action in actions)
            {
                if (action.CanExecute(context))
                {
                    action.Execute(context);
                    currentCooldownMs = action.GetCooldownMs();
                    return;
                }
            }
        }
    }
}
