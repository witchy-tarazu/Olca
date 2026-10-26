using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>保存済み報酬の契約検証。抽選/復元モデル生成/受領処理は所有しない。</summary>
    internal static class CheckpointRewardPlanValidator
    {
        internal static void Validate(CheckpointDto checkpoint,CheckpointDefinitions definitions,
            Dictionary<string,Card> cards,IReadOnlyDictionary<int,Enemy> enemies)
        {
            var battle=checkpoint.Battle;
            string contactId=enemies[battle.ContactCell].EnemyId;
            CheckpointCodec.ValidateReferences(battle.FirstHand,cards);
            CheckpointCodec.ValidateReferences(battle.RewardCandidates,cards);
            CheckpointValues.Require(ValidItem(battle.ItemRewardIndex,battle,definitions),"アイテム報酬候補/戦闘条件が不正です");
            ValidateEvaluationBands(checkpoint,definitions,cards,contactId);
            // 部分一致bossの旧保存だけは1〜3候補を保持。新規帯計画にこの例外を広げない。
            CheckpointValues.Require(ValidChoiceCount(contactId,battle.RewardOffered,battle.RewardCandidates,true),"報酬件数と接敵種別が一致しません");
            CheckpointValues.Require(battle.FirstHand.Length==Math.Min(battle.HandLimit,checkpoint.DeckOrder.Length)&&
                battle.FirstHand.All(id=>checkpoint.DeckOrder.Contains(id)),"初回手札が不整合です");
            CheckpointValues.Require(ValidCandidateUse(checkpoint,battle.RewardOffered,battle.RewardCandidates),"報酬候補が不整合です");
        }
        private static bool ValidChoiceCount(string enemyId,bool offered,string[] candidates,bool allowLegacyBoss)
            =>enemyId=="boss"?offered:!offered||candidates.Length==3||allowLegacyBoss&&enemyId.Contains("boss");
        private static bool ValidCandidateUse(CheckpointDto checkpoint,bool offered,string[] candidates)
            =>offered==(candidates.Length>0)&&candidates.Length<=3&&
                candidates.All(id=>!checkpoint.DeckOrder.Contains(id)&&!checkpoint.ObtainedOrder.Contains(id));
        private static bool ValidItem(int? index,CheckpointBattleDto battle,CheckpointDefinitions definitions)
            =>!index.HasValue||battle.ItemRewardHasNextFloor.HasValue&&index.Value>=0&&index.Value<definitions.ItemRewards.Count&&
                definitions.ItemRewards[index.Value].Weight>0&&definitions.ItemQualifyingCells(battle.ParticipantCells,battle.ItemRewardHasNextFloor.Value).Length>0;
        private static void ValidateEvaluationBands(CheckpointDto checkpoint,CheckpointDefinitions definitions,Dictionary<string,Card> cards,string contactId)
        {
            var battle=checkpoint.Battle;var bands=battle.EvaluationRewards;
            if(bands==null){CheckpointValues.Require(battle.EvaluationRewardDefinitionId==null,"評価報酬帯が欠落しています");return;}
            CheckpointValues.Require(!string.IsNullOrWhiteSpace(battle.EvaluationRewardDefinitionId)&&bands.Length==4&&
                bands.All(v=>v!=null)&&bands.Select(v=>v.Grade).OrderBy(v=>v).SequenceEqual(new[]{0,1,2,3}),"4評価帯の保存が不正です");
            foreach(var band in bands){
                CheckpointCodec.ValidateReferences(band.RewardCandidates,cards);
                CheckpointValues.Require(band.CurrencyAmounts!=null&&band.CurrencyAmounts.Length==battle.ParticipantCells.Length&&
                    band.CurrencyAmounts.All(v=>v>=0)&&ValidCandidateUse(checkpoint,band.RewardOffered,band.RewardCandidates)&&
                    ValidChoiceCount(contactId,band.RewardOffered,band.RewardCandidates,false)&&ValidItem(band.ItemRewardIndex,battle,definitions),"評価報酬の内容が不正です");
            }
            CheckpointValues.Require(bands.SelectMany(v=>v.RewardCandidates).Distinct().Count()==bands.Sum(v=>v.RewardCandidates.Length),"評価帯間で候補個体が重複しています");
            var first=bands.Single(v=>v.Grade==0);
            CheckpointValues.Require(battle.RewardOffered==first.RewardOffered&&battle.RewardCandidates.SequenceEqual(first.RewardCandidates)&&
                battle.CurrencyAmounts!=null&&battle.CurrencyAmounts.SequenceEqual(first.CurrencyAmounts)&&battle.ItemRewardIndex==first.ItemRewardIndex,"開始時の評価報酬参照が不一致です");
        }
    }
}
