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
        private readonly DamageTargetType targetType;
        private readonly int targetCount;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="baseDamage">基本ダメージ</param>
        /// <param name="targetType">対象タイプ</param>
        /// <param name="targetCount">対象数（複数攻撃の場合）</param>
        public DamageEffect(int baseDamage, DamageTargetType targetType = DamageTargetType.Single, int targetCount = 1)
        {
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

            var aliveEnemies = context.Battle.GetAliveEnemies();
            if (aliveEnemies.Count == 0) return;

            switch (targetType)
            {
                case DamageTargetType.Single:
                    // 単体攻撃: 最前列の敵1体（敵対象）
                    DealDamageToEnemy(aliveEnemies[0], context);
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
        /// </summary>
        private void DealDamageToEnemy(Enemy enemy, BattleContext context)
        {
            // baseDamageには既にカードのPowerが含まれている（CardEffectFactoryで設定済み）
            int finalDamage = baseDamage;

            // 属性相性を考慮（TODO: 後で実装）
            // finalDamage = ApplyElementalBonus(finalDamage, context.UsedCard.Element, enemy.Element);

            enemy.TakeDamage(finalDamage, context.UsedCard.Element);
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

            var aliveEnemies = context.Battle.GetAliveEnemies();
            if (aliveEnemies.Count == 0) return;

            // 最前列の敵のX座標を基準にする
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
