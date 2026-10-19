using NUnit.Framework;
using System;
using System.Linq;
using Olca.Application.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Domain;
using Olca.Tests.Mocks;
using System.Collections.Generic;
using Olca.Core.Interfaces;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class CardRewardSystemTests
    {
        private MockRandomProvider mockRandom;

        [SetUp]
        public void SetUp()
        {
            mockRandom = new MockRandomProvider(0);
        }

        private Deck CreateTestDeck()
        {
            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃")
            };

            return new Deck(cards);
        }

        [Test]
        public void ShouldDropCard_ボスは100パーセントドロップする()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(10, 0));

            // Act
            bool shouldDrop = rewardSystem.ShouldDropCard(boss);

            // Assert
            Assert.IsTrue(shouldDrop, "ボスは必ずカードをドロップする");
        }

        [Test]
        public void GenerateCardChoices_通常敵は3枚から選択()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(5, 0));

            // Act
            var choices = rewardSystem.GenerateCardChoices(enemy);

            // Assert
            Assert.AreEqual(3, choices.Count, "通常敵からは3枚の選択肢");
        }

        [Test]
        public void GenerateCardChoices_ボスは1枚のみ()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(10, 0));

            // Act
            var choices = rewardSystem.GenerateCardChoices(boss);

            // Assert
            Assert.AreEqual(1, choices.Count, "ボスからは1枚の選択肢");
            Assert.AreEqual(CardRarity.Limited, choices[0].Rarity, "ボスからはリミテッドカード");
        }

        [Test]
        public void SelectCard_デッキにカードが追加される()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var newCard = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            int initialDeckSize = rewardSystem.GetCurrentDeck().TotalCount;

            // Act
            rewardSystem.SelectCard(newCard);

            // Assert
            Assert.AreEqual(initialDeckSize + 1, rewardSystem.GetCurrentDeck().TotalCount,
                "デッキに1枚追加されている");
        }

        [Test]
        public void SelectCard_入手カードリストに追加される()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var newCard = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            // Act
            rewardSystem.SelectCard(newCard);

            // Assert
            Assert.AreEqual(1, rewardSystem.ObtainedCards.Count, "入手カードリストに追加されている");
            Assert.AreEqual(newCard, rewardSystem.ObtainedCards[0]);
        }

        [Test]
        public void SelectCard_イベントが発火する()
        {
            // Arrange
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var newCard = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            bool eventFired = false;
            Card receivedCard = null;

            rewardSystem.OnCardAdded += (card) =>
            {
                eventFired = true;
                receivedCard = card;
            };

            // Act
            rewardSystem.SelectCard(newCard);

            // Assert
            Assert.IsTrue(eventFired, "OnCardAddedイベントが発火した");
            Assert.AreEqual(newCard, receivedCard);
        }

        // ========== ICardPool 注入テスト ==========

        [Test]
        public void GenerateCardChoices_通常候補不足は代替生成せず失敗する()
        {
            // Arrange: 特定カードだけのプールを注入
            var poolCard = new Card("pool_sword", "プールソード", 1, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "プール内カード");
            var cardPool = new SimpleCardPool(new List<Card> { poolCard });

            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom, cardPool);
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(5, 0));

            Assert.IsFalse(rewardSystem.TryGenerateConfiguredChoices(enemy, out var choices));
            Assert.IsEmpty(choices);
            Assert.Throws<InvalidOperationException>(() => rewardSystem.GenerateCardChoices(enemy));
        }

        [Test]
        public void GenerateCardChoices_CardPoolなしはフォールバックカードが生成される()
        {
            // Arrange: cardPool を注入しない（デフォルト null）
            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom);
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(5, 0));

            // Act & Assert: 例外なく生成できること
            var choices = rewardSystem.GenerateCardChoices(enemy);
            Assert.AreEqual(3, choices.Count, "フォールバックでも3枚生成される");
            Assert.IsNotNull(choices[0], "生成されたカードはnullでない");
        }

        [Test]
        public void GenerateCardChoices_SimpleCardPool_該当レアリティがなければ全カードから選ぶ()
        {
            // Arrange: Normalカードのみのプールにボス（Limited）を要求する
            var normalCard = new Card("normal_card", "ノーマル", 1, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.None, 20, "テスト");
            var cardPool = new SimpleCardPool(new List<Card> { normalCard });

            var rewardSystem = new CardRewardSystem(CreateTestDeck(), mockRandom, cardPool);
            var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(10, 0));

            // Act: ボスへのドロップ（Limitedを要求するが、Normalしかない）
            var choices = rewardSystem.GenerateCardChoices(boss);

            // Assert: Limitedがないので全カードからフォールバック
            Assert.AreEqual(1, choices.Count, "ボスは1枚");
            Assert.AreEqual("normal_card", choices[0].Id, "フォールバックでNormalカードが返る");
        }

        private sealed class FallbackCountingRandom : IRandomProvider
        {
            public int Index, Calls;
            public int Range(int min, int max) { Calls++; return Math.Max(min, Math.Min(Index, max - 1)); }
            public float Range(float min, float max) => min;
            public float Value => 0;
        }

        [TestCase(0)][TestCase(2)][TestCase(int.MaxValue)]
        public void 未設定プールも固定乱数三回で重複なしになり次戦の候補を減らさない(int index)
        {
            var random = new FallbackCountingRandom { Index = index };
            var rewards = new CardRewardSystem(CreateTestDeck(), random);
            var enemy = new Enemy("normal", "敵", 10, ElementType.None, new Vector2Int(3, 0));
            var choices = rewards.GenerateCardChoices(enemy);
            Assert.AreEqual(3, random.Calls);
            Assert.AreEqual(3, choices.Select(c => (c.Id, c.ExceedLevel)).Distinct().Count());
            Assert.IsTrue(choices.All(c => c.Rarity == CardRarity.Normal));
            CollectionAssert.IsSubsetOf(choices.Select(c => c.Id), new[] { "sword", "heal", "shield", "fire", "ice" });
            var next = rewards.GenerateCardChoices(enemy);
            Assert.AreEqual(6, random.Calls);
            CollectionAssert.AreEqual(choices.Select(c => c.Id), next.Select(c => c.Id));
            for (int i=0;i<3;i++) Assert.AreNotSame(choices[i], next[i]);
            Assert.AreEqual(1, rewards.GetCurrentDeck().TotalCount);
            var boss = rewards.GenerateCardChoices(new Enemy("boss", "ボス", 100, ElementType.None, new Vector2Int(9,0)));
            Assert.AreEqual(7, random.Calls); Assert.AreEqual(1, boss.Count); Assert.AreEqual(CardRarity.Limited,boss[0].Rarity);
        }

        [TestCase(false)][TestCase(true)]
        public void 通常三択の保存復帰では再抽選せず旧重複候補も保持する(bool legacyDuplicates)
        {
            var map=DungeonConfig.CreateTestDungeon(20,30,3);var deck=CreateTestDeck();
            var session=DungeonCheckpointSession.CreateDefault(map,deck);
            var manager=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),deck,mockRandom,new MockLogger(),checkpoints:session);
            CheckpointDto saved=null;
            for(int seed=0;seed<100;seed++) {
                var departure=session.Store.Read();departure.Enemies[0].GridX=4;
                var encounter=new CheckpointBattleDto { ContactCell=3, ParticipantCells=new[]{3}, HandLimit=10 };
                Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure,encounter,session.Definitions,seed,null,
                    c=>session.Definitions.CaptureKnownCard("fixture",c).DefinitionKey,out var plan,out var error),error);
                if(plan.Snapshot.Battle.RewardOffered){saved=plan.Snapshot;break;}
            }
            Assert.IsNotNull(saved);Assert.AreEqual(3,saved.Battle.RewardCandidates.Length);
            var records=saved.Battle.RewardCandidates.Select(id=>saved.Cards.Single(c=>c.InstanceId==id)).ToArray();
            Assert.AreEqual(3,records.Select(c=>c.DefinitionKey).Distinct().Count());
            if(legacyDuplicates) foreach(var record in records.Skip(1)) {
                record.DefinitionKey=records[0].DefinitionKey;record.ValueHash=records[0].ValueHash;
            }
            var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-fallback-reward-"+Guid.NewGuid().ToString("N"));
            try {
                var store=new Olca.Unity.FileCheckpointStore(session.Definitions,directory);
                Assert.IsTrue(store.TryWrite(saved,out var error),error);
                var bytes=System.IO.File.ReadAllBytes(store.ActivePath);
                var reopened=new Olca.Unity.FileCheckpointStore(session.Definitions,directory);
                Assert.IsTrue(new DungeonCheckpointSession(session.Definitions,reopened).TryRead(out var restored,out error),error);
                CollectionAssert.AreEqual(saved.Battle.RewardCandidates,restored.Snapshot.Battle.RewardCandidates);
                Assert.AreEqual(legacyDuplicates?1:3,restored.RewardCandidates.Select(c=>c.Id).Distinct().Count());
                Assert.AreEqual(3,restored.RewardCandidates.Distinct().Count(),"同一定義でも保存個体を統合しない");
                CollectionAssert.AreEqual(saved.Battle.FirstHand,restored.Snapshot.Battle.FirstHand);
                CollectionAssert.AreEqual(saved.DeckOrder,restored.Snapshot.DeckOrder);
                CollectionAssert.AreEqual(saved.ObtainedOrder,restored.Snapshot.ObtainedOrder);
                CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
            } finally { if(System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory,true); }
        }

        private sealed class FixedCountingRandom : IRandomProvider
        {
            public int Calls;
            public int Range(int min, int max) { Calls++; return min; }
            public float Range(float min, float max) => min;
            public float Value => 0f;
        }

        [Test]
        public void 固定乱数でも同一ID段階を除外して三回で異なる実定義を抽選する()
        {
            var factory = new SimpleCardFactory();
            var heal = factory.CreateById(SimpleCardFactory.Heal);
            var tuning = factory.CreateById(SimpleCardFactory.Tuning);
            var sword = factory.CreateById(SimpleCardFactory.Sword);
            tuning.Modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Poison, Power = 7 });
            var definitions = new List<Card> { heal, heal, heal.Clone(), tuning, tuning.Clone(), sword };
            var random = new FixedCountingRandom();
            var rewards = new CardRewardSystem(CreateTestDeck(), random, new SimpleCardPool(definitions));
            var enemy = new Enemy("normal", "敵", 10, ElementType.None, new Vector2Int(3, 0));
            Assert.IsTrue(rewards.TryGenerateConfiguredChoices(enemy, out var choices));
            Assert.AreEqual(3, random.Calls);
            CollectionAssert.AreEqual(new[] { heal.Id, tuning.Id, sword.Id }, choices.Select(c => c.Id));
            Assert.AreEqual(CardCategory.Defense, choices[0].Category);
            Assert.AreEqual(CardCategory.Tuning, choices[1].Category);
            Assert.AreEqual(heal.Power, choices[0].Power);
            Assert.AreEqual(heal.Description, choices[0].Description);
            choices[1].Modifier.AdditionalEffects[0].Power = 99;
            Assert.AreEqual(7, tuning.Modifier.AdditionalEffects[0].Power);
            Assert.AreEqual(6, definitions.Count);
            var next = rewards.GenerateCardChoices(enemy);
            Assert.AreEqual(7, next[1].Modifier.AdditionalEffects[0].Power);
        }

        [Test]
        public void 同じIDでも異段階は別候補で同時に選ばれる()
        {
            var factory = new SimpleCardFactory();
            var normal = factory.CreateById(SimpleCardFactory.Sword);
            var lv1 = factory.CreateExceeded(normal, ExceedLevel.Lv1);
            var lv2 = factory.CreateExceeded(normal, ExceedLevel.Lv2);
            var rewards = new CardRewardSystem(CreateTestDeck(), mockRandom,
                new SimpleCardPool(new List<Card> { normal, lv1, lv1.Clone(), lv2 }));
            var choices = rewards.GenerateCardChoices(new Enemy("normal", "敵", 10, ElementType.None, new Vector2Int(3, 0)));
            CollectionAssert.AreEqual(new[] { ExceedLevel.Normal, ExceedLevel.Lv1, ExceedLevel.Lv2 }, choices.Select(c => c.ExceedLevel));
            Assert.IsTrue(choices.All(c => c.Id == normal.Id));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void 通常候補不足は乱数を消費せず空の失敗結果にする(int count)
        {
            var factory = new SimpleCardFactory();
            var definitions = new[] { factory.CreateById("sword"), factory.CreateById("heal") }.Take(count).ToList();
            definitions.Add(null);
            if (count > 0) definitions.Add(definitions[0].Clone());
            var random = new FixedCountingRandom();
            var rewards = new CardRewardSystem(CreateTestDeck(), random, new SimpleCardPool(definitions));
            Assert.IsFalse(rewards.TryGenerateConfiguredChoices(new Enemy("normal", "敵", 10, ElementType.None, new Vector2Int(3, 0)), out var choices));
            Assert.IsEmpty(choices);
            Assert.AreEqual(0, random.Calls);
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 2)]
        [TestCase(4, 3)]
        public void 設定済みボス候補は一から最大三択で重複しない(int count, int expected)
        {
            var factory = new SimpleCardFactory();
            var definitions = new[] { "sword", "heal", "shield", "tuning" }.Take(count).Select(factory.CreateById).ToList();
            if (count > 0) definitions.Add(definitions[0].Clone());
            var random = new FixedCountingRandom();
            var rewards = new CardRewardSystem(CreateTestDeck(), random, new SimpleCardPool(definitions));
            Assert.AreEqual(count > 0, rewards.TryGenerateConfiguredChoices(new Enemy("boss", "ボス", 100, ElementType.None, new Vector2Int(9, 0)), out var choices));
            Assert.AreEqual(expected, choices.Count);
            Assert.AreEqual(expected, random.Calls);
            Assert.AreEqual(expected, choices.Select(c => (c.Id, c.ExceedLevel)).Distinct().Count());
        }
    }
}

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class DepartureDeckRulesTests
    {
        private static Card Card(string id, CardRarity rarity = CardRarity.Normal, ExceedLevel level = ExceedLevel.Normal)
            => new Card(id, id, 1, rarity, CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "fixture", exceedLevel: level);
        private static List<Card> ValidCards() => Enumerable.Range(0, 20).Select(i => Card("fixture_" + i)).ToList();
        private static Dictionary<(string, ExceedLevel), int> Owned(IEnumerable<Card> cards)
            => cards.GroupBy(c => (c.Id, c.ExceedLevel)).ToDictionary(g => g.Key, g => g.Count());

        [TestCase(19)][TestCase(21)][TestCase(0)]
        public void 出撃前だけ二十枚以外を拒否し下書き自体を変更しない(int count)
        {
            var cards = Enumerable.Range(0,count).Select(i=>Card("fixture_"+i)).ToList();
            var draft = new Deck(cards);
            Assert.IsFalse(DepartureDeckRules.TryCreate(cards,Owned(cards),out var result,out var error));
            Assert.IsNull(result);Assert.IsNotEmpty(error);Assert.AreEqual(count,draft.TotalCount);
            CollectionAssert.AreEqual(cards,draft.GetAllCards());
        }
        [TestCase(CardRarity.Normal,4,4,true)]
        [TestCase(CardRarity.Normal,5,5,false)]
        [TestCase(CardRarity.Limited,1,5,true)]
        [TestCase(CardRarity.Limited,2,5,false)]
        [TestCase(CardRarity.Limited,1,6,false)]
        [TestCase(CardRarity.Ultimate,1,1,true)]
        [TestCase(CardRarity.Ultimate,2,2,false)]
        [TestCase(CardRarity.Ultimate,1,2,false)]
        public void 同名上限とレギュレーション合計上限を境界値で検証する(CardRarity rarity,int same,int total,bool valid)
        {
            var cards=ValidCards();for(int i=0;i<total;i++)cards[i]=Card(i<same?"same":"special_"+i,rarity);
            Assert.AreEqual(valid,DepartureDeckRules.TryCreate(cards,Owned(cards),out var deck,out var error),error);
            if(valid)Assert.AreEqual(20,deck.TotalCount);else Assert.IsNull(deck);
        }
        [TestCase(false)][TestCase(true)]
        public void 未所持と所持不足を拒否して所持データを消費しない(bool missing)
        {
            var cards=ValidCards();cards[1]=cards[0];var owned=Owned(cards);
            if(missing)owned.Remove((cards[0].Id,cards[0].ExceedLevel));else owned[(cards[0].Id,cards[0].ExceedLevel)]=1;
            var before=owned.ToArray();Assert.IsFalse(DepartureDeckRules.TryCreate(cards,owned,out _,out _));CollectionAssert.AreEqual(before,owned.ToArray());
        }
        [Test]
        public void 同一IDの段階違いは別カードとして数え各所持枚数と別個体を維持する()
        {
            var templates=new[]{Card("same"),Card("same",level:ExceedLevel.Lv1),Card("same",level:ExceedLevel.Lv2),Card("same",level:ExceedLevel.Lv3),Card("other")};
            var cards=templates.SelectMany(c=>Enumerable.Repeat(c,4)).ToList();var owned=Owned(cards);var before=owned.ToArray();
            Assert.IsTrue(DepartureDeckRules.TryCreate(cards,owned,out var a,out var error),error);
            Assert.IsTrue(DepartureDeckRules.TryCreate(cards,owned,out var b,out error),error);
            Assert.AreEqual(20,a.GetAllCards().Distinct().Count());Assert.AreEqual(20,b.GetAllCards().Distinct().Count());
            Assert.IsFalse(a.GetAllCards().Intersect(b.GetAllCards()).Any());Assert.IsFalse(a.GetAllCards().Intersect(cards).Any());
            a.GetAllCards()[0].Modifier.AdditionalEffects.Add(new AdditionalEffect {EffectType=AdditionalEffectType.Poison,Power=7});
            Assert.IsEmpty(cards[0].Modifier.AdditionalEffects);Assert.IsEmpty(b.GetAllCards()[0].Modifier.AdditionalEffects);
            CollectionAssert.AreEqual(before,owned.ToArray());
            owned[("same",ExceedLevel.Lv1)]=3;Assert.IsFalse(DepartureDeckRules.TryCreate(cards,owned,out _,out _));
        }
        [TestCase("null")][TestCase("rarity")][TestCase("level")][TestCase("conflict")]
        public void 不完全なカード定義を上限回避に利用できない(string kind)
        {
            var cards=ValidCards();var owned=Owned(cards);
            if(kind=="null")cards[0]=null;
            if(kind=="rarity")cards[0]=Card("fixture_0",(CardRarity)99);
            if(kind=="level")cards[0]=Card("fixture_0",level:(ExceedLevel)99);
            if(kind=="conflict"){cards[1]=Card(cards[0].Id,CardRarity.Limited,ExceedLevel.Lv1);owned[(cards[0].Id,ExceedLevel.Lv1)]=1;}
            Assert.IsFalse(DepartureDeckRules.TryCreate(cards,owned,out _,out _));
        }
        [TestCase(0)][TestCase(21)]
        public void 出撃後の零枚と上限超過は獲得履歴を保持して保存再開できる(int count)
        {
            var cards=Enumerable.Range(0,5).SelectMany(i=>Enumerable.Repeat(Card("fixture_"+i),4)).ToList();
            var owned=Owned(cards);Assert.IsTrue(DepartureDeckRules.TryCreate(cards,owned,out var deck,out var error),error);
            var map=DungeonConfig.CreateTestDungeon(20,30,3);var definitions=DungeonCheckpointSession.CreateDefault(map,deck).Definitions;
            var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-departure-"+Guid.NewGuid().ToString("N"));
            try {
                var store=new Olca.Unity.FileCheckpointStore(definitions,directory);
                var session=new DungeonCheckpointSession(definitions,store);
                var manager=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
                var saved=store.Read();saved.Sequence++;saved.Boundary=CheckpointBoundary.RewardCompleted;saved.PositionMilli=1000;
                var acquired=saved.Cards[0].Copy();acquired.InstanceId="acquired-fixture";
                saved.Cards=count==0?new[]{acquired}:saved.Cards.Concat(new[]{acquired}).ToArray();
                saved.DeckOrder=count==0?new string[0]:saved.DeckOrder.Concat(new[]{acquired.InstanceId}).ToArray();saved.ObtainedOrder=new[]{acquired.InstanceId};
                Assert.IsTrue(store.TryWrite(saved,out error),error);owned.Clear();
                var bytes=System.IO.File.ReadAllBytes(store.ActivePath);
                var reopened=new DungeonCheckpointSession(definitions,new Olca.Unity.FileCheckpointStore(definitions,directory));
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(reopened,new MockRandomProvider(),new MockLogger(),out var resumed,out error),error);
                resumed.ActivateCheckpoint();Assert.AreEqual(count,resumed.Dungeon.GetCurrentDeck().TotalCount);Assert.AreEqual(1,resumed.Dungeon.GetObtainedCards().Count);
                CollectionAssert.AreEqual(saved.DeckOrder,reopened.Last.DeckOrder);CollectionAssert.AreEqual(saved.ObtainedOrder,reopened.Last.ObtainedOrder);
                CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
            } finally {if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }
    }
}
