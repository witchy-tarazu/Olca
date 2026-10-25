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
    public class EnemyAIBehavior : IDisposable
    {
        private readonly Enemy enemy;
        private List<IEnemyAIAction> actions;
        private List<IEnemyAIAction> pendingPattern;
        private Dictionary<int,EnemyActionPattern> registeredPatterns;
        private EnemyActionPattern activeDefinition,pendingDefinition;
        public int? CurrentPatternId=>activeDefinition?.Id;
        public bool IsPatternStopped { get; private set; }
        private bool executingAction, interruptRequested, endingAction, disposed;
        private IContinuingEnemyAIAction runningAction;
        private AIContext runningContext;
        private FrameCountdown cooldown;
        private int nextActionIndex;
        private List<EnemyHpActionPattern> hpPatterns;
        private EnemyHpActionPattern selectedHpPattern;

        /// <summary>終了先を含む定義を原子的に検証して登録する。動作中の初期切替も完了まで保留する。</summary>
        public bool ConfigurePatterns(IReadOnlyList<EnemyActionPattern> patterns,int initialPatternId)
        {
            if(patterns==null||patterns.Count==0)throw new ArgumentException("パターン定義が必要です",nameof(patterns));
            var copy=new Dictionary<int,EnemyActionPattern>();
            foreach(var value in patterns){if(value==null||copy.ContainsKey(value.Id))throw new ArgumentException("パターンIDの欠落/重複です",nameof(patterns));copy.Add(value.Id,value);}
            if(!copy.ContainsKey(initialPatternId))throw new ArgumentException("初期パターンが未登録です",nameof(initialPatternId));
            foreach(var value in copy.Values)if(value.EndMode==EnemyPatternEndMode.Next&&!copy.ContainsKey(value.NextPatternId.Value))throw new ArgumentException("終了先パターンが未登録です",nameof(patterns));
            if(hpPatterns!=null)foreach(var value in hpPatterns)if(value.RegisteredPatternId.HasValue&&!copy.ContainsKey(value.RegisteredPatternId.Value))throw new ArgumentException("既存HP条件の参照先が未登録です",nameof(patterns));
            if(disposed||enemy.IsDead||pendingPattern!=null)return false;
            registeredPatterns=copy;selectedHpPattern=null;return QueuePattern(copy[initialPatternId]);
        }
        public bool TrySwitchRegisteredPattern(int id)
            =>registeredPatterns!=null&&registeredPatterns.TryGetValue(id,out var definition)&&QueuePattern(definition);
        private bool QueuePattern(EnemyActionPattern definition)
        {
            if(disposed||enemy.IsDead||pendingPattern!=null)return false;
            pendingPattern=definition.CopyActions();pendingDefinition=definition;
            if(!executingAction&&runningAction==null)ApplyPendingPattern();return true;
        }

        public bool ConfigureHpPatterns(IReadOnlyList<EnemyHpActionPattern> patterns)
        {
            if(disposed||enemy.IsDead)return false;
            if(patterns==null||patterns.Count==0)throw new ArgumentException("HP区間が必要です",nameof(patterns));
            var copy=new List<EnemyHpActionPattern>(patterns);
            int last=-1;
            foreach(var value in copy){if(value==null||value.MaximumHpBasisPoints<=last)throw new ArgumentException("HP上限は昇順・重複なしです",nameof(patterns));last=value.MaximumHpBasisPoints;if(value.RegisteredPatternId.HasValue&&(registeredPatterns==null||!registeredPatterns.ContainsKey(value.RegisteredPatternId.Value)))throw new ArgumentException("HP条件のパターンが未登録です",nameof(patterns));}
            if(last!=10000||!copy[copy.Count-1].IncludesBoundary)throw new ArgumentException("最大HPまでの区間が必要です",nameof(patterns));
            hpPatterns=copy;selectedHpPattern=null;return true;
        }
        private void EvaluateHpPattern()
        {
            if(hpPatterns==null||pendingPattern!=null)return;
            foreach(var value in hpPatterns){
                long hp=(long)enemy.CurrentHp*10000, boundary=(long)enemy.MaxHp*value.MaximumHpBasisPoints;
                if(hp<boundary||(value.IncludesBoundary&&hp==boundary)){
                    if(!ReferenceEquals(value,selectedHpPattern)&&(value.RegisteredPatternId.HasValue?TrySwitchRegisteredPattern(value.RegisteredPatternId.Value):TrySwitchActionPattern(value.CopyActions())))selectedHpPattern=value;
                    return;
                }
            }
        }

        /// <summary>管理している敵</summary>
        public Enemy Enemy => enemy;

        /// <summary>行動可能か（クールダウン終了・生存中・麻痺していない）</summary>
        public bool CanAct => !disposed && !IsPatternStopped && runningAction==null && !cooldown.IsActive && !enemy.IsDead && !enemy.IsParalyzed && !enemy.IsStunned;

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
            enemy.OnStaggered+=InterruptActiveAction;
            enemy.OnDeath+=Dispose;
        }

        /// <summary>定義済み行動列への切替。同期/継続行動の完了まで保留し、旧行動後の待ちは引き継がない。</summary>
        public bool TrySwitchActionPattern(List<IEnemyAIAction> nextActions)
        {
            if(nextActions==null||nextActions.Count==0||nextActions.Exists(action=>action==null))
                throw new ArgumentException("切替先の行動定義が必要です",nameof(nextActions));
            if(disposed||enemy.IsDead||pendingPattern!=null)return false;
            pendingPattern=new List<IEnemyAIAction>(nextActions);pendingDefinition=null;
            if(!executingAction&&runningAction==null)ApplyPendingPattern();
            return true;
        }
        private void ApplyPendingPattern()
        {
            actions=pendingPattern;pendingPattern=null;activeDefinition=pendingDefinition;pendingDefinition=null;nextActionIndex=0;IsPatternStopped=false;
            cooldown.SetMilliseconds(0);
        }

        /// <summary>
        /// AI更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        /// <param name="context">AI判断に必要な情報</param>
        public void Update(int deltaTimeMs, AIContext context, bool logicalFrame = false)
        {
            if(disposed)return;
            if(enemy.IsDead){Dispose();return;}
            EvaluateHpPattern();
            if(enemy.IsStunned){
                InterruptActiveAction();
                if(logicalFrame)enemy.TickStunFrame();else enemy.TickStun(deltaTimeMs);
                return;
            }
            if(enemy.IsParalyzed)InterruptActiveAction();
            if(runningAction!=null){
                runningContext=context;
                if(!runningAction.IsRunning)EndActiveAction(false);
                else RunActiveStep(()=>runningAction.Tick(deltaTimeMs,context,logicalFrame));
                return;
            }
            if(logicalFrame)cooldown.TickFrame();else cooldown.Tick(deltaTimeMs);
            if(CanAct)ExecuteAction(context);
        }

        private void FinishAction(IEnemyAIAction action)
        {
            if(disposed)return;
            if(pendingPattern!=null)ApplyPendingPattern();
            else{
                nextActionIndex++;
                if(nextActionIndex==actions.Count&&CompletePattern())return;
                cooldown.SetMilliseconds(action.GetCooldownMs());
            }
        }
        // Loopだけが最終行動後の従来待ちを維持する。Nextは切替規則に従い旧待ちを捨てる。
        private bool CompletePattern()
        {
            nextActionIndex=0;
            if(activeDefinition==null||activeDefinition.EndMode==EnemyPatternEndMode.Loop)return false;
            cooldown.SetMilliseconds(0);
            if(activeDefinition.EndMode==EnemyPatternEndMode.Stop){IsPatternStopped=true;return true;}
            QueuePattern(registeredPatterns[activeDefinition.NextPatternId.Value]);return true;
        }
        private void RunActiveStep(Action update)
        {
            executingAction=true;
            try{update();}finally{executingAction=false;}
            if(disposed)EndActiveAction(true);
            else if(interruptRequested)EndActiveAction(true);
            else if(runningAction!=null&&!runningAction.IsRunning)EndActiveAction(false);
        }
        private void EndActiveAction(bool cancel)
        {
            var action=runningAction;
            if(action==null)return;
            endingAction=executingAction=true;
            try{if(cancel&&action.IsRunning)action.Cancel(runningContext);}
            finally{runningAction=null;runningContext=default;endingAction=executingAction=interruptRequested=false;}
            FinishAction(action);
        }
        private void InterruptActiveAction()
        {
            if(endingAction)return;
            if(executingAction){interruptRequested=true;return;}
            EndActiveAction(true);
        }
        public void Dispose()
        {
            if(disposed)return;
            disposed=true;pendingPattern=null;hpPatterns=null;selectedHpPattern=null;registeredPatterns=null;activeDefinition=pendingDefinition=null;IsPatternStopped=true;
            enemy.OnStaggered-=InterruptActiveAction;enemy.OnDeath-=Dispose;
            if(executingAction){interruptRequested=true;return;}
            EndActiveAction(true);
        }

        /// <summary>
        /// 行動を決定して実行
        /// 順次試行し、実行不可なら次へ進む。全件不可でも一巡だけで終了する。
        /// </summary>
        private void ExecuteAction(AIContext context)
        {
            for(int attempted=0,limit=actions.Count;attempted<limit;attempted++)
            {
                var action=actions[nextActionIndex];
                if(!action.CanExecute(context)){
                    nextActionIndex++;
                    if(nextActionIndex==actions.Count&&CompletePattern())return;
                    continue;
                }
                if(action is IContinuingEnemyAIAction continuing){
                    runningAction=continuing;runningContext=context;interruptRequested=false;
                    RunActiveStep(()=>action.Execute(context));
                }else{
                    executingAction=true;
                    try{action.Execute(context);}finally{executingAction=false;interruptRequested=false;}
                    FinishAction(action);
                }
                return;
            }
        }
    }
}
