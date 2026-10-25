using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;
using System.Collections.Generic;

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class EnemyAISystemTests
    {
        private EnemyAISystem aiSystem;
        private PlayerCharacter player;
        private BattleField field;
        private MockRandomProvider random;
        private MockLogger logger;

        [SetUp]
        public void SetUp()
        {
            logger = new MockLogger();
            aiSystem = new EnemyAISystem(logger);
            player = new PlayerCharacter(100, new Vector2Int(2, 0));
            field = new BattleField();
            random = new MockRandomProvider();
        }

        private sealed class SequenceAction : IEnemyAIAction
        {
            public string Id;public bool Enabled=true;public int Wait;public int Checks;public System.Action Run;
            public bool CanExecute(AIContext context){Checks++;return Enabled;}
            public void Execute(AIContext context)=>Run?.Invoke();
            public int GetCooldownMs()=>Wait;public float GetCooldown()=>Wait/1000f;
        }
        [Test]
        public void 行動を順次実行し不可行動は次へ進めて一更新に一つだけ完了する()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var trace=new List<string>();
            var a=new SequenceAction{Id="a",Run=()=>trace.Add("a")};var skip=new SequenceAction{Enabled=false};var b=new SequenceAction{Id="b",Run=()=>trace.Add("b")};var source=new List<IEnemyAIAction>{a,skip,b};var ai=new EnemyAIBehavior(e,source);source.Clear();var context=new AIContext(e,player,field,random,logger);
            for(int i=0;i<4;i++){ai.Update(0,context);Assert.AreEqual(i+1,trace.Count);}CollectionAssert.AreEqual(new[]{"a","b","a","b"},trace);Assert.AreEqual(2,skip.Checks);
        }
        [Test]
        public void 全行動不可でも一巡で返り待ち時間を作らず次更新で再判定する()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var a=new SequenceAction{Enabled=false};var b=new SequenceAction{Enabled=false};var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{a,b});var context=new AIContext(e,player,field,random,logger);
            ai.Update(0,context);Assert.AreEqual(1,a.Checks);Assert.AreEqual(1,b.Checks);Assert.AreEqual(0,ai.CurrentCooldownMs);b.Enabled=true;int executed=0;b.Run=()=>executed++;ai.Update(0,context);Assert.AreEqual(1,executed);Assert.AreEqual(2,a.Checks);Assert.AreEqual(2,b.Checks);
        }
        [Test]
        public void 同期行動が完了してから次へ進め既存の行動後時計を敵ごとに維持する()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var trace=new List<string>();var a=new SequenceAction{Wait=100,Run=()=>{trace.Add("a-start");trace.Add("a-end");}};var b=new SequenceAction{Run=()=>trace.Add("b")};var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{a,b});var other=new EnemyAIBehavior(e,new List<IEnemyAIAction>{new SequenceAction()});var context=new AIContext(e,player,field,random,logger);
            ai.Update(0,context);Assert.AreEqual(100,ai.CurrentCooldownMs);Assert.AreEqual(0,other.CurrentCooldownMs);ai.Update(99,context);CollectionAssert.AreEqual(new[]{"a-start","a-end"},trace);Assert.AreEqual(1,ai.CurrentCooldownMs);ai.Update(1,context);CollectionAssert.AreEqual(new[]{"a-start","a-end","b"},trace);
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void 順次スキップと既存時計のトレースは描画分割によらず一致する(int fps)
        {
            string Replay(int renderFps){var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var trace=new List<string>();var a=new SequenceAction{Wait=100,Run=()=>trace.Add("a")};var b=new SequenceAction{Wait=50,Run=()=>trace.Add("b")};var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{a,new SequenceAction{Enabled=false},b});var context=new AIContext(e,player,field,random,logger);var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();long previous=0;for(int i=1;i<=renderFps;i++){long now=i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/renderFps;clock.Accumulate(now-previous);previous=now;clock.Drain((frame,ms)=>{if(frame==20)Assert.IsTrue(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{new SequenceAction{Wait=33,Run=()=>trace.Add("new")}}));ai.Update(ms,context,true);},3);}while(clock.PendingFrames>0)clock.Drain((frame,ms)=>{if(frame==20)Assert.IsTrue(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{new SequenceAction{Wait=33,Run=()=>trace.Add("new")}}));ai.Update(ms,context,true);},3);return string.Join(",",trace)+":"+ai.CurrentCooldownMs;}
            Assert.AreEqual(Replay(60),Replay(fps));
        }
        [Test]
        public void 空または欠落行動を生成時に拒否する()
        {var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));Assert.Throws<System.ArgumentException>(()=>new EnemyAIBehavior(e,new List<IEnemyAIAction>()));Assert.Throws<System.ArgumentException>(()=>new EnemyAIBehavior(e,new List<IEnemyAIAction>{null}));}
        [Test]
        public void 行動中の切替要求は完了後に適用し旧待ちを引継がず新行動の待ちだけ使う()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var context=new AIContext(e,player,field,random,logger);var trace=new List<string>();EnemyAIBehavior ai=null;
            var next=new SequenceAction{Wait=70,Run=()=>trace.Add("new")};var source=new List<IEnemyAIAction>{next};var old=new SequenceAction{Wait=1000,Run=()=>{trace.Add("old-start");Assert.IsTrue(ai.TrySwitchActionPattern(source));source.Clear();Assert.IsFalse(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{new SequenceAction()}));trace.Add("old-end");}};ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{old});
            ai.Update(0,context);CollectionAssert.AreEqual(new[]{"old-start","old-end"},trace);Assert.AreEqual(0,ai.CurrentCooldownMs);ai.Update(0,context);CollectionAssert.AreEqual(new[]{"old-start","old-end","new"},trace);Assert.AreEqual(70,ai.CurrentCooldownMs);ai.Update(69,context);Assert.AreEqual(3,trace.Count);ai.Update(1,context);Assert.AreEqual(4,trace.Count);
        }
        [Test]
        public void 行動後の待ち中に切替条件が成立したら旧待ちを破棄し新AIに余計な待機を挿入しない()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{new SequenceAction{Wait=2000}});var context=new AIContext(e,player,field,random,logger);ai.Update(0,context);ai.Update(500,context);Assert.AreEqual(1500,ai.CurrentCooldownMs);int called=0;
            Assert.IsTrue(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{new SequenceAction{Wait=300,Run=()=>called++}}));Assert.AreEqual(0,ai.CurrentCooldownMs);ai.Update(0,context);Assert.AreEqual(1,called);Assert.AreEqual(300,ai.CurrentCooldownMs);
        }
        [Test]
        public void 死亡未登録と不正な切替定義を拒否し既存行動と時計を壊さない()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{new SequenceAction{Wait=100}});var context=new AIContext(e,player,field,random,logger);ai.Update(0,context);
            Assert.Throws<System.ArgumentException>(()=>ai.TrySwitchActionPattern(new List<IEnemyAIAction>()));Assert.Throws<System.ArgumentException>(()=>ai.TrySwitchActionPattern(new List<IEnemyAIAction>{null}));Assert.AreEqual(100,ai.CurrentCooldownMs);Assert.IsFalse(aiSystem.TrySwitchActionPattern(e,new List<IEnemyAIAction>{new SequenceAction()}));e.TakeDamage(100,ElementType.None);Assert.IsFalse(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{new SequenceAction()}));
        }
        [Test]
        public void 麻痺による全体行動禁止を行動不可skipで迂回せず切替後も行動しない()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var action=new SequenceAction();int called=0;action.Run=()=>called++;var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{action});var context=new AIContext(e,player,field,random,logger);e.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000));Assert.IsTrue(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{action}));ai.Update(100,context);Assert.AreEqual(0,called);Assert.AreEqual(0,action.Checks);e.ClearStatus(StatusEffectType.Paralyze);ai.Update(0,context);Assert.AreEqual(1,called);
        }
        [TestCase(BattlePauseReason.User)] [TestCase(BattlePauseReason.CustomScreen)]
        public void 戦闘の停止中に切替要求を解決しても勝手に再開せず新行動の実行を停止する(BattlePauseReason reason)
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var b=new BattleSystem(player,new List<Enemy>{e},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:random);int called=0;b.Pause(reason);Assert.IsTrue(b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{new SequenceAction{Run=()=>called++}}));b.TickFrame(17);Assert.IsTrue(b.IsPaused);Assert.AreEqual(0,called);b.Resume(reason);b.TickFrame(17);Assert.AreEqual(1,called);
            b.RunLogicalFrame(()=>e.TakeDamage(10000,ElementType.None));Assert.IsFalse(b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{new SequenceAction()}));
        }
        [Test]
        public void 暗転とヒットストップは切替で解除せず終了後だけ新AIを実行する()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var b=new BattleSystem(player,new List<Enemy>{e},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:random);int called=0;Assert.IsTrue(b.TryStartDarkeningEffect(DarkeningSide.Opponent,0,3,()=>{}));Assert.IsTrue(b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{new SequenceAction{Run=()=>called++}}));while(b.IsDarkening){b.TickFrame(17);Assert.AreEqual(0,called);}b.RunLogicalFrame(()=>e.TakeDamage(1,ElementType.None,hitStopFrames:3));Assert.IsTrue(b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{new SequenceAction{Run=()=>called++}}));while(b.IsHitStopped){b.TickFrame(17);Assert.AreEqual(0,called);}b.TickFrame(17);Assert.AreEqual(1,called);
        }
        private sealed class ContinuingAction : IContinuingEnemyAIAction
        {
            public int Frames=3,Remaining,Starts,Ticks,Cancels,Wait=200;public System.Action Started,Advanced,Cancelled;
            public bool IsRunning{get;private set;}
            public bool CanExecute(AIContext context)=>true;
            public void Execute(AIContext context){Starts++;Remaining=Frames;IsRunning=Frames>0;Started?.Invoke();}
            public void Tick(int ms,AIContext context,bool logical){Ticks++;if(--Remaining<=0)IsRunning=false;Advanced?.Invoke();}
            public void Cancel(AIContext context){Cancels++;IsRunning=false;Cancelled?.Invoke();}
            public int GetCooldownMs()=>Wait;public float GetCooldown()=>Wait/1000f;
        }
        [Test]
        public void 継続行動の完了まで切替を保持し終了フレームには次行動を実行しない()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var action=new ContinuingAction();var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{action});var context=new AIContext(e,player,field,random,logger);int next=0;
            ai.Update(17,context,true);Assert.IsTrue(action.IsRunning);Assert.IsFalse(ai.CanAct);Assert.IsTrue(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{new SequenceAction{Wait=70,Run=()=>next++}}));
            for(int i=0;i<3;i++){ai.Update(17,context,true);Assert.AreEqual(0,next);}Assert.IsFalse(action.IsRunning);Assert.AreEqual(0,ai.CurrentCooldownMs);Assert.AreEqual(1,action.Starts);Assert.AreEqual(3,action.Ticks);ai.Update(17,context,true);Assert.AreEqual(1,next);Assert.AreEqual(70,ai.CurrentCooldownMs);
        }
        [Test]
        public void 切替しない継続行動は完了時からだけ元行動の待ちを開始する()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var a=new ContinuingAction{Frames=2,Wait=100};var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{a});var context=new AIContext(e,player,field,random,logger);ai.Update(0,context);ai.Update(0,context);Assert.AreEqual(0,ai.CurrentCooldownMs);ai.Update(0,context);Assert.AreEqual(100,ai.CurrentCooldownMs);ai.Update(99,context);Assert.AreEqual(1,a.Starts);ai.Update(1,context);Assert.AreEqual(2,a.Starts);
        }
        [Test]
        public void Tick内の完了と切替要求も旧待ちを入れず次更新で新行動を開始する()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var a=new ContinuingAction{Frames=1};var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{a});var context=new AIContext(e,player,field,random,logger);int next=0;a.Advanced=()=>Assert.IsTrue(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{new SequenceAction{Run=()=>next++}}));ai.Update(0,context);ai.Update(0,context);Assert.AreEqual(0,next);ai.Update(0,context);Assert.AreEqual(1,next);
        }
        [TestCase(false)] [TestCase(true)]
        public void のけぞりと麻痺の確定で継続行動を一度だけ中断し予約を解放する(bool paralysis)
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0),poiseDefinition:new EnemyPoiseDefinition(10,3));var b=new BattleSystem(player,new List<Enemy>{e},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:random);var a=new ContinuingAction{Frames=30};Assert.IsTrue(b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{a}));b.TickFrame(17);int next=0;Assert.IsTrue(b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{new SequenceAction{Run=()=>next++}}));
            if(paralysis){b.RunLogicalFrame(()=>e.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000)));b.TickFrame(17);}else b.RunLogicalFrame(()=>e.TakeDamage(1,ElementType.None,poiseDamage:10));
            Assert.AreEqual(1,a.Cancels);Assert.IsFalse(a.IsRunning);int ticks=a.Ticks;for(int i=0;i<2;i++)b.TickFrame(17);Assert.AreEqual(ticks,a.Ticks);Assert.AreEqual(1,a.Cancels);Assert.AreEqual(0,next);
        }
        [TestCase(false)] [TestCase(true)]
        public void 死亡と敗北終了で継続行動を一度だけ解除し通知後や再登録へ残さない(bool defeat)
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var b=new BattleSystem(player,new List<Enemy>{e},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:random);var a=new ContinuingAction{Frames=30};b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{a});b.TickFrame(17);bool? runningAtEnd=null;b.OnBattleEnd+=_=>runningAtEnd=a.IsRunning;
            b.RunLogicalFrame(()=>{if(defeat)player.TakeDamage(10000);else e.TakeDamage(10000,ElementType.None);});Assert.AreEqual(false,runningAtEnd);Assert.AreEqual(1,a.Cancels);Assert.IsFalse(b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{new SequenceAction()}));
        }
        [Test]
        public void 行動開始中の死亡も戻り時にキャンセルし破棄を重ねても二重解放しない()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var a=new ContinuingAction();var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{a});a.Started=()=>e.TakeDamage(10000,ElementType.None);ai.Update(0,new AIContext(e,player,field,random,logger));ai.Dispose();Assert.AreEqual(1,a.Cancels);Assert.IsFalse(a.IsRunning);
        }
        [TestCase(BattlePauseReason.User)] [TestCase(BattlePauseReason.CustomScreen)]
        public void 停止中は継続行動の時計も切替完了も進めない(BattlePauseReason reason)
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var b=new BattleSystem(player,new List<Enemy>{e},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:random);var a=new ContinuingAction();b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{a});b.TickFrame(17);b.Pause(reason);int next=0;b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{new SequenceAction{Run=()=>next++}});for(int i=0;i<10;i++)b.TickFrame(17);Assert.AreEqual(0,a.Ticks);Assert.IsTrue(a.IsRunning);Assert.AreEqual(0,next);b.Resume(reason);for(int i=0;i<3;i++)b.TickFrame(17);Assert.IsFalse(a.IsRunning);Assert.AreEqual(0,next);b.TickFrame(17);Assert.AreEqual(1,next);
        }
        [Test]
        public void 暗転とヒットストップとチェックポイント離脱で継続行動の寿命を守る()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var b=new BattleSystem(player,new List<Enemy>{e},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:random);var a=new ContinuingAction{Frames=30};b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{a});b.TickFrame(17);b.TryStartDarkeningEffect(DarkeningSide.Opponent,0,3,()=>{});for(int i=0;i<3;i++)b.TickFrame(17);Assert.AreEqual(0,a.Ticks);b.RunLogicalFrame(()=>e.TakeDamage(1,ElementType.None,hitStopFrames:3));for(int i=0;i<3;i++)b.TickFrame(17);Assert.AreEqual(0,a.Ticks);
            typeof(BattleSystem).GetMethod("AbandonForCheckpoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(b,null);Assert.AreEqual(1,a.Cancels);Assert.IsFalse(a.IsRunning);
        }
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void 継続行動と切替の時計は描画分割や追いつきで一致する(int fps)
        {
            string Replay(int hz){var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0));var p=new PlayerCharacter(100);var b=new BattleSystem(p,new List<Enemy>{e},new Deck(new List<Card>()),customGaugeSpeed:0,randomProvider:new MockRandomProvider());var a=new ContinuingAction{Frames=12};int next=0;b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{a});var c=new Olca.Core.Logic.Integration.LogicalFrameClock();var trace=new List<string>();long old=0;
                void Step(long frame,int ms){if(frame==2){b.Pause(BattlePauseReason.User);b.TrySwitchEnemyActionPattern(e,new List<IEnemyAIAction>{new SequenceAction{Wait=100,Run=()=>next++}});}if(frame==5)b.Resume(BattlePauseReason.User);if(frame==10)b.TryStartDarkeningEffect(DarkeningSide.Opponent,0,3,()=>{});if(frame==15)b.RunLogicalFrame(()=>e.TakeDamage(1,ElementType.None,hitStopFrames:3));b.TickFrame(ms);trace.Add($"{b.LogicalFrame}:{a.Remaining}:{a.Cancels}:{next}");}
                for(int i=1;i<=hz;i++){long now=i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/hz;c.Accumulate(now-old);old=now;c.Drain(Step,3);}while(c.PendingFrames>0)c.Drain(Step,3);Assert.IsFalse(a.IsRunning);Assert.Greater(next,0);return string.Join("|",trace);}
            Assert.AreEqual(Replay(60),Replay(fps));
        }
        [Test]
        public void 中断クリーンアップからの切替要求も完了後だけ適用し二重中断しない()
        {
            var e=new Enemy("e","テスト",100,ElementType.None,new Vector2Int(3,0),poiseDefinition:new EnemyPoiseDefinition(10,3));var a=new ContinuingAction{Frames=30};var ai=new EnemyAIBehavior(e,new List<IEnemyAIAction>{a});var context=new AIContext(e,player,field,random,logger);int next=0;a.Cancelled=()=>Assert.IsTrue(ai.TrySwitchActionPattern(new List<IEnemyAIAction>{new SequenceAction{Run=()=>next++}}));ai.Update(0,context);e.TakeDamage(1,ElementType.None,poiseDamage:10);Assert.AreEqual(1,a.Cancels);Assert.AreEqual(0,next);for(int i=0;i<3;i++)ai.Update(17,context,true);ai.Update(17,context,true);Assert.AreEqual(1,next);Assert.AreEqual(1,a.Cancels);
        }
        [Test]
        public void 敵を登録できる()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };

            // Act
            aiSystem.RegisterEnemy(enemy, actions);

            // Assert
            Assert.AreEqual(1, aiSystem.AIBehaviors.Count, "敵が1体登録されるべき");
        }

        [Test]
        public void 敵が登録解除できる()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };
            aiSystem.RegisterEnemy(enemy, actions);

            // Act
            aiSystem.UnregisterEnemy(enemy);

            // Assert
            Assert.AreEqual(0, aiSystem.AIBehaviors.Count, "敵が登録解除されるべき");
        }

        [Test]
        public void 隣接した敵がプレイヤーを攻撃する()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);

            // Assert
            Assert.Less(player.CurrentHp, initialHp, "プレイヤーがダメージを受けるべき");
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "10ダメージを受けるべき");
        }

        [Test]
        public void 離れた敵は攻撃しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(0, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(5, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);

            // Assert
            Assert.AreEqual(initialHp, player.CurrentHp, "離れているので攻撃されないべき");
        }

        [Test]
        public void クールダウン中は行動しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 1.0f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);
            int hpAfterFirstAttack = player.CurrentHp;

            // Act: クールダウン中に再度更新
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);

            // Assert
            Assert.AreEqual(hpAfterFirstAttack, player.CurrentHp, "クールダウン中は攻撃しないべき");
        }

        [Test]
        public void クールダウン後に再度行動できる()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 0.5f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);
            int hpAfterFirstAttack = player.CurrentHp;

            // Act: クールダウン時間経過
            aiSystem.Tick(deltaTimeMs: 600, player, field, random);

            // Assert
            Assert.Less(player.CurrentHp, hpAfterFirstAttack, "クールダウン後に再攻撃するべき");
            Assert.AreEqual(hpAfterFirstAttack - 10, player.CurrentHp, "再度10ダメージを受けるべき");
        }

        [Test]
        public void 死亡した敵は行動しない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy = new Enemy("slime", "スライム", 10, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 10, cooldown: 0.1f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 敵を倒す
            enemy.TakeDamage(10, ElementType.None);
            int hpBeforeUpdate = player.CurrentHp;

            // Act
            aiSystem.Tick(deltaTimeMs: 200, player, field, random);

            // Assert
            Assert.AreEqual(hpBeforeUpdate, player.CurrentHp, "死んだ敵は攻撃しないべき");
        }

        [Test]
        public void 複数の敵が同時に行動できる()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            var enemy1 = new Enemy("slime1", "スライム1", 50, ElementType.None, new Vector2Int(3, 0));
            var enemy2 = new Enemy("slime2", "スライム2", 50, ElementType.None, new Vector2Int(1, 0));

            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 5, cooldown: 1.0f)
            };

            aiSystem.RegisterEnemy(enemy1, actions);
            aiSystem.RegisterEnemy(enemy2, actions);

            int initialHp = player.CurrentHp;

            // Act
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);

            // Assert
            Assert.AreEqual(initialHp - 10, player.CurrentHp, "2体の敵が同時に攻撃するべき（5+5=10ダメージ）");
        }

        [Test]
        public void 死亡したプレイヤーは攻撃されない()
        {
            // Arrange
            player.MoveTo(new Vector2Int(2, 0));
            player.TakeDamage(90); // HP 10 残す
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var actions = new List<IEnemyAIAction>
            {
                new BasicAttackAction(attackDamage: 20, cooldown: 0.1f)
            };
            aiSystem.RegisterEnemy(enemy, actions);

            // 1回目の攻撃でプレイヤーを倒す
            aiSystem.Tick(deltaTimeMs: 100, player, field, random);
            Assert.IsTrue(player.IsDead, "プレイヤーが死亡するべき");

            // Act: 死んだプレイヤーに再度攻撃を試みる
            int hpAfterDeath = player.CurrentHp;
            aiSystem.Tick(deltaTimeMs: 200, player, field, random);

            // Assert
            Assert.AreEqual(hpAfterDeath, player.CurrentHp, "死んだプレイヤーは攻撃されないべき");
        }

        [Test]
        public void 空のアクションリストで登録するとエラー()
        {
            // Arrange
            var enemy = new Enemy("slime", "スライム", 50, ElementType.None, new Vector2Int(3, 0));
            var emptyActions = new List<IEnemyAIAction>();

            // Act & Assert
            Assert.Throws<System.ArgumentException>(() =>
            {
                aiSystem.RegisterEnemy(enemy, emptyActions);
            }, "空のアクションリストは許可されないべき");
        }

        [Test]
        public void nullの敵を登録するとエラー()
        {
            // Arrange
            var actions = new List<IEnemyAIAction> { new BasicAttackAction() };

            // Act & Assert
            Assert.Throws<System.ArgumentNullException>(() =>
            {
                aiSystem.RegisterEnemy(null, actions);
            }, "null敵は許可されないべき");
        }
    }
}


