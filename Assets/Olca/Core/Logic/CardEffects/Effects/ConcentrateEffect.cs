using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.CardEffects.Effects
{
    /// <summary>
    /// 集中状態を付与するカード効果
    /// 次に使用する攻撃カードの威力を倍率で強化する
    /// </summary>
    public class ConcentrateEffect : ICardEffect
    {
        private readonly float multiplier;

        /// <param name="multiplier">次カードへの威力倍率（デフォルト1.5倍）</param>
        public ConcentrateEffect(float multiplier = 1.5f)
        {
            if (multiplier <= 1.0f) throw new ArgumentException("倍率は1.0より大きい必要があります", nameof(multiplier));
            this.multiplier = multiplier;
        }

        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            // 旧戦闘の遅延処理が次戦のプレイヤーへ再付与することを防ぐ。
            if (context.Battle.IsGameOver) return;
            context.Player.SetConcentrateBuff(multiplier);
        }
    }
}
