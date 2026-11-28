using System.Linq;
using Olca.Core.Domain;
using System;
using System.Threading;
using Olca.Core.Interfaces;
using Olca.Core.Logic.CardEffects;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 基本攻撃AI: プレイヤーに隣接したら攻撃
    /// 整数ベース（ミリ秒）で時間を管理
    /// </summary>
    public class BasicAttackAction : IEnemyAIAction
    {
        private readonly int attackDamage;
        private readonly int? poiseDamage;
        private readonly ElementType attackElement;
        private readonly AttackProperty attackProperties;
        private readonly int cooldownMs;
        private readonly System.Action<PlayerCharacter> onHit;
        private readonly int hitInvincibilityMs;
        private readonly int hitStopFrames;

        /// <summary>
        /// コンストラクタ - ミリ秒版
        /// </summary>
        /// <param name="attackDamage">攻撃ダメージ</param>
        /// <param name="cooldownMs">攻撃後のクールダウン時間（ミリ秒）</param>
        public BasicAttackAction(int attackDamage, int cooldownMs, ElementType attackElement = ElementType.None, AttackProperty attackProperties = AttackProperty.None, int? poiseDamage = null, System.Action<PlayerCharacter> onHit = null, int hitInvincibilityMs = 0, int hitStopFrames = 0)
        {
            if(hitStopFrames<0)throw new System.ArgumentOutOfRangeException(nameof(hitStopFrames));
            this.hitStopFrames=hitStopFrames;
            if (hitInvincibilityMs < 0) throw new System.ArgumentOutOfRangeException(nameof(hitInvincibilityMs));
            this.onHit = onHit; this.hitInvincibilityMs = hitInvincibilityMs;
            if (poiseDamage < 0) throw new System.ArgumentOutOfRangeException(nameof(poiseDamage));
            this.poiseDamage = poiseDamage;
            AttackPropertyRules.Validate(attackProperties);
            this.attackProperties = attackProperties;
            this.attackElement = attackElement;
            this.attackDamage = attackDamage;
            this.cooldownMs = cooldownMs;
        }

        /// <summary>
        /// コンストラクタ - float版（後方互換）
        /// </summary>
        /// <param name="attackDamage">攻撃ダメージ</param>
        /// <param name="cooldown">攻撃後のクールダウン時間（秒）</param>
        public BasicAttackAction(int attackDamage = 10, float cooldown = 2.0f, ElementType attackElement = ElementType.None, AttackProperty attackProperties = AttackProperty.None, int? poiseDamage = null, System.Action<PlayerCharacter> onHit = null, int hitInvincibilityMs = 0, int hitStopFrames = 0)
            : this(attackDamage, (int)(cooldown * 1000f), attackElement, attackProperties, poiseDamage, onHit, hitInvincibilityMs, hitStopFrames)
        {
        }

        /// <summary>
        /// プレイヤーと隣接していれば攻撃可能
        /// </summary>
        public bool CanExecute(AIContext context)
        {
            if (!context.HasVisiblePlayer) return false;

            // プレイヤーと隣接（距離1）していれば攻撃可能
            return context.GetDistanceToPlayer() == 1;
        }

        /// <summary>
        /// プレイヤーにダメージを与える
        /// </summary>
        public void Execute(AIContext context)
        {
            if (!CanExecute(context)) return;
            var direction = new Vector2Int(context.Player.GridPosition.X - context.Self.GridPosition.X,
                context.Player.GridPosition.Y - context.Self.GridPosition.Y);
            var resolved = context.ResolveDirectionalInput(direction);
            if (resolved != direction) {
                context.Logger?.LogInfo($"{context.Self.Name}の攻撃方向が反転して空振りした");
                return; // 行動は実行済み。AI側の既存クールダウンは通常どおり開始する。
            }

            if (context.Player.TryTakeDamage(attackDamage, attackElement: attackElement, attackProperties: attackProperties, poiseDamage: poiseDamage, hitStopFrames: hitStopFrames))
            {
                onHit?.Invoke(context.Player);
                if (attackDamage > 0 && !context.Player.IsDead && hitInvincibilityMs > 0) context.Player.SetInvincible(hitInvincibilityMs);
            }
            context.Logger?.LogInfo($"{context.Self.Name}が{attackDamage}ダメージを与えた");
        }

        /// <summary>
        /// 攻撃後のクールダウン時間を取得（ミリ秒）
        /// </summary>
        public int GetCooldownMs() => cooldownMs;

        /// <summary>
        /// 攻撃後のクールダウン時間を取得（秒）- 互換性用
        /// </summary>
        public float GetCooldown() => cooldownMs / 1000f;
    }
    /// <summary>同じカード効果を敵の使用者・向きで実行する。期間はカード定義の論理前後隙を使う。</summary>
    public sealed class EnemyCardAction : IContinuingEnemyAIAction
    {
        private readonly Card card;
        private readonly EnemySummonDefinition summon;
        private readonly ICardDatabase database;
        private readonly int cooldownMs;
        private CancellationTokenSource cancellation;
        private FrameTask task;
        public bool IsRunning {get{ObserveCompletion();return cancellation!=null&&task.Status==FrameTaskStatus.Pending;}}
        public EnemyCardAction(Card card,int cooldownMs,ICardDatabase database=null,EnemySummonDefinition summon=null)
        {
            if(cooldownMs<0)throw new ArgumentOutOfRangeException(nameof(cooldownMs));
            this.card=card?.Clone()??throw new ArgumentNullException(nameof(card));
            this.cooldownMs=cooldownMs;this.database=database;this.summon=summon;
            ValidateCard(this.card);
        }
        internal static void ValidateCard(Card value)
        {
            if(value==null)throw new ArgumentNullException(nameof(value));
            if(value.AttackPowerBasisPoints!=0)throw new NotSupportedException("敵カードの攻撃力基準は未登録です");
        }
        public bool CanExecute(AIContext context)
        {
            if(context.Battle==null||context.Battle.IsGameOver||context.Self==null||context.Self.IsDead||IsRunning)return false;
            if(context.Self.IsInTemporaryMove)return false;
            if(card.Category==CardCategory.Defense||card.Category==CardCategory.Support)return true;
            if(!context.HasVisiblePlayer)return false;
            if(card.MoveMotionTime>0||card.AttackMotionTime>0)return true;
            if(card.TargetType==Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single&&(context.Self.GridPosition.Y!=context.Player.GridPosition.Y||context.Self.GridPosition.X==context.Player.GridPosition.X))return false;
            int range=card.TargetType==Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile?1:card.TargetType==Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTwoTiles?2:card.TargetType==Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontThreeTiles?3:0;
            if((card.Id=="sword"||card.Id=="fire_sword")&&card.TargetType==Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single)range=1;
            return range==0||context.Self.GridPosition.Y==context.Player.GridPosition.Y&&Math.Abs(context.Self.GridPosition.X-context.Player.GridPosition.X)<=range;
        }
        public void Execute(AIContext context)
        {
            if(!CanExecute(context))return;
            cancellation?.Dispose();cancellation=new CancellationTokenSource();
            int forward=context.Player.GridPosition.X<context.Self.GridPosition.X?-1:1;
            forward=context.ResolveDirectionalInput(new Vector2Int(forward,0)).X;
            var used=card.Category==CardCategory.Attack&&context.Self.IsConcentrating?card.WithPowerMultiplier(context.Self.ConsumeConcentrateBuff()):card;
            var effectContext=new BattleContext(context.Battle,used,context.Self,forward){CardDatabase=database,ActionCancellation=cancellation.Token,TemporaryPlayerPosition=context.Self.GridPosition,LockedSingleTarget=context.Player.GridPosition};
            ICardEffect effect=summon==null?CardEffectFactory.CreateEffect(used):new EnemySummonEffect(summon);var previewOwner=new object();
            bool notified=false;
            effectContext.AttackResolved=()=>{context.Battle.ClearEnemyAttackTelegraph(previewOwner);if(!notified){notified=true;context.Battle.NotifyEnemyCardUsed(context.Self,card.Id);}};
            if(used.Category==CardCategory.Attack||used.Category==CardCategory.Field){
                if(effect is Olca.Core.Logic.CardEffects.Effects.ModifiableCardEffect modified)context.Battle.SetEnemyAttackTelegraph(previewOwner,modified.PreviewTargets(effectContext));
                else if(effect is Olca.Core.Logic.CardEffects.Effects.TemporaryMoveAttackEffect movement)context.Battle.SetEnemyAttackTelegraph(previewOwner,new[]{movement.PreviewTarget(effectContext)});
            }
            task=UseAsync(effectContext,effect,previewOwner,cancellation.Token);
            ObserveCompletion();
        }
        private async FrameTask UseAsync(BattleContext context,ICardEffect effect,object previewOwner,CancellationToken ct)
        {
            var card=context.UsedCard;
            try{
                if(effect is IAsyncCardEffect asyncEffect)await asyncEffect.ExecuteAsync(context,ct);
                else{
                    if(!card.IsInstantEffect){if(card.StartupFrames.HasValue)await context.Battle.WaitForActionFramesAsync(card.StartupFrames.Value,ct);else if(card.StartupTime>0)await context.Battle.WaitForActionAsync(card.StartupTime,ct);}
                    ct.ThrowIfCancellationRequested();context.ResolveAttackPreview();effect.Execute(context);
                    if(!card.IsInstantEffect){if(card.RecoveryFrames.HasValue)await context.Battle.WaitForActionFramesAsync(card.RecoveryFrames.Value,ct);else if(card.RecoveryTime>0)await context.Battle.WaitForActionAsync(card.RecoveryTime,ct);}
                }
            }finally{context.Battle.ClearEnemyAttackTelegraph(previewOwner);}
        }
        private void ObserveCompletion(){if(task.Status!=FrameTaskStatus.Pending){if(task.Status!=FrameTaskStatus.Canceled)task.GetAwaiter().GetResult();cancellation?.Dispose();cancellation=null;}}
        public void Tick(int milliseconds,AIContext context,bool logicalFrame)=>ObserveCompletion();
        public void Cancel(AIContext context){var source=cancellation;cancellation=null;if(source==null)return;source.Cancel();source.Dispose();if(task.Status==FrameTaskStatus.Faulted)task.GetAwaiter().GetResult();}
        public int GetCooldownMs()=>cooldownMs;
        public float GetCooldown()=>cooldownMs/1000f;
    }

    public enum EnemyMovementKind { Stay, Random, Approach, Retreat, KeepDistance }

    /// <summary>通常の一マス移動。値/理想距離は定義元が指定し、乱数は実行時だけ消費する。</summary>
    public sealed class EnemyMovementAction : IEnemyAIAction
    {
        private readonly EnemyMovementKind kind;
        private readonly int cooldownMs;
        private readonly int minimumDistance,maximumDistance;
        private static readonly Vector2Int[] directions={new Vector2Int(-1,0),new Vector2Int(1,0),new Vector2Int(0,-1),new Vector2Int(0,1)};
        public EnemyMovementAction(EnemyMovementKind kind,int cooldownMs,int? minimumDistance=null,int? maximumDistance=null)
        {
            if(!Enum.IsDefined(typeof(EnemyMovementKind),kind))throw new ArgumentOutOfRangeException(nameof(kind));
            if(cooldownMs<0)throw new ArgumentOutOfRangeException(nameof(cooldownMs));
            if(kind==EnemyMovementKind.KeepDistance){
                if(!minimumDistance.HasValue||minimumDistance.Value<1||(maximumDistance??minimumDistance.Value)<minimumDistance.Value)throw new ArgumentException("正の理想距離/範囲が必要です");
                this.minimumDistance=minimumDistance.Value;this.maximumDistance=maximumDistance??minimumDistance.Value;
            }else if(minimumDistance.HasValue||maximumDistance.HasValue)throw new ArgumentException("理想距離は距離維持だけに指定してください");
            this.kind=kind;this.cooldownMs=cooldownMs;
        }
        private bool Ready(AIContext context)=>context.Self!=null&&!context.Self.IsDead&&!context.Self.IsStunned&&!context.Self.IsParalyzed&&!context.Self.IsInTemporaryMove&&(context.Battle==null||!context.Battle.IsGameOver);
        public bool CanExecute(AIContext context)
            =>Ready(context)&&(kind==EnemyMovementKind.Stay||Candidates(context).Count>0);
        private System.Collections.Generic.List<Vector2Int> Candidates(AIContext context)
        {
            var result=new System.Collections.Generic.List<Vector2Int>();
            // 通常の移動では参加者全員の論理位置が必要。未接続のcontextで味方を通過しない。
            if(context.Battle==null||context.Field==null||context.Player==null||kind==EnemyMovementKind.Random&&context.Random==null)return result;
            bool needsTarget=kind!=EnemyMovementKind.Random;
            if(needsTarget&&!context.HasVisiblePlayer)return result;
            var from=context.Self.GetLogicalPosition();var target=needsTarget?context.Player.GetLogicalPosition():from;
            int distance=Math.Abs(target.X-from.X)+Math.Abs(target.Y-from.Y);
            bool approach=kind==EnemyMovementKind.Approach||kind==EnemyMovementKind.KeepDistance&&distance>maximumDistance;
            bool retreat=kind==EnemyMovementKind.Retreat||kind==EnemyMovementKind.KeepDistance&&distance<minimumDistance;
            if(kind==EnemyMovementKind.Approach&&distance<=1||kind==EnemyMovementKind.KeepDistance&&!approach&&!retreat)return result;
            foreach(var direction in directions){
                var intended=new Vector2Int(from.X+direction.X,from.Y+direction.Y);
                int nextDistance=Math.Abs(target.X-intended.X)+Math.Abs(target.Y-intended.Y);
                if(needsTarget&&!(approach?nextDistance<distance:retreat&&nextDistance>distance))continue;
                var resolved=context.ResolveDirectionalInput(direction);var position=new Vector2Int(from.X+resolved.X,from.Y+resolved.Y);
                if(!context.Field.CanMoveTo(position,context.Self.TerrainState))continue;
                if(!context.Player.IsDead&&context.Player.GetLogicalPosition()==position)continue;
                bool occupied=false;foreach(var enemy in context.Battle.Enemies)if(!ReferenceEquals(enemy,context.Self)&&!enemy.IsDead&&enemy.GetLogicalPosition()==position){occupied=true;break;}
                if(!occupied)result.Add(position);
            }
            return result;
        }
        public void Execute(AIContext context)
        {
            if(!Ready(context)||kind==EnemyMovementKind.Stay)return;
            var candidates=Candidates(context);if(candidates.Count==0)return;
            int index=kind==EnemyMovementKind.Random?context.Random.Range(0,candidates.Count):0;
            context.Self.MoveTo(candidates[index]);
        }
        public int GetCooldownMs()=>cooldownMs;
        public float GetCooldown()=>cooldownMs/1000f;
    }

    /// <summary>仕様§5.1の設定例。使用カード/待ちを呼出し元から受け取り、製品の既定AIは変更しない。</summary>
    public static class EnemyActionPatternSamples
    {
        public static System.Collections.Generic.List<IEnemyAIAction> Slime(Card closeAttack,int cooldownMs,ICardDatabase database=null)
            => new System.Collections.Generic.List<IEnemyAIAction>{new EnemyMovementAction(EnemyMovementKind.Stay,0),new EnemyCardAction(closeAttack,cooldownMs,database)};

    }

}