namespace Olca.Tests.Battle
{
    [NUnit.Framework.TestFixture]
    public class PerceptionStatusTests
    {
        private Enemy enemy;
        private PlayerCharacter player;
        private BattleSystem battle;
        private int hits;
        private void Setup(int cooldown=0,TimedStatusResistances resistance=null)
        {
            hits=0;player=new PlayerCharacter(100,new Vector2Int(1,0));
            var keeper=new Enemy("keeper","検証",500,ElementType.None,new Vector2Int(5,0));
            battle=new BattleSystem(player,new List<Enemy>{keeper},new Deck());
            enemy=new Enemy("affected","検証",500,ElementType.None,new Vector2Int(2,0),isSummoned:true,statusResistances:resistance);
            battle.AddSummon(enemy,new List<IEnemyAIAction>{new BasicAttackAction(10,cooldown,poiseDamage:0,onHit:_=>hits++)});
        }
        private void Frames(int count){for(int i=0;i<count;i++)battle.TickFrame(17);}
        private AIContext Context()=>new AIContext(enemy,player,battle.Field,new MockRandomProvider(),null);
        [NUnit.Framework.TestCase(1,0)] [NUnit.Framework.TestCase(-1,0)]
        public void 混乱は決定済み方向だけ反転し基本攻撃は逆方向へ空振りする(int x,int y)
        {
            var e=new Enemy("e","検証",100,ElementType.None,new Vector2Int(2,0));var p=new PlayerCharacter(100,new Vector2Int(2+x,y));
            var context=new AIContext(e,p,new BattleField(),new MockRandomProvider(),null);int called=0;var action=new BasicAttackAction(10,200,onHit:_=>called++);
            e.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1000));Assert.IsTrue(context.HasVisiblePlayer);Assert.IsTrue(action.CanExecute(context));
            Assert.AreEqual(new Vector2Int(-x,-y),context.ResolveDirectionalInput(new Vector2Int(x,y)));action.Execute(context);Assert.AreEqual(100,p.CurrentHp);Assert.AreEqual(0,called);
            Assert.AreEqual(new Vector2Int(2,0),e.GridPosition);Assert.AreEqual(new Vector2Int(2+x,y),p.GridPosition);
            e.ClearStatus(StatusEffectType.Confusion);action.Execute(context);Assert.AreEqual(90,p.CurrentHp);Assert.AreEqual(1,called);
        }
        [NUnit.Framework.Test]
        public void 盲目は対象の位置情報を隠し直接実行も遠隔攻撃も通さず対象不要行動は残す()
        {
            Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,1000));var context=Context();var action=new BasicAttackAction(10,0);var free=new FreeAction();
            Assert.IsFalse(context.HasVisiblePlayer);Assert.AreEqual(int.MaxValue,context.GetDistanceToPlayer());Assert.IsFalse(context.IsPlayerOnLeft());Assert.IsFalse(context.IsPlayerOnRight());Assert.IsFalse(context.IsPlayerAbove());Assert.IsFalse(context.IsPlayerBelow());
            Assert.IsFalse(action.CanExecute(context));action.Execute(context);Assert.AreEqual(100,player.CurrentHp);
            var ai=new EnemyAIBehavior(enemy,new List<IEnemyAIAction>{action,free});ai.Update(17,context,true);Assert.AreEqual(1,free.Count);
            enemy.ClearStatus(StatusEffectType.Blind);Assert.IsTrue(context.HasVisiblePlayer);Assert.AreEqual(1,context.GetDistanceToPlayer());Assert.IsTrue(context.IsPlayerOnLeft());ai.Update(17,context,true);Assert.AreEqual(90,player.CurrentHp);Assert.AreEqual(1,free.Count);
            enemy.MoveTo(new Vector2Int(4,0));action.Execute(context);Assert.AreEqual(90,player.CurrentHp,"直接入口でも隣接条件を飛び越えない");
        }
        private sealed class FreeAction:IEnemyAIAction
        {
            public int Count;public bool CanExecute(AIContext context)=>true;public void Execute(AIContext context){Count++;}public int GetCooldownMs()=>0;public float GetCooldown()=>0;
        }
        [NUnit.Framework.Test]
        public void 混乱の空振りも既存クールダウンを消費し期限解除で連射へ変わらない()
        {
            Setup(200);enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1));Frames(1);Assert.AreEqual(0,hits);Assert.IsFalse(enemy.IsConfused);
            Frames(11);Assert.AreEqual(0,hits);Frames(1);Assert.AreEqual(1,hits);Assert.AreEqual(90,player.CurrentHp);
        }
        [NUnit.Framework.TestCase(StatusEffectType.Blind)] [NUnit.Framework.TestCase(StatusEffectType.Confusion)]
        public void 付与解除はフレーム境界で有効化し同フレーム取消と期限最終フレームを守る(StatusEffectType type)
        {
            Setup();battle.RunLogicalFrame(()=>{enemy.ApplyStatus(new StatusEffect(type,1000));battle.TickFrame(17);});Assert.AreEqual(1,hits,"新付与は次フレームから");Frames(1);Assert.AreEqual(1,hits);
            battle.RunLogicalFrame(()=>{enemy.ClearStatus(type);battle.TickFrame(17);});Assert.AreEqual(1,hits,"解除フレームは開始時状態を維持");Frames(1);Assert.AreEqual(2,hits);
            Setup();battle.RunLogicalFrame(()=>{enemy.ApplyStatus(new StatusEffect(type,1000));enemy.ClearStatus(type);battle.TickFrame(17);});Frames(1);Assert.AreEqual(2,hits,"取消付与を翌フレームへ残さない");
            Setup();enemy.ApplyStatus(new StatusEffect(type,1));Frames(1);Assert.AreEqual(0,hits,"最終フレームのAIにも状態を適用");Frames(1);Assert.AreEqual(1,hits);
        }
        [NUnit.Framework.Test]
        public void 盲目と混乱は独立解除し麻痺中断や直接位置変更へ操作反転を漏らさない()
        {
            Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1000));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,1000));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,1000));Frames(1);Assert.AreEqual(0,hits);
            enemy.ClearStatus(StatusEffectType.Paralyze);Frames(1);Assert.AreEqual(0,hits);Assert.IsTrue(enemy.IsConfused);Assert.IsTrue(enemy.IsBlind);
            enemy.ClearStatus(StatusEffectType.Blind);Frames(1);Assert.AreEqual(0,hits);Assert.IsTrue(Context().HasVisiblePlayer);enemy.MoveTo(new Vector2Int(3,0));Assert.AreEqual(new Vector2Int(3,0),enemy.GridPosition,"位置指定は方向入力ではない");
            enemy.MoveTo(new Vector2Int(2,0));enemy.ClearStatus(StatusEffectType.Confusion);Frames(1);Assert.AreEqual(1,hits);
        }
        [NUnit.Framework.TestCase(false)] [NUnit.Framework.TestCase(true)]
        public void 停止とカスタム復帰は両状態の時間と敵行動待ちを維持する(bool custom)
        {
            Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,100));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,100));Frames(2);
            if(custom){battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());}else battle.Pause(BattlePauseReason.User);
            Frames(30);Assert.AreEqual(4,enemy.GetStatus(StatusEffectType.Blind).RemainingFrames);Assert.AreEqual(4,enemy.GetStatus(StatusEffectType.Confusion).RemainingFrames);Assert.AreEqual(0,hits);
            if(custom)battle.CustomScreen.Close();else battle.Resume(BattlePauseReason.User);Frames(4);Assert.AreEqual(0,hits);Frames(1);Assert.AreEqual(1,hits);
        }
        [NUnit.Framework.TestCase(StatusEffectType.Blind,75,15)] [NUnit.Framework.TestCase(StatusEffectType.Confusion,50,30)] [NUnit.Framework.TestCase(StatusEffectType.Blind,100,0)]
        public void 対象の個別耐性は期間だけ短縮し無効付与と別状態へ漏らさない(StatusEffectType type,int resistance,int frames)
        {
            Setup(resistance:new TimedStatusResistances(confusion:type==StatusEffectType.Confusion?resistance:0,blind:type==StatusEffectType.Blind?resistance:0));var effect=new StatusEffect(type,1000);enemy.ApplyStatus(effect);
            Assert.AreEqual(60,effect.RemainingFrames);Assert.AreEqual(frames,enemy.GetStatus(type)?.RemainingFrames??0);
            var combined=TimedStatusResistances.Combine(new TimedStatusResistances(confusion:30,blind:40),new TimedStatusResistances(confusion:20,blind:60));Assert.AreEqual(50,combined.Confusion);Assert.AreEqual(100,combined.Blind);Assert.AreEqual(0,combined.Poison);
            var source=new StatusEffect(StatusEffectType.Confusion,1000,durationResistancePercent:25);Setup(resistance:new TimedStatusResistances(confusion:25));enemy.ApplyStatus(source);Assert.AreEqual(30,enemy.GetStatus(StatusEffectType.Confusion).RemainingFrames);Assert.AreEqual(45,source.RemainingFrames);
            var current=enemy.GetStatus(StatusEffectType.Confusion);enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1000,durationResistancePercent:100));Assert.AreSame(current,enemy.GetStatus(StatusEffectType.Confusion));
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TimedStatusResistances(confusion:-1));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TimedStatusResistances(blind:-1));
        }
        [NUnit.Framework.TestCase(StatusEffectType.Confusion)] [NUnit.Framework.TestCase(StatusEffectType.Blind)]
        public void 同じ付与定義を渡した別の敵と残時間を共有しない(StatusEffectType type)
        {
            Setup();var definition=new StatusEffect(type,1000);enemy.ApplyStatus(definition);var other=new Enemy("other","検証",100,ElementType.None,new Vector2Int(4,0));other.ApplyStatus(definition);Frames(2);
            Assert.AreEqual(58,enemy.GetStatus(type).RemainingFrames);Assert.AreEqual(60,other.GetStatus(type).RemainingFrames);Assert.AreEqual(60,definition.RemainingFrames);
        }
        private static Card PerceptionCard()
        {
            var modifier=new CardModifier();modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Confusion,Trigger=DerivativeTrigger.OnDamage,Duration=1});modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Blind,Trigger=DerivativeTrigger.OnDamage,Duration=1});
            return new Card("perception","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,1,"既存付与",modifier:modifier);
        }
        [NUnit.Framework.Test]
        public void 通常カードから両状態を付与し同種更新と個別解除を敵AIへ反映する()
        {
            Setup();var card=PerceptionCard();battle.RunLogicalFrame(()=>{battle.CardUsage.EnqueueCards(new List<Card>{card});Assert.IsTrue(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());battle.TickFrame(17);});Assert.AreEqual(1,hits);Assert.IsTrue(enemy.IsBlind);Assert.IsTrue(enemy.IsConfused);
            Frames(2);Assert.AreEqual(1,hits);Assert.AreEqual(58,enemy.GetStatus(StatusEffectType.Blind).RemainingFrames);
            battle.RunLogicalFrame(()=>{enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,100));battle.TickFrame(17);});Assert.AreEqual(6,enemy.GetStatus(StatusEffectType.Blind).RemainingFrames);Assert.AreEqual(57,enemy.GetStatus(StatusEffectType.Confusion).RemainingFrames);
            enemy.ClearStatus(StatusEffectType.Blind);Frames(1);Assert.AreEqual(1,hits,"混乱だけでも逆方向へ空振り");enemy.ClearStatus(StatusEffectType.Confusion);Frames(1);Assert.AreEqual(2,hits);
        }
        private static DungeonConfig Map(TimedStatusResistances resistance)
        {
            var map=DungeonConfig.CreateTestDungeon(5,30,4);map.MapCells[4]=new MapCell(4,new Enemy("boss","旧耐性",200,ElementType.None,new Vector2Int(4,0),statusResistances:resistance));return map;
        }
        [NUnit.Framework.Test]
        public void 旧耐性の定義hashは不変で新耐性だけ照合へ加え保存再開は開始状態へ戻る()
        {
            var old=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(Map(new TimedStatusResistances(20,30)),new Deck()).Definitions;
            Assert.AreEqual("E4E2D89CF56E33DAB5F9A9AF943F731D5BF893A4778750534FD5CD942FBE6739",old.MapHash);
            var map=Map(new TimedStatusResistances(confusion:50,blind:50));var card=PerceptionCard();var deck=new Deck(new List<Card>{card,card.Clone(),card.Clone(),card.Clone(),card.Clone()});var defaults=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck);
            string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"olca-perception-"+System.Guid.NewGuid().ToString("N"));
            try{
                var file=new Olca.Unity.FileCheckpointStore(defaults.Definitions,directory);var session=new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defaults.Definitions,file);var game=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(500),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
                for(int i=0;game.CurrentBattle==null&&i<100;i++)game.TickFrame(17);var b=game.CurrentBattle;Assert.IsNotNull(b);b.CustomScreen.Close();b.CardUsage.EnqueueCards(new List<Card>{card});Assert.IsTrue(b.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());Assert.IsTrue(b.Enemies[0].IsBlind);Assert.IsTrue(b.Enemies[0].IsConfused);var bytes=System.IO.File.ReadAllBytes(file.ActivePath);
                Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);Assert.IsTrue(game.SuspendForCheckpoint());restored.ActivateCheckpoint();var e=restored.CurrentBattle.Enemies[0];Assert.IsFalse(e.IsBlind);Assert.IsFalse(e.IsConfused);Assert.AreEqual(50,e.StatusResistances.Blind);Assert.AreEqual(50,e.StatusResistances.Confusion);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(file.ActivePath));
                var changed=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(Map(new TimedStatusResistances(confusion:75,blind:50)),deck).Definitions;Assert.IsNull(new Olca.Unity.FileCheckpointStore(changed,directory).Read());CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(file.ActivePath));
            }finally{if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }
        [NUnit.Framework.Test]
        public void 知覚状態の期限と敵行動トレースは描画速度や追いつきで変わらない()
        {
            List<string> Trace(int fps){Setup(200);enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,500));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,200));var trace=new List<string>();var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();long wall=0;
                void Tick(long frame,int ms){if(frame==8)battle.Pause(BattlePauseReason.User);if(frame==14)battle.Resume(BattlePauseReason.User);battle.TickFrame(ms);trace.Add(player.CurrentHp+":"+enemy.IsBlind+":"+enemy.IsConfused+":"+hits);}
                if(fps==0){clock.Accumulate(Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond);clock.Drain(Tick);}else for(int i=1;clock.Frame<60;i++){long next=(long)i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(next-wall);wall=next;clock.Drain(Tick);}return trace;}
            NUnit.Framework.CollectionAssert.AreEqual(Trace(60),Trace(30));NUnit.Framework.CollectionAssert.AreEqual(Trace(60),Trace(120));NUnit.Framework.CollectionAssert.AreEqual(Trace(60),Trace(0));
        }
    }
}


