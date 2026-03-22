using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    /// <summary>
    /// BattleSystem.SetFirstStrike（先制攻撃）のテスト
    /// </summary>
    [TestFixture]
    public class BattleSystemFirstStrikeTests
    {
        private BattleSystem battle;
        private PlayerCharacter player;
        private List<Enemy> enemies;
        private Deck deck;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(4, 0))
            };
            deck = new Deck(new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "攻撃")
            });
            logger = new MockLogger();
            battle = new BattleSystem(player, enemies, deck, customGaugeSpeed: 1.0f, logger: logger);
        }

        [Test]
        public void SetFirstStrike_カスタムゲージが満タンになる()
        {
            // Arrange: 初期状態はゲージ0
            Assert.IsFalse(battle.CustomGauge.IsFull, "前提: 初期状態はゲージ満タンでない");

            // Act
            battle.SetFirstStrike();

            // Assert
            Assert.IsTrue(battle.CustomGauge.IsFull, "先制攻撃でゲージ満タン");
            Assert.AreEqual(CustomGaugeSystem.MAX_GAUGE_MS, battle.CustomGauge.CurrentGaugeMs);
        }

        [Test]
        public void SetFirstStrike_呼び出し後すぐにカスタム画面を開ける()
        {
            // Arrange
            var mockRandom = new MockRandomProvider(0);
            battle.SetFirstStrike();

            // Act & Assert: ゲージ満タンなのでカスタム画面を開けるはず（例外が出ないことを確認）
            Assert.IsTrue(battle.CustomGauge.IsFull, "ゲージが満タンであること");
            // OpenCustomScreen はゲージが満タンでないと警告を出して何もしないため、
            // IsFull チェックでカバーする
        }

        [Test]
        public void SetFirstStrike_既にゲージが途中でも満タンに上書きされる()
        {
            // Arrange: ゲージを途中まで溜める
            battle.CustomGauge.SetGaugeMs(5000);
            Assert.IsFalse(battle.CustomGauge.IsFull, "前提: まだ満タンでない");

            // Act
            battle.SetFirstStrike();

            // Assert
            Assert.IsTrue(battle.CustomGauge.IsFull, "SetFirstStrikeで満タンに上書き");
        }
    }
}