namespace Olca.Core.Logic.Battle
{
    /// <summary>予告中の読取専用状態。経過はBattle論理時計から計算し、描画から進行しない。</summary>
    public readonly struct EnemyAttackPreview
    {
        public readonly Olca.Core.Domain.Vector2Int Target;
        public readonly int ElapsedFrames, TotalFrames;
        public EnemyAttackPreview(Olca.Core.Domain.Vector2Int target,int elapsed,int total)
        {Target=target;ElapsedFrames=elapsed;TotalFrames=total;}
    }
    public interface IEnemyAttackPreviewSource
    {bool TryGetAttackPreview(out EnemyAttackPreview preview);}

    /// <summary>即時Basicは互換保持。前隙/威力/待ちは登録元が明示する試用近接。</summary>
    public sealed class TelegraphedBasicAttackAction : IContinuingEnemyAIAction,IEnemyAttackPreviewSource
    {
        private readonly int startupFrames,damage,cooldownMs;
        private System.Threading.CancellationTokenSource cancellation;
        private FrameTask task;
        private AIContext active;
        private long started;
        private object previewOwner;
        private Olca.Core.Domain.Vector2Int target;
        public TelegraphedBasicAttackAction(int startupFrames,int damage,int cooldownMs)
        {
            if(startupFrames<1)throw new System.ArgumentOutOfRangeException(nameof(startupFrames));
            if(damage<0)throw new System.ArgumentOutOfRangeException(nameof(damage));
            if(cooldownMs<0)throw new System.ArgumentOutOfRangeException(nameof(cooldownMs));
            this.startupFrames=startupFrames;this.damage=damage;this.cooldownMs=cooldownMs;
        }
        public bool IsRunning {get{Observe();return cancellation!=null&&task.Status==FrameTaskStatus.Pending;}}
        public bool CanExecute(AIContext c)=>c.Battle!=null&&!c.Battle.IsGameOver&&!IsRunning&&c.Self!=null&&!c.Self.IsDead&&!c.Self.IsInTemporaryMove&&c.HasVisiblePlayer&&c.GetDistanceToPlayer()==1;
        public void Execute(AIContext c)
        {
            if(!CanExecute(c))return;
            active=c;started=c.Battle.ActionPresentationTimeUnits;
            var d=new Olca.Core.Domain.Vector2Int(c.Player.GridPosition.X-c.Self.GridPosition.X,c.Player.GridPosition.Y-c.Self.GridPosition.Y);
            var resolved=c.ResolveDirectionalInput(d); // 開始時に一度だけ決定。発動時に追尾/再反転しない。
            target=new Olca.Core.Domain.Vector2Int(c.Self.GridPosition.X+resolved.X,c.Self.GridPosition.Y+resolved.Y);
            cancellation=new System.Threading.CancellationTokenSource();
            var owner=new object();previewOwner=owner;
            c.Battle.SetEnemyAttackTelegraph(owner,new[]{target});
            task=Resolve(c,target,owner,cancellation.Token);Observe();
        }
        private async FrameTask Resolve(AIContext c,Olca.Core.Domain.Vector2Int lockedTarget,object owner,System.Threading.CancellationToken ct)
        {
            try{
                await c.Battle.WaitForActionFramesAsync(startupFrames,ct);ct.ThrowIfCancellationRequested();
                if(c.Battle.IsGameOver||c.Self.IsDead||c.Player.IsDead||!c.Player.GridPosition.Equals(lockedTarget)||c.GetDistanceToPlayer()!=1)return;
                if(c.Player.TryTakeDamage(damage)&&!c.Player.IsDead)c.Player.SetInvincible(c.Battle.DefaultHitInvincibilityMs);
            }finally{c.Battle.ClearEnemyAttackTelegraph(owner);if(ReferenceEquals(previewOwner,owner))previewOwner=null;}
        }
        private void Observe()
        {
            if(cancellation==null||task.Status==FrameTaskStatus.Pending)return;
            if(task.Status!=FrameTaskStatus.Canceled)task.GetAwaiter().GetResult();
            cancellation.Dispose();cancellation=null;
        }
        public bool TryGetAttackPreview(out EnemyAttackPreview preview)
        {
            preview=default;if(cancellation==null||cancellation.IsCancellationRequested||task.Status!=FrameTaskStatus.Pending)return false;
            preview=new EnemyAttackPreview(target,(int)System.Math.Min(startupFrames,System.Math.Max(0,(active.Battle.ActionPresentationTimeUnits-started)/50)),startupFrames);return true;
        }
        public void Tick(int milliseconds,AIContext context,bool logicalFrame)=>Observe();
        public void Cancel(AIContext context)
        {
            var source=cancellation;var owner=previewOwner;var battle=active.Battle;cancellation=null;previewOwner=null;if(source==null)return;
            try{source.Cancel();}finally{source.Dispose();battle?.ClearEnemyAttackTelegraph(owner);}
            if(task.Status==FrameTaskStatus.Faulted)task.GetAwaiter().GetResult();
        }
        public int GetCooldownMs()=>cooldownMs;
        public float GetCooldown()=>cooldownMs/1000f;
    }
}

