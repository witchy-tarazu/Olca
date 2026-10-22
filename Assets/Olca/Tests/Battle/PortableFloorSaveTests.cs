using System;using System.IO;using System.Linq;using NUnit.Framework;
using Olca.Core.Domain;using Olca.Core.Logic.Battle;using Olca.Core.Logic.Dungeon;using Olca.Core.Logic.Integration;using Olca.Application.Battle;using Olca.Tests.Mocks;
namespace Olca.Tests.Battle
{
 public class PortableFloorSaveTests
 {
  public static PortableBattlePackage Package(bool boss=true,bool final=false)
  {
   var factory=new SimpleCardFactory();var deck=factory.CreateStarterDeck();var maps=new[]{DungeonConfig.CreateTestDungeon(80,30,40),DungeonConfig.CreateTestDungeon(100,30,50)};
   if(!boss)maps[0].MapCells[79]=new MapCell(79);
   var definitions=new FloorSequenceDefinitions("floor-save-fixture","v1",maps,deck);int index=final?1:0;var session=DungeonCheckpointSession.CreateDefault(maps[index],deck);
   _=new DungeonBattleManager(maps[index],new PlayerCharacter(100),deck,new SeededBattleRandom(1),new MockLogger(),checkpoints:session,hasNextFloor:!final);
   var d=session.Store.Read();int cell=final?99:boss?79:40;foreach(var e in d.Enemies){e.Hp=e.CellIndex==cell?50:0;if(e.CellIndex==cell)e.GridX=4;}
   d.PositionMilli=(cell-1)*1000;d.Player.GridX=2;
   var encounter=new CheckpointBattleDto{ContactCell=cell,ParticipantCells=new[]{cell},HandLimit=10,GridOriginX=cell-3,CurrencyAmounts=new[]{7}};
   Assert.IsTrue(CheckpointBattlePlan.TryCreate(d,encounter,session.Definitions,73,null,c=>"fallback/v1/"+c.Rarity+"/"+c.Id,out var plan,out var error,!final),error);d=plan.Snapshot;
   if(!d.Battle.RewardOffered){var rewards=new[]{"heal","shield","sword"}.Select((id,n)=>session.Definitions.CaptureKnownCard(d.RunId+":fixture/reward/"+n,factory.CreateById(id))).ToArray();d.Cards=d.Cards.Concat(rewards).ToArray();d.Battle.RewardOffered=true;d.Battle.RewardCandidates=rewards.Select(c=>c.InstanceId).ToArray();}
   var envelope=new FloorSequenceCheckpointDto{RouteId=definitions.RouteId,Revision=definitions.Revision,RouteHash=definitions.RouteHash,FloorIndex=index,State=d};var t=CheckpointBattleFactory.ProductBasicTiming;
   return PortableBattlePackage.Capture(definitions,PortableCheckpointJson.Encode(envelope),new PortableBattleRules{ExplorationNormalSpeedMilliPerSecond=8000,ChargeFrames=60,IntervalFrames=12,HitInvincibilityMs=600,StartupFrames=t.StartupFrames,RecoveryFrames=t.RecoveryFrames,CancelAfterFrames=t.CancelAfterFrames,GaugeSpeed=2},999);
  }
  [TestCase(false)] [TestCase(true)]
  public void 中間ボス報酬後に次階層開始を保存しHPデッキ履歴通貨を一度だけ引き継ぐ(bool receive)
  {
   var p=Package();var source=(byte[])p.FloorRoute.CheckpointEnvelope.Clone();PortableRunCheckpoint result=null;
   var trace=PortablePostBattleReplay.Trace(p,receive,completed:r=>result=r,finishFloor:true);Assert.AreEqual(trace,PortablePostBattleReplay.Trace(p,receive,31,()=>GC.KeepAlive(new int[100]),finishFloor:true));
   var e=PortableCheckpointJson.DecodeFloor(result.FloorEnvelope);Assert.AreEqual(1,e.FloorIndex);Assert.AreEqual(CheckpointBoundary.Departure,e.State.Boundary);Assert.AreEqual(0,e.State.PositionMilli);Assert.AreEqual(receive?7:0,e.State.Currency);Assert.AreEqual(receive?21:20,e.State.DeckOrder.Length);Assert.AreEqual(receive?1:0,e.State.ObtainedOrder.Length);
   Assert.AreEqual(50,e.State.Enemies.First(x=>x.CellIndex==50).Hp);Assert.AreEqual(200,e.State.Enemies.First(x=>x.CellIndex==99).Hp);CollectionAssert.AreEqual(source,p.FloorRoute.CheckpointEnvelope);
   var manager=result.Resume(out var error);Assert.IsNotNull(manager,error);manager.ActivateCheckpoint();Assert.AreEqual(e.State.Player.Hp,manager.Dungeon.Player.CurrentHp);Assert.AreEqual(0,manager.Dungeon.PlayerPosition);
  }
  [Test]
  public void ボスなし終端は通常の整数フレームで次階層開始を保存する()
  {var p=Package(false);PortableRunCheckpoint r=null;PortablePostBattleReplay.Trace(p,true,completed:x=>r=x,finishFloor:true);Assert.AreEqual(1,PortableCheckpointJson.DecodeFloor(r.FloorEnvelope).FloorIndex);Assert.AreEqual(7,r.Restore().Snapshot.Currency);}
  private sealed class FaultIo:Olca.Unity.CheckpointFileIo
  {public bool Fail;public FaultIo(Olca.Unity.ValidationStorageGuard guard):base(guard){}public override void Replace(string t,string d,string b){if(Fail)throw new IOException("fixture floor replace");base.Replace(t,d,b);}}
  [Test]
  public void 階層保存失敗は旧階層を維持し再試行後に一度だけManagerを交換する()
  {
   string root=Path.Combine(Path.GetTempPath(),"olca-portable-floor-"+Guid.NewGuid().ToString("N"));var io=new FaultIo(new Olca.Unity.ValidationStorageGuard(root,root+"-mock-default"));Olca.Unity.FloorSequenceFileStore store=null;
   try{var p=Package();var s=new PortableBattleSession(p,openFloorStore:defs=>store=new Olca.Unity.FloorSequenceFileStore(defs,root+"/checkpoint",io));var before=File.ReadAllBytes(store.ActivePath);io.Fail=true;
    foreach(var c in PortableBattleReplay.Commands(p))s.Step(c);Assert.AreEqual(BattleState.Victory,s.Simulation.Battle.State);
    s.Execute(new PostBattleCommand(PostBattleCommandKind.Card,0));s.Execute(new PostBattleCommand(PostBattleCommandKind.Item,receive:true));s.Execute(new PostBattleCommand(PostBattleCommandKind.Currency,receive:true));Assert.IsTrue(s.Manager.Dungeon.IsCheckpointBlocked);Assert.AreEqual(0,s.CurrentFloorIndex);CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));
    io.Fail=false;s.Execute(new PostBattleCommand(PostBattleCommandKind.RetrySave));Assert.AreEqual(1,s.CurrentFloorIndex);Assert.AreEqual(GamePhase.Dungeon,s.Manager.CurrentPhase);var saved=File.ReadAllBytes(store.ActivePath);
    s.Execute(new PostBattleCommand(PostBattleCommandKind.RetrySave));s.Execute(new PostBattleCommand(PostBattleCommandKind.Currency,receive:true));CollectionAssert.AreEqual(saved,File.ReadAllBytes(store.ActivePath));Assert.AreEqual(7,s.CaptureResult().Restore().Snapshot.Currency);
   }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  }
  [Test]
  public void 最終ボスTerminalを次階層や通常再開へ変換しない()
  {PortableRunCheckpoint r=null;PortablePostBattleReplay.Trace(Package(final:true),true,completed:x=>r=x);Assert.AreEqual(CheckpointBoundary.Terminal,r.Restore().Snapshot.Boundary);Assert.AreEqual(1,PortableCheckpointJson.DecodeFloor(r.FloorEnvelope).FloorIndex);Assert.IsNull(r.Resume(out _));}
  [Test]
  public void 敗北は現在階層Terminalに留まり勝利結果を出さない()
  {var s=new PortableBattleSession(Package());s.Simulation.Battle.Player.TakeDamage(10000);Assert.AreEqual(0,s.CurrentFloorIndex);Assert.AreEqual(CheckpointBoundary.Terminal,s.Saved.Boundary);Assert.Throws<InvalidOperationException>(()=>s.CaptureResult());}
  [Test]
  public void 探索設定のない旧パッケージで終端進行を既定速度へfallbackしない()
  {var p=Package(false);p.Rules.ExplorationNormalSpeedMilliPerSecond=null;var s=new PortableBattleSession(p);foreach(var c in PortableBattleReplay.Commands(p))s.Step(c);s.Execute(new PostBattleCommand(PostBattleCommandKind.Card,-1));s.Execute(new PostBattleCommand(PostBattleCommandKind.Item));s.Execute(new PostBattleCommand(PostBattleCommandKind.Currency));Assert.Throws<NotSupportedException>(()=>s.Execute(new PostBattleCommand(PostBattleCommandKind.ExplorationFrame)));Assert.AreEqual(0,s.CurrentFloorIndex);}
  [Test]
  public void 結果の階層封筒欠損や選択状態不一致を拒否する()
  {PortableRunCheckpoint r=null;PortablePostBattleReplay.Trace(Package(),true,completed:x=>r=x);var bytes=r.FloorEnvelope;r.FloorEnvelope=null;Assert.Catch(()=>r.Restore());r.FloorEnvelope=bytes;var d=PortableCheckpointJson.DecodeFloor(bytes);d.FloorIndex=0;r.FloorEnvelope=PortableCheckpointJson.Encode(d);Assert.Catch(()=>r.Restore());}
 }
}
