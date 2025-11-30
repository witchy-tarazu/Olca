namespace Olca.Core.Logic.CardEffects
{
    /// <summary>
    /// カード効果のインターフェース（Strategyパターン）
    /// </summary>
    public interface ICardEffect
    {
        /// <summary>
        /// カード効果を実行
        /// </summary>
        /// <param name="context">バトルコンテキスト</param>
        void Execute(BattleContext context);
    }
}
