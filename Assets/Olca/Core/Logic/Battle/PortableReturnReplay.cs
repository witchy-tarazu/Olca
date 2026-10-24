using System;using System.Text;using System.Linq;using Olca.Core.Domain;using Olca.Core.Logic.Dungeon;
namespace Olca.Core.Logic.Battle
{
 public static class PortableReturnReplay
 {
  public static string Trace(PortableReturnPackage input,int batch=1,Action work=null,Func<IClearReturnStore> openStore=null,Action<PortableReturnResult> completed=null)
  {
   if(batch<1)throw new ArgumentOutOfRangeException(nameof(batch));var s=new PortableReturnSession(input,openStore);var text=new StringBuilder(s.Observe()).Append('\n');
   void Add(ReturnCommandKind kind,int index=0,bool pause=true){if(s.OperationIndex%batch==0)work?.Invoke();text.Append(s.Execute(new ReturnCommand(kind,index,pause))).Append('\n');}
   if(input.Outcome==RunReturnOutcome.Cleared){Add(ReturnCommandKind.BeginClear);Add(ReturnCommandKind.BeginClear);
    if(s.Snapshot.Flow?.Stage==ClearReturnStage.Choosing&&s.Snapshot.Flow.Candidates.Length>0){Add(ReturnCommandKind.Toggle);Add(ReturnCommandKind.Toggle);Add(ReturnCommandKind.Toggle);}
    Add(ReturnCommandKind.Reload);Add(ReturnCommandKind.ConfirmSelection);Add(ReturnCommandKind.ConfirmSelection);Add(ReturnCommandKind.Reload);Add(ReturnCommandKind.ConfirmReturn);Add(ReturnCommandKind.ConfirmReturn);Add(ReturnCommandKind.BeginClear);
   }else if(input.Outcome==RunReturnOutcome.Defeated){Add(ReturnCommandKind.EndDefeat);Add(ReturnCommandKind.EndDefeat);Add(ReturnCommandKind.Reload);Add(ReturnCommandKind.EndDefeat);}
   else{Add(ReturnCommandKind.RequestRetreat);Add(ReturnCommandKind.Pause);Add(ReturnCommandKind.RequestRetreat);Add(ReturnCommandKind.CancelRetreat);Add(ReturnCommandKind.RequestRetreat);Add(ReturnCommandKind.ConfirmRetreat);Add(ReturnCommandKind.ConfirmRetreat);Add(ReturnCommandKind.Reload);Add(ReturnCommandKind.RequestRetreat);}
   var result=s.CaptureResult();text.Append(result.Canonical()).Append('\n');completed?.Invoke(result);return text.ToString();
  }
 }
}
