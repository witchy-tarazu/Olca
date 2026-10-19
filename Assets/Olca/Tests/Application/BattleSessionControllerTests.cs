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

        [TestCase(30,false,false)][TestCase(60,false,false)][TestCase(120,false,false)][TestCase(60,true,false)]
        [TestCase(30,false,true)][TestCase(60,false,true)][TestCase(120,false,true)][TestCase(60,true,true)]
        public void 同じ確定イベントなら描画頻度とbacklogによらず同じ論理フレームで再開する(int fps,bool backlog,bool keyboard)
        {
            var loop=new Olca.Unity.UnityLogicalLoop();long now=0;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));
            typeof(Olca.Unity.UnityLogicalLoop).GetField("observed",flags).SetValue(loop,0L);
            var go=new UnityEngine.GameObject("confirmation-regression");
            var actualView=go.AddComponent<Olca.Presentation.CustomScreen.CustomScreenView>();
            var view=new MockBattleView();var battle=CreateBattleSystem();
            var session=new BattleSessionController(view,actualView,new MockRandomProvider(0),new MockLogger(),(frames,action)=>loop.AfterFrames(frames,action),24);
            typeof(Olca.Presentation.CustomScreen.CustomScreenView).GetField("customScreen",flags).SetValue(actualView,battle.CustomScreen);
            var click=typeof(Olca.Presentation.CustomScreen.CustomScreenView).GetMethod("OnConfirmClicked",flags);
            session.Attach(battle);battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);session.TryOpenCustomScreen();
            long resumed=-1;bool sent=false;long eventAt=System.Diagnostics.Stopwatch.Frequency*105/1000;
            actualView.ConfigureLogicalConfirmation(loop.QueueTimestampedCommand,()=>eventAt);
            try {
                Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
                for(int n=1;n<=fps*2;n++){
                    now=backlog&&n==1?System.Diagnostics.Stopwatch.Frequency:Math.Max(now,System.Diagnostics.Stopwatch.Frequency*n/fps);
                    var events=new List<Olca.Unity.TimestampedLogicalInput>();
                    if(!sent&&now>=eventAt){
                        if(keyboard) events.Add(new Olca.Unity.TimestampedLogicalInput(eventAt,new Olca.Core.Logic.Integration.LogicalInput { MenuConfirmButtonDown=true }));
                        else click.Invoke(actualView,null);
                        sent=true;
                    }
                    loop.PumpTimestamped(true,events,(frame,ms,input)=>{actualView.HandleLogicalConfirmation(input);battle.TickFrame(ms,input);if(!battle.IsPaused&&resumed<0)resumed=frame;});
                }
                Assert.AreEqual(31,resumed,"105ms確定は論理frame7、24frame後に再開");
            } finally {session.Detach();loop.Dispose();UnityEngine.Object.DestroyImmediate(go);}
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

        private (BattleSystem battle, Enemy enemy, BattleSessionController session) StatusBattle()
        {
            var enemy = new Enemy("fixture", "検証", 1000, ElementType.None, new Vector2Int(3, 0));
            var battle = new BattleSystem(new PlayerCharacter(1000, new Vector2Int(2, 0)),
                new List<Enemy> { enemy }, new Deck(), logger: new MockLogger());
            var session = CreateSession().session;
            session.Attach(battle);
            return (battle, enemy, session);
        }
        private Card StatusCard(AdditionalEffectType type, float seconds = 1f)
        {
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = type,
                Trigger = DerivativeTrigger.OnDamage, Duration = seconds, Power = 60 });
            return new Card("fixture", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 1, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single, modifier: modifier);
        }
        private void UseStatusCard(BattleSystem battle, BattleSessionController session, Card card)
        {
            battle.CardUsage.EnqueueCards(new List<Card> { card });
            var use = session.TryUseCardAsync();
            Assert.AreEqual(UniTaskStatus.Succeeded, use.Status);
            Assert.IsTrue(use.GetAwaiter().GetResult());
        }
        [Test]
        public void 通常セッションの毒付与は当該フレームに進まず再付与も次から有効になる()
        {
            var (battle, enemy, session) = StatusBattle();
            enemy.ApplyStun(60000);
            session.ProcessLogicalFrame(() => {
                UseStatusCard(battle, session, StatusCard(AdditionalEffectType.Poison));
                Assert.IsNull(enemy.ActiveStatus);
                battle.TickFrame(17);
                Assert.AreEqual(1000, enemy.CurrentHp, "HPはフレーム中に反映しない");
            });
            Assert.AreEqual(999, enemy.CurrentHp);
            Assert.AreEqual(60, enemy.ActiveStatus.RemainingFrames);
            session.ProcessLogicalFrame(() => {
                UseStatusCard(battle, session, StatusCard(AdditionalEffectType.Poison, 2f));
                Assert.AreEqual(60, enemy.ActiveStatus.RemainingFrames);
                battle.TickFrame(17);
            });
            Assert.AreEqual(997, enemy.CurrentHp);
            Assert.AreEqual(120, enemy.ActiveStatus.RemainingFrames);
            session.ProcessLogicalFrame(() => { enemy.ClearStatus(StatusEffectType.Poison); battle.TickFrame(17); });
            Assert.AreEqual(996, enemy.CurrentHp, "解除フレームの毒は計上する");
            Assert.IsNull(enemy.ActiveStatus);
            battle.TickFrame(17); Assert.AreEqual(996, enemy.CurrentHp);
            session.Detach();
        }
        [TestCase(false)] [TestCase(true)]
        public void 同フレームの解除は付与順によらず次フレームへの有効化も取り消す(bool clearFirst)
        {
            var (battle, enemy, session) = StatusBattle();
            session.ProcessLogicalFrame(() => {
                if (clearFirst) enemy.ClearStatus(StatusEffectType.Paralyze);
                UseStatusCard(battle, session, StatusCard(AdditionalEffectType.Paralyze));
                if (!clearFirst) enemy.ClearStatus(StatusEffectType.Paralyze);
                Assert.IsFalse(enemy.IsParalyzed);
                battle.TickFrame(17);
            });
            Assert.IsFalse(enemy.IsParalyzed); Assert.IsNull(enemy.ActiveStatus);
            Assert.AreEqual(990, battle.Player.CurrentHp, "新規麻痺では当該フレームの攻撃を中断しない");
            session.Detach();
        }
        [Test]
        public void 麻痺は次フレームから最終有効フレームまで行動判定に使う()
        {
            var (battle, enemy, session) = StatusBattle();
            session.ProcessLogicalFrame(() => {
                UseStatusCard(battle, session, StatusCard(AdditionalEffectType.Paralyze, .001f));
                battle.TickFrame(17);
            });
            Assert.AreEqual(990, battle.Player.CurrentHp); Assert.IsTrue(enemy.IsParalyzed);
            session.ProcessLogicalFrame(() => {
                Assert.IsTrue(enemy.IsParalyzed); battle.TickFrame(17);
                Assert.IsTrue(enemy.IsParalyzed, "残時間切れでもこのフレームの判定は維持");
            });
            Assert.IsFalse(enemy.IsParalyzed); Assert.IsNull(enemy.ActiveStatus);
            session.Detach();
        }
        [Test]
        public void 付与フレームの死亡は保留状態を有効化せず終了する()
        {
            var (battle, enemy, session) = StatusBattle();
            session.ProcessLogicalFrame(() => {
                UseStatusCard(battle, session, StatusCard(AdditionalEffectType.Poison));
                enemy.TakeDamage(2000, ElementType.None); battle.TickFrame(17);
            });
            Assert.IsTrue(battle.IsGameOver); Assert.IsNull(enemy.ActiveStatus);
            session.Detach();
        }
        private List<string> StatusTrace(int fps, bool stall)
        {
            var (battle, enemy, session) = StatusBattle();
            enemy.ApplyStun(60000);
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock();
            var trace = new List<string>();
            Action<long, int> tick = (frame, ms) => session.ProcessLogicalFrame(() => {
                if (frame == 1 || frame == 20) UseStatusCard(battle, session, StatusCard(AdditionalEffectType.Poison));
                if (frame == 25) battle.Pause(BattlePauseReason.User);
                if (frame == 30) battle.Resume(BattlePauseReason.User);
                if (frame == 35) { battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE); session.TryOpenCustomScreen(); }
                if (frame == 40) battle.CustomScreen.Cancel();
                if (frame == 50) enemy.ClearStatus(StatusEffectType.Poison);
                battle.TickFrame(ms);
                trace.Add(frame+":"+enemy.CurrentHp+":"+enemy.ActiveStatus?.RemainingFrames+":"+battle.IsPaused);
            });
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond * 2, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 7); }
            for (long render = 1; previous < total; render++)
            {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 7);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 7);
            Assert.AreEqual(120, clock.Frame); Assert.IsNull(enemy.ActiveStatus);
            Assert.AreEqual(trace[24].Split(':')[1], trace[28].Split(':')[1]);
            Assert.AreEqual(trace[34].Split(':')[1], trace[38].Split(':')[1]);
            session.Detach(); return trace;
        }
        [TestCase(30, false)] [TestCase(60, false)] [TestCase(120, false)]
        [TestCase(30, true)] [TestCase(60, true)] [TestCase(120, true)]
        public void 通常BattleSessionの状態境界は描画FPSと処理遅延に依存しない(int fps, bool stall)
        { CollectionAssert.AreEqual(StatusTrace(60, false), StatusTrace(fps, stall)); }

        [Test]
        public void プレイヤー死亡で終了した場合も生存敵への保留付与を取り消す()
        {
            var (battle, enemy, session) = StatusBattle();
            session.ProcessLogicalFrame(() => {
                UseStatusCard(battle, session, StatusCard(AdditionalEffectType.Poison));
                Assert.IsNull(enemy.ActiveStatus);
                battle.Player.TakeDamage(1000);
                battle.TickFrame(17);
            });
            Assert.AreEqual(BattleState.Defeat, battle.State);
            Assert.IsFalse(enemy.IsDead);
            Assert.IsNull(enemy.ActiveStatus);
            session.ProcessLogicalFrame(() => battle.TickFrame(17));
            Assert.IsNull(enemy.ActiveStatus, "終了後に保留が有効化されない");
            session.Detach();
        }

        [Test]
        public void 既存麻痺の明示解除フレームもAIは停止し次フレームに再開する()
        {
            var (battle, enemy, session) = StatusBattle();
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze, 1000));
            session.ProcessLogicalFrame(() => {
                enemy.ClearStatus(StatusEffectType.Paralyze);
                battle.TickFrame(17);
                Assert.IsTrue(enemy.IsParalyzed);
                Assert.AreEqual(1000, battle.Player.CurrentHp);
            });
            Assert.IsFalse(enemy.IsParalyzed);
            session.ProcessLogicalFrame(() => battle.TickFrame(17));
            Assert.AreEqual(990, battle.Player.CurrentHp, "解除の翌フレームに通常攻撃を再開");
            session.Detach();
        }

        [TestCase(false)] [TestCase(true)]
        public void 通常セッションの火攻撃は同じ草むらの全員を判定してから燃焼する(bool trackHits)
        {
            var position = new Vector2Int(3, 0);
            var first = new Enemy("a", "a", 1000, ElementType.None, position);
            var second = new Enemy("b", "b", 1000, ElementType.None, position);
            var floating = new Enemy("c", "c", 1000, ElementType.None, position);
            var immune = new Enemy("d", "d", 1000, ElementType.None, position, resistances: new ElementResistances(fire:100));
            floating.TerrainState.IsFloating = true;
            var battle = new BattleSystem(new PlayerCharacter(1000, new Vector2Int(2,0)),
                new List<Enemy>{first, second, floating, immune}, new Deck());
            var session = CreateSession().session; session.Attach(battle);
            var modifier = new CardModifier();
            if (trackHits) modifier.AdditionalEffects.Add(new AdditionalEffect {
                EffectType=AdditionalEffectType.Poison, Trigger=DerivativeTrigger.OnDamage, Power=3, Duration=1f });
            var card = new Card("fire", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire,
                CardSeries.Canon, 10, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.All, modifier:modifier);
            battle.Field.ChangePanelType(position, PanelType.Grass);
            session.ProcessLogicalFrame(() => UseStatusCard(battle,session,card));
            Assert.AreEqual(980,first.CurrentHp); Assert.AreEqual(980,second.CurrentHp);
            Assert.AreEqual(990,floating.CurrentHp); Assert.AreEqual(1000,immune.CurrentHp);
            Assert.AreEqual(PanelType.Normal,battle.Field.GetPanelType(position));
            session.ProcessLogicalFrame(() => UseStatusCard(battle,session,card));
            Assert.AreEqual(970,first.CurrentHp); Assert.AreEqual(970,second.CurrentHp);
            session.Detach();
        }

        [Test]
        public void 前方マスへの火攻撃は空の草むらも燃やし中断前やポーズ中には燃やさない()
        {
            var (battle, enemy, session) = StatusBattle();
            enemy.ApplyStun(60000);
            var empty = new Vector2Int(4,0);
            battle.Field.ChangePanelType(empty, PanelType.Grass);
            var card = new Card("fire", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire,
                CardSeries.Canon, 10, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTwoTiles,
                startupTime:.05f);
            battle.CardUsage.EnqueueCards(new List<Card>{card});
            UniTask<bool> pending=default;
            session.ProcessLogicalFrame(() => { pending=session.TryUseCardAsync(); battle.TickFrame(17); });
            Assert.AreEqual(PanelType.Grass,battle.Field.GetPanelType(empty));
            battle.Player.TakeDamage(1,forceStagger:true);
            Assert.IsTrue(pending.GetAwaiter().GetResult(), "モーション開始後の中断でもカードは消費する");
            for(int frame=0;frame<5;frame++)session.ProcessLogicalFrame(()=>battle.TickFrame(17));
            Assert.AreEqual(PanelType.Grass,battle.Field.GetPanelType(empty));
            battle.CardUsage.EnqueueCards(new List<Card>{card});
            session.ProcessLogicalFrame(()=>pending=session.TryUseCardAsync());
            battle.Pause(BattlePauseReason.User);
            for(int frame=0;frame<60;frame++)session.ProcessLogicalFrame(()=>battle.TickFrame(17));
            Assert.AreEqual(PanelType.Grass,battle.Field.GetPanelType(empty));
            battle.Resume(BattlePauseReason.User);
            for(int frame=0;frame<3;frame++)session.ProcessLogicalFrame(()=>battle.TickFrame(17));
            Assert.IsTrue(pending.GetAwaiter().GetResult());
            Assert.AreEqual(PanelType.Normal,battle.Field.GetPanelType(empty));
            session.Detach();
        }

        [TestCase(ElementType.Fire, PanelType.Normal, 1000)]
        [TestCase(ElementType.Water, PanelType.Grass, 990)]
        [TestCase(ElementType.None, PanelType.Grass, 990)]
        public void 燃焼は火属性だけで耐性無効時も起こる(ElementType element, PanelType expectedPanel, int expectedHp)
        {
            var enemy = new Enemy("immune", "検証", 1000, ElementType.None, new Vector2Int(3,0),
                resistances:new ElementResistances(fire:100));
            var battle = new BattleSystem(new PlayerCharacter(1000,new Vector2Int(2,0)),new List<Enemy>{enemy},new Deck());
            var session=CreateSession().session;session.Attach(battle);
            battle.Field.ChangePanelType(enemy.GridPosition,PanelType.Grass);
            var card=new Card("fixture","検証",1,CardRarity.Normal,CardCategory.Attack,element,
                CardSeries.Canon,10,"検証",Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single);
            session.ProcessLogicalFrame(()=>UseStatusCard(battle,session,card));
            Assert.AreEqual(expectedHp,enemy.CurrentHp);
            Assert.AreEqual(expectedPanel,battle.Field.GetPanelType(enemy.GridPosition));
            session.Detach();
        }

        [Test]
        public void 火属性を持つ非攻撃の地形カードは燃焼処理の対象外()
        {
            var (battle,enemy,session)=StatusBattle();
            var modifier=new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect {
                EffectType=AdditionalEffectType.PanelGrass, Trigger=DerivativeTrigger.Immediate });
            var card=new Card("field","検証",1,CardRarity.Normal,CardCategory.Field,ElementType.Fire,
                CardSeries.None,0,"検証",Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,modifier:modifier);
            session.ProcessLogicalFrame(()=>UseStatusCard(battle,session,card));
            Assert.AreEqual(PanelType.Grass,battle.Field.GetPanelType(enemy.GridPosition));
            Assert.AreEqual(1000,enemy.CurrentHp);
            session.Detach();
        }

        private List<string> SpreadFireTrace(int fps, bool stall)
        {
            var center = new Vector2Int(3,0); var empty = new Vector2Int(4,0);
            var a=new Enemy("a","検証",1000,ElementType.None,center);
            var b=new Enemy("b","検証",1000,ElementType.None,center);
            var floating=new Enemy("f","検証",1000,ElementType.None,center);
            var immune=new Enemy("i","検証",1000,ElementType.None,center,resistances:new ElementResistances(fire:100));
            floating.TerrainState.IsFloating=true;
            foreach(var e in new[]{a,b,floating,immune})e.ApplyStun(60000);
            var battle=new BattleSystem(new PlayerCharacter(1000,new Vector2Int(1,0)),new List<Enemy>{a,b,floating,immune},new Deck());
            var session=CreateSession().session;session.Attach(battle);
            foreach(int x in new[]{2,3,4})battle.Field.ChangePanelType(new Vector2Int(x,0),PanelType.Grass);
            var modifier=new CardModifier();modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Spread,Trigger=DerivativeTrigger.Immediate});
            var card=new Card("spread","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.Fire,CardSeries.Canon,10,"検証",
                Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,modifier:modifier,startupTime:.05f);
            var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();var trace=new List<string>();
            Action<long,int> tick=(frame,ms)=>session.ProcessLogicalFrame(()=>{
                if(frame==1||frame==10||frame==30){battle.CardUsage.EnqueueCards(new List<Card>{card});session.UseCardFromInput();}
                if(frame==2)battle.Player.TakeDamage(1,forceStagger:true);
                if(frame==11)battle.Pause(BattlePauseReason.User);
                if(frame==15)battle.Resume(BattlePauseReason.User);
                battle.TickFrame(ms);
                if(frame==9||frame==14)Assert.AreEqual(PanelType.Grass,battle.Field.GetPanelType(empty));
                if(frame==18){Assert.AreEqual(980,a.CurrentHp);Assert.AreEqual(980,b.CurrentHp);Assert.AreEqual(990,floating.CurrentHp);Assert.AreEqual(1000,immune.CurrentHp);}
                trace.Add(frame+":"+a.CurrentHp+":"+b.CurrentHp+":"+floating.CurrentHp+":"+battle.Field.GetPanelType(empty));
            });
            long total=Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond,previous=0;
            if(stall){previous=total/2;clock.Accumulate(previous);clock.Drain(tick,3);}
            for(long render=1;previous<total;render++){
                long now=Math.Min(total,(stall?total/2:0)+render*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps);
                clock.Accumulate(now-previous);previous=now;clock.Drain(tick,3);
            }
            while(clock.PendingFrames>0)clock.Drain(tick,3);
            Assert.AreEqual(970,a.CurrentHp);Assert.AreEqual(970,b.CurrentHp);
            foreach(int x in new[]{2,3,4})Assert.AreEqual(PanelType.Normal,battle.Field.GetPanelType(new Vector2Int(x,0)));
            session.Detach();return trace;
        }
        [TestCase(30,false)] [TestCase(60,false)] [TestCase(120,false)]
        [TestCase(30,true)] [TestCase(60,true)] [TestCase(120,true)]
        public void 拡散火攻撃の燃焼と中断再開は通常セッションでFPSと処理遅延に依存しない(int fps,bool stall)
        {CollectionAssert.AreEqual(SpreadFireTrace(60,false),SpreadFireTrace(fps,stall));}

        private List<string> PenetratingFireTrace(int fps, bool stall)
        {
            var center = new Vector2Int(3,0); var empty = new Vector2Int(4,0);
            var a=new Enemy("a","検証",1000,ElementType.None,center);
            var b=new Enemy("b","検証",1000,ElementType.None,center);
            var floating=new Enemy("f","検証",1000,ElementType.None,center);
            var immune=new Enemy("i","検証",1000,ElementType.None,center,resistances:new ElementResistances(fire:100));
            floating.TerrainState.IsFloating=true;
            foreach(var e in new[]{a,b,floating,immune})e.ApplyStun(60000);
            var battle=new BattleSystem(new PlayerCharacter(1000,new Vector2Int(1,0)),new List<Enemy>{a,b,floating,immune},new Deck());
            var session=CreateSession().session;session.Attach(battle);
            foreach(int x in new[]{0,1,2,3,4,5})battle.Field.ChangePanelType(new Vector2Int(x,0),PanelType.Grass);
            var modifier=new CardModifier();modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Penetration,Trigger=DerivativeTrigger.Immediate});
            var card=new Card("penetration","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.Fire,CardSeries.Canon,10,"検証",
                Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,modifier:modifier,startupTime:.05f);
            var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();var trace=new List<string>();
            Action<long,int> tick=(frame,ms)=>session.ProcessLogicalFrame(()=>{
                if(frame==1||frame==10||frame==30){battle.CardUsage.EnqueueCards(new List<Card>{card});session.UseCardFromInput();}
                if(frame==2)battle.Player.TakeDamage(1,forceStagger:true);
                if(frame==11)battle.Pause(BattlePauseReason.User);
                if(frame==15)battle.Resume(BattlePauseReason.User);
                battle.TickFrame(ms);
                if(frame==9||frame==14)Assert.AreEqual(PanelType.Grass,battle.Field.GetPanelType(empty));
                if(frame==18){Assert.AreEqual(980,a.CurrentHp);Assert.AreEqual(980,b.CurrentHp);Assert.AreEqual(990,floating.CurrentHp);Assert.AreEqual(1000,immune.CurrentHp);}
                trace.Add(frame+":"+a.CurrentHp+":"+b.CurrentHp+":"+floating.CurrentHp+":"+battle.Field.GetPanelType(empty));
            });
            long total=Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond,previous=0;
            if(stall){previous=total/2;clock.Accumulate(previous);clock.Drain(tick,3);}
            for(long render=1;previous<total;render++){
                long now=Math.Min(total,(stall?total/2:0)+render*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps);
                clock.Accumulate(now-previous);previous=now;clock.Drain(tick,3);
            }
            while(clock.PendingFrames>0)clock.Drain(tick,3);
            Assert.AreEqual(970,a.CurrentHp);Assert.AreEqual(970,b.CurrentHp);
            foreach(int x in new[]{0,1,2,3,4,5})Assert.AreEqual(x <= 1 ? PanelType.Grass : PanelType.Normal,battle.Field.GetPanelType(new Vector2Int(x,0)));
            session.Detach();return trace;
        }
        [TestCase(30,false)] [TestCase(60,false)] [TestCase(120,false)]
        [TestCase(30,true)] [TestCase(60,true)] [TestCase(120,true)]
        public void 貫通火攻撃の燃焼と中断再開は通常セッションでFPSと処理遅延に依存しない(int fps,bool stall)
        {CollectionAssert.AreEqual(PenetratingFireTrace(60,false),PenetratingFireTrace(fps,stall));}

        private List<string> ProjectileFireTrace(int fps, bool stall)
        {
            var center=new Vector2Int(3,0);
            var a=new Enemy("a","検証",1000,ElementType.None,center);
            var b=new Enemy("b","検証",1000,ElementType.None,center);
            var floating=new Enemy("f","検証",1000,ElementType.None,center);
            floating.TerrainState.IsFloating=true;
            foreach(var e in new[]{a,b,floating})e.ApplyStun(60000);
            var battle=new BattleSystem(new PlayerCharacter(1000,new Vector2Int(1,0)),new List<Enemy>{a,b,floating},new Deck());
            var session=CreateSession().session;session.Attach(battle);
            battle.Field.ChangePanelType(center,PanelType.Grass);
            var modifier=new CardModifier();modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.DelayedProjectile,
                Trigger=DerivativeTrigger.Immediate,DelayTime=.1f,TargetOffset=new Vector2Int(2,0),CancelOnHole=true});
            var card=new Card("projectile","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.Fire,CardSeries.Canon,10,"検証",
                Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,modifier:modifier,startupTime:.05f);
            var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();var trace=new List<string>();
            Action<long,int> tick=(frame,ms)=>session.ProcessLogicalFrame(()=>{
                if(frame==1||frame==10||frame==30){battle.CardUsage.EnqueueCards(new List<Card>{card});session.UseCardFromInput();}
                if(frame==2||frame==13)battle.Player.TakeDamage(1,forceStagger:true);
                if(frame==14)battle.Pause(BattlePauseReason.User);
                if(frame==20)battle.Resume(BattlePauseReason.User);
                battle.TickFrame(ms);
                if(frame==9||frame==19||frame==23)Assert.AreEqual(PanelType.Grass,battle.Field.GetPanelType(center));
                if(frame==25){Assert.AreEqual(980,a.CurrentHp);Assert.AreEqual(980,b.CurrentHp);Assert.AreEqual(990,floating.CurrentHp);}
                trace.Add(frame+":"+a.CurrentHp+":"+b.CurrentHp+":"+floating.CurrentHp+":"+battle.Field.GetPanelType(center)+":"+battle.DelayedEffects.ActiveEffectCount);
            });
            long total=Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond,previous=0;
            if(stall){previous=total/2;clock.Accumulate(previous);clock.Drain(tick,3);}
            for(long render=1;previous<total;render++){
                long now=Math.Min(total,(stall?total/2:0)+render*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps);
                clock.Accumulate(now-previous);previous=now;clock.Drain(tick,3);
            }
            while(clock.PendingFrames>0)clock.Drain(tick,3);
            Assert.AreEqual(970,a.CurrentHp);Assert.AreEqual(970,b.CurrentHp);Assert.AreEqual(PanelType.Normal,battle.Field.GetPanelType(center));
            session.Detach();return trace;
        }
        [TestCase(30,false)] [TestCase(60,false)] [TestCase(120,false)]
        [TestCase(30,true)] [TestCase(60,true)] [TestCase(120,true)]
        public void 投擲燃焼は着弾時に発生し発射後中断や描画負荷で失われない(int fps,bool stall)
        {CollectionAssert.AreEqual(ProjectileFireTrace(60,false),ProjectileFireTrace(fps,stall));}

        [TestCase(false)] [TestCase(true)]
        public void 属性変更された火投擲は空マスへ着弾して燃やし穴なら取消す(bool hole)
        {
            var (battle,enemy,session)=StatusBattle();enemy.ApplyStun(60000);
            var landing=new Vector2Int(4,0);battle.Field.ChangePanelType(landing,PanelType.Grass);
            var modifier=new CardModifier{ElementOverride=ElementType.Fire};
            modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.DelayedProjectile,
                Trigger=DerivativeTrigger.Immediate,DelayTime=.05f,TargetOffset=new Vector2Int(2,0),CancelOnHole=true});
            var card=new Card("projectile","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.Water,CardSeries.Canon,10,"検証",
                Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,modifier:modifier);
            session.ProcessLogicalFrame(()=>UseStatusCard(battle,session,card));
            for(int frame=0;frame<2;frame++)session.ProcessLogicalFrame(()=>battle.TickFrame(17));
            Assert.AreEqual(PanelType.Grass,battle.Field.GetPanelType(landing));
            if(hole)battle.Field.ChangePanelType(landing,PanelType.Hole);
            session.ProcessLogicalFrame(()=>battle.TickFrame(17));
            Assert.AreEqual(hole?PanelType.Hole:PanelType.Normal,battle.Field.GetPanelType(landing));
            Assert.AreEqual(0,battle.DelayedEffects.ActiveEffectCount);
            session.Detach();
        }

        [TestCase(ElementType.Fire, PanelType.Normal)]
        [TestCase(ElementType.Water, PanelType.Grass)]
        public void 貫通は空の前方経路にも届き非火属性や穴を燃焼で変更しない(ElementType element, PanelType expected)
        {
            var player = new PlayerCharacter(100, new Vector2Int(2, 0));
            var enemy = new Enemy("behind", "検証", 100, ElementType.None, new Vector2Int(0, 0));
            var battle = new BattleSystem(player, new List<Enemy> { enemy }, new Deck());
            var session = CreateSession().session; session.Attach(battle);
            battle.Field.ChangePanelType(new Vector2Int(3, 0), PanelType.Grass);
            battle.Field.ChangePanelType(new Vector2Int(4, 0), PanelType.Hole);
            battle.Field.ChangePanelType(new Vector2Int(5, 0), PanelType.Grass);
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Penetration, Trigger = DerivativeTrigger.Immediate });
            var card = new Card("penetration", "検証", 1, CardRarity.Normal, CardCategory.Attack, element,
                CardSeries.Canon, 10, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single, modifier: modifier);
            session.ProcessLogicalFrame(() => UseStatusCard(battle, session, card));
            Assert.AreEqual(expected, battle.Field.GetPanelType(new Vector2Int(3, 0)));
            Assert.AreEqual(expected, battle.Field.GetPanelType(new Vector2Int(5, 0)));
            Assert.AreEqual(PanelType.Hole, battle.Field.GetPanelType(new Vector2Int(4, 0)));
            Assert.AreEqual(100, enemy.CurrentHp, "後方の敵は貫通対象外");
            session.Detach();
        }
        [TestCase(ChainEffectOrigin.HitPosition, 3, false)]
        [TestCase(ChainEffectOrigin.HitPosition, 3, true)]
        [TestCase(ChainEffectOrigin.HitPosition, 5, false)]
        [TestCase(ChainEffectOrigin.PlayerPosition, 3, false)]
        [TestCase(ChainEffectOrigin.PlayerPosition, 3, true)]
        [TestCase(ChainEffectOrigin.PlayerPosition, 5, false)]
        public void 通常セッションのカード使用へ派生定義を渡して発動位置を守る(ChainEffectOrigin origin, int hitX, bool synchronous)
        {
            var player = new PlayerCharacter(100, new Vector2Int(1, 0));
            var primary = new Enemy("hit", "検証", 100, ElementType.None, new Vector2Int(hitX, 0));
            var forward = new Enemy("forward", "検証", 100, ElementType.None, new Vector2Int(hitX == 5 ? 0 : 4, 0));
            var second = new Enemy("second", "検証", 100, ElementType.None, new Vector2Int(hitX == 5 ? 0 : 4, 0));
            var database = new Olca.Tests.CardEffects.MockCardDatabase();
            var battle = new BattleSystem(player, new List<Enemy> { primary, forward, second }, new Deck(), cardDatabase: database);
            var session = CreateSession().session; session.Attach(battle);
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
                battle.Field.ChangePanelType(new Vector2Int(x, 0), PanelType.Grass);
            var penetration = new CardModifier();
            penetration.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Penetration, Trigger = DerivativeTrigger.Immediate });
            var child = new Card("pierce", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire,
                CardSeries.None, 10, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single, modifier: penetration);
            database.AddCard(child);
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                ChainCardId = child.Id, Trigger = DerivativeTrigger.OnHit, EffectOrigin = origin });
            // 最寄りを親の命中対象にし、右端ケースでは他の敵を後方へ置く。
            var parent = new Card("parent", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None,
                CardSeries.None, 1, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single, modifier: modifier);
            battle.CardUsage.EnqueueCards(new List<Card> { parent });
            session.ProcessLogicalFrame(() => {
                if (synchronous) Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());
                else session.UseCardFromInput();
            });
            Assert.AreEqual(0, battle.CardUsage.RemainingCount);

            int startX = origin == ChainEffectOrigin.HitPosition ? hitX : 1;
            Assert.AreEqual(startX < forward.GridPosition.X ? 80 : 100, forward.CurrentHp);
            Assert.AreEqual(startX < forward.GridPosition.X ? 80 : 100, second.CurrentHp);
            Assert.AreEqual(origin == ChainEffectOrigin.HitPosition ? 99 : 79, primary.CurrentHp);
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
                Assert.AreEqual(x > startX ? PanelType.Normal : PanelType.Grass,
                    battle.Field.GetPanelType(new Vector2Int(x, 0)), "位置=" + x);
            Assert.AreEqual(new Vector2Int(1, 0), player.GridPosition);
            session.Detach();
        }

        [TestCase(false)] [TestCase(true)]
        public void 探索からの戦闘と保存復帰の戦闘が派生定義を通常セッションへ引き継ぐ(bool restore)
        {
            var map = DungeonConfig.CreateTestDungeon(30, 20, 5);
            var factory = new Olca.Application.Battle.SimpleCardFactory();
            var deck = new Deck(new List<Card> { factory.CreateById("sword") });
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            var database = new Olca.Tests.CardEffects.MockCardDatabase();
            var childModifier = new CardModifier();
            childModifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Penetration, Trigger = DerivativeTrigger.Immediate });
            database.AddCard(new Card("child", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire,
                CardSeries.None, 1, "検証", modifier: childModifier));
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, new PlayerCharacter(100), deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints, cardDatabase: database);
            for (int frame = 0; frame < 1000 && manager.CurrentBattle == null; frame++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle);
            if (restore)
            {
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error, cardDatabase: database), error);
                Assert.IsTrue(manager.SuspendForCheckpoint()); manager = resumed; manager.ActivateCheckpoint();
            }
            var battle = manager.CurrentBattle;
            var session = CreateSession().session; session.Attach(battle);
            if (battle.CustomScreen.IsOpen) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
            var enemy = battle.Enemies[0]; int hp = enemy.CurrentHp;
            battle.Field.ChangePanelType(enemy.GridPosition, PanelType.Grass);
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                Trigger = DerivativeTrigger.OnHit, ChainCardId = "child", EffectOrigin = ChainEffectOrigin.PlayerPosition });
            var parent = new Card("parent", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None,
                CardSeries.None, 1, "検証", modifier: modifier);
            session.ProcessLogicalFrame(() => UseStatusCard(battle, session, parent));
            Assert.AreEqual(hp - 3, enemy.CurrentHp);
            Assert.AreEqual(PanelType.Normal, battle.Field.GetPanelType(enemy.GridPosition));
            session.Detach();
        }

        [TestCase(false, false)] [TestCase(true, false)]
        [TestCase(false, true)] [TestCase(true, true)]
        public void 通常セッションの二段派生は元の発動位置を継承し未解決IDでは派生だけを省く(bool delayed, bool missing)
        {
            var primary = new Enemy("primary", "検証", 100, ElementType.None, new Vector2Int(3, 0));
            var forward = new Enemy("forward", "検証", 100, ElementType.None, new Vector2Int(4, 0));
            primary.ApplyStun(60000); forward.ApplyStun(60000);
            var database = new Olca.Tests.CardEffects.MockCardDatabase();
            var battle = new BattleSystem(new PlayerCharacter(100, new Vector2Int(1, 0)),
                new List<Enemy> { primary, forward }, new Deck(), cardDatabase: database);
            var session = CreateSession().session; session.Attach(battle);
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
                battle.Field.ChangePanelType(new Vector2Int(x, 0), PanelType.Grass);
            var finalModifier = new CardModifier();
            finalModifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Penetration, Trigger = DerivativeTrigger.Immediate });
            if (!missing) database.AddCard(new Card("final", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, 5, "検証", modifier: finalModifier));
            var middleModifier = new CardModifier();
            middleModifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                Trigger = DerivativeTrigger.OnHit, ChainCardId = "final", EffectOrigin = ChainEffectOrigin.PlayerPosition });
            database.AddCard(new Card("middle", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 1, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile,
                modifier: middleModifier, startupTime: delayed ? .05f : 0));
            var parentModifier = new CardModifier();
            parentModifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                Trigger = DerivativeTrigger.OnHit, ChainCardId = "middle", EffectOrigin = ChainEffectOrigin.HitPosition });
            var parent = new Card("parent", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 1, "検証", modifier: parentModifier);
            int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            session.ProcessLogicalFrame(() => UseStatusCard(battle, session, parent));
            if (delayed)
            {
                Assert.AreEqual(100, forward.CurrentHp);
                battle.Pause(BattlePauseReason.User);
                for (int f = 0; f < 5; f++) session.ProcessLogicalFrame(() => battle.TickFrame(17));
                Assert.AreEqual(100, forward.CurrentHp);
                battle.Resume(BattlePauseReason.User);
                for (int f = 0; f < 3; f++) session.ProcessLogicalFrame(() => battle.TickFrame(17));
            }
            Assert.AreEqual(99, primary.CurrentHp, "親の命中位置より後方へ派生を戻さない");
            Assert.AreEqual(missing ? 99 : 89, forward.CurrentHp);
            for (int x = 0; x < BattleField.GRID_WIDTH; x++)
                Assert.AreEqual(!missing && x > 3 ? PanelType.Normal : PanelType.Grass,
                    battle.Field.GetPanelType(new Vector2Int(x, 0)), "位置=" + x);
            Assert.AreEqual(1, used, "派生は追加の手札を消費しない");
            Assert.AreEqual(0, battle.CardUsage.RemainingCount);
            Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount);
            Assert.AreEqual(new Vector2Int(1, 0), battle.Player.GridPosition);
            session.Detach();
        }

        private List<string> ImmediateChainTrace(int fps, bool stall, bool restore)
        {
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                Trigger = DerivativeTrigger.Immediate, ChainCardId = "child", EffectOrigin = ChainEffectOrigin.PlayerPosition });
            var parent = new Card("parent", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None,
                CardSeries.None, 1, "検証", modifier: modifier, startupTime: .05f);
            var deck = new Deck(new List<Card> { parent });
            var map = DungeonConfig.CreateTestDungeon(30, 20, 5);
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            var database = new Olca.Tests.CardEffects.MockCardDatabase();
            database.AddCard(new Card("child", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.Fire,
                CardSeries.None, 2, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.All));
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, new PlayerCharacter(1000), deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints, cardDatabase: database);
            for (int f = 0; f < 1000 && manager.CurrentBattle == null; f++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle);
            if (restore)
            {
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error, cardDatabase: database), error);
                Assert.IsTrue(manager.SuspendForCheckpoint()); manager = resumed; manager.ActivateCheckpoint();
                parent = manager.Dungeon.GetCurrentDeck().GetAllCards()[0];
                Assert.AreEqual(DerivativeTrigger.Immediate, parent.Modifier.AdditionalEffects[0].Trigger);
            }
            var battle = manager.CurrentBattle; var session = CreateSession().session; session.Attach(battle);
            if (battle.CustomScreen.IsOpen) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
            var enemy = battle.Enemies[0]; enemy.ApplyStun(60000); int initialHp = enemy.CurrentHp;
            battle.Field.ChangePanelType(enemy.GridPosition, PanelType.Grass);
            var hits = new List<int>(); enemy.OnDamaged += (damage, multiplier) => hits.Add(damage);
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock(); var trace = new List<string>();
            Action<long, int> tick = (frame, ms) => session.ProcessLogicalFrame(() => {
                if (frame == 1 || frame == 10 || frame == 30) { battle.CardUsage.EnqueueCards(new List<Card> { parent }); session.UseCardFromInput(); }
                if (frame == 2) battle.Player.TakeDamage(1, forceStagger: true);
                if (frame == 11) battle.Pause(BattlePauseReason.User);
                if (frame == 15) battle.Resume(BattlePauseReason.User);
                battle.TickFrame(ms);
                if (frame == 9 || frame == 14) Assert.AreEqual(initialHp, enemy.CurrentHp);
                if (frame == 18) CollectionAssert.AreEqual(new[] { 4, 1 }, hits, "派生ダメージが本体より先");
                trace.Add(frame + ":" + enemy.CurrentHp + ":" + hits.Count + ":" + battle.Field.GetPanelType(enemy.GridPosition));
            });
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
            for (long render = 1; previous < total; render++) {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 3);
            CollectionAssert.AreEqual(new[] { 4, 1, 2, 1 }, hits);
            Assert.AreEqual(initialHp - 8, enemy.CurrentHp);
            Assert.AreEqual(0, battle.CardUsage.RemainingCount);
            session.Detach(); return trace;
        }
        [TestCase(30, false, false)] [TestCase(60, false, false)] [TestCase(120, false, false)]
        [TestCase(30, true, false)] [TestCase(60, true, false)] [TestCase(120, true, false)]
        [TestCase(30, false, true)] [TestCase(60, false, true)] [TestCase(120, false, true)]
        [TestCase(30, true, true)] [TestCase(60, true, true)] [TestCase(120, true, true)]
        public void 無条件派生は通常使用と保存復帰で本体より先に発動し論理フレームで一致する(int fps, bool stall, bool restore)
        { CollectionAssert.AreEqual(ImmediateChainTrace(60, false, false), ImmediateChainTrace(fps, stall, restore)); }

        [TestCase(false)] [TestCase(true)]
        public void 無条件派生は本体が空振りでも実行し未解決IDなら余分な本体ダメージを発生させない(bool missing)
        {
            var enemy = new Enemy("e", "検証", 100, ElementType.None, new Vector2Int(4, 0));
            var database = new Olca.Tests.CardEffects.MockCardDatabase();
            if (!missing) database.AddCard(new Card("child", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 7, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.All));
            var battle = new BattleSystem(new PlayerCharacter(100, new Vector2Int(1, 0)), new List<Enemy> { enemy }, new Deck(), cardDatabase: database);
            var session = CreateSession().session; session.Attach(battle);
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                Trigger = DerivativeTrigger.Immediate, ChainCardId = "child", EffectOrigin = ChainEffectOrigin.PlayerPosition });
            var parent = new Card("parent", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None,
                CardSeries.None, 1, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile, modifier: modifier);
            session.ProcessLogicalFrame(() => UseStatusCard(battle, session, parent));
            Assert.AreEqual(missing ? 100 : 93, enemy.CurrentHp);
            Assert.AreEqual(0, battle.CardUsage.RemainingCount); session.Detach();
        }

        private List<string> NearestShotTrace(int fps, bool stall, bool restore)
        {
            var card = new Card("shot", "検証射撃", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.Canon, 3, "検証", startupTime: .05f);
            var deck = new Deck(new List<Card> { card });
            var map = DungeonConfig.CreateTestDungeon(30, 1, 5);
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, new PlayerCharacter(1000), deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints);
            for (int f = 0; f < 1000 && manager.CurrentBattle == null; f++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle);
            if (restore) {
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error), error);
                Assert.IsTrue(manager.SuspendForCheckpoint()); manager = resumed; manager.ActivateCheckpoint();
                card = manager.Dungeon.GetCurrentDeck().GetAllCards()[0];
            }
            var battle = manager.CurrentBattle; var session = CreateSession().session; session.Attach(battle);
            if (battle.CustomScreen.IsOpen) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
            Assert.GreaterOrEqual(battle.Enemies.Count, 3);
            battle.Player.MoveTo(new Vector2Int(2, 0));
            foreach (var e in battle.Enemies) { e.ApplyStun(60000); e.MoveTo(new Vector2Int(0, 0)); }
            var far = battle.Enemies[0]; var first = battle.Enemies[1]; var second = battle.Enemies[2];
            far.MoveTo(new Vector2Int(5, 0)); first.MoveTo(new Vector2Int(4, 0)); second.MoveTo(new Vector2Int(4, 0));
            int farHp = far.CurrentHp, firstHp = first.CurrentHp, secondHp = second.CurrentHp;
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock(); var trace = new List<string>();
            Action<long, int> tick = (frame, ms) => session.ProcessLogicalFrame(() => {
                if (frame == 1 || frame == 10 || frame == 20 || frame == 30) { battle.CardUsage.EnqueueCards(new List<Card> { card }); session.UseCardFromInput(); }
                // 前隙中の位置変更を発動時に反映。同じマスではfirstが先に配置された敵。
                if (frame == 2) { far.MoveTo(new Vector2Int(0, 0)); first.MoveTo(new Vector2Int(3, 0)); second.MoveTo(new Vector2Int(3, 0)); battle.Pause(BattlePauseReason.User); }
                if (frame == 6) battle.Resume(BattlePauseReason.User);
                if (frame == 11) first.MoveTo(new Vector2Int(0, 0));
                if (frame == 21) second.MoveTo(new Vector2Int(0, 0));
                battle.TickFrame(ms);
                if (frame == 5) Assert.AreEqual(firstHp, first.CurrentHp);
                trace.Add(frame + ":" + far.CurrentHp + ":" + first.CurrentHp + ":" + second.CurrentHp + ":" + battle.CardUsage.RemainingCount);
            });
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
            for (long render = 1; previous < total; render++) {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 3);
            Assert.AreEqual(farHp, far.CurrentHp); Assert.AreEqual(firstHp - 3, first.CurrentHp); Assert.AreEqual(secondHp - 3, second.CurrentHp);
            Assert.AreEqual(0, battle.CardUsage.RemainingCount, "後方にしか敵がいない空振りも消費");
            session.Detach(); return trace;
        }
        [TestCase(30, false, false)] [TestCase(60, false, false)] [TestCase(120, false, false)]
        [TestCase(30, true, false)] [TestCase(60, true, false)] [TestCase(120, true, false)]
        [TestCase(30, false, true)] [TestCase(60, false, true)] [TestCase(120, false, true)]
        [TestCase(30, true, true)] [TestCase(60, true, true)] [TestCase(120, true, true)]
        public void 単体射撃は通常進行と保存復帰で発動時の最寄りを選び同じマスでは配置順を守る(int fps, bool stall, bool restore)
        { CollectionAssert.AreEqual(NearestShotTrace(60, false, false), NearestShotTrace(fps, stall, restore)); }

        private List<string> ConditionalDerivativeTrace(int fps, bool stall, bool restore, DerivativeTrigger trigger, int scenario)
        {
            // scenario: 0=空振り、1=命中してもダメージ0、2=実ダメージあり。
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.ChainCard,
                Trigger = trigger, ChainCardId = "child", EffectOrigin = ChainEffectOrigin.PlayerPosition });
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.PanelIceAll, Trigger = trigger });
            var card = new Card("conditional", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, scenario == 1 ? 0 : 3, "検証",
                Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile, modifier: modifier, startupTime: .05f);
            var database = new Olca.Tests.CardEffects.MockCardDatabase();
            database.AddCard(new Card("child", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 2, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.All));
            var deck = new Deck(new List<Card> { card });
            var map = DungeonConfig.CreateTestDungeon(30, 1, 5);
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, new PlayerCharacter(1000), deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints, cardDatabase: database);
            for (int f = 0; f < 1000 && manager.CurrentBattle == null; f++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle);
            if (restore) {
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error, cardDatabase: database), error);
                Assert.IsTrue(manager.SuspendForCheckpoint()); manager = resumed; manager.ActivateCheckpoint();
                card = manager.Dungeon.GetCurrentDeck().GetAllCards()[0];
            }
            var battle = manager.CurrentBattle; var session = CreateSession().session; session.Attach(battle);
            if (battle.CustomScreen.IsOpen) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
            battle.Player.MoveTo(new Vector2Int(2, 0));
            foreach (var e in battle.Enemies) { e.ApplyStun(60000); e.MoveTo(new Vector2Int(5, 0)); }
            var enemy = battle.Enemies[0]; enemy.MoveTo(new Vector2Int(scenario == 0 ? 4 : 3, 0));
            int initialHp = enemy.CurrentHp, used = 0;
            battle.CardUsage.OnCardUsed += _ => used++;
            var panel = new Vector2Int(1, 0); battle.Field.ChangePanelType(panel, PanelType.Normal);
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock(); var trace = new List<string>();
            Action<long, int> tick = (frame, ms) => session.ProcessLogicalFrame(() => {
                if (frame == 1) { battle.CardUsage.EnqueueCards(new List<Card> { card }); session.UseCardFromInput(); }
                if (frame == 2) battle.Pause(BattlePauseReason.User);
                if (frame == 6) battle.Resume(BattlePauseReason.User);
                battle.TickFrame(ms);
                if (frame == 5) { Assert.AreEqual(initialHp, enemy.CurrentHp); Assert.AreEqual(PanelType.Normal, battle.Field.GetPanelType(panel)); }
                trace.Add(frame + ":" + enemy.CurrentHp + ":" + battle.Field.GetPanelType(panel) + ":" + battle.CardUsage.RemainingCount);
            });
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
            for (long render = 1; previous < total; render++) {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 3);
            bool triggered = scenario != 0 && (trigger == DerivativeTrigger.OnHit || scenario == 2);
            Assert.AreEqual(initialHp - (scenario == 2 ? 3 : 0) - (triggered ? 2 : 0), enemy.CurrentHp);
            Assert.AreEqual(triggered ? PanelType.Ice : PanelType.Normal, battle.Field.GetPanelType(panel));
            Assert.AreEqual(1, used, "空振りでも親を一枚消費し、派生では追加消費しない");
            Assert.AreEqual(0, battle.CardUsage.RemainingCount);
            session.Detach(); return trace;
        }
        [TestCase(30, false, false)] [TestCase(60, false, false)] [TestCase(120, false, false)]
        [TestCase(30, true, false)] [TestCase(60, true, false)] [TestCase(120, true, false)]
        [TestCase(30, false, true)] [TestCase(60, false, true)] [TestCase(120, false, true)]
        [TestCase(30, true, true)] [TestCase(60, true, true)] [TestCase(120, true, true)]
        public void 条件付き派生は通常使用と保存復帰で命中と実ダメージを区別する(int fps, bool stall, bool restore)
        {
            foreach (var trigger in new[] { DerivativeTrigger.OnHit, DerivativeTrigger.OnDamage })
                for (int scenario = 0; scenario < 3; scenario++)
                    CollectionAssert.AreEqual(ConditionalDerivativeTrace(60, false, false, trigger, scenario),
                        ConditionalDerivativeTrace(fps, stall, restore, trigger, scenario), trigger + ":" + scenario);
        }

        private List<string> ShieldCardTrace(int fps, bool stall, bool restore, bool legacy = false)
        {
            var database = new SimpleCardFactory();
            var card = database.CreateById(SimpleCardFactory.Shield);
            var heal = database.CreateById(SimpleCardFactory.Heal);
            if (legacy) { card = LegacyDefenseCard(true); heal = LegacyDefenseCard(false); }
            var deck = new Deck(new List<Card> { card, heal });
            var map = DungeonConfig.CreateTestDungeon(30, 1, 5);
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            // 敵が移動する前の同じ固定マップから新旧の定義を構築する。
            var currentDefinitions = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,
                new Deck(new List<Card> { database.CreateById(SimpleCardFactory.Shield), database.CreateById(SimpleCardFactory.Heal) }));
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, new PlayerCharacter(1000), deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints, cardDatabase: database);
            for (int f = 0; f < 1000 && manager.CurrentBattle == null; f++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle);
            if (restore) {
                if (legacy) {
                    var current = currentDefinitions;
                    Assert.IsTrue(current.Store.TryWrite(checkpoints.Store.Read(), out var migrationError), migrationError);
                    checkpoints = current;
                }

                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error, cardDatabase: database), error);
                Assert.IsTrue(manager.SuspendForCheckpoint()); manager = resumed; manager.ActivateCheckpoint();
                card = manager.Dungeon.GetCurrentDeck().GetAllCards()[0];
                heal = manager.Dungeon.GetCurrentDeck().GetAllCards()[1];
            }
            var battle = manager.CurrentBattle; var session = CreateSession().session; session.Attach(battle);
            if (battle.CustomScreen.IsOpen) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
            Assert.AreEqual(CardSubCategory.Barrier, card.SubCategory);
            Assert.AreEqual(CardSubCategory.Heal, heal.SubCategory);
            foreach (var e in battle.Enemies) { e.ApplyStun(60000); e.MoveTo(new Vector2Int(5, 0)); }
            battle.Player.TakeDamage(100, affectsPoise: false);
            int used = 0; battle.CardUsage.OnCardUsed += _ => used++;
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock(); var trace = new List<string>();
            Action<long, int> tick = (frame, ms) => session.ProcessLogicalFrame(() => {
                if (frame == 1) battle.CardUsage.EnqueueCards(new List<Card> { card, heal });
                if (frame == 2) battle.Pause(BattlePauseReason.User);
                if (frame == 3) session.UseCardFromInput();
                if (frame == 5) { Assert.AreEqual(0, used); Assert.AreEqual(0, battle.Player.BarrierHp); }
                if (frame == 6) battle.Resume(BattlePauseReason.User);
                if (frame == 7) {
                    session.UseCardFromInput();
                    Assert.AreEqual(900, battle.Player.CurrentHp, "シールドは回復しない");
                    Assert.AreEqual(15, battle.Player.BarrierHp);
                    Assert.AreEqual(1, used);
                }
                if (frame == 10) {
                    battle.Player.TakeDamage(10, affectsPoise: false);
                    Assert.AreEqual(900, battle.Player.CurrentHp); Assert.AreEqual(5, battle.Player.BarrierHp);
                }
                if (frame == 12) {
                    battle.Player.TakeDamage(10, affectsPoise: false);
                    Assert.AreEqual(900, battle.Player.CurrentHp, "超過分5は終端で確定"); Assert.AreEqual(0, battle.Player.BarrierHp);
                }
                if (frame == 13) Assert.AreEqual(895, battle.Player.CurrentHp);
                if (frame == 15) session.UseCardFromInput();
                battle.TickFrame(ms);
                trace.Add(frame + ":" + battle.Player.CurrentHp + ":" + battle.Player.BarrierHp + ":" + used);
            });
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
            for (long render = 1; previous < total; render++) {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 3);
            Assert.AreEqual(915, battle.Player.CurrentHp, "ヒールは20回復する");
            Assert.AreEqual(0, battle.Player.BarrierHp);
            Assert.AreEqual(2, used);
            Assert.AreEqual(0, battle.CardUsage.RemainingCount);
            session.Detach(); return trace;
        }
        [TestCase(30, false, false)] [TestCase(60, false, false)] [TestCase(120, false, false)]
        [TestCase(30, true, false)] [TestCase(60, true, false)] [TestCase(120, true, false)]
        [TestCase(30, false, true)] [TestCase(60, false, true)] [TestCase(120, false, true)]
        [TestCase(30, true, true)] [TestCase(60, true, true)] [TestCase(120, true, true)]
        public void 実定義のシールドとヒールは通常使用と保存復帰で防御と回復を区別する(int fps, bool stall, bool restore)
        { CollectionAssert.AreEqual(ShieldCardTrace(60, false, false), ShieldCardTrace(fps, stall, restore)); }

        [Test]
        public void 小カテゴリはマスターと各コピーへ引き継がれIDより優先される()
        {
            var card = Card.FromMaster(new Olca.Core.Domain.Master.CardMaster { Id = "barrier_named_heal", Name = "検証",
                Category = CardCategory.Defense, SubCategory = CardSubCategory.Heal, Power = 20 });
            Assert.IsInstanceOf<Olca.Core.Logic.CardEffects.Effects.HealEffect>(Olca.Core.Logic.CardEffects.CardEffectFactory.CreateEffect(card));
            var factory = new SimpleCardFactory();
            var shield = factory.CreateById(SimpleCardFactory.Shield);
            foreach (var copy in new[] { shield.Clone(), shield.WithPowerMultiplier(2), shield.CreateSelfChainCopy(50),
                shield.ApplyTuning(new Card("t", "検証", 1, CardRarity.Normal, CardCategory.Tuning, ElementType.None, CardSeries.None, 0, "検証")),
                factory.CreateExceeded(shield, ExceedLevel.Lv1) }) {
                Assert.AreEqual(CardSubCategory.Barrier, copy.SubCategory);
                Assert.IsInstanceOf<Olca.Core.Logic.CardEffects.Effects.BarrierEffect>(Olca.Core.Logic.CardEffects.CardEffectFactory.CreateEffect(copy));
            }
            var concentrate = new Card("arbitrary", "検証", 1, CardRarity.Normal, CardCategory.Support,
                ElementType.None, CardSeries.None, 50, "検証", subCategory: CardSubCategory.Concentrate);
            Assert.IsInstanceOf<Olca.Core.Logic.CardEffects.Effects.ConcentrateEffect>(Olca.Core.Logic.CardEffects.CardEffectFactory.CreateEffect(concentrate));
        }

        private static Card LegacyDefenseCard(bool shield) => new Card(shield ? "shield" : "heal", shield ? "シールド" : "ヒール",
            shield ? 1 : 2, CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None,
            shield ? 15 : 20, shield ? "防御" : "HP回復");

        [TestCase(30, false)] [TestCase(60, false)] [TestCase(120, false)]
        [TestCase(30, true)] [TestCase(60, true)] [TestCase(120, true)]
        public void 旧シールド保存は新定義で復帰して通常使用と同じ論理フレームになる(int fps, bool stall)
        { CollectionAssert.AreEqual(ShieldCardTrace(60, false, false), ShieldCardTrace(fps, stall, true, true)); }

        [TestCase(true)] [TestCase(false)]
        public void 旧新セーブを読み込んでも原本と所持順序獲得履歴HPを変更しない(bool legacy)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-defense-compat-" + System.Guid.NewGuid().ToString("N"));
            try {
                var factory = new SimpleCardFactory();
                var map = DungeonConfig.CreateTestDungeon(30, 20, 5);
                var currentDeck = new Deck(new List<Card> { factory.CreateById("shield"), factory.CreateById("heal"), factory.CreateById("shield") });
                var oldDeck = new Deck(new List<Card> { LegacyDefenseCard(true), LegacyDefenseCard(false), LegacyDefenseCard(true) });
                var source = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, legacy ? oldDeck : currentDeck);
                var originalStore = new Olca.Unity.FileCheckpointStore(source.Definitions, directory);
                var sourceSession = new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(source.Definitions, originalStore);
                var player = new PlayerCharacter(100); player.TakeDamage(27, affectsPoise: false);
                var originalManager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, player, legacy ? oldDeck : currentDeck,
                    new MockRandomProvider(), new MockLogger(), checkpoints: sourceSession);
                var dto = originalStore.Read(); Assert.IsNotNull(dto);
                dto.Sequence++; dto.ObtainedOrder = new[] { dto.DeckOrder[1], dto.DeckOrder[0] };
                Assert.IsTrue(originalStore.TryWrite(dto, out var error), error);
                byte[] active = System.IO.File.ReadAllBytes(originalStore.ActivePath), backup = System.IO.File.ReadAllBytes(originalStore.BackupPath);
                string originalJson = UnityEngine.JsonUtility.ToJson(dto);
                var current = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, currentDeck);
                var readStore = new Olca.Unity.FileCheckpointStore(current.Definitions, directory);
                var normalized = readStore.Read(); Assert.IsNotNull(normalized, readStore.ReadError);
                var expected = dto.Copy();
                for (int i = 0; i < expected.DeckOrder.Length; i++) {
                    var record = System.Array.Find(expected.Cards, c => c.InstanceId == expected.DeckOrder[i]);
                    var canonical = current.Definitions.CaptureKnownCard(record.InstanceId, currentDeck.GetAllCards()[i]);
                    record.DefinitionKey = canonical.DefinitionKey; record.ValueHash = canonical.ValueHash;
                }
                Assert.AreEqual(UnityEngine.JsonUtility.ToJson(expected), UnityEngine.JsonUtility.ToJson(normalized));
                Assert.AreEqual(originalJson, UnityEngine.JsonUtility.ToJson(dto), "入力DTOを変更しない");
                var resumedSession = new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(current.Definitions, readStore);
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(resumedSession,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out error, cardDatabase: factory), error);
                Assert.AreEqual(73, resumed.Dungeon.Player.CurrentHp);
                CollectionAssert.AreEqual(active, System.IO.File.ReadAllBytes(originalStore.ActivePath));
                CollectionAssert.AreEqual(backup, System.IO.File.ReadAllBytes(originalStore.BackupPath));
                Assert.IsTrue(readStore.TryReadBackup(out _, out error), error);
                CollectionAssert.AreEqual(active, System.IO.File.ReadAllBytes(originalStore.ActivePath));
                // テスト専用の破損コピー。正常なactive/backupには触れない。
                string corruptDirectory = System.IO.Path.Combine(directory, "corrupt"); System.IO.Directory.CreateDirectory(corruptDirectory);
                byte[] corrupt = (byte[])active.Clone(); corrupt[0] = 0;
                var corruptStore = new Olca.Unity.FileCheckpointStore(current.Definitions, corruptDirectory);
                System.IO.File.WriteAllBytes(corruptStore.ActivePath, corrupt);
                Assert.IsNull(corruptStore.Read()); Assert.IsNotEmpty(corruptStore.ReadError);
                CollectionAssert.AreEqual(corrupt, System.IO.File.ReadAllBytes(corruptStore.ActivePath));
                CollectionAssert.AreEqual(active, System.IO.File.ReadAllBytes(originalStore.ActivePath));
                CollectionAssert.AreEqual(backup, System.IO.File.ReadAllBytes(originalStore.BackupPath));
            } finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [TestCase("hash")] [TestCase("key")] [TestCase("grant")] [TestCase("hp")]
        [TestCase("order")] [TestCase("power")] [TestCase("unknown")]
        public void 旧カードの改変や未知定義と不正状態を互換処理で許可しない(string kind)
        {
            var map = DungeonConfig.CreateTestDungeon(30, 20, 5);
            var card = kind == "power" ? new Card("shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense,
                ElementType.None, CardSeries.None, 16, "防御") : kind == "unknown" ? new Card("unknown", "シールド", 1,
                CardRarity.Normal, CardCategory.Defense, ElementType.None, CardSeries.None, 15, "防御") : LegacyDefenseCard(true);
            var deck = new Deck(new List<Card> { card });
            var source = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            new Olca.Core.Logic.Integration.DungeonBattleManager(map, new PlayerCharacter(100), deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: source);
            var dto = source.Store.Read();
            if (kind == "hash") dto.Cards[0].ValueHash = "invalid";
            if (kind == "key") dto.Cards[0].DefinitionKey = "unknown/" + dto.Cards[0].ValueHash;
            if (kind == "grant") { dto.Cards[0].TuningCount = 1; dto.Cards[0].GrantKeys = new[] { "unknown" }; }
            if (kind == "hp") dto.Player.Hp = dto.Player.MaxHp + 1;
            if (kind == "order") dto.DeckOrder = new[] { "missing" };
            string before = UnityEngine.JsonUtility.ToJson(dto);
            var current = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,
                new Deck(new List<Card> { new SimpleCardFactory().CreateById("shield") }));
            Assert.IsFalse(Olca.Core.Logic.Dungeon.CheckpointCodec.TryRestore(dto, current.Definitions, out var restored, out var error));
            Assert.IsNull(restored); Assert.IsNotEmpty(error);
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(dto));
        }

        [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)] [TestCase(true, true)]
        public void 旧実定義の回復防御カードは通常加工経路で未定義の調律超越を生成保存しない(bool shield, bool exceed)
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "olca-legacy-processing-" + System.Guid.NewGuid().ToString("N"));
            try {
                var factory = new SimpleCardFactory(); var first = LegacyDefenseCard(shield);
                var second = exceed ? LegacyDefenseCard(shield) : factory.CreateById(SimpleCardFactory.Tuning);
                var deck = new Deck(new List<Card> { first, second });
                var map = DungeonConfig.CreateTestDungeon(30, 20, 5);
                var defaults = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
                var store = new Olca.Unity.FileCheckpointStore(defaults.Definitions, directory);
                var checkpoints = new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defaults.Definitions, store);
                var currentDeck = new Deck(new List<Card> { factory.CreateById(first.Id), factory.CreateById(second.Id) });
                var current = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, currentDeck);
                var game = new Olca.Core.Logic.Integration.DungeonBattleManager(map, new PlayerCharacter(100), deck,
                    new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints,
                    exceedSystem: new ExceedSystem(factory), tuningSystem: new TuningSystem(), cardDatabase: factory);
                for (int frame = 0; frame < 1000 && game.CurrentBattle == null; frame++) game.TickFrame(17);
                Assert.IsNotNull(game.CurrentBattle);
                var screen = game.CurrentBattle.CustomScreen;
                if (!screen.IsOpen) { game.CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE); game.CurrentBattle.OpenCustomScreen(new MockRandomProvider()); }
                var materials = new List<Card>(screen.CurrentHand);
                var beforeDeck = new List<Card>(game.Dungeon.GetCurrentDeck().GetAllCards());
                Assert.AreEqual(2, materials.Count);
                byte[] active = System.IO.File.ReadAllBytes(store.ActivePath), backup = System.IO.File.ReadAllBytes(store.BackupPath);
                string before = UnityEngine.JsonUtility.ToJson(store.Read());
                var result = screen.ExecuteCustomization(materials);
                Assert.IsFalse(result.CanExecute); StringAssert.Contains("未定義", result.Reason);
                CollectionAssert.AreEqual(materials, screen.CurrentHand);
                CollectionAssert.AreEqual(beforeDeck, game.Dungeon.GetCurrentDeck().GetAllCards());
                Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(store.Read()));
                var resumedStore = new Olca.Unity.FileCheckpointStore(current.Definitions, directory);
                var resumedSession = new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(current.Definitions, resumedStore);
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(resumedSession,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error, cardDatabase: factory), error);
                Assert.AreEqual(2, resumed.Dungeon.GetCurrentDeck().TotalCount);
                foreach (var card in resumed.Dungeon.GetCurrentDeck().GetAllCards()) {
                    Assert.AreEqual(0, card.TuningCount); Assert.AreEqual(ExceedLevel.Normal, card.ExceedLevel);
                }
                CollectionAssert.AreEqual(active, System.IO.File.ReadAllBytes(store.ActivePath));
                CollectionAssert.AreEqual(backup, System.IO.File.ReadAllBytes(store.BackupPath));
            } finally { if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true); }
        }

        [TestCase(100, 30, 20, 90, false)] [TestCase(100, 30, 20, 90, true)]
        [TestCase(10, 20, 15, 5, false)] [TestCase(10, 20, 15, 5, true)]
        [TestCase(10, 20, 5, 0, false)] [TestCase(10, 20, 5, 0, true)]
        [TestCase(95, 20, 50, 100, false)] [TestCase(95, 20, 50, 100, true)]
        [TestCase(100, 70, 20, 50, false)] [TestCase(100, 70, 20, 50, true)]
        [TestCase(100, int.MaxValue, int.MaxValue, 100, false)] [TestCase(100, int.MaxValue, int.MaxValue, 100, true)]
        public void 通常カード回復と被弾はフレーム終端で一度だけ合算する(int hp, int damage, int heal, int expected, bool healFirst)
        {
            var battle = CreateBattleSystem(); var session = CreateSession().session; session.Attach(battle);
            battle.Player.SetHp(hp); int notifications = 0, deaths = 0;
            battle.Player.OnHpChanged += (_, __) => notifications++;
            battle.Player.OnDeath += () => deaths++;
            battle.CardUsage.EnqueueCards(new List<Card> { new Card("heal", "検証", 1, CardRarity.Normal,
                CardCategory.Defense, ElementType.None, CardSeries.None, heal, "検証") });
            session.ProcessLogicalFrame(() => {
                if (healFirst) session.UseCardFromInput();
                battle.Player.TakeDamage(damage);
                if (!healFirst) session.UseCardFromInput();
                Assert.AreEqual(hp, battle.Player.CurrentHp); Assert.AreEqual(0, notifications); Assert.AreEqual(0, deaths);
            });
            Assert.AreEqual(expected, battle.Player.CurrentHp); Assert.AreEqual(1, notifications);
            Assert.AreEqual(expected == 0 ? 1 : 0, deaths); Assert.AreEqual(expected == 0, battle.IsGameOver);
            session.Detach();
        }

        [Test]
        public void バリア吸収後のダメージを合算し新規無敵は次フレームから防ぐ()
        {
            var battle = CreateBattleSystem(); var session = CreateSession().session; session.Attach(battle);
            battle.Player.SetHp(50); battle.Player.SetBarrier(15);
            session.ProcessLogicalFrame(() => {
                battle.Player.TakeDamage(10, affectsPoise: false);
                battle.Player.SetInvincible(100);
                battle.Player.TakeDamage(10, affectsPoise: false);
                battle.Player.Heal(7);
            });
            Assert.AreEqual(52, battle.Player.CurrentHp); Assert.AreEqual(0, battle.Player.BarrierHp);
            session.ProcessLogicalFrame(() => { battle.Player.TakeDamage(1000); battle.Player.Heal(1); });
            Assert.AreEqual(53, battle.Player.CurrentHp); Assert.AreEqual(0, battle.Player.AccumulatedDamage);
            session.Detach();
        }

        [TestCase(false, false)] [TestCase(false, true)] [TestCase(true, false)] [TestCase(true, true)]
        public void 毒のHP1下限は同フレーム通常攻撃と分離する(bool attack, bool attackFirst)
        {
            var battle = CreateBattleSystem(); var session = CreateSession().session; session.Attach(battle);
            var enemy = battle.Enemies[0]; enemy.ApplyStun(60000);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 6000));
            int notifications = 0; enemy.OnHpChanged += (_, __) => notifications++;
            session.ProcessLogicalFrame(() => {
                if (attack && attackFirst) enemy.TakeDamage(1, ElementType.None);
                battle.TickFrame(17);
                if (attack && !attackFirst) enemy.TakeDamage(1, ElementType.None);
            });
            Assert.AreEqual(attack ? 0 : 1, enemy.CurrentHp); Assert.AreEqual(1, notifications);
            session.Detach();
        }

        [TestCase(false)] [TestCase(true)]
        public void 同時致死では両者の確定HPを通知してから敗北を判定する(bool reverse)
        {
            var battle = CreateBattleSystem(); var session = CreateSession().session; session.Attach(battle);
            int playerNotices = 0, enemyNotices = 0, ends = 0;
            battle.Player.OnHpChanged += (_, __) => { playerNotices++; Assert.AreEqual(0, battle.Enemies[0].CurrentHp); };
            battle.Enemies[0].OnHpChanged += (_, __) => { enemyNotices++; Assert.AreEqual(0, battle.Player.CurrentHp); };
            battle.OnBattleEnd += result => { ends++; Assert.AreEqual(BattleResult.Defeat, result); Assert.AreEqual(1, playerNotices); Assert.AreEqual(1, enemyNotices); };
            session.ProcessLogicalFrame(() => {
                if (reverse) battle.Enemies[0].TakeDamage(1000, ElementType.None);
                battle.Player.TakeDamage(1000, affectsPoise: false);
                if (!reverse) battle.Enemies[0].TakeDamage(1000, ElementType.None);
                Assert.AreEqual(0, ends);
            });
            Assert.AreEqual(1, ends); session.Detach();
        }

        [Test]
        public void フレーム外のHP変更は即時反映し大回復も符号反転しない()
        {
            var player = new PlayerCharacter(100); int notices = 0; player.OnHpChanged += (_, __) => notices++;
            player.TakeDamage(20, affectsPoise: false); Assert.AreEqual(80, player.CurrentHp);
            player.Heal(int.MaxValue); Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(2, notices);
        }

        private List<string> FrameHpCheckpointTrace(int fps, bool stall, bool restore)
        {
            var factory = new SimpleCardFactory(); var card = factory.CreateById("heal");
            var deck = new Deck(new List<Card> { card }); var map = DungeonConfig.CreateTestDungeon(30, 20, 5);
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, new PlayerCharacter(100), deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints, cardDatabase: factory);
            for (int frame = 0; frame < 1000 && manager.CurrentBattle == null; frame++) manager.TickFrame(17);
            if (restore) {
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error, cardDatabase: factory), error);
                Assert.IsTrue(manager.SuspendForCheckpoint()); manager = resumed; manager.ActivateCheckpoint();
                card = manager.Dungeon.GetCurrentDeck().GetAllCards()[0];
            }
            var battle = manager.CurrentBattle; var session = CreateSession().session; session.Attach(battle);
            if (battle.CustomScreen.IsOpen) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
            foreach (var e in battle.Enemies) e.ApplyStun(60000);
            battle.Player.SetHp(10); var trace = new List<string>(); int notices = 0, endHp = -1;
            battle.Player.OnHpChanged += (_, __) => notices++;
            battle.OnBattleEnd += result => { Assert.AreEqual(BattleResult.Victory, result); endHp = battle.Player.CurrentHp; };
            manager.OnCardRewardPresented += _ => manager.Dungeon.OnCardSelectionCompleted(null);
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock();
            Action<long, int> tick = (frame, ms) => {
                session.ProcessLogicalFrame(() => {
                    if (frame == 1) battle.CardUsage.EnqueueCards(new List<Card> { card });
                    if (frame == 2) battle.Pause(BattlePauseReason.User);
                    if (frame == 5) battle.Resume(BattlePauseReason.User);
                    if (frame == 6) {
                        battle.Player.TakeDamage(25, affectsPoise: false); session.UseCardFromInput();
                        foreach (var e in battle.Enemies) e.TakeDamage(e.MaxHp, ElementType.None);
                    }
                    battle.TickFrame(ms);
                });
                trace.Add(frame + ":" + battle.Player.CurrentHp + ":" + notices + ":" + battle.IsGameOver);
            };
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
            for (long render = 1; previous < total; render++) {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 3);
            Assert.AreEqual(5, endHp); Assert.AreEqual(1, notices); Assert.AreEqual(5, checkpoints.Store.Read().Player.Hp);
            Assert.IsFalse(manager.Dungeon.IsCheckpointBlocked, manager.Dungeon.CheckpointError);
            Assert.IsTrue(checkpoints.TryRead(out var saved, out var readError), readError); Assert.AreEqual(5, saved.Player.CurrentHp);
            session.Detach(); return trace;
        }
        [TestCase(30, false, false)] [TestCase(60, false, false)] [TestCase(120, false, false)]
        [TestCase(30, true, false)] [TestCase(60, true, false)] [TestCase(120, true, false)]
        [TestCase(30, false, true)] [TestCase(60, false, true)] [TestCase(120, false, true)]
        [TestCase(30, true, true)] [TestCase(60, true, true)] [TestCase(120, true, true)]
        public void HP合算は通常使用と保存復帰でフレーム一致し確定HPだけを保存する(int fps, bool stall, bool restore)
        { CollectionAssert.AreEqual(FrameHpCheckpointTrace(60, false, false), FrameHpCheckpointTrace(fps, stall, restore)); }

        [Test]
        public void フレーム外のHP通知内反撃で敵が死亡しても死亡通知を重複しない()
        {
            var enemy = new Enemy("e", "検証", 100, ElementType.None, new Vector2Int(3, 0));
            bool countered = false; int deaths = 0;
            enemy.OnHpChanged += (_, __) => { if (!countered) { countered = true; enemy.TakeDamage(100, ElementType.None); } };
            enemy.OnDeath += () => deaths++;
            enemy.TakeDamage(1, ElementType.None);
            Assert.AreEqual(0, enemy.CurrentHp); Assert.AreEqual(1, deaths);
        }

        [Test]
        public void HP1で毒だけを受けるフレームには余分な命中通知を出さない()
        {
            var battle = CreateBattleSystem(); var enemy = battle.Enemies[0]; enemy.ApplyStun(60000);
            enemy.TakeDamage(enemy.MaxHp - 1, ElementType.None);
            enemy.ApplyStatus(new StatusEffect(StatusEffectType.Poison, 1000, 6000));
            int hits = 0, notices = 0;
            enemy.OnDamaged += (_, __) => hits++; enemy.OnHpChanged += (_, __) => notices++;
            battle.TickFrame(17);
            Assert.AreEqual(1, enemy.CurrentHp); Assert.AreEqual(0, hits); Assert.AreEqual(0, notices);
        }

        private List<string> InvincibilityDurationTrace(int fps, bool stall, int durationMs, int durationFrames, bool afterTick)
        {
            var (battle, enemy, session) = StatusBattle(); enemy.ApplyStun(60000);
            var trace = new List<string>(); var clock = new Olca.Core.Logic.Integration.LogicalFrameClock();
            int hp = battle.Player.CurrentHp;
            Action<long, int> tick = (frame, ms) => {
                bool protectedFrame = frame >= 2 && frame <= durationFrames + 1;
                session.ProcessLogicalFrame(() => {
                    if (frame == 1 && !afterTick) battle.Player.SetInvincible(durationMs);
                    Assert.AreEqual(protectedFrame, battle.Player.IsInvincible, "入力段の防御: " + frame);
                    if (frame <= durationFrames + 3) battle.Player.TakeDamage(1, affectsPoise: false);
                    battle.TickFrame(ms);
                    if (frame == 1 && afterTick) battle.Player.SetInvincible(durationMs);
                    Assert.AreEqual(protectedFrame, battle.Player.IsInvincible, "Tick後も同じ防御: " + frame);
                    if (frame <= durationFrames + 3) battle.Player.TakeDamage(1, affectsPoise: false);
                });
                if (!protectedFrame && frame <= durationFrames + 3) hp -= 2;
                Assert.AreEqual(hp, battle.Player.CurrentHp, "終端HP: " + frame);
                Assert.AreEqual(frame <= durationFrames, battle.Player.IsInvincible, "終端残期間: " + frame);
                trace.Add(frame + ":" + hp + ":" + battle.Player.IsInvincible);
            };
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
            for (long render = 1; previous < total; render++) {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 3);
            session.Detach(); return trace;
        }
        [TestCase(30, false)] [TestCase(60, false)] [TestCase(120, false)]
        [TestCase(30, true)] [TestCase(60, true)] [TestCase(120, true)]
        public void 新規無敵は入力とTickの順序によらず翌フレームから指定期間を守る(int fps, bool stall)
        {
            foreach (var duration in new[] { (ms: 16, frames: 1), (ms: 50, frames: 3), (ms: 100, frames: 6) })
                foreach (bool afterTick in new[] { false, true })
                    CollectionAssert.AreEqual(InvincibilityDurationTrace(60, false, duration.ms, duration.frames, false),
                        InvincibilityDurationTrace(fps, stall, duration.ms, duration.frames, afterTick));
        }

        [Test]
        public void 通常カードの命中通知から再入した被弾と回復も同じHP確定へ含める()
        {
            var battle = CreateBattleSystem(); var session = CreateSession().session; session.Attach(battle);
            battle.Player.SetHp(10); int notices = 0, deaths = 0;
            battle.Player.OnHpChanged += (_, __) => notices++; battle.Player.OnDeath += () => deaths++;
            battle.Enemies[0].OnDamaged += (_, __) => { battle.Player.TakeDamage(20, affectsPoise: false); battle.Player.Heal(15); };
            battle.CardUsage.EnqueueCards(new List<Card> { new Card("attack", "検証", 1, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.None, 1, "検証") });
            session.ProcessLogicalFrame(() => { session.UseCardFromInput(); battle.TickFrame(17); Assert.AreEqual(10, battle.Player.CurrentHp); });
            Assert.AreEqual(5, battle.Player.CurrentHp); Assert.AreEqual(1, notices); Assert.AreEqual(0, deaths);
            Assert.AreEqual(49, battle.Enemies[0].CurrentHp); session.Detach();
        }

        [TestCase(30, 20, 0, 0, 1)] [TestCase(20, 30, 0, 0, 1)]
        [TestCase(10, 15, 0, 25, 0)] [TestCase(15, 10, 0, 25, 0)]
        [TestCase(5, 10, 20, 0, 1)] [TestCase(0, 0, 10, 10, 0)]
        public void 強靭度削りは通常フレーム終端で合算し一度判定する(int first, int second, int initial, int expected, int staggers)
        {
            var battle = CreateBattleSystem(); var session = CreateSession().session; session.Attach(battle);
            battle.Enemies[0].ApplyStun(60000);
            if (initial > 0) battle.Player.TakeDamage(initial);
            int count = 0; battle.Player.OnStaggered += () => { count++; Assert.AreEqual(0, battle.Player.AccumulatedDamage); };
            session.ProcessLogicalFrame(() => {
                battle.Player.TakeDamage(first); battle.Player.TakeDamage(second);
                Assert.AreEqual(initial, battle.Player.AccumulatedDamage, "途中では蓄積を確定・リセットしない");
                Assert.AreEqual(0, count);
                battle.TickFrame(17);
            });
            Assert.AreEqual(expected, battle.Player.AccumulatedDamage); Assert.AreEqual(staggers, count);
            Assert.AreEqual(100 - initial - first - second, battle.Player.CurrentHp);
            session.Detach();
        }

        private List<string> ResistanceCardTrace(int fps, bool stall, bool restore, int resistance, int power)
        {
            var modifier = new CardModifier();
            modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.Poison,
                Trigger = DerivativeTrigger.OnHit, Duration = 10f, Power = 1 });
            var card = new Card("resistance", "検証", 1, CardRarity.Normal, CardCategory.Attack,
                ElementType.Fire, CardSeries.None, power, "検証",
                Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile, modifier: modifier, startupTime: .05f);
            var deck = new Deck(new List<Card> { card });
            var map = DungeonConfig.CreateTestDungeon(30, 1, 5);
            for (int i = 0; i < map.MapCells.Count; i++) {
                var cell = map.MapCells[i]; if (cell.Enemy == null) continue;
                var e = cell.Enemy;
                map.MapCells[i] = new MapCell(i, new Enemy(e.EnemyId, e.Name, e.MaxHp, e.Element, e.GridPosition,
                    resistances: new ElementResistances(fire: resistance)), cell.Height);
            }
            var player = new PlayerCharacter(1000); player.SetElementResistances(new ElementResistances(fire: -200, water: 150));
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, player, deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints);
            for (int f = 0; f < 1000 && manager.CurrentBattle == null; f++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle);
            if (restore) {
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error), error);
                Assert.IsTrue(manager.SuspendForCheckpoint()); manager = resumed; manager.ActivateCheckpoint();
                card = manager.Dungeon.GetCurrentDeck().GetAllCards()[0];
            }
            var battle = manager.CurrentBattle; var session = CreateSession().session; session.Attach(battle);
            if (battle.CustomScreen.IsOpen) { battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); }
            battle.Player.MoveTo(new Vector2Int(2, 0));
            foreach (var e in battle.Enemies) { e.ApplyStun(60000); e.MoveTo(new Vector2Int(5, 0)); }
            var enemy = battle.Enemies[0]; enemy.MoveTo(new Vector2Int(3, 0));
            int initialHp = enemy.CurrentHp, used = 0;
            battle.CardUsage.OnCardUsed += _ => used++;
            Assert.AreEqual(-200, battle.Player.Resistances.Fire, "旧保存の元値を維持");
            Assert.AreEqual(-100, battle.Player.Resistances.Get(ElementType.Fire));
            Assert.AreEqual(100, battle.Player.Resistances.Get(ElementType.Water));
            Assert.AreEqual(100, checkpoints.Store.Read().Player.WaterResistance);
            Assert.AreEqual(-200, checkpoints.Store.Read().Player.FireResistance);
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock(); var trace = new List<string>();
            Action<long, int> tick = (frame, ms) => session.ProcessLogicalFrame(() => {
                if (frame == 1) { battle.CardUsage.EnqueueCards(new List<Card> { card }); session.UseCardFromInput(); }
                if (frame == 2) battle.Pause(BattlePauseReason.User);
                if (frame == 6) battle.Resume(BattlePauseReason.User);
                battle.TickFrame(ms);
                if (frame == 5) { Assert.AreEqual(initialHp, enemy.CurrentHp); Assert.IsNull(enemy.ActiveStatus); }
                trace.Add(frame + ":" + enemy.CurrentHp + ":" + enemy.ActiveStatus?.Type.ToString() + ":" + battle.CardUsage.RemainingCount);
            });
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
            for (long render = 1; previous < total; render++) {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 3);
            int damage = power == 0 || resistance >= 100 ? 0 : resistance < 0 ? 2 : 1;
            Assert.AreEqual(initialHp - damage, enemy.CurrentHp);
            Assert.AreEqual(power == 0 || resistance < 100, enemy.ActiveStatus != null);
            Assert.AreEqual(1, used, "空振りでも親を一枚消費し、派生では追加消費しない");
            Assert.AreEqual(0, battle.CardUsage.RemainingCount);
            session.Detach(); return trace;
        }
        [TestCase(30, false, false)] [TestCase(60, false, false)] [TestCase(120, false, false)]
        [TestCase(30, true, false)] [TestCase(60, true, false)] [TestCase(120, true, false)]
        [TestCase(30, false, true)] [TestCase(60, false, true)] [TestCase(120, false, true)]
        [TestCase(30, true, true)] [TestCase(60, true, true)] [TestCase(120, true, true)]
        public void 耐性の下限最低ダメージ完全無効と状態専用を通常使用と保存復帰で区別する(int fps, bool stall, bool restore)
        {
            foreach (var value in new[] { (r: 99, p: 1), (r: 100, p: 1), (r: 100, p: 0), (r: -200, p: 1) })
                CollectionAssert.AreEqual(ResistanceCardTrace(60, false, false, value.r, value.p),
                    ResistanceCardTrace(fps, stall, restore, value.r, value.p));
        }

        private List<string> TransformationImmunityTrace(int fps, bool stall, bool restore, int resistance, bool invincible)
        {
            var map = DungeonConfig.CreateTestDungeon(8);
            var deck = new Deck();
            var player = new PlayerCharacter(100); player.SetElementResistances(new ElementResistances(fire: -100));
            var checkpoints = Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map, deck);
            var manager = new Olca.Core.Logic.Integration.DungeonBattleManager(map, player, deck,
                new MockRandomProvider(), new MockLogger(), checkpoints: checkpoints);
            for (int i = 0; i < 100 && manager.CurrentBattle == null; i++) manager.TickFrame(17);
            Assert.IsNotNull(manager.CurrentBattle);
            var saved = checkpoints.Store.Read(); Assert.AreEqual(1, saved.SchemaVersion);
            Assert.AreEqual(CheckpointBoundary.BattleStart, saved.Boundary);
            if (restore)
            {
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(checkpoints,
                    new MockRandomProvider(), new MockLogger(), out var resumed, out var error), error);
                Assert.IsTrue(manager.SuspendForCheckpoint()); manager = resumed; manager.ActivateCheckpoint();
            }
            var battle = manager.CurrentBattle; player = battle.Player;
            var session = CreateSession().session; session.Attach(battle);
            // 保存済み戦闘開始からも、通常の変身選択・確定をやり直す。変身中セーブは追加しない。
            battle.Transformations.Configure(new[] { new TransformationDefenseDefinition("immune-fixture",
                new ElementResistances(fire: resistance), ElementType.Fire) });
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            session.TryOpenCustomScreen();
            Assert.IsTrue(battle.Transformations.TryPreview("immune-fixture"));
            battle.CustomScreen.Confirm(); battle.CustomScreen.Close(); battle.Resume();
            Assert.AreEqual("immune-fixture", battle.Transformations.ActiveId);
            player.MoveTo(new Vector2Int(2, 0));
            foreach (var enemy in battle.Enemies) { enemy.ApplyStun(60000); enemy.MoveTo(new Vector2Int(5, 0)); }
            // 属性攻撃を持つ検証用召喚物を、既存の登録APIから通常AIへ接続する。
            // 同フレーム2発が、完全無効の変身を解除して後段へ基礎耐性を漏らさないことも確認。
            for (int i = 0; i < 2; i++) battle.AddSummon(
                new Enemy("fire-fixture-" + i, "検証", 10, ElementType.Fire, new Vector2Int(3, 0), isSummoned: true),
                new List<IEnemyAIAction> { new BasicAttackAction(10, 100000, ElementType.Fire) });
            int ended = 0; player.OnTransformationDefenseEnded += () => ended++;
            if (invincible) player.SetInvincible(120000);
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock(); var trace = new List<string>();
            Action<long, int> tick = (frame, ms) => session.ProcessLogicalFrame(() => {
                if (frame == 1) battle.Pause(BattlePauseReason.User);
                if (frame == 6) battle.Resume(BattlePauseReason.User);
                manager.TickFrame(ms);
                if (frame <= 5) { Assert.AreEqual(100, player.CurrentHp); Assert.AreEqual(0, ended); }
                trace.Add(frame + ":" + player.CurrentHp + ":" + battle.Transformations.ActiveId + ":" +
                    player.RemainingTransformationPhases + ":" + player.AccumulatedDamage + ":" + ended);
            });
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond, previous = 0;
            if (stall) { previous = total / 2; clock.Accumulate(previous); clock.Drain(tick, 3); }
            for (long render = 1; previous < total; render++) {
                long now = Math.Min(total, (stall ? total / 2 : 0) + render * total / fps);
                clock.Accumulate(now - previous); previous = now; clock.Drain(tick, 3);
            }
            while (clock.PendingFrames > 0) clock.Drain(tick, 3);
            bool preserved = resistance >= 100 || invincible;
            Assert.AreEqual(preserved ? 100 : resistance == 99 ? 79 : 75, player.CurrentHp);
            Assert.AreEqual(preserved ? 0 : 1, ended);
            Assert.AreEqual(preserved, player.HasTransformationDefense);
            Assert.AreEqual(preserved ? 3 : 0, player.RemainingTransformationPhases);
            Assert.AreEqual(preserved ? "immune-fixture" : null, battle.Transformations.ActiveId);
            if (preserved) Assert.AreEqual(0, player.AccumulatedDamage);
            Assert.IsTrue(battle.Transformations.IsUsed("immune-fixture"));
            // 完全無効でも使用済みや寿命をリセットせず、3回の通常カスタム再開で終了する。
            for (int i = 0; i < 3; i++) {
                battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE); battle.OpenCustomScreen(new MockRandomProvider());
                Assert.IsFalse(battle.Transformations.TryPreview("immune-fixture"));
                battle.CustomScreen.Confirm(); battle.CustomScreen.Close();
            }
            Assert.IsFalse(player.HasTransformationDefense); Assert.AreEqual(1, ended);
            Assert.AreEqual(-100, player.ActiveResistances.Fire);
            var after = checkpoints.Store.Read();
            Assert.AreEqual(saved.SchemaVersion, after.SchemaVersion); Assert.AreEqual(saved.Sequence, after.Sequence);
            Assert.AreEqual(saved.RunId, after.RunId); Assert.AreEqual(saved.MapHash, after.MapHash);
            Assert.AreEqual(saved.Player.FireResistance, after.Player.FireResistance);
            CollectionAssert.AreEqual(saved.DeckOrder, after.DeckOrder);
            session.Detach(); return trace;
        }

        [TestCase(30, false, false)] [TestCase(60, false, false)] [TestCase(120, false, false)]
        [TestCase(30, true, false)] [TestCase(60, true, false)] [TestCase(120, true, false)]
        [TestCase(30, false, true)] [TestCase(60, false, true)] [TestCase(120, false, true)]
        [TestCase(30, true, true)] [TestCase(60, true, true)] [TestCase(120, true, true)]
        public void 変身弱点の完全無効は通常敵AIと保存復帰で維持され論理フレームも一致する(int fps, bool stall, bool restore)
        {
            foreach (var value in new[] { (resistance: 99, invincible: false), (resistance: 100, invincible: false),
                (resistance: 150, invincible: false), (resistance: 50, invincible: true) })
                CollectionAssert.AreEqual(TransformationImmunityTrace(60, false, false, value.resistance, value.invincible),
                    TransformationImmunityTrace(fps, stall, restore, value.resistance, value.invincible));
        }

        [Test]
        public void 耐性補正は全て加算してから制限し無属性は補正しない()
        {
            var values = ElementResistances.Combine(new ElementResistances(fire: 80, water: -80, wind: int.MaxValue),
                new ElementResistances(fire: 80, water: -80, wind: int.MaxValue), new ElementResistances(fire: -70, wind: int.MinValue));
            Assert.AreEqual(90, values.Get(ElementType.Fire)); Assert.AreEqual(-100, values.Get(ElementType.Water));
            Assert.AreEqual(100, values.Get(ElementType.Wind)); Assert.AreEqual(1, values.CalculateDamage(1, ElementType.Fire, false));
            Assert.AreEqual(5, values.CalculateDamage(5, ElementType.None, false));
            var battle = CreateBattleSystem(); battle.Player.SetElementResistances(values);
            var session = CreateSession().session; session.Attach(battle);
            int staggers = 0; battle.Player.OnStaggered += () => staggers++;
            session.ProcessLogicalFrame(() => {
                battle.Player.TakeDamage(1, forceStagger: true, attackElement: ElementType.Wind);
                battle.Player.TakeDamage(1, affectsPoise: false, attackElement: ElementType.Fire);
            });
            Assert.AreEqual(99, battle.Player.CurrentHp); Assert.AreEqual(0, staggers);
            session.Detach();
        }


    }
}
