using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Interfaces;

namespace Olca.Application.Battle
{
    /// <summary>
    /// シンプルな融合レシピ提供者。
    /// コードベースでレシピを定義する（CSVマスターデータ未整備の間の仮実装）。
    /// </summary>
    public class SimpleFusionRecipeProvider : IFusionRecipeProvider, IFusionDefinitionCatalogProvider
    {
        // レシピ: 素材ID一覧（順序不問） → 結果カードID
        private readonly List<(List<string> materials, string resultId)> recipes;
        private readonly SimpleCardFactory cardFactory;

        public FusionDefinitionCatalog FusionDefinitions { get; }
        public IReadOnlyList<CollectionRecipe> CollectionRecipes => FusionDefinitions.CollectionRecipes;

        public SimpleFusionRecipeProvider(SimpleCardFactory cardFactory=null)
        {
            this.cardFactory=cardFactory??new SimpleCardFactory();
            recipes = new List<(List<string>, string)>
            {
                // ソード + ボム → ファイアソード
                (new List<string> { SimpleCardFactory.Sword, SimpleCardFactory.Bomb },
                 SimpleCardFactory.FireSword),
            };
            FusionDefinitions=new FusionDefinitionCatalog();
            foreach(var card in this.cardFactory.FusionCards)
                FusionDefinitions.RegisterCard(card,card.Series.Name,card.Id,(int)card.ExceedLevel);
            foreach(var recipe in recipes)
                FusionDefinitions.RegisterSpecial("special/v1/"+string.Join("+",recipe.materials.OrderBy(x=>x))+"/"+recipe.resultId,
                    this.cardFactory.CreateById(recipe.materials[0]),this.cardFactory.CreateById(recipe.materials[1]),this.cardFactory.CreateById(recipe.resultId));
            // 通常の系統組合せ表は製品仕様待ち。未登録を別カードへ推測しない。
        }

        /// <summary>
        /// 素材カードIDの組み合わせから結果カードIDを取得する（順序不問）
        /// </summary>
        public string FindResultCardId(IReadOnlyList<string> materialCardIds)
        {
            if (materialCardIds == null || materialCardIds.Count < 2) return null;

            foreach (var (materials, resultId) in recipes)
            {
                if (materials.Count == materialCardIds.Count && ContainsMaterials(materials, materialCardIds))
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
                if (ContainsMaterials(materials, partialMaterialIds))
                    return true;
            }

            return false;
        }
        // 順序は問わないが、各IDの必要枚数を超えた素材は受け付けない。
        private static bool ContainsMaterials(IReadOnlyList<string> recipe, IReadOnlyList<string> requested)
        {
            var remaining = new List<string>(recipe);
            foreach (var id in requested)
            {
                if (!remaining.Remove(id)) return false;
            }
            return true;
        }
    }
}
