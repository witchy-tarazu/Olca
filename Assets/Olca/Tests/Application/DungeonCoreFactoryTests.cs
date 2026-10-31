using System;
using System.Reflection;
using NUnit.Framework;
using Olca.Application.Battle;
using Olca.Application.Management;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Unity;
namespace Olca.Tests.Application
{
    public sealed class DungeonCoreFactoryTests
    {
        [TestCase(false)] [TestCase(true)] public void 通常生成と凍結preview_既存の加工System構成を区別する(bool preview)
        {
            int definitions=0;var factory=new DungeonCoreFactory(new UnityRandomProvider(7),new UnityLogger(),()=>{definitions++;return new SimpleCardFactory();});
            var settings=new ClearReturnSettings{BaseMaxHp=37,HpPerLevel=6};var player=factory.CreatePlayer(settings,3,2,20);
            Assert.AreEqual(49,player.MaxHp);var map=DungeonConfig.CreateTestDungeon(6,1,2);var deck=new Deck();
            var core=preview?factory.CreatePreview(map,player,deck,null):factory.Create(new DungeonCoreFactory.Launch{Map=map,Player=player,Deck=deck});
            Assert.AreEqual(preview?0:2,definitions);
            foreach(string name in new[]{"exceedSystem","tuningSystem","fusionSystem"}){
                var value=core.Dungeon.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(core.Dungeon);
                Assert.AreEqual(!preview,value!=null,name);
            }
        }
        [TestCase(false)] [TestCase(true)] public void 復帰候補作成_現Coreと保存を変えず確定済み敗北は拒否する(bool ended)
        {
            var cards=new SimpleCardFactory();var deck=cards.CreateStarterDeck();var map=DungeonConfig.CreateTestDungeon(6,1,2);
            var checkpoint=DungeonCheckpointSession.CreateDefault(map,deck);var factory=new DungeonCoreFactory(new UnityRandomProvider(7),new UnityLogger(),()=>new SimpleCardFactory());
            var current=factory.CreatePreview(map,new PlayerCharacter(100),deck,checkpoint);var before=PortableCheckpointJson.Serialize(checkpoint.Store.Read());
            var flow=new DungeonCheckpointFlow(factory);var receipts=ended?new[]{new ClearReceipt{RunId=checkpoint.RunId,Outcome=RunReturnOutcome.Defeated}}:Array.Empty<ClearReceipt>();
            bool prepared=flow.TryPrepare(checkpoint,null,null,()=>receipts,out var result,out var error);
            Assert.AreEqual(!ended,prepared,error);Assert.IsFalse(current.IsSuspended);CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(checkpoint.Store.Read()));
            if(ended)Assert.IsNull(result);else{Assert.AreNotSame(current,result.Core);Assert.AreSame(checkpoint,result.Checkpoint);Assert.IsNull(result.Route);result.Core.SuspendForCheckpoint();}
        }
    }
}
