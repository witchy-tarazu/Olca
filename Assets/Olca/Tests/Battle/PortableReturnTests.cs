using System;using System.IO;using System.Linq;using NUnit.Framework;using Olca.Core.Domain;using Olca.Core.Logic.Battle;using Olca.Core.Logic.Dungeon;
namespace Olca.Tests.Battle
{
 public class PortableReturnTests
 {
  public static PortableReturnPackage Package(RunReturnOutcome outcome=RunReturnOutcome.Cleared,bool overflow=false)
  {
   var battle=PortableFloorSaveTests.Package(final:true);PortableRunCheckpoint result=null;
   if(outcome==RunReturnOutcome.Cleared)PortablePostBattleReplay.Trace(battle,true,completed:r=>result=r);
   var state=outcome==RunReturnOutcome.Cleared?PortableCheckpointJson.Decode(result.ResultEnvelope):PortableCheckpointJson.Decode(battle.CheckpointEnvelope);
   var floor=PortableCheckpointJson.DecodeFloor(outcome==RunReturnOutcome.Cleared?result.FloorEnvelope:battle.FloorRoute.CheckpointEnvelope);
   if(outcome==RunReturnOutcome.Defeated){state.Boundary=CheckpointBoundary.Terminal;state.Player.Hp=0;state.Battle=null;}
   if(outcome!=RunReturnOutcome.Cleared){var record=battle.CreateCheckpointDefinitions().CaptureKnownCard(state.RunId+":fixture/discarded",new Olca.Application.Battle.SimpleCardFactory().CreateById("heal"));state.Cards=state.Cards.Concat(new[]{record}).ToArray();state.ObtainedOrder=new[]{record.InstanceId};}
   if(state.Battle==null)state.Cards=state.Cards.Where(c=>state.DeckOrder.Contains(c.InstanceId)||state.ObtainedOrder.Contains(c.InstanceId)).ToArray();
   floor.State=state;
   var profile=new ClearReturnProfile{Experience=400,Level=5,Tokens=17};
   if(outcome!=RunReturnOutcome.Cleared){var owned=new Olca.Application.Battle.SimpleCardFactory().CreateById("sword");profile.Owned=new[]{new OwnedCardCount{Id=owned.Id,Name=owned.Name,Level=owned.ExceedLevel,Rarity=owned.Rarity,DefinitionHash=CollectionDisplay.DefinitionHash(owned),Count=2}};}
   if(overflow){var card=CheckpointCodec.TryRestore(state,battle.CreateCheckpointDefinitions(),out var restored,out var error)?restored.Obtained.First():throw new Exception(error);profile.Owned=new[]{new OwnedCardCount{Id=card.Id,Name=card.Name,Level=card.ExceedLevel,Rarity=card.Rarity,DefinitionHash=CollectionDisplay.DefinitionHash(card),Count=4}};}
   var package=new PortableReturnPackage{Definitions=battle,CheckpointEnvelope=PortableCheckpointJson.Encode(state),FloorEnvelope=PortableCheckpointJson.Encode(floor),InitialProgressEnvelope=PortableCheckpointJson.Encode(profile),Settings=new ClearReturnSettings(),Outcome=outcome,CommitSeed="fixture/return/seed-v1"};package.Restore(out _);return package;
  }
  [TestCase(false)] [TestCase(true)]
  public void 成功持帰りと経験値は一度だけ確定し上限超過だけトークンになる(bool overflow)
  {
   var p=Package(overflow:overflow);PortableReturnResult result=null;var trace=PortableReturnReplay.Trace(p,completed:r=>result=r);Assert.AreEqual(trace,PortableReturnReplay.Trace(p,31,()=>GC.KeepAlive(new int[100])));var profile=result.Restore();Assert.AreEqual(500,profile.Experience);Assert.AreEqual(6,profile.Level);Assert.AreEqual(1,profile.Receipts.Length);Assert.AreEqual(125,profile.ReturnedHp);
   Assert.AreEqual(overflow?4:1,profile.Owned.Sum(c=>c.Count));Assert.AreEqual(overflow?17+profile.Flow.TokenAward:17,profile.Tokens);Assert.AreEqual(overflow,profile.Flow.TokenAward>0);
  }
  [TestCase(RunReturnOutcome.Defeated)] [TestCase(RunReturnOutcome.Retreated)]
  public void 敗北と撤退は所持成長通貨を保持し無報酬台帳だけ一度記録する(RunReturnOutcome outcome)
  {var p=Package(outcome);PortableReturnResult r=null;PortableReturnReplay.Trace(p,completed:x=>r=x);var result=r.Restore();Assert.AreEqual(400,result.Experience);Assert.AreEqual(5,result.Level);Assert.AreEqual(17,result.Tokens);Assert.AreEqual(2,result.Owned.Sum(c=>c.Count));Assert.AreEqual(0,result.Flow.Candidates.Length);Assert.AreEqual(0,result.ClearedDungeons.Length);Assert.AreEqual(1,result.Receipts.Length);Assert.AreEqual(120,result.ReturnedHp);Assert.AreEqual(outcome,result.Receipts[0].Outcome);}
  private sealed class FaultIo:Olca.Unity.CheckpointFileIo
  {public bool Fail,After;public FaultIo(Olca.Unity.ValidationStorageGuard g):base(g){}public override void Replace(string t,string d,string b){if(Fail&&!After)throw new IOException("fixture before progress replace");base.Replace(t,d,b);if(Fail&&After)throw new IOException("fixture lost response");}}
  [Test]
  public void 保存失敗は旧進行と同じ計画を保ち再試行再読込でも二重付与しない()
  {
   string root=Path.Combine(Path.GetTempPath(),"olca-return-"+Guid.NewGuid().ToString("N"));var io=new FaultIo(new Olca.Unity.ValidationStorageGuard(root,root+"-mock-default"));var p=Package();
   try{io.CreateDirectory(root);io.WriteAndFlush(root+"/progress.json",p.InitialProgressEnvelope);var store=new Olca.Unity.ClearReturnFileStore(root,io);var s=new PortableReturnSession(p,()=>store);s.Execute(new ReturnCommand(ReturnCommandKind.BeginClear));s.Execute(new ReturnCommand(ReturnCommandKind.Toggle));var before=File.ReadAllBytes(store.ActivePath);io.Fail=true;s.Execute(new ReturnCommand(ReturnCommandKind.ConfirmSelection));Assert.IsTrue(s.HasPendingSave);Assert.AreEqual(400,s.Snapshot.Experience);CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));
    s.Execute(new ReturnCommand(ReturnCommandKind.ConfirmSelection));io.Fail=false;s.Execute(new ReturnCommand(ReturnCommandKind.Retry));Assert.AreEqual(500,s.Snapshot.Experience);s=new PortableReturnSession(p,()=>store);s.Execute(new ReturnCommand(ReturnCommandKind.ConfirmSelection));s.Execute(new ReturnCommand(ReturnCommandKind.ConfirmReturn));s.Execute(new ReturnCommand(ReturnCommandKind.BeginClear));Assert.AreEqual(500,s.Snapshot.Experience);Assert.AreEqual(1,s.Snapshot.Receipts.Length);Assert.IsFalse(s.CanResumeSavedRun);
   }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  }
  [Test]
  public void 置換後の応答不明も同一文書の確認で二重保存しない()
  {
   string root=Path.Combine(Path.GetTempPath(),"olca-return-after-"+Guid.NewGuid().ToString("N"));var io=new FaultIo(new Olca.Unity.ValidationStorageGuard(root,root+"-mock-default")){Fail=true,After=true};var p=Package(RunReturnOutcome.Defeated);
   try{io.CreateDirectory(root);io.WriteAndFlush(root+"/progress.json",p.InitialProgressEnvelope);var s=new PortableReturnSession(p,()=>new Olca.Unity.ClearReturnFileStore(root,io));s.Execute(new ReturnCommand(ReturnCommandKind.EndDefeat));Assert.IsFalse(s.HasPendingSave);Assert.AreEqual(1,s.Snapshot.Receipts.Length);s.Execute(new ReturnCommand(ReturnCommandKind.EndDefeat));Assert.AreEqual(1,s.Snapshot.Receipts.Length);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  }
  [Test]
  public void 撤退取消はポーズを保ち進行を書き換えず未確認確定を拒否する()
  {var p=Package(RunReturnOutcome.Retreated);var s=new PortableReturnSession(p);var before=PortableCheckpointJson.Encode(s.Snapshot);s.Execute(new ReturnCommand(ReturnCommandKind.ConfirmRetreat));s.Execute(new ReturnCommand(ReturnCommandKind.Pause));s.Execute(new ReturnCommand(ReturnCommandKind.RequestRetreat));s.Execute(new ReturnCommand(ReturnCommandKind.CancelRetreat));Assert.IsFalse(s.RetreatConfirmation);CollectionAssert.AreEqual(before,PortableCheckpointJson.Encode(s.Snapshot));Assert.IsFalse(s.RunClosed);StringAssert.Contains("|True|",s.Observe());}
  [Test]
  public void 帰還結果は同じ攻略IDでも変更された境界を受領済みとして偽装しない()
  {PortableReturnResult r=null;PortableReturnReplay.Trace(Package(),completed:x=>r=x);var d=PortableCheckpointJson.Decode(r.Input.CheckpointEnvelope);d.Sequence++;r.Input.CheckpointEnvelope=PortableCheckpointJson.Encode(d);var f=PortableCheckpointJson.DecodeFloor(r.Input.FloorEnvelope);f.State=d;r.Input.FloorEnvelope=PortableCheckpointJson.Encode(f);Assert.Catch(()=>r.Restore());}
  [Test]
  public void 途中階層クリアと破損進行と未知版は元保存を変更せず拒否する()
  {var p=Package();p.Version=2;Assert.Catch(()=>new PortableReturnSession(p));p=Package();p.InitialProgressEnvelope=new byte[]{1,2};Assert.Catch(()=>new PortableReturnSession(p));p=Package();var e=PortableCheckpointJson.DecodeFloor(p.FloorEnvelope);e.FloorIndex=0;p.FloorEnvelope=PortableCheckpointJson.Encode(e);Assert.Catch(()=>new PortableReturnSession(p));}
 }
}
