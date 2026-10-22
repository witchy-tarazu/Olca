using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Integration;

namespace Olca.Tests.Battle
{
    public class PureCustomCommandTests
    {
        [TestCase(1)] [TestCase(7)] [TestCase(31)] [TestCase(240)]
        public void 同じ明示コマンド列は描画負荷とバッチと文化に依存しない(int batch)
        {
            string expected = CustomBattleReplay.Trace(); var old = System.Globalization.CultureInfo.CurrentCulture;
            try {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                Assert.AreEqual(expected, CustomBattleReplay.Trace(batch: batch, renderWork: () => { long n = 0; for (int i = 0; i < 2000; i++) n = unchecked(n * 33 + i); GC.KeepAlive(n); }));
            } finally { System.Globalization.CultureInfo.CurrentCulture = old; }
        }
        [Test]
        public void 取消加工連鎖逆順Undoと確定復帰は素材を二重消費せず戦闘時間を進めない()
        {
            var simulation = CustomBattleReplay.Create(); var screen = simulation.Battle.CustomScreen;
            var initial = screen.Commands.Observe(); var originalHashes = initial.Hand.Select(c => c.DefinitionHash).OrderBy(x => x).ToArray();
            var commands = CustomBattleReplay.Commands(); var rejected = new HashSet<int> { 1, 2, 5, 6, 9, 10, 14, 27, 30, 31 };
            int cardsUsed = 0; simulation.Battle.CardUsage.OnCardUsed += _ => cardsUsed++;
            for (int i = 0; i < commands.Count; i++) {
                var result = simulation.Step(commands[i]);
                if (commands[i].Custom != null) Assert.AreEqual(!rejected.Contains(i + 1), result.Events.Any(e => e.StartsWith("custom-accepted:")), "受付step " + (i + 1));
                if (i < 31) { Assert.AreEqual(0, result.LogicalFrame); Assert.AreEqual(100, result.PlayerHp); }
                if (i == 11) { // プレビュー取消は個体も素材も消費しない。
                    CollectionAssert.AreEqual(initial.Hand.Select(c => c.Handle).OrderBy(x => x), screen.Commands.Observe().Hand.Select(c => c.Handle).OrderBy(x => x));
                    Assert.AreEqual(0, screen.CustomizationUndoCount);
                }
                if (i == 14) { Assert.AreEqual(2, screen.CustomizationUndoCount); Assert.AreEqual(4, screen.CurrentHand.Count); Assert.AreEqual(2, screen.CurrentHand.Single(c => c.TuningCount > 0).TuningCount); }
                if (i == 17) { Assert.IsEmpty(screen.SelectedCards); Assert.AreEqual(1, screen.CustomizationUndoCount); }
                if (i == 18 || i == 20 || i == 22) {
                    CollectionAssert.AreEqual(originalHashes, screen.Commands.Observe().Hand.Select(c => c.DefinitionHash).OrderBy(x => x));
                    Assert.AreEqual(0, screen.CustomizationUndoCount);
                }
                if (i == 25) { Assert.IsTrue(screen.IsConfirming && simulation.Battle.IsPaused); Assert.AreEqual(1, simulation.Battle.CardUsage.RemainingCount); }
                if (i == 28) { Assert.IsFalse(screen.IsOpen || simulation.Battle.IsPaused); Assert.AreEqual(0, screen.CustomizationUndoCount); }
            }
            Assert.AreEqual(1, cardsUsed); Assert.AreEqual(0, simulation.Battle.CardUsage.RemainingCount);
            Assert.IsTrue(simulation.Battle.Enemies[0].CurrentHp < 120); Assert.Greater(simulation.Battle.LogicalFrame, 0);
            Assert.AreEqual(6, initial.Hand.Count, "過去観測は後続加工で変わらない");
        }
        [Test]
        public void 不正対象とポーズと閉画面の拒否は乱数手札コストを変更しない()
        {
            var simulation = CustomBattleReplay.Create(); var battle = simulation.Battle; var commands = battle.CustomScreen.Commands;
            var before = commands.Observe(); long draws = simulation.Random.Draws;
            Assert.AreEqual(CustomCommandRejection.CardUnavailable, commands.Execute(new CustomScreenCommand(CustomCommandKind.Select, long.MaxValue)).Rejection);
            Assert.AreEqual(CustomCommandRejection.InvalidCommand, commands.Execute(new CustomScreenCommand((CustomCommandKind)999)).Rejection);
            battle.Pause(BattlePauseReason.User);
            Assert.AreEqual(CustomCommandRejection.UserPaused, commands.Execute(new CustomScreenCommand(CustomCommandKind.Select, before.Hand[0].Handle)).Rejection);
            battle.Resume(BattlePauseReason.User); Assert.AreEqual(before.Canonical, commands.Observe().Canonical); Assert.AreEqual(draws, simulation.Random.Draws);
            commands.Execute(new CustomScreenCommand(CustomCommandKind.Confirm)); commands.Execute(new CustomScreenCommand(CustomCommandKind.ReturnToBattle));
            Assert.AreEqual(CustomCommandRejection.ScreenClosed, commands.Execute(new CustomScreenCommand(CustomCommandKind.Undo)).Rejection);
            Assert.AreEqual(0, battle.LogicalFrame);
        }
        [Test]
        public void ゲージ満タンの開始は動作時間とタメ量を保持して未使用キューを返す()
        {
            var simulation = new BattleSimulation(new BattleSimulationSettings(), 123); var battle = simulation.Battle;
            simulation.Step(new BattleFrameCommand(new LogicalInput { BasicAttackButton = true, BasicAttackButtonDown = true }));
            long frame = battle.LogicalFrame; battle.CustomGauge.SetGaugeMs(CustomGaugeSystem.MAX_GAUGE_MS);
            var opened = simulation.Step(new BattleFrameCommand(new LogicalInput { CustomScreenButtonDown = true }));
            Assert.IsTrue(battle.CustomScreen.IsOpen); Assert.AreEqual(frame, opened.LogicalFrame); Assert.AreEqual(2, battle.CustomScreen.AvailableCards.Count);
            simulation.Step(new BattleFrameCommand(custom: new CustomScreenCommand(CustomCommandKind.Confirm)));
            simulation.Step(new BattleFrameCommand(custom: new CustomScreenCommand(CustomCommandKind.ReturnToBattle)));
            Assert.AreEqual(frame, battle.LogicalFrame); Assert.AreEqual(2, battle.CardUsage.RemainingCount);
            simulation.Step(new BattleFrameCommand()); Assert.IsFalse(battle.HasPlayerAction, "復帰時の解放だけでは暴発しない");
        }
        [Test]
        public void 通常入力の開始拒否はフレームを失わず復帰も別のポーズを解除しない()
        {
            var simulation = new BattleSimulation(new BattleSimulationSettings(), 123);
            var rejected = simulation.Step(new BattleFrameCommand(new LogicalInput { CustomScreenButtonDown = true }));
            Assert.AreEqual(1, rejected.LogicalFrame); Assert.IsFalse(simulation.Battle.IsPaused);
            Assert.IsTrue(rejected.Events.Any(e => e.Contains("GaugeNotFull")));
            var custom = CustomBattleReplay.Create(); var battle = custom.Battle; var commands = battle.CustomScreen.Commands;
            Assert.IsTrue(commands.Execute(new CustomScreenCommand(CustomCommandKind.Confirm)).Accepted);
            battle.Pause(BattlePauseReason.User);
            Assert.IsTrue(commands.Execute(new CustomScreenCommand(CustomCommandKind.ReturnToBattle)).Accepted);
            Assert.IsFalse(battle.CustomScreen.IsOpen); Assert.IsTrue(battle.IsPaused && battle.IsUserPaused);
            custom.Step(new BattleFrameCommand()); Assert.AreEqual(0, battle.LogicalFrame);
            custom.Step(new BattleFrameCommand(userPaused: false)); Assert.AreEqual(1, battle.LogicalFrame);
        }

