using System;
using System.Linq;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using System.Threading;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.CardEffects
{
    /// <summary>
    /// カード効果実行時のコンテキスト
    /// </summary>
    public class BattleContext
    {
        /// <summary>バトルシステム</summary>
        public BattleSystem Battle { get; }

        /// <summary>プレイヤーキャラクター</summary>
        public PlayerCharacter Player => Battle.Player;

        /// <summary>敵リスト</summary>
        public IReadOnlyList<Enemy> Enemies => Battle.Enemies;

        /// <summary>バトルフィールド</summary>
        public BattleField Field => Battle.Field;

        /// <summary>使用したカード</summary>
        public Domain.Card UsedCard { get; }

        /// <summary>敵使用時だけ指定する。Player/Enemiesの意味自体は変更しない。</summary>
        public Enemy EnemySource { get; }
        public int ForwardX { get; }
        internal CancellationToken ActionCancellation { get; set; }
        // 前隙のある敵単体攻撃は狙ったマスを保持し、相手の移動へ追尾しない。
        internal Vector2Int? LockedSingleTarget { get; set; }
        internal CardTarget User => EnemySource == null ? new CardTarget(Player) : new CardTarget(EnemySource);
        internal List<CardTarget> GetAliveOpponents()
        {
            if (EnemySource != null) return Player.IsDead ? new List<CardTarget>() : new List<CardTarget>{new CardTarget(Player)};
            var result = new List<CardTarget>();
            foreach(var enemy in Enemies) if(!enemy.IsDead) result.Add(new CardTarget(enemy));
            return result;
        }
        internal CardTarget? GetNearestForwardOpponent()
        {
            var origin=GetEffectivePlayerPosition(); CardTarget? nearest=null;
            foreach(var target in GetAliveOpponents()) {
                if(LockedSingleTarget.HasValue&&target.GridPosition!=LockedSingleTarget.Value)continue;
                int distance=(target.GridPosition.X-origin.X)*ForwardX;
                if(target.GridPosition.Y!=origin.Y||distance<=0)continue;
                if(!nearest.HasValue||distance<(nearest.Value.GridPosition.X-origin.X)*ForwardX)nearest=target;
            }
            return nearest;
        }
        internal BattleContext ForCard(Domain.Card card)
        {
            var child=new BattleContext(Battle,card,EnemySource,ForwardX){CardDatabase=CardDatabase,TemporaryPlayerPosition=TemporaryPlayerPosition,ActionCancellation=ActionCancellation};
            if(EnemySource!=null)EnemyCardAction.ValidateCard(card);
            child.InheritDerivatives(this);return child;
        }

        internal AttackInfluence Influence { get; private set; }
        // 可変な共有スコープを使わず、遅延/連鎖の各判定へ個別の個体を渡す。
        internal BattleContext WithInfluence(AttackInfluence influence)
        {
            var child = new BattleContext(Battle, UsedCard, EnemySource, ForwardX) {
                CardDatabase = CardDatabase, TemporaryPlayerPosition = TemporaryPlayerPosition, Influence = influence, ActionCancellation=ActionCancellation, LockedSingleTarget=LockedSingleTarget
            };
            child.InheritDerivatives(this);
            return child;
        }

        /// <summary>
        /// カードデータベース（チェイン効果でカードを取得する際に使用）
        /// Application層から注入される
        /// </summary>
        public ICardDatabase CardDatabase { get; set; }

        // 同じ使用から同期的に派生した前後隙だけを追跡する。飛翔・設置は独立動作。
        private DerivativeLifetime derivativeLifetime = new DerivativeLifetime();
        internal FrameTask WaitForDerivativesAsync(CancellationToken ct) => derivativeLifetime.WaitAsync(ct);
        internal void InheritDerivatives(BattleContext parent) => derivativeLifetime = parent.derivativeLifetime;
        internal void RegisterActionDelay(DelayedEffect effect)
        {
            effect.OnFinished = derivativeLifetime.Add();
            if(ActionCancellation.IsCancellationRequested){effect.Finish();return;}
            Battle.DelayedEffects.RegisterDelayedEffect(effect);
            if(ActionCancellation.CanBeCanceled){
                var registration=ActionCancellation.Register(()=>Battle.DelayedEffects.Cancel(effect));
                var finish=effect.OnFinished;effect.OnFinished=()=>{registration.Dispose();finish?.Invoke();};
            }
        }

        private sealed class DerivativeLifetime
        {
            private int pending;
            private FrameCompletionSource completion;
            public Action Add()
            {
                if (pending++ == 0) completion = new FrameCompletionSource();
                bool finished = false;
                return () => {
                    if (finished) return;
                    finished = true;
                    if (--pending == 0) completion.TrySetResult();
                };
            }
            public FrameTask WaitAsync(CancellationToken ct)
                => pending == 0 ? FrameTask.CompletedTask : completion.Task.AttachExternalCancellation(ct);
        }

        /// <summary>
        /// チェイン効果用の一時的なプレイヤー位置オーバーライド
        /// null の場合は通常のプレイヤー位置を使用
        /// </summary>
        public Vector2Int? TemporaryPlayerPosition { get; set; }

        /// <summary>
        /// 有効なプレイヤー位置を取得（オーバーライドがあればそちらを優先）
        /// </summary>
        public Vector2Int GetEffectivePlayerPosition()
        {
            return TemporaryPlayerPosition ?? User.GridPosition;
        }

        /// <summary>発動位置より前方の最寄り1体。同じマスでは参加順を維持する。</summary>
        public Enemy GetNearestForwardEnemy()
        {
            var origin = GetEffectivePlayerPosition();
            Enemy nearest = null;
            foreach (var enemy in Enemies)
            {
                if (enemy.IsDead || enemy.GridPosition.Y != origin.Y || enemy.GridPosition.X <= origin.X) continue;
                if (nearest == null || enemy.GridPosition.X < nearest.GridPosition.X) nearest = enemy;
            }
            return nearest;
        }

        public BattleContext(BattleSystem battle, Domain.Card usedCard, Enemy enemySource = null, int forwardX = 1)
        {
            if(forwardX!=1&&forwardX!=-1)throw new ArgumentOutOfRangeException(nameof(forwardX));
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            if(enemySource!=null&&!battle.Enemies.Contains(enemySource))throw new ArgumentException("未登録の使用者です",nameof(enemySource));
            UsedCard = usedCard; EnemySource=enemySource; ForwardX=forwardX;
        }

        /// <summary>
        /// カードIDからカードを取得
        /// チェイン効果で使用
        /// </summary>
        public Domain.Card GetCardById(string cardId)
        {
            if (CardDatabase == null)
            {
                throw new InvalidOperationException(
                    "CardDatabase が設定されていません。Application層でBattleContext.CardDatabaseを設定してください。");
            }

            return CardDatabase.GetCardById(cardId);
        }
    }

    /// <summary>共通効果用の対象。敵とプレイヤーの既存防御・耐性・フレーム集計をそのまま呼ぶ。</summary>
    internal readonly struct CardTarget
    {
        private readonly Enemy enemy;
        private readonly PlayerCharacter player;
        internal CardTarget(Enemy value){enemy=value;player=null;}
        internal CardTarget(PlayerCharacter value){player=value;enemy=null;}
        internal bool IsDead => enemy!=null?enemy.IsDead:player.IsDead;
        internal string Name => enemy!=null?enemy.Name:"プレイヤー";
        internal Vector2Int GridPosition => enemy!=null?enemy.GridPosition:player.GridPosition;
        internal long TotalHitsTaken => enemy!=null?enemy.TotalHitsTaken:player.TotalHitsTaken;
        internal long TotalDamageTaken => enemy!=null?enemy.TotalDamageTaken:player.TotalDamageTaken;
        internal void Heal(int amount){if(enemy!=null)enemy.Heal(amount);else player.Heal(amount);}
        internal void SetBarrier(int hp){if(enemy!=null)enemy.SetBarrier(hp);else player.SetBarrier(hp);}
        internal void TakeDamage(int damage,ElementType element,AttackProperty properties,int? poiseDamage,int invincibilityMs,int hitStopFrames=0,AttackInfluence influence=null)
        {
            if(enemy!=null)enemy.TakeDamage(damage,element,properties,poiseDamage,invincibilityMs,hitStopFrames,influence);
            else if(!player.IsDead&&player.TryTakeDamage(damage,attackElement:element,attackProperties:properties,poiseDamage:poiseDamage,hitStopFrames:hitStopFrames)&&invincibilityMs>0&&!player.IsDead)player.SetInvincible(invincibilityMs);
        }
        internal void ApplyStatus(StatusEffect effect)
        {
            if(enemy!=null){enemy.ApplyStatus(effect);return;}
            if(player.IsDead)return;
            switch(effect.Type){
                case StatusEffectType.Paralyze:player.ApplyParalysis(effect.RemainingFrames);break;
                case StatusEffectType.Confusion:player.ApplyConfusion(effect.RemainingFrames);break;
                case StatusEffectType.Blind:player.ApplyBlindness(effect.RemainingFrames);break;
                case StatusEffectType.Poison:player.ApplyPoison(effect.PoisonKind,effect,effect.PoisonIntervalFrames??60);break;
            }
        }
    }

    /// <summary>
    /// カードデータベースインターフェース
    /// チェイン効果でカードを取得するために使用
    /// Application層で実装
    /// </summary>
    public interface ICardDatabase
    {
        /// <summary>
        /// カードIDからカードを取得
        /// </summary>
        Domain.Card GetCardById(string cardId);
    }
}
