using Olca.Core.Logic.Integration;
using NUnit.Framework;
using Olca.Core.Logic.Dungeon;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class PlayerMovementTests
    {
        [TestCase(false)] [TestCase(true)]
        public void 通常シーン用の減速値はダッシュ速度とジャンプ期間を変えず固定時間で進む(bool dash)
        {
            var p=new PlayerMovement();p.ConfigureNormalSpeed(8000);if(dash)p.StartDash();
            for(int n=0;n<60;n++)p.Update(n%3==0?16:17);
            Assert.AreEqual(dash?20000:8000,p.CurrentPositionMilli);
            var jump=new PlayerMovement();jump.ConfigureNormalSpeed(8000);jump.Jump();jump.Update(250);
            Assert.IsTrue(jump.IsJumping);Assert.AreEqual(1500,jump.HeightMilli);jump.Update(250);
            Assert.IsFalse(jump.IsJumping);Assert.AreEqual(0,jump.HeightMilli);Assert.AreEqual(4000,jump.CurrentPositionMilli);
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ConfigureNormalSpeed(0));
        }

        [TestCase(9)] [TestCase(12)]
        public void 戦闘移動は中間を隠して移動前後に表示しSpriteの形と色を変えない(int frames)
        {
            var root=new UnityEngine.GameObject("battle-blink");root.SetActive(false);
            var view=root.AddComponent<Olca.Presentation.Field.GameFieldView>();var sr=root.AddComponent<UnityEngine.SpriteRenderer>();
            var texture=new UnityEngine.Texture2D(8,16);var sprite=UnityEngine.Sprite.Create(texture,new UnityEngine.Rect(0,0,8,16),UnityEngine.Vector2.zero);
            sr.sprite=sprite;sr.color=new UnityEngine.Color(.6f,.7f,.8f,1);root.transform.localScale=new UnityEngine.Vector3(-1,1,1);
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(Olca.Presentation.Field.GameFieldView).GetField("playerSprite",flags).SetValue(view,sr);
            var apply=typeof(Olca.Presentation.Field.GameFieldView).GetMethod("SetBattleMoveVisibility",flags);
            try{
                for(int f=1;f<=frames;f++){
                    bool visible=Olca.Presentation.Field.GameFieldView.IsBattleMoveVisible(true,(float)f/frames);
                    apply.Invoke(view,new object[]{visible});
                    if(f==Olca.Core.Logic.Battle.NormalMoveTiming.Midpoint(frames))Assert.IsFalse(sr.enabled,"占有切替時は消失中");
                    Assert.AreSame(sprite,sr.sprite);Assert.AreEqual(new UnityEngine.Vector3(-1,1,1),root.transform.localScale);Assert.AreEqual(new UnityEngine.Color(.6f,.7f,.8f,1),sr.color);
                }
                Assert.IsTrue(sr.enabled);apply.Invoke(view,new object[]{false});apply.Invoke(view,new object[]{true});Assert.IsTrue(sr.enabled);
            }finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
        }

        [TestCase(false)][TestCase(true)]
        public void ノックバック姿勢は順番を守り終了時に元の表示状態を復元する(bool enabled)
        {
            var go = new UnityEngine.GameObject("knockback-test");
            var texture = new UnityEngine.Texture2D(256, 256);
            var sprite = UnityEngine.Sprite.Create(texture, new UnityEngine.Rect(0, 0, 256, 256), new UnityEngine.Vector2(127f / 256, 14f / 256), 100);
            try {
                var view = go.AddComponent<Olca.Presentation.Field.GameFieldView>();
                var renderer = go.AddComponent<UnityEngine.SpriteRenderer>(); var animator = go.AddComponent<UnityEngine.Animator>(); animator.enabled = enabled;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                void Set(string name, object value) => typeof(Olca.Presentation.Field.GameFieldView).GetField(name, flags).SetValue(view, value);
                Set("playerSprite", renderer); Set("playerAnimator", animator); Set("contactKnockbackSprites", new[] { sprite, sprite, sprite });
                go.transform.position = new UnityEngine.Vector3(3, 2, 0); go.transform.localScale = new UnityEngine.Vector3(-1, 1, 1);
                var phase = Olca.Presentation.Dungeon.ContactVisualPhase.Impact;
                Assert.AreEqual(0, Olca.Presentation.Field.GameFieldView.SelectContactFrame(phase, 0, 3));
                view.SetContactPose(phase, 0);
                phase = Olca.Presentation.Dungeon.ContactVisualPhase.Knockback;
                for (int frame = 1; frame <= 12; frame++) {
                    float progress = frame / 12f;
                    Assert.AreEqual(frame <= 6 ? 1 : 2, Olca.Presentation.Field.GameFieldView.SelectContactFrame(phase, progress, 3));
                    view.SetContactPose(phase, progress); Assert.AreSame(sprite, renderer.sprite); Assert.IsFalse(animator.enabled);
                    Assert.AreEqual(new UnityEngine.Vector3(3, 2, 0), go.transform.position);
                    Assert.AreEqual(new UnityEngine.Vector3(-1, 1, 1), go.transform.localScale);
                    Assert.AreEqual(UnityEngine.Quaternion.identity, go.transform.rotation);
                }
                view.SetContactPose(Olca.Presentation.Dungeon.ContactVisualPhase.None, 0);
                Assert.AreEqual(enabled, animator.enabled); Assert.IsNull(renderer.sprite);
                Assert.IsFalse((bool)typeof(Olca.Presentation.Field.GameFieldView).GetField("contactPoseActive", flags).GetValue(view));
            } finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture); }
        }

        [TestCase(false)][TestCase(true)]
        public void ジャンプ表示の参照で軌道と速度を変えず停止と着地に追従する(bool dash)
        {
            var movement=new PlayerMovement();if(dash)movement.StartDash();movement.Jump();
            int Phase()=>Olca.Presentation.Field.GameFieldView.SelectJumpAnimationPhase(movement.IsJumping,movement.JumpProgress);
            Assert.AreEqual(1,Phase());Assert.AreEqual(0,movement.HeightMilli);
            movement.Update(200);Assert.AreEqual(1,Phase());Assert.AreEqual(.4f,movement.JumpProgress,.00001f);
            var height=movement.HeightMilli;var position=movement.CurrentPositionMilli;movement.Pause();movement.Update(1000);
            Assert.AreEqual(height,movement.HeightMilli);Assert.AreEqual(position,movement.CurrentPositionMilli);Assert.AreEqual(.4f,movement.JumpProgress,.00001f);
            movement.Resume();movement.Update(50);Assert.AreEqual(2,Phase());Assert.AreEqual(1500,movement.HeightMilli);
            movement.Update(50);Assert.AreEqual(3,Phase());Assert.AreEqual(.6f,movement.JumpProgress,.00001f);
            movement.Update(200);Assert.AreEqual(0,Phase());Assert.IsFalse(movement.IsJumping);Assert.AreEqual(0,movement.HeightMilli);
            Assert.AreEqual(dash?10000:5000,movement.CurrentPositionMilli);
        }
        [TestCase(13,1)][TestCase(14,2)][TestCase(15,2)][TestCase(16,2)][TestCase(17,3)][TestCase(29,3)]
        public void ジャンプ表示は描画差分ではなく論理進行率で選ぶ(int frame,int expected)
        {
            Assert.AreEqual(expected,Olca.Presentation.Field.GameFieldView.SelectJumpAnimationPhase(true,frame/30f));
            Assert.AreEqual(0,Olca.Presentation.Field.GameFieldView.SelectJumpAnimationPhase(false,frame/30f));
        }

        [TestCase(false)] [TestCase(true)]
        public void ダッシュ終了後の空中加速は着地で終わり長い更新も同じ距離になる(bool split)
        {
            var movement = new PlayerMovement(); movement.StartDash(); movement.Jump();
            movement.Update(100); movement.StopDash();
            Assert.IsTrue(movement.IsDashJumping); Assert.IsFalse(movement.IsDashing);
            movement.Pause(); movement.Update(2000); Assert.AreEqual(2f, movement.CurrentPosition);
            movement.Resume();
            if (split) { for (int n = 0; n < 9; n++) movement.Update(100); }
            else movement.Update(900);
            Assert.AreEqual(15f, movement.CurrentPosition, .001f);
            Assert.IsFalse(movement.IsDashJumping); Assert.IsFalse(movement.IsJumping);
            Assert.IsFalse(movement.IsDashing); Assert.AreEqual(0, movement.Height);
        }

        [Test]
        public void Update_プレイヤーが基本速度で前進する()
        {
            // Arrange
            var movement = new PlayerMovement(initialPosition: 0f);

            // Act - 整数ベース版: 1000ms = 1秒
            movement.Update(deltaTimeMs: 1000);

            // Assert
            // 基本速度は10.0マス/秒なので、1秒で10.0マス進む
            Assert.AreEqual(10.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void StartDash_ダッシュ速度で前進する()
        {
            // Arrange
            var movement = new PlayerMovement(initialPosition: 0f);

            // Act
            movement.StartDash();
            movement.Update(deltaTimeMs: 1000);  // 1000ms = 1秒

            // Assert
            // ダッシュ速度は20.0マス/秒なので、1秒で20.0マス進む
            Assert.AreEqual(20.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void StopDash_通常速度に戻る()
        {
            // Arrange
            var movement = new PlayerMovement(initialPosition: 0f);
            movement.StartDash();
            movement.Update(deltaTimeMs: 1000);  // ダッシュで20マス進む

            float positionAfterDash = movement.CurrentPosition;  // 20.0

            // Act
            movement.StopDash();
            movement.Update(deltaTimeMs: 1000);  // 通常速度で10マス進む

            // Assert
            Assert.AreEqual(positionAfterDash + 10.0f, movement.CurrentPosition, 0.01f);
        }

        [Test]
        public void Jump_高さが上昇する()
        {
            // Arrange
            var movement = new PlayerMovement();

            // Act
            movement.Jump();
            movement.Update(deltaTimeMs: 100);  // 100ms = 0.1秒、ジャンプ開始直後

            // Assert
            Assert.Greater(movement.Height, 0f, "ジャンプして高さが上がっている");
        }

        [Test]
        public void Jump_最大高度は1点5マス分まで上がる()
        {
            // Arrange
            var movement = new PlayerMovement();

            // Act
            movement.Jump();
            movement.Update(deltaTimeMs: 250);  // ジャンプ時間の中間が最大高度

            // Assert
            Assert.AreEqual(1.5f, movement.Height, 0.01f, "ジャンプ最大高度が1.5マス分になっている");
        }

        [Test]
        public void Jump_放物線を描いて着地する()
        {
            // Arrange
            var movement = new PlayerMovement();
            movement.Jump();

            // Act: ジャンプ時間（0.5秒 = 500ms）経過させる
            for (int i = 0; i < 6; i++)
            {
                movement.Update(deltaTimeMs: 100);  // 100ms × 6 = 600ms
            }

            // Assert
            Assert.AreEqual(0f, movement.Height, 0.01f, "ジャンプ後に着地している");
            Assert.IsFalse(movement.IsJumping, "ジャンプ状態が終了している");
        }

        [Test]
        public void Pause_移動が停止する()
        {
            // Arrange
            var movement = new PlayerMovement();
            movement.Update(deltaTimeMs: 500);  // 500ms = 0.5秒、1マス進む
            float positionBeforePause = movement.CurrentPosition;

            // Act
            movement.Pause();
            movement.Update(deltaTimeMs: 1000);  // 1000ms = 1秒

            // Assert
            Assert.AreEqual(positionBeforePause, movement.CurrentPosition, "一時停止中は移動しない");
        }

        [Test]
        public void Resume_移動が再開される()
        {
            // Arrange
            var movement = new PlayerMovement();
            movement.Pause();
            movement.Update(deltaTimeMs: 1000);  // 停止中なので移動しない

            float positionDuringPause = movement.CurrentPosition;  // 0.0

            // Act
            movement.Resume();
            movement.Update(deltaTimeMs: 1000);

            // Assert
            Assert.Greater(movement.CurrentPosition, positionDuringPause, "再開後に移動している");
        }

        [Test]
        public void SetPosition_位置を変更できる()
        {
            // Arrange
            var movement = new PlayerMovement(0f);

            // Act
            movement.SetPosition(10f);

            // Assert
            Assert.AreEqual(10f, movement.CurrentPosition);
        }
        private static System.Collections.Generic.List<string> HeightTrace(int fps, bool stall, bool dash)
        {
            var m = new DungeonSystem(Olca.Core.Domain.DungeonConfig.CreateTestDungeon(2000,1000,1000),
                new Olca.Core.Domain.PlayerCharacter(100),new Olca.Core.Domain.Deck(),new Olca.Tests.Mocks.MockRandomProvider(),new Olca.Tests.Mocks.MockLogger()); if (dash) m.StartDash(); m.Jump();
            var clock = new Olca.Core.Logic.Integration.LogicalFrameClock();
            var trace = new System.Collections.Generic.List<string>();
            float maxHeight = 0; int obstacleFrames = 0, enemyFrames = 0;
            System.Action<long,int> tick = (frame,ms) => {
                m.TickFrame(); maxHeight = System.Math.Max(maxHeight,m.PlayerHeight);
                if (m.PlayerHeight > .1f) obstacleFrames++;
                if (m.PlayerHeight > .5f) enemyFrames++;
                trace.Add(frame + ":" + m.PlayerPosition + ":" + m.PlayerHeight + ":" + m.IsJumping);
                if (frame == 15) Assert.AreEqual(1.5f,m.PlayerHeight);
            };
            long total = Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond / 2, previous = 0;
            if (stall) { previous = total; clock.Accumulate(previous); clock.Drain(tick,3); }
            for (long render=1;previous<total;render++) {
                long now=System.Math.Min(total,render*Olca.Core.Logic.Integration.LogicalFrameClock.WallTicksPerSecond/fps);
                clock.Accumulate(now-previous);previous=now;clock.Drain(tick,3);
            }
            while(clock.PendingFrames>0)clock.Drain(tick,3);
            Assert.AreEqual(30,trace.Count);Assert.AreEqual(1.5f,maxHeight);
            Assert.IsFalse(m.IsJumping);Assert.AreEqual(0,m.PlayerHeight);
            Assert.AreEqual(dash?10f:5f,m.PlayerPosition);
            Assert.AreEqual(29,obstacleFrames);Assert.AreEqual(23,enemyFrames);
            return trace;
        }
        [TestCase(30,false,false)] [TestCase(60,false,false)] [TestCase(120,false,false)]
        [TestCase(30,true,false)] [TestCase(60,true,false)] [TestCase(120,true,false)]
        [TestCase(30,false,true)] [TestCase(60,false,true)] [TestCase(120,false,true)]
        [TestCase(30,true,true)] [TestCase(60,true,true)] [TestCase(120,true,true)]
        public void 高さ75パーセントでも滞空30フレームと通常ダッシュ飛距離を保つ(int fps,bool stall,bool dash)
        { CollectionAssert.AreEqual(HeightTrace(60,false,dash),HeightTrace(fps,stall,dash)); }

    }
}

namespace Olca.Tests.Dungeon
{
    public class ExplorationIceTests
    {
        private static PlayerMovement Move(DungeonSystem d)=>(PlayerMovement)typeof(DungeonSystem).GetField("playerMovement",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(d);
        private static DungeonBattleManager Start(out DungeonCheckpointSession session, int[] ice=null,bool visit=false)
        {
            var m=Olca.Core.Domain.DungeonConfig.CreateTestDungeon(30,100,29);
            var terrain=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(ice??new[]{1},i=>new Olca.Core.Domain.ExplorationTerrainCell(i,Olca.Core.Domain.PanelType.Ice)));
            var map=new Olca.Core.Domain.DungeonConfig(m.DungeonId,m.DungeonName,30,m.MapCells,
                visits:visit?new[]{new Olca.Core.Domain.DungeonVisitDefinition(6,false,new[]{new Olca.Core.Domain.DungeonVisitOption("existing",0,heal:1)},1)}:null,explorationTerrain:terrain);
            session=DungeonCheckpointSession.CreateDefault(map,new Olca.Core.Domain.Deck());
            var game=new DungeonBattleManager(map,new Olca.Core.Domain.PlayerCharacter(100),new Olca.Core.Domain.Deck(),new Olca.Tests.Mocks.MockRandomProvider(),new Olca.Tests.Mocks.MockLogger(),checkpoints:session);
            game.Dungeon.ConfigureNormalMovementSpeed(8000);
            return game;
        }
        [NUnit.Framework.Test]
        public void 氷の境界を跨ぐ同じフレーム内で加速開始と終了を処理する()
        {
            var d=Start(out _).Dungeon;var m=Move(d);
            for(int i=0;i<7;i++)d.TickFrame();NUnit.Framework.Assert.AreEqual(933,m.CurrentPositionMilli);
            d.TickFrame();NUnit.Framework.Assert.AreEqual(1100,m.CurrentPositionMilli);NUnit.Framework.Assert.AreEqual(12000,m.EffectiveSpeedMilliPerSecond);
            for(int i=0;i<4;i++)d.TickFrame();NUnit.Framework.Assert.AreEqual(1900,m.CurrentPositionMilli);
            d.TickFrame();NUnit.Framework.Assert.AreEqual(2066,m.CurrentPositionMilli);NUnit.Framework.Assert.AreEqual(8000,m.EffectiveSpeedMilliPerSecond);
            NUnit.Framework.Assert.AreEqual(8000,m.NormalSpeedMilliPerSecond);
        }
        [NUnit.Framework.TestCase(false)][NUnit.Framework.TestCase(true)]
        public void ジャンプ中とフロート中は氷加速せず着地後だけ有効になる(bool floating)
        {
            var d=Start(out _,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Range(0,20))).Dungeon;var m=Move(d);
            if(floating)d.Player.TerrainState.IsFloating=true;else d.Jump();
            for(int i=0;i<30;i++)d.TickFrame();NUnit.Framework.Assert.AreEqual(4000,m.CurrentPositionMilli);
            if(floating)d.Player.TerrainState.IsFloating=false;
            d.TickFrame();NUnit.Framework.Assert.AreEqual(4200,m.CurrentPositionMilli);
        }
        [NUnit.Framework.Test]
        public void 氷上ダッシュとダッシュジャンプは既存速度のままで着地後は通常氷速度へ戻る()
        {
            var d=Start(out _,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Range(0,20))).Dungeon;var m=Move(d);
            d.StartDash();for(int i=0;i<3;i++)d.TickFrame();NUnit.Framework.Assert.AreEqual(1000,m.CurrentPositionMilli);
            d.Jump();for(int i=0;i<30;i++)d.TickFrame();NUnit.Framework.Assert.AreEqual(11000,m.CurrentPositionMilli);NUnit.Framework.Assert.IsFalse(m.IsDashing);
            d.TickFrame();NUnit.Framework.Assert.AreEqual(11200,m.CurrentPositionMilli);
        }
        [NUnit.Framework.Test]
        public void 氷の停止復帰と大きな旧ms区間は残効バフや通過漏れを作らない()
        {
            var g=Start(out _);var m=Move(g.Dungeon);for(int i=0;i<8;i++)g.TickFrame(17);
            g.SetUserPaused(true);for(int i=0;i<60;i++)g.TickFrame(17);NUnit.Framework.Assert.AreEqual(1100,m.CurrentPositionMilli);
            g.SetUserPaused(false);g.TickFrame(17);NUnit.Framework.Assert.AreEqual(1300,m.CurrentPositionMilli);
            var batch=Start(out _).Dungeon;batch.Tick(1000);var frame=Start(out _).Dungeon;for(int i=0;i<60;i++)frame.TickFrame();
            NUnit.Framework.Assert.AreEqual(Move(batch).CurrentPositionMilli,Move(frame).CurrentPositionMilli);NUnit.Framework.Assert.AreEqual(8333,Move(frame).CurrentPositionMilli);
        }
        [NUnit.Framework.Test]
        public void 氷のジャンプ着地時刻と停止地点の順序を区間時間から求める()
        {
            var d=Start(out _,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Range(0,20)),true).Dungeon;d.Jump();d.Tick(1000);
            NUnit.Framework.Assert.IsNotNull(d.ActiveVisit);NUnit.Framework.Assert.AreEqual(6000,Move(d).CurrentPositionMilli);NUnit.Framework.Assert.IsFalse(Move(d).IsJumping);NUnit.Framework.Assert.AreEqual(100,d.Player.CurrentHp);
        }
        [NUnit.Framework.Test]
        public void 氷上の退出保存と再開は位置から速度を復元し通常セルへバフを残さない()
        {
            var g=Start(out var session,System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Range(2,8)),true);g.Dungeon.Tick(1000);
            NUnit.Framework.Assert.IsNotNull(g.Dungeon.ActiveVisit);NUnit.Framework.Assert.IsTrue(g.Dungeon.ExitVisit());
            NUnit.Framework.Assert.AreEqual(5,session.Store.Read().SchemaVersion);NUnit.Framework.Assert.AreEqual(6,session.Store.Read().TerrainContactCell);
            NUnit.Framework.Assert.IsTrue(DungeonBattleManager.TryRestoreCheckpoint(session,new Olca.Tests.Mocks.MockRandomProvider(),new Olca.Tests.Mocks.MockLogger(),out var resumed,out var error),error);
            resumed.Dungeon.ConfigureNormalMovementSpeed(8000);resumed.ActivateCheckpoint();resumed.TickFrame(17);NUnit.Framework.Assert.AreEqual(6200,Move(resumed.Dungeon).CurrentPositionMilli);
            Move(resumed.Dungeon).SetPosition(10);resumed.TickFrame(17);NUnit.Framework.Assert.AreEqual(10133,Move(resumed.Dungeon).CurrentPositionMilli);NUnit.Framework.Assert.AreEqual(8000,Move(resumed.Dungeon).EffectiveSpeedMilliPerSecond);
        }
    }
}
