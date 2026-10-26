using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Integration;
using Olca.Tests.Mocks;

namespace Olca.Tests
{
    public class EvaluationRewardAdapterTests
    {
        private sealed class Provider:IEvaluationRewardProvider
        {
            public int Calls;public bool Fail;
            public EvaluationRewardDefinition Resolve(BattleRewardContext c){Calls++;if(Fail)throw new Exception("保存再開中に再抽選定義を呼んだ");return Definition(c.ParticipantCells.Count);}
        }
        private static EvaluationRewardDefinition Definition(int count)
        {
            var pool=new SimpleCardPool(new[]{"sword","heal","shield","fire","ice"}.Select(id=>CardRewardSystem.CreateFallbackCard(id,CardRarity.Normal)).ToList());
            return new EvaluationRewardDefinition("fixture-only-v1",Enumerable.Range(0,4).Select(g=>new EvaluationRewardBand(pool,g==0?0:10000,0,Array.Empty<int>(),Enumerable.Repeat(11+g,count))));
        }
        private static Deck Deck()=>new Deck(Enumerable.Range(0,20).Select(i=>CardRewardSystem.CreateFallbackCard("sword",CardRarity.Normal)).ToList());
        private static BattleSystem Advance(DungeonBattleManager manager){for(int i=0;i<20000&&manager.CurrentBattle==null;i++)manager.TickFrame(17);Assert.IsFalse(manager.Dungeon.IsCheckpointBlocked,manager.Dungeon.CheckpointError);Assert.IsNotNull(manager.CurrentBattle);return manager.CurrentBattle;}
        private static void Win(BattleSystem battle){foreach(var e in battle.Enemies.ToArray())e.TakeDamage(10000,ElementType.None);}
        [Test]
        public void 全評価帯を開始時に保存し結果確定時に選択して再送では通貨を増殖させない()
        {
            var map=DungeonConfig.CreateTestDungeon(20,30,3);var deck=Deck();var session=DungeonCheckpointSession.CreateDefault(map,deck);var provider=new Provider();
            var manager=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,evaluationRewards:provider);
            var battle=Advance(manager);var saved=session.Store.Read();Assert.AreEqual(1,provider.Calls);Assert.AreEqual(4,saved.Battle.EvaluationRewards.Length);
            Assert.IsFalse(saved.Battle.EvaluationRewards[0].RewardOffered);Assert.IsTrue(saved.Battle.EvaluationRewards.Skip(1).All(v=>v.RewardCandidates.Length==3));
            var firstHand=saved.Battle.FirstHand.ToArray();provider.Fail=true;
            Assert.IsTrue(manager.SuspendForCheckpoint());
            var reopened=new DungeonCheckpointSession(session.Definitions,session.Store);
            Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(reopened,new MockRandomProvider(),new MockLogger(),out var restored,out var error,evaluationRewards:provider),error);
            List<Card> offered=null;restored.Dungeon.OnCardRewardOffered+=cards=>offered=cards;
            restored.ActivateCheckpoint();var current=restored.CurrentBattle;
            CollectionAssert.AreEqual(firstHand,reopened.Store.Read().Battle.FirstHand);Assert.AreEqual(1,provider.Calls);
            Win(current);var grade=restored.LastBattleEvaluation.Grade;var expected=saved.Battle.EvaluationRewards.Single(v=>v.Grade==(int)grade);
            Assert.IsTrue(expected.RewardOffered);Assert.IsTrue(offered.Select(c=>session.Definitions.CaptureKnownCard("verify",c).DefinitionKey).SequenceEqual(expected.RewardCandidates.Select(id=>saved.Cards.Single(v=>v.InstanceId==id).DefinitionKey)));
            restored.SelectCardReward(null);Assert.AreEqual(expected.CurrencyAmounts[0],restored.Dungeon.PendingCurrencyReward);
            restored.SelectCurrencyReward(true);long currency=restored.Dungeon.Currency;restored.SelectCurrencyReward(true);Assert.AreEqual(currency,restored.Dungeon.Currency);Assert.AreEqual(1,provider.Calls);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)]
        public void 評価帯別結果は独立した保存参照で外部配列の変更から保護する(int grade)
        {
            var map=DungeonConfig.CreateTestDungeon(20,30,3);var deck=Deck();var session=DungeonCheckpointSession.CreateDefault(map,deck);
            new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
            var state=session.Store.Read();state.Enemies[0].GridX=4;
            var encounter=new CheckpointBattleDto{ContactCell=3,ParticipantCells=new[]{3},CurrencyAmounts=new[]{7},HandLimit=10};
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(state,encounter,session.Definitions,123,null,c=>session.Definitions.CaptureKnownCard("fixture",c).DefinitionKey,out var plan,out var error,evaluation:Definition(1)),error);
            var copy=plan.Snapshot;copy.Battle.EvaluationRewards[grade].CurrencyAmounts[0]=999;Assert.AreEqual(11+grade,plan.Snapshot.Battle.EvaluationRewards[grade].CurrencyAmounts[0]);
            Assert.IsTrue(CheckpointCodec.TryRestore(plan.Snapshot,session.Definitions,out _,out error),error);
            copy=plan.Snapshot;copy.Battle.EvaluationRewards[grade].Grade=7;Assert.IsFalse(CheckpointCodec.TryRestore(copy,session.Definitions,out _,out _));
            copy=plan.Snapshot;copy.Battle.CurrencyAmounts=null;Assert.IsFalse(CheckpointCodec.TryRestore(copy,session.Definitions,out _,out _));
            copy=plan.Snapshot;copy.Battle.EvaluationRewards[grade].CurrencyAmounts=null;Assert.IsFalse(CheckpointCodec.TryRestore(copy,session.Definitions,out _,out _));
            copy=plan.Snapshot;copy.Battle.EvaluationRewards[grade].RewardCandidates=new[]{"missing"};Assert.IsFalse(CheckpointCodec.TryRestore(copy,session.Definitions,out _,out _));
        }
        private sealed class ItemProvider:IEvaluationRewardProvider
        {
            public EvaluationRewardDefinition Resolve(BattleRewardContext c){var cards=Definition(c.ParticipantCells.Count).Bands[0].CardPool;return new EvaluationRewardDefinition("fixture-items",Enumerable.Range(0,4).Select(g=>new EvaluationRewardBand(cards,0,g==0?0:100,new[]{g==1?1:0,g>=2?1:0},Enumerable.Repeat(20+g,c.ParticipantCells.Count))));}
        }
        [TestCase(0,false)][TestCase(1,false)][TestCase(2,false)][TestCase(3,false)][TestCase(3,true)]
        public void 評価帯のアイテム候補と率と通貨を確定し対象外戦闘へは配布しない(int grade,bool unqualified)
        {
            var basic=DungeonConfig.CreateTestDungeon(20,30,3);
            var map=new DungeonConfig(basic.DungeonId,basic.DungeonName,20,basic.MapCells,strongEnemyCells:unqualified?Array.Empty<int>():new[]{3},itemRewards:new[]{new ItemRewardEntry(new FieldItem("fixture-a","テストA",10)),new ItemRewardEntry(new FieldItem("fixture-b","テストB",20))});
            var deck=Deck();var session=DungeonCheckpointSession.CreateDefault(map,deck);var player=new PlayerCharacter(100);player.TakeDamage(50);
            var dungeon=new DungeonSystem(map,player,deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,evaluationRewards:new ItemProvider(),evaluateRewardGrade:_=>(BattleEvaluationGrade)grade);
            for(int i=0;i<20000&&dungeon.CurrentBattle==null;i++)dungeon.TickFrame();Assert.IsNotNull(dungeon.CurrentBattle,dungeon.CheckpointError);
            var saved=session.Store.Read();Win(dungeon.CurrentBattle);
            bool expected=grade>0&&!unqualified;Assert.AreEqual(expected,dungeon.PendingItemReward!=null);
            if(expected){Assert.AreEqual(grade==1?"fixture-a":"fixture-b",dungeon.PendingItemReward.Id);dungeon.SelectItemReward(true);int hp=player.CurrentHp;dungeon.SelectItemReward(true);Assert.AreEqual(hp,player.CurrentHp);}
            Assert.AreEqual(20+grade,dungeon.PendingCurrencyReward);dungeon.SelectCurrencyReward(true);Assert.AreEqual(20+grade,dungeon.Currency);
            Assert.IsTrue(CheckpointCodec.TryRestore(saved,session.Definitions,out _,out var error),error);
        }
        [TestCase(int.MinValue,false)][TestCase(int.MaxValue,true)]
        public void 評価帯の基礎率へ既存の率差分を加え上下限で止め候補の質は変えない(int bonus,bool offered)
        {
            var map=DungeonConfig.CreateTestDungeon(20,30,3);var deck=Deck();var session=DungeonCheckpointSession.CreateDefault(map,deck);
            new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
            var state=session.Store.Read();state.Enemies[0].GridX=4;var encounter=new CheckpointBattleDto{ContactCell=3,ParticipantCells=new[]{3},CurrencyAmounts=new[]{1},HandLimit=10};
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(state,encounter,session.Definitions,17,null,c=>session.Definitions.CaptureKnownCard("test",c).DefinitionKey,out var plan,out var error,normalRewardRateBonusBasisPoints:bonus,evaluation:Definition(1)),error);
            Assert.IsTrue(plan.Snapshot.Battle.EvaluationRewards.All(v=>v.RewardOffered==offered));
        }
        [TestCase(false)][TestCase(true)]
        public void 全帯のボスカード保証と最終ボスのアイテム対象外を維持する(bool nextFloor)
        {
            var basic=DungeonConfig.CreateTestDungeon(20,100,100);int cell=basic.MapCells.Single(v=>v.Enemy?.EnemyId=="boss").Index;
            var map=new DungeonConfig(basic.DungeonId,basic.DungeonName,20,basic.MapCells,itemRewards:new[]{new ItemRewardEntry(new FieldItem("test","テスト",1))});
            var deck=Deck();var session=DungeonCheckpointSession.CreateDefault(map,deck);new DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,hasNextFloor:nextFloor);
            var state=session.Store.Read();state.Enemies.Single(v=>v.CellIndex==cell).GridX=4;
            var pool=Definition(1).Bands[0].CardPool;var definition=new EvaluationRewardDefinition("fixture-boss",Enumerable.Range(0,4).Select(g=>new EvaluationRewardBand(pool,0,100,new[]{1},new[]{0})));
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(state,new CheckpointBattleDto{ContactCell=cell,ParticipantCells=new[]{cell},CurrencyAmounts=new[]{0},HandLimit=10},session.Definitions,17,null,c=>session.Definitions.CaptureKnownCard("test",c).DefinitionKey,out var plan,out var error,hasNextFloor:nextFloor,evaluation:definition),error);
            Assert.IsTrue(plan.Snapshot.Battle.EvaluationRewards.All(v=>v.RewardOffered&&v.RewardCandidates.Length>0&&v.ItemRewardIndex.HasValue==nextFloor));
        }
        [Test]
        public void 全帯必須と保存なし設定を拒否し製品値を推測しない()
        {
            Assert.Throws<ArgumentException>(()=>new EvaluationRewardDefinition("bad",Array.Empty<EvaluationRewardBand>()));
            Assert.Throws<ArgumentException>(()=>new DungeonBattleManager(DungeonConfig.CreateTestDungeon(20),new PlayerCharacter(100),Deck(),new MockRandomProvider(),new MockLogger(),evaluationRewards:new Provider()));
        }
    }
}
