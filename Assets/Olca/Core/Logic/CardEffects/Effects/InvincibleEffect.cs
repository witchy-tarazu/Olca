using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// 一定時間、無敵状態になるカード効果
    /// BattleSystem.Tick()が無敵時間をカウントダウンする
    /// </summary>
    public class InvincibleEffect : ICardEffect
    {
        private readonly int durationMs;

        /// <param name="durationMs">無敵持続時間（ミリ秒）</param>
        public InvincibleEffect(int durationMs = 3000)
        {
            if (durationMs <= 0) throw new ArgumentException("無敵時間は1以上である必要があります", nameof(durationMs));
            this.durationMs = durationMs;
        }

        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.Player.SetInvincible(durationMs);
        }
    }
}