namespace Olca.Core.Logic.Battle
{
    /// <summary>弓試作用の専用技。2〜3マス、開始時の1マスへ30f予告、追尾なし。Shotカードは不使用。</summary>
    public sealed class DungeonTrialArrowAction : IContinuingEnemyAIAction,IEnemyAttackPreviewSource
    {
        public const string EnemyId="trial-dungeon-archer-v1",TechniqueId="dungeon-trial-locked-arrow-v1";
        public static bool IsArcherEnemyId(string id)=>id==EnemyId||id==DungeonStepArcherAction.EnemyId;
        private readonly Action<Vector2Int> onArrow;
        public DungeonTrialArrowAction(Action<Vector2Int> onArrow=null){this.onArrow=onArrow;}
        private System.Threading.CancellationTokenSource cancellation;private FrameTask task;private AIContext active;private long started;private object owner;private Vector2Int target;
        public bool IsRunning {get{Observe();return cancellation!=null&&task.Status==FrameTaskStatus.Pending;}}
        public bool CanExecute(AIContext c)=>c.Battle!=null&&!c.Battle.IsGameOver&&!IsRunning&&c.Self!=null&&c.Player!=null&&!c.Self.IsDead&&!c.Self.IsStunned&&!c.Self.IsParalyzed&&!c.Self.IsInTemporaryMove&&c.HasVisiblePlayer&&c.GetDistanceToPlayer()>=2&&c.GetDistanceToPlayer()<=3;
        public void Execute(AIContext c){if(!CanExecute(c))return;active=c;started=c.Battle.ActionPresentationTimeUnits;var delta=new Vector2Int(c.Player.GridPosition.X-c.Self.GridPosition.X,c.Player.GridPosition.Y-c.Self.GridPosition.Y);var aim=c.ResolveDirectionalInput(delta);target=new Vector2Int(c.Self.GridPosition.X+aim.X,c.Self.GridPosition.Y+aim.Y);owner=new object();cancellation=new System.Threading.CancellationTokenSource();c.Battle.SetEnemyAttackTelegraph(owner,new[]{target});task=Resolve(c,target,owner,cancellation.Token);Observe();}
        private async FrameTask Resolve(AIContext c,Vector2Int locked,object ticket,System.Threading.CancellationToken token)
        {
            try{await c.Battle.WaitForActionFramesAsync(30,token);token.ThrowIfCancellationRequested();if(c.Battle.IsGameOver||c.Self.IsDead||c.Player.IsDead)return;onArrow?.Invoke(locked);if(!c.Player.GridPosition.Equals(locked))return;if(c.Player.TryTakeDamage(10)&&!c.Player.IsDead)c.Player.SetInvincible(c.Battle.DefaultHitInvincibilityMs);}
            finally{c.Battle.ClearEnemyAttackTelegraph(ticket);if(ReferenceEquals(owner,ticket))owner=null;}
        }
        private void Observe(){if(cancellation==null||task.Status==FrameTaskStatus.Pending)return;if(task.Status!=FrameTaskStatus.Canceled)task.GetAwaiter().GetResult();cancellation.Dispose();cancellation=null;}
        public bool TryGetAttackPreview(out EnemyAttackPreview preview){preview=default;if(cancellation==null||task.Status!=FrameTaskStatus.Pending)return false;preview=new EnemyAttackPreview(target,(int)Math.Min(30,Math.Max(0,(active.Battle.ActionPresentationTimeUnits-started)/50)),30);return true;}
        public void Tick(int ms,AIContext c,bool logicalFrame)=>Observe();
        public void Cancel(AIContext c){var source=cancellation;var ticket=owner;cancellation=null;owner=null;if(source==null)return;try{source.Cancel();}finally{source.Dispose();active.Battle?.ClearEnemyAttackTelegraph(ticket);}if(task.Status==FrameTaskStatus.Faulted)task.GetAwaiter().GetResult();}
        public int GetCooldownMs()=>1200;public float GetCooldown()=>1.2f;
    }
}

