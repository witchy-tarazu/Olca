using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;

namespace Olca.Tests.Battle
{
    public class PureBattleSimulationTests
    {
        private sealed class HostContext : SynchronizationContext
        {
            public int Posts;
            public override void Post(SendOrPostCallback callback, object state) { Posts++; throw new InvalidOperationException("外部時計へ継続を送ってはいけません"); }
        }
        private static async FrameTask<int> Ordered(FrameCompletionSource first, FrameCompletionSource second, List<string> trace)
        { trace.Add("begin"); await first.Task; trace.Add("first"); await second.Task; trace.Add("second"); return 7; }
        [Test]
        public void 共通入力振分けはカードを優先しポーズ中のカードを発生させない()
        {
            var simulation = new Olca.Core.Logic.Battle.BattleSimulation(new Olca.Core.Logic.Battle.BattleSimulationSettings(), 123);
            var attack = new Olca.Core.Logic.Battle.BasicAttackInputState();
            var input = new Olca.Core.Logic.Integration.LogicalInput { BasicAttackButtonDown = true, BasicAttackButton = true };
            int uses = 0;
            attack.ProcessInput(simulation.Battle, input, 15, 12, 2, () => uses++);
            Assert.IsTrue(attack.IsCharging);
            input.CardUseButtonDown = true;
            attack.ProcessInput(simulation.Battle, input, 15, 12, 2, () => uses++);
            Assert.IsFalse(attack.IsCharging); Assert.AreEqual(1, uses);
            simulation.Battle.Pause(Olca.Core.Logic.Battle.BattlePauseReason.User);
            attack.ProcessInput(simulation.Battle, input, 15, 12, 2, () => uses++);
            Assert.AreEqual(1, uses);
        }
        [Test]
        public void 待機は呼出元Contextや描画に送らず完了フレーム内で順番通り継続する()
        {
            var old = SynchronizationContext.Current; var host = new HostContext(); SynchronizationContext.SetSynchronizationContext(host);
            try {
                var first = new FrameCompletionSource(); var second = new FrameCompletionSource(); var events = new List<string>();
                var task = Ordered(first, second, events); var observed = task.AsTask();
                CollectionAssert.AreEqual(new[] { "begin" }, events); Assert.AreEqual(FrameTaskStatus.Pending, task.Status);
                first.TrySetResult(); CollectionAssert.AreEqual(new[] { "begin", "first" }, events);
                second.TrySetResult(); Assert.AreEqual(FrameTaskStatus.Succeeded, task.Status); Assert.AreEqual(7, task.GetAwaiter().GetResult());
                Assert.IsTrue(observed.IsCompleted); Assert.AreEqual(7, observed.Result); Assert.AreEqual(0, host.Posts);
                Assert.IsFalse(second.TrySetResult()); CollectionAssert.AreEqual(new[] { "begin", "first", "second" }, events);
            } finally { SynchronizationContext.SetSynchronizationContext(old); }
        }
        [Test]
        public void 外部取消は元の待機を完了させず複数観測と取消解除が一度だけ成立する()
        {
            var source = new FrameCompletionSource(); var cts = new CancellationTokenSource(); var linked = source.Task.AttachExternalCancellation(cts.Token);
            cts.Cancel(); Assert.AreEqual(FrameTaskStatus.Canceled, linked.Status); Assert.AreEqual(FrameTaskStatus.Pending, source.Task.Status);
            Assert.Throws<OperationCanceledException>(() => linked.GetAwaiter().GetResult());
            source.TrySetResult(); Assert.AreEqual(FrameTaskStatus.Canceled, linked.Status); cts.Dispose();
        }
        [TestCase(1)] [TestCase(7)] [TestCase(31)] [TestCase(240)]
        public void 同じseed入力設定は描画負荷とフレーム分割とUnity入力アダプターに依存しない(int batch)
        {
            string expected = BattleSimulation.SampleTrace();
            string actual = BattleSimulation.SampleTrace(batchSize: batch, renderWork: () => { long value = 1; for (int i = 0; i < 2000; i++) value = unchecked(value * 33 + i); GC.KeepAlive(value); }, attack: new Olca.Application.Battle.BasicAttackInputState());
            Assert.AreEqual(expected, actual);
        }
        [Test]
        public void 最小縦断は移動カード状態地形AIポーズを通り一度だけ勝利へ到達する()
        {
            var simulation = new BattleSimulation(new BattleSimulationSettings(), 123); var events = new List<string>();
            long frozen = 0; bool moved = false, card = false, hit = false;
            for (int frame = 1; frame <= 240; frame++) {
                var result = simulation.Step(BattleSimulation.SampleCommand(frame)); events.AddRange(result.Events);
                moved |= simulation.Battle.Player.GridPosition.X == 1;
                foreach (var value in result.Events) { card |= value.StartsWith("card:"); hit |= value.StartsWith("basic:"); }
                if (frame == 49) frozen = result.LogicalFrame;
                if (frame >= 50 && frame < 65) Assert.AreEqual(frozen, result.LogicalFrame);
            }
            Assert.IsTrue(moved && card && hit); Assert.AreEqual(BattleState.Victory, simulation.Battle.State);
            Assert.AreEqual(1, events.FindAll(x => x.StartsWith("end:")).Count); Assert.Greater(simulation.Random.Draws, 0);
            Assert.IsTrue(simulation.Battle.Player.CurrentHp > 0 && simulation.Battle.Player.CurrentHp < 100, "既存AIの被害を緩和していない");
        }
        [Test]
        public void シード乱数はセッションごとに独立し範囲を守る()
        {
            var a = new SeededBattleRandom(17); var b = new SeededBattleRandom(17);
            for (int i = 0; i < 100; i++) { Assert.AreEqual(a.Range(-10, 20), b.Range(-10, 20)); Assert.AreEqual(a.Value, b.Value); }
            Assert.AreEqual(a.State, b.State); Assert.Throws<ArgumentOutOfRangeException>(() => a.Range(1, 1));
        }
    }
}
