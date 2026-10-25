using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Olca.Presentation.Effects;
using UnityEngine;
using UnityEngine.TestTools;

namespace Olca.Tests
{
    public class WorldDamageNumberPoolTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject root;
        private GameObject source;
        private GameObject parent;
        private WorldDamageNumberPool pool;
        private HashSet<WorldDamageNumberView> Active => (HashSet<WorldDamageNumberView>)Field("activeInstances").GetValue(pool);
        private Queue<WorldDamageNumberView> Waiting => (Queue<WorldDamageNumberView>)Field("pool").GetValue(pool);
        private static FieldInfo Field(string name) => typeof(WorldDamageNumberPool).GetField(name, Private);
        private object Invoke(string name, params object[] args) => typeof(WorldDamageNumberPool).GetMethod(name, Private).Invoke(pool, args);
        private WorldDamageNumberView Get() => (WorldDamageNumberView)Invoke("Get");
        private void Return(WorldDamageNumberView view) => Invoke("Return", view);
        private Task Show(int damage, CancellationToken ct) => ((UniTask)Invoke("ShowDamageAsync", damage, Vector3.zero, ct)).AsTask();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("PoolTest");
            root.SetActive(false);
            parent = new GameObject("ExternalPoolParent");
            source = new GameObject("DamagePrefab");
            var text = source.AddComponent<TextMesh>();
            var view = source.AddComponent<WorldDamageNumberView>();
            typeof(WorldDamageNumberView).GetField("textMesh", Private).SetValue(view, text);
            typeof(WorldDamageNumberView).GetField("duration", Private).SetValue(view, 60f);
            typeof(WorldDamageNumberView).GetMethod("Awake", Private).Invoke(view, null);
            pool = root.AddComponent<WorldDamageNumberPool>();
            Field("prefab").SetValue(pool, view);
            Field("poolParent").SetValue(pool, parent.transform);
            Invoke("Awake");
            root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (pool != null) Invoke("OnDestroy");
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(parent);
            UnityEngine.Object.DestroyImmediate(source);
        }

        [Test]
        public void Get_初期容量を超えて上限まで全て別実体を貸し出す()
        {
            var views = Enumerable.Range(0, 20).Select(_ => Get()).ToArray();
            Assert.AreEqual(20, views.Distinct().Count());
            Assert.AreEqual(20, Active.Count);
            Assert.AreEqual(0, Waiting.Count);
            LogAssert.Expect(LogType.Warning, "[WorldDamageNumberPool] 最大プールサイズ(20)に達しています。");
            Assert.IsNull(Get());
            Return(views[3]);
            Assert.AreSame(views[3], Get());
            Assert.AreEqual(20, Active.Count);
        }

        [Test]
        public void Return_二重返却を待機キューに重複登録しない()
        {
            var views = Enumerable.Range(0, 10).Select(_ => Get()).ToArray();
            Return(views[0]);
            Return(views[0]);
            Assert.AreEqual(1, Waiting.Count);
            Assert.AreSame(views[0], Get());
            Assert.AreNotSame(views[0], Get());
        }

        [Test]
        public void Get_破棄済みの待機Viewを読み飛ばす()
        {
            var destroyed = Waiting.Peek();
            UnityEngine.Object.DestroyImmediate(destroyed.gameObject);
            var next = Get();
            Assert.IsTrue(next != null);
            Assert.IsFalse(ReferenceEquals(destroyed, next));
        }

        [Test]
        public void Return_破棄済みの使用中Viewを待機キューへ戻さない()
        {
            var view = Get();
            UnityEngine.Object.DestroyImmediate(view.gameObject);
            Return(view);
            Assert.AreEqual(0, Active.Count);
            Assert.AreEqual(9, Waiting.Count);
        }

        [Test]
        public async Task ShowDamage_敵とプレイヤーの同時表示を上書きせず別々に返却する()
        {
            using var enemy = new CancellationTokenSource();
            using var player = new CancellationTokenSource();
            var enemyTasks = Enumerable.Range(1, 6).Select(i => Show(i, enemy.Token)).ToArray();
            var playerTasks = Enumerable.Range(101, 6).Select(i => Show(i, player.Token)).ToArray();
            Assert.AreEqual(12, Active.Count);
            CollectionAssert.AreEquivalent(Enumerable.Range(1, 6).Concat(Enumerable.Range(101, 6)).Select(i => i.ToString()),
                Active.Select(v => v.GetComponent<TextMesh>().text));
            enemy.Cancel();
            await Completed(Task.WhenAll(enemyTasks));
            Assert.AreEqual(6, Active.Count);
            CollectionAssert.AreEquivalent(Enumerable.Range(101, 6).Select(i => i.ToString()), Active.Select(v => v.GetComponent<TextMesh>().text));
            player.Cancel();
            await Completed(Task.WhenAll(playerTasks));
            Assert.AreEqual(0, Active.Count);
            Assert.AreEqual(12, Waiting.Count);
            Assert.AreEqual(12, Waiting.Distinct().Count());
        }

        [Test]
        public async Task ClearAll_表示完了後に一度だけ返却し新しい表示を消さない()
        {
            var old = Enumerable.Range(1, 12).Select(i => Show(i, CancellationToken.None)).ToArray();
            pool.ClearAll();
            pool.ClearAll();
            using var nextCts = new CancellationTokenSource();
            var next = Show(99, nextCts.Token);
            var nextView = Active.Single(v => v != null && v.GetComponent<TextMesh>().text == "99");
            await Completed(Task.WhenAll(old));
            Assert.IsTrue(Active.Contains(nextView));
            Assert.AreEqual("99", nextView.GetComponent<TextMesh>().text);
            Assert.AreEqual(1, Active.Count);
            Assert.AreEqual(Waiting.Count, Waiting.Distinct().Count());
            nextCts.Cancel();
            await Completed(next);
            Assert.AreEqual(0, Active.Count);
        }

        [Test]
        public async Task OnDestroy_外部親のViewも破棄し表示中の処理を終了する()
        {
            var pending = Show(10, CancellationToken.None);
            // 通常のMonoBehaviourのライフサイクルはEditModeでは自動実行されない。
            Invoke("OnDestroy");
            UnityEngine.Object.DestroyImmediate(root);
            await Completed(pending);
            Assert.AreEqual(0, parent.transform.childCount);
        }

        [Test]
        public async Task 所有者の遮蔽は敵数値だけを隠し解除で古い数値を再生しない()
        {
            object enemyOwner=new object();using var cts=new CancellationTokenSource();
            Task Owned(int damage)=>((UniTask)Invoke("ShowOwnedDamageAsync",damage,Vector3.zero,cts.Token,enemyOwner)).AsTask();
            var enemy=Owned(10);var player=Show(100,cts.Token);var enemyView=Active.Single(v=>v.GetComponent<TextMesh>().text=="10");var playerView=Active.Single(v=>v.GetComponent<TextMesh>().text=="100");pool.SetOwnerVisible(enemyOwner,false);Assert.IsFalse(enemyView.gameObject.activeSelf);Assert.IsTrue(playerView.gameObject.activeSelf);var rejected=Owned(20);Assert.IsTrue(rejected.IsCompleted);Assert.AreEqual(2,Active.Count);pool.SetOwnerVisible(enemyOwner,true);Assert.IsFalse(enemyView.gameObject.activeSelf);var next=Owned(30);Assert.IsTrue(Active.Single(v=>v.GetComponent<TextMesh>().text=="30").gameObject.activeSelf);Assert.IsTrue(playerView.gameObject.activeSelf);cts.Cancel();await Completed(Task.WhenAll(enemy,player,rejected,next));Assert.AreEqual(0,Active.Count);Assert.AreEqual(Waiting.Count,Waiting.Distinct().Count());
        }

        [Test]
        public async Task 個別表示は二十ヒットを超えても欠落せず返却後の保持数を制限する()
        {
            using var cts = new CancellationTokenSource();
            var owner = new object();
            var tasks = Enumerable.Range(1, 25).Select(i => ((UniTask)Invoke("ShowFeedbackAsync",
                new Olca.Core.Domain.CombatFeedback(Olca.Core.Domain.CombatFeedbackKind.Damage, i),
                Vector3.zero, cts.Token, owner)).AsTask()).ToArray();
            Assert.AreEqual(25, Active.Count);
            Assert.AreEqual(25, Active.Select(v => v.transform.position.y).Distinct().Count());
            CollectionAssert.AreEquivalent(Enumerable.Range(1, 25).Select(i => i.ToString()), Active.Select(v => v.GetComponent<TextMesh>().text));
            cts.Cancel(); await Completed(Task.WhenAll(tasks));
            Assert.AreEqual(0, Active.Count); Assert.AreEqual(20, Waiting.Count);
            Assert.AreEqual(20, Waiting.Distinct().Count());
        }

        [Test]
        public async Task 回復と完全防御の表示はカナと正符号を使い既存数値と共存する()
        {
            using var cts = new CancellationTokenSource();
            var values = new[] {
                new Olca.Core.Domain.CombatFeedback(Olca.Core.Domain.CombatFeedbackKind.Healing, 7),
                new Olca.Core.Domain.CombatFeedback(Olca.Core.Domain.CombatFeedbackKind.Immune),
                new Olca.Core.Domain.CombatFeedback(Olca.Core.Domain.CombatFeedbackKind.Guard),
                new Olca.Core.Domain.CombatFeedback(Olca.Core.Domain.CombatFeedbackKind.BarrierDamage, 12) };
            var tasks = values.Select(v => ((UniTask)Invoke("ShowFeedbackAsync", v, Vector3.zero, cts.Token, new object())).AsTask()).ToArray();
            CollectionAssert.AreEquivalent(new[] { "+7", "ムコウ", "ガード", "12" }, Active.Select(v => v.GetComponent<TextMesh>().text));
            cts.Cancel(); await Completed(Task.WhenAll(tasks)); Assert.AreEqual(0, Active.Count);
        }

        [Test]
        public async Task 敵Viewは個別ヒットを購読し接触演出後や毎フレーム更新で二重表示しない()
        {
            var go = new GameObject("FeedbackEnemy");
            var view = go.AddComponent<Olca.Presentation.Field.WorldEnemyView>();
            var header = GameObject.CreatePrimitive(PrimitiveType.Quad);
            header.transform.SetParent(go.transform, false);
            header.transform.localPosition = new Vector3(0, 5, 0);
            var enemy = new Olca.Core.Domain.Enemy("feedback", "テスト", 100, Olca.Core.Domain.ElementType.None, new Olca.Core.Domain.Vector2Int(3, 0));
            try {
                view.SetDamageNumberPool(pool); view.Initialize(enemy, 0);
                enemy.TakeDamage(3, Olca.Core.Domain.ElementType.None);
                enemy.TakeDamage(4, Olca.Core.Domain.ElementType.None);
                Assert.AreEqual(0, Active.Count, "接触ダメージは演出開始まで保留");
                view.OnBattleStarted(null); view.TriggerPendingDamageEffect(); view.TriggerPendingDamageEffect();
                view.Refresh(); Assert.AreEqual(2, Active.Count);
                Assert.IsTrue(Active.All(v => v.transform.position.y > header.GetComponent<Renderer>().bounds.max.y), "名前/HPの表示範囲より上に出す");
                enemy.TakeDamage(5, Olca.Core.Domain.ElementType.None); view.Refresh();
                Assert.AreEqual(3, Active.Count);
                view.SetHiddenByBlindness(true); enemy.TakeDamage(6, Olca.Core.Domain.ElementType.None);
                Assert.AreEqual(3, Active.Count, "遮蔽中は数字を追加しない");
                view.SetHiddenByBlindness(false);
                view.Initialize(new Olca.Core.Domain.Enemy("next", "テスト", 100, Olca.Core.Domain.ElementType.None, new Olca.Core.Domain.Vector2Int(3, 0)), 0);
                enemy.TakeDamage(7, Olca.Core.Domain.ElementType.None);
                Assert.AreEqual(3, Active.Count, "古い敵の購読を解除する");
            } finally {
                UnityEngine.Object.DestroyImmediate(go); pool.ClearAll();
            }
            await Task.Delay(100); Assert.AreEqual(0, Active.Count);
        }

        [Test]
        public async Task 撃破演出中は敵表示を保持し再初期化で旧演出が新しい敵を消さない()
        {
            var go=new GameObject("DeathView");
            var body=go.AddComponent<SpriteRenderer>();var visual=go.AddComponent<Olca.Presentation.Battle.EnemyView>();
            typeof(Olca.Presentation.Battle.EnemyView).GetField("enemyImage",Private).SetValue(visual,body);
            var view=go.AddComponent<Olca.Presentation.Field.WorldEnemyView>();
            typeof(Olca.Presentation.Field.WorldEnemyView).GetField("enemyView",Private).SetValue(view,visual);
            typeof(Olca.Presentation.Field.WorldEnemyView).GetField("deathFadeDuration",Private).SetValue(view,60f);
            try {
                var enemy=new Olca.Core.Domain.Enemy("dead","テスト",10,Olca.Core.Domain.ElementType.None,new Olca.Core.Domain.Vector2Int(3,0));
                view.Initialize(enemy,0);enemy.TakeDamage(10,Olca.Core.Domain.ElementType.None);visual.Refresh();
                Assert.IsTrue(go.activeSelf,"HP0でも演出完了まで表示を残す");
                var pending=view.WaitForDeathPresentationAsync(false,CancellationToken.None).AsTask();Assert.IsFalse(pending.IsCompleted);
                var next=new Olca.Core.Domain.Enemy("next","テスト",10,Olca.Core.Domain.ElementType.None,new Olca.Core.Domain.Vector2Int(3,0));
                view.Initialize(next,0);await Completed(pending);
                Assert.IsTrue(go.activeSelf);Assert.AreSame(next,view.TrackedEnemy);Assert.AreEqual(1f,body.color.a);
            }finally{UnityEngine.Object.DestroyImmediate(go);}
        }

        [Test]
        public async Task 通常の消滅演出は戦闘停止に従い終端演出は別時計で完了する()
        {
            var go=new GameObject("DeathClock");var owner=new object();
            var body=go.AddComponent<SpriteRenderer>();var visual=go.AddComponent<Olca.Presentation.Battle.EnemyView>();
            typeof(Olca.Presentation.Battle.EnemyView).GetField("enemyImage",Private).SetValue(visual,body);
            var view=go.AddComponent<Olca.Presentation.Field.WorldEnemyView>();
            typeof(Olca.Presentation.Field.WorldEnemyView).GetField("enemyView",Private).SetValue(view,visual);
            typeof(Olca.Presentation.Field.WorldEnemyView).GetField("deathFadeDuration",Private).SetValue(view,.05f);
            try {
                view.Initialize(new Olca.Core.Domain.Enemy("dead","テスト",10,Olca.Core.Domain.ElementType.None,new Olca.Core.Domain.Vector2Int(3,0)),0);
                Olca.Unity.BattleVisualClock.SetPaused(owner,true);
                view.TrackedEnemy.TakeDamage(10,Olca.Core.Domain.ElementType.None);
                await Task.Delay(100);Assert.AreEqual(1f,body.color.a);Assert.IsTrue(go.activeSelf);
                await Completed(view.WaitForDeathPresentationAsync(true,CancellationToken.None).AsTask());
                Assert.IsFalse(go.activeSelf);Assert.IsTrue(view.IsDeathPresentationComplete);
            } finally {Olca.Unity.BattleVisualClock.SetPaused(owner,false);UnityEngine.Object.DestroyImmediate(go);}
        }

        private static async Task Completed(Task task)
        {
            Assert.AreSame(task, await Task.WhenAny(task, Task.Delay(3000)), "表示の終了待ちが完了しない");
            await task;
        }
    }
}

