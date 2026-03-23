using System.Collections.Generic;
using Olca.Core.Domain;

namespace Olca.Core.Interfaces
{
    /// <summary>
    /// 融合レシピ問い合わせの抽象インターフェース
    /// </summary>
    public interface IFusionRecipeProvider
    {
        /// <summary>
        /// 素材カードIDの組み合わせから結果カードIDを取得する（順序不問）
        /// </summary>
        /// <param name="materialCardIds">素材カードIDのリスト</param>
        /// <returns>生成カードのID。レシピが存在しない場合はnull</returns>
        string FindResultCardId(IReadOnlyList<string> materialCardIds);
    }
}
