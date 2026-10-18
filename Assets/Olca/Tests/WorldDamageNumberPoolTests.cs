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

        private static async Task Completed(Task task)
        {
            Assert.AreSame(task, await Task.WhenAny(task, Task.Delay(3000)), "表示の終了待ちが完了しない");
            await task;
        }
    }
}
