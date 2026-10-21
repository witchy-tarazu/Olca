using NUnit.Framework;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Domain;
using Olca.Tests.Mocks;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class ObstacleSystemTests
    {
        private MockLogger mockLogger;

        [SetUp]
        public void SetUp()
        {
            mockLogger = new MockLogger();
        }

        [Test]
        public void CheckObstacleContact_障害物セルでない場合_接触しない()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var emptyCell = new MapCell(0);  // 空白マス

            // Act
            var result = obstacleSystem.CheckObstacleContact(emptyCell, 0f, 0f);

            // Assert
            Assert.IsFalse(result.IsContact, "空セルでは接触しない");
        }

        [Test]
        public void CheckObstacleContact_障害物から離れている_接触しない()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("spike", "トゲ", 10);  // 即時ダメージ障害物
            var obstacleCell = new MapCell(5, obstacle, height: 0f);

            // Act: プレイヤーは位置0（障害物は位置5）
            var result = obstacleSystem.CheckObstacleContact(obstacleCell, 0f, 0f);

            // Assert
            Assert.IsFalse(result.IsContact, "遠すぎて接触しない");
        }

        [Test]
        public void CheckObstacleContact_ジャンプ中_回避できる()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("spike", "トゲ", 10);  // 即時ダメージ障害物
            var obstacleCell = new MapCell(5, obstacle, height: 0f);

            // Act: プレイヤーは位置5.5でジャンプ中（高さ1.0）
            var result = obstacleSystem.CheckObstacleContact(obstacleCell, 5.5f, 1.0f);

            // Assert
            Assert.IsTrue(result.IsContact, "接触範囲内");
            Assert.IsTrue(result.IsAvoided, "ジャンプで回避");
            Assert.AreEqual(obstacle, result.Obstacle);
        }

        [Test]
        public void CheckObstacleContact_ジャンプしていない_回避失敗()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("spike", "トゲ", 10);  // 即時ダメージ障害物
            var obstacleCell = new MapCell(5, obstacle, height: 0f);

            // Act: プレイヤーは位置5.5で地上（高さ0）
            var result = obstacleSystem.CheckObstacleContact(obstacleCell, 5.5f, 0f);

            // Assert
            Assert.IsTrue(result.IsContact, "接触範囲内");
            Assert.IsFalse(result.IsAvoided, "回避失敗");
            Assert.AreEqual(obstacle, result.Obstacle);
        }

        [Test]
        public void CreatePenaltyInfo_即時ダメージ_ダメージ情報を生成()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("spike", "トゲ", 10);  // 即時ダメージ障害物

            // Act
            var penalty = obstacleSystem.CreatePenaltyInfo(obstacle);

            // Assert
            Assert.AreEqual(ObstaclePenaltyType.ImmediateDamage, penalty.PenaltyType);
            Assert.AreEqual(10, penalty.Damage);
            Assert.AreEqual(0, penalty.CardDiscardCount);
        }

        [Test]
        public void CreatePenaltyInfo_状態異常_状態異常情報を生成()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = new Obstacle("poison", "毒沼", "poison_effect");  // 状態異常障害物

            // Act
            var penalty = obstacleSystem.CreatePenaltyInfo(obstacle);

            // Assert
            Assert.AreEqual(ObstaclePenaltyType.StatusEffect, penalty.PenaltyType);
            Assert.AreEqual(0, penalty.Damage);
            Assert.AreEqual("poison_effect", penalty.StatusEffectId);
        }

        [Test]
        public void CreatePenaltyInfo_カード破棄_破棄枚数情報を生成()
        {
            // Arrange
            var obstacleSystem = new ObstacleSystem(mockLogger);
            var obstacle = Obstacle.CreateCardDiscardObstacle("trap", "罠", 2);  // カード破棄障害物

            // Act
            var penalty = obstacleSystem.CreatePenaltyInfo(obstacle);

            // Assert
            Assert.AreEqual(ObstaclePenaltyType.CardDiscard, penalty.PenaltyType);
            Assert.AreEqual(2, penalty.CardDiscardCount);
            Assert.AreEqual(0, penalty.Damage);
        }
    }
}


namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class ObstacleControlTests
    {
        private static DungeonConfig Map(ObstacleControl control, int damage=0)
        {
            var m=DungeonConfig.CreateTestDungeon(8,30,7);m.MapCells[2]=new MapCell(2,new Obstacle("old","旧",damage,control));return m;
        }
        private static Olca.Core.Logic.Integration.DungeonBattleManager Game(ObstacleControl control, int hp=100, int damage=0)
            => new Olca.Core.Logic.Integration.DungeonBattleManager(Map(control,damage),new PlayerCharacter(hp),new Deck(),new MockRandomProvider(),new MockLogger());
        private static void Tick(Olca.Core.Logic.Integration.DungeonBattleManager game,int frames)
        { for(int i=0;i<frames;i++)game.TickFrame(17); }
        private static void Contact(Olca.Core.Logic.Integration.DungeonBattleManager game)
        { for(int i=0;i<60&&!game.Dungeon.IsObstacleStopped;i++)game.TickFrame(17);Assert.IsTrue(game.Dungeon.IsObstacleStopped); }
        [Test]
        public void 通常接触は一度だけ拘束しダッシュ解除とジャンプ拒否を行い期限後に前進する()
        {
            var g=Game(new ObstacleControl(6,true,5,2));int notices=0;g.Dungeon.OnObstaclePenalty+=_=>notices++;g.StartDash();Contact(g);
            Assert.IsTrue(g.Dungeon.IsRestrained);Assert.IsFalse(g.Dungeon.IsDashing);Assert.IsTrue(g.Dungeon.IsObstacleConsumed(2));Assert.IsFalse(g.Dungeon.IsExplorationMoving);float x=g.Dungeon.PlayerPosition;
            g.Jump();g.StartDash();Assert.IsFalse(g.Dungeon.IsJumping);Assert.IsFalse(g.Dungeon.IsDashing);Tick(g,5);Assert.AreEqual(90,g.Dungeon.Player.CurrentHp);Assert.AreEqual(x,g.Dungeon.PlayerPosition);
            Tick(g,1);Assert.IsFalse(g.Dungeon.IsObstacleStopped);Assert.AreEqual(85,g.Dungeon.Player.CurrentHp);Assert.AreEqual(x,g.Dungeon.PlayerPosition);Tick(g,1);Assert.Greater(g.Dungeon.PlayerPosition,x);Assert.AreEqual(1,notices);
        }
        [Test]
        public void のけぞりだけの設定も停止して自動解除しジャンプ回避なら開始しない()
        {
            var g=Game(new ObstacleControl(3));Contact(g);Assert.IsFalse(g.Dungeon.IsRestrained);Tick(g,3);Assert.IsFalse(g.Dungeon.IsObstacleStopped);Assert.AreEqual(100,g.Dungeon.Player.CurrentHp);
            g=Game(new ObstacleControl(6,true,5,2));g.Jump();Tick(g,18);Assert.IsTrue(g.Dungeon.IsObstacleConsumed(2));Assert.IsFalse(g.Dungeon.IsObstacleStopped);Assert.AreEqual(100,g.Dungeon.Player.CurrentHp);
        }
        [Test]
        public void ユーザーポーズは拘束時計と周期ダメージを止め復帰は残りから一度ずつ進む()
        {
            var g=Game(new ObstacleControl(6,true,5,2));Contact(g);Tick(g,1);float x=g.Dungeon.PlayerPosition;g.SetUserPaused(true);Tick(g,120);Assert.AreEqual(5,g.Dungeon.ObstacleStopRemainingFrames);Assert.AreEqual(100,g.Dungeon.Player.CurrentHp);Assert.AreEqual(x,g.Dungeon.PlayerPosition);
            g.SetUserPaused(false);Tick(g,5);Assert.AreEqual(85,g.Dungeon.Player.CurrentHp);Assert.IsFalse(g.Dungeon.IsObstacleStopped);
        }
        [Test]
        public void 拘束ダメージは被弾無敵バリアと毒のHP1制限を通らず同フレームを一度集計する()
        {
            var g=Game(new ObstacleControl(6,true,5,2));Contact(g);var p=g.Dungeon.Player;p.SetInvincible(100000);p.SetBarrier(100);p.ApplyPoison("test",new StatusEffect(StatusEffectType.Poison,1000,3),2);int changes=0;p.OnHpChanged+=(_,__)=>changes++;
            Tick(g,2);Assert.AreEqual(92,p.CurrentHp);Assert.AreEqual(1,changes);Assert.AreEqual(100,p.BarrierHp);Assert.IsFalse(p.IsStaggered);
            var fatal=Game(new ObstacleControl(3,true,10,1),hp:10);int deaths=0;fatal.Dungeon.Player.OnDeath+=()=>deaths++;Contact(fatal);Tick(fatal,1);Assert.AreEqual(0,fatal.Dungeon.Player.CurrentHp);Assert.AreEqual(Olca.Core.Logic.Integration.GamePhase.GameOver,fatal.CurrentPhase);Assert.IsFalse(fatal.Dungeon.IsObstacleStopped);Tick(fatal,10);Assert.AreEqual(1,deaths);
        }
        [Test]
        public void 接触のHP通知は合算確定後で再入しても二重接触や入力を受け付けない()
        {
            var g=Game(new ObstacleControl(3,true),damage:5);int notices=0;g.Dungeon.OnObstaclePenalty+=_=>{notices++;Assert.AreEqual(95,g.Dungeon.Player.CurrentHp);g.TickFrame(17);g.StartDash();g.Jump();};Contact(g);Assert.AreEqual(1,notices);Assert.AreEqual(3,g.Dungeon.ObstacleStopRemainingFrames);Assert.IsFalse(g.Dungeon.IsJumping);Assert.IsFalse(g.Dungeon.IsDashing);
        }
        [Test]
        public void 旧障害物hashを維持し新設定は複製と照合へ含める()
        {
            var m=Map(null,5);m.MapCells[3]=new MapCell(3,Obstacle.CreateCardDiscardObstacle("discard","破棄",1));var old=DungeonCheckpointSession.CreateDefault(m,new Deck());
            Assert.AreEqual("1617493362E71E5942C81F3E63350B330F9DC790DBE28B859C94276B279F76DB",old.Definitions.MapHash);
            var modern=DungeonCheckpointSession.CreateDefault(Map(new ObstacleControl(6,true,5,2),5),new Deck());Assert.AreNotEqual(old.Definitions.MapHash,modern.Definitions.MapHash);Assert.AreNotEqual(modern.Definitions.MapHash,DungeonCheckpointSession.CreateDefault(Map(new ObstacleControl(7,true,5,2),5),new Deck()).Definitions.MapHash);
            Assert.Throws<System.ArgumentException>(()=>new ObstacleControl(0));Assert.Throws<System.ArgumentException>(()=>new ObstacleControl(1,true,-1,1));Assert.Throws<System.ArgumentException>(()=>new ObstacleControl(1,true,1,0));Assert.Throws<System.ArgumentException>(()=>new ObstacleControl(1,false,1,1));
        }
        [Test]
        public void 中断再開は旧チェックポイントからで途中拘束やダメージを保存しない()
        {
            var map=Map(new ObstacleControl(6,true,5,2));var initial=DungeonCheckpointSession.CreateDefault(map,new Deck());var store=new MemoryCheckpointStore(initial.Definitions);var session=new DungeonCheckpointSession(initial.Definitions,store);var g=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);
            var saved=store.Read();Contact(g);Tick(g,2);Assert.AreEqual(95,g.Dungeon.Player.CurrentHp);Assert.AreEqual(saved.Sequence,store.Read().Sequence);Assert.AreEqual(saved.PositionMilli,store.Read().PositionMilli);
            Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);Assert.IsTrue(g.SuspendForCheckpoint());Assert.IsFalse(g.Dungeon.IsObstacleStopped);restored.ActivateCheckpoint();Assert.AreEqual(100,restored.Dungeon.Player.CurrentHp);Assert.IsFalse(restored.Dungeon.IsObstacleStopped);Assert.IsFalse(restored.Dungeon.IsObstacleConsumed(2));Contact(restored);Assert.AreEqual(6,restored.Dungeon.ObstacleStopRemainingFrames);
        }
        private sealed class FailingStore:ICheckpointStore
        {
            private readonly MemoryCheckpointStore store;public bool Fail;public FailingStore(CheckpointDefinitions d){store=new MemoryCheckpointStore(d);}public CheckpointDto Read()=>store.Read();public bool TryWrite(CheckpointDto value,out string error){if(Fail){error="fixture failure";return false;}return store.TryWrite(value,out error);}
        }
        [Test]
        public void 拘束死亡の保存失敗は停止して再試行後に一度だけ敗北を通知する()
        {
            var map=Map(new ObstacleControl(6,true,10,1));var initial=DungeonCheckpointSession.CreateDefault(map,new Deck());var store=new FailingStore(initial.Definitions);var session=new DungeonCheckpointSession(initial.Definitions,store);var g=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(10),new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);int deaths=0;g.Dungeon.OnDungeonFailed+=()=>deaths++;Contact(g);store.Fail=true;Tick(g,1);Assert.IsTrue(g.Dungeon.IsCheckpointBlocked);Assert.IsFalse(g.Dungeon.IsObstacleStopped);Tick(g,20);Assert.AreEqual(0,deaths);store.Fail=false;Assert.IsTrue(g.RetryCheckpoint());Assert.AreEqual(1,deaths);Assert.AreEqual(Olca.Core.Logic.Integration.GamePhase.GameOver,g.CurrentPhase);Assert.IsFalse(g.RetryCheckpoint());
        }
        [Test]
        public void 拘束解除後の通常接敵と保存復帰へ時計を持ち越さない()
        {
            var map=Map(new ObstacleControl(6,true,1,2));var initial=DungeonCheckpointSession.CreateDefault(map,new Deck());var store=new MemoryCheckpointStore(initial.Definitions);var session=new DungeonCheckpointSession(initial.Definitions,store);var g=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:session);Contact(g);for(int i=0;i<120&&g.CurrentBattle==null;i++)Tick(g,1);Assert.IsNotNull(g.CurrentBattle);Assert.IsFalse(g.Dungeon.IsObstacleStopped);Assert.AreEqual(97,g.Dungeon.Player.CurrentHp);Assert.IsTrue(g.Dungeon.IsObstacleConsumed(2));
            Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(session,new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);g.SuspendForCheckpoint();restored.ActivateCheckpoint();Assert.IsNotNull(restored.CurrentBattle);Assert.IsTrue(restored.Dungeon.IsObstacleConsumed(2));Assert.IsFalse(restored.Dungeon.IsObstacleStopped);Assert.AreEqual(97,restored.Dungeon.Player.CurrentHp);
        }
        [Test]
        public void 拘束と毒の同一フレーム処理は描画速度と追いつきに依存しない()
        {
            System.Collections.Generic.List<string> Trace(int fps){var g=Game(new ObstacleControl(20,true,2,3));var result=new System.Collections.Generic.List<string>();var c=new Olca.Core.Logic.Integration.LogicalFrameClock();long wall=0;void Frame(long f,int ms){if(f==15)g.SetUserPaused(true);if(f==20)g.SetUserPaused(false);g.TickFrame(ms);result.Add(g.Dungeon.PlayerPosition+":"+g.Dungeon.Player.CurrentHp+":"+g.Dungeon.ObstacleStopRemainingFrames);}if(fps==0){c.Accumulate(Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond);c.Drain(Frame);}else for(int i=1;c.Frame<60;i++){long t=(long)i*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps;c.Accumulate(t-wall);wall=t;c.Drain(Frame);}return result;}
            CollectionAssert.AreEqual(Trace(60),Trace(30));CollectionAssert.AreEqual(Trace(60),Trace(120));CollectionAssert.AreEqual(Trace(60),Trace(0));
        }
    }
}
