using System;
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

        internal AttackInfluence Influence { get; private set; }
        // 可変な共有スコープを使わず、遅延/連鎖の各判定へ個別の個体を渡す。
        internal BattleContext WithInfluence(AttackInfluence influence)
        {
            var child = new BattleContext(Battle, UsedCard) {
                CardDatabase = CardDatabase, TemporaryPlayerPosition = TemporaryPlayerPosition, Influence = influence
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
            Battle.DelayedEffects.RegisterDelayedEffect(effect);
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
            return TemporaryPlayerPosition ?? Player.GridPosition;
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

        public BattleContext(BattleSystem battle, Domain.Card usedCard)
        {
            Battle = battle;
            UsedCard = usedCard;
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
