using System;
using System.Reflection;
using System.Linq;
using Olca.Core.Logic.Battle;
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
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)] public void 外界持込みの未対応予約と未確定HPframeは無変更で拒否する(int kind)
        {
            var actor=new PlayerCharacter(100);actor.SetHp(37);if(kind==0)actor.ApplyParalysis(30);if(kind==1)actor.ApplyConfusion(30);if(kind==2)actor.ApplyBlindness(30);var flags=BindingFlags.Instance|BindingFlags.NonPublic;if(kind==3)typeof(PlayerCharacter).GetMethod("BeginHpFrame",flags).Invoke(actor,null);if(kind>=4)typeof(PlayerCharacter).GetMethod("Begin"+new[]{"Paralysis","Confusion","Blindness"}[kind-4]+"Frame",flags).Invoke(actor,new object[]{false});
            try{Assert.Throws<ArgumentException>(()=>OutsideDepartureVitals.Capture(actor,false));Assert.AreEqual(37,actor.CurrentHp);Assert.AreEqual(100,actor.MaxHp);}finally{if(kind==3){typeof(PlayerCharacter).GetMethod("CommitHpFrame",flags).Invoke(actor,null);typeof(PlayerCharacter).GetMethod("PublishHpFrame",flags).Invoke(actor,null);}if(kind>=4)typeof(PlayerCharacter).GetMethod("End"+new[]{"Paralysis","Confusion","Blindness"}[kind-4]+"Frame",flags).Invoke(actor,new object[]{false});}
        }
        private static PlayerTimedAilmentsDto Ailments136()=>new PlayerTimedAilmentsDto{Paralysis=new PlayerTimedAilmentDto{RemainingUnits=1475,DisplayFrames=30},Confusion=new PlayerTimedAilmentDto{RemainingUnits=1425,DisplayFrames=29},Blindness=new PlayerTimedAilmentDto{RemainingUnits=1375,DisplayFrames=28}};
        [Test] public void 時間状態は端数と耐性適用済み値とコピー分離をcheckpointへ保持する()
        {
            var actor=new PlayerCharacter(100);actor.SetHp(37);var state=Ailments136();actor.RestoreTimedAilments(state);state.Paralysis.RemainingUnits=0;Assert.AreEqual(1475,actor.CaptureTimedAilments().Paralysis.RemainingUnits);
            var map=DungeonConfig.CreateTestDungeon(6,1,2);var defs=new CheckpointDefinitions("ailment136",map);var session=new DungeonCheckpointSession(defs,new MemoryCheckpointStore(defs));var factory=new DungeonCoreFactory(new UnityRandomProvider(7),new UnityLogger(),()=>new SimpleCardFactory());factory.CreatePreview(map,actor,new Deck(),session);var dto=session.Store.Read();Assert.IsTrue(CheckpointCodec.TryRestore(dto,defs,out var loaded,out var error),error);Assert.AreEqual(37,loaded.Player.CurrentHp);Assert.IsTrue(PlayerTimedAilmentsDto.Same(actor.CaptureTimedAilments(),loaded.Player.CaptureTimedAilments()));dto.Player.TimedAilments.Confusion.RemainingUnits=1;Assert.AreEqual(1425,loaded.Player.CaptureTimedAilments().Confusion.RemainingUnits);
            var resistant=new PlayerCharacter(100,statusResistances:new TimedStatusResistances(0,100,100,100));resistant.RestoreTimedAilments(actor.CaptureTimedAilments());Assert.IsTrue(resistant.IsParalyzed&&resistant.IsConfused&&resistant.IsBlind);
            using(var outside=new OutsideExplorationSession()){outside.TryInitializePlayer(()=>resistant);using(var lease=outside.AttachPresentation()){outside.Core.TickFrame(0);outside.AdvancePlayerFrame();}}Assert.AreEqual(1425,resistant.CaptureTimedAilments().Paralysis.RemainingUnits);Assert.AreEqual(1375,resistant.CaptureTimedAilments().Confusion.RemainingUnits);Assert.AreEqual(1325,resistant.CaptureTimedAilments().Blindness.RemainingUnits);
        }
        [Test] public void 未反映付与と解除は復元だけでは実行せず次論理frameに適用する()
        {
            var source=new PlayerCharacter(100);source.ApplyParalysis(30);source.ApplyConfusion(31);source.ApplyBlindness(32);var saved=source.CaptureTimedAilments();var target=new PlayerCharacter(100);target.RestoreTimedAilments(saved);Assert.IsFalse(target.IsParalyzed||target.IsConfused||target.IsBlind);Assert.AreEqual(30,target.CaptureTimedAilments().Paralysis.PendingFrames);
            using(var outside=new OutsideExplorationSession()){outside.TryInitializePlayer(()=>target);using(var lease=outside.AttachPresentation()){outside.Core.TickFrame(0);outside.AdvancePlayerFrame();Assert.IsTrue(target.IsParalyzed&&target.IsConfused&&target.IsBlind);Assert.AreEqual(29,target.ParalysisRemainingFrames);target.ClearParalysis();target.ClearConfusion();target.ClearBlindness();var clearing=target.CaptureTimedAilments();var restored=new PlayerCharacter(100);restored.RestoreTimedAilments(clearing);Assert.IsTrue(PlayerTimedAilmentsDto.Same(clearing,restored.CaptureTimedAilments()));using(var resumed=new OutsideExplorationSession()){resumed.TryInitializePlayer(()=>restored);using(var resumedLease=resumed.AttachPresentation()){resumed.Core.TickFrame(0);resumed.AdvancePlayerFrame();}}Assert.IsNull(restored.CaptureTimedAilments());Assert.IsTrue(target.IsParalyzed&&target.IsConfused&&target.IsBlind);outside.Core.TickFrame(0);outside.AdvancePlayerFrame();Assert.IsNull(target.CaptureTimedAilments());}}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)] public void 不正な時間状態は復元前に拒否し既存個体を変えない(int kind)
        {
            var actor=new PlayerCharacter(100);actor.RestoreTimedAilments(Ailments136());var before=actor.CaptureTimedAilments();var invalid=Ailments136();if(kind==0)invalid.Version=2;if(kind==1)invalid.Blindness=null;if(kind==2)invalid.Paralysis.RemainingUnits=-1;if(kind==3)invalid.Confusion.PendingFrames=0;if(kind==4)invalid.Blindness.InterruptPending=true;Assert.Throws<ArgumentException>(()=>actor.RestoreTimedAilments(invalid));Assert.IsTrue(PlayerTimedAilmentsDto.Same(before,actor.CaptureTimedAilments()));
        }
        [TestCase(false)] [TestCase(true)] public void 通常生成と凍結preview_既存の加工System構成を区別する(bool preview)
        {
            int definitions=0;var factory=new DungeonCoreFactory(new UnityRandomProvider(7),new UnityLogger(),()=>{definitions++;return new SimpleCardFactory();});
            var settings=new ClearReturnSettings{BaseMaxHp=37,HpPerLevel=6};var player=factory.CreatePlayer(settings,3,2,20);
            Assert.AreEqual(49,player.MaxHp);var map=DungeonConfig.CreateTestDungeon(6,1,2);var deck=new Deck();
            var core=preview?factory.CreatePreview(map,player,deck,null):factory.Create(new DungeonCoreFactory.Launch{Map=map,Player=player,Deck=deck});
            Assert.AreEqual(preview?0:1,definitions);
            foreach(string name in new[]{"exceedSystem","tuningSystem","fusionSystem"}){
                var value=core.Dungeon.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(core.Dungeon);
                Assert.AreEqual(!preview,value!=null,name);
            }
        }
        private static T ProcessingSystem<T>(DungeonCoreFactory.Processing processing,string name)
            =>(T)typeof(DungeonCoreFactory.Processing).GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(processing);
        private static object Field(object value,string name)
            =>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
        [Test] public void 加工組立内は同じ定義元を共有し別組立と生成カード個体は分離する()
        {
            int calls=0;
            var factory=new DungeonCoreFactory(new SeededBattleRandom(7),new UnityLogger(),()=>{
                calls++;return new SimpleCardFactory(new CardPoiseSettings(15+calls,20+calls,30+calls));
            });
            var first=factory.CreateProcessing();Assert.AreEqual(1,calls);
            var exceed=ProcessingSystem<ExceedSystem>(first,"Exceed");var tuning=ProcessingSystem<TuningSystem>(first,"Tuning");var fusion=ProcessingSystem<FusionSystem>(first,"Fusion");
            Assert.AreSame(first.Cards,Field(exceed,"cardFactory"));Assert.AreSame(first.Cards,((Delegate)Field(tuning,"definitionProvider")).Target);
            Assert.AreSame(first.Cards,Field(fusion,"cardFactory"));Assert.AreSame(first.Cards,Field(Field(fusion,"recipeProvider"),"cardFactory"));
            // delegateが組立ごとに別設定を返しても、一つの組立内の融合元定義は一致する。
            var result=fusion.Execute(new[]{first.Cards.CreateById(SimpleCardFactory.Sword),first.Cards.CreateById(SimpleCardFactory.Bomb)});
            Assert.IsTrue(result.IsSuccess,result.ErrorMessage);Assert.AreEqual(CollectionDisplay.DefinitionHash(first.Cards.CreateById(SimpleCardFactory.FireSword)),CollectionDisplay.DefinitionHash(result.ResultCard));
            var a=first.Cards.CreateById(SimpleCardFactory.Sword);var b=first.Cards.CreateById(SimpleCardFactory.Sword);
            Assert.AreNotSame(a,b);Assert.AreNotSame(a.Modifier,b.Modifier);a.Modifier.BonusDamage=123;
            Assert.AreEqual(30,b.GetFinalPower());Assert.AreEqual(30,first.Cards.CreateById(SimpleCardFactory.Sword).GetFinalPower());
            var second=factory.CreateProcessing();Assert.AreEqual(2,calls);Assert.AreNotSame(first.Cards,second.Cards);
            Assert.AreEqual(16,first.Cards.CreateById(SimpleCardFactory.Sword).PoiseDamage);Assert.AreEqual(17,second.Cards.CreateById(SimpleCardFactory.Sword).PoiseDamage);
        }
        [Test] public void 共有後も既存定義hashと加工結果と未登録拒否を維持する()
        {
            var expected=new SimpleCardFactory();var factory=new DungeonCoreFactory(new SeededBattleRandom(7),new UnityLogger(),()=>new SimpleCardFactory());var p=factory.CreateProcessing();
            CollectionAssert.AreEqual(expected.CollectionCards.Select(CollectionDisplay.DefinitionHash).OrderBy(x=>x),p.Cards.CollectionCards.Select(CollectionDisplay.DefinitionHash).OrderBy(x=>x));
            var exceed=ProcessingSystem<ExceedSystem>(p,"Exceed");var tuning=ProcessingSystem<TuningSystem>(p,"Tuning");var fusion=ProcessingSystem<FusionSystem>(p,"Fusion");
            var sword=p.Cards.CreateById(SimpleCardFactory.Sword);string source=CollectionDisplay.DefinitionHash(sword);
            var upgraded=exceed.Execute(sword,p.Cards.CreateById(SimpleCardFactory.Sword));Assert.IsTrue(upgraded.IsSuccess,upgraded.ErrorMessage);Assert.AreEqual(45,upgraded.ResultCard.GetFinalPower());
            Assert.AreEqual(CollectionDisplay.DefinitionHash(expected.CreateExceeded(expected.CreateById(SimpleCardFactory.Sword),ExceedLevel.Lv1)),CollectionDisplay.DefinitionHash(upgraded.ResultCard));
            var tuned=tuning.Execute(sword,p.Cards.CreateById(SimpleCardFactory.Tuning));Assert.IsTrue(tuned.IsSuccess,tuned.ErrorMessage);Assert.AreEqual(40,tuned.ResultCard.GetFinalPower());Assert.AreEqual(source,CollectionDisplay.DefinitionHash(sword));
            var fused=fusion.Execute(new[]{sword,p.Cards.CreateById(SimpleCardFactory.Bomb)});Assert.IsTrue(fused.IsSuccess,fused.ErrorMessage);Assert.AreEqual(60,fused.ResultCard.GetFinalPower());Assert.AreEqual(CollectionDisplay.DefinitionHash(expected.CreateById(SimpleCardFactory.FireSword)),CollectionDisplay.DefinitionHash(fused.ResultCard));
            Assert.IsTrue(exceed.HasResultDefinition(p.Cards.CreateById(SimpleCardFactory.Bomb)));Assert.IsFalse(exceed.HasResultDefinition(p.Cards.CreateById(SimpleCardFactory.Heal)));Assert.IsFalse(fusion.Validate(new[]{p.Cards.CreateById(SimpleCardFactory.Bomb),p.Cards.CreateById(SimpleCardFactory.Shield)}).IsValid);
        }
        [TestCase(37)] [TestCase(83)] public void 報酬表示は確定済みHPを更新し枚数非表示と保存と進行を変えない(int hp)
        {
            var host=new UnityEngine.GameObject("reward-hud-test");host.SetActive(false);
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var hudObject=new UnityEngine.GameObject("hud",typeof(UnityEngine.RectTransform));hudObject.transform.SetParent(host.transform,false);
            var hud=hudObject.AddComponent<Olca.Presentation.Dungeon.DungeonHudView>();
            var countObject=new UnityEngine.GameObject("deck",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));countObject.transform.SetParent(hud.transform,false);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            try {
                typeof(Olca.Presentation.Dungeon.DungeonHudView).GetField("deckCountText",flags).SetValue(hud,countObject.GetComponent<UnityEngine.UI.Text>());
                var cards=new SimpleCardFactory();var deck=cards.CreateStarterDeck();var map=DungeonConfig.CreateTestDungeon(8,1,3);var checkpoint=DungeonCheckpointSession.CreateDefault(map,deck);
                var factory=new DungeonCoreFactory(new SeededBattleRandom(173),new Olca.Tests.Mocks.MockLogger(),()=>cards);var actor=new PlayerCharacter(100);var core=factory.CreatePreview(map,actor,deck,checkpoint);
                typeof(Olca.Application.Dungeon.DungeonBattleController).GetField("manager",flags).SetValue(controller,core);
                typeof(Olca.Application.Dungeon.DungeonBattleController).GetField("dungeonHudView",flags).SetValue(controller,hud);
                var update=typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("UpdateViews",flags);update.Invoke(controller,null);
                var label=(UnityEngine.UI.Text)typeof(Olca.Presentation.Dungeon.DungeonHudView).GetField("hpText",flags).GetValue(hud);Assert.AreEqual("HP: 100 / 100",label.text);
                // 状態変更は表示だけを検証するfixture。通常撃破の実証には使わない。
                actor.SetHp(hp);typeof(DungeonBattleManager).GetField("currentPhase",flags).SetValue(core,GamePhase.CardReward);
                var before=PortableCheckpointJson.Serialize(checkpoint.Store.Read());float position=core.Dungeon.PlayerPosition;
                update.Invoke(controller,null);update.Invoke(controller,null);
                Assert.AreEqual($"HP: {hp} / 100",label.text);Assert.IsFalse(countObject.activeSelf);Assert.AreEqual(hp,actor.CurrentHp);Assert.AreEqual(GamePhase.CardReward,core.CurrentPhase);Assert.AreEqual(position,core.Dungeon.PlayerPosition);CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(checkpoint.Store.Read()));
            } finally { controller.StopManagedHost();UnityEngine.Object.DestroyImmediate(host); }
        }
        [TestCase(false)] [TestCase(true)] public void 復帰候補作成_現Coreと保存を変えず確定済み敗北は拒否する(bool ended)
        {
            var cards=new SimpleCardFactory();var deck=cards.CreateStarterDeck();var map=DungeonConfig.CreateTestDungeon(6,1,2);
            int definitions=0;var checkpoint=DungeonCheckpointSession.CreateDefault(map,deck);var factory=new DungeonCoreFactory(new UnityRandomProvider(7),new UnityLogger(),()=>{definitions++;return new SimpleCardFactory();});
            var current=factory.CreatePreview(map,new PlayerCharacter(100),deck,checkpoint);var before=PortableCheckpointJson.Serialize(checkpoint.Store.Read());
            var flow=new DungeonCheckpointFlow(factory);var receipts=ended?new[]{new ClearReceipt{RunId=checkpoint.RunId,Outcome=RunReturnOutcome.Defeated}}:Array.Empty<ClearReceipt>();
            bool prepared=flow.TryPrepare(checkpoint,null,null,()=>receipts,out var result,out var error);
            Assert.AreEqual(!ended,prepared,error);Assert.AreEqual(1,definitions);Assert.IsFalse(current.IsSuspended);CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(checkpoint.Store.Read()));
            if(ended)Assert.IsNull(result);else{Assert.AreNotSame(current,result.Core);Assert.AreSame(checkpoint,result.Checkpoint);Assert.IsNull(result.Route);result.Core.SuspendForCheckpoint();}
        }
    }
}

