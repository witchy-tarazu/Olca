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
