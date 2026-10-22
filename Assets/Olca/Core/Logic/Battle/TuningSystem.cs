using System;
using System.Collections.Generic;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 調律システム
    /// 調律カードを対象カードに適用して追加効果を付与する
    /// </summary>
    public class TuningSystem
    {
        private readonly Func<Card, TuningGrantDefinition> definitionProvider;
        public TuningSystem(Func<Card, TuningGrantDefinition> definitionProvider = null)
        { this.definitionProvider = definitionProvider; }

        // 明示的な付与定義が新しい適性。旧Tuning区分は既存カード・保存の互換入口だけに残す。
        public static bool IsMaterial(Card card) => card!=null&&(card.TuningDefinition!=null||card.Category==CardCategory.Tuning);
        private TuningGrantDefinition Definition(Card card) => card?.TuningDefinition??
            (card?.Category==CardCategory.Tuning?definitionProvider?.Invoke(card):null);

        /// <summary>
        /// 調律可能かどうかを検証する
        /// </summary>
        public TuningValidationResult Validate(Card targetCard, Card tuningCard)
        {
            if (targetCard == null || tuningCard == null)
                return TuningValidationResult.Fail("カードがnullです");
            if (!IsMaterial(tuningCard))
                return TuningValidationResult.Fail("調律カードとしての適性がありません");
            if (IsMaterial(targetCard))
                return TuningValidationResult.Fail("調律カードへの調律はできません");

            var definition=Definition(tuningCard);
            if(definition!=null&&!definition.Allows(targetCard))return TuningValidationResult.Fail("調律の対象条件を満たしていません");
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

            var definition = Definition(tuningCard);
            if (definition == null)
            {
                var modifier = tuningCard.Modifier;
                // 段階に依存しない既存コスト定義だけは、その固定値をそのまま使用する。
                if (modifier.HasStageDependentValues || (modifier.CostAdjustment == 0 && !modifier.ZeroCost))
                    return TuningResult.Undefined("調律の段階表または付与定義が未定義です");
                definition = new TuningGrantDefinition(tuningCard.Id, fixedCostAdjustment: modifier.CostAdjustment, zeroCost: modifier.ZeroCost);
            }
            return TuningEvaluator.Evaluate(targetCard, definition);
        }
    }

    /// <summary>元の固有効果と全付与履歴から再構築する純粋評価器。</summary>
    public static class TuningEvaluator
    {
        public static TuningResult Evaluate(Card target, TuningGrantDefinition additional)
        {
            if (target == null || additional == null) return TuningResult.Undefined("付与定義が未定義です");
            if(TuningSystem.IsMaterial(target))return TuningResult.Fail("調律カードへの調律はできません");
            if(!additional.Allows(target))return TuningResult.Fail("調律の対象条件を満たしていません");
            if (target.IsEffectCopy || target.TuningCount != target.TuningHistory.Count)
                return TuningResult.Undefined("調律付与履歴が不足しているか使用時コピーです");
            var history = new List<TuningGrantDefinition>(target.TuningHistory) { additional };
            var original = target.GetTuningBaseModifier();
            var evaluated = original.Clone();
            if (evaluated.AdditionalEffects == null) return TuningResult.Undefined("元カードの追加効果が未定義です");
            try
            {
                foreach (var grant in history)
                {
                    if(grant!=null&&!grant.Allows(target))return TuningResult.Fail("保存された調律の対象条件を満たしていません");
                    if (grant == null || string.IsNullOrEmpty(grant.Id) ||
                        (grant.Effects.Count == 0 && grant.FixedCostAdjustment == 0 && !grant.ZeroCost))
                        return TuningResult.Undefined("調律の付与定義が未定義です");
                    evaluated.CostAdjustment = checked(evaluated.CostAdjustment + grant.FixedCostAdjustment);
                    evaluated.ZeroCost |= grant.ZeroCost;
                    foreach (var effect in grant.Effects)
                    {
                        if (effect == null || string.IsNullOrEmpty(effect.Id) || effect.MaxStage < 1 || effect.MaxStage > 4 ||
                            !effect.TryGetStage(Math.Min(history.Count, effect.MaxStage), out var value))
                            return TuningResult.Undefined("総調律回数に対応する段階表が未定義です");
                        // この基盤の対象は威力加算・状態異常付与。属性/範囲/貫通や重複上書きは扱わない。
                        if (value.CostAdjustment != 0 || value.ZeroCost || value.ElementOverride.HasValue || value.DamageMultiplier != 1f || value.AdditionalEffects == null)
                            return TuningResult.Undefined("段階値に対象外の変更が含まれています");
                        foreach (var status in value.AdditionalEffects)
                            if (status == null || (status.EffectType != AdditionalEffectType.Poison && status.EffectType != AdditionalEffectType.Paralyze))
                                return TuningResult.Undefined("段階値に対象外の追加効果が含まれています");
                        evaluated.BonusDamage = checked(evaluated.BonusDamage + value.BonusDamage);
                        evaluated.AdditionalEffects.AddRange(value.AdditionalEffects.ConvertAll(effectValue => effectValue.Clone()));
                    }
                }
                var result = target.WithEvaluatedTuning(original, history, evaluated);
                result.GetFinalCost(); // 確定前に最終コストの表現範囲も検証する。
                return TuningResult.Success(result);
            }
            catch (OverflowException) { return TuningResult.Undefined("調律評価値が範囲外です"); }
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
        public bool IsUndefined { get; }
        public string ErrorMessage { get; }
        public Card ResultCard { get; }

        private TuningResult(bool isSuccess, Card resultCard, string errorMessage, bool isUndefined = false)
        {
            IsSuccess = isSuccess;
            IsUndefined = isUndefined;
            ResultCard = resultCard;
            ErrorMessage = errorMessage;
        }

        public static TuningResult Success(Card card) =>
            new TuningResult(true, card, null);

        public static TuningResult Undefined(string message) => new TuningResult(false, null, message, true);

        public static TuningResult Fail(string message) =>
            new TuningResult(false, null, message);
    }
}