namespace Olca.Tests.Application
{
    using System.Collections;
    using System.Collections.Generic;
    using Olca.Application.Dungeon;
    public sealed class SavedDeckResolutionTests
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private sealed class TimingCounter:IReadOnlyDictionary<string,PlayerAttackTiming>
        {
            private readonly Dictionary<string,PlayerAttackTiming> values=new Dictionary<string,PlayerAttackTiming>();
            public int Enumerations;
            public void Set(string id,PlayerAttackTiming value)=>values[id]=value;
            public PlayerAttackTiming this[string key]=>values[key];public IEnumerable<string> Keys=>values.Keys;public IEnumerable<PlayerAttackTiming> Values=>values.Values;public int Count=>values.Count;
            public bool ContainsKey(string key)=>values.ContainsKey(key);public bool TryGetValue(string key,out PlayerAttackTiming value)=>values.TryGetValue(key,out value);
            public IEnumerator<KeyValuePair<string,PlayerAttackTiming>> GetEnumerator(){Enumerations++;return values.GetEnumerator();}IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        }
        private sealed class Store:IClearReturnStore,INewClearReturnStore
        {
            public ClearReturnProfile Value=new ClearReturnProfile();public int Writes;public bool IsNewProfile=>Writes==0;
            public ClearReturnProfile Read(out string error){error=null;return Value.Copy();}
            public bool TryWrite(long revision,ClearReturnProfile next,out string error){error=null;Assert.AreEqual(Value.Revision,revision);ClearReturnProfile.Validate(next);Value=next.Copy();Writes++;return true;}
        }
        private sealed class Context:IDisposable
        {
            public readonly UnityEngine.GameObject Host=new UnityEngine.GameObject("SavedDeckResolution");
            public readonly TimingCounter Timings=new TimingCounter();public readonly Store Store=new Store();
            public readonly DungeonBattleController Controller;public readonly ClearReturnSession Session;public readonly string[] Hashes;
            public SavedDepartureDeck Saved=>Session.Snapshot.SavedDecks.Single();
            public Context(){Host.SetActive(false);try{Controller=Host.AddComponent<DungeonBattleController>();typeof(DungeonBattleController).GetField("configuredCardTimings",Private).SetValue(Controller,Timings);
                var factory=(SimpleCardFactory)typeof(DungeonBattleController).GetMethod("CreateCardFactory",Private).Invoke(Controller,null);var starter=factory.CreateStarterDeck().GetAllCards();Hashes=starter.Select(CollectionDisplay.DefinitionHash).ToArray();Session=new ClearReturnSession(Store,definitions:factory.CollectionCards);Assert.IsTrue(Session.InitializeStarter(starter),Session.Error);typeof(DungeonBattleController).GetField("clearReturnSession",Private).SetValue(Controller,Session);Timings.Enumerations=0;}catch{Dispose();throw;}}
            public bool Resolve(SavedDepartureDeck saved,out Deck deck,out string error){var args=new object[]{saved,null,null};bool result=(bool)typeof(DungeonBattleController).GetMethod("TryResolveSavedDeck",Private).Invoke(Controller,args);deck=(Deck)args[1];error=(string)args[2];return result;}
            public void Replace(ClearReturnProfile profile){ClearReturnProfile.Validate(profile);Store.Value=profile.Copy();Assert.IsTrue(Session.Reload(),Session.Error);Timings.Enumerations=0;}
            public void Dispose(){try{if(Controller!=null){Controller.StopManagedHost();foreach(var name in new[]{"logicalLoop","dialogueLoop","explorationResumeLoop"}){var loop=typeof(DungeonBattleController).GetField(name,Private).GetValue(Controller);Assert.IsTrue((bool)typeof(UnityLogicalLoop).GetField("disposed",Private).GetValue(loop),name+"未解放");}}}finally{UnityEngine.Object.DestroyImmediate(Host);}}
        }
        [Test] public void 保存20枚の解決は定義組立一回で順序と個体分離と保存を保つ()
        {
            using(var c=new Context()){
                var before=PortableCheckpointJson.Encode(c.Session.Snapshot);int writes=c.Store.Writes;Assert.IsTrue(c.Resolve(c.Saved,out var deck,out var error),error);Assert.AreEqual(1,c.Timings.Enumerations);
                var cards=deck.GetAllCards();CollectionAssert.AreEqual(c.Hashes,cards.Select(CollectionDisplay.DefinitionHash));Assert.AreEqual(20,cards.Count);
                for(int i=0;i<cards.Count;i++)for(int j=i+1;j<cards.Count;j++){Assert.AreNotSame(cards[i],cards[j]);Assert.AreNotSame(cards[i].Modifier,cards[j].Modifier);}
                cards[0].Modifier.BonusDamage=7;Assert.IsTrue(cards.Skip(1).All(x=>x.Modifier.BonusDamage==0));
                c.Timings.Enumerations=0;Assert.IsTrue(c.Resolve(c.Saved,out var fresh,out error),error);Assert.AreEqual(1,c.Timings.Enumerations);CollectionAssert.AreEqual(c.Hashes,fresh.GetAllCards().Select(CollectionDisplay.DefinitionHash));
                CollectionAssert.AreEqual(before,PortableCheckpointJson.Encode(c.Session.Snapshot));Assert.AreEqual(writes,c.Store.Writes);
            }
        }
        [Test] public void 構築条件不成立は定義を組み立てず拒否する()
        {
            using(var c=new Context()){
                var before=PortableCheckpointJson.Encode(c.Session.Snapshot);Assert.IsFalse(c.Resolve(null,out _,out _));
                var shortDeck=c.Saved;shortDeck.Cards=shortDeck.Cards.Take(19).ToArray();Assert.IsFalse(c.Resolve(shortDeck,out _,out _));
                var unknown=c.Saved;unknown.Cards[0].Id="unregistered";Assert.IsFalse(c.Resolve(unknown,out _,out _));Assert.AreEqual(0,c.Timings.Enumerations);CollectionAssert.AreEqual(before,PortableCheckpointJson.Encode(c.Session.Snapshot));
            }
        }
        [Test] public void 保存の未登録hashは同IDの別定義へ置換せず拒否する()
        {
            using(var c=new Context()){
                var profile=c.Session.Snapshot;string missing=new string('F',64);profile.Owned.Single(x=>x.Id==SimpleCardFactory.Sword).DefinitionHash=missing;
                foreach(var slot in profile.SavedDecks.Single().Cards.Where(x=>x.Id==SimpleCardFactory.Sword))slot.DefinitionKey=missing;c.Replace(profile);
                var before=PortableCheckpointJson.Encode(c.Session.Snapshot);Assert.IsFalse(c.Resolve(c.Saved,out var deck,out var error));Assert.IsNull(deck);StringAssert.Contains("未特定",error);Assert.AreEqual(1,c.Timings.Enumerations);CollectionAssert.AreEqual(before,PortableCheckpointJson.Encode(c.Session.Snapshot));
            }
        }
        [Test] public void 旧メタデータkeyの保存も既存の定義と順序で解決する()
        {
            using(var c=new Context()){
                var profile=c.Session.Snapshot;foreach(var row in profile.Owned)row.DefinitionHash=null;
                foreach(var slot in profile.SavedDecks.Single().Cards)slot.DefinitionKey=OwnedDefinition.Key(profile.Owned.Single(x=>x.Id==slot.Id&&x.Level==slot.Level));c.Replace(profile);
                var before=PortableCheckpointJson.Encode(c.Session.Snapshot);Assert.IsTrue(c.Resolve(c.Saved,out var deck,out var error),error);Assert.AreEqual(1,c.Timings.Enumerations);CollectionAssert.AreEqual(c.Hashes,deck.GetAllCards().Select(CollectionDisplay.DefinitionHash));CollectionAssert.AreEqual(before,PortableCheckpointJson.Encode(c.Session.Snapshot));
            }
        }
        [Test] public void 別の解決ではカタログを更新し以前の定義をcacheしない()
        {
            using(var c=new Context()){
                Assert.IsTrue(c.Resolve(c.Saved,out _,out var error),error);c.Timings.Set(SimpleCardFactory.Sword,new PlayerAttackTiming(1,2));c.Timings.Enumerations=0;
                var before=PortableCheckpointJson.Encode(c.Session.Snapshot);Assert.IsFalse(c.Resolve(c.Saved,out _,out error));StringAssert.Contains("未特定",error);Assert.AreEqual(1,c.Timings.Enumerations);CollectionAssert.AreEqual(before,PortableCheckpointJson.Encode(c.Session.Snapshot));
            }
        }
    }
}