namespace Olca.Core.Logic.Battle
{
    /// <summary>新IDだけの試用。発射後72fは反撃時間、一発射につき一度だけ通常1マス後退する。</summary>
    /// <summary>行動中の待ち状態だけを読む表示契約。命中保証や防御低下を意味しない。</summary>
    public interface IEnemyRecoveryPreviewSource { bool TryGetRecoveryPreview(out int elapsedFrames,out int totalFrames); }
    public sealed class DungeonStepArcherAction : IContinuingEnemyAIAction,IEnemyAttackPreviewSource,IEnemyRecoveryPreviewSource
    {
        public const string EnemyId="trial-dungeon-archer-step-v2";
        // 独立した二体試用の終了対象。旧bossや旧弓の定義は一致しない。
        public const string PairTrialBossName="レンケイ ユミ（カリ）";
        // HP/属性はこの名義の固定値。強靭度は通常敵の調整値を引き継ぎ、現在値や有無では表示を変えない。
        public static bool IsPairTrialBoss(Enemy enemy)=>enemy!=null&&enemy.EnemyId=="boss"&&enemy.Name==PairTrialBossName&&!enemy.IsSummoned&&enemy.MaxHp==60&&enemy.Element==ElementType.None;
        public const int CounterWindowFrames=72,StepPresentationFrames=12;
        private readonly DungeonTrialArrowAction shot;
        private readonly EnemyMovementAction retreat=new EnemyMovementAction(EnemyMovementKind.Retreat,0);
        private System.Threading.CancellationTokenSource cancellation;
        private FrameTask task;
        private AIContext active;
        private long releasedAt=-1;
        private bool retreatSpent;
        public DungeonStepArcherAction(){shot=new DungeonTrialArrowAction(_=>{releasedAt=active.Battle.ActionPresentationTimeUnits;retreatSpent=false;});}
        public bool IsRunning {get{Observe();return cancellation!=null&&task.Status==FrameTaskStatus.Pending;}}
        private static bool Ready(AIContext c)=>c.Battle!=null&&!c.Battle.IsGameOver&&c.Self!=null&&c.Player!=null&&!c.Self.IsDead&&!c.Self.IsStunned&&!c.Self.IsParalyzed&&!c.Self.IsInTemporaryMove&&c.HasVisiblePlayer;
        public bool CanExecute(AIContext c)=>Ready(c)&&!IsRunning&&(shot.CanExecute(c)||!retreatSpent&&c.GetDistanceToPlayer()==1);
        public void Execute(AIContext c)
        {
            if(!CanExecute(c))return;active=c;releasedAt=-1;cancellation=new System.Threading.CancellationTokenSource();task=Run(c,cancellation.Token);Observe();
        }
        private async FrameTask Run(AIContext c,System.Threading.CancellationToken token)
        {
            try{
                long readyAt=c.Battle.ActionPresentationTimeUnits+CounterWindowFrames*50L;
                if(shot.CanExecute(c)){
                    shot.Execute(c);
                    while(shot.IsRunning)await c.Battle.WaitForActionFramesAsync(1,token);
                    token.ThrowIfCancellationRequested();if(releasedAt<0)return;
                    readyAt=releasedAt+CounterWindowFrames*50L;
                }
                long remaining=readyAt-c.Battle.ActionPresentationTimeUnits;
                if(remaining>0)await c.Battle.WaitForActionFramesAsync((int)((remaining+49)/50),token);
                token.ThrowIfCancellationRequested();
                if(!Ready(c)||c.GetDistanceToPlayer()!=1||retreatSpent)return;
                // 塞がっていても試行を消費。追われるたびに後退を再試行しない。
                retreatSpent=true;
                var from=c.Self.GetLogicalPosition();var target=c.Player.GetLogicalPosition();
                var direction=c.ResolveDirectionalInput(new Vector2Int(Math.Sign(from.X-target.X),0));
                var next=new Vector2Int(from.X+direction.X,from.Y);
                if(direction.X==0||!c.Field.IsInBounds(next)||c.Field.IsHole(next)||!c.Field.CanMoveTo(next,c.Self.TerrainState))return;
                // 参加者との衝突も既存の通常移動へ委ねる。一時移動/ワープは使用しない。
                if(retreat.CanExecute(c))retreat.Execute(c);
            }finally{shot.Cancel(c);}
        }
        private void Observe(){if(cancellation==null||task.Status==FrameTaskStatus.Pending)return;if(task.Status!=FrameTaskStatus.Canceled)task.GetAwaiter().GetResult();cancellation.Dispose();cancellation=null;}
        public void Tick(int ms,AIContext c,bool logicalFrame)=>Observe();
        public bool TryGetAttackPreview(out EnemyAttackPreview preview)=>shot.TryGetAttackPreview(out preview);
        public bool TryGetRecoveryPreview(out int elapsedFrames,out int totalFrames)
        {
            elapsedFrames=totalFrames=0;
            // Observe/時計更新/予約変更は行わず、発射済みの実行中状態だけを読む。
            if(cancellation==null||cancellation.IsCancellationRequested||task.Status!=FrameTaskStatus.Pending||releasedAt<0||!Ready(active)||active.Self.IsStaggered)return false;
            long units=active.Battle.ActionPresentationTimeUnits-releasedAt;
            if(units<0||units>=CounterWindowFrames*50L)return false;
            elapsedFrames=(int)(units/50);totalFrames=CounterWindowFrames;return true;
        }
        public void Cancel(AIContext c){var source=cancellation;cancellation=null;if(source==null)return;try{source.Cancel();}finally{source.Dispose();shot.Cancel(c);}if(task.Status==FrameTaskStatus.Faulted)task.GetAwaiter().GetResult();}
        public int GetCooldownMs()=>0;public float GetCooldown()=>0;
    }
}

