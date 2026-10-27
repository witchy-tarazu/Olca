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
    }
}
