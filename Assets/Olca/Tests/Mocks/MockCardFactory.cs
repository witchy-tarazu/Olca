using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Tests.Mocks
{
    // 既存テスト値を明示的な第1段階へ移すfixture。製品定義や後続段階を補完しない。
    public static class MockTuningDefinitions
    {
        public static TuningGrantDefinition FromModifier(Card card)
        {
            var value = card.Modifier.Clone();
            if (!value.HasStageDependentValues) return null;
            int cost = value.CostAdjustment; bool zero = value.ZeroCost;
            value.CostAdjustment = 0; value.ZeroCost = false;
            return new TuningGrantDefinition(card.Id, new[] {
                new TuningEffectDefinition(card.Id + ":test", 4, new Dictionary<int, CardModifier> { { 1, value } })
            }, cost, zero);
        }
    }

    /// <summary>
    /// テスト用カードファクトリのモック
    /// </summary>
    public class MockCardFactory : ICardFactory, IExceededCardDefinitionProvider
    {
        private readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();

        /// <summary>
        /// テスト用カードを登録する
        /// </summary>
        public void Register(string cardId, Card card)
        {
            cards[cardId] = card;
        }

        public Card CreateById(string cardId)
        {
            cards.TryGetValue(cardId, out var card);
            return card;
        }

        public bool HasExceededDefinition(Card baseCard, ExceedLevel nextLevel)
            => cards.ContainsKey($"{baseCard.Id}_lv{(int)nextLevel}");

        public Card CreateExceeded(Card baseCard, ExceedLevel nextLevel)
        {
            // 超越後カードID: "{元ID}_lv{level}" で探す
            string exceededId = $"{baseCard.Id}_lv{(int)nextLevel}";
            if (cards.TryGetValue(exceededId, out var card))
                return card;

            // 登録がない場合はベースカードのExceedLevelを上げたコピーを返す
            return new Card(
                exceededId, baseCard.Name + "★", baseCard.Cost,
                baseCard.Rarity, baseCard.Category, baseCard.Element,
                baseCard.Series, baseCard.Power + 10, baseCard.Description,
                exceedLevel: nextLevel
            );
        }
    }

    /// <summary>
    /// テスト用融合レシピプロバイダのモック
    /// </summary>
    public class MockFusionRecipeProvider : IFusionRecipeProvider
    {
        private readonly List<(List<string> materials, string resultId)> recipes
            = new List<(List<string>, string)>();

        /// <summary>
        /// テスト用レシピを登録する
        /// </summary>
        public void Register(string resultId, params string[] materialIds)
        {
            recipes.Add((new List<string>(materialIds), resultId));
        }

        public string FindResultCardId(System.Collections.Generic.IReadOnlyList<string> materialCardIds)
        {
            // 素材の順序不問で一致するレシピを探す
            var inputSorted = new List<string>(materialCardIds);
            inputSorted.Sort();

            foreach (var (materials, resultId) in recipes)
            {
                if (materials.Count != inputSorted.Count) continue;
                var recipeSorted = new List<string>(materials);
                recipeSorted.Sort();

                bool match = true;
                for (int i = 0; i < recipeSorted.Count; i++)
                {
                    if (recipeSorted[i] != inputSorted[i]) { match = false; break; }
                }
                if (match) return resultId;
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
                bool allContained = true;
                foreach (var id in partialMaterialIds)
                {
                    if (!materials.Contains(id)) { allContained = false; break; }
                }
                if (allContained) return true;
            }
            return false;
        }
    }
}