namespace Olca.Core.Logic.Battle
{
    /// <summary>ダンジョン専用の飛行試用。固定予告マスが空けば着地し90f反撃時間。全数値は仮。</summary>
    public sealed class DungeonFlyingDive455 : IContinuingEnemyAIAction,IEnemyAttackPreviewSource,IEnemyRecoveryPreviewSource
    {
        public const string DefinitionId="trial-dungeon-flying-dive-v1",BossName="ヒコウ キュウコウカ（カリ）";
        public const int WarningFrames=42,RecoveryFrames=90;
        public static bool Matches(Enemy e)=>e!=null&&e.EnemyId=="boss"&&e.Name==BossName&&e.MaxHp==40&&e.Element==ElementType.None&&!e.IsSummoned;
        private System.Threading.CancellationTokenSource cancellation;private FrameTask task;private AIContext active;private Vector2Int target;private object owner;private long started,landed=-1;
        public bool IsRunning{get{Observe();return cancellation!=null&&task.Status==FrameTaskStatus.Pending;}}
        private static bool Ready(AIContext c)=>c.Battle!=null&&c.Field!=null&&!c.Battle.IsGameOver&&c.Self!=null&&c.Player!=null&&!c.Self.IsDead&&!c.Player.IsDead&&!c.Self.IsStunned&&!c.Self.IsParalyzed&&!c.Self.IsInTemporaryMove&&c.HasVisiblePlayer;
        public bool CanExecute(AIContext c)=>Ready(c)&&!IsRunning;
        public void Execute(AIContext c)
        {
            if(!CanExecute(c))return;active=c;target=c.Player.GridPosition;started=c.Battle.ActionPresentationTimeUnits;landed=-1;owner=new object();cancellation=new System.Threading.CancellationTokenSource();c.Battle.SetEnemyAttackTelegraph(owner,new[]{target});task=Run(c,target,owner,cancellation.Token);Observe();
        }
        private async FrameTask Run(AIContext c,Vector2Int locked,object ticket,System.Threading.CancellationToken token)
        {
            try{
                await c.Battle.WaitForActionFramesAsync(WarningFrames,token);token.ThrowIfCancellationRequested();if(!Ready(c)||c.Self.IsStaggered)return;
                // 予告したマスだけへ攻撃。プレイヤーを追尾せず、占有/穴/地形には重ならない。
                if(c.Player.GridPosition.Equals(locked)&&c.Player.TryTakeDamage(10)&&!c.Player.IsDead)c.Player.SetInvincible(c.Battle.DefaultHitInvincibilityMs);
                bool free=!c.Player.GetLogicalPosition().Equals(locked)&&!c.Player.GridPosition.Equals(locked)&&!c.Battle.Enemies.Any(e=>!ReferenceEquals(e,c.Self)&&!e.IsDead&&(e.GetLogicalPosition().Equals(locked)||e.GridPosition.Equals(locked)));
                if(free&&c.Field.IsInBounds(locked)&&!c.Field.IsHole(locked)&&c.Field.CanMoveTo(locked,c.Self.TerrainState))c.Self.MoveTo(locked);
                c.Battle.ClearEnemyAttackTelegraph(ticket);landed=c.Battle.ActionPresentationTimeUnits;
                await c.Battle.WaitForActionFramesAsync(RecoveryFrames,token);
            }finally{c.Battle.ClearEnemyAttackTelegraph(ticket);if(ReferenceEquals(owner,ticket))owner=null;}
        }
        private void Observe(){if(cancellation==null||task.Status==FrameTaskStatus.Pending)return;if(task.Status!=FrameTaskStatus.Canceled)task.GetAwaiter().GetResult();cancellation.Dispose();cancellation=null;}
        public void Tick(int ms,AIContext c,bool logicalFrame)=>Observe();
        public bool TryGetAttackPreview(out EnemyAttackPreview preview){preview=default;if(cancellation==null||task.Status!=FrameTaskStatus.Pending||landed>=0)return false;preview=new EnemyAttackPreview(target,(int)Math.Min(WarningFrames,Math.Max(0,(active.Battle.ActionPresentationTimeUnits-started)/50)),WarningFrames);return true;}
        public bool TryGetRecoveryPreview(out int elapsedFrames,out int totalFrames){elapsedFrames=totalFrames=0;if(cancellation==null||task.Status!=FrameTaskStatus.Pending||landed<0||!Ready(active)||active.Self.IsStaggered)return false;long age=(active.Battle.ActionPresentationTimeUnits-landed)/50;if(age<0||age>=RecoveryFrames)return false;elapsedFrames=(int)age;totalFrames=RecoveryFrames;return true;}
        public void Cancel(AIContext c){var source=cancellation;var ticket=owner;cancellation=null;owner=null;if(source==null)return;try{source.Cancel();}finally{source.Dispose();active.Battle?.ClearEnemyAttackTelegraph(ticket);}if(task.Status==FrameTaskStatus.Faulted)task.GetAwaiter().GetResult();}
        public int GetCooldownMs()=>500;public float GetCooldown()=>.5f;
    }
}

