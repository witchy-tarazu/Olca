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

            return new DungeonSystem(config, player, mockRandom, mockLogger);
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
    }
}