namespace Olca.Tests.Battle
{
    [TestFixture]
    public class PlayerConfusionTests
    {
        private BattleSystem battle;private PlayerCharacter player;private Enemy keeper;
        private void Setup(int x=2){player=new PlayerCharacter(100,new Vector2Int(x,0));keeper=new Enemy("keeper","検証",500,ElementType.None,new Vector2Int(5,0));battle=new BattleSystem(player,new List<Enemy>{keeper},new Deck());}
        private static Olca.Core.Logic.Integration.LogicalInput Input(float held=0,float tap=0)=>new Olca.Core.Logic.Integration.LogicalInput{Horizontal=held,TouchHorizontalDown=tap};
        private void Frames(int n){for(int i=0;i<n;i++)battle.TickFrame(17);}
        [Test]
        public void 既存敵攻撃のonHitから次フレームに有効化し進行中の移動を反転し直さない()
        {
            Setup();var source=new Enemy("source","検証",500,ElementType.None,new Vector2Int(3,0),isSummoned:true);battle.AddSummon(source,new List<IEnemyAIAction>{new BasicAttackAction(1,100000,poiseDamage:0,onHit:p=>p.ApplyConfusion(60))});
            battle.TickFrame(17,Input(-1));Assert.AreEqual(99,player.CurrentHp);Assert.IsTrue(player.IsConfused);Assert.AreEqual(60,player.ConfusionRemainingFrames);Assert.AreEqual(new Vector2Int(1,0),battle.PlayerMovement.NormalMoveTo);
            Frames(8);Assert.AreEqual(new Vector2Int(1,0),player.GridPosition);battle.TickFrame(17,Input(1));Assert.AreEqual(new Vector2Int(0,0),battle.PlayerMovement.NormalMoveTo);Frames(8);Assert.AreEqual(new Vector2Int(0,0),player.GridPosition);
        }
        [TestCase(1,0,-1)] [TestCase(-1,0,1)] [TestCase(-1,1,-1)] [TestCase(1,-1,1)]
        public void 解決済み保持とタッチ優先入力を一度だけ反転する(float held,float tap,int expected)
        {
            Setup();player.ApplyConfusion(60);battle.TickFrame(17,Input(held,tap));Assert.AreEqual(new Vector2Int(2+expected,0),battle.PlayerMovement.NormalMoveTo);Assert.IsFalse(player.IsActionPrevented);
        }
        [Test]
        public void 付与解除のフレーム境界と同フレーム取消は通常入力にも同じ順序で効く()
        {
            Setup();battle.RunLogicalFrame(()=>{player.ApplyConfusion(60);battle.TickFrame(17,Input(1));});Assert.AreEqual(new Vector2Int(3,0),battle.PlayerMovement.NormalMoveTo);Assert.AreEqual(60,player.ConfusionRemainingFrames);
            Setup();player.ApplyConfusion(60);battle.TickFrame(17);battle.RunLogicalFrame(()=>{player.ClearConfusion();battle.TickFrame(17,Input(1));});Assert.AreEqual(new Vector2Int(1,0),battle.PlayerMovement.NormalMoveTo);Assert.IsFalse(player.IsConfused);
            Setup();battle.RunLogicalFrame(()=>{player.ApplyConfusion(60);player.ClearConfusion();battle.TickFrame(17);});battle.TickFrame(17,Input(1));Assert.AreEqual(new Vector2Int(3,0),battle.PlayerMovement.NormalMoveTo);Assert.IsFalse(player.IsConfused);
        }
        [TestCase(false)] [TestCase(true)]
        public void ポーズとカスタムは混乱時間と移動を停止し復帰は残りから進む(bool custom)
        {
            Setup();player.ApplyConfusion(60);battle.TickFrame(17,Input(1));float progress=battle.PlayerMovement.NormalMoveProgress;
            if(custom){battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());}else battle.Pause(BattlePauseReason.User);
            Frames(120);Assert.AreEqual(59,player.ConfusionRemainingFrames);Assert.AreEqual(progress,battle.PlayerMovement.NormalMoveProgress);
            if(custom)battle.CustomScreen.Close();else battle.Resume(BattlePauseReason.User);Frames(8);Assert.AreEqual(new Vector2Int(1,0),player.GridPosition);Assert.AreEqual(51,player.ConfusionRemainingFrames);
        }
        [Test]
        public void 個別解除は麻痺毒に漏れず座標指定や滑りへ入力反転を漏らさない()
        {
            Setup();player.ApplyConfusion(60);player.ApplyParalysis(10);player.ApplyPoison("external",new StatusEffect(StatusEffectType.Poison,1000,1),60);battle.TickFrame(17,Input(1));Assert.IsTrue(player.IsParalyzed);Assert.IsTrue(player.IsConfused);Assert.IsFalse(battle.PlayerMovement.IsNormalMoving);
            player.ClearParalysis();battle.TickFrame(17,Input(1));Assert.AreEqual(new Vector2Int(1,0),battle.PlayerMovement.NormalMoveTo);player.ClearConfusion();battle.TickFrame(17);Assert.AreEqual(1,player.CapturePoisons().Length);Assert.IsFalse(player.IsConfused);
            Setup();player.ApplyConfusion(60);battle.TickFrame(17);Assert.IsTrue(battle.PlayerMovement.TryMove(new Vector2Int(1,0),new List<Enemy>{keeper}));Assert.AreEqual(new Vector2Int(3,0),player.GridPosition,"座標方向を渡す直接移動APIは入力ではない");
        }
        [Test]
        public void 期間更新と耐性は既存状態を不正付与で消さず期限最終フレームまで反転する()
        {
            Setup();player.ApplyConfusion(60,50);battle.TickFrame(17);Assert.AreEqual(29,player.ConfusionRemainingFrames);player.ApplyConfusion(60,100);battle.TickFrame(17);Assert.AreEqual(28,player.ConfusionRemainingFrames);
            battle.RunLogicalFrame(()=>player.ApplyConfusion(1));Assert.AreEqual(1,player.ConfusionRemainingFrames);battle.TickFrame(17,Input(1));Assert.AreEqual(new Vector2Int(1,0),battle.PlayerMovement.NormalMoveTo);Assert.IsFalse(player.IsConfused);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>player.ApplyConfusion(0));Assert.Throws<System.ArgumentOutOfRangeException>(()=>player.ApplyConfusion(1,-1));
        }
        [TestCase(false)] [TestCase(true)]
        public void 勝敗で予約と有効状態を破棄し同じプレイヤーの次戦へ残さない(bool defeat)
        {
            Setup();player.ApplyConfusion(60);battle.TickFrame(17);battle.RunLogicalFrame(()=>{player.ApplyConfusion(120);if(defeat)player.TakeDamage(1000);else keeper.TakeDamage(1000,ElementType.None);});Assert.IsTrue(battle.IsGameOver);Assert.IsFalse(player.IsConfused);Assert.AreEqual(0,player.ConfusionRemainingFrames);
        }
        [Test]
        public void チェックポイント再開は保存済み開始状態で混乱と移動予約を再生しない()
        {
            var map=Olca.Core.Domain.DungeonConfig.CreateTestDungeon(8,30,7);var defaults=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,new Deck());var store=new Olca.Core.Logic.Dungeon.MemoryCheckpointStore(defaults.Definitions);var session=new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defaults.Definitions,store);var g=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);
            for(int i=0;i<100&&g.CurrentBattle==null;i++)g.TickFrame(17);var b=g.CurrentBattle;Assert.IsNotNull(b);b.CustomScreen.Close();var sequence=store.Read().Sequence;b.Player.ApplyConfusion(60);b.TickFrame(17,Input(1));Assert.IsTrue(b.Player.IsConfused);
            Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);Assert.IsTrue(g.SuspendForCheckpoint());Assert.IsFalse(b.Player.IsConfused);restored.ActivateCheckpoint();Assert.IsFalse(restored.CurrentBattle.Player.IsConfused);Assert.AreEqual(0,restored.CurrentBattle.Player.ConfusionRemainingFrames);Assert.IsFalse(restored.CurrentBattle.PlayerMovement.IsNormalMoving);Assert.AreEqual(sequence,store.Read().Sequence);
        }
        [Test]
        public void 混乱の入力と時間トレースは描画fpsと追いつきで変わらない()
        {
            List<string> Trace(int fps){Setup();player.ApplyConfusion(30);var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();var result=new List<string>();long wall=0;void Tick(long f,int ms){if(f==10)battle.Pause(BattlePauseReason.User);if(f==15)battle.Resume(BattlePauseReason.User);battle.TickFrame(ms,Input(f<25?1:-1));result.Add(player.GridPosition+":"+player.ConfusionRemainingFrames+":"+player.IsConfused+":"+battle.PlayerMovement.NormalMoveTo);}if(fps==0){clock.Accumulate(Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond);clock.Drain(Tick);}else for(int i=1;clock.Frame<60;i++){long t=(long)i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(t-wall);wall=t;clock.Drain(Tick);}return result;}
            CollectionAssert.AreEqual(Trace(60),Trace(30));CollectionAssert.AreEqual(Trace(60),Trace(120));CollectionAssert.AreEqual(Trace(60),Trace(0));
        }
    }
}


