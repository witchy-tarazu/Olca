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

        [TestCase(1, false, false)]
        [TestCase(2, false, false)]
        [TestCase(1, true, false)]
        [TestCase(2, true, false)]
        [TestCase(1, true, true)]
        [TestCase(2, true, true)]
        public void ダッシュ初撃全滅_開始後に勝利と報酬を一度だけ通知する(int count, bool boss, bool select)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 12; i++) cells.Add(new MapCell(i));
            for (int i = 0; i < count; i++) cells[2 + i] = new MapCell(2 + i,
                new Enemy(boss && i == 0 ? "boss" : "slime", "初撃対象", 15, ElementType.None, new Vector2Int(2 + i, 0)));
            if (!boss) cells[11] = new MapCell(11, new Enemy("boss", "遠方ボス", 200, ElementType.None, new Vector2Int(11, 0)));
            var manager = new DungeonBattleManager(new DungeonConfig("dash", "初撃全滅", 12, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            var events = new List<string>();
            IReadOnlyList<Card> choices = null;
            manager.OnBattleStarted += (battle, info) => { events.Add("start"); Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase); };
            manager.Dungeon.OnBattleEnd += result => { Assert.AreEqual(BattleResult.Victory, result); events.Add("victory"); };
            manager.OnCardRewardPresented += cards => { choices = cards; events.Add("reward"); };
            manager.OnBattleEnded += () => events.Add("end");
            manager.OnDungeonClearCompleted += _ => events.Add("clear");
            manager.Dungeon.OnReturnToDungeon += () => events.Add("return");
            manager.StartDash();
            for (int i = 0; i < 20 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            CollectionAssert.AreEqual(new[] { "start", "victory", "reward", "end" }, events);
            Assert.IsNull(manager.CurrentBattle);
            manager.SelectCardReward(select ? choices[0] : null);
            manager.Tick(0);
            Assert.AreEqual(boss ? GamePhase.DungeonCleared : GamePhase.Dungeon, manager.CurrentPhase);
            CollectionAssert.AreEqual(new[] { "start", "victory", "reward", "end", boss ? "clear" : "return" }, events);
            Assert.AreEqual(select ? 1 : 0, manager.Dungeon.GetObtainedCards().Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ダッシュ初撃_一部生存は戦闘継続し開始時敗北は上書きしない(bool dieAtStart)
        {
            var weak = new Enemy("weak", "弱敵", 10, ElementType.None, new Vector2Int(2, 0));
            var strong = new Enemy("strong", "強敵", 100, ElementType.None, new Vector2Int(3, 0));
            var cells = new List<MapCell> { new MapCell(0), new MapCell(1), new MapCell(2, weak), new MapCell(3, strong) };
            var player = new PlayerCharacter(100);
            var manager = new DungeonBattleManager(new DungeonConfig("dash", "初撃境界", 4, cells),
                player, CreateTestDeck(), mockRandom, mockLogger);
            int rewards = 0, ends = 0, failures = 0;
            manager.OnCardRewardPresented += _ => rewards++;
            manager.OnBattleEnded += () => ends++;
            manager.OnGameOver += () => failures++;
            if (dieAtStart) manager.OnBattleStarted += (_, __) => player.TakeDamage(1000);
            manager.StartDash();
            for (int i = 0; i < 20 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreEqual(0, rewards);
            if (dieAtStart)
            {
                Assert.AreEqual(GamePhase.GameOver, manager.CurrentPhase);
                Assert.AreEqual(1, failures);
                Assert.AreEqual(1, ends);
                Assert.IsFalse(weak.IsDead, "開始通知中の敗北後には初撃を適用しない");
                Assert.AreEqual(100, strong.CurrentHp);
            }
            else
            {
                Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase);
                Assert.IsTrue(weak.IsDead);
                Assert.AreEqual(85, strong.CurrentHp);
                Assert.AreEqual(0, ends);
                strong.TakeDamage(1000, ElementType.None);
                Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
                Assert.AreEqual(1, ends);
                Assert.AreEqual(1, rewards);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void 最奥ボス勝利_回避敵が残っても報酬完了後に一度だけクリア(bool selectReward, bool nearbyNormal)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 12; i++) cells.Add(new MapCell(i));
            var avoided = new Enemy("avoided", "回避敵", 20, ElementType.None, new Vector2Int(2, 0));
            var boss = new Enemy("boss", "最奥ボス", 200, ElementType.None, new Vector2Int(11, 0));
            cells[2] = new MapCell(2, avoided);
            cells[11] = new MapCell(11, boss);
            if (nearbyNormal) cells[10] = new MapCell(10,
                new Enemy("primary", "主敵", 20, ElementType.None, new Vector2Int(10, 0)));
            var manager = new DungeonBattleManager(new DungeonConfig("boss", "ボスクリア", 12, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            int clears = 0, returns = 0, rewards = 0;
            IReadOnlyList<Card> choices = null;
            manager.OnDungeonClearCompleted += _ => clears++;
            manager.Dungeon.OnReturnToDungeon += () => returns++;
            manager.OnCardRewardPresented += cards => { rewards++; choices = cards; };
            manager.Jump();
            // 新仕様ではボス巻込みは禁止。手前の通常敵も回避し、ボス自身へ直接接敵する。
            bool jumpedNearBoss = false;
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++)
            {
                if (nearbyNormal && !jumpedNearBoss && manager.Dungeon.PlayerPosition >= 8f)
                {
                    manager.Jump();
                    jumpedNearBoss = true;
                }
                manager.Tick(100);
            }
            Assert.AreEqual(GamePhase.Battle, manager.CurrentPhase);
            Assert.AreEqual(nearbyNormal ? 2 : 1, manager.CurrentBattle.Enemies.Count);
            foreach (var enemy in new List<Enemy>(manager.CurrentBattle.Enemies)) enemy.TakeDamage(1000, ElementType.None);
            manager.Tick(100);
            Assert.AreEqual(GamePhase.CardReward, manager.CurrentPhase);
            Assert.AreEqual(0, clears, "報酬完了前にはクリアしない");
            manager.SelectCardReward(selectReward ? choices[0] : null);
            Assert.AreEqual(GamePhase.DungeonCleared, manager.CurrentPhase);
            Assert.IsTrue(manager.Dungeon.IsDungeonCleared);
            Assert.AreSame(avoided, manager.Dungeon.Map.GetCell(2).Enemy);
            Assert.IsFalse(avoided.IsDead);
            Assert.IsNull(manager.Dungeon.Map.GetCell(11).Enemy);
            Assert.AreEqual(selectReward ? 1 : 0, manager.Dungeon.GetObtainedCards().Count);
            manager.ResumeAfterBattle();
            manager.Tick(1000);
            manager.Dungeon.OnCardSelectionCompleted(null);
            Assert.AreEqual(1, clears);
            Assert.AreEqual(1, rewards);
            Assert.AreEqual(0, returns);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 通常接敵_近隣ボスを残し通常巻込み戦闘の後でボスへ直接接敵する(bool dash)
        {
            var normal = new Enemy("normal", "主敵", 50, ElementType.None, new Vector2Int(2, 0));
            var boss = new Enemy("boss", "ボス", 200, ElementType.None, new Vector2Int(3, 0));
            var extra = new Enemy("extra", "通常巻込み", 50, ElementType.None, new Vector2Int(4, 0));
            var cells = new List<MapCell> { new MapCell(0), new MapCell(1), new MapCell(2, normal), new MapCell(3, boss), new MapCell(4, extra) };
            var manager = new DungeonBattleManager(new DungeonConfig("boss", "ボス除外", 5, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            int starts = 0, clears = 0;
            ContactInfo contact = null;
            manager.OnBattleStarted += (_, info) => { starts++; contact = info; };
            manager.OnDungeonClearCompleted += _ => clears++;
            if (dash) manager.StartDash();
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreSame(normal, contact.Enemy);
            CollectionAssert.AreEquivalent(new[] { normal, extra }, manager.CurrentBattle.Enemies);
            Assert.AreEqual(200, boss.CurrentHp, "初撃もボスへ適用しない");
            foreach (var enemy in new List<Enemy>(manager.CurrentBattle.Enemies)) enemy.TakeDamage(1000, ElementType.None);
            manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.AreEqual(0, clears);
            Assert.IsNull(manager.Dungeon.Map.GetCell(2).Enemy);
            Assert.IsNull(manager.Dungeon.Map.GetCell(4).Enemy);
            Assert.AreSame(boss, manager.Dungeon.Map.GetCell(3).Enemy);
            manager.StopDash();
            manager.ResumeAfterBattle();
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(50);
            Assert.AreEqual(2, starts);
            Assert.AreSame(boss, contact.Enemy);
            CollectionAssert.AreEqual(new[] { boss }, manager.CurrentBattle.Enemies);
            boss.TakeDamage(1000, ElementType.None);
            manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.DungeonCleared, manager.CurrentPhase);
            Assert.AreEqual(1, clears);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 最奥ボス未撃破_通常敵や手前のボスの勝利ではクリアしない(bool earlierBoss)
        {
            var cells = new List<MapCell>();
            for (int i = 0; i < 12; i++) cells.Add(new MapCell(i));
            cells[2] = new MapCell(2, new Enemy(earlierBoss ? "boss" : "slime", "手前の敵", 20,
                ElementType.None, new Vector2Int(2, 0)));
            var boss = new Enemy("boss", "最奥ボス", 200, ElementType.None, new Vector2Int(11, 0));
            cells[11] = new MapCell(11, boss);
            var manager = new DungeonBattleManager(new DungeonConfig("boss", "最奥未撃破", 12, cells),
                new PlayerCharacter(100), CreateTestDeck(), mockRandom, mockLogger);
            int clears = 0;
            manager.OnDungeonClearCompleted += _ => clears++;
            for (int i = 0; i < 100 && manager.CurrentPhase == GamePhase.Dungeon; i++) manager.Tick(100);
            manager.CurrentBattle.Enemies[0].TakeDamage(1000, ElementType.None);
            manager.Tick(100);
            manager.SelectCardReward(null);
            Assert.AreEqual(GamePhase.Dungeon, manager.CurrentPhase);
            Assert.IsFalse(manager.Dungeon.IsDungeonCleared);
            Assert.AreSame(boss, manager.Dungeon.Map.GetCell(11).Enemy);
            Assert.IsFalse(boss.IsDead);
            Assert.AreEqual(0, clears);
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