namespace Olca.Core.Logic.Battle
{
    /// <summary>新IDの混成チェック専用。既存前衛/弓の定義を上書きしない。</summary>
    public static class DungeonMixedPriority462
    {
        public const string FrontId="trial-dungeon-stance-front-v1",FrontName="カマエ スライム（カリ）",RearName="ヨミアイ ユミ（カリ）";
        public const int FrontHp=50,RearHp=40,WindupFrames=24,StrikeDamage=30,RestMs=2000;
        public static bool IsFront(Olca.Core.Domain.Enemy e)=>e!=null&&e.EnemyId==FrontId&&e.Name==FrontName&&e.MaxHp==FrontHp&&e.Element==Olca.Core.Domain.ElementType.None&&!e.IsSummoned;
        public static bool IsRear(Olca.Core.Domain.Enemy e)=>e!=null&&e.EnemyId=="boss"&&e.Name==RearName&&e.MaxHp==RearHp&&e.Element==Olca.Core.Domain.ElementType.None&&!e.IsSummoned;
        public static EnemyAIRegistration FrontPattern()=>new EnemyAIRegistration(new[]{new EnemyActionPattern(0,new IEnemyAIAction[]{new TelegraphedBasicAttackAction(WindupFrames,StrikeDamage,RestMs)},EnemyPatternEndMode.Loop)},0);
    }
}