namespace Olca.Tests.Battle
{
    [TestFixture]
    public class PlayerBlindnessTests
    {
        private PlayerCharacter player;private Enemy keeper;private BattleSystem battle;
        private void Setup(){player=new PlayerCharacter(100,new Vector2Int(2,0));keeper=new Enemy("keeper","検証",500,ElementType.None,new Vector2Int(5,0));battle=new BattleSystem(player,new List<Enemy>{keeper},new Deck());}
        private void Frames(int count){for(int i=0;i<count;i++)battle.TickFrame(17);}
        [Test]
        public void 既存敵攻撃onHitで付与し盲目中も通常カードの命中とダメージを変更しない()
        {
            Setup();var enemy=new Enemy("source","検証",500,ElementType.None,new Vector2Int(3,0),isSummoned:true);battle.AddSummon(enemy,new List<IEnemyAIAction>{new BasicAttackAction(1,100000,poiseDamage:0,onHit:p=>p.ApplyBlindness(60))});battle.TickFrame(17);Assert.AreEqual(99,player.CurrentHp);Assert.IsTrue(player.IsBlind);Assert.AreEqual(60,player.BlindRemainingFrames);Assert.IsFalse(player.IsActionPrevented);
            var card=new Card("test","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,10,"通常命中");battle.CardUsage.EnqueueCards(new List<Card>{card});Assert.IsTrue(battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());Assert.AreEqual(490,enemy.CurrentHp);
        }
        [Test]
        public void 付与解除と期限はフレーム境界で確定し操作方向を変えない()
        {
            Setup();battle.RunLogicalFrame(()=>{player.ApplyBlindness(60);Assert.IsFalse(player.IsBlind);battle.TickFrame(17);});Assert.IsTrue(player.IsBlind);Assert.AreEqual(60,player.BlindRemainingFrames);
            battle.RunLogicalFrame(()=>{player.ClearBlindness();battle.TickFrame(17);Assert.IsTrue(player.IsBlind);});Assert.IsFalse(player.IsBlind);
            battle.RunLogicalFrame(()=>{player.ApplyBlindness(60);player.ClearBlindness();battle.TickFrame(17);});Assert.IsFalse(player.IsBlind);
            player.ApplyBlindness(1);battle.RunLogicalFrame(()=>{battle.TickFrame(17,new Olca.Core.Logic.Integration.LogicalInput{Horizontal=1});Assert.IsTrue(player.IsBlind);});Assert.IsFalse(player.IsBlind);Assert.AreEqual(new Vector2Int(3,0),battle.PlayerMovement.NormalMoveTo);
        }
        [Test]
        public void 期間耐性と同種更新は量を持たず無効付与で既存状態を消さない()
        {
            Setup();player.ApplyBlindness(60,50);battle.TickFrame(17);Assert.AreEqual(29,player.BlindRemainingFrames);player.ApplyBlindness(60,100);battle.TickFrame(17);Assert.AreEqual(28,player.BlindRemainingFrames);battle.RunLogicalFrame(()=>player.ApplyBlindness(120));Assert.AreEqual(120,player.BlindRemainingFrames);Assert.Throws<System.ArgumentOutOfRangeException>(()=>player.ApplyBlindness(0));Assert.Throws<System.ArgumentOutOfRangeException>(()=>player.ApplyBlindness(1,-1));
        }
        [Test]
        public void 盲目混乱麻痺毒を独立所有し個別解除で他状態を失わない()
        {
            Setup();player.ApplyBlindness(60);player.ApplyConfusion(60);player.ApplyParalysis(60);player.ApplyPoison("external",new StatusEffect(StatusEffectType.Poison,1000,1),60);battle.TickFrame(17);Assert.IsTrue(player.IsBlind);Assert.IsTrue(player.IsConfused);Assert.IsTrue(player.IsParalyzed);player.ClearBlindness();battle.TickFrame(17);Assert.IsFalse(player.IsBlind);Assert.IsTrue(player.IsConfused);Assert.IsTrue(player.IsParalyzed);Assert.AreEqual(1,player.CapturePoisons().Length);player.ApplyBlindness(60);player.ClearConfusion();player.ClearParalysis();battle.TickFrame(17);Assert.IsTrue(player.IsBlind);Assert.IsFalse(player.IsConfused);Assert.IsFalse(player.IsParalyzed);
        }
        [TestCase(false)] [TestCase(true)]
        public void ポーズとカスタムは盲目時間を保持し復帰は残りから進む(bool custom)
        {
            Setup();player.ApplyBlindness(6);battle.TickFrame(17);if(custom){battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());}else battle.Pause(BattlePauseReason.User);Frames(120);Assert.IsTrue(player.IsBlind);Assert.AreEqual(5,player.BlindRemainingFrames);if(custom)battle.CustomScreen.Close();else battle.Resume(BattlePauseReason.User);Frames(5);Assert.IsFalse(player.IsBlind);Assert.AreEqual(0,player.BlindRemainingFrames);
        }
        [TestCase(false)] [TestCase(true)]
        public void 勝敗は有効と予約を破棄して次戦へ持ち越さない(bool defeat)
        {
            Setup();player.ApplyBlindness(60);battle.TickFrame(17);battle.RunLogicalFrame(()=>{player.ApplyBlindness(120);if(defeat)player.TakeDamage(1000);else keeper.TakeDamage(1000,ElementType.None);});Assert.IsTrue(battle.IsGameOver);Assert.IsFalse(player.IsBlind);Assert.AreEqual(0,player.BlindRemainingFrames);
        }
        [Test]
        public void チェックポイント再開は途中盲目と予約を保存せず開始状態へ戻る()
        {
            var map=DungeonConfig.CreateTestDungeon(8,30,7);var defaults=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,new Deck());var store=new Olca.Core.Logic.Dungeon.MemoryCheckpointStore(defaults.Definitions);var session=new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defaults.Definitions,store);var g=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);for(int i=0;i<100&&g.CurrentBattle==null;i++)g.TickFrame(17);var b=g.CurrentBattle;Assert.IsNotNull(b);b.CustomScreen.Close();b.Player.ApplyBlindness(60);b.TickFrame(17);var sequence=store.Read().Sequence;Assert.IsTrue(b.Player.IsBlind);Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);Assert.IsTrue(g.SuspendForCheckpoint());Assert.IsFalse(b.Player.IsBlind);restored.ActivateCheckpoint();Assert.IsFalse(restored.CurrentBattle.Player.IsBlind);Assert.AreEqual(0,restored.CurrentBattle.Player.BlindRemainingFrames);Assert.AreEqual(sequence,store.Read().Sequence);
        }
        [Test]
        public void 敵Rendererと補助表示だけを隠し復帰で元の非表示状態も維持する()
        {
            Setup();var cells=new List<MapCell>();for(int i=0;i<6;i++)cells.Add(new MapCell(i));cells[5]=new MapCell(5,keeper);var dungeon=new Olca.Core.Logic.Dungeon.DungeonSystem(new DungeonConfig("view","表示",6,cells),player,new Deck(),new MockRandomProvider(),new MockLogger());var root=new UnityEngine.GameObject("BlindViewTest");var ui=new UnityEngine.GameObject("OtherUI");
            try{var view=root.AddComponent<Olca.Presentation.Field.GameFieldView>();view.Initialize(dungeon);view.OnBattleStarted(battle,0);var enemy=view.GetEnemyView(keeper);Assert.IsNotNull(enemy);var body=enemy.GetComponent<UnityEngine.SpriteRenderer>();var name=new UnityEngine.GameObject("EnemyHint",typeof(UnityEngine.TextMesh));name.transform.SetParent(enemy.transform);var hint=name.GetComponent<UnityEngine.MeshRenderer>();var other=ui.AddComponent<UnityEngine.SpriteRenderer>();var prehidden=new UnityEngine.GameObject("AlreadyHidden",typeof(UnityEngine.SpriteRenderer));prehidden.transform.SetParent(enemy.transform);var pre=prehidden.GetComponent<UnityEngine.SpriteRenderer>();pre.forceRenderingOff=true;
                player.ApplyBlindness(60);battle.TickFrame(17);view.RefreshEnemyPerception();Assert.IsTrue(enemy.gameObject.activeSelf);Assert.IsTrue(body.forceRenderingOff);Assert.IsTrue(hint.forceRenderingOff);Assert.IsFalse(other.forceRenderingOff);Assert.IsTrue(name.activeSelf);var later=new UnityEngine.GameObject("LaterHint",typeof(UnityEngine.SpriteRenderer));later.transform.SetParent(enemy.transform);view.RefreshEnemyPerception();Assert.IsTrue(later.GetComponent<UnityEngine.SpriteRenderer>().forceRenderingOff);
                player.ClearBlindness();battle.TickFrame(17);view.RefreshEnemyPerception();Assert.IsFalse(body.forceRenderingOff);Assert.IsFalse(hint.forceRenderingOff);Assert.IsTrue(pre.forceRenderingOff);Assert.IsFalse(later.GetComponent<UnityEngine.SpriteRenderer>().forceRenderingOff);player.ApplyBlindness(60);battle.TickFrame(17);view.RefreshEnemyPerception();typeof(Olca.Presentation.Field.GameFieldView).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);Assert.IsFalse(body.forceRenderingOff);view.RefreshEnemyPerception();Assert.IsTrue(body.forceRenderingOff);view.OnBattleEnded();Assert.IsFalse(body.forceRenderingOff);Assert.IsFalse(other.forceRenderingOff);
            }finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(ui);}
        }
        [Test]
        public void 盲目と混乱の期間トレースは描画fpsと追いつきで同じになる()
        {
            List<string> Trace(int fps){Setup();player.ApplyBlindness(30);player.ApplyConfusion(40);var c=new Olca.Core.Logic.Integration.LogicalFrameClock();var result=new List<string>();long wall=0;void Tick(long f,int ms){if(f==10)battle.Pause(BattlePauseReason.User);if(f==15)battle.Resume(BattlePauseReason.User);battle.TickFrame(ms);result.Add(player.IsBlind+":"+player.BlindRemainingFrames+":"+player.IsConfused+":"+player.CurrentHp);}if(fps==0){c.Accumulate(Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond);c.Drain(Tick);}else for(int i=1;c.Frame<60;i++){long t=(long)i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps;c.Accumulate(t-wall);wall=t;c.Drain(Tick);}return result;}
            CollectionAssert.AreEqual(Trace(60),Trace(30));CollectionAssert.AreEqual(Trace(60),Trace(120));CollectionAssert.AreEqual(Trace(60),Trace(0));
        }
    }
}

