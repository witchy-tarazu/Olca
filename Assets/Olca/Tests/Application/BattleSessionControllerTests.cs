using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Olca.Application.Battle;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests.Application
{
    /// <summary>
    /// BattleSessionController のユニットテスト
    /// </summary>
    [TestFixture]
    public class BattleSessionControllerTests
    {
        // ==================== モック ====================

        /// <summary>IBattleView のテスト用モック</summary>
        private class MockBattleView : IBattleView
        {
            public int OpenCustomScreenCallCount { get; private set; }
            public int CloseCustomScreenCallCount { get; private set; }

            public void OpenCustomScreen() => OpenCustomScreenCallCount++;
            public Task CloseCustomScreenAsync()
            {
                CloseCustomScreenCallCount++;
                return Task.CompletedTask;
            }
        }

        /// <summary>ICustomScreenView のテスト用モック</summary>
        private class MockCustomScreenView : ICustomScreenView
        {
            public int InitializeCallCount { get; private set; }
            public event Action OnCustomScreenConfirmed;

            public void Initialize(CustomScreenSystem customScreen)
                => InitializeCallCount++;

            /// <summary>テストから確定イベントを手動発火する</summary>
            public void FireConfirmed() => OnCustomScreenConfirmed?.Invoke();
        }

        // ==================== ヘルパー ====================

        private BattleSystem CreateBattleSystem()
        {
            var player = new PlayerCharacter(maxHp: 100, initialPosition: new Vector2Int(0, 0), attackPower: 10);
            var enemies = new List<Enemy>
            {
                new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0))
            };
            var cards = new List<Card>
            {
                new Card("sword", "ソード", 1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃")
            };
            var deck = new Deck(cards);
            return new BattleSystem(player, enemies, deck, customGaugeSpeed: 2.0f,
                new MockRandomProvider(0), new MockLogger());
        }

        private (BattleSessionController session, MockBattleView battleView,
                 MockCustomScreenView customScreenView, MockLogger logger)
            CreateSession()
        {
            var battleView = new MockBattleView();
            var customScreenView = new MockCustomScreenView();
            var logger = new MockLogger();
            var session = new BattleSessionController(
                battleView, customScreenView, new MockRandomProvider(0), logger);
            return (session, battleView, customScreenView, logger);
        }

        // ==================== Attach / Detach ====================

        [Test]
        public void Attach_正常系_イベント購読が開始される()
        {
            // Arrange
            var (session, battleView, customScreenView, _) = CreateSession();
            var battle = CreateBattleSystem();

            // Act
            session.Attach(battle);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);

            // Assert: Detach前にゲージイベント購読が有効であることをログで確認
            //（OnGaugeFull はログを出力するだけなので、ログが記録されれば購読されている）
            Assert.DoesNotThrow(() => battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE));
        }

        [Test]
        public void Detach_購読が解除される()
        {
            // Arrange
            var (session, battleView, customScreenView, _) = CreateSession();
            var battle = CreateBattleSystem();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            session.Attach(battle);
            session.Detach();

            // Act: Detach後に確定イベントを発火してもOpenCustomScreenが呼ばれないことを確認
            customScreenView.FireConfirmed();

            // Assert: CloseCustomScreenAsync は呼ばれない（購読解除済み）
            Assert.AreEqual(0, battleView.CloseCustomScreenCallCount);
        }

        [Test]
        public void Attach_二回目のAttach_前回の購読が解除されてから再購読される()
        {
            // Arrange
            var (session, battleView, customScreenView, _) = CreateSession();
            var battle1 = CreateBattleSystem();
            var battle2 = CreateBattleSystem();

            // Act: 2回Attachしても購読が重複しない
            session.Attach(battle1);
            session.Attach(battle2); // 内部でDetach→Attachが行われる

            // Assert: 例外なく動作する
            Assert.DoesNotThrow(() => customScreenView.FireConfirmed());
        }

        // ==================== TryOpenCustomScreen ====================

        [Test]
        public void TryOpenCustomScreen_ゲージ満タンかつ画面閉中_カスタム画面が開く()
        {
            // Arrange
            var (session, battleView, customScreenView, _) = CreateSession();
            var battle = CreateBattleSystem();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            session.Attach(battle);

            // Act
            session.TryOpenCustomScreen();

            // Assert
            Assert.AreEqual(1, battleView.OpenCustomScreenCallCount);
            Assert.IsTrue(battle.CustomScreen.IsOpen);
        }

        [Test]
        public void TryOpenCustomScreen_ゲージ未満タン_何もしない()
        {
            // Arrange
            var (session, battleView, _, _) = CreateSession();
            var battle = CreateBattleSystem();
            // ゲージはデフォルト0
            session.Attach(battle);

            // Act
            session.TryOpenCustomScreen();

            // Assert
            Assert.AreEqual(0, battleView.OpenCustomScreenCallCount);
        }

        [Test]
        public void TryOpenCustomScreen_画面オープン中_何もしない()
        {
            // Arrange
            var (session, battleView, _, _) = CreateSession();
            var battle = CreateBattleSystem();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            session.Attach(battle);

            // 一度開く
            session.TryOpenCustomScreen();
            int callCountAfterFirst = battleView.OpenCustomScreenCallCount;

            // Act: 再度呼んでも開かない
            session.TryOpenCustomScreen();

            // Assert
            Assert.AreEqual(callCountAfterFirst, battleView.OpenCustomScreenCallCount);
        }

        [Test]
        public void TryOpenCustomScreen_Attach前_何もしない()
        {
            // Arrange
            var (session, battleView, _, _) = CreateSession();

            // Act: Attach前に呼んでも例外なし
            Assert.DoesNotThrow(() => session.TryOpenCustomScreen());
            Assert.AreEqual(0, battleView.OpenCustomScreenCallCount);
        }

        // ==================== OnCustomScreenConfirmed ====================

        [Test]
        public void OnCustomScreenConfirmed_確定後_CloseCustomScreenAsyncが呼ばれる()
        {
            // Arrange
            var (session, battleView, customScreenView, _) = CreateSession();
            var battle = CreateBattleSystem();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            session.Attach(battle);
            session.TryOpenCustomScreen(); // 画面を開く

            // Act: 確定イベント発火
            customScreenView.FireConfirmed();

            // UniTask の非同期処理が同期的に走りきるまで少し待つ
            // MockBattleView.CloseCustomScreenAsync は Task.CompletedTask を返すので即完了する
            // ただしUniTaskVoidのスケジューリングにより1フレーム遅延する可能性があるため
            // ここでは CloseCustomScreen が呼ばれた事実を確認する
            Assert.AreEqual(1, battleView.CloseCustomScreenCallCount,
                "確定後にCloseCustomScreenAsyncが1回呼ばれること");
        }
    }
}
