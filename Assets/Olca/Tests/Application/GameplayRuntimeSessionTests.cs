using System;
using System.Reflection;
using NUnit.Framework;
using Olca.Application.Management;
using Olca.Core.Domain;
using Olca.Core.Logic.Integration;
using Olca.Unity;
namespace Olca.Tests.Application
{
    public sealed class GameplayRuntimeSessionTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test] public void 更新口_別所有者と再入を拒否し追いつきフレームは全て処理する()
        {
            var owner=new object();var loop=new UnityLogicalLoop();long time=0;
            typeof(UnityLogicalLoop).GetField("readTimestamp",Private).SetValue(loop,(Func<long>)(()=>time));
            typeof(UnityLogicalLoop).GetField("observed",Private).SetValue(loop,0L);
            using(var runtime=new GameplayRuntimeSession(owner,loop)){
                runtime.Initialize(7);int frames=0;
                Assert.Throws<InvalidOperationException>(()=>runtime.Pump(new object(),true,(_,__,___)=>frames++,()=>true,()=>true));
                time=System.Diagnostics.Stopwatch.Frequency/10;
                runtime.Pump(owner,true,(_,__,___)=>{frames++;Assert.Throws<InvalidOperationException>(()=>runtime.Pump(owner,true,(_,__,___)=>{},()=>true,()=>true));},()=>true,()=>true);
                Assert.AreEqual(6,frames);Assert.AreEqual(6,loop.Clock.Frame);
                runtime.Pump(owner,true,(_,__,___)=>frames++,()=>true,()=>true);Assert.AreEqual(6,frames,"同じ観測時刻を二重に進めない");
                runtime.Dispose();time+=System.Diagnostics.Stopwatch.Frequency;runtime.Pump(owner,true,(_,__,___)=>frames++,()=>true,()=>true);Assert.AreEqual(6,frames);
            }
        }

        [TestCase(false)] [TestCase(true)] public void 更新中終了_後続callbackを止め未処理負債を維持する(bool command)
        {
            var owner=new object();var loop=new UnityLogicalLoop();long now=0;
            typeof(UnityLogicalLoop).GetField("readTimestamp",Private).SetValue(loop,(Func<long>)(()=>now));
            typeof(UnityLogicalLoop).GetField("observed",Private).SetValue(loop,0L);
            using(var runtime=new GameplayRuntimeSession(owner,loop)){
                runtime.Initialize(7);int ticks=0,after=0;
                if(command){loop.Schedule(()=>runtime.Dispose());loop.Schedule(()=>after++);}
                now=System.Diagnostics.Stopwatch.Frequency/10;
                runtime.Pump(owner,true,(_,__,___)=>{ticks++;runtime.Dispose();},()=>true,()=>true);
                Assert.AreEqual(command?0:1,ticks);Assert.AreEqual(0,after);Assert.AreEqual(1,loop.Clock.Frame);Assert.AreEqual(5,loop.Clock.PendingFrames);
                loop.Advance(10000000,new LogicalInput(),(_,__,___)=>after++);Assert.AreEqual(0,after);Assert.AreEqual(5,loop.Clock.PendingFrames);
            }
        }
        [Test] public void 所有終了_入力購読とモデル参照を返却しCoreを停止する()
        {
            var runtime=new GameplayRuntimeSession(new object(),new UnityLogicalLoop(observeEditor:true));runtime.Initialize(7);var input=runtime.Input;input.StartEventCapture();
            var core=runtime.CreateDungeon(()=>new Olca.Core.Logic.Integration.DungeonBattleManager(DungeonConfig.CreateTestDungeon(6,1,2),new PlayerCharacter(100),new Deck(),runtime.Random,runtime.Logger));
            Assert.AreSame(core,runtime.Dungeon);Assert.Throws<InvalidOperationException>(()=>runtime.Initialize(8));Assert.Throws<InvalidOperationException>(()=>runtime.CreateDungeon(()=>core));
            runtime.Dispose();runtime.Dispose();Assert.IsTrue(core.IsSuspended);Assert.IsNull(runtime.Input);Assert.IsNull(runtime.Dungeon);Assert.IsFalse((bool)typeof(UnityInputProvider).GetField("capturing",Private).GetValue(input));
        }
        [Test] public void 起動再試行_既存時計を返し入力は新規生成する()
        {
            var loop=new UnityLogicalLoop(observeEditor:true);var first=new GameplayRuntimeSession(new object(),loop);first.Initialize(7);var input=first.Input;input.StartEventCapture();first.ReturnLoopForInitializationRetry();
            using(var retry=new GameplayRuntimeSession(new object(),loop)){retry.Initialize(7);Assert.AreSame(loop,retry.Loop);Assert.AreNotSame(input,retry.Input);Assert.IsFalse((bool)typeof(UnityInputProvider).GetField("capturing",Private).GetValue(input));}
        }
    }
}
