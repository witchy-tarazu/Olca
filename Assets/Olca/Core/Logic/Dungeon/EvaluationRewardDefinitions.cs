using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>入手表・補正値は登録側で確定する。乱数や可変の敵参照を外へ渡さない。</summary>
    public sealed class BattleRewardContext
    {
        public string DungeonId { get; }
        public int ContactCell { get; }
        public bool HasNextFloor { get; }
        public IReadOnlyList<int> ParticipantCells { get; }
        public IReadOnlyList<int> BaseCurrencyAmounts { get; }
        public BattleRewardContext(string dungeonId, CheckpointBattleDto encounter, bool nextFloor)
        {
            DungeonId=dungeonId;ContactCell=encounter.ContactCell;HasNextFloor=nextFloor;
            ParticipantCells=Array.AsReadOnly((int[])encounter.ParticipantCells.Clone());
            BaseCurrencyAmounts=Array.AsReadOnly((int[])encounter.CurrencyAmounts.Clone());
        }
    }
    public interface IEvaluationRewardProvider
    {
        EvaluationRewardDefinition Resolve(BattleRewardContext context);
    }
    /// <summary>評価帯ごとの明示入力。通貨は初期参加者順の補正済み額。丸め規則は追加しない。</summary>
    public sealed class EvaluationRewardBand
    {
        public ICardPool CardPool { get; }
        public int NormalCardChanceBasisPoints { get; }
        public int ItemChancePercent { get; }
        public IReadOnlyList<int> ItemWeights { get; }
        public IReadOnlyList<int> CurrencyAmounts { get; }
        public EvaluationRewardBand(ICardPool cards, int normalCardChanceBasisPoints, int itemChancePercent,
            IEnumerable<int> itemWeights, IEnumerable<int> currencyAmounts)
        {
            var items=itemWeights?.ToArray();var amounts=currencyAmounts?.ToArray();
            if(cards==null||normalCardChanceBasisPoints<0||normalCardChanceBasisPoints>10000||itemChancePercent<0||itemChancePercent>100||
                items==null||amounts==null||items.Any(v=>v<0)||amounts.Any(v=>v<0)||items.Sum(v=>(long)v)>int.MaxValue)
                throw new ArgumentException("評価報酬の明示定義が不正です");
            CardPool=cards;NormalCardChanceBasisPoints=normalCardChanceBasisPoints;ItemChancePercent=itemChancePercent;
            ItemWeights=Array.AsReadOnly(items);CurrencyAmounts=Array.AsReadOnly(amounts);
        }
    }
    public sealed class EvaluationRewardDefinition
    {
        public string Id { get; }
        public IReadOnlyList<EvaluationRewardBand> Bands { get; }
        // Fair, Good, Great, Excellent の順。未登録帯を推測しない。
        public EvaluationRewardDefinition(string id, IEnumerable<EvaluationRewardBand> bands)
        {
            var values=bands?.ToArray();
            if(string.IsNullOrWhiteSpace(id)||values==null||values.Length!=4||values.Any(v=>v==null))throw new ArgumentException("4評価帯の定義が必要です");
            Id=id;Bands=Array.AsReadOnly(values);
        }
    }
}
namespace Olca.Core.Domain
{
    /// <summary>開始時に全帯の抽選結果を保存。再開時に現在の外部入手表で再抽選しない。</summary>
    public sealed class EvaluationRewardPlanDto
    {
        public int Grade;
        public bool RewardOffered;
        public string[] RewardCandidates;
        public int[] CurrencyAmounts;
        public int? ItemRewardIndex;
        public EvaluationRewardPlanDto Copy()=>new EvaluationRewardPlanDto{Grade=Grade,RewardOffered=RewardOffered,
            RewardCandidates=RewardCandidates?.ToArray(),CurrencyAmounts=CurrencyAmounts?.ToArray(),ItemRewardIndex=ItemRewardIndex};
    }
}
