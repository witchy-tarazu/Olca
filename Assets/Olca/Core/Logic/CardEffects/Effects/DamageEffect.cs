using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// ダメージを与えるカード効果
    /// </summary>
    public class DamageEffect : ICardEffect
    {
        private readonly int baseDamage;
        private readonly int hitStopFrames;
        private readonly DamageTargetType targetType;
        private readonly int targetCount;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseDamage">基本ダメージ</param>
        /// <param name="targetType">対象タイプ</param>
        /// <param name="targetCount">対象数（複数攻撃の場合）</param>
        public DamageEffect(int baseDamage, DamageTargetType targetType = DamageTargetType.Single, int targetCount = 1, int hitStopFrames = 0)
        {
            if(hitStopFrames<0)throw new ArgumentOutOfRangeException(nameof(hitStopFrames));
            this.hitStopFrames=hitStopFrames;
            this.baseDamage = baseDamage;
            this.targetType = targetType;
            this.targetCount = targetCount;
        }

        /// <summary>
        /// 効果を実行
        /// </summary>
        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            using (var influence = new AttackInfluence()) ExecuteHitbox(context.WithInfluence(influence));
        }

        private void ExecuteHitbox(BattleContext context)
        {
            var aliveEnemies = context.GetAliveOpponents();
            if (aliveEnemies.Count == 0) return;

            switch (targetType)
            {
                case DamageTargetType.Single:
                    // 単体射撃は実際の発動位置より前方の最寄り1体。
                    var nearest = context.GetNearestForwardOpponent();
                    if (nearest.HasValue) DealDamageToEnemy(nearest.Value, context);
                    break;

                case DamageTargetType.All:
                    // 全体攻撃: すべての敵
                    foreach (var enemy in aliveEnemies)
                    {
                        DealDamageToEnemy(enemy, context);
                    }
                    break;

                case DamageTargetType.Multiple:
                    // 複数攻撃: 指定数の敵
                    int count = Math.Min(targetCount, aliveEnemies.Count);
                    for (int i = 0; i < count; i++)
                    {
                        DealDamageToEnemy(aliveEnemies[i], context);
                    }
                    break;

                case DamageTargetType.FrontTile:
                    // 前方1マス攻撃（マス対象）
                    DealDamageToTiles(context, 1);
                    break;

                case DamageTargetType.FrontTwoTiles:
                    // 前方2マス攻撃（マス対象）
                    DealDamageToTiles(context, 2);
                    break;

                case DamageTargetType.FrontThreeTiles:
                    // 前方3マス攻撃（マス対象）
                    DealDamageToTiles(context, 3);
                    break;
            }
        }

        /// <summary>
        /// 敵にダメージを与える
        /// 属性耐性と地形の補正はEnemy.TakeDamage内で計算される
        /// </summary>
        private void DealDamageToEnemy(CardTarget enemy, BattleContext context)
        {
            // baseDamageには既にカードのPowerが含まれている（CardEffectFactoryで設定済み）
            int finalDamage = baseDamage;

            // GetFinalElement()でElementOverrideを考慮した最終属性を使用
            enemy.TakeDamage(finalDamage, context.UsedCard.GetFinalElement(), context.UsedCard.Properties, context.UsedCard.PoiseDamage, context.UsedCard.HitInvincibilityMs, hitStopFrames, context.Influence);
        }

        /// <summary>
        /// マス対象の攻撃（前方N マスの敵全てにダメージ）
        /// </summary>
        /// <param name="context">バトルコンテキスト</param>
        /// <param name="tileRange">攻撃範囲（マス数）</param>
        private void DealDamageToTiles(BattleContext context, int tileRange)
        {
            // TODO: プレイヤーの位置情報が実装されたら、正確な座標計算に変更
            // 現在は簡易実装: 敵陣の最前列からN マス分を攻撃範囲とする

            var aliveEnemies = context.GetAliveOpponents();
            if (aliveEnemies.Count == 0) return;

            // 最前列の敵のX座標を基準にする
            if(context.EnemySource!=null){
                var origin=context.GetEffectivePlayerPosition();
                foreach(var target in aliveEnemies){int distance=(target.GridPosition.X-origin.X)*context.ForwardX;if(target.GridPosition.Y==origin.Y&&distance>0&&distance<=tileRange)DealDamageToEnemy(target,context);}
                return;
            }
            int frontX = aliveEnemies[0].GridPosition.X;
            int maxX = frontX + tileRange - 1;

            // 攻撃範囲内のマスにいる全ての敵にダメージ
            foreach (var enemy in aliveEnemies)
            {
                if (enemy.GridPosition.X >= frontX && enemy.GridPosition.X <= maxX)
                {
                    DealDamageToEnemy(enemy, context);
                }
            }
        }
    }

    /// <summary>
    /// ダメージ対象タイプ
    /// </summary>
    public enum DamageTargetType
    {
        /// <summary>単体（最前列の敵1体）- 敵対象</summary>
        Single,

        /// <summary>全体</summary>
        All,

        /// <summary>複数（指定数）</summary>
        Multiple,

        /// <summary>前方1マス - マス対象（ソード系）</summary>
        FrontTile,

        /// <summary>前方2マス - マス対象（ロングソード系）</summary>
        FrontTwoTiles,

        /// <summary>前方3マス - マス対象</summary>
        FrontThreeTiles
    }
}
