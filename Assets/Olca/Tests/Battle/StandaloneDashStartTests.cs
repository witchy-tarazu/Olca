using System;
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
    public class StandaloneDashStartTests
    {
        private static PortableBattlePackage Package(bool dash,int? effect=null,int? value=null,int size=100)
        {
            var f=new SimpleCardFactory();var deck=f.CreateStarterDeck();var map=DungeonConfig.CreateTestDungeon(size,30,50);
            if(value.HasValue)
            {
                var enemy=map.MapCells[50].Enemy;
                map.MapCells[50]=new MapCell(50,new Enemy(enemy.EnemyId,enemy.Name,enemy.MaxHp,enemy.Element,enemy.GridPosition,
                    dashContact:new EnemyDashContactDefinition((DashContactOutcome)effect.Value,value.Value)));
            }
            var session=DungeonCheckpointSession.CreateDefault(map,deck);
            _=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new SeededBattleRandom(1),new MockLogger(),checkpoints:session);
            var d=session.Store.Read();d.Enemies.First(e=>e.CellIndex==50).GridX=4;d.Player.GridX=2;
            var encounter=new CheckpointBattleDto{ContactCell=50,ParticipantCells=new[]{50},HandLimit=10,GridOriginX=47,DashContact=dash,DashEffect=effect,DashValue=value};
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(d,encounter,session.Definitions,73,null,c=>"fallback/v1/"+c.Rarity+"/"+c.Id,out var plan,out var error),error);
            var timing=CheckpointBattleFactory.ProductBasicTiming;
            return PortableBattlePackage.Capture(session.Definitions,PortableCheckpointJson.Encode(plan.Snapshot),new PortableBattleRules{ChargeFrames=60,IntervalFrames=12,HitInvincibilityMs=600,StartupFrames=timing.StartupFrames,RecoveryFrames=timing.RecoveryFrames,CancelAfterFrames=timing.CancelAfterFrames,GaugeSpeed=2},999);
        }
        [TestCase(0,15)] [TestCase(1,180)] [TestCase(2,0)] [TestCase(3,9)]
        public void 初回カスタム準備後に既存初撃を一度だけ適用して開始イベントを観測する(int effect,int value)
        {
            var p=Package(true,effect,value);var original=(byte[])p.CheckpointEnvelope.Clone();var s=p.CreateSimulation();var b=s.Battle;
            Assert.IsTrue(b.StartEffects.Applied);Assert.AreEqual(0,b.LogicalFrame);Assert.IsTrue(b.CustomScreen.IsOpen&&b.IsPaused);
            Assert.AreEqual(effect==0?35:50,b.Enemies[0].CurrentHp);Assert.AreEqual(effect==3?91:100,b.Player.CurrentHp);Assert.AreEqual(effect==1,b.Enemies[0].IsStunned);
            Assert.IsTrue(s.InitialObservation.Events.Any(e=>e.StartsWith("start:")));Assert.IsFalse(b.StartEffects.Complete());
            Assert.AreEqual(effect==0?35:50,b.Enemies[0].CurrentHp);Assert.AreEqual(effect==3?91:100,b.Player.CurrentHp);
            CollectionAssert.AreEqual(original,p.CheckpointEnvelope);var resumed=p.CreateSimulation();Assert.AreEqual(b.Enemies[0].CurrentHp,resumed.Battle.Enemies[0].CurrentHp);Assert.AreEqual(b.Player.CurrentHp,resumed.Battle.Player.CurrentHp);
        }
        [TestCase(0,15)] [TestCase(1,180)] [TestCase(2,0)]
        public void 旧保存で省略された初撃量は既存互換値を維持する(int effect,int expected)
        {var s=Package(true,effect).CreateSimulation();Assert.AreEqual(expected,s.Battle.StartEffects.Value);}
        [TestCase(false)] [TestCase(true)]
        public void 通常開始と効果未指定の旧ダッシュ開始では勝手に初撃を足さない(bool dash)
        {var s=Package(dash).CreateSimulation();Assert.AreEqual(50,s.Battle.Enemies[0].CurrentHp);Assert.AreEqual(100,s.Battle.Player.CurrentHp);Assert.IsTrue(s.Battle.StartEffects.Applied);}
        [Test]
        public void スタンはカスタム停止中に進まず戦闘復帰後の論理フレームで解除する()
        {
            var s=Package(true,1,3).CreateSimulation();for(int i=0;i<20;i++)s.Step(new BattleFrameCommand());Assert.IsTrue(s.Battle.Enemies[0].IsStunned);
            s.Step(new BattleFrameCommand(custom:new CustomScreenCommand(CustomCommandKind.Confirm)));s.Step(new BattleFrameCommand(custom:new CustomScreenCommand(CustomCommandKind.ReturnToBattle)));
            s.Step(new BattleFrameCommand());s.Step(new BattleFrameCommand());Assert.IsTrue(s.Battle.Enemies[0].IsStunned);s.Step(new BattleFrameCommand());Assert.IsFalse(s.Battle.Enemies[0].IsStunned);
        }
        [Test]
        public void 開始準備の防御設定を通し再入と致死開始の二重適用を防ぐ()
        {
            var p=Package(true,3,15);var r=p.Restore(out _);var b=CheckpointBattleFactory.Create(r.Player,new DungeonMap(r.Map),r.Deck,r.Snapshot,r.FirstHand,new SeededBattleRandom(3));
            b.Player.SetBarrier(20);Assert.IsTrue(b.StartEffects.Complete());Assert.AreEqual(100,b.Player.CurrentHp);Assert.AreEqual(5,b.Player.BarrierHp);Assert.IsFalse(b.StartEffects.Complete());Assert.AreEqual(5,b.Player.BarrierHp);
            p=Package(true,0,50);r=p.Restore(out _);b=CheckpointBattleFactory.Create(r.Player,new DungeonMap(r.Map),r.Deck,r.Snapshot,r.FirstHand,new SeededBattleRandom(3));int reentry=0;var started=b;
            b.Enemies[0].OnHpChanged+=(hp,max)=>{if(!started.StartEffects.Complete())reentry++;};Assert.IsTrue(b.StartEffects.Complete());Assert.AreEqual(BattleState.Victory,b.State);Assert.Greater(reentry,0);Assert.IsFalse(b.StartEffects.Complete());
        }
        [TestCase(1)] [TestCase(7)] [TestCase(31)] [TestCase(240)]
        public void 同じ開始状態と入力は負荷と分割で一致し初期観測も同じになる(int batch)
        {var p=Package(true,0,15);Assert.AreEqual(PortableBattleReplay.Trace(p),PortableBattleReplay.Trace(p,batch,()=>{long n=0;for(int i=0;i<2000;i++)n=unchecked(n*33+i);GC.KeepAlive(n);}));}
        [Test]
        public void 異なる階層相当の定義を保存へ混ぜるとhash照合で拒否する()
        {var a=Package(false);var b=Package(false,size:80);Assert.AreNotEqual(a.MapHash,b.MapHash);a.Map=b.Map;Assert.Throws<ArgumentException>(()=>a.CreateSimulation());}
    }
}
