using System;
using System.Collections.Generic;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 個別の敵のAI行動を管理
    /// クールダウン管理、行動決定、実行を担当
    /// </summary>
    public class EnemyAIBehavior
    {
        private readonly Enemy enemy;
        private readonly List<IEnemyAIAction> actions;
        private float currentCooldown;

        /// <summary>管理している敵</summary>
        public Enemy Enemy => enemy;

        /// <summary>行動可能か（クールダウンが終了していて、敵が生存している）</summary>
        public bool CanAct => currentCooldown <= 0f && !enemy.IsDead;

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

            this.currentCooldown = 0f;
        }

        /// <summary>
        /// AI更新
        /// </summary>
        /// <param name="deltaTime">経過時間（秒）</param>
        /// <param name="context">AI判断に必要な情報</param>
        public void Update(float deltaTime, AIContext context)
        {
            if (enemy.IsDead) return;

            // クールダウン減少
            if (currentCooldown > 0f)
            {
                currentCooldown -= deltaTime;
                if (currentCooldown < 0f) currentCooldown = 0f;
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
                    currentCooldown = action.GetCooldown();
                    return;
                }
            }
        }
    }
}
