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
    }
}
