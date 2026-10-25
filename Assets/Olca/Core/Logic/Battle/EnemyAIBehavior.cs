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
        private List<IEnemyAIAction> actions;
        private List<IEnemyAIAction> pendingPattern;
        private bool executingAction;
        private FrameCountdown cooldown;
        private int nextActionIndex;

        /// <summary>管理している敵</summary>
        public Enemy Enemy => enemy;

        /// <summary>行動可能か（クールダウン終了・生存中・麻痺していない）</summary>
        public bool CanAct => !cooldown.IsActive && !enemy.IsDead && !enemy.IsParalyzed && !enemy.IsStunned;

        /// <summary>現在のクールダウン時間（ミリ秒）</summary>
        public int CurrentCooldownMs => cooldown.Milliseconds;

        /// <summary>現在のクールダウン時間（秒）- 互換性用</summary>
        public float CurrentCooldown => cooldown.Milliseconds / 1000f;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="enemy">管理する敵</param>
        /// <param name="actions">AI行動リスト（実行順）</param>
        public EnemyAIBehavior(Enemy enemy, List<IEnemyAIAction> actions)
        {
            this.enemy = enemy ?? throw new ArgumentNullException(nameof(enemy));
            if(actions==null)throw new ArgumentNullException(nameof(actions));
            this.actions = new List<IEnemyAIAction>(actions);

            if (actions.Count == 0)
                throw new ArgumentException("AI行動リストは空であってはいけません", nameof(actions));

            if(this.actions.Exists(action=>action==null))throw new ArgumentException("AI行動が欠落しています",nameof(actions));
            this.cooldown = new FrameCountdown();
        }

        /// <summary>定義済み行動列への切替。同期行動中は完了まで保留し、旧行動後の待ちは引き継がない。</summary>
        public bool TrySwitchActionPattern(List<IEnemyAIAction> nextActions)
        {
            if(nextActions==null||nextActions.Count==0||nextActions.Exists(action=>action==null))
                throw new ArgumentException("切替先の行動定義が必要です",nameof(nextActions));
            if(enemy.IsDead||pendingPattern!=null)return false;
            pendingPattern=new List<IEnemyAIAction>(nextActions);
            if(!executingAction)ApplyPendingPattern();
            return true;
        }
        private void ApplyPendingPattern()
        {
            actions=pendingPattern;pendingPattern=null;nextActionIndex=0;
            cooldown.SetMilliseconds(0);
        }

        /// <summary>
        /// AI更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        /// <param name="context">AI判断に必要な情報</param>
        public void Update(int deltaTimeMs, AIContext context, bool logicalFrame = false)
        {
            if (enemy.IsDead) return;

            // スタン中はスタン時間をカウントダウンし、行動をスキップ
            if (enemy.IsStunned)
            {
                if (logicalFrame) enemy.TickStunFrame(); else enemy.TickStun(deltaTimeMs);
                return;
            }

            // クールダウン減少
            if (logicalFrame) cooldown.TickFrame(); else cooldown.Tick(deltaTimeMs);

            // 行動可能な場合、行動を決定して実行
            if (CanAct)
            {
                ExecuteAction(context);
            }
        }

        /// <summary>
        /// 行動を決定して実行
        /// 順次試行し、実行不可なら次へ進む。全件不可でも一巡だけで終了する。
        /// </summary>
        private void ExecuteAction(AIContext context)
        {
            for(int attempted=0;attempted<actions.Count;attempted++)
            {
                var action=actions[nextActionIndex];
                if(!action.CanExecute(context)){
                    nextActionIndex=(nextActionIndex+1)%actions.Count;
                    continue;
                }
                executingAction=true;
                try{action.Execute(context);}finally{executingAction=false;}
                // 完了後にだけ切替。旧待ちを移植したり、新AIとは別の待ちを追加したりしない。
                if(pendingPattern!=null)ApplyPendingPattern();
                else{
                    nextActionIndex=(nextActionIndex+1)%actions.Count;
                    cooldown.SetMilliseconds(action.GetCooldownMs());
                }
                return;
            }
        }
    }
}
