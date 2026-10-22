using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Application.Battle;
using Olca.Tests.Mocks;
namespace Olca.Tests.Battle
{
    public class PortableProductBattleTests
    {
        private static PortableBattlePackage Package(bool legacy=false)
        {
            var factory=new SimpleCardFactory();var deck=legacy?new Deck(new System.Collections.Generic.List<Card>{new Card("heal","ヒール",2,CardRarity.Normal,CardCategory.Defense,ElementType.None,CardSeries.None,20,"HP回復")}):factory.CreateStarterDeck();
            var map=DungeonConfig.CreateTestDungeon(100,30,50);var session=DungeonCheckpointSession.CreateDefault(map,deck);
            _=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),deck,new SeededBattleRandom(1),new MockLogger(),checkpoints:session);
            var departure=session.Store.Read();departure.Enemies.First(e=>e.CellIndex==50).GridX=4;departure.Player.GridX=2;
            var encounter=new CheckpointBattleDto{ContactCell=50,ParticipantCells=new[]{50},GridOriginX=47,HandLimit=10};
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure,encounter,session.Definitions,7,null,c=>"fallback/v1/"+c.Rarity+"/"+c.Id,out var plan,out var error),error);
            var timing=CheckpointBattleFactory.ProductBasicTiming;
            return PortableBattlePackage.Capture(session.Definitions,PortableCheckpointJson.Encode(plan.Snapshot),new PortableBattleRules{ChargeFrames=60,IntervalFrames=12,HitInvincibilityMs=600,StartupFrames=timing.StartupFrames,RecoveryFrames=timing.RecoveryFrames,CancelAfterFrames=timing.CancelAfterFrames,GaugeSpeed=CheckpointBattleFactory.DefaultGaugeSpeed},123);
        }
        private static PortableBattlePackage Copy(PortableBattlePackage p)=>PortableCheckpointJson.Deserialize<PortableBattlePackage>(PortableCheckpointJson.Serialize(p));
        [TestCase(1)] [TestCase(7)] [TestCase(31)] [TestCase(240)]
        public void 正規製品ファクトリーの定義と保存境界はJSON復元と負荷分割で一致する(int batch)
        {
            var p=Package();var bytes=(byte[])p.CheckpointEnvelope.Clone();var restored=p.Restore(out _);var hash=restored.FirstHand.Select(CollectionDisplay.DefinitionHash).ToArray();
            var copy=Copy(p);Assert.AreEqual(PortableBattleReplay.Trace(p),PortableBattleReplay.Trace(copy,batch,()=>{long n=0;for(int i=0;i<2000;i++)n=unchecked(n*33+i);GC.KeepAlive(n);}));
            var simulation=copy.CreateSimulation();CollectionAssert.AreEqual(hash,simulation.Battle.CustomScreen.CurrentHand.Select(CollectionDisplay.DefinitionHash));
            CollectionAssert.AreEqual(bytes,p.CheckpointEnvelope);Assert.AreEqual(restored.Snapshot.Battle.RewardOffered,copy.Restore(out _).Snapshot.Battle.RewardOffered);
            CollectionAssert.AreEqual(restored.Snapshot.Battle.RewardCandidates,copy.Restore(out _).Snapshot.Battle.RewardCandidates);
        }
        [TestCase("version")] [TestCase("missing")] [TestCase("card")] [TestCase("key")] [TestCase("map")] [TestCase("revision")] [TestCase("envelope")]
        public void 版欠損定義不整合は別定義へ差し替えず復帰前に拒否する(string invalid)
        {
            var p=Copy(Package());
            if(invalid=="version")p.Version=999;
            if(invalid=="missing")p.Cards=p.Cards.Skip(1).ToArray();
            if(invalid=="card")p.Cards[0].Power++;
            if(invalid=="key")p.Cards[0].Key="different";
            if(invalid=="map")p.Map.Cells.First(c=>c.Enemy!=null).Enemy.Hp++;
            if(invalid=="revision")p.Revision="other";
            if(invalid=="envelope")p.CheckpointEnvelope[0]=0;
            Assert.Catch(()=>p.CreateSimulation());
        }
        [Test]
        public void 同ID別定義は個体キーとhashで復元しIDだけのDB検索を拒否する()
        {
            var p=Package();var restored=p.Restore(out var db);Assert.IsTrue(p.Cards.Count(c=>c.Id=="sword"&&c.ExceedLevel==ExceedLevel.Normal)>1);
            Assert.Throws<InvalidOperationException>(()=>db.GetCardById("sword"));
            var simulation=p.CreateSimulation();CollectionAssert.AreEqual(restored.FirstHand.Select(CollectionDisplay.DefinitionHash),simulation.Battle.CustomScreen.CurrentHand.Select(CollectionDisplay.DefinitionHash));
        }
        [Test]
        public void 旧保存の省略設定と旧防御定義を遡及上書きせず再開する()
        {
            var p=Copy(Package(true));var restored=p.Restore(out _);var dto=restored.Snapshot;dto.Player.Style=null;dto.Player.Poisons=null;dto.Player.AttackGrowth=null;
            p.CheckpointEnvelope=PortableCheckpointJson.Encode(dto);var simulation=p.CreateSimulation();
            Assert.AreEqual(CardSubCategory.None,simulation.Battle.CustomScreen.CurrentHand[0].SubCategory);Assert.AreEqual(20,simulation.Battle.CustomScreen.CurrentHand[0].Power);
            Assert.AreEqual(CheckpointBoundary.BattleStart,p.Restore(out _).Snapshot.Boundary);
        }
        [Test]
        public void 旧保存にない乱数は明示seedで分離し状態指定時は呼出回数を引き継ぐ()
        {
            var p=Package();var legacy=PortableCheckpointJson.Decode(p.CheckpointEnvelope);legacy.Battle.Random=null;legacy.Battle.RandomVersion=null;p.CheckpointEnvelope=PortableCheckpointJson.Encode(legacy);var random=new SeededBattleRandom(456);for(int i=0;i<7;i++)random.Range(0,13);
            p.RandomState=random.State;p.RandomDraws=random.Draws;var s=p.CreateSimulation();Assert.AreEqual(random.State,s.Random.State);Assert.AreEqual(7,s.Random.Draws,"保存済み初回手札を引き直さない");
            Assert.AreEqual(random.Range(0,100),s.Random.Range(0,100));p.RandomState=0;Assert.Throws<ArgumentException>(()=>p.CreateSimulation());
        }
        [Test]
        public void 既存保存封筒のhash不一致と未対応境界は修復や上書きなしで拒否する()
        {
            var p=Package();var dto=PortableCheckpointJson.Decode(p.CheckpointEnvelope);dto.Battle.DashContact=true;p.CheckpointEnvelope=PortableCheckpointJson.Encode(dto);
            Assert.Catch(()=>p.CreateSimulation());p=Package();dto=PortableCheckpointJson.Decode(p.CheckpointEnvelope);dto.Boundary=CheckpointBoundary.RewardCompleted;dto.Battle=null;p.CheckpointEnvelope=PortableCheckpointJson.Encode(dto);Assert.Catch(()=>p.CreateSimulation());
        }
    }
}
