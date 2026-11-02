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
        private static OutsideExplorationCore LandingCore(params OutsideLandingSurface[] surfaces)=>new OutsideExplorationCore(new OutsideMovementDefinition("landing-test",32*60000,8*60000,6*60000,surfaces));
        private static void LandOnValidationTop(OutsideExplorationCore c){for(int i=0;i<80;i++){c.TickFrame(1,i==0,true);if(c.SupportId=="top")return;}Assert.Fail("検証上面へ着地しない");}
        [TestCase(1)] [TestCase(7)] [TestCase(120)]
        public void 外界上面接地は描画分割に依存せず下から通過して下降時だけ止まる(int parts)
        {
            var c=LandingCore(new OutsideLandingSurface("top",12*60000,16*60000,60000));var clock=new LogicalFrameClock();long prior=0;bool landed=false,passedBelow=false;
            for(int i=1;i<=parts;i++){long t=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(t-prior);prior=t;clock.Drain((f,ms)=>{c.TickFrame(landed?0:1,f==1,true);if(c.VerticalVelocity>0&&c.Height<60000)passedBelow=true;if(c.SupportId=="top")landed=true;});}
            Assert.IsTrue(passedBelow);Assert.IsTrue(landed);Assert.IsTrue(c.IsGrounded);Assert.AreEqual("top",c.SupportId);Assert.AreEqual(60000,c.Height);Assert.AreEqual(0,c.VerticalVelocity);Assert.AreEqual(120,c.Frame);
        }
        [Test] public void 外界足場上の保持は再ジャンプせず新しい押下だけで飛び両端から落ちる()
        {
            foreach(int direction in new[]{-1,1}){var c=LandingCore(new OutsideLandingSurface("top",12*60000,16*60000,60000));LandOnValidationTop(c);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.AreEqual(60000,c.Height);Assert.AreEqual("top",c.SupportId);
                c.TickFrame(0,false,false);c.TickFrame(0,true,true);Assert.Greater(c.Height,60000);Assert.IsFalse(c.IsGrounded);for(int i=0;i<100;i++)c.TickFrame(0,false,true);Assert.AreEqual("top",c.SupportId);
                for(int i=0;i<80&&c.IsGrounded;i++)c.TickFrame(direction);Assert.IsFalse(c.IsGrounded);Assert.IsNull(c.SupportId);Assert.AreEqual(60000,c.Height);Assert.AreEqual(-400,c.VerticalVelocity);
                for(int i=0;i<60;i++)c.TickFrame(direction);Assert.AreEqual("floor",c.SupportId);Assert.AreEqual(0,c.Height);}
        }
        [Test] public void 外界重複上面は配列順でなく最初に横切る高さと識別子で決まる()
        {
            var low=new OutsideLandingSurface("low",0,32*60000,58000);var a=new OutsideLandingSurface("a",0,32*60000,60000);var b=new OutsideLandingSurface("b",0,32*60000,60000);
            foreach(var surfaces in new[]{new[]{low,b,a},new[]{a,low,b}}){var c=LandingCore(surfaces);for(int i=0;i<100;i++)c.TickFrame(0,i==0,true);Assert.AreEqual("a",c.SupportId);Assert.AreEqual(60000,c.Height);}
        }
        [Test] public void 外界上面の支持は高さと端を検証し旧入力配列の変更から独立する()
        {
            var top=new OutsideLandingSurface("top",12*60000,16*60000,60000);Assert.IsTrue(top.Contains(top.Left));Assert.IsTrue(top.Contains(top.Right));Assert.IsFalse(top.Contains(top.Left-1));Assert.IsFalse(top.Contains(top.Right+1));
            var input=new[]{top};var c=LandingCore(input);input[0]=new OutsideLandingSurface("other",0,32*60000,90000);LandOnValidationTop(c);Assert.AreEqual("top",c.SupportId);
            Assert.Throws<System.ArgumentException>(()=>LandingCore(top,top));Assert.Throws<System.ArgumentException>(()=>LandingCore(new OutsideLandingSurface("overflow",0,32*60000,long.MaxValue)));
        }
        [Test] public void 外界上面を同frameで横断し終点が面外なら検証モデルでは着地しない()
        {
            var c=new OutsideExplorationCore(new OutsideMovementDefinition("fast",100*60000,0,60*60000,new OutsideLandingSurface("thin",334000,338000,60000)));
            // 初めはその場で跳び、下降時だけ高速横移動する。狭い上面を飛び越してfloorへ戻る。
            for(int i=0;i<55;i++)c.TickFrame(i>=50?1:0,i==0,true);Assert.AreEqual(66000,c.Height);Assert.AreEqual(300000,c.Position);c.TickFrame(1,false,true);Assert.AreEqual(56000,c.Height);Assert.IsNull(c.SupportId);for(int i=0;i<64;i++)c.TickFrame(1,false,true);Assert.AreEqual("floor",c.SupportId);Assert.AreEqual(0,c.Height);
        }
        [Test] public void 外界支持面の識別子と位置は表示借用を返しても保持される()
        {
            var s=new OutsideExplorationSession();var first=s.AttachPresentation();for(int i=0;i<80&&s.Core.SupportId!="validation-top";i++)s.Core.TickFrame(1,i==0,true);Assert.AreEqual("validation-top",s.Core.SupportId);long y=s.Core.Height,f=s.Core.Frame,x=s.Core.Position;first.Dispose();var next=s.AttachPresentation();Assert.AreSame(s.Core,next.Core);Assert.AreEqual("validation-top",next.Core.SupportId);Assert.AreEqual(y,next.Core.Height);Assert.AreEqual(f,next.Core.Frame);Assert.AreEqual(x,next.Core.Position);next.Dispose();
        }
        [Test] public void 外界カメラ追従は既存の両端余白と狭いマップの中央を保つ()
        {
            var p=OutsideCameraPolicy.Follow;const long u=60000;
            Assert.AreEqual(4,p.ResolveHorizontalCenter(0,32*u,10));Assert.AreEqual(8,p.ResolveHorizontalCenter(8*u,32*u,10));Assert.AreEqual(28,p.ResolveHorizontalCenter(32*u,32*u,10));Assert.AreEqual(16,p.ResolveHorizontalCenter(8*u,32*u,40));
        }
        [Test] public void 外界固定カメラは移動で動かず画面比率とマップ範囲に応じて拘束する()
        {
            const long u=60000;var p=OutsideCameraPolicy.FixedAt(12*u);
            foreach(long x in new[]{0,8*u,16*u,32*u}){Assert.AreEqual(12,p.ResolveHorizontalCenter(x,32*u,10));Assert.AreEqual(12,p.ResolveHorizontalCenter(x,32*u,20));}
            Assert.AreEqual(4,OutsideCameraPolicy.FixedAt(0).ResolveHorizontalCenter(8*u,32*u,10));Assert.AreEqual(16,p.ResolveHorizontalCenter(8*u,32*u,40));
        }
        [Test] public void 外界カメラ設定は非表示だけで変更しView世代や位置と時計を初期化しない()
        {
            using(var s=new OutsideExplorationSession()){
                var core=s.Core;core.TickFrame(1);long x=core.Position,f=core.Frame;var fixedPolicy=OutsideCameraPolicy.FixedAt(12*60000);
                Assert.IsTrue(s.TryConfigureCamera(fixedPolicy));var a=s.AttachPresentation();Assert.AreSame(fixedPolicy,a.Camera);Assert.IsFalse(s.TryConfigureCamera(OutsideCameraPolicy.Follow));var b=s.AttachPresentation();a.Dispose();Assert.IsTrue(b.IsCurrent);Assert.AreSame(fixedPolicy,b.Camera);b.Dispose();Assert.AreEqual(x,core.Position);Assert.AreEqual(f,core.Frame);Assert.IsTrue(s.TryConfigureCamera(OutsideCameraPolicy.Follow));Assert.AreSame(OutsideCameraPolicy.Follow,s.Camera);
            }
        }
        [Test] public void 外界カメラ不正値と終了後の設定を拒否する()
        {
            const long u=60000;Assert.Throws<System.ArgumentOutOfRangeException>(()=>OutsideCameraPolicy.FixedAt(-1));var p=OutsideCameraPolicy.Follow;
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(0,32*u,float.NaN));Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(0,32*u,0));Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(-1,32*u,10));Assert.Throws<System.ArgumentOutOfRangeException>(()=>OutsideCameraPolicy.FixedAt(33*u).ResolveHorizontalCenter(0,32*u,10));
            var s=new OutsideExplorationSession();Assert.Throws<System.ArgumentNullException>(()=>s.TryConfigureCamera(null));Assert.Throws<System.ArgumentOutOfRangeException>(()=>s.TryConfigureCamera(OutsideCameraPolicy.FixedAt(33*u)));s.Dispose();Assert.Throws<System.ObjectDisposedException>(()=>s.TryConfigureCamera(p));
        }
    }
}
