using NUnit.Framework;
using Olca.Core.Logic.Dungeon;

namespace Olca.Tests.Dungeon
{
    [TestFixture]
    public class PlayerMovementTests
    {
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
