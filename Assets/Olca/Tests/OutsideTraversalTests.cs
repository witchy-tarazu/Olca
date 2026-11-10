using System;
using NUnit.Framework;
using Olca.Core.Logic.Integration;

namespace Olca.Tests
{
    public sealed class OutsideTraversalTests
    {
        private static OutsideExplorationCore Core(OutsideTraversalProfile p=null)=>new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain,p??OutsideTraversalProfile.ValidationDash);
        private static void ToWall(OutsideExplorationCore c)
        {long x=c.TraversalProfile.Walls[0].X-c.TraversalProfile.BodyHalfWidth;for(int i=0;i<300&&c.Position<x;i++)c.TickFrame(1);Assert.AreEqual(x,c.Position);}
        [Test] public void 外界ダッシュは地上の一押下だけで十二frame進み保持と途中押下を予約しない()
        {
            var c=Core();long start=c.Position;c.TickFrame(1,dashDown:true,dashHeld:true);
            Assert.IsTrue(c.IsDashing);Assert.AreEqual(0,c.DashFrame);
            for(int i=1;i<12;i++){c.TickFrame(1,dashHeld:true);Assert.IsTrue(c.IsDashing);Assert.AreEqual(i,c.DashFrame);}
            Assert.AreEqual(144000,c.Position-start);
            for(int i=0;i<30;i++){c.TickFrame(1,dashHeld:true);Assert.IsFalse(c.IsDashing);}
            Assert.AreEqual(144000+30*6000,c.Position-start);
        }
        [Test] public void 外界ダッシュの反転は残時間を更新せず離しても一回分を走り切る()
        {
            var c=Core();long start=c.Position;
            for(int i=0;i<4;i++)c.TickFrame(1,dashDown:i==0,dashHeld:i==0);
            for(int i=0;i<8;i++)c.TickFrame(-1);
            Assert.IsTrue(c.IsDashing);Assert.AreEqual(11,c.DashFrame);Assert.AreEqual(start-48000,c.Position);
            c.TickFrame(0);Assert.IsFalse(c.IsDashing);Assert.AreEqual(start-48000,c.Position);
        }
        [Test] public void 二連続後の待機は三十frameで不足中の保持を自動発動しない()
        {
            var c=Core();for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
            c.TickFrame(0);for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
            c.TickFrame(0);Assert.AreEqual(30,c.DashCooldownRemaining);
            c.TickFrame(0,dashDown:true,dashHeld:true);Assert.IsFalse(c.IsDashing);Assert.AreEqual(29,c.DashCooldownRemaining);
            for(int i=0;i<29;i++)c.TickFrame(0,dashHeld:true);
            Assert.AreEqual(0,c.DashCooldownRemaining);Assert.IsFalse(c.IsDashing);
            c.TickFrame(0);c.TickFrame(0,dashDown:true,dashHeld:true);Assert.IsTrue(c.IsDashing);
        }
        [Test] public void 一秒ダッシュ未使用で連続回数を戻す()
        {
            var c=Core();for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
            while(c.Frame<71)c.TickFrame(0);
            for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
            c.TickFrame(0);Assert.AreEqual(0,c.DashCooldownRemaining);
        }
        [Test] public void 空中の通常ダッシュ押下と保持着地では開始せずゲージも消費しない()
        {
            var c=Core();Assert.IsTrue(c.CardGauge.TrySpend(80));c.TickFrame(1,true,true);
            c.TickFrame(1,false,true,true,true);Assert.IsFalse(c.IsDashing);
            for(int i=0;i<118;i++)c.TickFrame(1,false,true,false,true);
            Assert.IsTrue(c.IsGrounded);Assert.IsFalse(c.IsDashing);Assert.AreEqual(2400,c.CardGauge.CurrentUnits);
        }
        [Test] public void ダッシュジャンプは同じ縦軌道で遠くへ進み空中停止と反転を受ける()
        {
            var normal=Core();var dash=Core();long a=0,b=0;
            for(int i=0;i<80;i++){
                normal.TickFrame(1,i==0,true);dash.TickFrame(1,i==0,true,i==0,i==0);
                a=Math.Max(a,normal.Height);b=Math.Max(b,dash.Height);
                Assert.AreEqual(normal.Height,dash.Height);Assert.AreEqual(normal.VerticalVelocity,dash.VerticalVelocity);
            }
            Assert.AreEqual(186000,a);Assert.AreEqual(a,b);Assert.Greater(dash.Position,normal.Position);Assert.IsFalse(dash.IsDashJumping);
            dash=Core();dash.TickFrame(1,true,true,true,true);Assert.IsFalse(dash.IsDashing);Assert.IsTrue(dash.IsDashJumping);
            long x=dash.Position;dash.TickFrame(0,false,true);Assert.AreEqual(x,dash.Position);dash.TickFrame(-1,false,true);Assert.Less(dash.Position,x);
        }
        [Test] public void 検証壁は未解放なら接触だけで壁能力を使えない()
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(false));ToWall(c);c.TickFrame(1,true,true);Assert.IsFalse(c.IsWallSliding);Assert.IsFalse(c.IsWallKicking);
            c.TickFrame(1);long v=c.VerticalVelocity;c.TickFrame(1,true,true);Assert.IsFalse(c.IsWallKicking);Assert.AreEqual(v-400,c.VerticalVelocity);
        }
        [Test] public void 壁方向保持だけで滑り速度を制限し離すと通常重力へ戻る()
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(true));ToWall(c);c.TickFrame(1,true,true);Assert.IsFalse(c.IsWallSliding,"上昇中は壁ずりしない");
            while(c.VerticalVelocity>=0){c.TickFrame(1,false,true);Assert.IsFalse(c.IsWallSliding);}
            c.TickFrame(1,false,true);Assert.IsTrue(c.IsWallSliding);Assert.AreEqual(0,c.WallSlideFrame);
            for(int i=0;i<60&&c.VerticalVelocity>-1800;i++)c.TickFrame(1,false,true);
            Assert.AreEqual(-1800,c.VerticalVelocity);
            long y=c.Height;c.TickFrame(1,false,true);Assert.AreEqual(y-1800,c.Height);Assert.AreEqual(-1800,c.VerticalVelocity);
            c.TickFrame(0,false,true);Assert.IsFalse(c.IsWallSliding);Assert.AreEqual(-2200,c.VerticalVelocity);
        }
        [TestCase(-1)][TestCase(1)] public void 壁ずりは左右とも頂点の静止stepまで開始せず下降stepから始まり接地で消える(int side)
        {
            var p=OutsideTraversalProfile.ValidationWall(true);long contact=p.Walls[0].X-side*p.BodyHalfWidth;
            var c=new OutsideExplorationCore(new OutsideMovementDefinition("wall-boundary",32*60000,contact,6*60000),p);c.TickFrame(side,true,true);
            bool apex=false,fall=false;
            for(int i=0;i<150&&!c.IsGrounded;i++){
                long beforeV=c.VerticalVelocity,y=c.Height;c.TickFrame(side,false,true);
                if(beforeV>=0){Assert.IsFalse(c.IsWallSliding);Assert.GreaterOrEqual(c.Height,y);if(beforeV==0){apex=true;Assert.AreEqual(y,c.Height);}}
                else if(!c.IsGrounded){fall=true;Assert.IsTrue(c.IsWallSliding);Assert.Less(c.Height,y);}
            }
            Assert.IsTrue(apex);Assert.IsTrue(fall);Assert.IsTrue(c.IsGrounded);Assert.IsFalse(c.IsWallSliding);Assert.AreEqual(0,c.WallSlideFrame);
        }
        [Test] public void 壁蹴りは離す押し直しで同壁へ反復でき押出し中だけ反対方向へ進む()
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(true));ToWall(c);c.TickFrame(1,true,true);c.TickFrame(1);
            long x=c.Position,y=c.Height;c.TickFrame(1,true,true);Assert.IsTrue(c.IsWallKicking);Assert.AreEqual(0,c.WallKickFrame);Assert.AreEqual(-1,c.Facing);Assert.AreEqual(x-8000,c.Position);Assert.AreEqual(y+12000,c.Height);
            for(int i=1;i<6;i++)c.TickFrame(1,false,true);
            Assert.AreEqual(x-48000,c.Position);Assert.AreEqual(-1,c.Facing);
            for(int i=0;i<20&&c.Position<x;i++)c.TickFrame(1);
            Assert.AreEqual(x,c.Position);Assert.IsFalse(c.IsGrounded);long secondY=c.Height;c.TickFrame(1,true,true);
            Assert.IsTrue(c.IsWallKicking);Assert.AreEqual(0,c.WallKickFrame);Assert.AreEqual(secondY+12000,c.Height);Assert.Greater(secondY,y);
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public void 壁ごとの滑りと蹴りの可否を独立に使う(bool slide,bool kick)
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(true,slide,kick));ToWall(c);c.TickFrame(1,true,true);Assert.IsFalse(c.IsWallSliding);while(c.VerticalVelocity>=0)c.TickFrame(1,false,true);c.TickFrame(1,false,true);Assert.AreEqual(slide,c.IsWallSliding);
            c.TickFrame(1);c.TickFrame(1,true,true);Assert.AreEqual(kick,c.IsWallKicking);
        }
        [Test] public void 高速で検証壁を通り抜けず実移動停止からIdleを開始し残ダッシュ中も継続する()
        {
            var p=OutsideTraversalProfile.ValidationWall(false);long wall=p.Walls[0].X-p.BodyHalfWidth;
            var c=new OutsideExplorationCore(new OutsideMovementDefinition("wall-near",32*60000,wall-1000,6*60000),p);
            c.TickFrame(1,dashDown:true,dashHeld:true);Assert.AreEqual(wall,c.Position);Assert.IsTrue(c.IsDashing);Assert.AreEqual(0,c.IdleFrame);
            for(int i=1;i<12;i++)c.TickFrame(1,dashHeld:true);
            c.TickFrame(0);Assert.IsFalse(c.IsDashing);Assert.AreEqual(11,c.IdleFrame);c.TickFrame(0);Assert.AreEqual(12,c.IdleFrame);
        }
        [Test] public void 接地壁でShiftダッシュしても待機時計だけは連続しダッシュ時間と再使用待ちは変わらない()
        {
            var c=Core(OutsideTraversalProfile.ValidationWall(true));ToWall(c);for(int i=0;i<8;i++)c.TickFrame(1);long idle=c.IdleFrame,x=c.Position;
            for(int i=0;i<12;i++){c.TickFrame(1,dashDown:i==0,dashHeld:true);Assert.IsTrue(c.IsDashing);Assert.AreEqual(i,c.DashFrame);Assert.AreEqual(x,c.Position);Assert.IsFalse(c.IsMoving);Assert.AreEqual(idle+i+1,c.IdleFrame);}
            c.TickFrame(1);Assert.IsFalse(c.IsDashing);Assert.AreEqual(0,c.DashCooldownRemaining);
            for(int i=0;i<12;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);c.TickFrame(1);Assert.AreEqual(30,c.DashCooldownRemaining);
        }
        [Test] public void 残像は四つまでの論理状態で表示借用と停止中に変わらず十二frameで消える()
        {
            using(var s=new OutsideExplorationSession()){
                var c=s.Core;for(int i=0;i<10;i++)c.TickFrame(1,dashDown:i==0,dashHeld:true);
                Assert.AreEqual(4,c.Afterimages.Count);var last=c.Afterimages[3];long frame=c.Frame;
                var first=s.AttachPresentation();first.Dispose();var second=s.AttachPresentation();Assert.AreSame(c,second.Core);Assert.AreEqual(frame,c.Frame);Assert.AreEqual(last.Frame,c.Afterimages[3].Frame);Assert.IsFalse(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true)));second.Dispose();
                for(int i=0;i<20;i++)c.TickFrame(0);Assert.AreEqual(0,c.Afterimages.Count);
            }
        }
        [TestCase(1)][TestCase(7)][TestCase(120)] public void ダッシュジャンプの結果は描画分割に依存しない(int parts)
        {
            var expected=Core();var actual=Core();var clock=new LogicalFrameClock();
            void Step(OutsideExplorationCore c,long f)=>c.TickFrame(f<60?1:-1,f==17,f>=17&&f<60,f==1||f==15,f<=12||f>=15&&f<=28);
            for(int f=1;f<=120;f++)Step(expected,f);
            long consumed=0;for(int i=1;i<=parts;i++){long target=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-consumed);consumed=target;clock.Drain((f,ms)=>Step(actual,f));}
            Assert.AreEqual(120,actual.Frame);Assert.AreEqual(expected.Position,actual.Position);Assert.AreEqual(expected.Height,actual.Height);Assert.AreEqual(expected.VerticalVelocity,actual.VerticalVelocity);Assert.AreEqual(expected.DashCooldownRemaining,actual.DashCooldownRemaining);Assert.AreEqual(expected.IsDashJumping,actual.IsDashJumping);Assert.AreEqual(expected.CardGauge.CurrentUnits,actual.CardGauge.CurrentUnits);
        }
        [Test] public void 縦追従は必要な高さだけ表示を送り低い跳躍と固定表示を変えない()
        {
            var follow=OutsideCameraPolicy.Follow;
            Assert.AreEqual(0,follow.ResolveVerticalOffset(3*60000,10,2,1.5f,2.5f));
            Assert.AreEqual(0,follow.ResolveVerticalOffset(4*60000,10,2,1.5f,2.5f));
            Assert.AreEqual(2,follow.ResolveVerticalOffset(6*60000,10,2,1.5f,2.5f));
            Assert.AreEqual(0,OutsideCameraPolicy.FixedAt(12*60000).ResolveVerticalOffset(6*60000,10,2,1.5f,2.5f));
        }
        [Test] public void 縦追従の不正高さと非有限表示寸法は拒否する()
        {
            var follow=OutsideCameraPolicy.Follow;
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(-1,10,2,1,2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(0,float.NaN,2,1,2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(0,10,float.PositiveInfinity,1,2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(0,10,2,-1,2));
            Assert.Throws<ArgumentOutOfRangeException>(()=>follow.ResolveVerticalOffset(0,10,2,1,0));
        }
        [Test] public void 試走設定は初回表示前だけで解放や地形を保存へ書かない()
        {
            using(var s=new OutsideExplorationSession()){
                Assert.IsFalse(s.Core.TraversalProfile.WallActionsUnlocked);Assert.AreEqual(0,s.Core.TraversalProfile.Walls.Count);Assert.Throws<ArgumentNullException>(()=>s.TryConfigureTraversal(null));
                Assert.IsTrue(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true)));var lease=s.AttachPresentation();Assert.IsFalse(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationDash));lease.Dispose();
                s.Core.TickFrame(0);Assert.IsFalse(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationDash));Assert.IsTrue(s.Core.TraversalProfile.WallActionsUnlocked);
            }
        }
        [Test] public void 外界個体は一度だけ生成しView再生成でもHPと状態を保持する()
        {
            using(var s=new OutsideExplorationSession()){
                int made=0;Assert.IsTrue(s.TryInitializePlayer(()=>{made++;return new Olca.Core.Domain.PlayerCharacter(100);}));var actor=s.Player;actor.SetHp(46);actor.ApplyParalysis(3);
                using(var first=s.AttachPresentation()){Assert.AreSame(actor,first.Player);s.Core.TickFrame(0);Assert.IsTrue(s.AdvancePlayerFrame());Assert.IsFalse(s.CanPerformAttack);Assert.AreEqual(2,actor.ParalysisRemainingFrames);Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(2,actor.ParalysisRemainingFrames);}
                Assert.IsFalse(s.TryInitializePlayer(()=>{made++;return new Olca.Core.Domain.PlayerCharacter(200);}));using(var second=s.AttachPresentation()){Assert.AreSame(actor,second.Player);Assert.AreEqual(46,second.Player.CurrentHp);Assert.AreEqual(2,actor.ParalysisRemainingFrames);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.AreEqual(1,actor.ParalysisRemainingFrames);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsTrue(s.CanPerformAttack);}
                Assert.AreEqual(1,made);Assert.AreEqual(46,actor.CurrentHp);
            }
        }
        [Test] public void 外界停止は状態時間を進めず死亡しても表示再入で全回復しない()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));using(var lease=s.AttachPresentation()){
                    s.Player.ApplyParalysis(4);for(int i=0;i<20;i++)Assert.IsFalse(s.AdvancePlayerFrame());s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.AreEqual(3,s.Player.ParalysisRemainingFrames);s.Player.SetHp(0);Assert.IsFalse(s.CanPerformAttack);
                }
                using(var lease=s.AttachPresentation()){Assert.AreEqual(0,lease.Player.CurrentHp);Assert.IsFalse(s.CanPerformAttack);}
            }
        }
        [Test] public void 外界終了は旧leaseから個体を切り離し新規世代へ状態を渡さない()
        {
            var s=new OutsideExplorationSession();Assert.Throws<ArgumentNullException>(()=>s.TryInitializePlayer(null));Assert.Throws<ArgumentException>(()=>s.TryInitializePlayer(()=>null));s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));var old=s.AttachPresentation();s.Player.SetHp(31);s.Dispose();Assert.IsNull(old.Player);Assert.IsNull(s.Player);Assert.IsFalse(s.CanPerformAttack);Assert.IsFalse(s.AdvancePlayerFrame());Assert.Throws<ObjectDisposedException>(()=>s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100)));
            using(var next=new OutsideExplorationSession()){next.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));Assert.AreEqual(100,next.Player.CurrentHp);}
        }
        [Test] public void 外界の期間防御は同じ論理時計で二frame後に終了し停止時は保持する()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));using(var lease=s.AttachPresentation()){
                    s.Player.SetAura(10,2);Assert.IsTrue(s.Player.HasAura);for(int i=0;i<5;i++)Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(2,s.Player.ProtectionRemainingFrames);
                    s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsTrue(s.Player.HasAura);Assert.AreEqual(1,s.Player.ProtectionRemainingFrames);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsFalse(s.Player.HasAura);
                }
            }
        }
    }
}
