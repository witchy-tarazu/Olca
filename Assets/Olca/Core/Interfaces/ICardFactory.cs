using Olca.Core.Domain;

namespace Olca.Core.Interfaces
{
    /// <summary>
    /// カード生成の抽象インターフェース
    /// Core層がマスターデータに直接依存しないためのDI境界
    /// </summary>
    public interface ICardFactory
    {
        /// <summary>
        /// カードIDからカードを生成する
        /// </summary>
        /// <param name="cardId">カードID</param>
        /// <returns>生成されたカード。存在しないIDの場合はnull</returns>
        Card CreateById(string cardId);

        /// <summary>
        /// 超越後カードを生成する（ベースカードと超越レベルから）
        /// </summary>
        /// <param name="baseCard">超越前のカード</param>
        /// <param name="nextLevel">超越後のグレード</param>
        /// <returns>生成された超越カード。生成不可の場合はnull</returns>
        Card CreateExceeded(Card baseCard, ExceedLevel nextLevel);
    }
    /// <summary>計算による仮性能ではなく、登録済みの上位カード定義が存在するか。</summary>
    public interface IExceededCardDefinitionProvider
    {
        bool HasExceededDefinition(Card baseCard, ExceedLevel nextLevel);
    }
}
