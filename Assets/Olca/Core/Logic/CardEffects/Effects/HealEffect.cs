using System;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// 回復するカード効果
    /// </summary>
    public class HealEffect : ICardEffect
    {
        private readonly int healAmount;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="healAmount">回復量</param>
        public HealEffect(int healAmount)
        {
            this.healAmount = healAmount;
        }

        /// <summary>
        /// 効果を実行
        /// </summary>
        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            // プレイヤーを回復
            // 上限処理はHP反映時に行う。同フレームの未確定ダメージを失わない。
            context.Player.Heal(healAmount);
        }
    }
}
