using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Customization;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Tests.Mocks;
namespace Olca.Tests
{
    public class CustomizationAcquisitionTests
    {
        private string directory;private CustomizationCatalog catalog;private bool running;
        private Olca.Unity.ClearReturnFileStore Store(Olca.Unity.CheckpointFileIo io=null)=>new Olca.Unity.ClearReturnFileStore(directory,io);
        private ClearReturnSession Session(Olca.Unity.CheckpointFileIo io=null)=>new ClearReturnSession(Store(io),isCustomizationRunActive:()=>running);
        private static string Id()=>Guid.NewGuid().ToString("N");
        private static CustomizationGrant Part(string id=null,string run=null)=>new CustomizationGrant(id??Id(),parts:new Dictionary<string,int>{{"dot",1}},clearRunId:run);
        private static CustomizationAcquisitionDefinition Define(string context,int max,params CustomizationGrant[] grants)=>new CustomizationAcquisitionDefinition(context,max,grants,grants.ToDictionary(v=>v.RequestId,v=>"テスト "+v.RequestId));
        [SetUp]public void Setup(){directory=Path.Combine(Path.GetTempPath(),"olca-acquisition-"+Id());catalog=new CustomizationCatalog("fixture",new PartsBoardDefinition(2,1,new[]{new Vector2Int(0,0)}),new[]{"a"},new Dictionary<string,PartShape>{{"dot",new PartShape(new[]{new Vector2Int(0,0)})}},null);var s=Session();Assert.IsTrue(s.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()),s.Error);}
        [TearDown]public void Cleanup(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        [Test]
        public void 所持済み装飾品だけを候補から除き重複パーツは除かない()
        {
            var s=Session();Assert.IsTrue(s.TryGrantCustomization(catalog,new CustomizationGrant(Id(),new[]{"a"},new Dictionary<string,int>{{"dot",1}})));
            var part=Part();Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define("home-event",1,new CustomizationGrant(Id(),new[]{"a"}),part)),s.Error);
            var plan=s.FindCustomizationAcquisition("home-event");Assert.AreEqual(part.RequestId,plan.Candidates.Single().RequestId);Assert.AreEqual(1,s.CustomizationSnapshot.OwnedParts.Single().Count);
            Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"home-event",part.RequestId),s.Error);Assert.AreEqual(2,Session().CustomizationSnapshot.OwnedParts.Single().Count);
        }
        [Test]
        public void 保存候補は再定義取消再開で変わらず同じ受領と上限超過で増殖しない()
        {
            var s=Session();var a=Part();var b=Part();Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define("event",1,a,b)),s.Error);
            var bytes=File.ReadAllBytes(Store().ActivePath);var snapshot=s.FindCustomizationAcquisition("event");snapshot.Candidates[0].Parts[0].Count=999;
            // 閉じる操作はCoreに要求しない。再起動後も同じ計画を読み出すだけ。
            s=Session();Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define("event",99,Part())),s.Error);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));
            Assert.AreEqual(a.RequestId,s.FindCustomizationAcquisition("event").Candidates[0].RequestId);Assert.IsFalse(s.TryGrantCustomization(catalog,a));
            Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"event",a.RequestId),s.Error);long revision=s.Snapshot.Revision;
            Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"event",a.RequestId));Assert.AreEqual(revision,s.Snapshot.Revision);
            Assert.IsFalse(s.TryClaimCustomizationAcquisition(catalog,"event",b.RequestId));Assert.AreEqual(1,Session().CustomizationSnapshot.OwnedParts.Single().Count);
            Assert.IsFalse(s.TryPrepareCustomizationAcquisition(catalog,Define("other",1,a)));
        }
        private sealed class Fault:Olca.Unity.CheckpointFileIo {public bool Before,After;public override void Replace(string a,string b,string c){if(Before)throw new IOException("fixture before");base.Replace(a,b,c);if(After)throw new IOException("fixture after");}}
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public void 候補保存と受取の前後書込失敗は同じ計画で原子的に再試行する(bool claim,bool after)
        {
            var a=Part();var definition=Define("event",1,a);var s=Session();if(claim)Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,definition));
            var before=File.ReadAllBytes(Store().ActivePath);var io=new Fault{Before=!after,After=after};s=Session(io);
            bool ok=claim?s.TryClaimCustomizationAcquisition(catalog,"event",a.RequestId):s.TryPrepareCustomizationAcquisition(catalog,definition);Assert.AreEqual(after,ok,s.Error);
            if(!after){Assert.IsTrue(s.HasPendingSave);CollectionAssert.AreEqual(before,File.ReadAllBytes(Store().ActivePath));Assert.IsFalse(s.TryClaimCustomizationAcquisition(catalog,"event",a.RequestId));io.Before=false;Assert.IsTrue(s.Retry(),s.Error);}
            s=Session();Assert.IsNotNull(s.FindCustomizationAcquisition("event"));Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"event",a.RequestId));Assert.AreEqual(1,Session().CustomizationSnapshot.OwnedParts.Single().Count);
        }
        [Test]
        public void 攻略中と不明な定義と別攻略のクリアを装う候補は保存しない()
        {
            var s=Session();var original=File.ReadAllBytes(Store().ActivePath);running=true;Assert.IsFalse(s.TryPrepareCustomizationAcquisition(catalog,Define("event",1,Part())));running=false;
            Assert.IsFalse(s.TryPrepareCustomizationAcquisition(catalog,Define("event",1,new CustomizationGrant(Id(),new[]{"unknown"}))));
            var run=Id();Assert.IsFalse(s.TryPrepareCustomizationAcquisition(catalog,Define("clear:"+run,1,Part(run:run))));CollectionAssert.AreEqual(original,File.ReadAllBytes(Store().ActivePath));
        }
        [Test]
        public void 旧省略状態は空候補として読み込まれ内容差替と上限破損を検出する()
        {
            Assert.IsEmpty(Session().CustomizationSnapshot.Acquisitions);var s=Session();var a=Part();Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define("event",1,a)));Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"event",a.RequestId));
            var state=s.CustomizationSnapshot;state.Acquisitions[0].Candidates[0].Parts[0].Count=2;Assert.Throws<ArgumentException>(()=>CustomizationProgress.Validate(state));
            state=s.CustomizationSnapshot;state.Acquisitions[0].MaximumClaims=0;Assert.Throws<ArgumentException>(()=>CustomizationProgress.Validate(state));
        }
        [Test]
        public void 同じ装飾品を含む二候補の連続確定は二重取得しない()
        {
            var s=Session();var a=new CustomizationGrant(Id(),new[]{"a"});var b=new CustomizationGrant(Id(),new[]{"a"});Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define("event",2,a,b)));Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"event",a.RequestId));Assert.IsFalse(s.TryClaimCustomizationAcquisition(catalog,"event",b.RequestId));Assert.AreEqual(1,Session().CustomizationSnapshot.OwnedAccessoryIds.Length);
        }
        [Test]
        public void 実際のクリア帰還から候補を保存復帰して一度だけ受け取れる()
        {
            var map=DungeonConfig.CreateTestDungeon(8,30,2);var deck=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck();var checkpoints=DungeonCheckpointSession.CreateDefault(map,deck);
            var game=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:checkpoints);var departure=checkpoints.Store.Read();var s=Session();Assert.IsTrue(s.PrepareNextDeparture(departure,"fixture"),s.Error);
            game.OnCardRewardPresented+=_=>game.SelectCardReward(null);
            for(int i=0;i<1000&&game.CurrentPhase!=GamePhase.DungeonCleared;i++){game.TickFrame(17);if(game.CurrentBattle!=null)foreach(var e in game.CurrentBattle.Enemies.ToArray())if(!e.IsDead)e.TakeDamage(99999,ElementType.None);game.ResumeAfterBattle();}
            Assert.AreEqual(GamePhase.DungeonCleared,game.CurrentPhase);Assert.IsTrue(CheckpointCodec.TryRestore(checkpoints.Store.Read(),checkpoints.Definitions,out var source,out var error),error);
            Assert.IsTrue(s.Begin(source,"fixture",new ClearReturnSettings()),s.Error);Assert.IsTrue(s.ConfirmSelection());Assert.IsTrue(s.ConfirmReturn());
            var grant=Part(run:departure.RunId);string context="clear:"+departure.RunId;Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define(context,1,grant)),s.Error);
            s=Session();Assert.AreEqual(departure.RunId,s.FindCustomizationAcquisition(context).Candidates[0].ClearRunId);Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,context,grant.RequestId),s.Error);long revision=s.Snapshot.Revision;Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,context,grant.RequestId));Assert.AreEqual(revision,s.Snapshot.Revision);Assert.AreEqual(1,Session().CustomizationSnapshot.OwnedParts.Single().Count);
        }
    }
}
