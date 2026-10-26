using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;

namespace Olca.Tests.CardTests
{
    [TestFixture]
    public class CardActionSequenceTests
    {
        private readonly List<string> registered = new List<string>();
        private sealed class Signal : ICardEffect
        {
            private readonly Action<BattleContext> callback;
            public Signal(Action<BattleContext> callback) { this.callback = callback; }
            public void Execute(BattleContext context) { context.NotifyActionEffectResolved(); callback?.Invoke(context); }
        }
        private static Card Card(string id, int? startup = null) => new Card(id, "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 10, "", startupFrames: startup);
        private static BattleSystem Battle() => new BattleSystem(new PlayerCharacter(100, 10), new List<Enemy> { new Enemy("target", "テキ", 1000, ElementType.None, new Vector2Int(4, 0)) }, new Deck());
        private static ActionVisualDefinition Visual(string motion, string equipment, int startup, int recovery, int frame = 0)
            => new ActionVisualDefinition(motion, equipment, startup == 0 ? null : new[] { new ActionDisplayFrame(frame, startup) }, null,
                recovery == 0 ? null : new[] { new ActionDisplayFrame(frame + 1, recovery) }, startup);
        private static CardActionDefinition Step(string id, string equipment, int startup, int recovery, Action<BattleContext> callback = null)
            => new CardActionDefinition(id, () => new Signal(callback), new PlayerAttackTiming(startup, recovery), Visual("body/" + id, equipment, startup, recovery));
        private void Register(string id, params CardActionDefinition[] actions)
        { CardEffectFactory.RegisterEffectStatic(id, () => new CardActionSequenceEffect(actions)); registered.Add(id); }
        private static void Tick(BattleSystem battle, int n) { for (int i = 0; i < n; i++) battle.TickFrame(16); }
        [TearDown]
        public void Cleanup()
        {
            var field = typeof(CardEffectFactory).GetField("staticEffectCreators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var definitions = (Dictionary<string, Func<ICardEffect>>)field.GetValue(null);
            foreach (var id in registered) definitions.Remove(id); registered.Clear();
        }

        [Test]
        public void 剣から銃へ既存の通常カード使用経路で順次切替し前の装備参照を残さない()
        {
            var signals = new List<long>(); Register("action_test_sequence", Step("sword", "blade/common", 2, 2, c => signals.Add(c.Battle.LogicalFrame)), Step("shot", "gun/common", 1, 2, c => signals.Add(c.Battle.LogicalFrame)));
            var battle = Battle(); battle.CardUsage.EnqueueCards(new[] { Card("action_test_sequence") }.ToList());
            var use = battle.CardUsage.TryUseCurrentCardAsync(); var first = battle.PlayerActionVisual;
            Assert.AreEqual("blade/common", first.Definition.Visual.EquipmentKey); Assert.AreEqual(PlayerAttackPhase.Startup, battle.PlayerActionPhase);
            Tick(battle, 2); Assert.AreEqual(2, first.EffectFrame); Assert.IsTrue(first.EffectMarkerMatches); Assert.AreEqual(PlayerAttackPhase.Recovery, battle.PlayerActionPhase);
            Tick(battle, 2); var second = battle.PlayerActionVisual; Assert.AreNotSame(first, second); Assert.AreEqual("gun/common", second.Definition.Visual.EquipmentKey);
            Tick(battle, 3); Assert.AreEqual(new long[] { 2, 5 }, signals); Assert.IsNull(battle.PlayerActionVisual); Assert.IsFalse(battle.HasPlayerAction); Assert.IsTrue(use.GetAwaiter().GetResult());
        }
        [Test]
        public void 同じアクションを別カードが共有でき同素材の別フレーム設定も禁止しない()
        {
            var shared = Step("sword", "blade/common", 1, 1); Register("action_test_a", shared); Register("action_test_b", shared);
            var battle = Battle(); battle.CardUsage.EnqueueCards(new[] { Card("action_test_a"), Card("action_test_b") }.ToList());
            var a = battle.CardUsage.TryUseCurrentCardAsync(); Assert.AreSame(shared, battle.PlayerActionVisual.Definition); Tick(battle, 2); Assert.IsTrue(a.GetAwaiter().GetResult());
            var b = battle.CardUsage.TryUseCurrentCardAsync(); Assert.AreSame(shared, battle.PlayerActionVisual.Definition); Tick(battle, 2); Assert.IsTrue(b.GetAwaiter().GetResult());
            var alternate = new CardActionDefinition("alternate", () => new Signal(null), new PlayerAttackTiming(3, 2), Visual("body/sword", "blade/common", 3, 2, 2));
            Assert.AreEqual(shared.Visual.MotionKey, alternate.Visual.MotionKey); Assert.AreNotEqual(shared.Timing.StartupFrames, alternate.Timing.StartupFrames);
        }
        [Test]
        public void コマ数と配分を保持し境界で切替わり表示から性能待機を追加しない()
        {
            var input = new[] { new ActionDisplayFrame(0, 1), new ActionDisplayFrame(3, 2) };
            var visual = new ActionVisualDefinition("body/shared", null, input, new[] { new ActionDisplayFrame(4, 1) }, new[] { new ActionDisplayFrame(2, 2) }, 3);
            input[0] = new ActionDisplayFrame(9, 8);
            Assert.AreEqual(0, visual.SelectFrame(PlayerAttackPhase.Startup, 0)); Assert.AreEqual(3, visual.SelectFrame(PlayerAttackPhase.Startup, 1));
            Assert.AreEqual(4, visual.SelectFrame(PlayerAttackPhase.Executing, 100)); Assert.AreEqual(-1, visual.SelectFrame(PlayerAttackPhase.None, 0));
            Assert.Throws<ArgumentException>(() => new CardActionDefinition("bad", () => new Signal(null), new PlayerAttackTiming(4, 2), visual));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ActionDisplayFrame(0, 0));
        }
        [TestCase(false)][TestCase(true)]
        public void 停止理由を重ねても論理時計と表示フレームは進まず残る停止を勝手に解除しない(bool extra)
        {
            Register("action_test_pause", Step("throw", "bomb/common", 3, 2)); var battle = Battle(); battle.CardUsage.EnqueueCards(new[] { Card("action_test_pause") }.ToList()); var use = battle.CardUsage.TryUseCurrentCardAsync();
            Tick(battle, 1); var state = battle.PlayerActionVisual; long elapsed = state.ElapsedPhaseFrames; int frame = state.DisplayFrame;
            battle.Pause(); if (extra) battle.Pause(BattlePauseReason.User); Tick(battle, 10); Assert.AreEqual(elapsed, state.ElapsedPhaseFrames); Assert.AreEqual(frame, state.DisplayFrame);
            battle.Resume(); if (extra) { Tick(battle, 3); Assert.AreEqual(elapsed, state.ElapsedPhaseFrames); battle.Resume(BattlePauseReason.User); }
            Tick(battle, 4); Assert.IsTrue(use.GetAwaiter().GetResult()); Assert.IsNull(battle.PlayerActionVisual);
        }
        [TestCase(false)][TestCase(true)]
        public void 発生前の外部取消は未消費で発生後の取消は使用済みとし次アクションを始めない(bool after)
        {
            int hits = 0; Register("action_test_cancel", Step("sword", "blade/common", 2, 3, _ => hits++), Step("shot", "gun/common", 1, 1, _ => hits++));
            var battle = Battle(); battle.CardUsage.EnqueueCards(new[] { Card("action_test_cancel") }.ToList()); using (var cts = new CancellationTokenSource()) {
                var use = battle.CardUsage.TryUseCurrentCardAsync(cts.Token); Tick(battle, after ? 2 : 1); cts.Cancel();
                Assert.AreEqual(after, use.GetAwaiter().GetResult()); Assert.AreEqual(after ? 1 : 0, hits); Assert.AreEqual(after ? 0 : 1, battle.CardUsage.RemainingCount); Assert.IsNull(battle.PlayerActionVisual); Tick(battle, 8); Assert.AreEqual(after ? 1 : 0, hits);
            }
        }
        [Test]
        public void 既存ダメージ効果の実発生通知を使い前後隙を二重適用しない()
        {
            var action = new CardActionDefinition("sword", () => new ModifiableCardEffect(DamageTargetType.All), new PlayerAttackTiming(2, 2), Visual("body/sword", "blade/common", 2, 2));
            Register("action_test_damage", action); var battle = Battle(); battle.CardUsage.EnqueueCards(new[] { Card("action_test_damage", 2) }.ToList()); var use = battle.CardUsage.TryUseCurrentCardAsync();
            Tick(battle, 1); Assert.AreEqual(1000, battle.Enemies[0].CurrentHp); Tick(battle, 1); Assert.AreEqual(990, battle.Enemies[0].CurrentHp); Assert.IsTrue(battle.PlayerActionVisual.EffectMarkerMatches);
            Tick(battle, 2); Assert.IsTrue(use.GetAwaiter().GetResult()); Assert.IsNull(battle.PlayerActionVisual);
        }
        [Test]
        public void 既存カードの時間と矛盾する定義は効果発生前に拒否する()
        {
            int hits = 0; var sequence = new CardActionSequenceEffect(new[] { Step("sword", null, 2, 1, _ => hits++) });
            var battle = Battle(); var task = sequence.ExecuteAsync(new BattleContext(battle, Card("conflict", 4)), CancellationToken.None);
            Assert.AreEqual(FrameTaskStatus.Faulted, task.Status); Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()); Assert.AreEqual(0, hits); Assert.IsNull(battle.PlayerActionVisual);
        }
        [Test]
        public void 発射済みボムの寿命は本体のアクション後隙と独立し終了後に装備を残さない()
        {
            var visual = new ActionVisualDefinition("body/throw", "bomb/hand", new[] { new ActionDisplayFrame(0, 1) }, null, new[] { new ActionDisplayFrame(1, 1) }, 1, "bomb/flying");
            var action = new CardActionDefinition("throw", () => new ModifiableCardEffect(DamageTargetType.All, productBomb: true), new PlayerAttackTiming(1, 1), visual);
            Register("action_test_bomb", action); var battle = Battle(); battle.CardUsage.EnqueueCards(new List<Card> { Card("action_test_bomb") }); var use = battle.CardUsage.TryUseCurrentCardAsync();
            Tick(battle, 2); Assert.IsTrue(use.GetAwaiter().GetResult()); Assert.IsNull(battle.PlayerActionVisual); Assert.Greater(battle.DelayedEffects.GetActiveEffects().Count, 0);
            Tick(battle, 5); Assert.Greater(battle.DelayedEffects.GetActiveEffects().Count, 0); Assert.IsNull(battle.PlayerActionVisual);
        }
        [Test]
        public void 同期入口から待機列を開始してカードだけ先に消費しない()
        {
            Register("action_test_sync", Step("sword", "blade/common", 2, 2)); var battle = Battle(); battle.CardUsage.EnqueueCards(new List<Card> { Card("action_test_sync") });
            Assert.Throws<InvalidOperationException>(() => battle.CardUsage.TryUseCurrentCard()); Assert.IsNull(battle.PlayerActionVisual); Assert.AreEqual(1, battle.CardUsage.RemainingCount);
        }
        [Test]
        public void カード全体の派生延長をアクション単位へ暗黙変換しない()
        {
            var card = new Card("derivative", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 10, "", extendRecoveryDuringDerivative: true);
            var sequence = new CardActionSequenceEffect(new[] { Step("sword", null, 2, 1) });
            var task = sequence.ExecuteAsync(new BattleContext(Battle(), card), CancellationToken.None);
            var error = Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult()); StringAssert.Contains("派生待機", error.Message);
        }
        private sealed class SpawnAction : ICardEffect
        {
            private readonly CardActionSequenceEffect sequence;
            public SpawnAction(CardActionSequenceEffect sequence) { this.sequence = sequence; }
            public void Execute(BattleContext context) { sequence.Execute(context); }
        }
        [Test]
        public void 親カード終了後も続く派生列は古い武器やプレイヤー行動状態を復活させない()
        {
            int resolved = 0;
            var child = new CardActionSequenceEffect(new[] { Step("sword", "old/blade", 2, 1, _ => resolved++), Step("shot", "old/gun", 1, 1, _ => resolved++) });
            CardEffectFactory.RegisterEffectStatic("action_test_derived", () => new SpawnAction(child)); registered.Add("action_test_derived");
            var battle = Battle(); battle.CardUsage.EnqueueCards(new List<Card> { Card("action_test_derived") });
            var parent = battle.CardUsage.TryUseCurrentCardAsync(); Assert.IsTrue(parent.GetAwaiter().GetResult()); Assert.IsNull(battle.PlayerActionVisual); Assert.IsFalse(battle.HasPlayerAction);
            Tick(battle, 6); Assert.AreEqual(2, resolved); Assert.IsNull(battle.PlayerActionVisual); Assert.AreEqual(PlayerAttackPhase.None, battle.PlayerActionPhase); Assert.IsFalse(battle.HasPlayerAction);
        }
    }
}
