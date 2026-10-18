using Olca.Core.Domain;
using System.Collections.Generic;

namespace Olca.Core.Interfaces
{
    /// <summary>
    /// カードプールのインターフェース
    /// ダンジョン報酬のカード生成に使用する
    /// </summary>
    public interface ICardPool
    {
        /// <summary>
        /// 指定レアリティのカードをランダムに1枚取得する
        /// </summary>
        /// <param name="rarity">取得するカードのレアリティ</param>
        /// <param name="random">乱数プロバイダー</param>
        /// <returns>取得したカード</returns>
        Card GetRandomCard(CardRarity rarity, IRandomProvider random);

        /// <summary>
        /// 抽選対象の既存定義を列挙する。空なら候補なし。呼出側はリストを変更しない。
        /// </summary>
        IReadOnlyList<Card> GetCandidates(CardRarity rarity);
    }
}
