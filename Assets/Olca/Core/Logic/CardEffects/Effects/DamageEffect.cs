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
                    // 単体攻撃: 最前列の敵1体
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
            }
        }

        /// <summary>
        /// 敵にダメージを与える
        /// </summary>
        private void DealDamageToEnemy(Enemy enemy, BattleContext context)
        {
            // カードの属性とパワーを考慮
            int finalDamage = baseDamage + context.UsedCard.Power;

            // 属性相性を考慮（TODO: 後で実装）
            // finalDamage = ApplyElementalBonus(finalDamage, context.UsedCard.Element, enemy.Element);

            enemy.TakeDamage(finalDamage, context.UsedCard.Element);
        }
    }

    /// <summary>
    /// ダメージ対象タイプ
    /// </summary>
    public enum DamageTargetType
    {
        /// <summary>単体（最前列1体）</summary>
        Single,

        /// <summary>全体</summary>
        All,

        /// <summary>複数（指定数）</summary>
        Multiple
    }
}
