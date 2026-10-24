using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Application.Battle;
using Olca.Tests.Mocks;
namespace Olca.Tests.Battle
{
    public class PortableFloorRouteTests
    {
        public static PortableBattlePackage Package(int index)
        {
            var deck=new SimpleCardFactory().CreateStarterDeck();
            var maps=new[]{DungeonConfig.CreateTestDungeon(80,30,40),DungeonConfig.CreateTestDungeon(100,30,50)};
            var route=new FloorSequenceDefinitions("two-floor-fixture","v1",maps,deck);
            var session=DungeonCheckpointSession.CreateDefault(maps[index],deck);
            _=new DungeonBattleManager(maps[index],new PlayerCharacter(100),deck,new SeededBattleRandom(1),new MockLogger(),checkpoints:session);
            var departure=session.Store.Read();int cell=index==0?40:50;
            departure.Enemies.First(e=>e.CellIndex==cell).GridX=index==0?3:4;departure.Player.GridX=2;
            var encounter=new CheckpointBattleDto{ContactCell=cell,ParticipantCells=new[]{cell},HandLimit=10,GridOriginX=cell-3};
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure,encounter,session.Definitions,73,null,c=>"fallback/v1/"+c.Rarity+"/"+c.Id,out var plan,out var error),error);
            var envelope=new FloorSequenceCheckpointDto{RouteId=route.RouteId,Revision=route.Revision,RouteHash=route.RouteHash,FloorIndex=index,State=plan.Snapshot};
            var t=CheckpointBattleFactory.ProductBasicTiming;
            return PortableBattlePackage.Capture(route,PortableCheckpointJson.Encode(envelope),new PortableBattleRules{ChargeFrames=60,IntervalFrames=12,HitInvincibilityMs=600,StartupFrames=t.StartupFrames,RecoveryFrames=t.RecoveryFrames,CancelAfterFrames=t.CancelAfterFrames,GaugeSpeed=2},999);
        }
        [TestCase(0)] [TestCase(1)]
        public void 全階層を検証して選択階層の敵配置と乱数を復帰する(int index)
        {
            var p=PortableCheckpointJson.Deserialize<PortableBattlePackage>(PortableCheckpointJson.Serialize(Package(index)));
            var restored=p.Restore(out _);Assert.AreEqual(index==0?40:50,restored.Snapshot.Battle.ContactCell);
            Assert.AreEqual(2,p.FloorRoute.Floors.Length);Assert.IsTrue(p.CreateSimulation().Battle.StartEffects.Applied);
            Assert.AreEqual(PortableBattleReplay.Trace(p),PortableBattleReplay.Trace(p,31,()=>GC.KeepAlive(new int[100])));
        }
        [TestCase("index")] [TestCase("version")] [TestCase("route")] [TestCase("revision")] [TestCase("hash")] [TestCase("state")]
        public void 不正な階層封筒を単階層へfallbackせず拒否する(string mutation)
        {
            var p=Package(1);var e=PortableCheckpointJson.DecodeFloor(p.FloorRoute.CheckpointEnvelope);
            switch(mutation){case "index":e.FloorIndex=0;break;case "version":e.FormatVersion=2;break;case "route":e.RouteId="unknown";break;case "revision":e.Revision="unknown";break;case "hash":e.RouteHash="bad";break;case "state":e.State.Player.Hp--;break;}
            p.FloorRoute.CheckpointEnvelope=PortableCheckpointJson.Encode(e);Assert.Catch(()=>p.CreateSimulation());
        }
        [Test]
        public void 非選択階層の改変と欠損も拒否する()
        {var p=Package(1);p.FloorRoute.Floors[0].MapHash="bad";Assert.Catch(()=>p.CreateSimulation());p=Package(1);p.FloorRoute.Floors[0]=null;Assert.Catch(()=>p.CreateSimulation());}
        [Test]
        public void 再開を反復しても元封筒と乱数状態を変更しない()
        {var p=Package(1);var before=(byte[])p.FloorRoute.CheckpointEnvelope.Clone();var a=PortableBattleReplay.Trace(p);Assert.AreEqual(a,PortableBattleReplay.Trace(p));CollectionAssert.AreEqual(before,p.FloorRoute.CheckpointEnvelope);}
    }
}
