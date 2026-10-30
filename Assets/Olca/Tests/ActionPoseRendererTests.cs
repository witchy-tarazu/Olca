using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Olca.Presentation.Field;
using System.Threading;

namespace Olca.Tests
{
    [TestFixture]
    public class ActionPoseRendererTests
    {
        private sealed class Signal : ICardEffect { public void Execute(BattleContext context) { context.NotifyActionEffectResolved(); } }
        private GameObject root;
        private SpriteRenderer body;
        private Animator animator;
        private ActionPoseRenderer renderer;
        private readonly List<Sprite> sprites = new List<Sprite>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private Sprite Sprite(string name)
        {
            var texture = new Texture2D(2, 2); var sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero); sprite.name = name;
            textures.Add(texture); sprites.Add(sprite); return sprite;
        }
        [SetUp] public void Setup()
        {
            root = new GameObject("action-view"); body = root.AddComponent<SpriteRenderer>(); animator = root.AddComponent<Animator>();
            body.sprite = Sprite("idle"); root.transform.localScale = new Vector3(-.8f, .8f, 1); root.transform.localRotation = Quaternion.Euler(0, 0, 17);
            renderer = new ActionPoseRenderer(body, animator);
        }
        [TearDown] public void Cleanup()
        {
            renderer.Dispose(); UnityEngine.Object.DestroyImmediate(root);
            foreach (var sprite in sprites) UnityEngine.Object.DestroyImmediate(sprite); foreach (var texture in textures) UnityEngine.Object.DestroyImmediate(texture);
            sprites.Clear(); textures.Clear();
            var field = typeof(CardEffectFactory).GetField("staticEffectCreators", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            ((Dictionary<string, Func<ICardEffect>>)field.GetValue(null)).Remove("pose_fixture");
        }
        private static CardActionDefinition Action(string motion, string equipment, int recoveryIndex = 1)
            => new CardActionDefinition(motion, () => new Signal(), new PlayerAttackTiming(2, 2), new ActionVisualDefinition(motion, equipment, new[] { new ActionDisplayFrame(0, 2) }, null, new[] { new ActionDisplayFrame(recoveryIndex, 2) }, 2));
        private static BattleSystem Battle(params CardActionDefinition[] actions)
        {
            CardEffectFactory.RegisterEffectStatic("pose_fixture", () => new CardActionSequenceEffect(actions));
            var b = new BattleSystem(new PlayerCharacter(100, 10), new List<Enemy> { new Enemy("enemy", "テキ", 1000, ElementType.None, new Olca.Core.Domain.Vector2Int(4, 0)) }, new Deck());
            b.CardUsage.EnqueueCards(new List<Card> { new Card("pose_fixture", "テスト", 1, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Sword, 10, "") }); return b;
        }
        private static void Tick(BattleSystem b, int n) { for (int i = 0; i < n; i++) b.TickFrame(16); }

        [Test]
        public void 異なるコマ数の剣銃を切替え前の武器を残さず本体Transformと元姿勢を保持する()
        {
            var idle = body.sprite; var a = Sprite("sword-ready"); var b = Sprite("sword-hit"); var c = Sprite("shot-ready"); var d = Sprite("shot-hit"); var blade = Sprite("blade"); var gun = Sprite("gun");
            var sword = new ActionAppearance("sword", "blade", new[] { a, b }, new[] { new Vector2(.2f, .3f), new Vector2(.5f, .4f) }, new[] { blade, blade });
            var shot = new ActionAppearance("shot", "gun", new[] { c, c, d }, new[] { Vector2.zero, Vector2.one, new Vector2(.1f, .2f) }, new[] { gun, gun, gun });
            var swordAction = Action("sword", "blade"); var shotAction = Action("shot", "gun", 2); sword.Validate(swordAction.Visual); shot.Validate(shotAction.Visual);
            renderer.Configure(new[] { sword, shot }); var battle = Battle(swordAction, shotAction); var use = battle.CardUsage.TryUseCurrentCardAsync();
            var rotation = root.transform.localRotation; var scale = root.transform.localScale;
            Assert.IsTrue(renderer.Apply(battle.PlayerActionVisual)); Assert.AreSame(a, body.sprite); Assert.AreSame(blade, renderer.EquipmentRenderer.sprite); Assert.IsFalse(animator.enabled);
            Tick(battle, 2); renderer.Apply(battle.PlayerActionVisual); Assert.AreSame(b, body.sprite); Assert.AreEqual(new Vector3(.5f, .4f, 0), renderer.EquipmentRenderer.transform.localPosition);
            Tick(battle, 2); renderer.Apply(battle.PlayerActionVisual); Assert.AreSame(c, body.sprite); Assert.AreSame(gun, renderer.EquipmentRenderer.sprite); Assert.AreEqual(1, root.transform.childCount);
            Tick(battle, 4); Assert.IsTrue(use.GetAwaiter().GetResult()); Assert.IsFalse(renderer.Apply(battle.PlayerActionVisual)); Assert.AreSame(idle, body.sprite); Assert.IsTrue(animator.enabled);
            Assert.IsNull(renderer.EquipmentRenderer.sprite); Assert.IsFalse(renderer.EquipmentRenderer.gameObject.activeSelf); Assert.AreEqual(rotation, root.transform.localRotation); Assert.AreEqual(scale, root.transform.localScale);
        }
        [TestCase(false)][TestCase(true)]
        public void 停止中のコマは変わらず取消時は停止理由を変えず表示を復元する(bool initiallyEnabled)
        {
            animator.enabled = initiallyEnabled;
            var idle = body.sprite; var pose = Sprite("ready"); var prop = Sprite("blade"); renderer.Configure(new[] { new ActionAppearance("sword", "blade", new[] { pose, pose }, new[] { Vector2.zero, Vector2.zero }, new[] { prop, prop }) });
            var battle = Battle(Action("sword", "blade")); using (var cts = new CancellationTokenSource()) {
                var use = battle.CardUsage.TryUseCurrentCardAsync(cts.Token); renderer.Apply(battle.PlayerActionVisual); battle.Pause(BattlePauseReason.User); Tick(battle, 5); renderer.Apply(battle.PlayerActionVisual); Assert.AreSame(pose, body.sprite);
                cts.Cancel(); Assert.IsFalse(use.GetAwaiter().GetResult()); renderer.Apply(battle.PlayerActionVisual); Assert.AreSame(idle, body.sprite); Assert.AreEqual(initiallyEnabled, animator.enabled); Assert.IsTrue(battle.IsPaused); Assert.IsFalse(renderer.EquipmentRenderer.gameObject.activeSelf);
            }
        }
        [Test]
        public void 未登録の次アクションを前の絵で代用せず明示設定の失敗は現設定を壊さない()
        {
            var idle = body.sprite; var pose = Sprite("ready"); var binding = new ActionAppearance("sword", null, new[] { pose, pose }, new[] { Vector2.zero, Vector2.zero }); renderer.Configure(new[] { binding });
            var battle = Battle(Action("sword", null), Action("unknown", null)); var use = battle.CardUsage.TryUseCurrentCardAsync(); renderer.Apply(battle.PlayerActionVisual);
            Assert.Throws<ArgumentException>(() => renderer.Configure(new[] { binding, binding })); Assert.IsTrue(renderer.IsActive); Assert.AreSame(pose, body.sprite);
            Tick(battle, 4); Assert.IsFalse(renderer.Apply(battle.PlayerActionVisual)); Assert.AreSame(idle, body.sprite); StringAssert.Contains("unknown", renderer.MissingBinding); Tick(battle, 4); Assert.IsTrue(use.GetAwaiter().GetResult());
        }
        [Test]
        public void 素材配列をコピーして参照するだけで外部Spriteを破棄せず範囲外コマを設定時に拒否する()
        {
            var pose = Sprite("ready"); var other = Sprite("other"); var input = new[] { pose, pose }; var binding = new ActionAppearance("sword", null, input, new[] { Vector2.zero, Vector2.zero }); input[0] = other;
            Assert.AreSame(pose, binding.Body[0]); Assert.Throws<ArgumentException>(() => binding.Validate(Action("sword", null, 3).Visual)); renderer.Configure(new[] { binding }); renderer.Dispose(); Assert.IsTrue(pose != null); Assert.IsTrue(other != null);
        }

        [TestCase(2)][TestCase(4)]
        public void ダッシュ中の追加描画は論理位置とカメラの相対位置を逆方向へ動かさない(int drawsPerFrame)
        {
            var fieldRoot = new GameObject("dash-camera-fixture");
            var cameraRoot = new GameObject("dash-camera");
            var actor = new GameObject("dash-actor");
            try
            {
                var view = fieldRoot.AddComponent<GameFieldView>();
                var camera = cameraRoot.AddComponent<Camera>();
                camera.orthographic = false;
                var cells = new List<MapCell>(); for (int i = 0; i < 100; i++) cells.Add(new MapCell(i));
                var dungeon = new Olca.Core.Logic.Dungeon.DungeonSystem(
                    new DungeonConfig("dash-camera", "ダッシュ", 100, cells), new PlayerCharacter(100), new Deck(),
                    new Olca.Tests.Mocks.MockRandomProvider(), new Olca.Tests.Mocks.MockLogger());
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                typeof(GameFieldView).GetField("fieldCamera", flags).SetValue(view, camera);
                typeof(GameFieldView).GetField("playerTransform", flags).SetValue(view, actor.transform);
                typeof(GameFieldView).GetField("dungeonSystem", flags).SetValue(view, dungeon);
                var position = typeof(GameFieldView).GetMethod("UpdatePlayerPosition", flags);
                var follow = typeof(GameFieldView).GetMethod("UpdateCamera", flags);
                position.Invoke(view, null); follow.Invoke(view, null);
                actor.transform.localScale = new Vector3(-.8f, .8f, 1);
                var scale = actor.transform.localScale; var rotation = actor.transform.rotation;
                dungeon.StartDash();
                for (int frame = 0; frame < 36; frame++)
                {
                    dungeon.TickFrame(); position.Invoke(view, null); follow.Invoke(view, null);
                    var actorPosition = actor.transform.position;
                    var cameraPosition = camera.transform.position;
                    float relativeX = actorPosition.x - cameraPosition.x;
                    for (int draw = 1; draw < drawsPerFrame; draw++)
                    {
                        position.Invoke(view, null); follow.Invoke(view, null);
                        Assert.AreEqual(actorPosition, actor.transform.position);
                        Assert.AreEqual(cameraPosition, camera.transform.position, "論理更新なしにカメラだけ進めない");
                        Assert.AreEqual(relativeX, actor.transform.position.x - camera.transform.position.x);
                    }
                }
                Assert.Greater(dungeon.PlayerPosition, 0f, "論理移動を止めて表示を安定させない");
                Assert.Greater(camera.transform.position.x, 2.5f, "カメラを固定するだけの対策にしない");
                Assert.Less(camera.transform.position.x, actor.transform.position.x + 2.5f,
                    "ダッシュ中に前へ出る既存のカメラ余白を保持する");
                Assert.AreEqual(scale, actor.transform.localScale); Assert.AreEqual(rotation, actor.transform.rotation);
                dungeon.StopDash(); follow.Invoke(view, null);
                Assert.AreEqual(actor.transform.position.x + 2.5f, camera.transform.position.x, .00001f,
                    "通常移動の既存直接追従へ戻る");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fieldRoot);
                UnityEngine.Object.DestroyImmediate(cameraRoot);
                UnityEngine.Object.DestroyImmediate(actor);
            }
        }
    }
}
