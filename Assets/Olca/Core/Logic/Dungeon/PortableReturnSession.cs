using System;using System.IO;using System.Linq;using Olca.Core.Domain;using Olca.Core.Logic.Integration;
namespace Olca.Core.Logic.Dungeon
{
 public sealed class PortableReturnPackage
 {
  public int Version=1;public string CommitAlgorithm="olca.return-commit.sha256-v1",CommitSeed;
  public PortableBattlePackage Definitions;public byte[] CheckpointEnvelope,FloorEnvelope,InitialProgressEnvelope;public ClearReturnSettings Settings;public RunReturnOutcome Outcome;
  public RestoredCheckpoint Restore(out string dungeonKey)
  {
   if(Version!=1||CommitAlgorithm!="olca.return-commit.sha256-v1"||string.IsNullOrWhiteSpace(CommitSeed)||Definitions==null||CheckpointEnvelope==null||InitialProgressEnvelope==null||Settings==null||!Enum.IsDefined(typeof(RunReturnOutcome),Outcome))throw new InvalidDataException("帰還入力の版または内容が不正です");
   Settings.Validate();PortableCheckpointJson.DecodeProgress(InitialProgressEnvelope);Definitions.Restore(out _,out var defs);dungeonKey=defs.DungeonId;
   var dto=PortableCheckpointJson.Decode(CheckpointEnvelope);
   if(Definitions.FloorRoute!=null){
    if(FloorEnvelope==null)throw new InvalidDataException("階層封筒が欠損しています");var floors=Definitions.FloorRoute.CreateDefinitions();var e=PortableCheckpointJson.DecodeFloor(FloorEnvelope);
    if(!floors.TryRestore(e,out var restoredFloor,out var error))throw new InvalidDataException(error);
    if(!PortableCheckpointJson.Encode(restoredFloor.State.Snapshot).SequenceEqual(CheckpointEnvelope)||(Outcome==RunReturnOutcome.Cleared&&e.FloorIndex!=floors.FloorCount-1))throw new InvalidDataException("最終階層または帰還状態が不一致です");
    defs=floors.Floor(e.FloorIndex);dungeonKey=floors.RouteId;
   }else if(FloorEnvelope!=null)throw new InvalidDataException("単階層に階層封筒は指定できません");
   if(!CheckpointCodec.TryRestore(dto,defs,out var source,out var failure))throw new InvalidDataException(failure);
   ClearReturnSession.ValidateSource(source,dungeonKey,Settings,Outcome);
   return source;
  }
 }
 public sealed class MemoryClearReturnStore : IClearReturnStore
 {
  private ClearReturnProfile current;
  public MemoryClearReturnStore(ClearReturnProfile initial){ClearReturnProfile.Validate(initial);current=initial.Copy();}
  public ClearReturnProfile Read(out string error){error=null;return current.Copy();}
  public bool TryWrite(long expected,ClearReturnProfile next,out string error)
  {error=null;ClearReturnProfile.Validate(next);if(PortableCheckpointJson.Hash(PortableCheckpointJson.Encode(current))==PortableCheckpointJson.Hash(PortableCheckpointJson.Encode(next)))return true;if(current.Revision!=expected||next.Revision!=expected+1){error="進行保存の世代が競合しています";return false;}current=next.Copy();return true;}
 }
 public enum ReturnCommandKind { BeginClear,Toggle,ConfirmSelection,ConfirmReturn,Pause,RequestRetreat,CancelRetreat,ConfirmRetreat,EndDefeat,Retry,Reload }
 public sealed class ReturnCommand
 {
  public ReturnCommandKind Kind {get;}public int Index {get;}public bool Paused {get;}
  public ReturnCommand(ReturnCommandKind kind,int index=0,bool paused=true){Kind=kind;Index=index;Paused=paused;}
 }
 /// <summary>Controllerの帰還入力を既存ClearReturnSessionへ渡す。報酬／損失計算を複製しない。</summary>
 public sealed class PortableReturnSession
 {
  private readonly PortableReturnPackage input;private readonly RestoredCheckpoint source;private readonly string dungeonKey;private readonly IClearReturnStore store;
  private ClearReturnSession progress;private DungeonBattleManager manager;public bool RetreatConfirmation {get;private set;}
  public long OperationIndex {get;private set;}public ClearReturnProfile Snapshot=>progress.Snapshot;public bool HasPendingSave=>progress.HasPendingSave;
  public PlayerCharacter ReturnedPlayer=>progress.ReturnedPlayer;public bool RunClosed=>Snapshot.Receipts.Any(r=>r.RunId==source.Snapshot.RunId);
  public bool CanResumeSavedRun=>manager!=null&&!manager.IsSuspended&&!RunClosed&&!HasPendingSave&&(Snapshot.Flow==null||Snapshot.Flow.Stage==ClearReturnStage.Returned);
  public PortableReturnSession(PortableReturnPackage package,Func<IClearReturnStore> openStore=null)
  {
   input=PortableCheckpointJson.Deserialize<PortableReturnPackage>(PortableCheckpointJson.Serialize(package));source=input.Restore(out dungeonKey);store=openStore?.Invoke()??new MemoryClearReturnStore(PortableCheckpointJson.DecodeProgress(input.InitialProgressEnvelope));CreateProgress();
   if(input.Outcome==RunReturnOutcome.Retreated){
    input.Definitions.Restore(out var catalog,out var defs);bool hasNext=false;
    if(input.Definitions.FloorRoute!=null){var floors=input.Definitions.FloorRoute.CreateDefinitions();var e=PortableCheckpointJson.DecodeFloor(input.FloorEnvelope);defs=floors.Floor(e.FloorIndex);hasNext=e.FloorIndex+1<floors.FloorCount;catalog=new PortableCardCatalog(defs.PortableCards.Values);}
    var checkpoint=new MemoryCheckpointStore(defs);if(!checkpoint.TryWrite(source.Snapshot,out var error))throw new InvalidDataException(error);var processing=input.Definitions.Processing?.Restore();
    if(!DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(defs,checkpoint),new Olca.Core.Logic.Battle.SeededBattleRandom(input.Definitions.ReplaySeed),PortableBattleSession.CreateLogger(),out manager,out error,processing?.Exceed,processing?.Tuning,processing?.Fusion,catalog,hasNext))throw new InvalidDataException(error);
   }
  }
  private void CreateProgress()
  {
   var processing=input.Definitions.Processing?.Restore();var cards=input.Definitions.Cards.Select(c=>c.Restore()).Concat(input.Definitions.Processing?.Cards.Select(c=>c.Restore())??Array.Empty<Card>()).GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First()).ToArray();
   progress=new ClearReturnSession(store,processing?.CollectionRecipes,cards,()=>CheckpointValues.Hash(w=>{w.Write(input.CommitAlgorithm);w.Write(input.CommitSeed);w.Write(source.Snapshot.RunId);w.Write(progress.Snapshot.Revision+1);}).Substring(0,32).ToLowerInvariant());
   if(!progress.Ready)throw new InvalidDataException(progress.Error);
  }
  public string Execute(ReturnCommand command)
  {
   OperationIndex++;bool accepted=false;
   if(command==null)return Observe("invalid");
   bool matching=Snapshot.Flow?.RunId==source.Snapshot.RunId;
   switch(command.Kind){
    case ReturnCommandKind.BeginClear:if(input.Outcome==RunReturnOutcome.Cleared)accepted=progress.Begin(source,dungeonKey,input.Settings);break;
    case ReturnCommandKind.Toggle:if(matching)accepted=progress.Toggle(command.Index);break;
    case ReturnCommandKind.ConfirmSelection:if(matching)accepted=progress.ConfirmSelection();break;
    case ReturnCommandKind.ConfirmReturn:if(matching)accepted=progress.ConfirmReturn();break;
    case ReturnCommandKind.EndDefeat:if(input.Outcome==RunReturnOutcome.Defeated)accepted=progress.EndWithoutRewards(source,dungeonKey,input.Settings,input.Outcome);break;
    case ReturnCommandKind.Pause:if(manager!=null&&!manager.IsSuspended&&!RunClosed&&!RetreatConfirmation&&!HasPendingSave){manager.SetUserPaused(command.Paused);accepted=true;}break;
    case ReturnCommandKind.RequestRetreat:if(manager!=null&&manager.IsUserPaused&&!manager.IsSuspended&&!RunClosed&&!HasPendingSave){RetreatConfirmation=true;accepted=true;}break;
    case ReturnCommandKind.CancelRetreat:if(RetreatConfirmation){RetreatConfirmation=false;accepted=true;}break;
    case ReturnCommandKind.ConfirmRetreat:
     if(manager!=null&&manager.IsUserPaused&&RetreatConfirmation&&!RunClosed&&!HasPendingSave&&manager.SuspendForCheckpoint()){
      RetreatConfirmation=false;manager.SetUserPaused(false);manager.Dungeon.DiscardRunCurrency();accepted=progress.EndWithoutRewards(source,dungeonKey,input.Settings,RunReturnOutcome.Retreated);
     }break;
    case ReturnCommandKind.Retry:accepted=progress.Retry();break;
    case ReturnCommandKind.Reload:if(!HasPendingSave){CreateProgress();accepted=true;}break;
   }
   return Observe(command.Kind+":"+(accepted?"accepted":"rejected"));
  }
  public string Observe(string eventText="")
  {var p=Snapshot;var player=ReturnedPlayer;return string.Join("|",OperationIndex,p.Revision,p.Flow?.Stage,p.Flow?.Outcome,p.Experience,p.Level,p.Tokens,p.Owned.Sum(c=>c.Count),p.Receipts.Length,HasPendingSave,RetreatConfirmation,manager?.IsUserPaused??false,RunClosed,player?.CurrentHp??0,player?.MaxHp??0,PortableCheckpointJson.Hash(PortableCheckpointJson.Encode(p)),eventText);}
  public PortableReturnResult CaptureResult()
  {if(HasPendingSave||Snapshot.Flow?.RunId!=source.Snapshot.RunId||Snapshot.Flow.Stage!=ClearReturnStage.Returned||!RunClosed)throw new InvalidOperationException("帰還確定と保存が完了していません");var result=new PortableReturnResult{Input=PortableCheckpointJson.Deserialize<PortableReturnPackage>(PortableCheckpointJson.Serialize(input)),ProgressEnvelope=PortableCheckpointJson.Encode(Snapshot)};result.Restore();return result;}
 }
 public sealed class PortableReturnResult
 {
  public int Version=1;public PortableReturnPackage Input;public byte[] ProgressEnvelope;
  public ClearReturnProfile Restore()
  {if(Version!=1||Input==null||ProgressEnvelope==null)throw new InvalidDataException("帰還結果の版または内容が不正です");var source=Input.Restore(out _);var p=PortableCheckpointJson.DecodeProgress(ProgressEnvelope);if(p.Flow?.RunId!=source.Snapshot.RunId||p.Flow.Stage!=ClearReturnStage.Returned||p.Flow.Outcome!=Input.Outcome||p.Receipts.Count(r=>r.RunId==source.Snapshot.RunId)!=1)throw new InvalidDataException("帰還結果と受領台帳が一致しません");
   var processing=Input.Definitions.Processing?.Restore();var verifier=new ClearReturnSession(new MemoryClearReturnStore(p),processing?.CollectionRecipes,Input.Definitions.Cards.Select(c=>c.Restore()).ToArray());
   Input.Restore(out var key);bool same=Input.Outcome==RunReturnOutcome.Cleared?verifier.Begin(source,key,Input.Settings):verifier.EndWithoutRewards(source,key,Input.Settings,Input.Outcome);
   if(!same)throw new InvalidDataException(verifier.Error);return p;}
  public string Canonical(){var p=Restore();return string.Join("|",p.Revision,p.Flow.Outcome,p.Experience,p.Level,p.Tokens,p.Owned.Sum(c=>c.Count),p.Receipts.Length,p.ReturnedHp,PortableCheckpointJson.Hash(ProgressEnvelope));}
 }
}
