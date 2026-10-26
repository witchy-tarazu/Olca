using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using NUnit.Framework;
using Olca.Tests.Mocks;
using Olca.Core.Domain;
using Olca.Core.Logic.Customization;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;

namespace Olca.Tests
{
    public class CustomizationPersistenceTests
    {
        private string directory;
        private bool running;
        private ClearReturnSession session;
        private CustomizationCatalog catalog;
        private Olca.Unity.ClearReturnFileStore Store(Olca.Unity.CheckpointFileIo io = null) => new Olca.Unity.ClearReturnFileStore(directory,io);
        private ClearReturnSession Reload() => new ClearReturnSession(Store(),isCustomizationRunActive:()=>running);
        private static string Id()=>Guid.NewGuid().ToString("N");
        private static Dictionary<string,int> Counts(string id,int count=1)=>new Dictionary<string,int>{{id,count}};
        private CustomizationGrant Grant(IEnumerable<string> accessories=null,IReadOnlyDictionary<string,int> parts=null,IReadOnlyDictionary<string,int> unlocks=null,string run=null)
            =>new CustomizationGrant(Id(),accessories,parts,unlocks,run);
        [SetUp] public void Setup()
        {
            directory=Path.Combine(Path.GetTempPath(),"olca-customization-"+Id());running=false;
            catalog=new CustomizationCatalog("fixture-board",new PartsBoardDefinition(4,2,new[]{new Vector2Int(0,0),new Vector2Int(0,1)}),new[]{"a","b","c","d"},
                new Dictionary<string,PartShape>{{"bar",new PartShape(new[]{new Vector2Int(0,0),new Vector2Int(0,1)})},{"dot",new PartShape(new[]{new Vector2Int(0,0)})}},
                new Dictionary<string,IEnumerable<Vector2Int>>{{"right",new[]{new Vector2Int(1,0),new Vector2Int(1,1)}},{"far",new[]{new Vector2Int(3,1)}}});
            session=Reload();Assert.IsTrue(session.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()),session.Error);
        }
        [TearDown] public void Cleanup(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
        private void Acquire()
        {
            Assert.IsTrue(session.TryGrantCustomization(catalog,Grant(new[]{"a","b"},new Dictionary<string,int>{{"bar",2},{"dot",1}},Counts("right"))),session.Error);
        }
        [Test]
        public void 取得装備配置固定解放を原子的に保存し再読込で同じ配置を再検証する()
        {
            Acquire();Assert.IsFalse(session.TrySetCustomizationLoadout(catalog,new[]{"a"},new[]{new PartPlacement("p","bar",new Vector2Int(1,0))}));
            Assert.IsTrue(session.TryApplyCustomizationUnlock(catalog,"right"),session.Error);
            Assert.IsTrue(session.TrySetCustomizationLoadout(catalog,new[]{"a","b"},new[]{new PartPlacement("p","bar",new Vector2Int(0,0)),new PartPlacement("q","bar",new Vector2Int(1,0))}),session.Error);
            var restored=Reload();var state=restored.CustomizationSnapshot;CollectionAssert.AreEqual(new[]{"a","b"},state.EquippedAccessoryIds);Assert.AreEqual(2,state.Placements.Length);Assert.AreEqual(4,state.UnlockedCells.Length);
            Assert.IsTrue(restored.TryGetCustomizationLayout(catalog,out var layout,out var error),error);Assert.AreEqual(4,layout.OccupiedCells.Count);Assert.AreEqual(2,state.OwnedParts.Single(v=>v.Id=="bar").Count);Assert.AreEqual(1,state.OwnedUnlockItems.Single().Count);
            long revision=restored.Snapshot.Revision;Assert.IsTrue(restored.TryApplyCustomizationUnlock(catalog,"right"));Assert.AreEqual(revision,restored.Snapshot.Revision);
        }
        [Test]
        public void 取得再送は増殖せず同じ要求の内容差替と装飾品再取得を拒否しパーツ重複取得は保持する()
        {
            var grant=Grant(new[]{"a"},Counts("bar",2));Assert.IsTrue(session.TryGrantCustomization(catalog,grant));long revision=session.Snapshot.Revision;
            Assert.IsTrue(session.TryGrantCustomization(catalog,grant));Assert.AreEqual(revision,session.Snapshot.Revision);
            var bytes=File.ReadAllBytes(Store().ActivePath);
            Assert.IsFalse(session.TryGrantCustomization(catalog,new CustomizationGrant(grant.RequestId,new[]{"a"},Counts("bar",3))));
            Assert.IsFalse(session.TryGrantCustomization(catalog,Grant(new[]{"a"})));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));
            Assert.IsTrue(session.TryGrantCustomization(catalog,Grant(parts:Counts("bar",2))));Assert.AreEqual(4,session.CustomizationSnapshot.OwnedParts.Single().Count);
        }
        [Test]
        public void 装飾品は三枠が既定で同一品重複と未所持と枠超過を拒否する()
        {
            Assert.AreEqual(3,catalog.AccessorySlots);Assert.IsTrue(session.TryGrantCustomization(catalog,Grant(new[]{"a","b","c","d"})));
            Assert.IsFalse(session.TrySetCustomizationLoadout(catalog,new[]{"a","a"},null));Assert.IsFalse(session.TrySetCustomizationLoadout(catalog,new[]{"unknown"},null));Assert.IsFalse(session.TrySetCustomizationLoadout(catalog,new[]{"a","b","c","d"},null));
            Assert.IsTrue(session.TrySetCustomizationLoadout(catalog,new[]{"a","b","c"},null));Assert.AreEqual(3,Reload().CustomizationSnapshot.EquippedAccessoryIds.Length);
        }
        [Test]
        public void 攻略中は装備編集と装飾品パーツ取得を拒否し固定領域の解放は保存できる()
        {
            Acquire();running=true;var bytes=File.ReadAllBytes(Store().ActivePath);
            Assert.IsFalse(session.TrySetCustomizationLoadout(catalog,new[]{"a"},Array.Empty<PartPlacement>()));Assert.IsFalse(session.TryGrantCustomization(catalog,Grant(parts:Counts("bar"))));Assert.IsFalse(session.TryGrantCustomization(catalog,Grant(new[]{"c"})));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));
            Assert.IsTrue(session.TryGrantCustomization(catalog,Grant(unlocks:Counts("far"))));Assert.IsTrue(session.TryApplyCustomizationUnlock(catalog,"right"));Assert.IsTrue(session.TryApplyCustomizationUnlock(catalog,"far"));
            Assert.IsTrue(Reload().CustomizationSnapshot.UnlockedCells.Any(v=>v.X==3&&v.Y==1));Assert.IsEmpty(session.CustomizationSnapshot.Placements);
        }
        [TestCase(RunReturnOutcome.Cleared)][TestCase(RunReturnOutcome.Defeated)][TestCase(RunReturnOutcome.Retreated)]
        public void クリア敗北撤退の通常帰還処理で所持配置解放を保持して復帰する(RunReturnOutcome outcome)
        {
            Acquire();Assert.IsTrue(session.TrySetCustomizationLoadout(catalog,new[]{"a"},new[]{new PartPlacement("p","bar",new Vector2Int(0,0))}));
            var source=RunSource(outcome,out var departure);Assert.IsTrue(session.PrepareNextDeparture(departure,"fixture"),session.Error);running=true;
            Assert.IsTrue(session.TryApplyCustomizationUnlock(catalog,"right"));Assert.IsFalse(session.TrySetCustomizationLoadout(catalog,null,null));
            if(outcome==RunReturnOutcome.Cleared){Assert.IsTrue(session.Begin(source,"fixture",new ClearReturnSettings()),session.Error);Assert.IsTrue(session.ConfirmSelection());Assert.IsTrue(session.ConfirmReturn());}
            else Assert.IsTrue(session.EndWithoutRewards(source,"fixture",new ClearReturnSettings(),outcome),session.Error);
            running=false;var restored=Reload();Assert.AreEqual(outcome,restored.Snapshot.Flow.Outcome);CollectionAssert.AreEqual(new[]{"a"},restored.CustomizationSnapshot.EquippedAccessoryIds);Assert.AreEqual(4,restored.CustomizationSnapshot.UnlockedCells.Length);Assert.AreEqual(2,restored.CustomizationSnapshot.OwnedParts.Single(v=>v.Id=="bar").Count);
            Assert.IsTrue(restored.TryGetCustomizationLayout(catalog,out var layout,out var error),error);Assert.AreEqual(2,layout.OccupiedCells.Count);
        }
        [Test]
        public void クリア報酬だけは帰還途中で取得でき不一致の攻略IDと敗北報酬を拒否する()
        {
            var source=RunSource(RunReturnOutcome.Cleared,out var departure);Assert.IsTrue(session.PrepareNextDeparture(departure,"fixture"));running=true;Assert.IsTrue(session.Begin(source,"fixture",new ClearReturnSettings()));
            Assert.IsFalse(session.TryGrantCustomization(catalog,Grant(new[]{"a"},run:Id())));
            Assert.IsTrue(session.TryGrantCustomization(catalog,Grant(new[]{"a"},Counts("bar"),run:source.Snapshot.RunId)),session.Error);Assert.IsTrue(session.ConfirmSelection());Assert.IsTrue(session.ConfirmReturn());
            Assert.AreEqual(1,Reload().CustomizationSnapshot.OwnedParts.Single().Count);
        }
        private sealed class FaultIo:Olca.Unity.CheckpointFileIo
        {
            public bool Before,After;
            public override void Replace(string temporary,string destination,string backup){if(Before)throw new IOException("fixture before");base.Replace(temporary,destination,backup);if(After)throw new IOException("fixture after");}
        }
        [TestCase("grant",false)][TestCase("grant",true)][TestCase("unlock",false)][TestCase("unlock",true)][TestCase("layout",false)][TestCase("layout",true)]
        public void 書込失敗と完了後エラーは旧状態か確定状態だけを残し同じ計画を再試行する(string operation,bool after)
        {
            Acquire(); if(operation=="layout")Assert.IsTrue(session.TryApplyCustomizationUnlock(catalog,"right"));
            var io=new FaultIo{Before=!after,After=after};var failing=new ClearReturnSession(Store(io));var before=File.ReadAllBytes(Store().ActivePath);var grant=Grant(parts:Counts("bar",2));
            bool ok=operation=="grant"?failing.TryGrantCustomization(catalog,grant):operation=="unlock"?failing.TryApplyCustomizationUnlock(catalog,"right"):
                failing.TrySetCustomizationLoadout(catalog,new[]{"a"},new[]{new PartPlacement("p","bar",new Vector2Int(1,0))});
            Assert.AreEqual(after,ok);
            if(!after){Assert.IsTrue(failing.HasPendingSave);CollectionAssert.AreEqual(before,File.ReadAllBytes(Store().ActivePath));Assert.IsFalse(failing.TryApplyCustomizationUnlock(catalog,"right"));io.Before=false;Assert.IsTrue(failing.Retry(),failing.Error);}
            var restored=Reload();
            if(operation=="grant"){Assert.IsTrue(failing.TryGrantCustomization(catalog,grant));Assert.AreEqual(4,restored.CustomizationSnapshot.OwnedParts.Single(v=>v.Id=="bar").Count);Assert.AreEqual(2,restored.CustomizationSnapshot.Receipts.Length);}
            if(operation=="unlock"){Assert.IsTrue(failing.TryApplyCustomizationUnlock(catalog,"right"));Assert.AreEqual(4,restored.CustomizationSnapshot.UnlockedCells.Length);}
            if(operation=="layout"){Assert.IsTrue(restored.TryGetCustomizationLayout(catalog,out var layout,out var error),error);Assert.AreEqual(2,layout.OccupiedCells.Count);Assert.AreEqual("a",restored.CustomizationSnapshot.EquippedAccessoryIds.Single());}
        }
        [TestCase(RunReturnOutcome.Defeated)][TestCase(RunReturnOutcome.Retreated)]
        public void 敗北撤退からクリア報酬を装って所持を増やせない(RunReturnOutcome outcome)
        {
            var source=RunSource(outcome,out var departure);Assert.IsTrue(session.PrepareNextDeparture(departure,"fixture"));Assert.IsTrue(session.EndWithoutRewards(source,"fixture",new ClearReturnSettings(),outcome));
            var bytes=File.ReadAllBytes(Store().ActivePath);Assert.IsFalse(session.TryGrantCustomization(catalog,Grant(parts:Counts("bar"),run:source.Snapshot.RunId)));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));
        }
        [Test]
        public void 所持数の加算上限と未登録品の混入は部分的な取得を保存しない()
        {
            Assert.IsTrue(session.TryGrantCustomization(catalog,Grant(parts:Counts("bar",int.MaxValue))));var bytes=File.ReadAllBytes(Store().ActivePath);
            Assert.IsFalse(session.TryGrantCustomization(catalog,Grant(new[]{"a"},Counts("bar",1))));
            Assert.IsFalse(session.TryGrantCustomization(catalog,Grant(new[]{"a"},Counts("unknown",1))));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));Assert.IsEmpty(session.CustomizationSnapshot.OwnedAccessoryIds);
        }
        [DataContract] private sealed class Envelope
        {
            [DataMember]public int Version;[DataMember]public string Payload;[DataMember]public string Sha256;
        }
        private void WritePayload(string payload)=>File.WriteAllBytes(Store().ActivePath,PortableCheckpointJson.Serialize(new Envelope{Version=1,Payload=payload,Sha256=PortableCheckpointJson.Hash(Encoding.UTF8.GetBytes(payload))}));
        [Test]
        public void 新項目を欠く旧保存と空の部分状態を既定空として読み読込だけでは書き換えない()
        {
            var payload=Encoding.UTF8.GetString(PortableCheckpointJson.Serialize(session.Snapshot));Assert.IsTrue(payload.Contains("\"Customization\":null"));payload=Regex.Replace(payload,",?\"Customization\":null","");WritePayload(payload);var bytes=File.ReadAllBytes(Store().ActivePath);
            var restored=Reload();Assert.IsTrue(restored.Ready);Assert.IsEmpty(restored.CustomizationSnapshot.OwnedAccessoryIds);Assert.IsEmpty(restored.CustomizationSnapshot.UnlockedCells);Assert.AreEqual(20,restored.Snapshot.Owned.Sum(v=>v.Count));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));
            var missing=PortableCheckpointJson.Deserialize<CustomizationProgress>(Encoding.UTF8.GetBytes("{}"));CustomizationProgress.Validate(missing);Assert.IsEmpty(missing.Copy().Placements);
        }
        [Test]
        public void 定義未登録の読込でも所持を捨てず異なる盤面定義で上書きしない()
        {
            Acquire();Assert.IsTrue(session.TryApplyCustomizationUnlock(catalog,"right"));var bytes=File.ReadAllBytes(Store().ActivePath);var restored=Reload();Assert.AreEqual(2,restored.CustomizationSnapshot.OwnedAccessoryIds.Length);
            var other=new CustomizationCatalog("different",catalog.Board,catalog.Accessories,catalog.Parts,new Dictionary<string,IEnumerable<Vector2Int>>());
            Assert.IsFalse(restored.TryGetCustomizationLayout(other,out _,out _));Assert.IsFalse(restored.TryApplyCustomizationUnlock(other,"right"));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));
        }
        [Test]
        public void 入力と公開スナップショットを変更しても確定状態を変更しない()
        {
            var ids=new[]{"a"};var counts=Counts("bar",2);var grant=Grant(ids,counts);ids[0]="unknown";counts["bar"]=99;Assert.IsTrue(session.TryGrantCustomization(catalog,grant));
            var state=session.CustomizationSnapshot;state.OwnedAccessoryIds[0]="unknown";state.OwnedParts[0].Count=99;Assert.AreEqual("a",session.CustomizationSnapshot.OwnedAccessoryIds[0]);Assert.AreEqual(2,session.CustomizationSnapshot.OwnedParts[0].Count);
        }
        [TestCase("count")][TestCase("equipped")][TestCase("cell")][TestCase("rotation")][TestCase("receipt")][TestCase("applied")]
        public void 不正な新状態は保存を黙って初期化せず読込拒否して原本を保持する(string kind)
        {
            Acquire();var profile=session.Snapshot;var state=profile.Customization;
            if(kind=="count")state.OwnedParts[0].Count=0;
            if(kind=="equipped")state.EquippedAccessoryIds=new[]{"unknown"};
            if(kind=="cell")state.UnlockedCells=new[]{new CustomizationCell{X=-1,Y=0}};
            if(kind=="rotation")state.Placements=new[]{new SavedPartPlacement{InstanceId="p",PartId="bar",X=0,Y=0,Rotation=(PartRotation)45}};
            if(kind=="receipt")state.Receipts[0].Fingerprint="bad";
            if(kind=="applied")state.AppliedUnlockItemIds=new[]{"unknown"};
            WritePayload(Encoding.UTF8.GetString(PortableCheckpointJson.Serialize(profile)));var bytes=File.ReadAllBytes(Store().ActivePath);var restored=Reload();Assert.IsFalse(restored.Ready);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(Store().ActivePath));
        }
        private static RestoredCheckpoint RunSource(RunReturnOutcome outcome,out CheckpointDto departure)
        {
            var map=DungeonConfig.CreateTestDungeon(8,30,2);var deck=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck();var checkpoints=DungeonCheckpointSession.CreateDefault(map,deck);
            var game=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:checkpoints);departure=checkpoints.Store.Read();
            game.OnCardRewardPresented+=_=>game.SelectCardReward(null);
            if(outcome!=RunReturnOutcome.Retreated)for(int i=0;i<500&&game.CurrentPhase!=GamePhase.DungeonCleared&&game.CurrentPhase!=GamePhase.GameOver;i++){
                game.TickFrame(17);if(game.CurrentBattle!=null){if(outcome==RunReturnOutcome.Defeated)game.CurrentBattle.Player.TakeDamage(99999);else foreach(var enemy in game.CurrentBattle.Enemies.ToArray())if(!enemy.IsDead)enemy.TakeDamage(enemy.MaxHp,ElementType.None);}game.ResumeAfterBattle();}
            var dto=outcome==RunReturnOutcome.Retreated?departure:checkpoints.Store.Read();Assert.IsTrue(CheckpointCodec.TryRestore(dto,checkpoints.Definitions,out var result,out var error),error);return result;
        }
    }
}
