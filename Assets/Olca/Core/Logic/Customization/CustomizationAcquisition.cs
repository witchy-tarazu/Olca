using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Customization
{
    public sealed class CustomizationAcquisitionContext
    {
        public string ClearRunId { get; }
        public string DungeonKey { get; }
        public bool IsClearReward=>ClearRunId!=null;
        public CustomizationAcquisitionContext(string clearRunId,string dungeonKey=null){ClearRunId=clearRunId;DungeonKey=dungeonKey;}
    }
    public interface ICustomizationAcquisitionProvider
    {
        // 拠点イベントの安定ID、候補、受取上限は入手元から明示する。価格/消費は追加しない。
        CustomizationAcquisitionDefinition Resolve(CustomizationAcquisitionContext context);
    }
    public sealed class CustomizationAcquisitionDefinition
    {
        private readonly CustomizationAcquisitionPlan data;
        public CustomizationAcquisitionPlan Snapshot=>data.Copy();
        public CustomizationAcquisitionDefinition(string contextId,int maximumClaims,IEnumerable<CustomizationGrant> candidates,IReadOnlyDictionary<string,string> displayNames)
        {
            data=new CustomizationAcquisitionPlan{ContextId=contextId,MaximumClaims=maximumClaims,
                Candidates=candidates?.Select(v=>{var c=CustomizationAcquisitionCandidate.FromGrant(v);c.DisplayName=displayNames!=null&&displayNames.TryGetValue(v.RequestId,out var name)?name:null;return c;}).ToArray()};
            CustomizationAcquisitionPlan.Validate(data);
        }
    }
    public sealed class CustomizationAcquisitionCandidate
    {
        public string RequestId,ClearRunId,DisplayName;
        public string[] Accessories;
        public CustomizationItemCount[] Parts,UnlockItems;
        public CustomizationGrant ToGrant()=>new CustomizationGrant(RequestId,Accessories,
            Parts?.ToDictionary(v=>v.Id,v=>v.Count),UnlockItems?.ToDictionary(v=>v.Id,v=>v.Count),ClearRunId);
        public CustomizationAcquisitionCandidate Copy()=>new CustomizationAcquisitionCandidate{RequestId=RequestId,ClearRunId=ClearRunId,DisplayName=DisplayName,
            Accessories=Accessories?.ToArray(),Parts=Parts?.Select(v=>v?.Copy()).ToArray(),UnlockItems=UnlockItems?.Select(v=>v?.Copy()).ToArray()};
        internal static CustomizationAcquisitionCandidate FromGrant(CustomizationGrant grant)
        {
            if(grant==null)throw new ArgumentException("取得候補が欠落しています");
            return new CustomizationAcquisitionCandidate{RequestId=grant.RequestId,ClearRunId=grant.ClearRunId,Accessories=grant.Accessories.ToArray(),
                Parts=grant.Parts.Select(v=>new CustomizationItemCount{Id=v.Key,Count=v.Value}).ToArray(),UnlockItems=grant.UnlockItems.Select(v=>new CustomizationItemCount{Id=v.Key,Count=v.Value}).ToArray()};
        }
    }
    /// <summary>提示前に保存する固定候補。受取済みは既存の原子的受領履歴で判定する。</summary>
    public sealed class CustomizationAcquisitionPlan
    {
        public string ContextId,BoardId;
        public int MaximumClaims;
        public CustomizationAcquisitionCandidate[] Candidates;
        public CustomizationAcquisitionPlan Copy()=>new CustomizationAcquisitionPlan{ContextId=ContextId,BoardId=BoardId,MaximumClaims=MaximumClaims,Candidates=Candidates?.Select(v=>v?.Copy()).ToArray()};
        public static void Validate(CustomizationAcquisitionPlan p)
        {
            if(p==null||string.IsNullOrWhiteSpace(p.ContextId)||p.MaximumClaims<0||p.Candidates==null||p.Candidates.Any(v=>v==null)||
                p.Candidates.Select(v=>v.RequestId).Distinct().Count()!=p.Candidates.Length)throw new ArgumentException("取得候補計画が不正です");
            foreach(var v in p.Candidates){
                if(string.IsNullOrWhiteSpace(v.DisplayName)||v.Accessories==null||v.Parts==null||v.UnlockItems==null||v.Parts.Any(n=>n==null)||v.UnlockItems.Any(n=>n==null))throw new ArgumentException("取得内容が欠落しています");
                v.ToGrant();
            }
            if(p.Candidates.Select(v=>v.ClearRunId).Distinct().Count()>1)throw new ArgumentException("入手元の異なる候補を混在できません");
            var run=p.Candidates.FirstOrDefault()?.ClearRunId;
            if(run!=null&&p.ContextId!="clear:"+run)throw new ArgumentException("クリア報酬の入手元IDが一致しません");
        }
    }
}
namespace Olca.Core.Domain
{
    using Olca.Core.Logic.Customization;
    public sealed partial class ClearReturnSession
    {
        public CustomizationAcquisitionPlan FindCustomizationAcquisition(string contextId)=>CustomizationSnapshot.Acquisitions.SingleOrDefault(v=>v.ContextId==contextId)?.Copy();
        /// <summary>同じ入手元の候補は再登録で変えない。取消/再開は保存済み計画を再提示する。</summary>
        public bool TryPrepareCustomizationAcquisition(CustomizationCatalog catalog,CustomizationAcquisitionDefinition definition)
        {
            if(!Available())return Fail("未保存の処理を先に確定してください");
            try{
                if(catalog==null||definition==null)throw new ArgumentException("入手元と装備定義が必要です");
                var plan=definition.Snapshot;var existing=FindCustomizationAcquisition(plan.ContextId);
                if(existing!=null){if(existing.BoardId!=catalog.BoardId)return Fail("取得計画の盤面が一致しません");Error=null;return true;}
                if(customizationRunActive?.Invoke()==true||!ExchangeAvailable())return Fail("取得候補は帰還確定後に提示してください");
                var next=current.Copy();var state=BindCustomization(next,catalog);plan.BoardId=catalog.BoardId;
                foreach(var candidate in plan.Candidates){
                    var grant=candidate.ToGrant();
                    if(grant.ClearRunId!=null&&(current.Flow?.RunId!=grant.ClearRunId||current.Flow.Outcome!=RunReturnOutcome.Cleared||current.NextDeparture?.ParentRunId==grant.ClearRunId))return Fail("クリアが確定した攻略が必要です");
                    if(grant.Accessories.Any(id=>!catalog.Accessories.Contains(id))||grant.Parts.Keys.Any(id=>!catalog.Parts.ContainsKey(id))||grant.UnlockItems.Keys.Any(id=>!catalog.UnlockItems.ContainsKey(id)))return Fail("未登録の取得候補です");
                    if(state.Acquisitions.Any(v=>v.Candidates.Any(c=>c.RequestId==grant.RequestId))||state.Receipts.Any(v=>v.RequestId==grant.RequestId))return Fail("別の入手元で使われた取得IDです");
                }
                // 装飾品の再入手導線だけを除外する。所持済みパーツは候補に残す。
                plan.Candidates=plan.Candidates.Where(v=>!v.Accessories.Any(id=>state.OwnedAccessoryIds.Contains(id))).ToArray();
                state.Acquisitions=state.Acquisitions.Concat(new[]{plan}).ToArray();return Stage(next);
            }catch(Exception e) when(e is ArgumentException||e is OverflowException){return Fail(e.Message);}
        }
        public bool TryClaimCustomizationAcquisition(CustomizationCatalog catalog,string contextId,string requestId)
        {
            if(!Available())return Fail("未保存の処理を先に再試行してください");
            var plan=FindCustomizationAcquisition(contextId);var candidate=plan?.Candidates.SingleOrDefault(v=>v.RequestId==requestId);
            if(candidate==null||catalog==null||plan.BoardId!=catalog.BoardId)return Fail("保存された取得候補が必要です");
            var receipt=CustomizationSnapshot.Receipts.SingleOrDefault(v=>v.RequestId==requestId);
            if(receipt==null&&plan.Candidates.Count(v=>CustomizationSnapshot.Receipts.Any(r=>r.RequestId==v.RequestId))>=plan.MaximumClaims)return Fail("この入手元の受取は完了しています");
            return TryGrantCustomizationCore(catalog,candidate.ToGrant(),true);
        }
    }
}