namespace Olca.Tests
{
    public class EnemyGaugeBindingTests
    {
        private readonly List<GameObject> objects=new List<GameObject>();
        private Olca.Presentation.Battle.EnemyView View(Olca.Core.Domain.Enemy enemy)
        {
            var obj=new GameObject("GaugeFixture");objects.Add(obj);
            var label=new GameObject("Hp",typeof(TextMesh));label.transform.SetParent(obj.transform,false);
            label.GetComponent<TextMesh>().font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var view=obj.AddComponent<Olca.Presentation.Battle.EnemyView>();
            typeof(Olca.Presentation.Battle.EnemyView).GetField("hpText",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,label.GetComponent<TextMesh>());
            view.Initialize(enemy);return view;
        }
        private static Olca.Core.Domain.Enemy Enemy(string id="grunt",bool poise=true)=>new Olca.Core.Domain.Enemy(id,"テスト",100,Olca.Core.Domain.ElementType.None,new Olca.Core.Domain.Vector2Int(3,0),poiseDefinition:poise?new Olca.Core.Domain.EnemyPoiseDefinition(20,3):null);
        private static LineRenderer Line(Olca.Presentation.Battle.EnemyView view,string name)=>view.transform.Find(name).GetComponent<LineRenderer>();
        [TearDown] public void Cleanup(){foreach(var obj in objects)if(obj!=null)UnityEngine.Object.DestroyImmediate(obj);objects.Clear();}
        [Test] public void 通常敵の被弾回復と強靭度成立リセットを別々の残量へ反映する()
        {
            var enemy=Enemy();var view=View(enemy);Assert.IsFalse(view.GaugeVisible);view.ShowHp();
            enemy.TakeDamage(25,Olca.Core.Domain.ElementType.None,poiseDamage:5);view.Refresh();
            Assert.AreEqual(.75f,view.HpFraction);Assert.AreEqual(.75f,view.PoiseFraction);Assert.IsTrue(Line(view,"EnemyHpFill").enabled);
            enemy.Heal(10);view.Refresh();Assert.AreEqual(.85f,view.HpFraction);Assert.AreEqual(.75f,view.PoiseFraction);
            enemy.TakeDamage(1,Olca.Core.Domain.ElementType.None,poiseDamage:15);view.Refresh();Assert.IsTrue(enemy.IsStaggered);Assert.AreEqual(0,view.PoiseFraction);Assert.IsFalse(Line(view,"EnemyPoiseFill").enabled);
            for(int n=0;n<3;n++)enemy.TickStunFrame();view.Refresh();Assert.AreEqual(1,view.PoiseFraction);Assert.IsTrue(Line(view,"EnemyPoiseFill").enabled);
            Assert.AreEqual(84,enemy.CurrentHp);Assert.AreEqual(0,enemy.AccumulatedPoiseDamage);
        }
        [Test] public void 強靭度未定義と無敵で無効になった被弾を仮の残量にしない()
        {
            var enemy=Enemy(poise:false);var view=View(enemy);view.ShowHp();Assert.IsNull(view.PoiseFraction);Assert.IsFalse(Line(view,"EnemyPoiseTrack").enabled);
            enemy.SetInvincible(1000);enemy.TakeDamage(50,Olca.Core.Domain.ElementType.None);view.Refresh();Assert.AreEqual(1,view.HpFraction);Assert.AreEqual(100,enemy.CurrentHp);
            var other=View(Enemy());other.ShowHp();Assert.AreEqual(1,other.PoiseFraction);Assert.IsFalse(Line(view,"EnemyPoiseTrack").enabled);
        }
        [Test] public void 終了と死亡でゲージを隠し再表示と別モデル初期化で古い値を残さない()
        {
            var enemy=Enemy();var view=View(enemy);view.ShowHp();view.HideHp();Assert.IsFalse(Line(view,"EnemyHpTrack").enabled);view.ShowHp();Assert.IsTrue(Line(view,"EnemyHpTrack").enabled);
            enemy.TakeDamage(1000,Olca.Core.Domain.ElementType.None);view.Refresh();Assert.IsFalse(view.GaugeVisible);Assert.IsFalse(Line(view,"EnemyHpFill").enabled);
            view.gameObject.SetActive(true);view.Initialize(Enemy());Assert.IsFalse(view.GaugeVisible);view.ShowHp();Assert.AreEqual(1,view.HpFraction);Assert.AreEqual(4,view.GetComponentsInChildren<LineRenderer>(true).Length);
        }
        [TestCase(false)] [TestCase(true)] public void 参加していない敵はグリッド内でもゲージと本体を隠し終了後に探索へ戻す(bool participates)
        {
            var enemy=Enemy("boss");var view=View(enemy);var world=view.gameObject.AddComponent<Olca.Presentation.Field.WorldEnemyView>();
            typeof(Olca.Presentation.Field.WorldEnemyView).GetField("enemyView",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(world,view);
            world.Initialize(enemy,0);world.OnBattleStarted(null,participates);
            Assert.AreEqual(participates,view.gameObject.activeSelf);Assert.AreEqual(participates,view.GaugeVisible);
            world.OnBattleEnded();Assert.IsTrue(view.gameObject.activeSelf);Assert.IsFalse(view.GaugeVisible);
        }
        [Test] public void 同じマスの通常敵は本体を動かさずゲージだけ分けて個体別の値を読める()
        {
            var a=View(Enemy());var b=View(Enemy());a.ShowHp();b.ShowHp();a.Refresh();b.Refresh();
            var first=a.transform.Find("Hp").position;var second=b.transform.Find("Hp").position;
            Assert.Greater(first.y,second.y);Assert.AreEqual(Vector3.zero,a.transform.position);Assert.AreEqual(Vector3.zero,b.transform.position);
            b.Initialize(Enemy());b.ShowHp();b.Refresh();Assert.Less(Vector3.Distance(second,b.transform.Find("Hp").position),.0001f);
            a.HideHp();b.Refresh();Assert.Less(Vector3.Distance(first,b.transform.Find("Hp").position),.0001f);
        }
        [Test] public void 通常敵の強靭度はHP円の右外側だけに沿う()
        {
            var view=View(Enemy());view.ShowHp();var hp=Line(view,"EnemyHpTrack");var poise=Line(view,"EnemyPoiseTrack");
            var center=view.transform.Find("Hp").position;
            for(int n=0;n<poise.positionCount;n++)Assert.GreaterOrEqual(poise.GetPosition(n).x,center.x-.0001f);
            Assert.Greater(Vector3.Distance(poise.GetPosition(0),center),Vector3.Distance(hp.GetPosition(0),center));
        }
        [Test] public void 複数ボスの縦ゲージは右端で分離し敵移動に追従せず終了時に枠を解放する()
        {
            if(Camera.main==null){var obj=new GameObject("GaugeCamera",typeof(Camera));objects.Add(obj);obj.tag="MainCamera";obj.transform.position=new Vector3(0,0,-10);}
            var a=View(Enemy("boss"));var b=View(Enemy("boss"));a.ShowHp();b.ShowHp();
            Assert.IsTrue(a.IsBossGauge);Assert.IsTrue(b.IsBossGauge);Assert.IsTrue(a.IsFixedHudRenderer(Line(a,"EnemyHpTrack")));Assert.IsTrue(a.IsFixedHudRenderer(a.transform.Find("Hp").GetComponent<Renderer>()));var first=Line(a,"EnemyHpTrack").GetPosition(0);var second=Line(b,"EnemyHpTrack").GetPosition(0);
            Assert.Greater(first.x,second.x);a.transform.position+=Vector3.right*4;a.Refresh();Assert.Less(Vector3.Distance(first,Line(a,"EnemyHpTrack").GetPosition(0)),.0001f);
            a.HideHp();b.Refresh();Assert.Less(Vector3.Distance(first,Line(b,"EnemyHpTrack").GetPosition(0)),.0001f);
        }
    }
}
