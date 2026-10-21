using NUnit.Framework;
using System.Linq;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Tests.Mocks;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class DungeonSystemTests
    {
        // 獲得履歴・障害物の単体fixture。報酬選択の受付とは分離する。
        private static void GrantFixtureReward(DungeonSystem dungeon, Card card)
        {
            var rewards = (CardRewardSystem)typeof(DungeonSystem).GetField("cardRewardSystem",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(dungeon);
            rewards.SelectCard(card);
        }

        [Test]
        public void 移動表示の論理状態は未更新の描画フレームで失われず接敵で停止する()
        {
            var dungeon = CreateDungeonSystem();
            float position = dungeon.PlayerPosition;
            Assert.IsTrue(dungeon.IsExplorationMoving);
            for (int render = 0; render < 4; render++) {
                Assert.AreEqual(position, dungeon.PlayerPosition);
                Assert.IsTrue(dungeon.IsExplorationMoving, "描画のみのフレームでidleへ戻さない");
            }
            dungeon.StartDash(); Assert.IsTrue(dungeon.IsExplorationMoving);
            dungeon.StopDash(); Assert.IsTrue(dungeon.IsExplorationMoving, "ダッシュ解除後も自動歩行を続ける");
            for (int frame = 0; frame < 1000 && !dungeon.IsInBattle; frame++) dungeon.TickFrame();
            Assert.IsTrue(dungeon.IsInBattle); Assert.IsFalse(dungeon.IsExplorationMoving);
        }

        [Test]
        public void 移動表示は死亡時に止まる()
        {
            var dungeon = CreateDungeonSystem(); Assert.IsTrue(dungeon.IsExplorationMoving);
            dungeon.Player.TakeDamage(1000); Assert.IsFalse(dungeon.IsExplorationMoving);
        }

        [Test]
        public void プレイヤー毒は探索で自動進行し停止中は保存値も変わらず接敵と勝利で間隔だけ戻る()
        {
            var d=CreateDungeonSystem();var p=d.Player;
            p.ApplyPoison("fixture",new StatusEffect(StatusEffectType.Poison,60000,2),60);
            d.TickFrame();var a=p.CapturePoisons()[0];Assert.AreEqual(179950,a.RemainingUnits);Assert.AreEqual(2950,a.NextDamageUnits);
            var movement=(Olca.Core.Logic.Dungeon.PlayerMovement)typeof(DungeonSystem).GetField("playerMovement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(d);
            movement.Pause();for(int i=0;i<20;i++)d.TickFrame();Assert.AreEqual(a.RemainingUnits,p.CapturePoisons()[0].RemainingUnits);
            movement.Resume();int frames=1;
            while(!d.IsInBattle&&frames<2000){d.TickFrame();frames++;}
            Assert.IsTrue(d.IsInBattle);var start=p.CapturePoisons()[0];
            Assert.AreEqual(180000-frames*50,start.RemainingUnits);Assert.AreEqual(3000,start.NextDamageUnits);
            d.CurrentBattle.TickFrame(16);var before=p.CapturePoisons()[0];
            foreach(var e in d.CurrentBattle.Enemies.ToArray())e.TakeDamage(99999,ElementType.None);
            Assert.IsFalse(d.IsInBattle);var end=p.CapturePoisons()[0];
            Assert.AreEqual(before.RemainingUnits,end.RemainingUnits);Assert.AreEqual(3000,end.NextDamageUnits);
        }

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

            // Act: 接触範囲の全区間を回避高度で通過する（下降中の接触は別テスト）。
            dungeon.Tick(deltaTimeMs: 200);
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

            Card selectedCard = null;
            dungeon.OnCardRewardOffered += cards => selectedCard = cards[0];
            dungeon.OnBattleCompleted(true);
            Assert.IsNotNull(selectedCard);

            // Act: 実際に提示された候補を確定
            dungeon.OnCardSelectionCompleted(selectedCard);

            // Assert
            Assert.AreEqual(initialDeckSize + 1, dungeon.GetCurrentDeck().TotalCount,
                "デッキにカードが追加された");
        }

        [Test]
        public void GetObtainedCards_初期デッキは獲得履歴に含めない()
        {
            var dungeon = CreateDungeonSystem();

            Assert.AreEqual(2, dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(0, dungeon.GetObtainedCards().Count);
        }

        [Test]
        public void GetObtainedCards_報酬取得で増加しスキップでは変化しない()
        {
            var dungeon = CreateDungeonSystem();
            var reward = CreateTestDeck().GetAllCards()[0];

            dungeon.OnCardSelectionCompleted(null);
            Assert.AreEqual(0, dungeon.GetObtainedCards().Count);
            GrantFixtureReward(dungeon, reward);
            GrantFixtureReward(dungeon, reward);
            dungeon.OnCardSelectionCompleted(null);

            Assert.AreEqual(2, dungeon.GetObtainedCards().Count, "同種カードも取得枚数として数える");
            Assert.AreEqual(4, dungeon.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void GetObtainedCards_新規ダンジョンには獲得履歴を持ち越さない()
        {
            var previous = CreateDungeonSystem();
            GrantFixtureReward(previous, CreateTestDeck().GetAllCards()[0]);

            var next = CreateDungeonSystem();

            Assert.AreEqual(1, previous.GetObtainedCards().Count);
            Assert.AreEqual(0, next.GetObtainedCards().Count);
            Assert.AreEqual(2, next.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void GetObtainedCards_入手したカード一覧を取得できる()
        {
            // Arrange
            var dungeon = CreateDungeonSystem(cellCount: 10);

            var card1 = new Card("fire", "ファイア", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 40, "炎属性攻撃");

            // Act
            GrantFixtureReward(dungeon, card1);

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

        private DungeonSystem CreateCardDiscardDungeon(Deck initialDeck, int discardCount)
        {
            var cells = new System.Collections.Generic.List<MapCell>
            {
                new MapCell(0), new MapCell(1),
                new MapCell(2, Obstacle.CreateCardDiscardObstacle("trap", "カード破棄罠", discardCount)),
                new MapCell(3), new MapCell(4),
                new MapCell(5, new Enemy("slime", "スライム", 100, ElementType.None, new Vector2Int(5, 0)))
            };
            var config = new DungeonConfig("discard_test", "カード破棄テスト", 6, cells);
            return new DungeonSystem(config, new PlayerCharacter(maxHp: 100), initialDeck, mockRandom, mockLogger);
        }

        [TestCase(0, 2)]
        [TestCase(1, 1)]
        [TestCase(5, 0)]
        public void Tick_カード破棄障害物_要求枚数を残数まで適用する(int requested, int expected)
        {
            var initialDeck = CreateTestDeck();
            var dungeon = CreateCardDiscardDungeon(initialDeck, requested);

            dungeon.Tick(deltaTimeMs: 200);

            Assert.AreEqual(2f, dungeon.PlayerPosition);
            Assert.AreEqual(expected, dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(expected, dungeon.GetCurrentDeck().RemainingCount);
            Assert.AreEqual(2, initialDeck.TotalCount, "初期デッキは変更しない");
            Assert.AreEqual(100, dungeon.Player.CurrentHp, "カード破棄ではHPを変更しない");
        }

        [Test]
        public void Tick_カード破棄障害物_空デッキでも処理できる()
        {
            var dungeon = CreateCardDiscardDungeon(new Deck(), 5);
            int penalties = 0;
            dungeon.OnObstaclePenalty += _ => penalties++;

            dungeon.Tick(deltaTimeMs: 200);

            Assert.AreEqual(1, penalties, "空デッキでも障害物接触を処理する");
            Assert.AreEqual(0, dungeon.GetCurrentDeck().TotalCount);
            Assert.AreEqual(0, dungeon.GetCurrentDeck().RemainingCount);
        }

        [Test]
        public void Tick_固定乱数でカード破棄_初期デッキを保全し次戦の手札に反映する()
        {
            var initialDeck = CreateTestDeck();
            var survivor = initialDeck.GetAllCards()[0];
            var discarded = initialDeck.GetAllCards()[1];
            mockRandom.SetFixedValue(1);
            var dungeon = CreateCardDiscardDungeon(initialDeck, 1);

            dungeon.Tick(deltaTimeMs: 200);

            CollectionAssert.AreEqual(new[] { survivor }, dungeon.GetCurrentDeck().GetAllCards());
            CollectionAssert.AreEqual(new[] { survivor, discarded }, initialDeck.GetAllCards());
            for (int i = 0; i < 10 && !dungeon.IsInBattle; i++)
                dungeon.Tick(deltaTimeMs: 100);
            Assert.IsTrue(dungeon.IsInBattle);
            dungeon.CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            dungeon.CurrentBattle.OpenCustomScreen(mockRandom);
            CollectionAssert.AreEqual(new[] { survivor }, dungeon.CurrentBattle.CustomScreen.CurrentHand);
        }

        [Test]
        public void Tick_カード破棄障害物_ドロー済みカードも除去して履歴を整合させる()
        {
            var dungeon = CreateCardDiscardDungeon(CreateTestDeck(), 1);
            var deck = dungeon.GetCurrentDeck();
            var drawn = deck.DrawCards(1, mockRandom)[0];
            var survivor = deck.GetAllCards()[1];

            dungeon.Tick(deltaTimeMs: 200);

            CollectionAssert.DoesNotContain(deck.GetAllCards(), drawn);
            Assert.AreEqual(1, deck.RemainingCount);
            CollectionAssert.AreEqual(new[] { survivor }, deck.DrawCards(5, mockRandom));
            deck.ResetDrawHistory();
            CollectionAssert.AreEqual(new[] { survivor }, deck.DrawCards(5, mockRandom));
        }

        [Test]
        public void Tick_カード破棄障害物_同じセルで再処理しない()
        {
            var dungeon = CreateCardDiscardDungeon(CreateTestDeck(), 1);
            int penalties = 0;
            dungeon.OnObstaclePenalty += _ => penalties++;

            dungeon.Tick(deltaTimeMs: 200);
            dungeon.Tick(deltaTimeMs: 0);
            dungeon.Tick(deltaTimeMs: 10);

            Assert.AreEqual(1, penalties);
            Assert.AreEqual(1, dungeon.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void Tick_カード破棄障害物をジャンプ回避_着地後も破棄しない()
        {
            var dungeon = CreateCardDiscardDungeon(CreateTestDeck(), 1);
            int penalties = 0;
            dungeon.OnObstaclePenalty += _ => penalties++;
            dungeon.Tick(deltaTimeMs: 100);
            dungeon.Jump();

            dungeon.Tick(deltaTimeMs: 100);
            Assert.Greater(dungeon.PlayerHeight, 0f);
            for (int i = 0; i < 5; i++) dungeon.Tick(deltaTimeMs: 100);

            Assert.AreEqual(0, penalties);
            Assert.AreEqual(2, dungeon.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void Tick_獲得カードを破棄_クリア報酬用の獲得履歴に残る()
        {
            var initialDeck = CreateTestDeck();
            var dungeon = CreateCardDiscardDungeon(initialDeck, 1);
            var reward = new Card("reward", "獲得カード", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 10, "報酬");
            GrantFixtureReward(dungeon, reward);
            dungeon.ResumePlayerMovement();
            mockRandom.SetFixedValue(2);

            dungeon.Tick(deltaTimeMs: 200);

            CollectionAssert.AreEqual(initialDeck.GetAllCards(), dungeon.GetCurrentDeck().GetAllCards());
            CollectionAssert.AreEqual(new[] { reward }, dungeon.GetObtainedCards());
            Assert.AreEqual(2, initialDeck.TotalCount);
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

            // 無報酬のため選択待ちを挟まない。

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
            Assert.IsTrue(dungeon.IsDungeonCleared, "最後の敵が無報酬なら選択待ちなしでクリアする");

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

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class CheckpointCoreTests
    {
        private Olca.Core.Logic.Dungeon.CheckpointDefinitions definitions;
        private DungeonConfig map;
        private Card standard, fallback;
        private CheckpointDto departure;
        private sealed class CountingRandom : Olca.Core.Interfaces.IRandomProvider
        {
            public int Calls;
            public int Range(int min, int max) { Calls++; return min; }
            public float Range(float min, float max) { Calls++; return min; }
            public float Value { get { Calls++; return 0; } }
        }
        [SetUp]
        public void SetUp()
        {
            map = DungeonConfig.CreateTestDungeon(10, 30, 3);
            definitions = new CheckpointDefinitions("fixture-v1", map);
            standard = new Card("sword", "同名", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "通常");
            fallback = new Card("sword", "同名", 2, CardRarity.Normal, CardCategory.Attack, ElementType.Fire, CardSeries.Sword, 45, "別定義");
            definitions.RegisterCard("standard/v1/sword", standard);
            definitions.RegisterCard("fallback/v1/sword", fallback);
            // 現行フォールバック抽選が実際に生成する値を登録。製品プールは作らない。
            foreach (string id in new[] { "sword", "heal", "shield", "fire", "ice" })
                definitions.RegisterCard("reward/v1/" + id, new Card(id, id + "_card", 1, CardRarity.Normal,
                    CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "Fallback card"));
            departure = new CheckpointDto {
                DefinitionsRevision = definitions.Revision, DungeonId = definitions.DungeonId, MapHash = definitions.MapHash,
                RunId = "run", Sequence = 1, Boundary = CheckpointBoundary.Departure,
                Player = new CheckpointPlayerDto { Hp = 93, MaxHp = 100, AttackPower = 10, Poise = 30, CustomSpeed = 1, GridX = 0, GridY = 0 },
                Cards = new[] { definitions.CaptureCard("a", "standard/v1/sword", standard), definitions.CaptureCard("b", "standard/v1/sword", standard.Clone()),
                    definitions.CaptureCard("c", "fallback/v1/sword", fallback), definitions.CaptureCard("discarded", "fallback/v1/sword", fallback) },
                DeckOrder = new[] { "c", "b", "a" }, ObtainedOrder = new[] { "discarded", "c" },
                Enemies = new[] { new CheckpointEnemyDto { CellIndex = 3, EnemyId = "enemy_3", Hp = 50, GridX = 4 },
                    new CheckpointEnemyDto { CellIndex = 9, EnemyId = "boss", Hp = 200, GridX = 5 } }
            };
        }
        [Test]
        public void プレイヤー毒は旧保存互換を保ちファイル復帰へ正確に持ち越す()
        {
            Assert.IsTrue(CheckpointCodec.TryRestore(departure,definitions,out var legacy,out var error),error);
            Assert.IsEmpty(legacy.Player.CapturePoisons());
            departure.Player.Poisons=new[]{new PlayerPoisonDto{Kind="fixture",Damage=7,IntervalFrames=6,RemainingUnits=2997,NextDamageUnits=197}};
            var directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-player-poison-"+System.Guid.NewGuid().ToString("N"));
            try {
                var store=new Olca.Unity.FileCheckpointStore(definitions,directory);
                Assert.IsTrue(store.TryWrite(departure,out error),error);
                var data=new Olca.Unity.FileCheckpointStore(definitions,directory).Read();
                Assert.IsTrue(CheckpointCodec.TryRestore(data,definitions,out var restored,out error),error);
                Assert.AreEqual(2997,restored.Player.CapturePoisons()[0].RemainingUnits);
                Assert.AreEqual(197,restored.Player.CapturePoisons()[0].NextDamageUnits);
                var copy=data.Copy();copy.Player.Poisons[0].RemainingUnits=1;Assert.AreEqual(2997,data.Player.Poisons[0].RemainingUnits);
                var bytes=System.IO.File.ReadAllBytes(store.ActivePath);data.Player.Poisons[0].NextDamageUnits=301;
                Assert.IsFalse(store.TryWrite(data,out error));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
                data.Player.Poisons=new[]{departure.Player.Poisons[0],departure.Player.Poisons[0]};
                Assert.IsFalse(CheckpointCodec.TryRestore(data,definitions,out _,out error));
            } finally {if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }

        [TestCase(0)] [TestCase(50)] [TestCase(150)]
        public void 敵の時間耐性は通常定義とファイル保存復帰へ伝わり異なる定義を拒否する(int resistance)
        {
            var configured = DungeonConfig.CreateTestDungeon(10, 30, 3);
            for (int i = 0; i < configured.MapCells.Count; i++) {
                var cell = configured.MapCells[i]; var e = cell.Enemy; if (e == null) continue;
                configured.MapCells[i] = new MapCell(i, new Enemy(e.EnemyId, e.Name, e.MaxHp, e.Element, e.GridPosition,
                    statusResistances: new TimedStatusResistances(poison: resistance, paralyze: 50)), cell.Height);
            }
            var configuredDefinitions = new CheckpointDefinitions("fixture-v1", configured);
            configuredDefinitions.RegisterCard("standard/v1/sword", standard);
            configuredDefinitions.RegisterCard("fallback/v1/sword", fallback);
            var dto = departure.Copy(); dto.MapHash = configuredDefinitions.MapHash;
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-status-map-" + System.Guid.NewGuid().ToString("N"));
            try {
                var store = new Olca.Unity.FileCheckpointStore(configuredDefinitions, directory);
                Assert.IsTrue(store.TryWrite(dto, out var error), error);
                var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
                var read = new Olca.Unity.FileCheckpointStore(configuredDefinitions, directory);
                Assert.IsTrue(CheckpointCodec.TryRestore(read.Read(), configuredDefinitions, out var restored, out error), error);
                var enemy = restored.Enemies[3];
                Assert.AreEqual(resistance, enemy.StatusResistances.Poison); Assert.AreEqual(50, enemy.StatusResistances.Paralyze);
                enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
                Assert.AreEqual(30, enemy.ActiveStatus.RemainingFrames);
                Assert.IsFalse(CheckpointCodec.TryRestore(read.Read(), definitions, out _, out _));
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
                var route = new FloorSequenceDefinitions("status-fixture", "v1", new[] { configured }, new Deck());
                Assert.AreEqual(resistance, route.CreateInitialMap(0).MapCells[3].Enemy.StatusResistances.Poison);
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [Test]
        public void 時間耐性未指定の旧階層マップハッシュを変更しない()
        {
            // a5caaed の実行時保存から取得した2階層の旧hash。
            Assert.AreEqual("6F62727233C3B0E3001BD1A7C522B3431389AF797E7CCEE76717732B6B79E09F",
                new CheckpointDefinitions("fixed-runtime-v1", DungeonConfig.CreateTestDungeon(6, 30, 100)).MapHash);
            Assert.AreEqual("7292E95285520F6E55A2856F410B9FB607BB2E24290EA1DBAE118726AB555682",
                new CheckpointDefinitions("fixed-runtime-v1", DungeonConfig.CreateTestDungeon(20, 7, 5)).MapHash);
        }

        [TestCase(AttackProperty.None)]
        [TestCase(AttackProperty.Sword)]
        [TestCase(AttackProperty.Sword | AttackProperty.Break)]
        public void 攻撃性質の保存復帰は個体順序と獲得参照を維持し別性質定義を拒否する(AttackProperty properties)
        {
            Card Make(AttackProperty value) => new Card("sword", "同名", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.Sword, 45, "別定義", properties: value);
            var card = Make(properties);
            definitions.RegisterCard("property/v1", card);
            departure.Cards[2] = definitions.CaptureCard("c", "property/v1", card);
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-properties-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new Olca.Unity.FileCheckpointStore(definitions, directory);
                Assert.IsTrue(store.TryWrite(departure, out var error), error);
                var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
                var readStore = new Olca.Unity.FileCheckpointStore(definitions, directory);
                var roundtrip = readStore.Read(); Assert.IsNotNull(roundtrip, readStore.ReadError);
                Assert.IsTrue(CheckpointCodec.TryRestore(roundtrip, definitions, out var restored, out error), error);
                Assert.AreEqual(properties, restored.Deck.GetAllCards()[0].Properties);
                Assert.AreEqual(ElementType.Fire, restored.Deck.GetAllCards()[0].Element);
                Assert.AreSame(restored.Cards["c"], restored.Obtained[1]);
                Assert.AreSame(restored.Cards["c"], restored.Deck.GetAllCards()[0]);
                CollectionAssert.AreEqual(departure.DeckOrder, roundtrip.DeckOrder);
                CollectionAssert.AreEqual(departure.ObtainedOrder, roundtrip.ObtainedOrder);
                Assert.AreEqual(93, restored.Player.CurrentHp);
                var changed = new CheckpointDefinitions("fixture-v1", map);
                changed.RegisterCard("standard/v1/sword", standard);
                changed.RegisterCard("fallback/v1/sword", fallback);
                changed.RegisterCard("property/v1", Make(properties == AttackProperty.None ? AttackProperty.Break : AttackProperty.None));
                Assert.IsFalse(CheckpointCodec.TryRestore(roundtrip, changed, out _, out _));
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath), "復帰や定義不一致でも保存原本を書き換えない");
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [Test]
        public void 性質未指定のソードは導入前保存の定義ハッシュと一致する()
        {
            // a5caaed の実行時保存から取得した旧値。旧カタログを明示して互換性を検証する。
            var sword = new Olca.Application.Battle.SimpleCardFactory(legacyPoise: true).CreateById("sword");
            Assert.IsNull(sword.PoiseDamage);
            definitions.RegisterCard("legacy-sword", sword);
            Assert.AreEqual(AttackProperty.None, sword.Properties);
            Assert.AreEqual("6FF05396427C959B85CF868B3F3B186894D17E8DD4CBF04214C0F6A85B236A72",
                definitions.CaptureKnownCard("legacy", sword).ValueHash);
        }

        [Test]
        public void 属性耐性の保存復帰と上限検証()
        {
            departure.Player.FireResistance=-50;departure.Player.WaterResistance=100;
            departure.Player.WindResistance=50;departure.Player.EarthResistance=-200;
            var store=new MemoryCheckpointStore(definitions);Assert.IsTrue(store.TryWrite(departure,out var error),error);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(),definitions,out var restored,out error),error);
            Assert.IsTrue(restored.Player.Resistances.HasSameValues(new ElementResistances(-50,100,50,-200)));
            Assert.AreEqual(93,restored.Player.CurrentHp);
            departure.Player.FireResistance=101;
            Assert.IsFalse(CheckpointCodec.TryRestore(departure,definitions,out _,out _));
        }

        [Test]
        public void 重み付き戦闘開始計画は保存後の重み変更でも候補を再抽選しない()
        {
            bool saved = false;
            var cards = new[] { "sword", "heal", "shield", "fire", "ice" }
                .Select(id => CardRewardSystem.CreateFallbackCard(id, CardRarity.Normal)).ToList();
            var pool = new Olca.Tests.Mocks.SimpleCardPool(cards, c => {
                Assert.IsFalse(saved, "保存復帰では重みを参照しない");
                return c.Id == "heal" ? 10 : c.Id == "fire" ? 0 : 1;
            });
            CheckpointBattlePlan plan = null;
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, seed, pool,
                    c => "reward/v1/" + c.Id, out plan, out var error), error);
                if (plan.Snapshot.Battle.RewardOffered) break;
            }
            Assert.IsTrue(plan.Snapshot.Battle.RewardOffered);
            var snapshot = plan.Snapshot;
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-weighted-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new Olca.Unity.FileCheckpointStore(definitions, directory);
                Assert.IsTrue(store.TryWrite(snapshot, out var error), error); saved = true;
                var bytes = System.IO.File.ReadAllBytes(store.ActivePath);
                var reopened = new Olca.Unity.FileCheckpointStore(definitions, directory);
                Assert.IsTrue(CheckpointCodec.TryRestore(reopened.Read(), definitions, out var restored, out error), error);
                Assert.AreEqual(3, restored.RewardCandidates.Count);
                Assert.IsFalse(restored.RewardCandidates.Any(c => c.Id == "fire"));
                CollectionAssert.AreEqual(snapshot.Battle.RewardCandidates, restored.Snapshot.Battle.RewardCandidates);
                CollectionAssert.AreEqual(bytes, System.IO.File.ReadAllBytes(store.ActivePath));
            }
            finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 部分一致IDの新規通常報酬と旧保存一候補を再抽選せず復帰する(bool legacy)
        {
            map.MapCells[3] = new MapCell(3, new Enemy("miniboss", "通常敵", 50, ElementType.None, new Vector2Int(3, 0)));
            var updated = new CheckpointDefinitions("fixture-v1", map);
            updated.RegisterCard("standard/v1/sword", standard);
            updated.RegisterCard("fallback/v1/sword", fallback);
            foreach (string id in new[] { "sword", "heal", "shield", "fire", "ice" })
                updated.RegisterCard("reward/v1/" + id, CardRewardSystem.CreateFallbackCard(id, CardRarity.Normal));
            var oldReward = CardRewardSystem.CreateFallbackCard("sword", CardRarity.Limited);
            updated.RegisterCard("legacy/boss/sword", oldReward);
            departure.MapHash = updated.MapHash; departure.Enemies[0].EnemyId = "miniboss";
            definitions = updated;
            var snapshot = FindPlan(legacy).Snapshot;
            if (legacy)
            {
                var keep = snapshot.Battle.RewardCandidates[0];
                var remove = snapshot.Battle.RewardCandidates.Skip(1).ToArray();
                snapshot.Battle.RewardCandidates = new[] { keep };
                snapshot.Cards = snapshot.Cards.Where(c => !remove.Contains(c.InstanceId))
                    .Select(c => c.InstanceId == keep ? definitions.CaptureCard(keep, "legacy/boss/sword", oldReward) : c).ToArray();
            }
            var store = new MemoryCheckpointStore(definitions);
            Assert.IsTrue(store.TryWrite(snapshot, out var error), error);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), definitions, out var restored, out error), error);
            CollectionAssert.AreEqual(snapshot.Battle.RewardCandidates, restored.Snapshot.Battle.RewardCandidates);
            Assert.AreEqual(legacy ? 1 : 0, restored.RewardCandidates.Count);
            if (legacy) Assert.AreEqual(CardRarity.Limited, restored.RewardCandidates[0].Rarity);
        }

        private CheckpointBattleDto Encounter() => new CheckpointBattleDto {
            ContactCell = 3, ParticipantCells = new[] { 3 }, HandLimit = 2, GridOriginX = -1,
            DashContact = true, DashEffect = (int)ActionSystem.DashContactEffect.InitialDamage };

        private CheckpointBattlePlan FindPlan(bool offered)
        {
            for (int seed = 0; seed < 100; seed++)
            {
                Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, seed, null,
                    c => "reward/v1/" + c.Id, out var plan, out var error), error);
                if (plan.Snapshot.Battle.RewardOffered == offered) return plan;
            }
            Assert.Fail("fixture内で必要な抽選結果が得られませんでした"); return null;
        }

        [TestCase(CheckpointBoundary.Departure)]
        [TestCase(CheckpointBoundary.RewardCompleted)]
        public void 境界の往復で個体と定義と順序HP敵配置を維持する(CheckpointBoundary boundary)
        {
            departure.Boundary = boundary;
            if (boundary == CheckpointBoundary.RewardCompleted) departure.Enemies[0].Hp = 0;
            var store = new MemoryCheckpointStore(definitions);
            Assert.IsTrue(store.TryWrite(departure, out var error), error);
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), definitions, out var restored, out error), error);
            Assert.AreEqual(93, restored.Player.CurrentHp);
            Assert.AreEqual(100, restored.Player.MaxHp);
            Assert.AreEqual(45, restored.Deck.GetAllCards()[0].Power);
            Assert.AreEqual(ElementType.Fire, restored.Deck.GetAllCards()[0].Element);
            Assert.AreNotSame(restored.Cards["a"], restored.Cards["b"]);
            Assert.AreSame(restored.Cards["c"], restored.Obtained[1]);
            Assert.IsFalse(restored.Deck.GetAllCards().Contains(restored.Cards["discarded"]));
            Assert.AreEqual(departure.Enemies[0].Hp, restored.Enemies[3].CurrentHp);
            Assert.AreEqual(10, restored.Map.TotalCellCount);
            if (boundary == CheckpointBoundary.RewardCompleted) Assert.IsNull(restored.Map.MapCells[3].Enemy);
            else Assert.AreSame(restored.Enemies[3], restored.Map.MapCells[3].Enemy);
            Assert.AreEqual(new Vector2Int(4, 0), restored.Enemies[3].GridPosition);
            CollectionAssert.AreEqual(departure.DeckOrder, restored.Snapshot.DeckOrder);
            CollectionAssert.AreEqual(departure.ObtainedOrder, restored.Snapshot.ObtainedOrder);
            // 保存前・読出後・復元後の編集はいずれも保存先に漏れない。
            departure.Player.Hp = 1; store.Read().Cards[0].GrantKeys = null;
            restored.Cards["a"].Modifier.BonusDamage = 999;
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), definitions, out var again, out error), error);
            Assert.AreEqual(93, again.Player.CurrentHp); Assert.AreEqual(30, again.Cards["a"].GetFinalPower());
        }

        [Test]
        public void 開始計画の抽選は一回で保存再試行と復元では乱数を使わない()
        {
            var plan = FindPlan(true);
            string error;
            int calls = plan.RandomCalls; Assert.Greater(calls, 0);
            var store = new MemoryCheckpointStore(definitions);
            Assert.IsTrue(store.TryWrite(plan.Snapshot, out error), error);
            Assert.IsFalse(store.TryWrite(plan.Snapshot, out error)); // 同一世代の二重確定
            Assert.IsTrue(CheckpointCodec.TryRestore(store.Read(), definitions, out var restored, out error), error);
            Assert.AreEqual(2, restored.FirstHand.Count); Assert.AreEqual(1, restored.Deck.RemainingCount);
            Assert.AreEqual(3, restored.RewardCandidates.Count);
            Assert.AreNotSame(restored.RewardCandidates[0], restored.RewardCandidates[1]);
            var remaining = restored.Deck.DrawCards(3, new MockRandomProvider());
            Assert.IsFalse(remaining.Exists(c => restored.FirstHand.Contains(c)));
            Assert.AreEqual(calls, plan.RandomCalls);
            Assert.IsTrue(restored.Snapshot.Battle.DashContact);
            Assert.AreEqual(-1, restored.Snapshot.Battle.GridOriginX);
            Assert.AreEqual(CheckpointBoundary.Departure, departure.Boundary);
            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, departure.DeckOrder);
            var edited = plan.Snapshot; edited.Battle.FirstHand[0] = "bad";
            Assert.AreNotEqual("bad", plan.Snapshot.Battle.FirstHand[0]);
        }

        [Test]
        public void 報酬なしも確定結果として保存する()
        {
            var plan = FindPlan(false);
            string error;
            Assert.IsFalse(plan.Snapshot.Battle.RewardOffered);
            Assert.IsEmpty(plan.Snapshot.Battle.RewardCandidates);
            Assert.IsTrue(CheckpointCodec.TryRestore(plan.Snapshot, definitions, out _, out error), error);
        }

        [TestCase("schema")][TestCase("revision")][TestCase("map")][TestCase("definition")]
        [TestCase("hash")][TestCase("history")][TestCase("duplicate")][TestCase("missing")]
        [TestCase("hp")][TestCase("enemy")][TestCase("boundary")][TestCase("null")]
        public void 不正データの保存と復元は既存世代と入力を変更しない(string fault)
        {
            var store = new MemoryCheckpointStore(definitions); Assert.IsTrue(store.TryWrite(departure, out _));
            var bad = departure.Copy(); bad.Sequence = 2;
            switch (fault) {
                case "schema": bad.SchemaVersion++; break;
                case "revision": bad.DefinitionsRevision = "old"; break;
                case "map": bad.MapHash = "old"; break;
                case "definition": bad.Cards[0].DefinitionKey = "unknown"; break;
                case "hash": bad.Cards[0].ValueHash = "wrong"; break;
                case "history": bad.Cards[0].TuningCount = 1; break;
                case "duplicate": bad.Cards[1].InstanceId = "a"; break;
                case "missing": bad.DeckOrder[0] = "unknown"; break;
                case "hp": bad.Player.Hp = 101; break;
                case "enemy": bad.Enemies = new[] { bad.Enemies[0] }; break;
                case "boundary": bad.Boundary = CheckpointBoundary.BattleStart; break;
                case "null": bad.Cards = null; break;
            }
            Assert.IsFalse(store.TryWrite(bad, out _));
            Assert.IsFalse(CheckpointCodec.TryRestore(bad, definitions, out var restored, out _));
            Assert.IsNull(restored); Assert.AreEqual(1, store.Read().Sequence); Assert.AreEqual(2, bad.Sequence);
            Assert.AreEqual(93, store.Read().Player.Hp);
        }

        [Test]
        public void 調律履歴を往復し旧履歴と定義改変を推測で補わない()
        {
            var grant = new TuningGrantDefinition("bonus", new[] { new TuningEffectDefinition("bonus", 1,
                new System.Collections.Generic.Dictionary<int, CardModifier> { { 1, new CardModifier { BonusDamage = 3 } } }) });
            definitions.RegisterGrant("grant/v1/bonus", grant);
            var tuned = TuningEvaluator.Evaluate(standard, grant).ResultCard;
            departure.Cards[0] = definitions.CaptureCard("a", "standard/v1/sword", tuned, "grant/v1/bonus");
            Assert.IsTrue(CheckpointCodec.TryRestore(departure, definitions, out var restored, out var error), error);
            Assert.AreEqual(33, restored.Cards["a"].GetFinalPower()); Assert.AreEqual(1, restored.Cards["a"].TuningHistory.Count);
            Assert.Throws<System.ArgumentException>(() => definitions.CaptureCard("legacy", "standard/v1/sword", standard.ApplyTuning(fallback)));
            var changed = new CheckpointDefinitions("fixture-v1", map);
            changed.RegisterCard("standard/v1/sword", fallback);
            Assert.IsFalse(CheckpointCodec.TryRestore(departure, changed, out _, out _));
        }

        [TestCase("hand")][TestCase("reward")][TestCase("participant")][TestCase("grid")][TestCase("dash")]
        public void 戦闘計画の不整合は復元も上書きも拒否する(string fault)
        {
            var plan = FindPlan(true);
            var store = new MemoryCheckpointStore(definitions); Assert.IsTrue(store.TryWrite(plan.Snapshot, out _));
            var bad = plan.Snapshot; bad.Sequence++;
            switch (fault) {
                case "hand": bad.Battle.FirstHand[0] = bad.Battle.FirstHand[1]; break;
                case "reward": bad.Battle.RewardOffered = false; break;
                case "participant": bad.Enemies[0].Hp = 0; break;
                case "grid": bad.Enemies[0].GridX = 6; break;
                case "dash": bad.Battle.DashEffect = 99; break;
            }
            Assert.IsFalse(store.TryWrite(bad, out _)); Assert.AreEqual(plan.Snapshot.Sequence, store.Read().Sequence);
        }

        [Test]
        public void 空デッキとボス確定報酬も復元できる()
        {
            departure.DeckOrder = new string[0]; departure.ObtainedOrder = new string[0]; departure.Cards = new CheckpointCardDto[0];
            foreach (string id in new[] { "sword", "heal", "shield", "fire", "ice" })
                definitions.RegisterCard("boss/v1/" + id, new Card(id, id + "_card", 1, CardRarity.Limited,
                    CardCategory.Attack, ElementType.None, CardSeries.Sword, 30, "Fallback card"));
            var encounter = Encounter(); encounter.ContactCell = 9; encounter.ParticipantCells = new[] { 9 };
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure, encounter, definitions, 0, null,
                c => "boss/v1/" + c.Id, out var plan, out var error), error);
            Assert.IsTrue(CheckpointCodec.TryRestore(plan.Snapshot, definitions, out var restored, out error), error);
            Assert.IsEmpty(restored.FirstHand); Assert.AreEqual(0, restored.Deck.TotalCount);
            Assert.IsTrue(restored.Snapshot.Battle.RewardOffered); Assert.AreEqual(1, restored.RewardCandidates.Count);
        }

        [Test]
        public void 計画生成後の定義不足は入力と既存保存を変更しない()
        {
            var store = new MemoryCheckpointStore(definitions); Assert.IsTrue(store.TryWrite(departure, out _));
            var encounter = Encounter(); encounter.ContactCell = 9; encounter.ParticipantCells = new[] { 9 };
            Assert.IsFalse(CheckpointBattlePlan.TryCreate(departure, encounter, definitions, 0, null,
                c => "unknown", out var plan, out _));
            Assert.IsNull(plan); Assert.AreEqual(1, store.Read().Sequence); Assert.AreEqual(1, departure.Sequence);
            CollectionAssert.AreEqual(new[] { "c", "b", "a" }, departure.DeckOrder);
        }

        [Test]
        public void 同じ計画Seedで別候補を作っても結果が一致し失敗試行は乱数を共有しない()
        {
            CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, 3, null,
                c => "reward/v1/" + c.Id, out var first, out var error);
            Assert.NotNull(first, error);
            CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, 3, null, c => "unknown", out _, out _);
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(departure, Encounter(), definitions, 3, null,
                c => "reward/v1/" + c.Id, out var second, out error), error);
            CollectionAssert.AreEqual(first.Snapshot.DeckOrder, second.Snapshot.DeckOrder);
            CollectionAssert.AreEqual(first.Snapshot.Battle.FirstHand, second.Snapshot.Battle.FirstHand);
            CollectionAssert.AreEqual(first.Snapshot.Cards.Select(c => c.ValueHash), second.Snapshot.Cards.Select(c => c.ValueHash));
            Assert.AreEqual(first.RandomCalls, second.RandomCalls);
        }

        [Test]
        public void 不正接敵は抽選前に失敗し旧保存を維持する()
        {
            var encounter = Encounter(); encounter.ParticipantCells = new[] { 9 };
            Assert.IsFalse(CheckpointBattlePlan.TryCreate(departure, encounter, definitions, 0, null,
                c => "reward/v1/" + c.Id, out var plan, out _));
            Assert.IsNull(plan); Assert.AreEqual(1, departure.Sequence);
        }
    }
}

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class SweptDungeonContactTests
    {
        private static MapCell EnemyCell(int index, float height = 0, bool boss = false) => new MapCell(index,
            new Enemy(boss ? "boss" : "enemy_" + index, "接触fixture", 50, ElementType.None, new Vector2Int(index, 0)), height);
        private static MapCell Trap(int index, int damage = 15) => new MapCell(index, new Obstacle("trap_" + index, "接触fixture", damage));
        private static DungeonConfig Config(params MapCell[] placed)
        {
            var cells = new System.Collections.Generic.List<MapCell>(); for (int i = 0; i < 40; i++) cells.Add(new MapCell(i));
            cells[39] = EnemyCell(39, boss: true); foreach (var cell in placed) cells[cell.Index] = cell;
            return new DungeonConfig("swept-fixture", "接触fixture", 40, cells);
        }
        private static DungeonSystem Create(params MapCell[] placed) => new DungeonSystem(Config(placed), new PlayerCharacter(100), new Deck(), new MockRandomProvider(0, 1), new MockLogger());
        [TestCase(false, 6)] [TestCase(true, 6)]
        [TestCase(false, 16)] [TestCase(true, 16)]
        public void ダッシュジャンプの空中と着地後の接敵は区間分割によらず通常距離で止まる(bool split, int cell)
        {
            var game = Create(EnemyCell(cell, height: cell == 6 ? 1.5f : 0f)); ContactInfo contact = null;
            game.OnBattleStart += c => contact = c; game.StartDash(); game.Jump();
            if (split) { for (int i = 0; i < 90 && !game.IsInBattle; i++) game.TickFrame(); }
            else game.Tick(1500);
            Assert.IsNotNull(contact); Assert.IsFalse(contact.IsDashContact); Assert.IsNull(contact.DashEffect);
            Assert.AreEqual(cell - 1f, game.PlayerPosition, .001f);
            Assert.AreEqual(50, game.CurrentBattle.Enemies[0].CurrentHp);
            // 接敵で停止したジャンプを、戦闘復帰時に再開しない。
            game.CurrentBattle.Enemies[0].TakeDamage(50, ElementType.None);
            Assert.IsFalse(game.IsDungeonCleared);
            game.ResumePlayerMovement(); Assert.IsFalse(game.IsJumping); Assert.IsFalse(game.IsDashJumping);
            Assert.IsFalse(game.IsDashing); Assert.AreEqual(0, game.PlayerHeight);
        }
        [TestCase(false, 4f)][TestCase(true, 3.5f)]
        public void 大きな経過時間でも最初の敵の接触位置で止まり余剰移動を捨てる(bool dash, float expected)
        {
            var game = Create(EnemyCell(5), EnemyCell(12)); int starts = 0; game.OnBattleStart += _ => starts++;
            if (dash) game.StartDash(); game.Tick(1000);
            Assert.AreEqual(1, starts); Assert.IsTrue(game.IsInBattle); Assert.AreEqual(expected, game.PlayerPosition, .001f);
            game.Tick(1000); Assert.AreEqual(1, starts); Assert.AreEqual(expected, game.PlayerPosition, .001f);
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void 可変フレームでも障害物を通過順に一度ずつ処理して敵で止まる(int schedule)
        {
            var game = Create(Trap(2), Trap(4), EnemyCell(8), Trap(10)); var events = new System.Collections.Generic.List<string>();
            game.OnObstaclePenalty += p => events.Add("trap"); game.OnBattleStart += _ => events.Add("battle");
            int[][] frames = { new[] { 1000 }, new[] { 16, 333, 7, 500, 900 }, new[] { 100, 40, 260, 1, 1000 } };
            foreach (int ms in frames[schedule]) game.Tick(ms);
            CollectionAssert.AreEqual(new[] { "trap", "trap", "battle" }, events); Assert.AreEqual(70, game.Player.CurrentHp);
            Assert.AreEqual(7f, game.PlayerPosition, .001f);
        }
        [TestCase(false, 85)][TestCase(true, 100)]
        public void 敵と障害物の前後はセル番号ではなく既存接触距離の位置順で決まる(bool dash, int hp)
        {
            var game = Create(Trap(4), EnemyCell(5)); if (dash) game.StartDash(); game.Tick(1000);
            Assert.IsTrue(game.IsInBattle); Assert.AreEqual(hp, game.Player.CurrentHp);
        }
        [Test] public void 障害物通知からTickが再入しても移動と同じ罠を二重処理しない()
        {
            var game = Create(Trap(2), EnemyCell(5)); int hits = 0, starts = 0;
            game.OnObstaclePenalty += _ => { hits++; game.Tick(1000); }; game.OnBattleStart += _ => starts++;
            game.Tick(1000); Assert.AreEqual(1, hits); Assert.AreEqual(1, starts); Assert.AreEqual(85, game.Player.CurrentHp);
            Assert.AreEqual(4f, game.PlayerPosition, .001f);
        }
        [Test] public void 障害物の効果を確定してから通知し再入による戦闘開始にも反映する()
        {
            var game = Create(Trap(2), EnemyCell(8)); int notifiedHp = 0;
            game.OnObstaclePenalty += _ => { notifiedHp = game.Player.CurrentHp; game.StartBattle(new ContactInfo {
                Enemy = game.Map.GetCell(8).Enemy, EnemyCellIndex = 8, ContactPositionX = game.PlayerPosition }); };
            game.Tick(1000); Assert.AreEqual(85, notifiedHp); Assert.IsTrue(game.IsInBattle);
            Assert.AreEqual(2f, game.PlayerPosition, .001f);
        }
        [Test] public void 開始通知中に戦闘が完了して再開されても外側Tickの残りを進めない()
        {
            var game = Create(EnemyCell(5), EnemyCell(20)); int starts = 0;
            game.OnBattleStart += _ => { starts++; game.CurrentBattle.Enemies[0].TakeDamage(50, ElementType.None); game.ResumePlayerMovement(); game.Tick(1000); };
            game.Tick(1000); Assert.AreEqual(1, starts); Assert.AreEqual(4f, game.PlayerPosition, .001f);
        }
        [Test] public void 致死障害物以降の敵へ進まず敗北を一度だけ通知する()
        {
            var game = Create(Trap(2, 150), EnemyCell(3)); int starts = 0, deaths = 0;
            game.OnBattleStart += _ => starts++; game.OnDungeonFailed += () => deaths++;
            game.Tick(1000); game.Tick(1000);
            Assert.AreEqual(1, deaths); Assert.AreEqual(0, starts); Assert.AreEqual(2f, game.PlayerPosition, .001f);
        }
        [Test] public void ジャンプ中に通過した通常敵を終点の着地高度で接触扱いしない()
        {
            var game = Create(EnemyCell(3), EnemyCell(8, boss: true)); int contacted = -1;
            game.OnBattleStart += c => contacted = c.EnemyCellIndex; game.Jump(); game.Tick(1000);
            Assert.AreEqual(8, contacted); Assert.AreEqual(7f, game.PlayerPosition, .001f);
        }
        [Test] public void 空中敵は区間途中の到達高度で接触し着地後の終点高度を使わない()
        {
            var game = Create(EnemyCell(3, 1.5f)); int contacted = -1;
            game.OnBattleStart += c => contacted = c.EnemyCellIndex; game.Jump(); game.Tick(1000);
            Assert.AreEqual(3, contacted); Assert.AreEqual(2f, game.PlayerPosition, .001f);
        }
        [Test] public void 敵の接触範囲内で下降して回避高度を下回った時点を見落とさない()
        {
            var game = Create(EnemyCell(5)); game.Jump(); game.Tick(1000);
            Assert.IsTrue(game.IsInBattle); Assert.Greater(game.PlayerPosition, 4.4f); Assert.Less(game.PlayerPosition, 4.5f);
        }
        [Test] public void 後退区間でも最初に横切る既存前方検出範囲を一度だけ通知する()
        {
            var map = new DungeonMap(Config(EnemyCell(5), EnemyCell(15))); var detector = new ContactSystem(map);
            var movement = new PlayerMovement(20f); int cell = -1, calls = 0;
            detector.OnEnemyContact += c => { cell = c.EnemyCellIndex; calls++; detector.CheckContact(movement); };
            movement.Update(-2000); detector.CheckContact(movement); detector.CheckContact(movement);
            Assert.AreEqual(15, cell); Assert.AreEqual(1, calls); Assert.That(movement.CurrentPosition, Is.InRange(14f, 15f));
        }
        [Test] public void 後退で複数障害物を順に処理し方向を戻しても二重被弾しない()
        {
            var game = Create(Trap(15), Trap(10));
            var movement = (PlayerMovement)typeof(DungeonSystem).GetField("playerMovement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(game);
            movement.SetPosition(20f);
            var hp = new System.Collections.Generic.List<int>(); game.OnObstaclePenalty += _ => hp.Add(game.Player.CurrentHp);
            game.Tick(-1500); game.Tick(1500);
            CollectionAssert.AreEqual(new[] { 85, 70 }, hp); Assert.AreEqual(20f, game.PlayerPosition);
        }
        [Test] public void 最大経過時間でもセル数に応じて処理して最初の敵で停止する()
        {
            var game = Create(Trap(2), EnemyCell(8)); game.Tick(int.MaxValue);
            Assert.IsTrue(game.IsInBattle); Assert.AreEqual(85, game.Player.CurrentHp); Assert.AreEqual(7f, game.PlayerPosition);
        }
        [Test] public void ジャンプ回避した障害物を終点の着地高度で再処理しない()
        {
            var game = Create(Trap(2), EnemyCell(8)); game.Jump(); game.Tick(1000);
            Assert.AreEqual(100, game.Player.CurrentHp); Assert.IsTrue(game.IsInBattle);
        }
        [TestCase(0)][TestCase(1)]
        public void 下降中の接触位置はフレーム分割に依存しない(int partition)
        {
            var game = Create(EnemyCell(5)); game.Jump();
            foreach (int ms in partition == 0 ? new[] { 1000 } : new[] { 17, 111, 33, 239, 100, 500 }) game.Tick(ms);
            Assert.IsTrue(game.IsInBattle); Assert.That(game.PlayerPosition, Is.InRange(4.458f, 4.460f));
        }
    }
}
