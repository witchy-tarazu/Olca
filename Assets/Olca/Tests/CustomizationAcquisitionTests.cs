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
        private static CustomizationAcquisitionDefinition TrialKit(string run=null)=>Olca.Application.Management.TrialCustomizationDefinitions.Provider().Resolve(new CustomizationAcquisitionContext(run));
        [Test]
        public void 試用装備は読取だけで旧保存を変えず別盤面と不明所持を自動登録しない()
        {
            var before=File.ReadAllBytes(Store().ActivePath);Assert.IsTrue(Olca.Application.Management.TrialCustomizationDefinitions.CanRegister(Session().CustomizationSnapshot));
            Assert.IsFalse(Olca.Application.Management.TrialCustomizationDefinitions.CanRegister(new CustomizationProgress{BoardId="other"}));
            Assert.IsFalse(Olca.Application.Management.TrialCustomizationDefinitions.CanRegister(new CustomizationProgress{OwnedAccessoryIds=new[]{"unknown"}}));
            Assert.IsFalse(Olca.Application.Management.TrialCustomizationDefinitions.CanRegister(new CustomizationProgress{OwnedParts=new[]{new CustomizationItemCount{Id="unknown",Count=1}}}));
            var a=TrialKit().Snapshot;var b=TrialKit().Snapshot;Assert.AreEqual(a.ContextId,b.ContextId);Assert.AreEqual(a.Candidates.Single().RequestId,b.Candidates.Single().RequestId);
            var run=Id();var clear=TrialKit(run).Snapshot;Assert.AreEqual("clear:"+run,clear.ContextId);var kit=clear.Candidates.Single(c=>c.Accessories.Contains(Olca.Application.Management.TrialCustomizationDefinitions.Accessory));Assert.AreEqual(run,kit.ClearRunId);Assert.AreNotEqual(a.Candidates.Single().RequestId,kit.RequestId);Assert.AreEqual(kit.RequestId,TrialKit(run).Snapshot.Candidates.Single(c=>c.Accessories.Contains(Olca.Application.Management.TrialCustomizationDefinitions.Accessory)).RequestId);Assert.AreEqual(2,clear.Candidates.Length);Assert.AreEqual(1,clear.MaximumClaims);Assert.IsTrue(clear.Candidates.All(c=>c.ClearRunId==run));
            CollectionAssert.AreEqual(before,File.ReadAllBytes(Store().ActivePath));
        }
        [Test]
        public void 試用一式の取得配置回転固定解放と再読込は既知耐性だけを加算する()
        {
            catalog=Olca.Application.Management.TrialCustomizationDefinitions.Catalog();var s=Session();var definition=TrialKit();var origin=definition.Snapshot.ContextId;var id=definition.Snapshot.Candidates.Single().RequestId;
            Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,definition),s.Error);Assert.IsEmpty(s.CustomizationSnapshot.OwnedAccessoryIds);Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,origin,id),s.Error);
            var state=Session().CustomizationSnapshot;Assert.AreEqual(1,state.OwnedAccessoryIds.Length);Assert.AreEqual(2,state.OwnedParts.Single().Count);Assert.AreEqual(1,state.OwnedUnlockItems.Single().Count);Assert.IsEmpty(state.EquippedAccessoryIds);Assert.IsEmpty(state.Placements);Assert.AreEqual(6,state.UnlockedCells.Length);
            string accessory=Olca.Application.Management.TrialCustomizationDefinitions.Accessory,part=Olca.Application.Management.TrialCustomizationDefinitions.Part;
            var placements=new[]{new PartPlacement("first",part,new Vector2Int(0,0)),new PartPlacement("second",part,new Vector2Int(2,0),PartRotation.Clockwise90)};
            Assert.IsFalse(s.TrySetCustomizationLoadout(catalog,new[]{accessory},placements));Assert.IsTrue(s.TryApplyCustomizationUnlock(catalog,Olca.Application.Management.TrialCustomizationDefinitions.Unlock),s.Error);
            Assert.IsTrue(s.TrySetCustomizationLoadout(catalog,new[]{accessory},placements),s.Error);s=Session();Assert.AreEqual(9,s.CustomizationSnapshot.UnlockedCells.Length);Assert.AreEqual(2,s.CustomizationSnapshot.Placements.Length);
            var abilities=Olca.Application.Management.TrialCustomizationDefinitions.Abilities().Resolve(new CustomizationLoadoutSources(catalog,s.CustomizationSnapshot));var actor=new PlayerCharacter(100);actor.SetDepartureAbilities(abilities);Assert.AreEqual(20,actor.Resistances.Fire);Assert.AreEqual(0,actor.Resistances.Water);Assert.IsFalse(actor.TerrainState.IsFloating);
            var snapshot=abilities.Capture();var restored=new ResolvedCustomizationAbilities(snapshot);Assert.AreEqual(20,restored.CombineElements(ElementResistances.Neutral).Fire);Assert.AreEqual(Olca.Application.Management.TrialCustomizationDefinitions.AbilityId,snapshot.DefinitionId);
            long revision=s.Snapshot.Revision;Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,origin,id));Assert.AreEqual(revision,s.Snapshot.Revision);Assert.AreEqual(2,s.CustomizationSnapshot.OwnedParts.Single().Count);
            var other=new CustomizationGrant(Id(),new[]{accessory},new Dictionary<string,int>{{part,2}});Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define("other-trial",1,other)),s.Error);Assert.IsEmpty(s.FindCustomizationAcquisition("other-trial").Candidates);Assert.AreEqual(2,s.CustomizationSnapshot.OwnedParts.Single().Count);
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public void 試用一式も候補と受領の保存失敗再起動を同じ内容で復旧する(bool claim,bool after)
        {
            catalog=Olca.Application.Management.TrialCustomizationDefinitions.Catalog();var definition=TrialKit();var origin=definition.Snapshot.ContextId;var id=definition.Snapshot.Candidates.Single().RequestId;var s=Session();if(claim)Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,definition));
            var bytes=File.ReadAllBytes(Store().ActivePath);var fault=new Fault{Before=!after,After=after};s=Session(fault);
            bool ok=claim?s.TryClaimCustomizationAcquisition(catalog,origin,id):s.TryPrepareCustomizationAcquisition(catalog,definition);Assert.AreEqual(after,ok,s.Error);
            if(!after){Assert.IsTrue(s.HasPendingSave);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));fault.Before=false;Assert.IsTrue(s.Retry(),s.Error);}
            s=Session();Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,origin,id),s.Error);Assert.AreEqual(1,s.CustomizationSnapshot.OwnedAccessoryIds.Length);Assert.AreEqual(2,s.CustomizationSnapshot.OwnedParts.Single().Count);long revision=s.Snapshot.Revision;Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,origin,id));Assert.AreEqual(revision,s.Snapshot.Revision);
        }
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
        [TestCase(false)] [TestCase(true)]
        public void 質試用候補は新しいクリアだけに提示し旧候補へ追加せず所持装備を保存する(bool oldPlan)
        {
            catalog=Olca.Application.Management.TrialCustomizationDefinitions.Catalog();var map=DungeonConfig.CreateTestDungeon(8,30,2);var deck=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck();var checkpoints=DungeonCheckpointSession.CreateDefault(map,deck);var game=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:checkpoints);var departure=checkpoints.Store.Read();var s=Session();Assert.IsTrue(s.PrepareNextDeparture(departure,"fixture"),s.Error);game.OnCardRewardPresented+=_=>game.SelectCardReward(null);
            for(int i=0;i<1000&&game.CurrentPhase!=GamePhase.DungeonCleared;i++){game.TickFrame(17);if(game.CurrentBattle!=null)foreach(var enemy in game.CurrentBattle.Enemies.ToArray())if(!enemy.IsDead)enemy.TakeDamage(99999,ElementType.None);game.ResumeAfterBattle();}Assert.AreEqual(GamePhase.DungeonCleared,game.CurrentPhase);Assert.IsTrue(CheckpointCodec.TryRestore(checkpoints.Store.Read(),checkpoints.Definitions,out var source,out var error),error);Assert.IsTrue(s.Begin(source,"fixture",new ClearReturnSettings()),s.Error);Assert.IsTrue(s.ConfirmSelection());Assert.IsTrue(s.ConfirmReturn());
            var definition=TrialKit(departure.RunId);var plan=definition.Snapshot;var quality=plan.Candidates.Single(c=>c.Accessories.Contains(Olca.Application.Management.TrialCustomizationDefinitions.QualityAccessory));
            if(oldPlan){var kit=plan.Candidates.Single(c=>c.RequestId!=quality.RequestId);var legacy=Define(plan.ContextId,1,kit.ToGrant());Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,legacy),s.Error);var before=File.ReadAllBytes(Store().ActivePath);Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,definition));CollectionAssert.AreEqual(before,File.ReadAllBytes(Store().ActivePath));Assert.AreEqual(1,s.FindCustomizationAcquisition(plan.ContextId).Candidates.Length);Assert.IsFalse(s.TryClaimCustomizationAcquisition(catalog,plan.ContextId,quality.RequestId));return;}
            Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,definition),s.Error);Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,plan.ContextId,quality.RequestId),s.Error);s=Session();Assert.IsTrue(s.TrySetCustomizationLoadout(catalog,new[]{Olca.Application.Management.TrialCustomizationDefinitions.QualityAccessory},Array.Empty<PartPlacement>()),s.Error);var cold=Session();var abilities=Olca.Application.Management.TrialCustomizationDefinitions.Abilities().Resolve(new CustomizationLoadoutSources(catalog,cold.CustomizationSnapshot));Assert.AreEqual(1,abilities.RewardQualityBonus);Assert.AreEqual(0,abilities.CombineElements(ElementResistances.Neutral).Fire);long revision=cold.Snapshot.Revision;Assert.IsTrue(cold.TryClaimCustomizationAcquisition(catalog,plan.ContextId,quality.RequestId));Assert.AreEqual(revision,cold.Snapshot.Revision);Assert.AreEqual(1,cold.CustomizationSnapshot.OwnedAccessoryIds.Length);Assert.AreEqual(1,TrialKit().Snapshot.Candidates.Length,"通常Hub一式には質装備を追加しない");
        }
        [Test] public void 質試用の秀プラスは明示別IDで旧四帯と元カード性能を変えない(){var cards=new Olca.Application.Battle.SimpleCardFactory();var provider=Olca.Application.Management.DungeonCoreFactory.CreateTrialRewardProvider(cards);var encounter=new CheckpointBattleDto{ContactCell=3,ParticipantCells=new[]{3},CurrencyAmounts=new[]{7}};var old=provider.Resolve(new BattleRewardContext(Olca.Application.Management.DungeonCoreFactory.TrialRewardMapId,encounter,false));var quality=provider.Resolve(new BattleRewardContext(Olca.Application.Management.DungeonCoreFactory.TrialRewardMapId,encounter,false,1));Assert.AreEqual("trial-card-rewards-v1",old.Id);Assert.AreEqual(4,old.Bands.Count);Assert.AreEqual("trial-card-quality-rewards-v1",quality.Id);Assert.AreEqual(5,quality.Bands.Count);for(int n=0;n<4;n++){Assert.AreEqual(5000,quality.Bands[n].NormalCardChanceBasisPoints);CollectionAssert.AreEqual(old.Bands[n].CurrencyAmounts,quality.Bands[n].CurrencyAmounts);}Assert.AreEqual(10000,quality.Bands[4].NormalCardChanceBasisPoints);CollectionAssert.AreEqual(new[]{11},quality.Bands[4].CurrencyAmounts);Assert.AreEqual(0,quality.Bands[4].ItemChancePercent);Assert.Throws<ArgumentException>(()=>provider.Resolve(new BattleRewardContext(Olca.Application.Management.DungeonCoreFactory.TrialRewardMapId,new CheckpointBattleDto{ContactCell=3,ParticipantCells=new[]{3},CurrencyAmounts=new[]{int.MaxValue}},false,1)));Assert.Throws<ArgumentOutOfRangeException>(()=>new BattleRewardContext("id",encounter,false,-1));}
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
        [Test]
        public void 受領状態は一時点で固定し新規受取と同一要求の再送を区別する()
        {
            var s=Session();var accessory=new CustomizationGrant(Id(),new[]{"a"});var part=Part();
            Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define("event",1,accessory,part)));
            var plan=s.FindCustomizationAcquisition("event");var progress=s.CustomizationSnapshot;
            var before=new CustomizationAcquisitionAvailability(plan,progress);
            Assert.IsTrue(before.ForCandidate(accessory.RequestId).CanReceive);Assert.IsTrue(before.ForCandidate(part.RequestId).CanReceive);
            // 呼出元が後から公開snapshotを変更しても、解決済みの読み取り結果は変わらない。
            plan.Candidates[0].Accessories[0]="changed";progress.OwnedAccessoryIds=new[]{"a"};
            Assert.IsTrue(before.ForCandidate(accessory.RequestId).CanReceive);
            Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"event",accessory.RequestId),s.Error);
            var after=new CustomizationAcquisitionAvailability(s.FindCustomizationAcquisition("event"),s.CustomizationSnapshot);
            Assert.AreEqual(1,after.ReceivedCount);var received=after.ForCandidate(accessory.RequestId);
            Assert.IsTrue(received.IsReceived);Assert.IsTrue(received.HasOwnedAccessory);Assert.IsTrue(received.LimitReached);Assert.IsFalse(received.CanReceive);
            Assert.IsFalse(after.ForCandidate(part.RequestId).CanReceive);Assert.IsFalse(s.TryClaimCustomizationAcquisition(catalog,"event",part.RequestId));
            long revision=s.Snapshot.Revision;Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"event",accessory.RequestId));Assert.AreEqual(revision,s.Snapshot.Revision);
        }
        [Test]
        public void 受取枠が残っていても別候補で取得した同じ装飾品は受け取れない()
        {
            var s=Session();var a=new CustomizationGrant(Id(),new[]{"a"});var b=new CustomizationGrant(Id(),new[]{"a"});
            Assert.IsTrue(s.TryPrepareCustomizationAcquisition(catalog,Define("event",2,a,b)));Assert.IsTrue(s.TryClaimCustomizationAcquisition(catalog,"event",a.RequestId));
            var availability=new CustomizationAcquisitionAvailability(s.FindCustomizationAcquisition("event"),s.CustomizationSnapshot);
            var other=availability.ForCandidate(b.RequestId);Assert.IsFalse(other.IsReceived);Assert.IsTrue(other.HasOwnedAccessory);Assert.IsFalse(other.LimitReached);Assert.IsFalse(other.CanReceive);
            Assert.IsFalse(s.TryClaimCustomizationAcquisition(catalog,"event",b.RequestId));Assert.Throws<ArgumentException>(()=>availability.ForCandidate("missing"));
        }
    }
}
namespace Olca.Tests
{
    public class CustomizationAcquisitionPresentationTests
    {
        private UnityEngine.GameObject root;private Olca.Presentation.Dungeon.CustomizationAcquisitionView view;private int claims,cancels,retries;
        private UnityEngine.UI.Button Button(string name)=>view.GetComponentsInChildren<UnityEngine.UI.Button>().Single(v=>v.name==name);
        private void Create(){root=new UnityEngine.GameObject("AcquisitionFixture",typeof(UnityEngine.RectTransform),typeof(UnityEngine.Canvas));var go=new UnityEngine.GameObject("Acquisition",typeof(UnityEngine.RectTransform));go.transform.SetParent(root.transform,false);view=go.AddComponent<Olca.Presentation.Dungeon.CustomizationAcquisitionView>();view.Initialize(UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf"),_=>claims++,()=>{cancels++;view.Close();},()=>retries++);}
        [TearDown]public void Destroy(){if(root!=null)UnityEngine.Object.DestroyImmediate(root);claims=cancels=retries=0;}
        [Test] public void 行き先の検証エラーは保存再試行を出さずページ変更と戻るを保ち保存待ちは再試行する()
        {
            root=new UnityEngine.GameObject("QualityDepartureValidationFixture",typeof(UnityEngine.RectTransform),typeof(UnityEngine.Canvas));var screen=root.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();screen.Initialize(UnityEngine.Resources.GetBuiltinResource<UnityEngine.Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});int back=0,retry=0;var names=Enumerable.Range(0,7).Select(n=>"test"+n).ToArray();var unlocked=names.Select(_=>true).ToArray();
            void Render(bool pending)=>screen.RenderDungeonSelection(names,unlocked,0,_=>{},()=>{},()=>back++,pending,pending?"保存失敗":"定義未登録",()=>retry++,retrySaveAvailable:pending);
            UnityEngine.UI.Button Find(string name)=>screen.GetComponentsInChildren<UnityEngine.UI.Button>().Single(b=>b.name==name);
            try{Render(false);Assert.IsFalse(screen.GetComponentsInChildren<UnityEngine.UI.Button>().Any(b=>b.name=="RetryDungeonDeparture"));Assert.IsTrue(Find("CancelDungeonSelection").IsInteractable());Find("NextDungeonPage").onClick.Invoke();Assert.AreEqual("2/2",screen.GetComponentsInChildren<UnityEngine.UI.Text>().Single(t=>t.name=="DungeonPage").text);Assert.IsFalse(screen.GetComponentsInChildren<UnityEngine.UI.Button>().Any(b=>b.name=="RetryDungeonDeparture"));Find("CancelDungeonSelection").onClick.Invoke();Assert.AreEqual(1,back);Render(true);Find("RetryDungeonDeparture").onClick.Invoke();Assert.AreEqual(1,retry);}finally{typeof(Olca.Presentation.Dungeon.ClearReturnView).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(screen,null);}
        }
        [Test]public void 受領前と保存済み結果は区別し登録形状だけを表示して二重確定しない()
        {
            Create();string id=Guid.NewGuid().ToString("N");var grant=new CustomizationGrant(id,parts:new Dictionary<string,int>{{"dot",1}});var plan=new CustomizationAcquisitionDefinition("event",1,new[]{grant},new Dictionary<string,string>{{id,"<b>セット</b>"}}).Snapshot;plan.BoardId="fixture";var progress=new CustomizationProgress{OwnedParts=new[]{new CustomizationItemCount{Id="dot",Count=1}}};
            view.Open(plan,progress);Button("AcquisitionCandidate_"+id).onClick.Invoke();Assert.IsFalse(view.GetComponentsInChildren<UnityEngine.RectTransform>().Any(v=>v.name=="AcquisitionShape_0"));
            var catalog=new CustomizationCatalog("fixture",new PartsBoardDefinition(2,1,new[]{new Vector2Int(0,0)}),null,new Dictionary<string,PartShape>{{"dot",new PartShape(new[]{new Vector2Int(0,0)})}},null);view.Open(plan,progress,false,catalog);Button("AcquisitionCandidate_"+id).onClick.Invoke();Assert.AreEqual(1,view.GetComponentsInChildren<UnityEngine.RectTransform>().Single(v=>v.name=="AcquisitionShape_0").childCount);Assert.AreEqual(0,claims);var body=view.GetComponentsInChildren<UnityEngine.UI.Text>().Single(v=>v.name=="AcquisitionPreviewInfoText");StringAssert.Contains("<b>セット</b>",body.text);Assert.IsFalse(body.supportRichText);StringAssert.Contains("dot × 1 / ショジ 1",body.text);var old=Button("ClaimCustomizationAcquisition");old.onClick.Invoke();Assert.AreEqual(1,claims);
            var received=progress.Copy();received.OwnedParts[0].Count=2;received.Receipts=new[]{new CustomizationGrantReceipt{RequestId=id,Fingerprint=new string('0',64)}};view.Saved(plan,received);StringAssert.Contains("ウケトリズみ",view.GetComponentsInChildren<UnityEngine.UI.Text>().Single(v=>v.name=="AcquisitionPreviewInfoText").text);Assert.IsFalse(Button("ClaimCustomizationAcquisition").interactable);Assert.DoesNotThrow(()=>old.onClick.Invoke());Assert.AreEqual(1,claims);Assert.AreEqual(1,progress.OwnedParts[0].Count);view.Close();view.Open(plan,received,false,catalog);var completed=view.GetComponentsInChildren<UnityEngine.UI.Text>().Single(v=>v.name=="AcquisitionPreviewInfoText").text;StringAssert.Contains("カンリョウ",completed);Assert.IsFalse(completed.Contains("コウホをエラ"));Assert.IsFalse(Button("ClaimCustomizationAcquisition").interactable);
        }
        [Test]public void 空候補と保存未完了は受領取消を誤って実行しない()
        {
            Create();view.Open(new CustomizationAcquisitionPlan{ContextId="empty",MaximumClaims=0,Candidates=Array.Empty<CustomizationAcquisitionCandidate>()},new CustomizationProgress());Assert.IsTrue(view.GetComponentsInChildren<UnityEngine.UI.Text>().Any(v=>v.name=="NoAcquisitionCandidates"));Assert.IsFalse(Button("ClaimCustomizationAcquisition").interactable);view.Failed(true);Button("CancelCustomizationAcquisition").onClick.Invoke();Assert.AreEqual(0,cancels);Assert.AreEqual(0,claims);var old=Button("RetryCustomizationAcquisition");old.onClick.Invoke();Assert.AreEqual(1,retries);view.Close();Assert.DoesNotThrow(()=>old.onClick.Invoke());Assert.AreEqual(1,retries);
        }
    }
}