namespace Olca.Core.Logic.Battle
{
    /// <summary>一射後に再照準する専用二連砲台。各予告は固定、二射後は90f排熱。数値は試用。</summary>
    public sealed class DungeonReaimCannon473 : IContinuingEnemyAIAction, IEnemyAttackPreviewSource, IEnemyRecoveryPreviewSource
    {
        public const string DefinitionId="trial-dungeon-reaim-cannon-v1", BossName="ニレン サイショウジュン（カリ）";
        public const int WarningFrames=30, BetweenShotsFrames=12, RecoveryFrames=90, Damage=10, MaximumHp=60;
        public static bool Matches(Enemy e)=>e!=null&&e.EnemyId=="boss"&&e.Name==BossName&&e.MaxHp==MaximumHp&&e.Element==ElementType.None&&!e.IsSummoned;
        private CancellationTokenSource cancellation;
        private FrameTask task;
        private AIContext active;
        private long started, released=-1;
        private int recoveryLength;
        private bool warning;
        private object owner;
        private Vector2Int target;
        private static bool Ready(AIContext c)=>c.Battle!=null&&c.Field!=null&&!c.Battle.IsGameOver&&c.Self!=null&&c.Player!=null&&!c.Self.IsDead&&!c.Player.IsDead&&!c.Self.IsStunned&&!c.Self.IsParalyzed&&!c.Self.IsStaggered&&!c.Self.IsInTemporaryMove&&c.HasVisiblePlayer;
        public bool IsRunning {get{Observe();return cancellation!=null&&task.Status==FrameTaskStatus.Pending;}}
        public bool CanExecute(AIContext c)=>Ready(c)&&!IsRunning&&c.Self.GridPosition.Y==c.Player.GridPosition.Y&&c.GetDistanceToPlayer()>=2&&c.GetDistanceToPlayer()<=4;
        public void Execute(AIContext c)
        {
            if(!CanExecute(c))return;active=c;released=-1;recoveryLength=0;owner=new object();cancellation=new CancellationTokenSource();
            task=Run(c,owner,cancellation.Token);Observe();
        }
        private async FrameTask Run(AIContext c,object ticket,CancellationToken token)
        {
            try{
                for(int shot=0;shot<2;shot++){
                    token.ThrowIfCancellationRequested();if(!Ready(c))return;
                    // 二射目の開始時にだけ狙い直す。予告中は追尾しない。
                    var delta=new Vector2Int(c.Player.GridPosition.X-c.Self.GridPosition.X,c.Player.GridPosition.Y-c.Self.GridPosition.Y);
                    var aim=c.ResolveDirectionalInput(delta);target=new Vector2Int(c.Self.GridPosition.X+aim.X,c.Self.GridPosition.Y+aim.Y);
                    if(!c.Field.IsInBounds(target))return;
                    var locked=target;started=c.Battle.ActionPresentationTimeUnits;released=-1;warning=true;
                    c.Battle.SetEnemyAttackTelegraph(ticket,new[]{locked});
                    await c.Battle.WaitForActionFramesAsync(WarningFrames,token);token.ThrowIfCancellationRequested();if(!Ready(c))return;
                    warning=false;c.Battle.ClearEnemyAttackTelegraph(ticket);
                    if(c.Player.GridPosition.Equals(locked)&&c.Player.TryTakeDamage(Damage)&&!c.Player.IsDead)c.Player.SetInvincible(c.Battle.DefaultHitInvincibilityMs);
                    released=c.Battle.ActionPresentationTimeUnits;recoveryLength=shot==0?BetweenShotsFrames:RecoveryFrames;
                    await c.Battle.WaitForActionFramesAsync(recoveryLength,token);
                }
            }finally{warning=false;c.Battle.ClearEnemyAttackTelegraph(ticket);if(ReferenceEquals(owner,ticket))owner=null;}
        }
        private void Observe(){if(cancellation==null||task.Status==FrameTaskStatus.Pending)return;if(task.Status!=FrameTaskStatus.Canceled)task.GetAwaiter().GetResult();cancellation.Dispose();cancellation=null;}
        public void Tick(int milliseconds,AIContext context,bool logicalFrame)=>Observe();
        public bool TryGetAttackPreview(out EnemyAttackPreview preview)
        {
            preview=default;if(!warning||cancellation==null||cancellation.IsCancellationRequested||task.Status!=FrameTaskStatus.Pending)return false;
            preview=new EnemyAttackPreview(target,(int)Math.Min(WarningFrames,Math.Max(0,(active.Battle.ActionPresentationTimeUnits-started)/50)),WarningFrames);return true;
        }
        public bool TryGetRecoveryPreview(out int elapsedFrames,out int totalFrames)
        {
            elapsedFrames=totalFrames=0;if(warning||cancellation==null||cancellation.IsCancellationRequested||task.Status!=FrameTaskStatus.Pending||released<0||!Ready(active))return false;
            long age=(active.Battle.ActionPresentationTimeUnits-released)/50;if(age<0||age>=recoveryLength)return false;
            elapsedFrames=(int)age;totalFrames=recoveryLength;return true;
        }
        public void Cancel(AIContext c)
        {
            var source=cancellation;var ticket=owner;cancellation=null;owner=null;released=-1;warning=false;if(source==null)return;
            try{source.Cancel();}finally{source.Dispose();active.Battle?.ClearEnemyAttackTelegraph(ticket);}if(task.Status==FrameTaskStatus.Faulted)task.GetAwaiter().GetResult();
        }
        public int GetCooldownMs()=>500;
        public float GetCooldown()=>.5f;
    }
}