        [Test]
        public void Unityの実View選択と確定も同じコマンドを一度ずつ通る()
        {
            var simulation = CustomBattleReplay.Create(); var screen = simulation.Battle.CustomScreen;
            var root = new UnityEngine.GameObject("pure-custom-command-view", typeof(UnityEngine.RectTransform));
            var view = root.AddComponent<Olca.Presentation.CustomScreen.CustomScreenView>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(Olca.Presentation.CustomScreen.CustomScreenView).GetField("customScreen", flags).SetValue(view, screen);
            try {
                Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame();
                var card = screen.CurrentHand[0]; long sequence = screen.Commands.Sequence;
                view.OnCardClicked(card); Assert.AreEqual(sequence + 1, screen.Commands.Sequence); Assert.AreSame(card, screen.SelectedCards[0]);
                view.OnCardClicked(card); Assert.AreEqual(sequence + 2, screen.Commands.Sequence); Assert.IsEmpty(screen.SelectedCards);
                typeof(Olca.Presentation.CustomScreen.CustomScreenView).GetMethod("OnConfirmClicked", flags).Invoke(view, null);
                Assert.AreEqual(sequence + 3, screen.Commands.Sequence); Assert.IsTrue(screen.IsConfirming);
                Assert.AreEqual(0, simulation.Battle.LogicalFrame);
            } finally { UnityEngine.Object.DestroyImmediate(root); Olca.Unity.GameplayPause.SetPaused(false); }
        }
    }
}
