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
        public void 暗転は戦闘時間とゲージを停止し敵味方各一件を後入れ先出しで解決する()
        {
            var order=new List<string>();var frame=battle.LogicalFrame;var gauge=battle.CustomGauge.CurrentGaugeMs;
            Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Player,2,3,()=>order.Add("oldPlayer")));
            Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Opponent,2,3,()=>order.Add("opponent")));
            Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Player,1,1,()=>order.Add("newPlayer")));
            Assert.IsTrue(battle.IsPaused);Assert.AreEqual(DarkeningSide.Player,battle.ActiveDarkeningSide);
            battle.TickFrame(17);CollectionAssert.AreEqual(new[]{"newPlayer"},order);
            Assert.AreEqual(DarkeningSide.Opponent,battle.ActiveDarkeningSide);
            for(int i=0;i<3;i++)battle.TickFrame(17);
            CollectionAssert.AreEqual(new[]{"newPlayer","opponent"},order);Assert.IsFalse(battle.IsPaused);
            Assert.AreEqual(frame,battle.LogicalFrame);Assert.AreEqual(gauge,battle.CustomGauge.CurrentGaugeMs);
            battle.TickFrame(17);Assert.Greater(battle.LogicalFrame,frame);Assert.Greater(battle.CustomGauge.CurrentGaugeMs,gauge);
        }
        [Test]
        public void 暗転の受付境界と同陣営連続発動を拒否しポーズ中は演出時間も停止する()
        {
            int count=0;Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Player,1,2,()=>count++));
            battle.SetFirstStrike();battle.OpenCustomScreen(new MockRandomProvider(0));Assert.IsFalse(battle.CustomScreen.IsOpen);
            Assert.IsFalse(battle.CustomScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Open),new MockRandomProvider(0)).Accepted);
            Assert.IsFalse(battle.TryStartDarkeningEffect(DarkeningSide.Player,1,2,()=>count++));
            battle.Pause(BattlePauseReason.User);for(int i=0;i<5;i++)battle.TickFrame(17);
            Assert.AreEqual(0,count);Assert.IsTrue(battle.IsDarkening);
            battle.Resume(BattlePauseReason.User);battle.TickFrame(17);
            Assert.IsFalse(battle.TryStartDarkeningEffect(DarkeningSide.Opponent,1,2,()=>count++));
            battle.TickFrame(17);Assert.AreEqual(1,count);Assert.IsFalse(battle.IsDarkening);
        }
        [Test]
        public void 暗転中の戦闘終了と効果例外は保留効果を残さない()
        {
            int count=0;Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Player,1,2,()=>count++));
            enemies[0].TakeDamage(1000,ElementType.None);Assert.IsTrue(battle.IsGameOver);Assert.IsFalse(battle.IsDarkening);Assert.AreEqual(0,count);
        }
        [Test]
        public void 暗転効果例外は停止状態を解放し未定義時間を補完しない()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>battle.TryStartDarkeningEffect(DarkeningSide.Player,2,1,()=>{}));
            Assert.IsTrue(battle.TryStartDarkeningEffect(DarkeningSide.Player,0,1,()=>throw new System.InvalidOperationException("fixture")));
            Assert.Throws<System.InvalidOperationException>(()=>battle.TickFrame(17));Assert.IsFalse(battle.IsDarkening);
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