namespace Olca.Core.Logic.Battle
{
    /// <summary>任意試用だけの盾持ち。ガードの所有・固定予告・解除を一行動で閉じる。期間は調整可能な仮値。</summary>
    public sealed class DungeonGuard488 : IContinuingEnemyAIAction,IEnemyAttackPreviewSource,IEnemyRecoveryPreviewSource
    {
        public const string DefinitionId="trial-dungeon-guard-opening-v1",BossName="タテ ゴーレム（カリ）";
        public const int GuardFrames=90,WarningFrames=30,RecoveryFrames=60,MaximumHp=60,Damage=10;
        public static bool Matches(Enemy e)=>e!=null&&e.EnemyId=="boss"&&e.Name==BossName&&e.MaxHp==MaximumHp&&e.Element==ElementType.None&&!e.IsSummoned;
        private CancellationTokenSource cancellation;private FrameTask task;private AIContext active;private object owner;private bool warning;private long started,released=-1;private Vector2Int target;
        private static bool Ready(AIContext c)=>c.Battle!=null&&c.Field!=null&&!c.Battle.IsGameOver&&c.Self!=null&&c.Player!=null&&!c.Self.IsDead&&!c.Player.IsDead&&!c.Self.IsStunned&&!c.Self.IsParalyzed&&!c.Self.IsStaggered&&!c.Self.IsInTemporaryMove&&c.HasVisiblePlayer;
        public bool IsRunning {get{Observe();return cancellation!=null&&task.Status==FrameTaskStatus.Pending;}}
        public bool CanExecute(AIContext c)=>Ready(c)&&!IsRunning;
        public void Execute(AIContext c){if(!CanExecute(c))return;active=c;owner=new object();released=-1;warning=false;cancellation=new CancellationTokenSource();task=Run(c,owner,cancellation.Token);Observe();}
        private async FrameTask Run(AIContext c,object ticket,CancellationToken token)
        {
            try{
                c.Self.Guard.Set(true,true);await c.Battle.WaitForActionFramesAsync(GuardFrames,token);token.ThrowIfCancellationRequested();c.Self.Guard.Clear();if(!Ready(c))return;
                var aim=c.ResolveDirectionalInput(new Vector2Int(Math.Sign(c.Player.GridPosition.X-c.Self.GridPosition.X),0));target=new Vector2Int(c.Self.GridPosition.X+aim.X,c.Self.GridPosition.Y);
                if(!c.Field.IsInBounds(target))return;
                warning=true;started=c.Battle.ActionPresentationTimeUnits;c.Battle.SetEnemyAttackTelegraph(ticket,new[]{target});
                await c.Battle.WaitForActionFramesAsync(WarningFrames,token);token.ThrowIfCancellationRequested();if(!Ready(c))return;
                warning=false;c.Battle.ClearEnemyAttackTelegraph(ticket);
                if(c.Player.GridPosition.Equals(target)&&c.Player.TryTakeDamage(Damage)&&!c.Player.IsDead)c.Player.SetInvincible(c.Battle.DefaultHitInvincibilityMs);
                released=c.Battle.ActionPresentationTimeUnits;await c.Battle.WaitForActionFramesAsync(RecoveryFrames,token);
            }finally{c.Self.Guard.Clear();warning=false;c.Battle.ClearEnemyAttackTelegraph(ticket);if(ReferenceEquals(owner,ticket))owner=null;}
        }
        private void Observe(){if(cancellation==null||task.Status==FrameTaskStatus.Pending)return;if(task.Status!=FrameTaskStatus.Canceled)task.GetAwaiter().GetResult();cancellation.Dispose();cancellation=null;}
        public void Tick(int milliseconds,AIContext context,bool logicalFrame)=>Observe();
        public bool TryGetAttackPreview(out EnemyAttackPreview preview){preview=default;if(!warning||cancellation==null||cancellation.IsCancellationRequested||task.Status!=FrameTaskStatus.Pending)return false;preview=new EnemyAttackPreview(target,(int)Math.Min(WarningFrames,Math.Max(0,(active.Battle.ActionPresentationTimeUnits-started)/50)),WarningFrames);return true;}
        public bool TryGetRecoveryPreview(out int elapsedFrames,out int totalFrames){elapsedFrames=totalFrames=0;if(released<0||cancellation==null||cancellation.IsCancellationRequested||task.Status!=FrameTaskStatus.Pending||!Ready(active))return false;long age=(active.Battle.ActionPresentationTimeUnits-released)/50;if(age<0||age>=RecoveryFrames)return false;elapsedFrames=(int)age;totalFrames=RecoveryFrames;return true;}
        public void Cancel(AIContext c){var source=cancellation;var ticket=owner;cancellation=null;owner=null;released=-1;warning=false;active.Self?.Guard.Clear();if(source==null)return;try{source.Cancel();}finally{source.Dispose();active.Battle?.ClearEnemyAttackTelegraph(ticket);}if(task.Status==FrameTaskStatus.Faulted)task.GetAwaiter().GetResult();}
        public int GetCooldownMs()=>0;public float GetCooldown()=>0;
        public static EnemyAIRegistration Pattern()=>new EnemyAIRegistration(new[]{new EnemyActionPattern(0,new IEnemyAIAction[]{new EnemyMovementAction(EnemyMovementKind.Approach,0),new DungeonGuard488()},EnemyPatternEndMode.Loop)},0);
    }
}
