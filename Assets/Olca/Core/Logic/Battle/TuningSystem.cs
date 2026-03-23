using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 調律システム
    /// 調律カードを対象カードに適用して追加効果を付与する
    /// </summary>
    public class TuningSystem
    {
        /// <summary>
        /// 調律可能かどうかを検証する
        /// </summary>
        public TuningValidationResult Validate(Card targetCard, Card tuningCard)
        {
            if (targetCard == null || tuningCard == null)
                return TuningValidationResult.Fail("カードがnullです");
            if (tuningCard.Category != CardCategory.Tuning)
                return TuningValidationResult.Fail("調律カードではありません（CardCategory.Tuningが必要）");
            if (targetCard.Category == CardCategory.Tuning)
                return TuningValidationResult.Fail("調律カードへの調律はできません");

            return TuningValidationResult.Success();
        }

        /// <summary>
        /// 調律を実行し、追加効果が付与された新しいカードを返す
        /// 元のカードは変更されない（イミュータブル）
        /// </summary>
        /// <param name="targetCard">調律対象カード</param>
        /// <param name="tuningCard">調律素材カード（消費される）</param>
        /// <returns>調律結果</returns>
        public TuningResult Execute(Card targetCard, Card tuningCard)
        {
            var validation = Validate(targetCard, tuningCard);
            if (!validation.IsValid)
                return TuningResult.Fail(validation.ErrorMessage);

            var resultCard = targetCard.ApplyTuning(tuningCard);
            return TuningResult.Success(resultCard);
        }
    }

    /// <summary>
    /// 調律検証結果
    /// </summary>
    public readonly struct TuningValidationResult
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }

        private TuningValidationResult(bool isValid, string errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static TuningValidationResult Success() =>
            new TuningValidationResult(true, null);

        public static TuningValidationResult Fail(string message) =>
            new TuningValidationResult(false, message);
    }

    /// <summary>
    /// 調律実行結果
    /// </summary>
    public readonly struct TuningResult
    {
        public bool IsSuccess { get; }
        public string ErrorMessage { get; }
        public Card ResultCard { get; }

        private TuningResult(bool isSuccess, Card resultCard, string errorMessage)
        {
            IsSuccess = isSuccess;
            ResultCard = resultCard;
            ErrorMessage = errorMessage;
        }

        public static TuningResult Success(Card card) =>
            new TuningResult(true, card, null);

        public static TuningResult Fail(string message) =>
            new TuningResult(false, null, message);
    }
}
