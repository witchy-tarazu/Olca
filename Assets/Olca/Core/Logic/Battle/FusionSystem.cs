using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 融合システム
    /// 複数のカードをレシピに基づいて合成し、新しいカードを生成する
    /// </summary>
    public class FusionSystem
    {
        private readonly IFusionRecipeProvider recipeProvider;
        private readonly ICardFactory cardFactory;

        /// <param name="recipeProvider">融合レシピ提供者</param>
        /// <param name="cardFactory">カード生成ファクトリ</param>
        public FusionSystem(IFusionRecipeProvider recipeProvider, ICardFactory cardFactory)
        {
            this.recipeProvider = recipeProvider ?? throw new ArgumentNullException(nameof(recipeProvider));
            this.cardFactory = cardFactory ?? throw new ArgumentNullException(nameof(cardFactory));
        }

        /// <summary>通常選択・直接実行で共通の素材役割の排他条件。</summary>
        public static string MaterialRoleError(IReadOnlyList<Card> cards)
        {
            if(cards==null||cards.Count<2||cards.Any(c=>c==null))return null;
            int count=cards.Count(TuningSystem.IsMaterial);
            if(count>0&&count!=cards.Count)return "調律素材とそれ以外の融合はできません";
            if(count==cards.Count&&cards.Select(c=>c.Series?.Name).Distinct().Count()!=cards.Count)
                return "同じシリーズの調律素材は融合できません";
            return null;
        }

        /// <summary>
        /// 融合可能かどうかを検証する
        /// </summary>
        public FusionValidationResult Validate(IReadOnlyList<Card> materials)
        {
            if (materials == null || materials.Count < 2)
                return FusionValidationResult.Fail("2枚以上のカードが必要です");
            if (materials.Any(c => c == null))
                return FusionValidationResult.Fail("カードにnullが含まれています");

            var materialError=MaterialRoleError(materials);
            if(materialError!=null)return FusionValidationResult.Fail(materialError);
            var ids = materials.Select(c => c.Id).ToList();
            string resultId = recipeProvider.FindResultCardId(ids);
            if (resultId == null)
                return FusionValidationResult.Fail("この組み合わせの融合レシピが存在しません");

            return FusionValidationResult.Success(resultId);
        }

        /// <summary>
        /// 部分的な素材リストに対して互換性のあるレシピが存在するか判定する。
        /// カスタマイズモードでの選択可能カード絞り込みに使用する。
        /// </summary>
        public bool HasCompatibleRecipe(IReadOnlyList<string> partialIds)
            => recipeProvider.HasRecipeContaining(partialIds);

        /// <summary>
        /// 融合を実行し、生成カードを返す
        /// </summary>
        /// <param name="materials">融合素材カードリスト（2枚以上）</param>
        /// <returns>融合結果</returns>
        public FusionResult Execute(IReadOnlyList<Card> materials)
        {
            var validation = Validate(materials);
            if (!validation.IsValid)
                return FusionResult.Fail(validation.ErrorMessage);

            var resultCard = cardFactory.CreateById(validation.ResultCardId);
            if (resultCard == null)
                return FusionResult.Fail("融合後カードの生成に失敗しました");

            return FusionResult.Success(resultCard);
        }
    }

    /// <summary>
    /// 融合検証結果
    /// </summary>
    public readonly struct FusionValidationResult
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }
        public string ResultCardId { get; }

        private FusionValidationResult(bool isValid, string resultCardId, string errorMessage)
        {
            IsValid = isValid;
            ResultCardId = resultCardId;
            ErrorMessage = errorMessage;
        }

        public static FusionValidationResult Success(string resultCardId) =>
            new FusionValidationResult(true, resultCardId, null);

        public static FusionValidationResult Fail(string message) =>
            new FusionValidationResult(false, null, message);
    }

    /// <summary>
    /// 融合実行結果
    /// </summary>
    public readonly struct FusionResult
    {
        public bool IsSuccess { get; }
        public string ErrorMessage { get; }
        public Card ResultCard { get; }

        private FusionResult(bool isSuccess, Card resultCard, string errorMessage)
        {
            IsSuccess = isSuccess;
            ResultCard = resultCard;
            ErrorMessage = errorMessage;
        }

        public static FusionResult Success(Card card) =>
            new FusionResult(true, card, null);

        public static FusionResult Fail(string message) =>
            new FusionResult(false, null, message);
    }
}
