using System.Collections.Generic;
using System.Linq;
using Olca.Core.Interfaces;

namespace Olca.Application.Battle
{
    /// <summary>
    /// シンプルな融合レシピ提供者。
    /// コードベースでレシピを定義する（CSVマスターデータ未整備の間の仮実装）。
    /// </summary>
    public class SimpleFusionRecipeProvider : IFusionRecipeProvider
    {
        // レシピ: 素材IDセット（順序不問） → 結果カードID
        private readonly List<(HashSet<string> materials, string resultId)> recipes;

        public SimpleFusionRecipeProvider()
        {
            recipes = new List<(HashSet<string>, string)>
            {
                // ソード + ボム → ファイアソード
                (new HashSet<string> { SimpleCardFactory.Sword, SimpleCardFactory.Bomb },
                 SimpleCardFactory.FireSword),
            };
        }

        /// <summary>
        /// 素材カードIDの組み合わせから結果カードIDを取得する（順序不問）
        /// </summary>
        public string FindResultCardId(IReadOnlyList<string> materialCardIds)
        {
            if (materialCardIds == null || materialCardIds.Count < 2) return null;

            var inputSet = new HashSet<string>(materialCardIds);

            foreach (var (materials, resultId) in recipes)
            {
                if (materials.SetEquals(inputSet))
                    return resultId;
            }

            return null;
        }

        /// <summary>
        /// 指定した素材IDリストを全て含む融合レシピが存在するか判定する（部分一致）
        /// </summary>
        public bool HasRecipeContaining(IReadOnlyList<string> partialMaterialIds)
        {
            if (partialMaterialIds == null || partialMaterialIds.Count == 0) return false;

            foreach (var (materials, _) in recipes)
            {
                if (partialMaterialIds.All(id => materials.Contains(id)))
                    return true;
            }

            return false;
        }
    }
}
