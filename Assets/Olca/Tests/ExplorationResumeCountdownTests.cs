using NUnit.Framework;
using Olca.Core.Logic.Integration;
namespace Olca.Tests
{
    public class ExplorationResumeCountdownTests
    {
        [TestCase(1)] [TestCase(5)] [TestCase(90)]
        public void 論理フレームのまとめ方にかかわらず三二一は各三十で完了通知は一回(int batch)
        {
            var timer=new ExplorationResumeCountdown();timer.Begin();int finished=0;
            for(int start=0;start<90;start+=batch)for(int i=start;i<System.Math.Min(start+batch,90);i++)
            {Assert.AreEqual(3-i/30,timer.DisplayCount);if(timer.AdvanceFrame())finished++;}
            Assert.AreEqual(1,finished);Assert.AreEqual(0,timer.DisplayCount);Assert.IsFalse(timer.AdvanceFrame());
        }
        [Test] public void 再開連打は延長せず取消後だけ全長で始める()
        {
            var timer=new ExplorationResumeCountdown();Assert.IsFalse(timer.AdvanceFrame());timer.Begin();timer.AdvanceFrame();timer.Begin();Assert.AreEqual(89,timer.RemainingFrames);
            timer.Cancel();Assert.AreEqual(0,timer.DisplayCount);timer.Begin();Assert.AreEqual(90,timer.RemainingFrames);
        }
        [Test] public void 外界Sessionは表示借用を返して再表示しても同じ状態を保持し旧借用は新表示を閉じない()
        {
            var session=new OutsideExplorationSession();var first=session.AttachPresentation();Assert.IsTrue(first.IsCurrent);first.Dispose();Assert.IsFalse(session.IsPresented);Assert.IsFalse(session.IsEnded);
            var second=session.AttachPresentation();var third=session.AttachPresentation();Assert.IsFalse(second.IsCurrent);second.Dispose();Assert.IsTrue(third.IsCurrent);Assert.IsTrue(session.IsPresented);third.Dispose();Assert.IsFalse(session.IsPresented);
        }
        [Test] public void 外界SessionはRoot終了後の再表示と旧入力を受けない()
        {
            var session=new OutsideExplorationSession();var lease=session.AttachPresentation();session.Dispose();session.Dispose();Assert.IsTrue(session.IsEnded);Assert.IsFalse(lease.IsCurrent);Assert.IsFalse(session.IsPresented);lease.Dispose();Assert.Throws<System.ObjectDisposedException>(()=>session.AttachPresentation());
        }

        [TestCase(1)] [TestCase(10)] [TestCase(60)]
        public void 外界横移動は描画分割に依存せず六十Hzで進み停止と反転ができる(int parts)
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var clock=new LogicalFrameClock();
            long consumed=0;
            for(int i=1;i<=parts;i++){long target=LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-consumed);consumed=target;clock.Drain((frame,ms)=>core.TickFrame(1));}
            Assert.AreEqual(60,core.Frame);Assert.AreEqual(14*OutsideMovementDefinition.UnitsPerWorldUnit,core.Position);
            long stopped=core.Position;for(int i=0;i<60;i++)core.TickFrame(0);Assert.AreEqual(stopped,core.Position);
            for(int i=0;i<60;i++)core.TickFrame(-1);Assert.AreEqual(core.Definition.Start,core.Position);Assert.AreEqual(-1,core.Facing);
        }
        [Test] public void 外界の境界で止まり逆入力は即座に離れ検証状態は表示借用と別に保持される()
        {
            var s=new OutsideExplorationSession();var lease=s.AttachPresentation();for(int i=0;i<1000;i++)s.Core.TickFrame(-1);Assert.AreEqual(0,s.Core.Position);
            lease.Dispose();long frame=s.Core.Frame;var next=s.AttachPresentation();Assert.AreSame(s.Core,next.Core);Assert.AreEqual(frame,next.Core.Frame);
            s.Core.TickFrame(1);Assert.Greater(s.Core.Position,0);for(int i=0;i<1000;i++)s.Core.TickFrame(1);Assert.AreEqual(s.Core.Definition.Width,s.Core.Position);
            s.Core.TickFrame(-1);Assert.Less(s.Core.Position,s.Core.Definition.Width);
        }
        [Test] public void 外界定義が受け付ける整数極値でも端から反対側へ桁あふれしない()
        {
            var core=new OutsideExplorationCore(new OutsideMovementDefinition("edge",long.MaxValue,long.MaxValue,long.MaxValue));
            core.TickFrame(1);Assert.AreEqual(long.MaxValue,core.Position);core.TickFrame(-1);Assert.Greater(core.Position,0);Assert.Less(core.Position,long.MaxValue);
            var left=new OutsideExplorationCore(new OutsideMovementDefinition("edge",long.MaxValue,0,long.MaxValue));left.TickFrame(-1);Assert.AreEqual(0,left.Position);left.TickFrame(1);Assert.Greater(left.Position,0);
        }
        [Test] public void 外界低速設定でも端から逆入力で離れ整数未満の余りを失わない()
        {
            var core=new OutsideExplorationCore(new OutsideMovementDefinition("slow",100,100,1));for(int i=0;i<60;i++)core.TickFrame(-1);Assert.AreEqual(99,core.Position);
            var left=new OutsideExplorationCore(new OutsideMovementDefinition("slow",100,0,1));for(int i=0;i<60;i++)left.TickFrame(1);Assert.AreEqual(1,left.Position);
        }

        [TestCase(1)] [TestCase(7)] [TestCase(60)]
        public void 外界ジャンプは保持で高くなり描画分割で軌道が変わらない(int parts)
        {
            var held=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var tap=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var clock=new LogicalFrameClock();long prior=0,peak=0,shortPeak=0;
            for(int i=1;i<=parts;i++){long target=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-prior);prior=target;clock.Drain((f,ms)=>{held.TickFrame(1,f==1,true);tap.TickFrame(1,f==1,f==1);peak=System.Math.Max(peak,held.Height);shortPeak=System.Math.Max(shortPeak,tap.Height);});}
            Assert.AreEqual(186000,peak);Assert.Less(shortPeak,peak);Assert.IsTrue(held.IsGrounded);Assert.IsTrue(tap.IsGrounded);Assert.AreEqual(20*OutsideMovementDefinition.UnitsPerWorldUnit,held.Position);Assert.AreEqual(120,held.Frame);
        }
        [Test] public void 外界ジャンプは保持着地と空中押下で再ジャンプを予約しない()
        {
            var c=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);c.TickFrame(0,true,true);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.IsTrue(c.IsGrounded);
            c.TickFrame(0,true,true);Assert.IsTrue(c.IsGrounded);c.TickFrame(0,false,false);c.TickFrame(0,true,true);Assert.Greater(c.Height,0);
            c.TickFrame(0,false,false);c.TickFrame(0,true,true);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.IsTrue(c.IsGrounded);
        }
        [Test] public void 外界空中状態はView借用終了で進まず再表示時も保持する()
        {
            var s=new OutsideExplorationSession();var a=s.AttachPresentation();s.Core.TickFrame(-1,true,true);long y=s.Core.Height,v=s.Core.VerticalVelocity,f=s.Core.Frame;a.Dispose();var b=s.AttachPresentation();Assert.AreSame(s.Core,b.Core);Assert.AreEqual(y,b.Core.Height);Assert.AreEqual(v,b.Core.VerticalVelocity);Assert.AreEqual(f,b.Core.Frame);b.Dispose();
        }
    }
}
