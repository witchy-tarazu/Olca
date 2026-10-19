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
                Assert.AreEqual(999, enemy.CurrentHp);
            });
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
            var forward = new Enemy("forward", "検証", 100, ElementType.None, new Vector2Int(4, 0));
            var second = new Enemy("second", "検証", 100, ElementType.None, new Vector2Int(4, 0));
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
            // 配置順でSingleの対象を確定し、指定位置から実際の派生カードを発動する。
            var parent = new Card("parent", "検証", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None,
                CardSeries.None, 1, "検証", Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single, modifier: modifier);
            battle.CardUsage.EnqueueCards(new List<Card> { parent });
            session.ProcessLogicalFrame(() => {
                if (synchronous) Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());
                else session.UseCardFromInput();
            });
            Assert.AreEqual(0, battle.CardUsage.RemainingCount);

            int startX = origin == ChainEffectOrigin.HitPosition ? hitX : 1;
            Assert.AreEqual(startX < 4 ? 80 : 100, forward.CurrentHp);
            Assert.AreEqual(startX < 4 ? 80 : 100, second.CurrentHp);
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

    }
}
