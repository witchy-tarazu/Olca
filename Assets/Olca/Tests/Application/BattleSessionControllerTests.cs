using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Cysharp.Threading.Tasks;
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
            public bool CompleteRepeatedCloseImmediately { get; set; }
            public Task CloseTask { get; set; } = Task.CompletedTask;
            public int OpenCustomScreenCallCount { get; private set; }
            public int CloseCustomScreenCallCount { get; private set; }

            public void OpenCustomScreen() => OpenCustomScreenCallCount++;
            public Task CloseCustomScreenAsync()
            {
                CloseCustomScreenCallCount++;
                return CompleteRepeatedCloseImmediately && CloseCustomScreenCallCount > 1 ? Task.CompletedTask : CloseTask;
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

        [Test] public void 描画の閉じる処理が未完了でも論理期限で一度だけ戦闘を再開する()
        {
            var waiting = new TaskCompletionSource<bool>(); var view = new MockBattleView { CloseTask = waiting.Task };
            var custom = new MockCustomScreenView(); var loop = new Olca.Unity.UnityLogicalLoop();
            var session = new BattleSessionController(view, custom, new MockRandomProvider(0), new MockLogger(),
                (frames,action)=>loop.AfterFrames(frames,action),24);
            var battle=CreateBattleSystem();session.Attach(battle);battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);session.TryOpenCustomScreen();
            battle.CustomScreen.Confirm(); custom.FireConfirmed();
            loop.Advance(3833334,null,(frame,ms,input)=>battle.Tick(ms)); Assert.IsTrue(battle.IsPaused);
            loop.Advance(166666,null,(frame,ms,input)=>battle.Tick(ms)); Assert.IsFalse(battle.IsPaused);
            Assert.IsFalse(waiting.Task.IsCompleted); Assert.AreEqual(1,view.CloseCustomScreenCallCount);
            waiting.SetResult(true);session.Detach();loop.Dispose();
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
        [TestCase(false)]
        [TestCase(true)]
        public async Task 再初期化後に旧closeが完了しても新しいcustomは閉じない(bool sameBattle)
        {
            var (session, view, custom, _) = CreateSession();
            var oldBattle = CreateBattleSystem();
            session.Attach(oldBattle);
            oldBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            session.TryOpenCustomScreen();
            var delayedClose = new TaskCompletionSource<bool>();
            view.CloseTask = delayedClose.Task;
            custom.FireConfirmed();
            var nextBattle = sameBattle ? oldBattle : CreateBattleSystem();
            session.Attach(nextBattle);
            nextBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            session.TryOpenCustomScreen();
            delayedClose.SetResult(true);
            await Task.Delay(50);
            Assert.IsTrue(nextBattle.CustomScreen.IsOpen);
            Assert.IsTrue(nextBattle.IsPaused);
            session.Detach();
        }

        [Test]
        public async Task 再初期化は旧カード動作を中止し新キューを消費しない()
        {
            var (session, _, _, _) = CreateSession();
            var oldBattle = CreateBattleSystem();
            session.Attach(oldBattle);
            var active = new Card("moving", "一時移動", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Sword, 10, "待機", moveDirection: "Right", moveDistance: 1,
                moveMotionTime: 1f, attackMotionTime: 1f, penetrationMode: MovePenetrationMode.Invincible);
            oldBattle.CardUsage.EnqueueCards(new List<Card> { active });
            int used = 0; oldBattle.CardUsage.OnCardUsed += _ => used++;
            var pending = oldBattle.CardUsage.TryUseCurrentCardAsync().AsTask();
            Assert.IsFalse(pending.IsCompleted);
            var nextBattle = CreateBattleSystem();
            nextBattle.CardUsage.EnqueueCards(new List<Card> { active });
            session.Attach(nextBattle);
            var completed = await Task.WhenAny(pending, Task.Delay(1000));
            Assert.AreSame(pending, completed);
            Assert.IsFalse(await pending);
            oldBattle.Tick(2000); await Task.Delay(50);
            Assert.AreEqual(0, used);
            Assert.IsFalse(oldBattle.Player.IsInTemporaryMove);
            Assert.IsNull(oldBattle.CardUsage.InFlightCard);
            CollectionAssert.AreEqual(new[] { active }, nextBattle.CardUsage.GetRemainingCards());
            session.Detach();
        }

        private Card MovingCard() => new Card("input_move", "入力移動攻撃", 1, CardRarity.Normal,
            CardCategory.Attack, ElementType.None, CardSeries.Sword, 10, "前隙・後隙",
            moveDirection: "Right", moveDistance: 3, moveMotionTime: 0.1f, attackMotionTime: 0.1f,
            penetrationMode: MovePenetrationMode.Invincible);

        private async Task<bool> FinishUse(BattleSystem battle, Task<bool> task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                battle.Tick(10); await Task.Delay(10);
            }
            Assert.IsTrue(task.IsCompleted);
            return await task;
        }

        [Test]
        public async Task 入力経路は非同期待機を通り連打で待機カードを飛ばさない()
        {
            var (session, _, _, logger) = CreateSession();
            var battle = CreateBattleSystem(); session.Attach(battle);
            var active = MovingCard();
            var next = new Card("heal", "回復", 1, CardRarity.Normal, CardCategory.Defense,
                ElementType.None, CardSeries.None, 20, "回復");
            battle.CardUsage.EnqueueCards(new List<Card> { active, next });
            int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            var task = session.TryUseCardAsync().AsTask();
            Assert.IsFalse(task.IsCompleted);
            Assert.IsTrue(battle.Player.IsInTemporaryMove);
            Assert.AreEqual(50, battle.Enemies[0].CurrentHp);
            for (int n = 0; n < 10; n++) session.UseCardFromInput();
            Assert.IsFalse(await session.TryUseCardAsync().AsTask());
            Assert.IsTrue(await FinishUse(battle, task));
            Assert.AreEqual(1, used);
            Assert.AreEqual(40, battle.Enemies[0].CurrentHp);
            Assert.AreSame(next, battle.CardUsage.CurrentCard);
            Assert.IsFalse(battle.Player.IsInTemporaryMove);
            Assert.IsEmpty(logger.ErrorLogs);
            session.Detach();
        }

        [Test]
        public async Task 入力経路の即時回復は従来どおり一枚だけ消費する()
        {
            var (session, _, _, _) = CreateSession();
            var battle = CreateBattleSystem(); session.Attach(battle);
            battle.Player.TakeDamage(50);
            battle.CardUsage.EnqueueCards(new List<Card> { new Card("heal", "回復", 1,
                CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None, 20, "回復") });
            Assert.IsTrue(await session.TryUseCardAsync().AsTask());
            Assert.AreEqual(70, battle.Player.CurrentHp);
            Assert.IsTrue(battle.CardUsage.IsEmpty);
            Assert.IsFalse(await session.TryUseCardAsync().AsTask());
            session.Detach();
            Assert.IsFalse(await session.TryUseCardAsync().AsTask());
        }

        [Test]
        public async Task custom表示とclose待機中の入力は拒否し動作を再開する()
        {
            var (session, view, custom, _) = CreateSession();
            var battle = CreateBattleSystem(); session.Attach(battle);
            var card = MovingCard(); battle.CardUsage.EnqueueCards(new List<Card> { card });
            var task = session.TryUseCardAsync().AsTask();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE); session.TryOpenCustomScreen();
            Assert.IsFalse(await session.TryUseCardAsync().AsTask());
            battle.Tick(1000); await Task.Delay(50);
            Assert.IsFalse(task.IsCompleted);
            Assert.AreEqual(50, battle.Enemies[0].CurrentHp);
            var closing = new TaskCompletionSource<bool>(); view.CloseTask = closing.Task;
            battle.CustomScreen.Confirm(); custom.FireConfirmed();
            Assert.IsFalse(await session.TryUseCardAsync().AsTask());
            closing.SetResult(true); await Task.Delay(30);
            Assert.IsTrue(await FinishUse(battle, task));
            Assert.AreEqual(40, battle.Enemies[0].CurrentHp);
            session.Detach();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task 入力から開始した動作の強靭度中断は一回だけ消費する(bool afterHit)
        {
            var (session, _, _, logger) = CreateSession();
            var battle = CreateBattleSystem(); session.Attach(battle);
            var card = MovingCard();battle.CardUsage.EnqueueCards(new List<Card> { card, card });
            int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            var task = session.TryUseCardAsync().AsTask();
            if (afterHit) { battle.Tick(101); await Task.Delay(30); }
            battle.Player.TakeDamage(battle.Player.Poise);
            Assert.IsTrue(await FinishUse(battle, task));
            Assert.AreEqual(afterHit ? 40 : 50, battle.Enemies[0].CurrentHp);
            Assert.AreEqual(1, used);Assert.AreEqual(1, battle.CardUsage.RemainingCount);
            Assert.IsEmpty(logger.ErrorLogs);session.Detach();
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task 入力動作中の破棄終了再初期化は古い完了を漏らさない(int boundary)
        {
            var (session, _, _, logger) = CreateSession();
            var battle = CreateBattleSystem(); session.Attach(battle);
            var card = MovingCard();battle.CardUsage.EnqueueCards(new List<Card> { card });
            int used = 0;battle.CardUsage.OnCardUsed += _ => used++;
            var task = session.TryUseCardAsync().AsTask();
            BattleSystem next = null;
            if (boundary == 0) battle.CardUsage.Clear();
            if (boundary == 1) session.Detach();
            if (boundary == 2) { next = CreateBattleSystem();next.CardUsage.EnqueueCards(new List<Card> { card });session.Attach(next); }
            if (boundary == 3) battle.Player.TakeDamage(1000);
            if (boundary == 4) battle.Enemies[0].TakeDamage(1000, ElementType.None);
            Assert.IsFalse(await FinishUse(battle, task));
            battle.Tick(1000);await Task.Delay(30);
            Assert.AreEqual(0, used);Assert.IsNull(battle.CardUsage.InFlightCard);
            Assert.IsFalse(battle.Player.IsInTemporaryMove);Assert.IsEmpty(logger.ErrorLogs);
            if (next != null) CollectionAssert.AreEqual(new[] { card }, next.CardUsage.GetRemainingCards());
            session.Detach();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task カード使用例外は入力void境界にも漏れず次カードを誤消費しない(bool voidInput)
        {
            var (session, _, _, logger) = CreateSession();
            var battle = CreateBattleSystem();session.Attach(battle);
            var card = MovingCard();battle.CardUsage.EnqueueCards(new List<Card> { card, card });
            Action<Card> fail = _ => throw new InvalidOperationException("検証用の使用完了例外");
            battle.CardUsage.OnCardUsed += fail;
            int unobserved = 0;Action<Exception> observe = _ => unobserved++;
            UniTaskScheduler.UnobservedTaskException += observe;
            try
            {
                if (voidInput)
                {
                    session.UseCardFromInput();
                    for (int n = 0; n < 30; n++) { battle.Tick(10);await Task.Delay(10); }
                }
                else Assert.IsFalse(await FinishUse(battle, session.TryUseCardAsync().AsTask()));
                Assert.AreEqual(1, logger.ErrorLogs.Count);
                Assert.AreEqual(0, unobserved);
                Assert.AreEqual(1, battle.CardUsage.RemainingCount);
                Assert.IsNull(battle.CardUsage.InFlightCard);
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= observe;
                battle.CardUsage.OnCardUsed -= fail;session.Detach();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task closeのキャンセルと例外は入力イベントから未捕捉にしない(bool cancelled)
        {
            var (session, view, custom, logger) = CreateSession();
            var battle = CreateBattleSystem();session.Attach(battle);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);session.TryOpenCustomScreen();
            view.CloseTask = cancelled ? Task.FromCanceled(new System.Threading.CancellationToken(true))
                : Task.FromException(new InvalidOperationException("検証用close例外"));
            custom.FireConfirmed(); await Task.Delay(30);
            Assert.AreEqual(cancelled ? 0 : 1, logger.ErrorLogs.Count);
            Assert.IsTrue(battle.IsPaused);
            session.Detach();
        }

        [Test]
        public async Task 同一画面の確定連打でも最初のclose完了前に再開しない()
        {
            var (session, view, custom, _) = CreateSession();
            var battle = CreateBattleSystem();session.Attach(battle);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);session.TryOpenCustomScreen();
            var closing = new TaskCompletionSource<bool>();view.CloseTask = closing.Task;
            view.CompleteRepeatedCloseImmediately = true;
            int confirmed = 0;battle.CustomScreen.OnConfirmed += _ => confirmed++;
            battle.CustomScreen.Confirm();custom.FireConfirmed();
            battle.CustomScreen.Confirm();custom.FireConfirmed();
            await Task.Delay(30);
            bool stayedPaused = battle.IsPaused && battle.CustomScreen.IsOpen;
            int closeCalls = view.CloseCustomScreenCallCount;
            closing.SetResult(true);await Task.Delay(30);
            Assert.IsTrue(stayedPaused);
            Assert.AreEqual(1, closeCalls);
            Assert.AreEqual(1, confirmed);
            Assert.IsFalse(battle.IsPaused);session.Detach();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task 確定close失敗後は停止を保って再試行し次回も開ける(bool cancelled)
        {
            var (session, view, custom, _) = CreateSession();
            var battle = CreateBattleSystem();session.Attach(battle);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);session.TryOpenCustomScreen();
            view.CloseTask = cancelled ? Task.FromCanceled(new System.Threading.CancellationToken(true))
                : Task.FromException(new InvalidOperationException("検証用close失敗"));
            battle.CustomScreen.Confirm();custom.FireConfirmed();await Task.Delay(30);
            Assert.IsTrue(battle.IsPaused);Assert.IsTrue(battle.CustomScreen.IsOpen);
            Assert.IsTrue(battle.CustomScreen.CanConfirm());
            view.CloseTask = Task.CompletedTask;
            battle.CustomScreen.Confirm();custom.FireConfirmed();await Task.Delay(30);
            Assert.IsFalse(battle.IsPaused);Assert.IsFalse(battle.CustomScreen.IsOpen);
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);session.TryOpenCustomScreen();
            Assert.IsTrue(battle.CustomScreen.CanConfirm());
            Assert.IsTrue(battle.IsPaused);session.Detach();
        }

    }
}
