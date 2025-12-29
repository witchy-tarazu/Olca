using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// 調律効果に対応した汎用ダメージ効果
    /// Modifierの追加効果（スプレッド、貫通、多段ヒット等）を自動的に適用する
    /// </summary>
    public class ModifiableDamageEffect : ICardEffect
    {
        private readonly DamageTargetType baseTargetType;

        public ModifiableDamageEffect(DamageTargetType targetType = DamageTargetType.Single)
        {
            this.baseTargetType = targetType;
        }

        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var card = context.UsedCard;
            var modifier = card.Modifier;

            // 最終的な威力を計算
            int finalDamage = card.GetFinalPower();
            ElementType finalElement = card.GetFinalElement();

            // 基本的な対象を取得
            var targets = GetBaseTargets(context, baseTargetType);

            // 追加効果を適用
            if (modifier.AdditionalEffects.Count > 0)
            {
                foreach (var effect in modifier.AdditionalEffects)
                {
                    ApplyAdditionalEffect(context, effect, ref targets, finalDamage, finalElement);
                }
            }
            else
            {
                // 追加効果なしの場合は通常のダメージ
                DealDamageToTargets(targets, finalDamage, finalElement);
            }
        }

        /// <summary>
        /// 基本的な対象リストを取得
        /// </summary>
        private List<Enemy> GetBaseTargets(BattleContext context, DamageTargetType targetType)
        {
            var aliveEnemies = context.Battle.GetAliveEnemies();
            var targets = new List<Enemy>();

            if (aliveEnemies.Count == 0) return targets;

            switch (targetType)
            {
                case DamageTargetType.Single:
                    targets.Add(aliveEnemies[0]);
                    break;

                case DamageTargetType.All:
                    targets.AddRange(aliveEnemies);
                    break;

                case DamageTargetType.FrontTile:
                    targets.AddRange(GetEnemiesInTileRange(context, 1));
                    break;

                case DamageTargetType.FrontTwoTiles:
                    targets.AddRange(GetEnemiesInTileRange(context, 2));
                    break;

                case DamageTargetType.FrontThreeTiles:
                    targets.AddRange(GetEnemiesInTileRange(context, 3));
                    break;
            }

            return targets;
        }

        /// <summary>
        /// 前方Nマスにいる敵を取得
        /// </summary>
        private List<Enemy> GetEnemiesInTileRange(BattleContext context, int range)
        {
            var result = new List<Enemy>();
            var playerPos = context.Player.GridPosition;
            var aliveEnemies = context.Battle.GetAliveEnemies();

            for (int i = 1; i <= range; i++)
            {
                Vector2Int targetPos = new Vector2Int(playerPos.X + i, playerPos.Y);
                foreach (var enemy in aliveEnemies)
                {
                    if (enemy.GridPosition.Equals(targetPos))
                    {
                        result.Add(enemy);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 追加効果を適用
        /// </summary>
        private void ApplyAdditionalEffect(
            BattleContext context,
            AdditionalEffect effect,
            ref List<Enemy> targets,
            int damage,
            ElementType element)
        {
            switch (effect.EffectType)
            {
                case AdditionalEffectType.Spread:
                    // スプレッド効果: 着弾地点から前後1マスに拡散
                    ApplySpreadEffect(context, targets, damage, element);
                    break;

                case AdditionalEffectType.Penetration:
                    // 貫通効果: プレイヤーより前方の敵全てを対象に
                    targets = GetPenetratingTargets(context);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.MultiHit:
                    // 多段ヒット効果
                    int hitCount = effect.HitCount > 0 ? effect.HitCount : 3;
                    DealMultiHitDamage(targets, damage, element, hitCount);
                    break;

                case AdditionalEffectType.PanelIce:
                    // パネル変化: Ice
                    ApplyPanelChange(context, targets, PanelType.Ice);
                    DealDamageToTargets(targets, damage, element);
                    break;

                case AdditionalEffectType.PanelMagma:
                    // パネル変化: Magma
                    ApplyPanelChange(context, targets, PanelType.Magma);
                    DealDamageToTargets(targets, damage, element);
                    break;

                default:
                    DealDamageToTargets(targets, damage, element);
                    break;
            }
        }

        /// <summary>
        /// スプレッド効果を適用
        /// </summary>
        private void ApplySpreadEffect(BattleContext context, List<Enemy> baseTargets, int damage, ElementType element)
        {
            if (baseTargets.Count == 0) return;

            var affectedEnemies = new HashSet<Enemy>();
            var aliveEnemies = context.Battle.GetAliveEnemies();

            // 各ターゲットの着弾地点から前後1マスに拡散
            foreach (var target in baseTargets)
            {
                Vector2Int center = target.GridPosition;
                var spreadPositions = new[]
                {
                    center,
                    new Vector2Int(center.X + 1, center.Y),
                    new Vector2Int(center.X - 1, center.Y)
                };

                foreach (var pos in spreadPositions)
                {
                    foreach (var enemy in aliveEnemies)
                    {
                        if (enemy.GridPosition.Equals(pos))
                        {
                            affectedEnemies.Add(enemy);
                        }
                    }
                }
            }

            DealDamageToTargets(new List<Enemy>(affectedEnemies), damage, element);
        }

        /// <summary>
        /// 貫通する敵リストを取得
        /// </summary>
        private List<Enemy> GetPenetratingTargets(BattleContext context)
        {
            var result = new List<Enemy>();
            var playerPos = context.Player.GridPosition;
            var aliveEnemies = context.Battle.GetAliveEnemies();

            foreach (var enemy in aliveEnemies)
            {
                if (enemy.GridPosition.X > playerPos.X && enemy.GridPosition.Y == playerPos.Y)
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        /// <summary>
        /// 対象リストにダメージを与える
        /// </summary>
        private void DealDamageToTargets(List<Enemy> targets, int damage, ElementType element)
        {
            foreach (var target in targets)
            {
                if (!target.IsDead)
                {
                    target.TakeDamage(damage, element);
                }
            }
        }

        /// <summary>
        /// 多段ヒットダメージを与える
        /// </summary>
        private void DealMultiHitDamage(List<Enemy> targets, int damagePerHit, ElementType element, int hitCount)
        {
            foreach (var target in targets)
            {
                for (int i = 0; i < hitCount; i++)
                {
                    if (!target.IsDead)
                    {
                        target.TakeDamage(damagePerHit, element);
                    }
                }
            }
        }

        /// <summary>
        /// パネル変化を適用
        /// </summary>
        private void ApplyPanelChange(BattleContext context, List<Enemy> targets, PanelType panelType)
        {
            foreach (var target in targets)
            {
                context.Field.ChangePanelType(target.GridPosition, panelType);
            }
        }
    }
}
