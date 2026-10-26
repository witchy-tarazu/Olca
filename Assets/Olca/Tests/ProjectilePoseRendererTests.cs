using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Logic.CardEffects.Effects;
using Olca.Presentation.Field;
using GridPoint = Olca.Core.Domain.Vector2Int;

namespace Olca.Tests
{
    public class ProjectilePoseRendererTests
    {
        private GameObject root;
        private Texture2D texture;
        private Sprite first, second;
        private ProjectilePoseRenderer renderer;
        private readonly List<string> keys = new List<string>();
        private BattleSystem battle;
        [SetUp] public void Setup()
        {
            root = new GameObject("projectile-fixture"); texture = new Texture2D(2, 2);
            first = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            second = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            renderer = new ProjectilePoseRenderer(root.transform);
            renderer.Configure(new[] { Appearance("shared") });
            battle = new BattleSystem(new PlayerCharacter(100, 10), new List<Enemy> { new Enemy("enemy", "テキ", 1000, ElementType.None, new GridPoint(4, 0)) }, new Deck());
        }
        private ProjectileAppearance Appearance(string key) => new ProjectileAppearance(key, new[] { first, second },
            new[] { new ActionDisplayFrame(0, 1), new ActionDisplayFrame(1, 1) }, .4f, .2f, 1.4f, true);
        [TearDown] public void Cleanup()
        {
            renderer.Dispose(); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); UnityEngine.Object.DestroyImmediate(texture);
            var creators = (Dictionary<string, Func<ICardEffect>>)typeof(CardEffectFactory).GetField("staticEffectCreators", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            foreach (var key in keys) creators.Remove(key); keys.Clear();
        }
        private void Refresh() => renderer.Refresh(battle, p => new Vector3(p.X, 0, 0), f => f, 0, 6);
        private void Tick(int n) { for (int i = 0; i < n; i++) battle.TickFrame(16); }
        private Olca.Core.Interfaces.FrameTask<bool> Use(string id, bool product, CancellationToken ct = default)
        {
            var modifier = new CardModifier(); if (!product) modifier.AdditionalEffects.Add(new AdditionalEffect { EffectType = AdditionalEffectType.DelayedProjectile, DelayTime = .2f, TargetOffset = new GridPoint(3, 0), CancelOnHole = true });
            var card = new Card(id, "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 1, "", modifier: modifier);
            var visual = new ActionVisualDefinition("throw", null, new[] { new ActionDisplayFrame(0, 1) }, null, new[] { new ActionDisplayFrame(1, 1) }, 1, "shared");
            var action = new CardActionDefinition("throw", () => new ModifiableCardEffect(DamageTargetType.All, productBomb: product), new PlayerAttackTiming(1, 1), visual);
            CardEffectFactory.RegisterEffectStatic(id, () => new CardActionSequenceEffect(new[] { action })); keys.Add(id);
            battle.CardUsage.EnqueueCards(new List<Card> { card }); return battle.CardUsage.TryUseCurrentCardAsync(ct);
        }
        [TestCase(false)][TestCase(true)]
        public void 登録経路が共用素材を弾へ渡し本体終了後も既存の寿命で進む(bool product)
        {
            var use = Use("projectile_a", product); Tick(1); var effect = battle.DelayedEffects.GetActiveEffects().Single();
            Assert.AreEqual("shared", effect.ProjectileVisualKey); Assert.AreEqual(new GridPoint(0, 0), effect.SourcePosition); Assert.AreEqual(0, effect.ElapsedDisplayFrames);
            Refresh(); Assert.AreEqual(1, renderer.ActiveCount); Assert.AreSame(first, root.GetComponentInChildren<SpriteRenderer>().sprite);
            Tick(1); Refresh(); Assert.IsTrue(use.GetAwaiter().GetResult()); Assert.IsNull(battle.PlayerActionVisual); Assert.AreEqual(1, renderer.ActiveCount); Assert.AreEqual(1, effect.ElapsedDisplayFrames); Assert.AreSame(second, root.GetComponentInChildren<SpriteRenderer>().sprite);
            if (product) { Assert.AreEqual(1484, effect.RemainingTimeMs); Tick(89); } else Tick(11);
            Refresh(); Assert.AreEqual(0, renderer.ActiveCount); Assert.IsTrue(effect.WasExecuted);
        }
        [Test]
        public void 別カードの同時弾は素材だけを共有し発射後取消でも残り敗北時に消去する()
        {
            using var cts = new CancellationTokenSource(); var use = Use("projectile_a", true, cts.Token); Tick(1); cts.Cancel(); Assert.IsTrue(use.GetAwaiter().GetResult());
            var next = Use("projectile_b", true); Tick(2); Assert.IsTrue(next.GetAwaiter().GetResult()); Refresh(); Assert.AreEqual(2, renderer.ActiveCount);
            var views = root.GetComponentsInChildren<SpriteRenderer>(); Assert.AreEqual(2, views.Length); Assert.AreNotSame(views[0], views[1]);
            battle.Player.TakeDamage(1000); Refresh(); Assert.IsTrue(battle.IsGameOver); Assert.AreEqual(0, renderer.ActiveCount);
        }
        [Test]
        public void カスタム手動暗転ヒットストップは寿命とコマと位置を進めない()
        {
            Use("projectile_a", true); Tick(2); Refresh(); var e = battle.DelayedEffects.GetActiveEffects().Single(); long elapsed = e.ElapsedDisplayFrames; int remaining = e.RemainingTimeMs;
            var view = root.GetComponentInChildren<SpriteRenderer>(); var pos = view.transform.position; var sprite = view.sprite;
            void Frozen(int count) { Tick(count); Refresh(); Assert.AreEqual(elapsed, e.ElapsedDisplayFrames); Assert.AreEqual(remaining, e.RemainingTimeMs); Assert.AreEqual(pos, view.transform.position); Assert.AreSame(sprite, view.sprite); }
            battle.Pause(); battle.Pause(BattlePauseReason.User); Frozen(5); battle.Resume(); Frozen(3); battle.Resume(BattlePauseReason.User);
            battle.TryStartDarkeningEffect(DarkeningSide.Opponent, 0, 3, () => { }); Frozen(3);
            battle.RunLogicalFrame(() => battle.Enemies[0].TakeDamage(1, ElementType.None, poiseDamage: 0, hitStopFrames: 3)); Frozen(3);
            Tick(1); Refresh(); Assert.Greater(e.ElapsedDisplayFrames, elapsed); Assert.AreNotEqual(pos, view.transform.position);
        }
        [Test]
        public void 発生前取消では生成せず穴落下と戦闘解放では残留しない()
        {
            using (var cts = new CancellationTokenSource()) { var use = Use("projectile_cancel", false, cts.Token); cts.Cancel(); Assert.IsFalse(use.GetAwaiter().GetResult()); }
            Refresh(); Assert.AreEqual(0, renderer.ActiveCount); battle.CardUsage.Clear();
            var use2 = Use("projectile_hole", false); Tick(2); Assert.IsTrue(use2.GetAwaiter().GetResult()); var effect = battle.DelayedEffects.GetActiveEffects().Single(); Refresh();
            battle.Field.ChangePanelType(effect.TargetPosition, PanelType.Hole); Tick(12); Refresh(); Assert.IsFalse(effect.WasExecuted); Assert.AreEqual(0, renderer.ActiveCount);
            Use("projectile_retreat", true); Tick(2); Refresh(); Assert.AreEqual(1, renderer.ActiveCount); typeof(BattleSystem).GetMethod("AbandonForCheckpoint", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(battle, null); Refresh(); Assert.AreEqual(0, renderer.ActiveCount); Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount);
        }
        [Test]
        public void 未登録参照を代用せず非表示解放後も効果寿命を変更せず再接続できる()
        {
            Use("projectile_a", true); Tick(2); var effect = battle.DelayedEffects.GetActiveEffects().Single(); Refresh(); int remaining = effect.RemainingTimeMs;
            renderer.Clear(); Assert.AreEqual(0, renderer.ActiveCount); Assert.AreEqual(remaining, effect.RemainingTimeMs); Refresh(); Assert.AreEqual(1, renderer.ActiveCount);
            renderer.Configure(new[] { Appearance("different") }); Refresh(); Assert.AreEqual(0, renderer.ActiveCount); Assert.AreEqual(remaining, effect.RemainingTimeMs);
            renderer.Dispose(); Assert.IsTrue(first != null); Assert.IsTrue(texture != null);
        }
        [Test]
        public void 敵使用者が死亡しても発射済み弾を保持し最後の敵撃破では全て解放する()
        {
            var source = new Enemy("source", "テキ", 1000, ElementType.None, new GridPoint(4, 0)); battle = new BattleSystem(new PlayerCharacter(100, 10), new List<Enemy> { source, new Enemy("other", "テキ", 1000, ElementType.None, new GridPoint(5, 0)) }, new Deck());
            var card = new Card("enemy_projectile", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 1, "");
            var action = new CardActionDefinition("throw", () => new ModifiableCardEffect(DamageTargetType.All, productBomb: true), new PlayerAttackTiming(1, 1),
                new ActionVisualDefinition("throw", null, new[] { new ActionDisplayFrame(0, 1) }, null, new[] { new ActionDisplayFrame(1, 1) }, 1, "shared"));
            var task = new CardActionSequenceEffect(new[] { action }).ExecuteAsync(new BattleContext(battle, card, source, -1), CancellationToken.None);
            Tick(2); task.GetAwaiter().GetResult(); Refresh(); var effect = battle.DelayedEffects.GetActiveEffects().Single();
            Assert.AreEqual(new GridPoint(4, 0), effect.SourcePosition); Assert.AreEqual(new GridPoint(1, 0), effect.TargetPosition);
            battle.RunLogicalFrame(() => source.TakeDamage(10000, ElementType.None)); Refresh(); Assert.IsFalse(battle.IsGameOver); Assert.AreEqual(1, renderer.ActiveCount);
            battle.RunLogicalFrame(() => battle.Enemies.Last().TakeDamage(10000, ElementType.None)); Refresh(); Assert.IsTrue(battle.IsGameOver); Assert.AreEqual(0, renderer.ActiveCount); Assert.AreEqual(0, battle.DelayedEffects.ActiveEffectCount);
        }
        [Test]
        public void 入力配列を固定し不正なコマと重複登録を拒否する()
        {
            var images = new[] { first, second }; var frames = new[] { new ActionDisplayFrame(0, 2), new ActionDisplayFrame(1, 1) };
            var value = new ProjectileAppearance("shared", images, frames, 0, 0, 0); images[0] = second; frames[0] = new ActionDisplayFrame(1, 5);
            Assert.AreSame(first, value.SelectFrame(0)); Assert.AreSame(second, value.SelectFrame(100));
            Assert.Throws<ArgumentException>(() => new ProjectileAppearance("bad", images, new[] { new ActionDisplayFrame(5, 1) }, 0, 0, 0));
            Assert.Throws<ArgumentException>(() => renderer.Configure(new[] { value, value })); Use("projectile_a", true); Tick(2); Refresh(); Assert.AreEqual(1, renderer.ActiveCount);
        }
    }
}
