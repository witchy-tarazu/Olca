using NUnit.Framework;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Domain;
using Olca.Tests.Mocks;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class DungeonSystemTests
    {
        private MockRandomProvider mockRandom;
        private MockLogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockRandom = new MockRandomProvider(0);
            mockLogger = new MockLogger();
        }

        /// <summary>
        /// テスト用のダンジョンシステムを作成
        /// </summary>
        private DungeonSystem CreateDungeonSystem(int cellCount = 20)
        {
            var config = DungeonConfig.CreateTestDungeon(cellCount);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            return new DungeonSystem(config, player, deck, mockRandom, mockLogger);
        }

        /// <summary>
        /// テスト用のデッキを作成
        /// </summary>
        private Deck CreateTestDeck()
        {
            var cards = new System.Collections.Generic.List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃"),
                new Card("heal", "ヒール", 2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 20, "HP回復")
            };

            return new Deck(cards);
        }

        [Test]
        public void Tick_プレイヤーが前進する()
        {
            // Arrange
            var dungeon = CreateDungeonSystem();
            float initialPosition = dungeon.PlayerPosition;

            // Act
            dungeon.Tick(deltaTime: 1.0f);

            // Assert
            Assert.Greater(dungeon.PlayerPosition, initialPosition, "プレイヤーが前進している");
        }

        [Test]
        public void Tick_バトル中は更新されない()
        {
            // Arrange
            var dungeon = CreateDungeonSystem();

            // 強制的にバトル状態にする（リフレクションまたは接敵させる）
            // 今回は接敵まで進める
            for (int i = 0; i < 100; i++)
            {
                dungeon.Tick(0.1f);
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");

            float positionDuringBattle = dungeon.PlayerPosition;

            // Act
            dungeon.Tick(1.0f);

            // Assert
            Assert.AreEqual(positionDuringBattle, dungeon.PlayerPosition, "バトル中は移動しない");
        }

        [Test]
        public void 敵との接敵時_バトルが開始される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            bool battleStarted = false;
            ContactInfo receivedContactInfo = null;

            dungeon.OnBattleStart += (contactInfo) =>
            {
                battleStarted = true;
                receivedContactInfo = contactInfo;
            };

            // Act: 敵がいるマスまで進める（5マス目に敵がいる）
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(0.1f);
                if (battleStarted) break;
            }

            // Assert
            Assert.IsTrue(battleStarted, "バトルが開始された");
            Assert.IsNotNull(receivedContactInfo, "接敵情報が渡された");
            Assert.IsNotNull(receivedContactInfo.Enemy, "敵情報が含まれている");
            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");
        }

        [Test]
        public void ダッシュ中の接敵_ダッシュ効果が設定される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            ContactInfo receivedContactInfo = null;

            dungeon.OnBattleStart += (contactInfo) =>
            {
                receivedContactInfo = contactInfo;
            };

            // Act: ダッシュしながら敵に接近
            dungeon.StartDash();
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(0.1f);
                if (receivedContactInfo != null) break;
            }

            // Assert
            Assert.IsNotNull(receivedContactInfo, "接敵情報が渡された");
            Assert.IsTrue(receivedContactInfo.IsDashContact, "ダッシュ接敵フラグが立っている");
            Assert.IsNotNull(receivedContactInfo.DashEffect, "ダッシュ効果が設定されている");
        }

        [Test]
        public void Jump_ジャンプできる()
        {
            // Arrange
            var dungeon = CreateDungeonSystem();
            float initialHeight = dungeon.PlayerHeight;

            // Act
            dungeon.Jump();
            dungeon.Tick(0.1f);  // ジャンプ更新

            // Assert
            Assert.Greater(dungeon.PlayerHeight, initialHeight, "ジャンプして高さが上がっている");
        }

        [Test]
        public void OnBattleCompleted_勝利後にダンジョンが再開される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(0.1f);
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");

            // カードドロップなし（60%未満にする）
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.IsFalse(dungeon.IsInBattle, "バトル状態が解除された");

            // プレイヤーが再び移動できることを確認
            float positionAfterBattle = dungeon.PlayerPosition;
            dungeon.Tick(1.0f);
            Assert.Greater(dungeon.PlayerPosition, positionAfterBattle, "バトル後も移動できる");
        }

        [Test]
        public void OnBattleCompleted_敗北時にゲームオーバーイベントが発火する()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            bool gameOverFired = false;

            dungeon.OnDungeonFailed += () => gameOverFired = true;

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(0.1f);
                if (dungeon.IsInBattle) break;
            }

            // Act: バトル敗北
            dungeon.OnBattleCompleted(victory: false);

            // Assert
            Assert.IsTrue(gameOverFired, "ゲームオーバーイベントが発火した");
        }

        [Test]
        public void OnBattleCompleted_カードドロップ時に選択画面が表示される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            System.Collections.Generic.List<Card> offeredCards = null;

            dungeon.OnCardRewardOffered += (cards) =>
            {
                offeredCards = cards;
            };

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(0.1f);
                if (dungeon.IsInBattle) break;
            }

            // mockRandomが0.5を返すように設定（ドロップ率60%は成功）
            mockRandom.SetFixedValue(0.5f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.IsNotNull(offeredCards, "カード選択画面が表示された");
            Assert.Greater(offeredCards.Count, 0, "カード選択肢が提示された");
        }

        [Test]
        public void OnCardSelectionCompleted_カードがデッキに追加される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(0.1f);
                if (dungeon.IsInBattle) break;
            }

            int initialDeckSize = dungeon.GetCurrentDeck().TotalCount;

            var selectedCard = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            // Act: カード選択完了
            dungeon.OnCardSelectionCompleted(selectedCard);

            // Assert
            Assert.AreEqual(initialDeckSize + 1, dungeon.GetCurrentDeck().TotalCount,
                "デッキにカードが追加された");
        }

        [Test]
        public void GetObtainedCards_入手したカード一覧を取得できる()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);

            var card1 = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            // Act
            dungeon.OnCardSelectionCompleted(card1);

            // Assert
            Assert.AreEqual(1, dungeon.GetObtainedCards().Count, "入手カード一覧に1枚追加");
            Assert.AreEqual(card1, dungeon.GetObtainedCards()[0]);
        }

        [Test]
        public void Tick_障害物に接触_ペナルティイベントが発火する()
        {
            // Arrange
            var obstacle = new Obstacle("spike", "トゲ", 15);  // 即時ダメージ障害物
            // マップはインデックスでセルを取得するため、インデックス3に障害物を配置するなら
            // それ以前のセルも必要
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),  // 空白
                new MapCell(1),  // 空白
                new MapCell(2),  // 空白
                new MapCell(3, obstacle, height: 0f)  // 障害物
            };
            var config = new DungeonConfig(
                dungeonId: "test_obstacle",
                dungeonName: "障害物テストダンジョン",
                totalCellCount: 10,
                mapCells: cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            PenaltyInfo receivedPenalty = null;
            dungeon.OnObstaclePenalty += (penalty) => receivedPenalty = penalty;

            // Act: 障害物位置まで進める（位置3に到達）
            for (int i = 0; i < 50; i++)
            {
                dungeon.Tick(0.1f);
                if (receivedPenalty != null) break;
            }

            // Assert
            Assert.IsNotNull(receivedPenalty, "ペナルティイベントが発火");
            Assert.AreEqual(ObstaclePenaltyType.ImmediateDamage, receivedPenalty.PenaltyType);
            Assert.AreEqual(15, receivedPenalty.Damage);
        }

        [Test]
        public void Tick_障害物をジャンプで回避_ペナルティが発生しない()
        {
            // Arrange
            // 障害物を位置2に配置（より近い位置でテスト）
            var obstacle = new Obstacle("spike", "トゲ", 15);  // 即時ダメージ障害物
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),  // 空白
                new MapCell(1),  // 空白
                new MapCell(2, obstacle, height: 0f)  // 障害物
            };
            var config = new DungeonConfig(
                dungeonId: "test_obstacle",
                dungeonName: "障害物テストダンジョン",
                totalCellCount: 10,
                mapCells: cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            PenaltyInfo receivedPenalty = null;
            dungeon.OnObstaclePenalty += (penalty) => receivedPenalty = penalty;

            // Act: 障害物の手前でジャンプして飛び越える
            // BASE_SPEED=2.0, deltaTime=0.1 → 1Tickで0.2移動
            // JUMP_DURATION=0.5秒 → 5Tickでジャンプ終了（その間に0.2*5=1.0マス移動）
            // 位置2に到達するまで約10Tick
            // 位置1.5付近でジャンプ開始すれば、位置2通過時（2.5Tick後）にまだジャンプ中
            bool jumped = false;
            for (int i = 0; i < 30; i++)
            {
                // 位置1.5付近でジャンプ開始
                if (!jumped && dungeon.PlayerPosition >= 1.5f)
                {
                    dungeon.Jump();
                    jumped = true;
                }
                dungeon.Tick(0.1f);
                if (dungeon.PlayerPosition > 3.0f) break;
            }

            // デバッグ: ログを確認
            string logInfo = string.Join("\n", mockLogger.InfoLogs);
            UnityEngine.Debug.Log($"Position: {dungeon.PlayerPosition}, Height: {dungeon.PlayerHeight}\nLogs:\n{logInfo}");

            // Assert
            Assert.IsNull(receivedPenalty, $"ジャンプで回避したのでペナルティなし\nLogs:\n{logInfo}");
        }

        [Test]
        public void 障害物ダメージでゲームオーバー()
        {
            // Arrange
            var obstacle = new Obstacle("spike", "トゲ", 150);  // 即死ダメージ障害物
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),  // 空白
                new MapCell(1),  // 空白
                new MapCell(2),  // 空白
                new MapCell(3, obstacle, height: 0f)  // 障害物
            };
            var config = new DungeonConfig(
                dungeonId: "test_obstacle",
                dungeonName: "障害物テストダンジョン",
                totalCellCount: 10,
                mapCells: cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            bool gameOverFired = false;
            dungeon.OnDungeonFailed += () => gameOverFired = true;

            // Act: 障害物位置まで進める
            for (int i = 0; i < 50; i++)
            {
                dungeon.Tick(0.1f);
                if (gameOverFired) break;
            }

            // Assert
            Assert.IsTrue(gameOverFired, "HPが0以下になりゲームオーバー");
            Assert.LessOrEqual(player.CurrentHp, 0);
        }
    }
}
