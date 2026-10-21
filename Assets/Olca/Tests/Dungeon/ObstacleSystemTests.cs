using NUnit.Framework;
using System.Linq;
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


namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class ObstacleVisualLifecycleTests
    {
        private UnityEngine.GameObject root,cameraObject;
        private UnityEngine.Camera camera;
        private Olca.Presentation.Field.GameFieldView view;
        private Olca.Core.Logic.Integration.DungeonBattleManager game;
        private void Setup(ObstacleControl control=null)
        {
            var cells=new System.Collections.Generic.List<MapCell>();for(int i=0;i<20;i++)cells.Add(new MapCell(i));
            cells[2]=new MapCell(2,new Obstacle("visual","表示",0,control));cells[8]=new MapCell(8,Obstacle.CreateCardDiscardObstacle("card","破棄",1));
            game=new Olca.Core.Logic.Integration.DungeonBattleManager(new DungeonConfig("visual","表示",20,cells),new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger());
            cameraObject=new UnityEngine.GameObject("VisualTestCamera");camera=cameraObject.AddComponent<UnityEngine.Camera>();camera.orthographic=true;camera.aspect=16f/9;camera.transform.position=new UnityEngine.Vector3(0,0,-10);
            root=new UnityEngine.GameObject("VisualTestRoot");view=root.AddComponent<Olca.Presentation.Field.GameFieldView>();typeof(Olca.Presentation.Field.GameFieldView).GetField("fieldCamera",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(view,camera);view.Initialize(game.Dungeon);
        }
        private UnityEngine.Transform Cell(int cell)=>root.transform.Find("ObstacleCell_"+cell);
        private void Tick(int frames){for(int i=0;i<frames;i++)game.TickFrame(17);view.RefreshObstacleVisuals();}
        [TearDown]public void Cleanup(){if(root!=null)UnityEngine.Object.DestroyImmediate(root);if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);}
        [Test]
        public void 画面内だけ生成しセル範囲と接触端を示しカメラ再入場でも二重表示しない()
        {
            Setup();Assert.IsNotNull(Cell(2));Assert.IsNull(Cell(8));var marker=Cell(2).Find("Contact");Assert.That(marker.position.x,Is.EqualTo(2.02f).Within(.001));var area=Cell(2).Find("Area").GetComponent<UnityEngine.SpriteRenderer>();Assert.LessOrEqual(area.bounds.max.x,3);Assert.GreaterOrEqual(area.bounds.min.x,2);Assert.Less(area.color.a,.2);
            for(int i=0;i<5;i++)view.RefreshObstacleVisuals();Assert.AreEqual(1,root.GetComponentsInChildren<UnityEngine.SpriteRenderer>().Count(r=>r.name=="Contact"));camera.transform.position=new UnityEngine.Vector3(9,0,-10);view.RefreshObstacleVisuals();Assert.IsNull(Cell(2));Assert.IsNotNull(Cell(8));camera.transform.position=new UnityEngine.Vector3(2.5f,0,-10);view.RefreshObstacleVisuals();Assert.IsNotNull(Cell(2));Assert.IsNull(Cell(8));
        }
        [Test]
        public void 接触消滅は論理時計で進み描画だけとポーズ中には進まない()
        {
            Setup();for(int i=0;i<60&&!game.Dungeon.IsObstacleConsumed(2);i++)Tick(1);var renderer=Cell(2).Find("Contact").GetComponent<UnityEngine.SpriteRenderer>();var color=renderer.color;long frame=game.Dungeon.ObstacleVisualFrame;for(int i=0;i<100;i++)view.RefreshObstacleVisuals();Assert.AreEqual(color,renderer.color);Assert.AreEqual(frame,game.Dungeon.ObstacleVisualFrame);
            game.SetUserPaused(true);Tick(50);Assert.AreEqual(color,renderer.color);game.SetUserPaused(false);Tick(6);Assert.That(renderer.color.a,Is.EqualTo(color.a*.5f).Within(.001));Tick(6);Assert.IsNull(Cell(2));camera.transform.position=new UnityEngine.Vector3(9,0,-10);view.RefreshObstacleVisuals();camera.transform.position=new UnityEngine.Vector3(2.5f,0,-10);view.RefreshObstacleVisuals();Assert.IsNull(Cell(2),"消費済みセルの消滅を再生しない");
        }
        [Test]
        public void 拘束中は危険表示を保持し解除から消滅し再初期化や無効化で残像を残さない()
        {
            Setup(new ObstacleControl(18,true,1,6));for(int i=0;i<60&&!game.Dungeon.IsRestrained;i++)Tick(1);Assert.AreEqual(2,game.Dungeon.ActiveObstacleCell);Tick(13);Assert.IsNotNull(Cell(2));var renderer=Cell(2).Find("Contact").GetComponent<UnityEngine.SpriteRenderer>();Assert.Greater(renderer.color.r,renderer.color.g);view.Initialize(game.Dungeon);Assert.IsNotNull(Cell(2));Tick(5);Assert.IsFalse(game.Dungeon.IsRestrained);Assert.AreEqual(game.Dungeon.ObstacleVisualFrame,game.Dungeon.GetObstacleFadeStartFrame(2));Tick(12);Assert.IsNull(Cell(2));view.Initialize(game.Dungeon);Assert.IsNull(Cell(2));view.enabled=false;Assert.IsNull(Cell(2));view.enabled=true;Assert.IsNull(Cell(2));
        }
        [Test]
        public void 新しいモデルへの交換は古い定義と演出を破棄して一度だけ生成する()
        {
            Setup();var old=Cell(2).gameObject;var cells=new System.Collections.Generic.List<MapCell>();for(int i=0;i<20;i++)cells.Add(new MapCell(i));cells[2]=new MapCell(2,Obstacle.CreateCardDiscardObstacle("new","新定義",0));var replacement=new DungeonSystem(new DungeonConfig("new","新",20,cells),new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger());view.Initialize(replacement);Assert.IsTrue(old==null);Assert.IsNotNull(Cell(2).Find("CardA"));Assert.IsNull(Cell(2).Find("Warning"));Assert.AreEqual(1,root.GetComponentsInChildren<UnityEngine.SpriteRenderer>().Count(r=>r.name=="Contact"));
        }
    }
}

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class VisitMarkerLifecycleTests
    {
        private UnityEngine.GameObject root,cameraObject;
        private UnityEngine.Camera camera;
        private Olca.Presentation.Field.GameFieldView view;
        private Olca.Core.Logic.Integration.DungeonBattleManager game;
        private DungeonCheckpointSession session;
        private void Setup()
        {
            var cells=Enumerable.Range(0,12).Select(i=>new MapCell(i)).ToList();var card=new Olca.Application.Battle.SimpleCardFactory().CreateById("sword");
            var map=new DungeonConfig("marker","表示",12,cells,visits:new[]{new DungeonVisitDefinition(2,true,new[]{new DungeonVisitOption("sword",10,card)},1),new DungeonVisitDefinition(8,false,new[]{new DungeonVisitOption("heal",0,heal:20)},1)});
            var initial=DungeonCheckpointSession.CreateDefault(map,new Deck());new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger(),checkpoints:initial);
            var data=initial.Store.Read();data.Currency=20;data.Player.Hp=60;Assert.IsTrue(CheckpointCodec.TryRestore(data,initial.Definitions,out var state,out var error),error);
            var store=new MemoryCheckpointStore(initial.Definitions);Assert.IsTrue(store.TryWrite(data,out error),error);session=new DungeonCheckpointSession(initial.Definitions,store);
            game=new Olca.Core.Logic.Integration.DungeonBattleManager(state.Map,state.Player,state.Deck,new MockRandomProvider(),new MockLogger(),checkpoints:session,restoration:state);
            cameraObject=new UnityEngine.GameObject("VisitCamera");camera=cameraObject.AddComponent<UnityEngine.Camera>();camera.orthographic=true;camera.aspect=16f/9;camera.transform.position=new UnityEngine.Vector3(0,0,-10);
            root=new UnityEngine.GameObject("VisitView");view=root.AddComponent<Olca.Presentation.Field.GameFieldView>();typeof(Olca.Presentation.Field.GameFieldView).GetField("fieldCamera",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(view,camera);view.Initialize(game.Dungeon);
        }
        private UnityEngine.Transform Cell(int cell)=>root.transform.Find("VisitCell_"+cell);
        [TearDown]public void Cleanup(){if(root!=null)UnityEngine.Object.DestroyImmediate(root);if(cameraObject!=null)UnityEngine.Object.DestroyImmediate(cameraObject);}
        [Test]
        public void 停止地点マーカーは画面内だけ生成し形状を分け再入場でも二重表示しない()
        {
            Setup();Assert.IsNotNull(Cell(2));Assert.IsNull(Cell(8));Assert.IsNotNull(Cell(2).Find("BagHandle"));
            var face=Cell(2).Find("Face").GetComponent<UnityEngine.SpriteRenderer>();Assert.That(face.bounds.size.x,Is.EqualTo(.46f).Within(.001));Assert.Less(face.color.a,.1f);
            for(int i=0;i<10;i++)view.RefreshVisitVisuals();Assert.AreEqual(1,root.GetComponentsInChildren<UnityEngine.SpriteRenderer>().Count(r=>r.name=="BagHandle"));
            camera.transform.position=new UnityEngine.Vector3(9,0,-10);view.RefreshVisitVisuals();Assert.IsNull(Cell(2));Assert.IsNotNull(Cell(8).Find("RecoveryHorizontal"));Assert.IsFalse(Cell(8).Find("Near").gameObject.activeSelf);
            camera.transform.position=new UnityEngine.Vector3(2.5f,0,-10);view.RefreshVisitVisuals();Assert.IsNotNull(Cell(2));Assert.IsNull(Cell(8));Assert.IsTrue(Cell(2).Find("Near").gameObject.activeSelf);
        }
        [Test]
        public void 接近入場使用退出と保存復帰の表示はゲーム状態を進めず停止中も変化しない()
        {
            Setup();var d=game.Dungeon;long seq=session.Store.Read().Sequence;var color=Cell(2).Find("Top").GetComponent<UnityEngine.SpriteRenderer>().color;
            for(int i=0;i<100;i++)view.RefreshVisitVisuals();Assert.AreEqual(0,d.PlayerPosition);Assert.AreEqual(20,d.Currency);Assert.AreEqual(60,d.Player.CurrentHp);Assert.AreEqual(seq,session.Store.Read().Sequence);
            while(d.ActiveVisit==null)game.TickFrame(17);view.RefreshVisitVisuals();Assert.AreEqual(DungeonVisitDisplayState.Active,d.GetVisitDisplayState(2));Assert.IsFalse(Cell(2).Find("Near").gameObject.activeSelf);
            game.SetUserPaused(true);var pos=d.PlayerPosition;color=Cell(2).Find("Top").GetComponent<UnityEngine.SpriteRenderer>().color;
            for(int i=0;i<30;i++){game.TickFrame(17);view.RefreshVisitVisuals();}Assert.AreEqual(pos,d.PlayerPosition);Assert.AreEqual(color,Cell(2).Find("Top").GetComponent<UnityEngine.SpriteRenderer>().color);game.SetUserPaused(false);
            Assert.IsTrue(d.ChooseVisit(0));view.RefreshVisitVisuals();Assert.AreEqual(DungeonVisitDisplayState.Used,d.GetVisitDisplayState(2));Assert.IsTrue(Cell(2).Find("Check").gameObject.activeSelf);
            Assert.IsTrue(d.ExitVisit());view.RefreshVisitVisuals();Assert.AreEqual(DungeonVisitDisplayState.Closed,d.GetVisitDisplayState(2));Assert.Less(Cell(2).Find("Top").GetComponent<UnityEngine.SpriteRenderer>().color.a,.5f);
            Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(new DungeonCheckpointSession(session.Definitions,session.Store),new MockRandomProvider(),new MockLogger(),out var restored,out var error),error);
            view.Initialize(restored.Dungeon);Assert.IsTrue(Cell(2).Find("Check").gameObject.activeSelf);Assert.IsFalse(Cell(2).Find("Near").gameObject.activeSelf);Assert.AreEqual(10,restored.Dungeon.Currency);Assert.AreEqual(1,restored.Dungeon.GetObtainedCards().Count);
        }
        [Test]
        public void 無効化とモデル交換で古いマーカーを破棄し再有効化で一度だけ戻す()
        {
            Setup();var old=Cell(2).gameObject;var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;view.enabled=false;view.GetType().GetMethod("OnDisable",flags).Invoke(view,null);view.RefreshVisitVisuals();Assert.IsNull(Cell(2));Assert.IsTrue(old==null);view.enabled=true;view.GetType().GetMethod("OnEnable",flags).Invoke(view,null);Assert.IsNotNull(Cell(2));
            var cells=Enumerable.Range(0,12).Select(i=>new MapCell(i)).ToList();var replacement=new DungeonSystem(new DungeonConfig("empty","空",12,cells),new PlayerCharacter(100),new Deck(),new MockRandomProvider(),new MockLogger());
            view.Initialize(replacement);Assert.IsNull(Cell(2));Assert.IsNull(Cell(8));Assert.AreEqual(0,root.GetComponentsInChildren<UnityEngine.SpriteRenderer>().Length);
        }
    }
}
