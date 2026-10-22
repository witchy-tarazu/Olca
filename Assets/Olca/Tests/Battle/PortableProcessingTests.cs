using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Application.Battle;
using Olca.Tests.Mocks;
namespace Olca.Tests.Battle
{
    public class PortableProcessingTests
    {
        public static PortableBattlePackage Package(bool savedTuning=false)
        {
            var f=new SimpleCardFactory();var deck=new Deck(new[]{"sword","sword","bomb","bomb","tuning","tuning","heal","heal","shield","shield"}.Select(f.CreateById).ToList());
            var map=DungeonConfig.CreateTestDungeon(100,30,50);var session=DungeonCheckpointSession.CreateDefault(map,deck);
            var known=new HashSet<string>(session.Definitions.CollectionDefinitions.Select(CollectionDisplay.DefinitionHash));
            foreach(var c in f.FusionCards)if(known.Add(CollectionDisplay.DefinitionHash(c)))session.Definitions.RegisterCard("processing/"+CollectionDisplay.DefinitionHash(c),c);
            var grant=f.GetTuningDefinition(f.CreateById("tuning"));session.Definitions.RegisterGrant("grant/product/v1",grant);
            _=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new SeededBattleRandom(1),new MockLogger(),checkpoints:session);
            var d=session.Store.Read();d.Enemies.First(e=>e.CellIndex==50).GridX=4;d.Player.GridX=2;
            if(savedTuning){var original=d.Cards.First(c=>c.DefinitionKey=="initial/v1/"+CollectionDisplay.DefinitionHash(f.CreateById("sword")));var tuned=TuningEvaluator.Evaluate(f.CreateById("sword"),grant).ResultCard;var n=session.Definitions.CaptureKnownCard(original.InstanceId,tuned);d.Cards[Array.IndexOf(d.Cards,original)]=n;}
            var encounter=new CheckpointBattleDto{ContactCell=50,ParticipantCells=new[]{50},HandLimit=10,GridOriginX=47};
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(d,encounter,session.Definitions,73,null,c=>"fallback/v1/"+c.Rarity+"/"+c.Id,out var plan,out var error),error);
            var t=CheckpointBattleFactory.ProductBasicTiming;var p=PortableBattlePackage.Capture(session.Definitions,PortableCheckpointJson.Encode(plan.Snapshot),new PortableBattleRules{ChargeFrames=60,IntervalFrames=12,HitInvincibilityMs=600,StartupFrames=t.StartupFrames,RecoveryFrames=t.RecoveryFrames,CancelAfterFrames=t.CancelAfterFrames,GaugeSpeed=2},999);
            p.Processing=PortableProcessingCatalog.Capture("product-derived-fixture","v1",f.FusionCards.Concat(p.Cards.Select(c=>c.Restore())),f,new SimpleFusionRecipeProvider(f).FusionDefinitions,f.GetTuningDefinition);
            return PortableCheckpointJson.Deserialize<PortableBattlePackage>(PortableCheckpointJson.Serialize(p));
        }
        [Test]
        public void 正規の特殊融合調律超越を通常コマンドへ復帰し取消と消費とUndoを維持する()
        {
            var p=Package();var s=p.CreateSimulation();var hand=s.Battle.CustomScreen;var before=hand.CurrentHand.Select(CollectionDisplay.DefinitionHash).OrderBy(x=>x).ToArray();
            var commands=PortableProcessingReplay.Commands(p);Assert.AreEqual(3,commands.Count(c=>c.Custom?.Kind==CustomCommandKind.Undo));
            foreach(var c in commands.TakeWhile(c=>c.Custom?.Kind!=CustomCommandKind.Select))s.Step(c);
            CollectionAssert.AreEqual(before,hand.CurrentHand.Select(CollectionDisplay.DefinitionHash).OrderBy(x=>x));Assert.AreEqual(0,hand.Commands.Observe().UndoCount);
            Assert.AreEqual(PortableProcessingReplay.Trace(p),PortableProcessingReplay.Trace(p,7,()=>GC.KeepAlive(new int[100])));
        }
        [Test]
        public void 保存済み調律履歴と再開後の加工が同じ元定義へ戻る()
        {
            var p=Package(true);var state=p.Restore(out _);Assert.AreEqual(1,state.Deck.GetAllCards().Count(c=>c.TuningCount==1));Assert.IsTrue(state.Deck.GetAllCards().Any(c=>c.TuningCount==1&&c.GetFinalPower()==40));
            var before=(byte[])p.CheckpointEnvelope.Clone();Assert.AreEqual(PortableProcessingReplay.Trace(p),PortableProcessingReplay.Trace(p));CollectionAssert.AreEqual(before,p.CheckpointEnvelope);
            p.Grants=Array.Empty<PortableTuningGrant>();Assert.Catch(()=>p.CreateSimulation());
        }
        [TestCase("version")] [TestCase("missing")] [TestCase("result")] [TestCase("grant")] [TestCase("hash")] [TestCase("normal")]
        public void 不正または欠損した加工定義を別の製品値へfallbackしない(string mutation)
        {
            var p=Package();switch(mutation){case "version":p.Processing.Version=2;break;case "missing":p.Processing.Cards=Array.Empty<PortableCardDefinition>();break;case "result":p.Processing.Special[0].Result="absent";break;case "grant":p.Processing.Tuning[0].Grant.Id="different";break;case "hash":p.Processing.Hash="bad";break;case "normal":p.Processing.Normal=new[]{new PortableNormalFusion{A="missing",B="missing",Series="missing"}};break;}Assert.Catch(()=>p.CreateSimulation());
        }
        [Test]
        public void 通常融合の表と特殊優先を同じ登録カタログから復元する()
        {
            var f=new SimpleCardFactory();var special=new SimpleFusionRecipeProvider(f).FusionDefinitions;special.RegisterNormal(CardSeries.Sword.Name,CardSeries.Bomb.Name,CardRarity.Normal,"heal");
            var p=PortableProcessingCatalog.Capture("normal-fixture","v1",f.FusionCards,f,special,f.GetTuningDefinition);var r=p.Restore();Assert.AreEqual("fire_sword",r.Fusion.Execute(new[]{f.CreateById("sword"),f.CreateById("bomb")}).ResultCard.Id);
            var normal=new FusionDefinitionCatalog();foreach(var c in f.FusionCards)normal.RegisterCard(c,c.Series.Name,c.Id,(int)c.ExceedLevel);normal.RegisterNormal(CardSeries.Sword.Name,CardSeries.Bomb.Name,CardRarity.Normal,"heal");
            r=PortableProcessingCatalog.Capture("normal-only-fixture","v1",f.FusionCards,f,normal,f.GetTuningDefinition).Restore();Assert.AreEqual("heal",r.Fusion.Execute(new[]{f.CreateById("bomb"),f.CreateById("sword")}).ResultCard.Id);
        }
        [Test]
        public void 付与付きカードの対象条件と欠落段階を補完せず輸送する()
        {
            var sword=new SimpleCardFactory().CreateById("sword");var condition=new TuningTargetCondition(CardCategory.Attack,CardSeries.Sword,ElementType.None,CollectionDisplay.DefinitionHash(sword));
            var grant=new TuningGrantDefinition("fixture/restricted/v1",new[]{new TuningEffectDefinition("damage",2,new Dictionary<int,CardModifier>{[1]=new CardModifier{BonusDamage=7}},condition)},targetCondition:condition);
            var material=new SimpleCardFactory().CreateById("tuning").WithTuningDefinition(grant);
            var dto=PortableCheckpointJson.Deserialize<PortableCardDefinition>(PortableCheckpointJson.Serialize(PortableCardDefinition.Capture("fixture/tuning/v1",material)));var restored=dto.Restore();
            var system=new TuningSystem();var first=system.Execute(sword,restored);Assert.IsTrue(first.IsSuccess);Assert.AreEqual(37,first.ResultCard.GetFinalPower());Assert.IsFalse(system.Execute(first.ResultCard,restored).IsSuccess);
            Assert.IsFalse(system.Validate(new SimpleCardFactory().CreateById("bomb"),restored).IsValid);Assert.AreEqual(CollectionDisplay.DefinitionHash(material),CollectionDisplay.DefinitionHash(restored));
        }
        [Test]
        public void 同ID別定義を調律対象や超越元へ推測解決しない()
        {
            var p=Package();var r=p.Processing.Restore();var original=new SimpleCardFactory().CreateById("sword");var restored=PortableCardDefinition.Capture("series/v1",original).Restore();Assert.IsTrue(new SimpleCardFactory().HasExceededDefinition(restored,ExceedLevel.Lv1));var changed=original.WithAttackTiming(new PlayerAttackTiming(3,9,4));
            Assert.IsFalse(r.Exceed.Validate(original,changed).IsValid);Assert.IsFalse(r.HasExceededDefinition(changed,ExceedLevel.Lv1));Assert.IsNull(r.GetGrant(new Card("tuning","別",1,CardRarity.Normal,CardCategory.Tuning,ElementType.None,CardSeries.None,0,"fixture")));
        }
    }
}
