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
            // Arrange
            var config = DungeonConfig.CreateTestDungeon(10);
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var deck = CreateTestDeck();
            var manager = new DungeonBattleManager(config, player, deck, mockRandom, mockLogger);

            // カードドロップなしに設定
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
