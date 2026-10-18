using System;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 超越（Exceed）システム
    /// 同名カード2枚を合成して上位グレードカードを生成する
    /// </summary>
    public class ExceedSystem
    {
        private readonly ICardFactory cardFactory;

        /// <param name="cardFactory">カード生成ファクトリ</param>
        public ExceedSystem(ICardFactory cardFactory)
        {
            this.cardFactory = cardFactory ?? throw new ArgumentNullException(nameof(cardFactory));
        }

        /// <summary>
        /// 超越可能かどうかを検証する
        /// </summary>
        public ExceedValidationResult Validate(Card cardA, Card cardB)
        {
            if (cardA == null || cardB == null)
                return ExceedValidationResult.Fail("カードがnullです");
            if (cardA.Id != cardB.Id)
                return ExceedValidationResult.Fail("同名カード2枚が必要です");
            if (cardA.ExceedLevel != cardB.ExceedLevel)
                return ExceedValidationResult.Fail("同じ超越段階のカード2枚が必要です");
            if (cardA.ExceedLevel >= ExceedLevel.Lv3)
                return ExceedValidationResult.Fail("最上位カードはこれ以上超越できません");

            return ExceedValidationResult.Success();
        }

        /// <summary>
        /// 超越を実行し、上位グレードカードを返す
        /// </summary>
        /// <param name="cardA">素材カード1</param>
        /// <param name="cardB">素材カード2（cardAと同名）</param>
        /// <returns>超越結果</returns>
        public bool HasResultDefinition(Card card)
            => card != null && cardFactory is IExceededCardDefinitionProvider definitions &&
               definitions.HasExceededDefinition(card, (ExceedLevel)((int)card.ExceedLevel + 1));

        public ExceedResult Execute(Card cardA, Card cardB)
        {
            var validation = Validate(cardA, cardB);
            if (!validation.IsValid)
                return ExceedResult.Fail(validation.ErrorMessage);

            var nextLevel = (ExceedLevel)((int)cardA.ExceedLevel + 1);
            var resultCard = cardFactory.CreateExceeded(cardA, nextLevel);
            if (resultCard == null)
                return ExceedResult.Fail("超越後カードの生成に失敗しました");

            return ExceedResult.Success(resultCard);
        }
    }

    /// <summary>
    /// 超越検証結果
    /// </summary>
    public readonly struct ExceedValidationResult
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }

        private ExceedValidationResult(bool isValid, string errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        public static ExceedValidationResult Success() =>
            new ExceedValidationResult(true, null);

        public static ExceedValidationResult Fail(string message) =>
            new ExceedValidationResult(false, message);
    }

    /// <summary>
    /// 超越実行結果
    /// </summary>
    public readonly struct ExceedResult
    {
        public bool IsSuccess { get; }
        public string ErrorMessage { get; }
        public Card ResultCard { get; }

        private ExceedResult(bool isSuccess, Card resultCard, string errorMessage)
        {
            IsSuccess = isSuccess;
            ResultCard = resultCard;
            ErrorMessage = errorMessage;
        }

        public static ExceedResult Success(Card card) =>
            new ExceedResult(true, card, null);

        public static ExceedResult Fail(string message) =>
            new ExceedResult(false, null, message);
    }
}
