using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// バリア（一定ダメージを吸収するシールド）を付与するカード効果
    /// </summary>
    public class BarrierEffect : ICardEffect
    {
        private readonly int barrierHp;

        /// <param name="barrierHp">吸収できるダメージ量</param>
        public BarrierEffect(int barrierHp)
        {
            if (barrierHp <= 0) throw new ArgumentException("バリアHPは1以上である必要があります", nameof(barrierHp));
            this.barrierHp = barrierHp;
        }

        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Player.SetBarrier(barrierHp);
        }
    }
}
