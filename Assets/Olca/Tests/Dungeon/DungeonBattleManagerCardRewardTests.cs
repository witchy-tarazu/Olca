using NUnit.Framework;
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
        private MockRandomProvider mockRandom;
        private MockLogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockRandom = new MockRandomProvider(0);  // 通常敵もカードをドロップする
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
        public void バトル勝利_乱数が高くても報酬を提示し_選択完了後Dungeonフェーズに戻る()
        {
            // Arrange: 現在の通常敵ドロップ率は100%のため、0.9でも報酬が出る
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
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase,
                "通常敵も報酬選択を待つ");
            manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase,
                "報酬をスキップするとDungeonフェーズに戻る");
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
    }
}
