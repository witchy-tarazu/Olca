using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
namespace Olca.Tests.Battle
{
    public class PortablePostBattleTests
    {
        private sealed class FaultIo:Olca.Unity.CheckpointFileIo
        {
            public bool Fail;public FaultIo(Olca.Unity.ValidationStorageGuard guard):base(guard){}
            public override void Replace(string temp,string destination,string backup){if(Fail)throw new IOException("fixture before replace");base.Replace(temp,destination,backup);}
        }
        public static PortableBattlePackage Package(){var p=PortableProcessingTests.Package();var d=PortableCheckpointJson.Decode(p.CheckpointEnvelope);var defs=p.CreateCheckpointDefinitions();
            var rewards=new[]{"heal","shield","sword"}.Select((id,index)=>defs.CaptureKnownCard(d.RunId+":fixture/reward/v1/"+index,defs.CollectionDefinitions.First(c=>c.Id==id))).ToArray();d.Cards=d.Cards.Concat(rewards).ToArray();d.Battle.RewardOffered=true;d.Battle.RewardCandidates=rewards.Select(c=>c.InstanceId).ToArray();d.Battle.CurrencyAmounts=new[]{7};p.CheckpointEnvelope=PortableCheckpointJson.Encode(d);p.Restore(out _);return p;}
        [TestCase(false)] [TestCase(true)]
        public void 勝利後の受取とスキップを同じ保存経路で一度だけ確定して復帰する(bool receive)
        {
            PortableRunCheckpoint result=null;var p=Package();var source=(byte[])p.CheckpointEnvelope.Clone();var trace=PortablePostBattleReplay.Trace(p,receive,completed:r=>result=r);
            Assert.AreEqual(trace,PortablePostBattleReplay.Trace(p,receive,31,()=>GC.KeepAlive(new int[100])));CollectionAssert.AreEqual(source,p.CheckpointEnvelope);
            var decoded=PortableCheckpointJson.Deserialize<PortableRunCheckpoint>(PortableCheckpointJson.Serialize(result));var restored=decoded.Restore();Assert.AreEqual(CheckpointBoundary.RewardCompleted,restored.Snapshot.Boundary);Assert.AreEqual(receive?11:10,restored.Deck.TotalCount);Assert.AreEqual(receive?7:0,restored.Snapshot.Currency);Assert.AreEqual(receive?1:0,restored.Obtained.Count);Assert.IsTrue(restored.Snapshot.Enemies.Any(e=>e.Hp==0));Assert.IsNotNull(decoded.Resume(out var error),error);
        }
        [Test]
        public void 保存失敗は旧境界と確定前の所持を保ち同じ計画の再試行で一度だけ反映する()
        {
            string root=Path.Combine(Path.GetTempPath(),"olca-post-"+Guid.NewGuid().ToString("N"));var guard=new Olca.Unity.ValidationStorageGuard(root,root+"-mock-default");var io=new FaultIo(guard);Olca.Unity.FileCheckpointStore store=null;
            try{
                var p=Package();var s=new PortableBattleSession(p,defs=>store=new Olca.Unity.FileCheckpointStore(defs,root+"/checkpoint",io));var before=File.ReadAllBytes(store.ActivePath);io.Fail=true;
                foreach(var c in PortableBattleReplay.Commands(p))s.Step(c);Assert.AreEqual(BattleState.Victory,s.Simulation.Battle.State);
                s.Execute(new PostBattleCommand(PostBattleCommandKind.Card,0));s.Execute(new PostBattleCommand(PostBattleCommandKind.Item,receive:true));s.Execute(new PostBattleCommand(PostBattleCommandKind.Currency,receive:true));
                Assert.IsTrue(s.Manager.Dungeon.IsCheckpointBlocked);CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.Throws<InvalidOperationException>(()=>s.CaptureResult());
                int count=s.Manager.Dungeon.GetCurrentDeck().TotalCount;s.Execute(new PostBattleCommand(PostBattleCommandKind.Card,0));Assert.AreEqual(count,s.Manager.Dungeon.GetCurrentDeck().TotalCount);
                io.Fail=false;s.Execute(new PostBattleCommand(PostBattleCommandKind.RetrySave));Assert.IsFalse(s.Manager.Dungeon.IsCheckpointBlocked);var result=s.CaptureResult();Assert.AreEqual(11,result.Restore().Deck.TotalCount);Assert.AreEqual(7,result.Restore().Snapshot.Currency);var bytes=File.ReadAllBytes(store.ActivePath);
                s.Execute(new PostBattleCommand(PostBattleCommandKind.RetrySave));s.Execute(new PostBattleCommand(PostBattleCommandKind.Card,0));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));Assert.AreEqual(result.Canonical(),s.CaptureResult().Canonical());
            }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        [Test]
        public void 戦闘中の中断は途中保存を増やさず結果出力を拒否する()
        {var p=Package();var s=new PortableBattleSession(p);var before=PortableCheckpointJson.Encode(s.Saved);s.Execute(new PostBattleCommand(PostBattleCommandKind.Suspend));Assert.IsTrue(s.Manager.IsSuspended);s.Step(new BattleFrameCommand());CollectionAssert.AreEqual(before,PortableCheckpointJson.Encode(s.Saved));Assert.Throws<InvalidOperationException>(()=>s.CaptureResult());}
        [Test]
        public void 敗北の既存Terminalは保存されるが勝利結果と再開へ変換しない()
        {var s=new PortableBattleSession(Package());s.Simulation.Battle.Player.TakeDamage(10000);Assert.AreEqual(GamePhase.GameOver,s.Manager.CurrentPhase);Assert.AreEqual(CheckpointBoundary.Terminal,s.Saved.Boundary);Assert.AreEqual(0,s.Saved.Player.Hp);Assert.Throws<InvalidOperationException>(()=>s.CaptureResult());}
        [Test]
        public void 結果の版と攻略IDと保存境界の不整合を拒否する()
        {PortableRunCheckpoint r=null;PortablePostBattleReplay.Trace(Package(),false,completed:x=>r=x);r.Version=2;Assert.Catch(()=>r.Restore());r.Version=1;var d=PortableCheckpointJson.Decode(r.ResultEnvelope);d.RunId="another";r.ResultEnvelope=PortableCheckpointJson.Encode(d);Assert.Catch(()=>r.Restore());}
    }
}
