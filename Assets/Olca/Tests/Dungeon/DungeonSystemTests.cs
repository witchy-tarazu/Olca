using NUnit.Framework;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;
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
        /// 通常敵（スライム、index 3）とボス（index 9）を配置したダンジョンを作成
        /// ボスのみになる CreateTestDungeon(10) の代替として使用する
        /// </summary>
        private DungeonSystem CreateDungeonSystemWithEarlyNormalEnemy()
        {
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0), new MapCell(1), new MapCell(2),
                new MapCell(3, new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0))),
                new MapCell(4), new MapCell(5), new MapCell(6), new MapCell(7), new MapCell(8),
                new MapCell(9, new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(9, 0))),
            };
            var config = new DungeonConfig("test_dungeon", "テストダンジョン", 10, cells);
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

            // Act - 整数ベース版: 1000ms = 1秒
            dungeon.Tick(deltaTimeMs: 1000);

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
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");

            float positionDuringBattle = dungeon.PlayerPosition;

            // Act
            dungeon.Tick(deltaTimeMs: 1000);  // 1000ms = 1秒

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
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
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
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (receivedContactInfo != null) break;
            }

            // Assert
            Assert.IsNotNull(receivedContactInfo, "接敵情報が渡された");
            Assert.IsTrue(receivedContactInfo.IsDashContact, "ダッシュ接敵フラグが立っている");
            Assert.IsNotNull(receivedContactInfo.DashEffect, "ダッシュ効果が設定されている");
        }

        [Test]
        public void ジャンプ中に通常敵へ接近すると接敵せず回避できる()
        {
            // Arrange
            var enemy = new Enemy("enemy_5", "スライム", 50, ElementType.None, new Vector2Int(5, 0));
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),
                new MapCell(1),
                new MapCell(2),
                new MapCell(3),
                new MapCell(4),
                new MapCell(5, enemy),
                new MapCell(6),
                new MapCell(7),
                new MapCell(8),
                new MapCell(9)
            };
            var config = new DungeonConfig("jump_avoid_test", "ジャンプ回避テスト", 10, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var dungeon = new DungeonSystem(config, player, CreateTestDeck(), mockRandom, mockLogger);
            bool battleStarted = false;
            dungeon.OnBattleStart += _ => battleStarted = true;

            // Act: 接敵判定が敵セル5を見るタイミングまでジャンプを維持する
            dungeon.Jump();
            for (int i = 0; i < 5; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
            }

            // Assert
            Assert.IsFalse(battleStarted, "ジャンプ中なので通常敵を回避して接敵しない");
            Assert.IsFalse(dungeon.IsInBattle, "回避時はバトル状態にならない");
            Assert.Greater(dungeon.PlayerPosition, 4f, "敵の手前まで進んでいる");
        }

        [Test]
        public void Jump_ジャンプできる()
        {
            // Arrange
            var dungeon = CreateDungeonSystem();
            float initialHeight = dungeon.PlayerHeight;

            // Act
            dungeon.Jump();
            dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒、ジャンプ更新

            // Assert
            Assert.Greater(dungeon.PlayerHeight, initialHeight, "ジャンプして高さが上がっている");
        }

        [Test]
        public void OnBattleCompleted_勝利後にダンジョンが再開される()
        {
            // Arrange: index 3 に通常敵、index 9 にボス（通常敵撃破後も移動継続できる構成）
            var dungeon = CreateDungeonSystemWithEarlyNormalEnemy();

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");

            // カードドロップなし（通常敵の閾値0.6未満にならない）
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.IsFalse(dungeon.IsInBattle, "バトル状態が解除された");

            // ResumePlayerMovement() を呼ぶまでは移動しない（遷移アニメーション待ち）
            float positionAfterBattle = dungeon.PlayerPosition;
            dungeon.Tick(deltaTimeMs: 1000);
            Assert.AreEqual(positionAfterBattle, dungeon.PlayerPosition, "ResumePlayerMovement前は移動しない");

            // ResumePlayerMovement() 後は再び移動できる
            dungeon.ResumePlayerMovement();
            dungeon.Tick(deltaTimeMs: 1000);  // 1000ms = 1秒
            Assert.Greater(dungeon.PlayerPosition, positionAfterBattle, "ResumePlayerMovement後は移動できる");
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
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
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
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
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
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
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
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
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
            // BASE_SPEED_MILLI_PER_SEC=10000, deltaTimeMs=100 → 1Tickで1.0移動
            // JUMP_DURATION_MS=500 → 5Tickでジャンプ終了（その間に1.0*5=5.0マス移動）
            // 位置2に到達するまで約2Tick
            // 位置0.9以上でジャンプ開始すれば、次のTick（位置2通過時）にまだジャンプ中
            bool jumped = false;
            for (int i = 0; i < 30; i++)
            {
                // 位置0.9付近でジャンプ開始（速度10/s・100ms刻みの場合、1Tick=1.0マス）
                if (!jumped && dungeon.PlayerPosition >= 0.9f)
                {
                    dungeon.Jump();
                    jumped = true;
                }
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (dungeon.PlayerPosition > 3.0f) break;
            }

            // デバッグ: ログを確認
            string logInfo = string.Join("\n", mockLogger.InfoLogs);

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
                dungeon.Tick(deltaTimeMs: 100);  // 100ms = 0.1秒
                if (gameOverFired) break;
            }

            // Assert
            Assert.IsTrue(gameOverFired, "HPが0以下になりゲームオーバー");
            Assert.LessOrEqual(player.CurrentHp, 0);
        }

        // ========== フェーズ5: バトルシステム統合テスト ==========

        [Test]
        public void 接敵時_BattleSystemが初期化される()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);

            // Act: 敵がいるマスまで進める（5マス目に敵がいる）
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert
            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");
            Assert.IsNotNull(dungeon.CurrentBattle, "BattleSystemが初期化されている");
        }

        [Test]
        public void 接敵時_プレイヤーHPが共有されている()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            // プレイヤーに事前ダメージ
            player.TakeDamage(30);
            int hpBeforeBattle = player.CurrentHp;  // 70

            // Act: 敵がいるマスまで進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert
            Assert.IsNotNull(dungeon.CurrentBattle, "BattleSystemが存在する");
            Assert.AreEqual(hpBeforeBattle, dungeon.CurrentBattle.Player.CurrentHp,
                "BattleSystemのプレイヤーHPがダンジョンと同じ（参照共有）");
        }

        [Test]
        public void ダッシュ接敵時_敵に初期ダメージが入る()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            Enemy contactedEnemy = null;
            int enemyHpBeforeBattle = 0;

            dungeon.OnBattleStart += (contactInfo) =>
            {
                contactedEnemy = contactInfo.Enemy;
            };

            // 敵のHPを記録するために、まず通常接敵を試す
            // テストダンジョンの敵は5マス目にいるはず
            var testEnemy = config.MapCells[5].Enemy;
            if (testEnemy != null)
            {
                enemyHpBeforeBattle = testEnemy.CurrentHp;
            }

            // Act: ダッシュしながら敵に接近
            dungeon.StartDash();
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert
            Assert.IsNotNull(contactedEnemy, "敵と接敵した");
            if (enemyHpBeforeBattle > 0)
            {
                Assert.Less(contactedEnemy.CurrentHp, enemyHpBeforeBattle,
                    "ダッシュ接敵で敵にダメージが入っている");
            }
        }

        [Test]
        public void 通常接敵時_プレイヤーは左中央敵は右中央に配置される()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            ContactInfo contactInfo = null;
            dungeon.OnBattleStart += (info) => contactInfo = info;

            // Act: 通常接敵（ダッシュなし）
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert: バトルグリッド上の位置は常に固定（左中央=1、右中央=4）
            Assert.IsNotNull(contactInfo, "接敵した");
            Assert.IsFalse(contactInfo.IsDashContact, "通常接敵");
            Assert.AreEqual(1, player.GridPosition.X, "プレイヤーは左側中央（グリッド1）");
            Assert.AreEqual(4, contactInfo.Enemy.GridPosition.X, "敵は右側中央（グリッド4）");
        }

        [Test]
        public void ダッシュ接敵時_プレイヤーは左中央敵は右中央に配置される()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            ContactInfo contactInfo = null;
            dungeon.OnBattleStart += (info) => contactInfo = info;

            // Act: ダッシュ接敵
            dungeon.StartDash();
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert: バトルグリッド上の位置は常に固定（左中央=1、右中央=4）
            // ノックバック量はダンジョンマップ上の移動量で表現され、グリッド位置には影響しない
            Assert.IsNotNull(contactInfo, "接敵した");
            Assert.IsTrue(contactInfo.IsDashContact, "ダッシュ接敵");
            Assert.AreEqual(1, player.GridPosition.X, "プレイヤーは左側中央（グリッド1）");
            Assert.AreEqual(4, contactInfo.Enemy.GridPosition.X, "敵は右側中央（グリッド4）");
        }

        [Test]
        public void バトル勝利後_HPが回復しない()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態");

            // バトル中にダメージを受ける
            player.TakeDamage(30);
            int hpAfterDamage = player.CurrentHp;  // 70

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.AreEqual(hpAfterDamage, player.CurrentHp, "バトル後もHPは回復しない");
        }

        [Test]
        public void バトル終了後_CurrentBattleがnullになる()
        {
            // Arrange: index 3 に通常敵、index 9 にボス
            var dungeon = CreateDungeonSystemWithEarlyNormalEnemy();

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            Assert.IsNotNull(dungeon.CurrentBattle, "バトル中はCurrentBattleがある");

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // 報酬選択が完了するまでバトル参照を保持する
            Assert.IsNotNull(dungeon.CurrentBattle, "報酬選択中はバトル参照を保持する");
            dungeon.OnCardSelectionCompleted(null);

            // Assert
            Assert.IsNull(dungeon.CurrentBattle, "バトル終了後はCurrentBattleがnull");
        }

        [Test]
        public void バトル終了時_OnBattleEndイベントが発火する()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);
            BattleResult? receivedResult = null;

            dungeon.OnBattleEnd += (result) => receivedResult = result;

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // Assert
            Assert.IsNotNull(receivedResult, "OnBattleEndが発火した");
            Assert.AreEqual(BattleResult.Victory, receivedResult, "勝利結果");
        }

        [Test]
        public void ダンジョン復帰時_OnReturnToDungeonイベントが発火する()
        {
            // Arrange: index 3 に通常敵、index 9 にボス（通常敵撃破後も復帰できる構成）
            var dungeon = CreateDungeonSystemWithEarlyNormalEnemy();
            bool returnEventFired = false;

            dungeon.OnReturnToDungeon += () => returnEventFired = true;

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            Assert.IsFalse(returnEventFired, "報酬選択中はまだ復帰しない");
            dungeon.OnCardSelectionCompleted(null);

            // Assert
            Assert.IsTrue(returnEventFired, "OnReturnToDungeonが発火した");
        }

        [Test]
        public void 全敵撃破後_ダンジョンクリアイベントが発火する()
        {
            // Arrange: 敵が1体だけのダンジョン
            var enemy = new Enemy("boss", "ボス", 100, ElementType.None, new Vector2Int(5, 0));
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),
                new MapCell(1),
                new MapCell(2),
                new MapCell(3),
                new MapCell(4),
                new MapCell(5, enemy),  // 唯一の敵
                new MapCell(6),
                new MapCell(7),
                new MapCell(8),
                new MapCell(9)
            };
            var config = new DungeonConfig("clear_test", "クリアテスト", 10, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            bool clearFired = false;
            dungeon.OnDungeonCleared += () => clearFired = true;

            // 接敵までTickを進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            Assert.IsTrue(dungeon.IsInBattle, "バトル状態になっている");

            // ボスはカードを100%ドロップするため、選択肢を提示された後にスキップ
            System.Collections.Generic.List<Card> rewardCards = null;
            dungeon.OnCardRewardOffered += (cards) => rewardCards = cards;

            // Act: バトル勝利
            dungeon.OnBattleCompleted(victory: true);

            // カード報酬をスキップ（選択なし）
            if (rewardCards != null)
            {
                dungeon.OnCardSelectionCompleted(null);
            }

            // Assert
            Assert.IsTrue(clearFired, "ダンジョンクリアイベントが発火した");
            Assert.IsTrue(dungeon.IsDungeonCleared, "IsDungeonClearedがtrueになった");
        }

        [Test]
        public void ダンジョンクリア後_Tickしても移動しない()
        {
            // Arrange: 敵が1体だけのダンジョン
            var enemy = new Enemy("enemy_1", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),
                new MapCell(1),
                new MapCell(2),
                new MapCell(3, enemy),
                new MapCell(4),
            };
            var config = new DungeonConfig("clear_test2", "クリアテスト2", 5, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            // 接敵
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // カードドロップなし
            mockRandom.SetFixedValue(0.7f);

            // バトル勝利
            dungeon.OnBattleCompleted(victory: true);
            Assert.IsFalse(dungeon.IsDungeonCleared, "報酬選択完了まではクリアを待つ");
            dungeon.OnCardSelectionCompleted(null);
            Assert.IsTrue(dungeon.IsDungeonCleared, "クリア済み");

            float posAfterClear = dungeon.PlayerPosition;

            // Act: クリア後にTick
            dungeon.Tick(deltaTimeMs: 1000);

            // Assert: 移動しない
            Assert.AreEqual(posAfterClear, dungeon.PlayerPosition, "クリア後は移動しない");
        }

        [Test]
        public void 画面内の複数敵がバトルに参加する()
        {
            // Arrange: 複数の敵を配置したダンジョン
            var enemy1 = new Enemy("goblin", "ゴブリン", 50, ElementType.None, new Vector2Int(5, 0));
            var enemy2 = new Enemy("slime", "スライム", 30, ElementType.None, new Vector2Int(7, 0));
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0),
                new MapCell(1),
                new MapCell(2),
                new MapCell(3),
                new MapCell(4),
                new MapCell(5, enemy1),  // 敵1
                new MapCell(6),
                new MapCell(7, enemy2),  // 敵2（画面内）
                new MapCell(8),
                new MapCell(9)
            };
            var config = new DungeonConfig(
                dungeonId: "multi_enemy",
                dungeonName: "複数敵ダンジョン",
                totalCellCount: 10,
                mapCells: cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            var dungeon = new DungeonSystem(config, player, deck, mockRandom, mockLogger);

            // Act: 敵1に接敵するまで進める
            for (int i = 0; i < 200; i++)
            {
                dungeon.Tick(deltaTimeMs: 100);
                if (dungeon.IsInBattle) break;
            }

            // Assert
            Assert.IsNotNull(dungeon.CurrentBattle, "バトル開始");
            Assert.GreaterOrEqual(dungeon.CurrentBattle.Enemies.Count, 2,
                "画面内の複数敵がバトルに参加している");
        }
    }
}
