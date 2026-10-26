using System;
using System.Collections.Generic;
using System.Linq;

namespace Olca.Core.Logic.Customization
{
    public sealed class CustomizationCandidateAvailability
    {
        public bool IsReceived { get; }
        public bool HasOwnedAccessory { get; }
        public bool LimitReached { get; }
        public bool CanReceive=>!IsReceived&&!HasOwnedAccessory&&!LimitReached;
        internal CustomizationCandidateAvailability(bool received,bool owned,bool limitReached)
        {IsReceived=received;HasOwnedAccessory=owned;LimitReached=limitReached;}
    }
    /// <summary>一時点の受領/所持/上限を一度だけ解決する。受領済みの冪等再送と新規受取を区別する。</summary>
    public sealed class CustomizationAcquisitionAvailability
    {
        private readonly Dictionary<string,CustomizationCandidateAvailability> candidates;
        public int ReceivedCount { get; }
        public CustomizationAcquisitionAvailability(CustomizationAcquisitionPlan plan,CustomizationProgress progress)
        {
            if(plan==null||progress==null)throw new ArgumentNullException(plan==null?nameof(plan):nameof(progress));
            var receipts=new HashSet<string>(progress.Receipts.Select(v=>v.RequestId),StringComparer.Ordinal);
            var owned=new HashSet<string>(progress.OwnedAccessoryIds,StringComparer.Ordinal);
            ReceivedCount=plan.Candidates.Count(v=>receipts.Contains(v.RequestId));
            candidates=plan.Candidates.ToDictionary(v=>v.RequestId,v=>new CustomizationCandidateAvailability(
                receipts.Contains(v.RequestId),v.Accessories.Any(owned.Contains),ReceivedCount>=plan.MaximumClaims),StringComparer.Ordinal);
        }
        public CustomizationCandidateAvailability ForCandidate(string requestId)
        {
            if(requestId==null||!candidates.TryGetValue(requestId,out var value))throw new ArgumentException("保存された取得候補が必要です");
            return value;
        }
    }
}
