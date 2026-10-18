using NUnit.Framework;
using Olca.Core.Logic.Integration;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Integration
{
    [TestFixture]
    public class DungeonBattleManagerTests
    {
        private MockRandomProvider mockRandom;
        private MockLogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockRandom = new MockRandomProvider(0);
            mockLogger = new MockLogger();
        }

        private Deck CreateTestDeck()
        {
            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃"),
                new Card("heal", "ヒール", 2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None, 20, "HP回復")
            };

            return new Deck(cards);
        }

        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(1, true)]
        [TestCase(2, true)]
        public void 勝利復帰_撃破参加敵を除去し再接敵せず生存敵を保持する(int count, bool includeDead)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 25; i++) cells.Add(new MapCell(i));
            var avoided = new Enemy("avoided", "回避敵", 20, ElementType.None, new Vector2Int(2, 0));
            var primary = new Enemy("primary", "主敵", 20, ElementType.None, new Vector2Int(10, 0));
            var extra = new Enemy("extra", "隣接巻込み敵", 20, ElementType.None, new Vector2Int(11, 0));
            var unrelated = new Enemy("unrelated", "遠方生存敵", 20, ElementType.None, new Vector2Int(20, 0));
            var dead = new Enemy("dead", "既に死亡した敵", 20, ElementType.None, new Vector2Int(12, 0));
            dead.TakeDamage(100, ElementType.None);
            cells[2] = new MapCell(2, avoided);
            cells[10] = new MapCell(10, primary);
            if (count == 2) cells[11] = new MapCell(11, extra);
            if (includeDead) cells[12] = new MapCell(12, dead);
            cells[20] = new MapCell(20, unrelated);
            var manager = new DungeonBattleManager(new DungeonConfig("roundtrip", "勝利復帰", 25, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            int starts = 0, victories = 0, rewards = 0, returns = 0;
            manager.OnBattleStarted += (_, __) => starts++;
            manager.Dungeon.OnBattleEnd += result => { if (result == BattleResult.Victory) victories++; };
            manager.OnCardRewardPresented += _ => rewards++;
            manager.Dungeon.OnReturnToDungeon += () => returns++;
            manager.Jump(); // 最初の通常敵を実際に回避して通過する。
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(100);
            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase);
            var participants = new List<Enemy>(manager.CurrentBattle.Enemies);
            Assert.AreEqual(count, participants.Count);
            Assert.IsFalse(participants.Contains(dead));
            foreach (var enemy in participants) enemy.TakeDamage(100, ElementType.None);
            manager.Tick(100);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            foreach (var enemy in participants)
                Assert.IsFalse(manager.Dungeon.Map.GetAllEnemies().Exists(entry => ReferenceEquals(entry.enemy, enemy)));
            manager.SelectCardReward(null);
            manager.ResumeAfterBattle();
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon && manager.Dungeon.PlayerPosition < 14f; i++)
                manager.Tick(100);
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.GreaterOrEqual(manager.Dungeon.PlayerPosition, 14f);
            Assert.AreSame(avoided, manager.Dungeon.Map.GetCell(2).Enemy);
            Assert.AreSame(unrelated, manager.Dungeon.Map.GetCell(20).Enemy);
            Assert.IsFalse(avoided.IsDead);
            Assert.IsFalse(unrelated.IsDead);
            if (includeDead) Assert.AreSame(dead, manager.Dungeon.Map.GetCell(12).Enemy, "無関係の参照は一括削除しない");
            Assert.AreEqual(1, starts);
            Assert.AreEqual(1, victories);
            Assert.AreEqual(1, rewards);
            Assert.AreEqual(1, returns);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 接触判定_死亡敵を無視して次の生存敵に一度だけ接触する(bool dash)
        {
            var dead = new Enemy("dead", "死亡敵", 10, ElementType.None, new Vector2Int(2, 0));
            dead.TakeDamage(100, ElementType.None);
            var alive = new Enemy("alive", "生存敵", 10, ElementType.None, new Vector2Int(3, 0));
            var cells = new List<MapCell> { new MapCell(0), new MapCell(1), new MapCell(2, dead), new MapCell(3, alive), new MapCell(4) };
            var contact = new ContactSystem(new DungeonMap(new DungeonConfig("contact", "接触", 5, cells)));
            var movement = new PlayerMovement(1f);
            if (dash) movement.StartDash();
            int calls = 0;
            contact.OnEnemyContact += info => { calls++; Assert.AreSame(alive, info.Enemy); };
            contact.CheckContact(movement);
            contact.ResetContact();
            contact.CheckContact(movement);
            Assert.AreEqual(0, calls);
            movement.SetPosition(2f);
            contact.CheckContact(movement);
            contact.CheckContact(movement);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void 初期化時はダンジョンフェーズ()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();

            // Act
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            // Assert
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.IsNotNull(manager.Dungeon);
            Assert.IsNull(manager.CurrentBattle);
        }

        [Test]
        public void Tick_ダンジョンフェーズ中はダンジョンが更新される()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            float initialPosition = manager.Dungeon.PlayerPosition;

            // Act
            manager.Tick(deltaTimeMs: 1000);

            // Assert
            Assert.Greater(manager.Dungeon.PlayerPosition, initialPosition, "ダンジョンでプレイヤーが前進");
        }

        [Test]
        public void 敵と接敵するとバトルフェーズに遷移する()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            BattleSystem battleStarted = null;
            manager.OnBattleStarted += (battle, contactInfo) => battleStarted = battle;

            // Act: 敵まで進める
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (battleStarted != null) break;
            }

            // Assert
            Assert.IsNotNull(battleStarted, "バトルが開始された");
            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase);
            Assert.IsNotNull(manager.CurrentBattle);
        }

        [Test]
        public void バトル勝利後ダンジョンに戻る()
        {
            // Arrange: index 3 に通常敵（スライム）、index 9 にボスを配置
            // CreateTestDungeon(10) はボスのみ（100%ドロップ）のため専用の構成を使用
            var cells = new List<MapCell>
            {
                new MapCell(0), new MapCell(1), new MapCell(2),
                new MapCell(3, new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0))),
                new MapCell(4), new MapCell(5), new MapCell(6), new MapCell(7), new MapCell(8),
                new MapCell(9, new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(9, 0))),
            };
            var config = new DungeonConfig("test_dungeon", "テストダンジョン", 10, cells);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            // カードドロップなし（通常敵は0.7f >= 0.6f でドロップしない）
            mockRandom.SetFixedValue(0.7f);

            // 敵まで進める
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (manager.CurrentPhase == GamePhase.Battle) break;
            }

            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase, "バトル開始");

            // Act: バトルに勝利
            var battle = manager.CurrentBattle;
            var enemy = battle.Enemies[0];

            // 敵を倒す
            while (!enemy.IsDead)
            {
                enemy.TakeDamage(100, ElementType.None);
            }

            // Tick を進めてバトル終了を処理
            manager.Tick(deltaTimeMs: 100);

            // 通常敵も報酬を提示するため、選択を完了してから復帰を確認する
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase, "報酬選択を待つ");
            manager.SelectCardReward(null);

            // Assert
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase, "ダンジョンフェーズに戻る");
            Assert.IsNull(manager.CurrentBattle, "バトルシステムが破棄される");
        }

        [Test]
        public void バトル敗北時ゲームオーバー()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            bool gameOverFired = false;
            manager.OnGameOver += () => gameOverFired = true;

            // 敵まで進める
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (manager.CurrentPhase == GamePhase.Battle) break;
            }

            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase, "バトル開始");

            // Act: プレイヤーを倒す
            player.TakeDamage(200);

            // Tick を進めてバトル終了を処理
            manager.Tick(deltaTimeMs: 100);

            // Assert
            Assert.AreEqual(GamePhase.GameOver, manager.CurrentPhase);
            Assert.IsTrue(gameOverFired, "ゲームオーバーイベント発火");
        }

        [Test]
        public void ダッシュ接敵時に初撃ダメージが適用される()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 20);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            ContactInfo receivedContactInfo = null;
            manager.OnBattleStarted += (battle, contactInfo) => receivedContactInfo = contactInfo;

            // Act: ダッシュしながら敵に接近
            manager.StartDash();
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (receivedContactInfo != null) break;
            }

            // Assert
            Assert.IsNotNull(receivedContactInfo, "接敵した");
            Assert.IsTrue(receivedContactInfo.IsDashContact, "ダッシュ接敵");

            // 敵のHPが初撃ダメージ分減っている
            // ダッシュ効果がInitialDamageの場合、攻撃力の50% = 10ダメージ
            var enemy = receivedContactInfo.Enemy;
            int expectedDamage = player.AttackPower / 2;  // 20 / 2 = 10
            Assert.Less(enemy.CurrentHp, enemy.MaxHp, "敵がダメージを受けている");
        }

        [Test]
        public void Jump_ダンジョンフェーズ中のみ有効()
        {
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            // Act: ダンジョンフェーズでジャンプ
            manager.Jump();
            manager.Tick(deltaTimeMs: 100);  // Tickを呼んでHeightを更新

            // Assert
            Assert.Greater(manager.Dungeon.PlayerHeight, 0f, "ジャンプした");

            // バトルフェーズに移行
            for (int i = 0; i < 200; i++)
            {
                manager.Tick(deltaTimeMs: 100);
                if (manager.CurrentPhase == GamePhase.Battle) break;
            }

            // バトル中にジャンプしても何も起こらない（エラーにならない）
            manager.Jump();
        }
    }
}