namespace Olca.Tests.Battle
{
    [NUnit.Framework.TestFixture]
    public class EnemyCardPatternConnectionTests
    {
        private PlayerCharacter player;private Enemy enemy,other;private BattleSystem battle;
        private Card Make(Olca.Core.Logic.CardEffects.Effects.DamageTargetType target=Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single,CardCategory category=CardCategory.Attack,int power=7,CardModifier modifier=null,int startup=0,int recovery=0,int invincible=0)
            =>new Card("fixture","テスト",1,CardRarity.Normal,category,ElementType.None,CardSeries.Sword,power,"",targetType:target,modifier:modifier,startupFrames:startup,recoveryFrames:recovery,poiseDamage:0,hitInvincibilityMs:invincible);
        private void Setup(int sourceX=3)
        {
            player=new PlayerCharacter(500,new Vector2Int(2,0));enemy=new Enemy("source","テスト",100,ElementType.None,new Vector2Int(sourceX,0),poiseDefinition:new EnemyPoiseDefinition(30,3));other=new Enemy("other","テスト",100,ElementType.None,new Vector2Int(4,0));
            battle=new BattleSystem(player,new List<Enemy>{enemy,other},new Deck(),customGaugeSpeed:0,randomProvider:new MockRandomProvider());
            battle.TrySwitchEnemyActionPattern(other,new List<IEnemyAIAction>{new CountAction()});
            battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{new CountAction()});
        }
        private AIContext Context()=>new AIContext(enemy,player,battle.Field,new MockRandomProvider(),null,battle);
        private sealed class CountAction:IEnemyAIAction{public int Count;public bool CanExecute(AIContext c)=>true;public void Execute(AIContext c)=>Count++;public int GetCooldownMs()=>1000;public float GetCooldown()=>1;}
        private void Frames(int count){for(int i=0;i<count;i++)battle.TickFrame(17);}
        [TestCase(1)] [TestCase(3)]
        public void 敵カードは使用者の左右から相手だけへ命中し味方を傷つけない(int sourceX)
        {Setup(sourceX);new EnemyCardAction(Make(Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile),0).Execute(Context());Assert.AreEqual(493,player.CurrentHp);Assert.AreEqual(100,enemy.CurrentHp);Assert.AreEqual(100,other.CurrentHp);}
        [Test]
        public void 射程外と盲目は不可で自己回復は相手視認を必要としない()
        {Setup(5);Assert.IsFalse(new EnemyCardAction(Make(Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile),0).CanExecute(Context()));enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,1000));Assert.IsFalse(new EnemyCardAction(Make(),0).CanExecute(Context()));Assert.IsTrue(new EnemyCardAction(Make(category:CardCategory.Defense),0).CanExecute(Context()));}
        [Test]
        public void 混乱では攻撃方向が反転し相手や味方の所属は反転しない()
        {Setup();enemy.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,1000));new EnemyCardAction(Make(),0).Execute(Context());Assert.AreEqual(500,player.CurrentHp);Assert.AreEqual(100,other.CurrentHp);}
        [Test]
        public void 回復とバリアは使用した敵自身だけに適用する()
        {Setup();enemy.TakeDamage(40,ElementType.None);player.TakeDamage(30,affectsPoise:false);new EnemyCardAction(Make(category:CardCategory.Defense,power:9),0).Execute(Context());Assert.AreEqual(69,enemy.CurrentHp);Assert.AreEqual(470,player.CurrentHp);var card=new Card("barrier_test","テスト",1,CardRarity.Normal,CardCategory.Defense,ElementType.None,CardSeries.Sword,12,"");new EnemyCardAction(card,0).Execute(Context());Assert.AreEqual(12,enemy.BarrierHp);Assert.AreEqual(0,player.BarrierHp);Assert.AreEqual(0,other.BarrierHp);}
        private Card Multi(int invincible=0)
        {var m=new CardModifier();m.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.MultiHit,HitCount=3});return Make(modifier:m,invincible:invincible);}
        [Test]
        public void 敵の多段と被弾無敵は同フレーム集計と次フレーム拒否を維持する()
        {Setup();battle.RunLogicalFrame(()=>new EnemyCardAction(Multi(1000),0).Execute(Context()));Assert.AreEqual(479,player.CurrentHp);new EnemyCardAction(Make(),0).Execute(Context());Assert.AreEqual(479,player.CurrentHp);}
        [Test]
        public void ガードとバリアで完全防御したカードの付随状態は発生しない()
        {Setup();var m=new CardModifier();m.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Paralyze,Trigger=DerivativeTrigger.OnHit,Duration=1});player.SetBarrier(100);new EnemyCardAction(Make(modifier:m),0).Execute(Context());Assert.AreEqual(500,player.CurrentHp);Assert.IsFalse(player.IsParalyzed);}
        [Test]
        public void 命中派生の状態はプレイヤー側耐性へ接続する()
        {Setup();player.SetBattleStatusResistances(new TimedStatusResistances(paralyze:50));var m=new CardModifier();m.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Paralyze,Trigger=DerivativeTrigger.OnHit,Duration=1});battle.RunLogicalFrame(()=>new EnemyCardAction(Make(modifier:m),0).Execute(Context()));Assert.IsTrue(player.IsParalyzed);Assert.AreEqual(30,player.ParalysisRemainingFrames);Assert.IsFalse(enemy.IsParalyzed);}
        [Test]
        public void 前後隙中は次行動とHP切替を待ち新AIへ旧待ちを移植しない()
        {Setup();var old=new EnemyCardAction(Make(startup:2,recovery:3),900);var next=new CountAction();battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{old});Frames(1);Assert.IsTrue(old.IsRunning);Assert.AreEqual(500,player.CurrentHp);battle.ConfigureEnemyHpPatterns(enemy,new[]{new EnemyHpActionPattern(5000,true,new[]{next}),new EnemyHpActionPattern(10000,true,new IEnemyAIAction[]{old})});enemy.TakeDamage(50,ElementType.None,poiseDamage:0);Frames(1);Assert.AreEqual(0,next.Count);Frames(1);Assert.AreEqual(493,player.CurrentHp);Frames(3);Assert.AreEqual(0,next.Count);Frames(1);Assert.AreEqual(1,next.Count);}
        [TestCase(BattlePauseReason.User)] [TestCase(BattlePauseReason.CustomScreen)]
        public void 停止中は前隙とHP条件を進めず再開で一度だけ接続する(BattlePauseReason reason)
        {Setup();var old=new EnemyCardAction(Make(startup:3),0);var next=new CountAction();battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{old});Frames(1);battle.ConfigureEnemyHpPatterns(enemy,new[]{new EnemyHpActionPattern(5000,true,new[]{next}),new EnemyHpActionPattern(10000,true,new IEnemyAIAction[]{old})});battle.Pause(reason);enemy.TakeDamage(50,ElementType.None,poiseDamage:0);Frames(20);Assert.AreEqual(500,player.CurrentHp);Assert.AreEqual(0,next.Count);battle.Resume(reason);Frames(6);Assert.AreEqual(493,player.CurrentHp);Assert.AreEqual(1,next.Count);}
        [Test]
        public void 暗転とヒットストップはカード前隙もHP自動選択も進めない()
        {Setup();var a=new EnemyCardAction(Make(startup:3),0);battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{a});Frames(1);battle.TryStartDarkeningEffect(DarkeningSide.Opponent,0,3,()=>{});Frames(3);Assert.AreEqual(500,player.CurrentHp);battle.RunLogicalFrame(()=>enemy.TakeDamage(1,ElementType.None,poiseDamage:0,hitStopFrames:3));Frames(3);Assert.AreEqual(500,player.CurrentHp);Frames(3);Assert.AreEqual(493,player.CurrentHp);}
        [TestCase(false)] [TestCase(true)]
        public void 死亡または撤退で前隙を取消し後からプレイヤーを傷つけない(bool retreat)
        {Setup();var a=new EnemyCardAction(Make(startup:5),0);battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{a});Frames(1);if(retreat)typeof(BattleSystem).GetMethod("AbandonForCheckpoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(battle,null);else enemy.TakeDamage(1000,ElementType.None);Assert.IsFalse(a.IsRunning);Frames(20);Assert.AreEqual(500,player.CurrentHp);}
        [Test]
        public void のけぞりでカード前隙を取消し発動予約を残さない()
        {Setup();enemy.SetPoise(30);var a=new EnemyCardAction(Make(startup:5),1000);battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{a});Frames(1);enemy.TakeDamage(40,ElementType.None);Assert.IsFalse(a.IsRunning);Frames(8);Assert.AreEqual(500,player.CurrentHp);}
        [TestCase(true,1)] [TestCase(false,0)]
        public void HP境界の包含は設定を使い整数比較で丸めない(bool include,int expected)
        {Setup();var low=new CountAction();var high=new CountAction();battle.ConfigureEnemyHpPatterns(enemy,new[]{new EnemyHpActionPattern(5000,include,new[]{low}),new EnemyHpActionPattern(10000,true,new[]{high})});enemy.TakeDamage(50,ElementType.None,poiseDamage:0);Frames(1);Assert.AreEqual(expected,low.Count);Assert.AreEqual(1-expected,high.Count);}
        [Test]
        public void HP区間を飛び越える被弾と回復で各対象列を選び同じ列を毎回先頭に戻さない()
        {Setup();var low=new CountAction();var mid=new CountAction();var high=new CountAction();battle.ConfigureEnemyHpPatterns(enemy,new[]{new EnemyHpActionPattern(3000,true,new[]{low}),new EnemyHpActionPattern(7000,true,new[]{mid}),new EnemyHpActionPattern(10000,true,new[]{high})});Frames(1);enemy.TakeDamage(80,ElementType.None,poiseDamage:0);Frames(1);Assert.AreEqual(1,low.Count);Assert.AreEqual(0,mid.Count);Frames(3);Assert.AreEqual(1,low.Count);enemy.Heal(80);Frames(1);Assert.AreEqual(2,high.Count);}
        [Test]
        public void HP設定はコピーし重複逆順空列最大HP不足を拒否する()
        {Setup();var a=new CountAction();var list=new List<IEnemyAIAction>{a};var pattern=new EnemyHpActionPattern(10000,true,list);list.Clear();Assert.IsTrue(battle.ConfigureEnemyHpPatterns(enemy,new[]{pattern}));Frames(1);Assert.AreEqual(1,a.Count);Assert.Throws<System.ArgumentException>(()=>battle.ConfigureEnemyHpPatterns(enemy,new[]{pattern,pattern}));Assert.Throws<System.ArgumentException>(()=>battle.ConfigureEnemyHpPatterns(enemy,new[]{new EnemyHpActionPattern(5000,true,new[]{a})}));}
        [Test]
        public void スライム設定例は既存カードと指定待ちだけを使用する()
        {Setup();battle.TrySwitchEnemyActionPattern(enemy,EnemyActionPatternSamples.Slime(Make(Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile),2000));Frames(1);Assert.AreEqual(500,player.CurrentHp);Frames(1);Assert.AreEqual(493,player.CurrentHp);Frames(2);Assert.AreEqual(493,player.CurrentHp);}
        private sealed class Database:Olca.Core.Logic.CardEffects.ICardDatabase
        {public Card Value;public Card GetCardById(string id)=>Value?.Id==id?Value:null;}
        [TestCase(false)] [TestCase(true)]
        public void 別カード派生は発動位置を変えても敵使用者と対象側を保持する(bool hitOrigin)
        {Setup();var child=Make(Olca.Core.Logic.CardEffects.Effects.DamageTargetType.All);var m=new CardModifier();m.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.ChainCard,ChainCardId=child.Id,Power=100,EffectOrigin=hitOrigin?ChainEffectOrigin.HitPosition:ChainEffectOrigin.PlayerPosition,Trigger=DerivativeTrigger.OnHit});var source=new Card("parent","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,3,"",modifier:m,poiseDamage:0);new EnemyCardAction(source,0,new Database{Value=child}).Execute(Context());Assert.AreEqual(490,player.CurrentHp);Assert.AreEqual(100,enemy.CurrentHp);Assert.AreEqual(100,other.CurrentHp);}
        [Test]
        public void 遅延投擲の相対位置を敵の向きへ変換し着弾もプレイヤーだけに適用する()
        {Setup();var m=new CardModifier();m.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.DelayedProjectile,DelayTime=0.05f,TargetOffset=new Vector2Int(1,0)});new EnemyCardAction(Make(modifier:m),1000).Execute(Context());Assert.AreEqual(new Vector2Int(2,0),battle.DelayedEffects.GetActiveEffects()[0].TargetPosition);Assert.AreEqual(500,player.CurrentHp);Frames(3);Assert.AreEqual(493,player.CurrentHp);Assert.AreEqual(100,other.CurrentHp);}
        [Test]
        public void 敵の前方パネル効果はプレイヤーのマスへ適用する()
        {Setup();var m=new CardModifier();m.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.PanelIce});new EnemyCardAction(Make(Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile,category:CardCategory.Field,power:0,modifier:m),0).Execute(Context());Assert.AreEqual(PanelType.Ice,battle.Field.GetPanelType(player.GridPosition));Assert.AreEqual(PanelType.Normal,battle.Field.GetPanelType(other.GridPosition));}
        [Test]
        public void 未登録死亡終了後のHP設定とカード実行を拒否する()
        {Setup();var values=new[]{new EnemyHpActionPattern(10000,true,new[]{new CountAction()})};Assert.IsFalse(battle.ConfigureEnemyHpPatterns(new Enemy("x","テスト",100,ElementType.None,new Vector2Int(0,0)),values));enemy.TakeDamage(1000,ElementType.None);Assert.IsFalse(battle.ConfigureEnemyHpPatterns(enemy,values));Assert.IsFalse(new EnemyCardAction(Make(),0).CanExecute(Context()));}
        [Test]
        public void HPは同フレーム被弾と回復の確定値で選び中間の閾値通過では切り替えない()
        {Setup();var low=new CountAction();var high=new CountAction();battle.ConfigureEnemyHpPatterns(enemy,new[]{new EnemyHpActionPattern(5000,true,new[]{low}),new EnemyHpActionPattern(10000,true,new[]{high})});battle.RunLogicalFrame(()=>{enemy.TakeDamage(70,ElementType.None,poiseDamage:0);enemy.Heal(50);});Frames(1);Assert.AreEqual(80,enemy.CurrentHp);Assert.AreEqual(0,low.Count);Assert.AreEqual(1,high.Count);}
        [TestCase(false)] [TestCase(true)]
        public void 勝敗確定で残る前隙を解除し終了後に発動しない(bool defeat)
        {Setup();var a=new EnemyCardAction(Make(startup:10),0);battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{a});Frames(1);battle.RunLogicalFrame(()=>{if(defeat)player.TakeDamage(10000);else{enemy.TakeDamage(10000,ElementType.None);other.TakeDamage(10000,ElementType.None);}});Assert.IsTrue(battle.IsGameOver);Assert.IsFalse(a.IsRunning);Frames(20);Assert.IsFalse(a.IsRunning);}
        [Test]
        public void 派生前隙の待機中断は子カードの予約も解除する()
        {Setup();var child=Make(startup:5);var m=new CardModifier();m.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.ChainCard,ChainCardId=child.Id,Power=100,Trigger=DerivativeTrigger.Immediate});var parent=new Card("parent","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,0,"",modifier:m,extendRecoveryDuringDerivative:true,poiseDamage:0);var a=new EnemyCardAction(parent,1000,new Database{Value=child});battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{a});Frames(1);Assert.IsTrue(a.IsRunning);Assert.AreEqual(1,battle.DelayedEffects.ActiveEffectCount);enemy.TakeDamage(40,ElementType.None);Assert.IsFalse(a.IsRunning);Assert.AreEqual(0,battle.DelayedEffects.ActiveEffectCount);Frames(8);Assert.AreEqual(500,player.CurrentHp);}
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void HP切替とカード前後隙の結果は描画分割に依存しない(int fps)
        {Setup();var a=new EnemyCardAction(Make(startup:2,recovery:3),1000);var low=new CountAction();battle.ConfigureEnemyHpPatterns(enemy,new[]{new EnemyHpActionPattern(5000,true,new[]{low}),new EnemyHpActionPattern(10000,true,new IEnemyAIAction[]{a})});var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();long last=0;for(int i=1;i<=fps;i++){long now=i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps;clock.Accumulate(now-last);last=now;clock.Drain((frame,ms)=>{if(frame==2)enemy.TakeDamage(50,ElementType.None,poiseDamage:0);battle.TickFrame(ms);},3);}while(clock.PendingFrames>0)clock.Drain((frame,ms)=>{if(frame==2)enemy.TakeDamage(50,ElementType.None,poiseDamage:0);battle.TickFrame(ms);},3);Assert.AreEqual(493,player.CurrentHp);Assert.AreEqual(1,low.Count);}
        [TestCase(Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single)]
        [TestCase(Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile)]
        public void 前隙中にプレイヤーが離れたマスへ攻撃を追尾させない(Olca.Core.Logic.CardEffects.Effects.DamageTargetType target)
        {Setup();var a=new EnemyCardAction(Make(target,startup:3),1000);battle.TrySwitchEnemyActionPattern(enemy,new List<IEnemyAIAction>{a});Frames(1);player.MoveTo(new Vector2Int(1,0));Frames(3);Assert.AreEqual(500,player.CurrentHp);}
        [Test]
        public void 単体射撃は発動位置と同じマスを前方の相手として取得しない()
        {Setup();enemy.MoveTo(player.GridPosition);Assert.IsFalse(new EnemyCardAction(Make(),0).CanExecute(Context()));}
    }
}