namespace Olca.Tests.Application
{
    public sealed class ExplorationDraftProjectionTests
    {
        private static Card[] Project(ClearReturnProfile source,Card[] options,params SavedDeckCard[] slots)
            =>(Card[])typeof(ExplorationDeckLibrary).GetMethod("ResolveDraftCards",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{source,options,slots});
        private static OwnedCardCount Owned(Card card)=>new OwnedCardCount{Id=card.Id,Name=card.Name,Rarity=card.Rarity,Level=card.ExceedLevel,DefinitionHash=CollectionDisplay.DefinitionHash(card),Count=4};
        private static SavedDeckCard Slot(OwnedCardCount row)=>new SavedDeckCard{Id=row.Id,Level=row.Level,DefinitionKey=OwnedDefinition.Key(row)};
        [Test] public void 探索draft投影_20枚の順序hashと候補からの個体分離を保つ()
        {
            var originals=new SimpleCardFactory().CreateStarterDeck().GetAllCards();var options=originals.GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First()).ToArray();var source=new ClearReturnProfile{Owned=options.Select(Owned).ToArray()};var slots=originals.Select(c=>Slot(source.Owned.Single(o=>OwnedDefinition.CardMatches(o,c)))).ToArray();var before=PortableCheckpointJson.Encode(source);
            var result=Project(source,options,slots);CollectionAssert.AreEqual(originals.Select(CollectionDisplay.DefinitionHash),result.Select(CollectionDisplay.DefinitionHash));
            for(int i=0;i<result.Length;i++){Assert.IsFalse(options.Any(o=>ReferenceEquals(o,result[i])||ReferenceEquals(o.Modifier,result[i].Modifier)));for(int j=0;j<i;j++){Assert.AreNotSame(result[i],result[j]);Assert.AreNotSame(result[i].Modifier,result[j].Modifier);}}
            result[0].Modifier.BonusDamage=99;Assert.IsTrue(options.All(c=>c.Modifier.BonusDamage==0));Assert.IsTrue(result.Skip(1).All(c=>c.Modifier.BonusDamage==0));CollectionAssert.AreEqual(before,PortableCheckpointJson.Encode(source));
        }
        [Test] public void 探索draft投影_未解決候補と曖昧所持と未知slotを置換しない()
        {
            var sword=new SimpleCardFactory().CreateById(SimpleCardFactory.Sword);var row=Owned(sword);var source=new ClearReturnProfile{Owned=new[]{row}};
            Assert.IsNull(Project(source,new Card[]{null},Slot(row)).Single());Assert.IsNull(Project(source,new[]{sword},new SavedDeckCard{Id="unknown",Level=row.Level}).Single());Assert.IsNull(Project(source,new[]{sword},new SavedDeckCard[]{null}).Single());
            source.Owned=new[]{row,row.Copy()};Assert.IsNull(Project(source,new[]{sword,sword},new SavedDeckCard{Id=row.Id,Level=row.Level}).Single());
        }
        [Test] public void 探索draft投影_同IDの異なる定義と旧keyを対応する候補へ結ぶ()
        {
            var factory=new SimpleCardFactory();var a=factory.CreateById(SimpleCardFactory.Sword);var b=a.Clone();b.Modifier.BonusDamage=7;var first=Owned(a);var second=Owned(b);var source=new ClearReturnProfile{Owned=new[]{first,second}};
            var cards=Project(source,new[]{a,b},Slot(second),Slot(first));Assert.AreEqual(CollectionDisplay.DefinitionHash(b),CollectionDisplay.DefinitionHash(cards[0]));Assert.AreEqual(CollectionDisplay.DefinitionHash(a),CollectionDisplay.DefinitionHash(cards[1]));
            source.Owned=new[]{first};first.DefinitionHash=null;var legacy=Project(source,new[]{a},Slot(first)).Single();Assert.AreEqual(CollectionDisplay.DefinitionHash(a),CollectionDisplay.DefinitionHash(legacy));
        }
        [Test] public void 探索draft投影_次の描画で渡された候補を使い前描画へ保持しない()
        {
            var card=new SimpleCardFactory().CreateById(SimpleCardFactory.Sword);var row=Owned(card);var source=new ClearReturnProfile{Owned=new[]{row}};var slot=Slot(row);var first=Project(source,new[]{card},slot);Assert.NotNull(first[0]);Assert.IsNull(Project(source,new Card[]{null},slot)[0]);var next=Project(source,new[]{card},slot);Assert.AreNotSame(first[0],next[0]);Assert.AreEqual(CollectionDisplay.DefinitionHash(first[0]),CollectionDisplay.DefinitionHash(next[0]));
        }
    }
}

