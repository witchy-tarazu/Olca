using NUnit.Framework;
using System.Linq;
using Olca.Application.Battle;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class DungeonBattleManagerCardRewardTests
    {
        private DungeonBattleManager OnceManager(CountingRewardRandom random)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 30; i++) cells.Add(new MapCell(i));
            foreach (int x in new[] { 3, 15, 29 }) cells[x] = new MapCell(x,
                new Enemy(x == 29 ? "boss" : "slime", "敵", 50, ElementType.None, new Vector2Int(x, 0)));
            return new DungeonBattleManager(new DungeonConfig("once", "報酬検証", 30, cells),
                new PlayerCharacter(100), CreateTestDeck(), random, mockLogger);
        }

        [TestCase(0f)]
        [TestCase(0.9f)]
        public void 重複した終了通知では報酬抽選と結果通知を繰り返さない(float roll)
        {
            var random = new CountingRewardRandom { Roll = roll }; var m = OnceManager(random);
            int ends = 0, offers = 0, returns = 0;
            m.OnBattleEnded += () => ends++; m.OnCardRewardPresented += _ => offers++;
            m.Dungeon.OnReturnToDungeon += () => returns++;
            var battle = AdvanceToBattle(m); random.FloatCalls = 0;
            battle.Enemies[0].TakeDamage(1000, ElementType.None);
            m.Dungeon.OnBattleCompleted(true); m.Dungeon.OnBattleCompleted(false);
            Assert.AreEqual(1, ends); Assert.AreEqual(1, random.FloatCalls);
            Assert.AreEqual(roll < 0.5f ? 1 : 0, offers);
            if (roll < 0.5f) m.SelectCardReward(null);
            Assert.AreEqual(1, returns); Assert.AreEqual(GamePhase.Dungeon, m.CurrentPhase);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void 受取とスキップの確定後は直接再通知しても獲得と復帰を繰り返さない(bool skipFirst, bool skipSecond)
        {
            var m = OnceManager(new CountingRewardRandom()); IReadOnlyList<Card> choices = null;
            m.OnCardRewardPresented += c => choices = c; int returns = 0;
            m.Dungeon.OnReturnToDungeon += () => returns++;
            AdvanceToBattle(m).Enemies[0].TakeDamage(1000, ElementType.None);
            int before = m.Dungeon.GetCurrentDeck().TotalCount;
            m.SelectCardReward(skipFirst ? null : choices[0]);
            m.Dungeon.OnCardSelectionCompleted(skipSecond ? null : choices[0]);
            Assert.AreEqual(skipFirst ? 0 : 1, m.Dungeon.GetObtainedCards().Count);
            Assert.AreEqual(before + (skipFirst ? 0 : 1), m.Dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(1, returns);
        }

        [Test]
        public void 候補外カードは受け取らず正しい候補の選択待ちを維持する()
        {
            var m = OnceManager(new CountingRewardRandom()); IReadOnlyList<Card> choices = null;
            m.OnCardRewardPresented += c => choices = c;
            AdvanceToBattle(m).Enemies[0].TakeDamage(1000, ElementType.None);
            m.SelectCardReward(choices[0].Clone());
            Assert.AreEqual(GamePhase.CardReward, m.CurrentPhase); Assert.AreEqual(0, m.Dungeon.GetObtainedCards().Count);
            m.SelectCardReward(choices[0]); Assert.AreEqual(1, m.Dungeon.GetObtainedCards().Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 終了通知または獲得イベントからの再入でも確定は一度だけ(bool duringAdd)
        {
            var random = new CountingRewardRandom(); var m = OnceManager(random); IReadOnlyList<Card> choices = null;
            m.OnCardRewardPresented += c => choices = c; bool reentered = false; int ends = 0, returns = 0;
            m.Dungeon.OnReturnToDungeon += () => returns++;
            m.OnBattleEnded += () => { ends++; if (!duringAdd && !reentered) { reentered = true; m.Dungeon.OnBattleCompleted(true); } };
            var rewards = (CardRewardSystem)typeof(DungeonSystem).GetField("cardRewardSystem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(m.Dungeon);
            rewards.OnCardAdded += card => { if (duringAdd && !reentered) { reentered = true; m.SelectCardReward(card); } };
            var battle = AdvanceToBattle(m); random.FloatCalls = 0; battle.Enemies[0].TakeDamage(1000, ElementType.None);
            m.SelectCardReward(choices[0]);
            Assert.IsTrue(reentered); Assert.AreEqual(1, ends); Assert.AreEqual(1, random.FloatCalls);
            Assert.AreEqual(1, m.Dungeon.GetObtainedCards().Count); Assert.AreEqual(1, returns);
        }

        [Test]
        public void 確定後の次戦は新規抽選でき最後のボス報酬後に一度だけクリアする()
        {
            var random = new CountingRewardRandom(); var m = OnceManager(random); IReadOnlyList<Card> choices = null;
            int offers = 0, clears = 0, returns = 0; m.OnCardRewardPresented += c => { choices = c; offers++; };
            m.OnDungeonClearCompleted += _ => clears++; m.Dungeon.OnReturnToDungeon += () => returns++;
            for (int n = 0; n < 3; n++)
            {
                m.ResumeAfterBattle(); var battle = AdvanceToBattle(m); Assert.IsNotNull(battle);
                battle.Enemies[0].TakeDamage(1000, ElementType.None); m.SelectCardReward(choices[0]);
            }
            Assert.AreEqual(2, random.FloatCalls, "雑魚2戦のみ確率抽選しボスは保証");
            Assert.AreEqual(3, offers); Assert.AreEqual(3, m.Dungeon.GetObtainedCards().Count);
            Assert.AreEqual(2, returns); Assert.AreEqual(1, clears); Assert.AreEqual(GamePhase.DungeonCleared, m.CurrentPhase);
            m.Dungeon.OnCardSelectionCompleted(null); m.Dungeon.OnBattleCompleted(true); Assert.AreEqual(1, clears);
        }

        private MockRandomProvider mockRandom;
        private MockLogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockRandom = new MockRandomProvider(0, 0f);  // 報酬ありを明示するfixture
            mockLogger = new MockLogger();
        }

        private DungeonBattleManager CreateManager()
        {
            // index 3 に通常敵（スライム）、index 9 にボスを配置
            // CreateTestDungeon(10) はボスのみ（100%ドロップ）のため、通常敵の動作を検証できない
            var cells = new List<MapCell>
            {
                new MapCell(0), new MapCell(1), new MapCell(2),
                new MapCell(3, new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0))),
                new MapCell(4), new MapCell(5), new MapCell(6), new MapCell(7), new MapCell(8),
                new MapCell(9, new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(9, 0))),
            };
            var config = new DungeonConfig("test_dungeon", "テストダンジョン", 10, cells);
            var player = new PlayerCharacter(
                maxHp: 100,
                initialPosition: new Vector2Int(0, 0),
                attackPower: 10);
            var deck = CreateTestDeck();
            return new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 最後の敵の報酬確定_クリアイベントで最終獲得数とデッキ数を参照できる(bool skip)
        {
            var manager = new DungeonBattleManager(DungeonConfig.CreateTestDungeon(10),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            var battle = AdvanceToBattle(manager);
            List<Card> choices = null;
            manager.OnCardRewardPresented += cards => choices = new List<Card>(cards);
            battle.Enemies[0].TakeDamage(1000, ElementType.None);
            manager.Tick(100);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            Assert.IsNotNull(choices);
            int initialDeckCount = manager.Dungeon.GetCurrentDeck().TotalCount;
            int clearCount = 0;
            manager.OnDungeonClearCompleted += cards =>
            {
                clearCount++;
                Assert.AreEqual(GamePhase.DungeonCleared, manager.CurrentPhase);
                Assert.AreEqual(skip ? 0 : 1, cards.Count);
                Assert.AreEqual(skip ? 0 : 1, manager.Dungeon.GetObtainedCards().Count);
                Assert.AreEqual(initialDeckCount + (skip ? 0 : 1), manager.Dungeon.GetCurrentDeck().TotalCount);
            };

            manager.SelectCardReward(skip ? null : choices[0]);

            Assert.AreEqual(1, clearCount);
            Assert.AreEqual(GamePhase.DungeonCleared, manager.CurrentPhase);
        }

        private Deck CreateTestDeck()
        {
            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃"),
            };
            return new Deck(cards);
        }

        /// <summary>バトルフェーズに入るまでTickを進める</summary>
        private BattleSystem AdvanceToBattle(DungeonBattleManager manager)
        {
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (manager.CurrentPhase == GamePhase.Battle) break;
            }
            return manager.CurrentBattle;
        }

        [Test]
        public void バトル勝利_カードドロップあり_CardRewardフェーズになる()
        {
            // Arrange: 通常敵のカード報酬を確認する
            mockRandom.SetFixedValue(0f);
            var manager = CreateManager();
            var battle = AdvanceToBattle(manager);
            Assert.IsNotNull(battle, "バトルが開始された");

            // Act: 敵を倒す
            var enemy = battle.Enemies[0];
            while (!enemy.IsDead)
                enemy.TakeDamage(100, ElementType.None);
            manager.Tick(deltaTimeMs: 100);

            // Assert
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase,
                "カードドロップ時はCardRewardフェーズになる");
        }

        [Test]
        public void バトル勝利_乱数が高いと報酬なしでDungeonフェーズに戻る()
        {
            // Arrange: 0.9は基本率50%の範囲外
            mockRandom.SetFixedValue(0.9f);
            var manager = CreateManager();
            var battle = AdvanceToBattle(manager);
            Assert.IsNotNull(battle, "バトルが開始された");

            // Act: 敵を倒す
            var enemy = battle.Enemies[0];
            while (!enemy.IsDead)
                enemy.TakeDamage(100, ElementType.None);
            manager.Tick(deltaTimeMs: 100);

            // Assert
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase,
                "無報酬なら選択待ちを挟まず探索へ戻る");
        }

        [Test]
        public void OnCardRewardPresented_カードドロップ時に通常敵は3枚の選択肢で発火する()
        {
            // Arrange
            mockRandom.SetFixedValue(0f);
            var manager = CreateManager();

            IReadOnlyList<Card> receivedChoices = null;
            manager.OnCardRewardPresented += choices => receivedChoices = choices;

            var battle = AdvanceToBattle(manager);

            // Act: 敵を倒す
            var enemy = battle.Enemies[0];
            while (!enemy.IsDead)
                enemy.TakeDamage(100, ElementType.None);
            manager.Tick(deltaTimeMs: 100);

            // Assert
            Assert.IsNotNull(receivedChoices, "OnCardRewardPresentedが発火した");
            Assert.AreEqual(3, receivedChoices.Count, "通常敵は3枚の選択肢");
        }

        [Test]
        public void OnBattleEnded_バトル終了時に発火する()
        {
            // Arrange: ドロップなし（フェーズ変化をシンプルに）
            mockRandom.SetFixedValue(0.9f);
            var manager = CreateManager();

            bool battleEndedFired = false;
            manager.OnBattleEnded += () => battleEndedFired = true;

            var battle = AdvanceToBattle(manager);

            // Act: 敵を倒す
            var enemy = battle.Enemies[0];
            while (!enemy.IsDead)
                enemy.TakeDamage(100, ElementType.None);
            manager.Tick(deltaTimeMs: 100);

            // Assert
            Assert.IsTrue(battleEndedFired, "OnBattleEndedが発火した");
        }

        [Test]
        public void SelectCardReward_カードを選択するとデッキに追加されてDungeonフェーズに戻る()
        {
            // Arrange
            mockRandom.SetFixedValue(0f);
            var manager = CreateManager();

            IReadOnlyList<Card> receivedChoices = null;
            manager.OnCardRewardPresented += choices => receivedChoices = choices;

            var battle = AdvanceToBattle(manager);
            int initialDeckCount = manager.Dungeon.GetCurrentDeck().TotalCount;

            // バトル勝利 → CardRewardフェーズ
            var enemy = battle.Enemies[0];
            while (!enemy.IsDead)
                enemy.TakeDamage(100, ElementType.None);
            manager.Tick(deltaTimeMs: 100);

            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            Assert.IsNotNull(receivedChoices);

            // Act: カードを選択
            manager.SelectCardReward(receivedChoices[0]);

            // Assert
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase, "ダンジョンフェーズに戻る");
            Assert.AreEqual(initialDeckCount + 1, manager.Dungeon.GetCurrentDeck().TotalCount,
                "デッキに1枚追加される");
        }

        [Test]
        public void SelectCardReward_null選択スキップ_枚数変わらずDungeonに戻る()
        {
            // Arrange
            mockRandom.SetFixedValue(0f);
            var manager = CreateManager();

            var battle = AdvanceToBattle(manager);
            int initialDeckCount = manager.Dungeon.GetCurrentDeck().TotalCount;

            var enemy = battle.Enemies[0];
            while (!enemy.IsDead)
                enemy.TakeDamage(100, ElementType.None);
            manager.Tick(deltaTimeMs: 100);

            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);

            // Act: カードをスキップ（null選択）
            manager.SelectCardReward(null);

            // Assert
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase, "ダンジョンフェーズに戻る");
            Assert.AreEqual(initialDeckCount, manager.Dungeon.GetCurrentDeck().TotalCount,
                "デッキ枚数は変わらない");
        }

        [Test]
        public void SelectCardReward_CardRewardフェーズ以外では無効()
        {
            // Arrange: ダンジョンフェーズで呼ぶ
            var manager = CreateManager();
            int initialDeckCount = manager.Dungeon.GetCurrentDeck().TotalCount;

            var dummyCard = new Card("test", "テスト", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "テスト");

            // Act
            manager.SelectCardReward(dummyCard);

            // Assert
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase, "フェーズが変わらない");
            Assert.AreEqual(initialDeckCount, manager.Dungeon.GetCurrentDeck().TotalCount,
                "デッキ枚数が変わらない");
        }

        private sealed class CountingRewardRandom : IRandomProvider
        {
            public float Roll;
            public int FloatCalls;
            public int Range(int min, int max) => min;
            public float Range(float min, float max) { FloatCalls++; return Roll; }
            public float Value => Roll;
        }

        [TestCase(0f, true)]
        [TestCase(0.4999f, true)]
        [TestCase(0.5f, false)]
        [TestCase(0.5001f, false)]
        [TestCase(1f, false)]
        public void 雑魚報酬は半分未満だけ当選し一回だけ抽選する(float roll, bool expected)
        {
            var random = new CountingRewardRandom { Roll = roll };
            var rewards = new CardRewardSystem(CreateTestDeck(), random);
            Assert.AreEqual(expected, rewards.ShouldDropCard(new Enemy("slime", "敵", 10, ElementType.None, new Vector2Int(3, 0))));
            Assert.AreEqual(1, random.FloatCalls);
        }

        [TestCase("boss")]
        [TestCase("final_boss")]
        public void ボス保証は高い乱数でも抽選を消費せず報酬を出す(string id)
        {
            var random = new CountingRewardRandom { Roll = 1f };
            var rewards = new CardRewardSystem(CreateTestDeck(), random);
            Assert.IsTrue(rewards.ShouldDropCard(new Enemy(id, "ボス", 100, ElementType.None, new Vector2Int(9, 0))));
            Assert.AreEqual(0, random.FloatCalls);
        }

        [TestCase(0.4999f, true)]
        [TestCase(0.5f, false)]
        public void 複数敵の勝利でも抽選は一回で無報酬なら探索へ復帰する(float roll, bool drops)
        {
            var random = new CountingRewardRandom { Roll = roll };
            var cells = new List<MapCell>();
            for (int i = 0; i < 30; i++) cells.Add(new MapCell(i));
            cells[3] = new MapCell(3, new Enemy("a", "敵A", 50, ElementType.None, new Vector2Int(3, 0)));
            cells[4] = new MapCell(4, new Enemy("b", "敵B", 50, ElementType.None, new Vector2Int(4, 0)));
            cells[29] = new MapCell(29, new Enemy("boss", "ボス", 100, ElementType.None, new Vector2Int(29, 0)));
            var manager = new DungeonBattleManager(new DungeonConfig("multi", "複数敵", 30, cells),
                new PlayerCharacter(100), CreateTestDeck(), random, mockLogger);
            int offers = 0, ends = 0;
            manager.OnCardRewardPresented += _ => offers++;
            manager.OnBattleEnded += () => ends++;
            var battle = AdvanceToBattle(manager);
            Assert.AreEqual(2, battle.Enemies.Count);
            random.FloatCalls = 0;
            battle.Enemies[0].TakeDamage(1000, ElementType.None);
            Assert.AreEqual(0, random.FloatCalls);
            battle.Enemies[1].TakeDamage(1000, ElementType.None);
            manager.Tick(0);
            Assert.AreEqual(1, random.FloatCalls);
            Assert.AreEqual(drops ? 1 : 0, offers);
            Assert.AreEqual(drops ? GamePhase.CardReward : GamePhase.Dungeon, manager.CurrentPhase);
            if (drops) manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.IsNull(manager.CurrentBattle);
            Assert.AreEqual(1, ends);
            manager.Tick(0);
            Assert.AreEqual(1, random.FloatCalls);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void 注入プールはManagerから報酬へ届き取得とスキップの履歴を保つ(bool skip, bool boss)
        {
            var factory = new SimpleCardFactory();
            var definitions = new List<Card> { factory.CreateById("heal"), factory.CreateById("tuning"), factory.CreateById("sword") };
            var cells = new List<MapCell>();
            for (int i = 0; i < 30; i++) cells.Add(new MapCell(i));
            cells[3] = new MapCell(3, new Enemy(boss ? "boss" : "slime", "敵", 50, ElementType.None, new Vector2Int(3, 0)));
            if (!boss) cells[29] = new MapCell(29, new Enemy("boss", "ボス", 100, ElementType.None, new Vector2Int(29, 0)));
            var manager = new DungeonBattleManager(new DungeonConfig("pool", "注入検証", 30, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger,
                rewardPool: new SimpleCardPool(definitions));
            IReadOnlyList<Card> offered = null;
            manager.OnCardRewardPresented += cards => offered = cards;
            var battle = AdvanceToBattle(manager);
            battle.Enemies[0].TakeDamage(1000, ElementType.None);
            manager.Tick(0);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            CollectionAssert.AreEqual(definitions.Select(c => c.Id), offered.Select(c => c.Id));
            int before = manager.Dungeon.GetCurrentDeck().TotalCount;
            var chosen = offered[0];
            manager.SelectCardReward(skip ? null : chosen);
            Assert.AreEqual(boss ? GamePhase.DungeonCleared : GamePhase.Dungeon, manager.CurrentPhase);
            Assert.AreEqual(before + (skip ? 0 : 1), manager.Dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(skip ? 0 : 1, manager.Dungeon.GetObtainedCards().Count);
            if (!skip)
            {
                Assert.AreSame(chosen, manager.Dungeon.GetObtainedCards()[0]);
                Assert.AreEqual(CardCategory.Defense, chosen.Category);
                Assert.AreEqual(definitions[0].Power, chosen.Power);
                var recipient = new PlayerCharacter(100);
                recipient.TakeDamage(40);
                var nextBattle = new BattleSystem(recipient, new List<Enemy> {
                    new Enemy("next", "次戦", 100, ElementType.None, new Vector2Int(3, 0)) }, new Deck());
                nextBattle.CardUsage.EnqueueCards(new List<Card> { chosen });
                Assert.IsTrue(nextBattle.CardUsage.TryUseCurrentCard());
                Assert.AreEqual(80, recipient.CurrentHp, "報酬Healは攻撃の仮カードでなく回復として機能する");
            }
            Assert.AreEqual(3, definitions.Count);
        }
    }
}
