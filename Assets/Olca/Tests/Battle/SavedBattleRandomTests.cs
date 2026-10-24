using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Application.Battle;
using Olca.Tests.Mocks;
namespace Olca.Tests.Battle
{
    public class SavedBattleRandomTests
    {
        private sealed class CountRandom:IRandomProvider
        { public int Calls; public int Range(int min,int max){Calls++;return min;}public float Range(float min,float max){Calls++;return min;}public float Value {get{Calls++;return 0;}} }
        private static CheckpointDto Plan(out CheckpointDefinitions defs)
        {
            var f=new SimpleCardFactory();var deck=f.CreateStarterDeck();var map=DungeonConfig.CreateTestDungeon(100,30,50);
            var session=DungeonCheckpointSession.CreateDefault(map,deck);defs=session.Definitions;
            _=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new SeededBattleRandom(1),new MockLogger(),checkpoints:session);
            var d=session.Store.Read();d.Enemies.First(e=>e.CellIndex==50).GridX=3;d.Player.GridX=2;
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(d,new CheckpointBattleDto{ContactCell=50,ParticipantCells=new[]{50},HandLimit=10,GridOriginX=47},defs,73,null,
                c=>"fallback/v1/"+c.Rarity+"/"+c.Id,out var plan,out var error),error);return plan.Snapshot;
        }
        private static BattleSystem Battle(CheckpointDto dto,CheckpointDefinitions defs,IRandomProvider fallback)
        {
            Assert.IsTrue(CheckpointCodec.TryRestore(dto,defs,out var r,out var error),error);
            return CheckpointBattleFactory.Create(r.Player,new DungeonMap(r.Map),r.Deck,r.Snapshot,r.FirstHand,fallback);
        }
        private static string Continue(BattleSystem b,IRandomProvider external)
        {
            b.CustomGauge.SetGaugeMs(CustomGaugeSystem.MAX_GAUGE_MS);b.OpenCustomScreen(external);
            foreach(var c in b.CustomScreen.CurrentHand.Take(3).ToArray())b.CustomScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Select,b.CustomScreen.Commands.HandleOf(c)));
            b.CustomScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Confirm));b.CustomScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.ReturnToBattle));
            for(int i=0;i<600;i++)b.TickFrame(16,new LogicalInput{Horizontal=i<36?1:0,HorizontalDown=i==0?1:0});
            Assert.IsTrue(b.CustomGauge.IsFull);var before=b.CheckpointRandomState?.Draws;
            b.CustomScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Open),external);
            if(before.HasValue){Assert.AreEqual(0,before.Value,"既存AIは優先順評価で乱数を消費しない");Assert.Less(b.Player.CurrentHp,b.Player.MaxHp,"隣接fixtureで敵攻撃が実際に発生した");Assert.Greater(b.CheckpointRandomState.Draws,before.Value,"追加ドローが同じ所有乱数を消費する");}
            return b.Player.CurrentHp+"|"+b.LogicalFrame+"|"+b.CustomScreen.Commands.Observe().Canonical+"|"+(b.CheckpointRandomState==null?"legacy":Encoding.UTF8.GetString(PortableCheckpointJson.Serialize(b.CheckpointRandomState)));
        }
        [Test]
        public void 新規開始保存を復帰すると外部乱数に依存せず敵行動と追加ドローの順が一致する()
        {
            var dto=Plan(out var defs);Assert.AreEqual(1,dto.Battle.RandomVersion);dto.Battle.Random.Validate();Assert.AreEqual(73,dto.Battle.Random.Seed);Assert.AreEqual(0,dto.Battle.Random.Draws);
            var json=PortableCheckpointJson.Encode(dto);var copy=PortableCheckpointJson.Decode(json);var fallbackA=new CountRandom();var fallbackB=new CountRandom();
            var a=Battle(dto,defs,fallbackA);var b=Battle(copy,defs,fallbackB);Assert.AreEqual(Continue(a,fallbackA),Continue(b,fallbackB));
            Assert.AreEqual(0,fallbackA.Calls);Assert.AreEqual(0,fallbackB.Calls);Assert.AreEqual(0,dto.Battle.Random.Draws,"ライブ進行で保存境界を上書きしない");
            CollectionAssert.AreEqual(json,PortableCheckpointJson.Encode(dto));
        }
        [Test]
        public void 旧保存は追記せず従来外部乱数を使い初回手札と候補を保持する()
        {
            var d=Plan(out var defs);d.Battle.RandomVersion=null;d.Battle.Random=null;var json=PortableCheckpointJson.Encode(d);var r=PortableCheckpointJson.Decode(json);var external=new CountRandom();
            var battle=Battle(r,defs,external);Continue(battle,external);Assert.Greater(external.Calls,0);Assert.IsNull(battle.CheckpointRandomState);
            Assert.IsNull(r.Battle.Random);Assert.IsNull(r.Battle.RandomVersion);CollectionAssert.AreEqual(json,PortableCheckpointJson.Encode(r));
        }
        [TestCase("missing")] [TestCase("marker")] [TestCase("version")] [TestCase("algorithm")] [TestCase("state")] [TestCase("draws")]
        public void 新形式の欠損不正版不一致は旧保存へ黙ってfallbackしない(string invalid)
        {
            var d=Plan(out var defs);
            if(invalid=="missing")d.Battle.Random=null;if(invalid=="marker")d.Battle.RandomVersion=null;if(invalid=="version")d.Battle.Random.Version=9;
            if(invalid=="algorithm")d.Battle.Random.Algorithm="unknown";if(invalid=="state")d.Battle.Random.State=0;if(invalid=="draws")d.Battle.Random.Draws=-1;
            Assert.IsFalse(CheckpointCodec.TryRestore(d,defs,out _,out var error));Assert.IsNotEmpty(error);
        }
        [TestCase("Algorithm")] [TestCase("Version")] [TestCase("Seed")] [TestCase("State")] [TestCase("Draws")]
        public void 版付き状態の必須フィールド欠落はデコードで拒否する(string field)
        {
            string json=Encoding.UTF8.GetString(PortableCheckpointJson.Serialize(new SeededBattleRandom(73).CaptureState()));
            json=Regex.Replace(json,"\""+field+"\":(?:\"[^\"]*\"|[^,}]+),?","").Replace(",}","}");
            Assert.Catch(()=>PortableCheckpointJson.Deserialize<BattleRandomStateDto>(Encoding.UTF8.GetBytes(json)));
        }
        [Test]
        public void 輸送側seedで保存された乱数を上書きせずコピーも状態を共有しない()
        {
            var d=Plan(out var defs);var timing=CheckpointBattleFactory.ProductBasicTiming;var p=PortableBattlePackage.Capture(defs,PortableCheckpointJson.Encode(d),new PortableBattleRules{
                ChargeFrames=60,IntervalFrames=12,HitInvincibilityMs=600,StartupFrames=timing.StartupFrames,RecoveryFrames=timing.RecoveryFrames,CancelAfterFrames=timing.CancelAfterFrames,GaugeSpeed=2},999);
            Assert.AreEqual(73,p.ReplaySeed);Assert.AreEqual(d.Battle.Random.State,p.RandomState);var copy=d.Copy();copy.Battle.Random.State++;
            Assert.AreNotEqual(copy.Battle.Random.State,d.Battle.Random.State);p.ReplaySeed++;Assert.Throws<ArgumentException>(()=>p.CreateSimulation());
        }
    }
}