namespace Olca.Tests.Application
{
    public sealed class TrialDungeonRouteTests
    {
        private static string CatalogHash(CheckpointDefinitions value)=>(string)typeof(CheckpointDefinitions).GetProperty("CardCatalogHash",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
        private static CheckpointDefinitions LegacyCatalog(SimpleCardFactory cards,Deck deck){var map=DungeonConfig.CreateTestDungeon(8,100,100);var shop=new DungeonVisitDefinition(1,true,new[]{SimpleCardFactory.Sword,SimpleCardFactory.Bomb,SimpleCardFactory.Heal}.Select(id=>new DungeonVisitOption(id,10,cards.CreateById(id))).ToArray(),3);map=new DungeonConfig(map.DungeonId,map.DungeonName,map.TotalCellCount,map.MapCells,visits:new[]{shop});var defs=DungeonCheckpointSession.CreateDefault(map,deck).Definitions;var hashes=new System.Collections.Generic.HashSet<string>(defs.CollectionDefinitions.Select(CollectionDisplay.DefinitionHash));foreach(var card in cards.CollectionCards)if(hashes.Add(CollectionDisplay.DefinitionHash(card)))defs.RegisterCard("product/catalog/v1/"+CollectionDisplay.DefinitionHash(card),card);return defs;}
        private static FloorSequenceDefinitions Definitions(SimpleCardFactory cards,Deck deck)=>DungeonCoreFactory.CreateTrialRoute(LegacyCatalog(cards,deck),cards,new EnemyPoiseDefinition(30,18,180),new EnemyPoiseDefinition(60,18,180));
        [Test] public void 試用二階層は既存敵性能と固定配置で最終階層だけボスを持つ()
        {
            var cards=new SimpleCardFactory();var deck=cards.CreateStarterDeck();var before=deck.GetAllCards().Select(CollectionDisplay.DefinitionHash).ToArray();var route=Definitions(cards,deck);
            Assert.AreEqual(2,route.FloorCount);Assert.AreEqual("trial-two-floor-v1",route.RouteId);Assert.AreEqual("trial-v1",route.Revision);
            for(int i=0;i<2;i++){var map=route.CreateInitialMap(i);Assert.AreEqual(i==0?14:18,map.TotalCellCount);StringAssert.Contains("カリ",map.DungeonName);CollectionAssert.AreEqual(i==0?new[]{3,9}:new[]{3,9,17},map.MapCells.Where(c=>c.Enemy!=null).Select(c=>c.Index));foreach(var cell in map.MapCells.Where(c=>c.Enemy!=null)){Assert.AreEqual(cell.Type==CellType.Boss?200:50,cell.Enemy.MaxHp);Assert.AreEqual(ElementType.None,cell.Enemy.Element);}Assert.AreEqual(6,map.Visits.Single().Cell);Assert.AreEqual(cards.CreateById(SimpleCardFactory.Heal).Power,map.Visits.Single().Options.Single().Heal);}
            Assert.Throws<ArgumentException>(()=>route.CreateInitialMap(0).ValidateFinalFloorForDeparture());Assert.DoesNotThrow(()=>route.CreateInitialMap(1).ValidateFinalFloorForDeparture());Assert.AreEqual(route.RouteHash,Definitions(cards,deck).RouteHash);CollectionAssert.AreEqual(before,deck.GetAllCards().Select(CollectionDisplay.DefinitionHash));var copy=route.CreateInitialMap(0);copy.MapCells[3].Enemy.TakeDamage(50,ElementType.None);Assert.AreEqual(50,route.CreateInitialMap(0).MapCells[3].Enemy.CurrentHp);
        }
        [TestCase(false)] [TestCase(true)] public void 旧外界三十枚の全領域とwarm個体は試用階層往復とcoldで再採番再抽選しない(bool returnedFixture)
        {
            var factory=new SimpleCardFactory();var initial=factory.CreateStarterDeck();var legacy=LegacyCatalog(factory,initial);string oldMap=legacy.MapHash,oldCatalog=CatalogHash(legacy);var defs=DungeonCoreFactory.CreateTrialRoute(legacy,factory,new EnemyPoiseDefinition(30,18,180),new EnemyPoiseDefinition(60,18,180));
            var first=new[]{SimpleCardFactory.Sword,SimpleCardFactory.Bomb,SimpleCardFactory.Heal}.Select(id=>factory.CreateById(id)).ToArray();var source=first.Concat(factory.CollectionCards.Where(c=>c.Rarity==CardRarity.Normal&&c.ExceedLevel!=ExceedLevel.Lv3&&!first.Any(f=>f.Id==c.Id&&f.ExceedLevel==c.ExceedLevel))).SelectMany(c=>Enumerable.Range(0,4).Select(_=>c.Clone())).Take(30).ToArray();Assert.AreEqual(30,source.Length);var owned=source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"catalog172-"+Guid.NewGuid().ToString("N"));try{var route=new FloorSequenceSession(defs,new FloorSequenceFileStore(defs,dir));for(int floor=0;floor<2;floor++){
                var shared=defs.CreateCheckpointSession(floor,route).Definitions;Assert.AreEqual(oldCatalog,CatalogHash(shared));foreach(var card in factory.CollectionCards)Assert.AreEqual(legacy.CaptureKnownCard("same",card).DefinitionKey,shared.CaptureKnownCard("same",card).DefinitionKey);
                using(var warm=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                    warm.TryInitializePlayer(()=>new PlayerCharacter(100));warm.Player.SetHp(73);Assert.IsTrue(warm.QueueSelectedDeck("legacy-catalog",source,owned,new SeededBattleRandom(172),out var error),error);for(int i=0;i<400&&warm.Core.Position!=600000;i++){warm.Core.TickFrame(1);warm.AdvanceDeckLocation();}Assert.AreEqual(600000,warm.Core.Position);warm.Cards.TrySelect(2);Assert.IsTrue(warm.Cards.TryConsumeSelected(warm.Cards.SelectedCard,warm.Cards.HandRevision,warm.Core.CardGauge,out _));var runtime=warm.CaptureRuntime(legacy);if(returnedFixture)runtime.Cards.Returned=new[]{Enumerable.Range(0,30).First(i=>!runtime.Cards.Drawn.Contains(i))};runtime.Validate();Assert.IsTrue(runtime.Cards.Cards.Any(c=>c.DefinitionKey.StartsWith("visit/v1/")));var original=PortableCheckpointJson.Serialize(runtime);var actor=OutsidePlayerSaveData.Capture(warm.Player);actor.Id=runtime.Id;warm.RestoreSavedState(actor,runtime,null,legacy);var cards=warm.Cards;var transfer=runtime.Copy();transfer.Id=Guid.NewGuid().ToString("N");var transferred=actor.Copy();transferred.Id=transfer.Id;
                    Assert.IsTrue(warm.RestoreSavedState(transferred,transfer,null,shared));Assert.AreSame(cards,warm.Cards);Assert.AreEqual(runtime.Cards.Key(),warm.Cards.CaptureState(shared).Key());Assert.IsFalse(warm.RestoreSavedState(transferred,transfer,null,shared));Assert.AreSame(cards,warm.Cards);Assert.IsTrue(cold.RestoreSavedState(transferred,transfer,null,shared));Assert.AreEqual(runtime.Cards.Key(),cold.Cards.CaptureState(shared).Key());
                    var back=warm.CaptureRuntime(shared);var backActor=OutsidePlayerSaveData.Capture(warm.Player);backActor.Id=back.Id;Assert.IsTrue(warm.RestoreSavedState(backActor,back,null,legacy));Assert.AreSame(cards,warm.Cards);Assert.AreEqual(runtime.Cards.Key(),warm.Cards.CaptureState(legacy).Key());Assert.IsTrue(warm.Cards.TryConsumeSelected(warm.Cards.SelectedCard,warm.Cards.HandRevision,warm.Core.CardGauge,out _));Assert.IsTrue(cold.Cards.TryConsumeSelected(cold.Cards.SelectedCard,cold.Cards.HandRevision,cold.Core.CardGauge,out _));Assert.AreEqual(warm.Cards.CaptureState(legacy).Key(),cold.Cards.CaptureState(shared).Key());Assert.AreEqual(warm.Core.CardGauge.CurrentUnits,cold.Core.CardGauge.CurrentUnits);var bad=runtime.Cards.Copy();bad.Cards[29].DefinitionKey="visit/v1/unknown";Assert.Throws<ArgumentException>(()=>OutsideExplorationCards.RestoreState(bad,shared));CollectionAssert.AreEqual(original,PortableCheckpointJson.Serialize(runtime));
                }
            }Assert.AreEqual(oldMap,legacy.MapHash);Assert.AreEqual(oldCatalog,CatalogHash(legacy));}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        private sealed class TransferIo:CheckpointFileIo
        {
            public string Fault;public int Attempts;public byte[] Before,Plan;
            public override void Replace(string temporary,string destination,string backup){var bytes=System.IO.File.ReadAllBytes(temporary);if(PortableCheckpointJson.DecodeFloor(bytes).FloorIndex==1){Attempts++;Before=System.IO.File.ReadAllBytes(destination);Plan=bytes;if(Fault=="before")throw new System.IO.IOException("試用階層保存失敗fixture");base.Replace(temporary,destination,backup);if(Fault=="after")throw new System.IO.IOException("試用階層保存応答喪失fixture");return;}base.Replace(temporary,destination,backup);}
        }
        private static void CompleteByExplicitFixture(DungeonBattleManager manager)
        {
            // 配置と境界を通す試験。通常の攻撃入力・敵攻略を実証するfixtureとは区別する。
            manager.OnCardRewardPresented+=_=>manager.SelectCardReward(null);
            for(int i=0;i<2000&&!manager.Dungeon.IsFloorCompleted&&!manager.Dungeon.IsCheckpointBlocked&&manager.CurrentPhase!=GamePhase.DungeonCleared;i++){
                manager.TickFrame(17);if(manager.CurrentBattle!=null){var battle=manager.CurrentBattle;if(battle.CustomScreen.IsOpen){battle.CustomScreen.Confirm();battle.CustomScreen.Close();battle.Resume(BattlePauseReason.CustomScreen);}foreach(var e in battle.Enemies.ToArray())if(!e.IsDead)e.TakeDamage(e.MaxHp,ElementType.None);}
                if(manager.Dungeon.ActiveVisit!=null){if(manager.Dungeon.CanChooseVisit(0))Assert.IsTrue(manager.Dungeon.ChooseVisit(0));if(manager.Dungeon.CanExitVisit)manager.Dungeon.ExitVisit();}
                if(manager.Dungeon.PendingCurrencyReward>0)manager.SelectCurrencyReward(true);if(manager.Dungeon.PendingItemReward!=null)manager.SelectItemReward(false);
                if(manager.CurrentPhase==GamePhase.Dungeon&&manager.Dungeon.ActiveVisit==null&&!manager.Dungeon.IsInBattle)manager.ResumeAfterBattle();
            }
            Assert.IsTrue(manager.Dungeon.IsFloorCompleted||manager.Dungeon.IsCheckpointBlocked||manager.CurrentPhase==GamePhase.DungeonCleared,"fixture停止: "+manager.CurrentPhase+" / "+manager.Dungeon.PlayerPosition+" / "+manager.Dungeon.CheckpointError);
        }
        [TestCase(null)] [TestCase("before")] [TestCase("after")] public void 試用実配置の終端から次階層保存と再試行とcoldを経て最終ボスへ進める(string fault)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"trial172-"+Guid.NewGuid().ToString("N"));
            try{var cards=new SimpleCardFactory();var deck=cards.CreateStarterDeck();var defs=Definitions(cards,deck);var io=new TransferIo{Fault=fault};var store=new FloorSequenceFileStore(defs,dir,io);var run=new FloorSequenceSession(defs,store);var checkpoint=defs.CreateCheckpointSession(0,run);var player=new PlayerCharacter(100);player.SetHp(40);var factory=new DungeonCoreFactory(new SeededBattleRandom(172),new Olca.Tests.Mocks.MockLogger(),()=>cards);var first=factory.Create(new DungeonCoreFactory.Launch{Map=defs.CreateInitialMap(0),Player=player,Deck=deck,Checkpoints=checkpoint,HasNextFloor=true});string runId=run.Current.Snapshot.State.RunId;
                CompleteByExplicitFixture(first);Assert.AreEqual(60,player.CurrentHp,"既存20回復だけで階層間の自動全回復なし");
                if(fault=="before"){Assert.IsTrue(first.Dungeon.IsCheckpointBlocked);Assert.AreEqual(0,run.Current.FloorIndex);Assert.IsTrue(run.HasPendingSave);CollectionAssert.AreEqual(io.Before,System.IO.File.ReadAllBytes(store.ActivePath));io.Fault=null;Assert.IsTrue(first.RetryCheckpoint());}
                Assert.IsFalse(first.Dungeon.IsCheckpointBlocked);Assert.IsTrue(first.Dungeon.IsFloorCompleted);Assert.AreEqual(1,run.Current.FloorIndex);Assert.AreEqual(fault=="before"?2:1,io.Attempts);Assert.IsFalse(run.HasPendingSave);Assert.AreEqual(60,run.Current.State.Player.CurrentHp);Assert.AreEqual(runId,run.Current.Snapshot.State.RunId);Assert.IsFalse(first.RetryCheckpoint());
                var bytes=System.IO.File.ReadAllBytes(store.ActivePath);var cold=new FloorSequenceSession(defs,new FloorSequenceFileStore(defs,dir));Assert.IsTrue(cold.TryLoad(out var error),error);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));Assert.AreEqual(1,cold.Current.FloorIndex);var final=factory.CreateFloor(cold,defs,out _);final.ActivateCheckpoint();CompleteByExplicitFixture(final);Assert.AreEqual(GamePhase.DungeonCleared,final.CurrentPhase);Assert.AreEqual(80,cold.Current.State.Player.CurrentHp);Assert.AreEqual(CheckpointBoundary.Terminal,cold.Current.Snapshot.State.Boundary);Assert.AreEqual(runId,cold.Current.Snapshot.State.RunId);Assert.IsFalse(final.RetryCheckpoint());
            }finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [TestCase("floors.active.json")] [TestCase("floors.backup.json")] public void 計画欠損のrun別階層保存は検出し受領済みrunは書き換えず除外する(string file)
        {
            string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"orphan172-"+Guid.NewGuid().ToString("N")),id=Guid.NewGuid().ToString("N");try{string dir=System.IO.Path.Combine(root,"runs",id);System.IO.Directory.CreateDirectory(dir);string path=System.IO.Path.Combine(dir,file);var bytes=new byte[]{1,2,3};System.IO.File.WriteAllBytes(path,bytes);Assert.IsTrue(FloorSequenceFileStore.TryFindUnclaimedRunFloors(root,Array.Empty<string>(),out var found,out var error),error);Assert.IsTrue(found);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(path));Assert.IsTrue(FloorSequenceFileStore.TryFindUnclaimedRunFloors(root,new[]{id},out found,out error),error);Assert.IsFalse(found);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(path));}finally{if(System.IO.Directory.Exists(root))System.IO.Directory.Delete(root,true);}
        }
        [Test] public void 階層保存先がファイルなら探索せず安全に拒否する()
        {
            string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"bad-runs172-"+Guid.NewGuid().ToString("N"));try{System.IO.Directory.CreateDirectory(root);System.IO.File.WriteAllText(System.IO.Path.Combine(root,"runs"),"fixture");Assert.IsFalse(FloorSequenceFileStore.TryFindUnclaimedRunFloors(root,Array.Empty<string>(),out var found,out var error));Assert.IsFalse(found);Assert.IsNotEmpty(error);Assert.AreEqual("fixture",System.IO.File.ReadAllText(System.IO.Path.Combine(root,"runs")));}finally{if(System.IO.Directory.Exists(root))System.IO.Directory.Delete(root,true);}
        }
    }
}