namespace Olca.Tests.Battle
{
    [TestFixture]
    public class EnemyMotionBuffTelegraphTests
    {
        private PlayerCharacter player;private Enemy source,other;private BattleSystem battle;
        private sealed class Idle:IEnemyAIAction{public int Count;public bool CanExecute(AIContext c)=>true;public void Execute(AIContext c){Count++;}public int GetCooldownMs()=>100000;public float GetCooldown()=>100;}
        private void Setup(int sourceX=3)
        {player=new PlayerCharacter(500,new Vector2Int(1,0));source=new Enemy("source","テスト",100,ElementType.None,new Vector2Int(sourceX,0),poiseDefinition:new EnemyPoiseDefinition(30,3));other=new Enemy("other","テスト",100,ElementType.None,new Vector2Int(5,0),poiseDefinition:new EnemyPoiseDefinition(30,3));battle=new BattleSystem(player,new List<Enemy>{source,other},new Deck(),customGaugeSpeed:0,randomProvider:new MockRandomProvider());battle.TrySwitchEnemyActionPattern(source,new List<IEnemyAIAction>{new Idle()});battle.TrySwitchEnemyActionPattern(other,new List<IEnemyAIAction>{new Idle()});}
        private void Frames(int n){for(int i=0;i<n;i++)battle.TickFrame(17);}
        private Card Attack(int startup=6,int recovery=3,CardModifier modifier=null,Olca.Core.Logic.CardEffects.Effects.DamageTargetType target=Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single)
            =>new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,7,"",targetType:target,startupFrames:startup,recoveryFrames:recovery,poiseDamage:0,modifier:modifier);
        private Card Move(float move=.05f,float after=.05f,int distance=2,MovePenetrationMode mode=MovePenetrationMode.Invincible)
            =>new Card("move_fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,7,"",moveDirection:"Right",moveDistance:distance,moveMotionTime:move,attackMotionTime:after,penetrationMode:mode,poiseDamage:0);
        private AIContext Context()=>new AIContext(source,player,battle.Field,new MockRandomProvider(),null,battle);
        private void Install(EnemyCardAction a)=>battle.TrySwitchEnemyActionPattern(source,new List<IEnemyAIAction>{a});
        [Test]
        public void 敵集中は自身が所有し次の攻撃で一度だけ消費し回復では消費しない()
        {Setup();var focus=new Card("concentrate_test","テスト",1,CardRarity.Normal,CardCategory.Support,ElementType.None,CardSeries.Sword,100,"");new EnemyCardAction(focus,0).Execute(Context());Assert.IsTrue(source.IsConcentrating);Assert.IsFalse(player.IsConcentrating);Assert.IsFalse(other.IsConcentrating);new EnemyCardAction(new Card("heal_test","テスト",1,CardRarity.Normal,CardCategory.Defense,ElementType.None,CardSeries.Sword,5,""),0).Execute(Context());Assert.IsTrue(source.IsConcentrating);new EnemyCardAction(Attack(0,0),0).Execute(Context());Assert.AreEqual(486,player.CurrentHp);Assert.IsFalse(source.IsConcentrating);new EnemyCardAction(Attack(0,0),0).Execute(Context());Assert.AreEqual(479,player.CurrentHp);}
        [Test]
        public void 集中付与フレームでは旧倍率だけを使い次フレームに新倍率が確定する()
        {Setup();battle.RunLogicalFrame(()=>{source.SetConcentrateBuff(2);new EnemyCardAction(Attack(0,0),0).Execute(Context());Assert.IsFalse(source.IsConcentrating);});Assert.AreEqual(493,player.CurrentHp);Assert.IsTrue(source.IsConcentrating);new EnemyCardAction(Attack(0,0),0).Execute(Context());Assert.AreEqual(479,player.CurrentHp);}
        [TestCase(false)] [TestCase(true)]
        public void 死亡または離脱で敵集中を解除し次戦へ持ち越さない(bool abandon)
        {Setup();source.SetConcentrateBuff(2);if(abandon)typeof(BattleSystem).GetMethod("AbandonForCheckpoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(battle,null);else source.TakeDamage(1000,ElementType.None);Assert.IsFalse(source.IsConcentrating);}
        [Test]
        public void 敵一時移動は向きを反転し被弾座標だけを変えて完了後は元へ戻る()
        {Setup();var a=new EnemyCardAction(Move(),1000);Install(a);Frames(1);Assert.AreEqual(new Vector2Int(1,0),source.GridPosition);Assert.AreEqual(new Vector2Int(3,0),source.GetLogicalPosition());Assert.IsTrue(source.IsInTemporaryMove);Assert.AreEqual(500,player.CurrentHp);CollectionAssert.AreEqual(new[]{new Vector2Int(1,0)},battle.EnemyAttackTiles);Frames(3);Assert.AreEqual(493,player.CurrentHp);Assert.AreEqual(0,battle.EnemyAttackTiles.Count);Assert.IsTrue(source.IsInTemporaryMove);Frames(3);Assert.IsFalse(source.IsInTemporaryMove);Assert.AreEqual(new Vector2Int(3,0),source.GridPosition);}
        [Test]
        public void 一時移動先からプレイヤーが離れれば移動後の攻撃は追尾しない()
        {Setup();Install(new EnemyCardAction(Move(),1000));Frames(1);player.MoveTo(new Vector2Int(0,0));Frames(3);Assert.AreEqual(500,player.CurrentHp);Frames(3);Assert.AreEqual(new Vector2Int(3,0),source.GridPosition);}
        [TestCase(BattlePauseReason.User)] [TestCase(BattlePauseReason.CustomScreen)]
        public void 停止中は移動先と予告を保持し再開後に一度だけ命中する(BattlePauseReason reason)
        {Setup();Install(new EnemyCardAction(Move(),1000));Frames(1);battle.Pause(reason);Frames(20);Assert.IsTrue(source.IsInTemporaryMove);Assert.AreEqual(1,battle.EnemyAttackTiles.Count);Assert.AreEqual(500,player.CurrentHp);battle.Resume(reason);Frames(6);Assert.AreEqual(493,player.CurrentHp);Assert.IsFalse(source.IsInTemporaryMove);}
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void のけぞり死亡離脱で一時位置と予告と移動元保護を即解除する(int kind)
        {Setup();var a=new EnemyCardAction(Move(),1000);Install(a);Frames(1);if(kind==0)source.TakeDamage(40,ElementType.None);else if(kind==1)source.TakeDamage(1000,ElementType.None);else typeof(BattleSystem).GetMethod("AbandonForCheckpoint",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(battle,null);Assert.IsFalse(source.IsInTemporaryMove);Assert.AreEqual(new Vector2Int(3,0),source.GridPosition);Assert.AreEqual(0,battle.EnemyAttackTiles.Count);Frames(8);Assert.AreEqual(500,player.CurrentHp);}
        [Test]
        public void 移動元を穴に変更しても保護し同時二体の片方終了で他方の保護を失わない()
        {Setup(4);player.MoveTo(new Vector2Int(0,0));other.MoveTo(new Vector2Int(5,0));var a=new EnemyCardAction(Move(.05f,.05f,2),1000);var b=new EnemyCardAction(Move(.2f,.1f,2),1000);Install(a);battle.TrySwitchEnemyActionPattern(other,new List<IEnemyAIAction>{b});Frames(1);Assert.IsTrue(source.IsInTemporaryMove);Assert.IsTrue(other.IsInTemporaryMove);battle.Field.ChangePanelType(new Vector2Int(4,0),PanelType.Hole);battle.Field.ChangePanelType(new Vector2Int(5,0),PanelType.Hole);Assert.AreEqual(PanelType.Cracked,battle.Field.GetPanelType(new Vector2Int(4,0)));Assert.AreEqual(PanelType.Cracked,battle.Field.GetPanelType(new Vector2Int(5,0)));Frames(6);Assert.IsFalse(source.IsInTemporaryMove);Assert.IsTrue(other.IsInTemporaryMove);battle.Field.ChangePanelType(new Vector2Int(5,0),PanelType.Hole);Assert.AreEqual(PanelType.Cracked,battle.Field.GetPanelType(new Vector2Int(5,0)));}
        [Test]
        public void Blockingはプレイヤー論理位置の手前で止まり範囲外や穴へ侵入しない()
        {Setup();Install(new EnemyCardAction(Move(mode:MovePenetrationMode.Blocking),1000));Frames(1);Assert.AreEqual(new Vector2Int(2,0),source.GridPosition);Frames(6);Assert.AreEqual(500,player.CurrentHp);Assert.AreEqual(new Vector2Int(3,0),source.GridPosition);}
        [Test]
        public void 移動してもヒット判定用の敵座標にカードが命中する()
        {Setup();Install(new EnemyCardAction(Move(),1000));Frames(1);var attack=new Card("player_fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,7,"",targetType:Olca.Core.Logic.CardEffects.Effects.DamageTargetType.All,poiseDamage:0);new Olca.Core.Logic.CardEffects.Effects.ModifiableCardEffect(Olca.Core.Logic.CardEffects.Effects.DamageTargetType.All).Execute(new Olca.Core.Logic.CardEffects.BattleContext(battle,attack));Assert.AreEqual(93,source.CurrentHp);}
        [Test]
        public void 予告は開始時のマスに固定し発動前にプレイヤーが移動しても追尾しない()
        {Setup();Install(new EnemyCardAction(Attack(),1000));Frames(1);CollectionAssert.AreEqual(new[]{new Vector2Int(1,0)},battle.EnemyAttackTiles);player.MoveTo(new Vector2Int(0,0));Frames(2);CollectionAssert.AreEqual(new[]{new Vector2Int(1,0)},battle.EnemyAttackTiles);Frames(4);Assert.AreEqual(0,battle.EnemyAttackTiles.Count);Assert.AreEqual(500,player.CurrentHp);}
        [Test]
        public void 同一マスへの複数予告は一つに統合し一体中断後も他方の予告を維持する()
        {Setup();Install(new EnemyCardAction(Attack(10),1000));battle.TrySwitchEnemyActionPattern(other,new List<IEnemyAIAction>{new EnemyCardAction(Attack(15),1000)});Frames(1);Assert.AreEqual(1,battle.EnemyAttackTiles.Count);source.TakeDamage(40,ElementType.None);Assert.AreEqual(1,battle.EnemyAttackTiles.Count);other.TakeDamage(40,ElementType.None);Assert.AreEqual(0,battle.EnemyAttackTiles.Count);}
        [Test]
        public void 前隙ゼロの瞬時攻撃は予告時間を捏造せず同じ更新で解除する()
        {Setup();Install(new EnemyCardAction(Attack(0,0),1000));Frames(1);Assert.AreEqual(0,battle.EnemyAttackTiles.Count);Assert.AreEqual(493,player.CurrentHp);}
        [Test]
        public void 暗転とヒットストップは予告と移動待機を保持する()
        {Setup();Install(new EnemyCardAction(Move(),1000));Frames(1);battle.TryStartDarkeningEffect(DarkeningSide.Opponent,0,3,()=>{});Frames(3);Assert.AreEqual(1,battle.EnemyAttackTiles.Count);Assert.AreEqual(500,player.CurrentHp);battle.RunLogicalFrame(()=>source.TakeDamage(1,ElementType.None,poiseDamage:0,hitStopFrames:3));Frames(3);Assert.AreEqual(1,battle.EnemyAttackTiles.Count);Assert.AreEqual(500,player.CurrentHp);Frames(6);Assert.AreEqual(493,player.CurrentHp);Assert.AreEqual(0,battle.EnemyAttackTiles.Count);}
        [Test]
        public void 一時移動と集中を同じ行動列で反復しても二重移動やバフ残留を起こさない()
        {Setup();var focus=new Card("concentrate_test","テスト",1,CardRarity.Normal,CardCategory.Support,ElementType.None,CardSeries.Sword,100,"");battle.TrySwitchEnemyActionPattern(source,new List<IEnemyAIAction>{new EnemyCardAction(focus,0),new EnemyCardAction(Move(),0)});Frames(17);Assert.LessOrEqual(player.CurrentHp,472);Assert.AreEqual(new Vector2Int(3,0),source.GetLogicalPosition());}
        [Test]
        public void HP切替は一時移動攻撃を途中解除せず元位置へ戻った後に新行動を実行する()
        {Setup();var a=new EnemyCardAction(Move(),1000);var low=new Idle();Install(a);Frames(1);battle.ConfigureEnemyHpPatterns(source,new[]{new EnemyHpActionPattern(5000,true,new[]{low}),new EnemyHpActionPattern(10000,true,new IEnemyAIAction[]{a})});source.TakeDamage(50,ElementType.None,poiseDamage:0);Frames(5);Assert.AreEqual(0,low.Count);Assert.IsTrue(source.IsInTemporaryMove);Frames(1);Assert.IsFalse(source.IsInTemporaryMove);Assert.AreEqual(0,low.Count);Frames(1);Assert.AreEqual(1,low.Count);}
        [Test]
        public void 範囲外へ進まず穴の手前で止まり復帰不能な位置を作らない()
        {Setup();battle.Field.ChangePanelType(new Vector2Int(2,0),PanelType.Hole);Install(new EnemyCardAction(Move(mode:MovePenetrationMode.Blocking),1000));Frames(1);Assert.IsFalse(source.IsInTemporaryMove);Assert.AreEqual(new Vector2Int(3,0),source.GridPosition);Assert.AreEqual(0,battle.EnemyAttackTiles.Count);Frames(6);Assert.AreEqual(500,player.CurrentHp);}
        [Test]
        public void 敵詳細の集中はカナと実効倍率を表示しプレイヤーの状態を参照しない()
        {Setup();source.SetConcentrateBuff(2);string text=Olca.Presentation.CustomScreen.CustomDisplay.CombatantDetails(source);StringAssert.Contains("シュウチュウ: ツギノカード 2 バイ",text);Assert.IsFalse(player.IsConcentrating);Assert.IsFalse(other.IsConcentrating);}
        [TestCase(30)] [TestCase(60)] [TestCase(120)]
        public void 一時移動集中予告の反復は描画分割と追いつきで同じ論理状態になる(int fps)
        {
            string Replay(int hz){Setup();var focus=new Card("concentrate_test","テスト",1,CardRarity.Normal,CardCategory.Support,ElementType.None,CardSeries.Sword,100,"");battle.TrySwitchEnemyActionPattern(source,new List<IEnemyAIAction>{new EnemyCardAction(focus,0),new EnemyCardAction(Move(),100)});var clock=new Olca.Core.Logic.Integration.LogicalFrameClock();long last=0;var trace=new List<string>();void Step(long frame,int ms){if(frame==8)battle.Pause(BattlePauseReason.User);if(frame==12)battle.Resume(BattlePauseReason.User);battle.TickFrame(ms);trace.Add(player.CurrentHp+":"+source.GridPosition+":"+source.IsConcentrating+":"+battle.EnemyAttackTiles.Count);}for(int i=1;i<=hz;i++){long now=i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/hz;clock.Accumulate(now-last);last=now;clock.Drain(Step,3);}while(clock.PendingFrames>0)clock.Drain(Step,3);return string.Join("|",trace);}
            Assert.AreEqual(Replay(60),Replay(fps));
        }
        [TestCase(false)] [TestCase(true)]
        public void 新しい敵の途中状態を黙って保存せず解除後は同じ保存契約を保つ(bool temporary)
        {
            var map=DungeonConfig.CreateTestDungeon(8,30,7);var deck=new Deck();var defaults=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck);var hash=defaults.Definitions.MapHash;
            var store=new Olca.Core.Logic.Dungeon.MemoryCheckpointStore(defaults.Definitions);var session=new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defaults.Definitions,store);var manager=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),deck,new MockRandomProvider(),new MockLogger(),checkpoints:session);
            var capture=typeof(Olca.Core.Logic.Dungeon.DungeonCheckpointSession).GetMethod("Capture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            Olca.Core.Domain.CheckpointDto Capture()=>(Olca.Core.Domain.CheckpointDto)capture.Invoke(session,new object[]{manager.Dungeon,Olca.Core.Domain.CheckpointBoundary.Departure,null});
            var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(Capture());var enemy=map.MapCells[7].Enemy;
            if(temporary)enemy.MoveTemporarily(new Vector2Int(4,0));else enemy.SetConcentrateBuff(2);
            Assert.Throws<System.ArgumentException>(()=>Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck));
            var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>Capture());Assert.IsInstanceOf<System.ArgumentException>(error.InnerException);
            if(temporary)enemy.ReturnToOriginalPosition();else enemy.SetConcentrateBuff(1);
            CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Encode(Capture()));Assert.AreEqual(hash,Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck).Definitions.MapHash);
        }
    }
}
