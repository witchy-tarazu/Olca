using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Olca.Application.Management;
using Olca.Presentation.Management;
using Olca.Unity;

namespace Olca.Tests.Application
{
    public class GameSystemManagementTests
    {
        private GameObject host, events;
        private ResourceManager resources;
        private DialogManager dialogs;
        private ViewManager views;
        private SoundManager sounds;
        private GameSystem system;
        private int frame;
        private readonly object owner = new object();
        private readonly List<(EventSystem System, bool Enabled)> priorEvents = new List<(EventSystem, bool)>();
        private EventSystem priorCurrent;
        private Action viewCleanup;
        [SetUp] public void Setup()
        {
            priorCurrent = EventSystem.current;
            foreach (var value in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { priorEvents.Add((value,value.enabled)); value.enabled=false; }
            events = new GameObject("ManagedTestsEventSystem",typeof(EventSystem));
            var eventSystem=events.GetComponent<EventSystem>();
            // EditModeではMonoBehaviourのOnEnableが自動実行されないため、既存テストと同じ登録経路を通す。
            typeof(EventSystem).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(eventSystem,null);
            EventSystem.current=eventSystem;
            host = new GameObject("ManagedTests",typeof(RectTransform));
            resources = new ResourceManager(); dialogs = new DialogManager(()=>frame); views = new ViewManager(resources,dialogs); sounds = new SoundManager(host.transform);
            system = new GameSystem(new SaveManager(),resources,sounds,views,dialogs);
        }
        [TearDown] public void Cleanup()
        {
            viewCleanup?.Invoke();viewCleanup=null;
            system?.Dispose(); UnityEngine.Object.DestroyImmediate(host);
            if(events!=null)typeof(EventSystem).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(events.GetComponent<EventSystem>(),null);
            UnityEngine.Object.DestroyImmediate(events);
            foreach(var item in priorEvents)if(item.System!=null)item.System.enabled=item.Enabled;
            priorEvents.Clear(); if(priorCurrent!=null)EventSystem.current=priorCurrent;
        }
        private GameObject Panel(string name) { var value=new GameObject(name,typeof(RectTransform));value.transform.SetParent(host.transform,false);value.SetActive(false);return value; }
        private void Register(string key,Func<CancellationToken,Task<GameObject>> factory,Action<GameObject,ManagedScreenContext> show=null)
        { views.Register(owner,key,factory,show??((go,context)=>go.SetActive(true)),go=>{if(go!=null)go.SetActive(false);}); }
        private Button Button(GameObject parent,string name) { var value=new GameObject(name,typeof(RectTransform),typeof(Button));value.transform.SetParent(parent.transform,false);return value.GetComponent<Button>(); }
        private async Task<(ManagedScreenContext Context,GameObject Panel,Button Confirm,Button Cancel)> Screen()
        {
            ManagedScreenContext context=null;var screen=Panel("screen");var popup=Panel("popup");popup.transform.SetParent(screen.transform,false);
            var confirm=Button(popup,"confirm");var cancel=Button(popup,"cancel");
            Register("screen",ct=>Task.FromResult(screen),(go,ctx)=>{context=ctx;go.SetActive(true);});
            Assert.IsTrue((await views.ShowAsync("screen")).Succeeded);return(context,popup,confirm,cancel);
        }
        [Test] public async Task 同じ画面の連打は一度だけ生成し表示を共有する()
        {
            var pending=new TaskCompletionSource<GameObject>();int created=0,shown=0;
            Register("pending",ct=>{created++;return pending.Task;},(go,ctx)=>{shown++;go.SetActive(true);});
            var first=views.ShowAsync("pending");var second=views.ShowAsync("pending");Assert.AreEqual(1,created);
            pending.SetResult(Panel("loaded"));Assert.IsTrue((await first).Succeeded);Assert.IsTrue((await second).Succeeded);Assert.AreEqual(1,shown);
            views.CloseCurrent();Assert.IsTrue((await views.ShowAsync("pending")).Succeeded);Assert.AreEqual(1,created);
            views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.AreEqual(0,views.RegisteredCount);
        }
        [Test] public async Task 取消後に届いた生成物を表示せず破棄し旧画面を保つ()
        {
            var old=Panel("old");Register("old",ct=>Task.FromResult(old));await views.ShowAsync("old");
            var pending=new TaskCompletionSource<GameObject>();Register("pending",ct=>pending.Task);
            using(var cancel=new CancellationTokenSource()) {
                var request=views.ShowAsync("pending",cancel.Token);cancel.Cancel();var late=Panel("late");pending.SetResult(late);
                Assert.IsTrue((await request).Cancelled);Assert.IsTrue(late==null);Assert.AreEqual("old",views.CurrentKey);Assert.IsTrue(old.activeSelf);
            }
        }
        [Test] public async Task 表示中画面への再要求は別画面の後着ロードを取り消す()
        {
            var current=Panel("current");Register("current",ct=>Task.FromResult(current));await views.ShowAsync("current");
            var firstLoad=new TaskCompletionSource<GameObject>();var nextLoad=new TaskCompletionSource<GameObject>();int loads=0;
            Register("slow",ct=>++loads==1?firstLoad.Task:nextLoad.Task);var slow=views.ShowAsync("slow");
            Assert.IsTrue((await views.ShowAsync("current")).Succeeded);Assert.AreEqual("current",views.CurrentKey);Assert.IsTrue(current.activeSelf);
            var reopened=views.ShowAsync("slow");Assert.AreEqual(2,loads);
            var late=Panel("late");firstLoad.SetResult(late);Assert.IsTrue((await slow).Cancelled);Assert.IsTrue(late==null);
            nextLoad.SetResult(Panel("latest"));Assert.IsTrue((await reopened).Succeeded);Assert.AreEqual("slow",views.CurrentKey);
        }
        [Test] public async Task 生成失敗では元画面と開いていたDialogを維持する()
        {
            var screen=await Screen();var popup=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>{});
            Register("fail",ct=>Task.FromException<GameObject>(new InvalidOperationException("fixture load failure")));
            var failed=await views.ShowAsync("fail");Assert.IsFalse(failed.Succeeded);StringAssert.Contains("fixture",failed.Error);
            Assert.AreEqual("screen",views.CurrentKey);Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsFalse(popup.IsCompleted);
            views.CloseCurrent();Assert.AreEqual(DialogResult.ParentClosed,await popup);
        }
        [Test] public async Task 別画面要求が先行ロードを無効にして遅延結果を解放する()
        {
            var pending=new TaskCompletionSource<GameObject>();Register("slow",ct=>pending.Task);
            var slow=views.ShowAsync("slow");Register("fast",ct=>Task.FromResult(Panel("fast")));Assert.IsTrue((await views.ShowAsync("fast")).Succeeded);
            var late=Panel("late");pending.SetResult(late);Assert.IsTrue((await slow).Cancelled);Assert.IsTrue(late==null);Assert.AreEqual("fast",views.CurrentKey);
        }
        [Test] public async Task Dialog連打を共有し終了前に状態を確定して旧クリックを拒否する()
        {
            var screen=await Screen();int callbacks=0;
            var first=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>{callbacks++;Assert.AreEqual(0,dialogs.ActiveCount);});
            Assert.AreSame(first,dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>callbacks++));
            var ticket=typeof(DialogManager).GetField("current",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(dialogs);
            var oldClick=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("ConfirmAction").GetValue(ticket);
            screen.Cancel.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await first);Assert.AreEqual(1,callbacks);
            var next=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>callbacks++);oldClick();Assert.IsFalse(next.IsCompleted);
            screen.Confirm.onClick.Invoke();Assert.AreEqual(DialogResult.Confirmed,await next);Assert.AreEqual(2,callbacks);
        }
        [Test] public async Task 親画面を閉じるとDialogは親終了になり結果処理も保存も実行しない()
        {
            var screen=await Screen();int effects=0;var result=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>effects++);
            views.CloseCurrent();Assert.AreEqual(DialogResult.ParentClosed,await result);Assert.AreEqual(0,effects);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(screen.Panel.activeSelf);
            Assert.AreEqual(DialogResult.ParentClosed,await screen.Context.ShowDialog(screen.Panel,screen.Confirm,screen.Cancel,_=>effects++));
        }
        [Test] public async Task Dialogを閉じた同frameは親入力を止め次frameで解放する()
        {
            var screen=await Screen();frame=10;var result=dialogs.Show(screen.Context,screen.Panel,screen.Confirm,screen.Cancel,_=>{});screen.Cancel.onClick.Invoke();await result;
            Assert.IsTrue(screen.Context.DialogBlocksInput);frame++;dialogs.Tick();Assert.IsFalse(screen.Context.DialogBlocksInput);
        }
        [Test] public async Task 設定の初期化Dialogを取消すと起点ボタンへ戻る()
        {
            var panel=Panel("keyboard");var view=panel.AddComponent<Olca.Presentation.Field.GameplayKeyboardSettingsView>();
            // EditModeで手動更新したViewの入力購読も明示解放する。
            viewCleanup=()=>{if(view!=null)view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);};
            view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),profile=>null);
            Register("keyboard",ct=>Task.FromResult(panel),(go,context)=>{view.ConfigureManagement(context);view.Open(UnityInputProvider.CreateDefaultKeyboardProfile());});
            await views.ShowAsync("keyboard");var update=view.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);update.Invoke(view,null);
            Assert.AreEqual("CloseKeyboardSettings",EventSystem.current.currentSelectedGameObject.name);
            var buttons=panel.GetComponentsInChildren<Button>(true);Array.Find(buttons,x=>x.name=="ResetKeyboardSettings").onClick.Invoke();update.Invoke(view,null);
            Array.Find(buttons,x=>x.name=="CancelKeyboardReset").onClick.Invoke();frame++;dialogs.Tick();update.Invoke(view,null);
            Assert.AreEqual(0,dialogs.ActiveCount);Assert.AreEqual("ResetKeyboardSettings",EventSystem.current.currentSelectedGameObject.name);
        }
        [Test] public async Task 破棄中のロードは後着しても表示せず二重解放しない()
        {
            var pending=new TaskCompletionSource<GameObject>();Register("late",ct=>pending.Task);var request=views.ShowAsync("late");system.Dispose();
            var late=Panel("late");pending.SetResult(late);Assert.IsTrue((await request).Cancelled);Assert.IsTrue(late==null);Assert.AreEqual(0,resources.OwnedInstanceCount);system.Dispose();
        }
        [Test] public void 保存先確定だけではファイルを書かず攻略所有を重複させない()
        {
            string path=Path.Combine(Path.GetTempPath(),"managed-"+Guid.NewGuid().ToString("N"));
            var session=system.BeginSession(owner,path+"/settings",path+"/checkpoints",path+"/progress");Assert.IsFalse(Directory.Exists(path));
            Assert.AreSame(session,system.BeginSession(owner,path+"/settings",path+"/checkpoints",path+"/progress"));
            Assert.Throws<InvalidOperationException>(()=>system.BeginSession(new object(),path,path,path));session.Dispose();Assert.IsNull(system.CurrentSession);session.Dispose();
        }
        [Test] public void 音声は画面毎に生成せず所有者を一つに保ち終了時に停止する()
        {
            var route=sounds.BeginSession(owner,new AudioVolumeSettings());Assert.AreSame(route,sounds.BeginSession(owner,new AudioVolumeSettings()));
            Assert.AreEqual(3,host.GetComponentsInChildren<AudioSource>().Length);
            Assert.Throws<InvalidOperationException>(()=>sounds.BeginSession(new object(),new AudioVolumeSettings()));
            sounds.EndSession(owner);Assert.IsNull(route.Bgm.clip);sounds.Dispose();sounds.EndSession(owner);Assert.AreEqual(0,host.GetComponentsInChildren<AudioSource>().Length);
        }
        [Test] public void 初期化途中の失敗で攻略所有を戻し再試行と二重開始を区別する()
        {
            string path = Path.Combine(Path.GetTempPath(), "managed-start-" + Guid.NewGuid().ToString("N"));
            var go = new GameObject("controller");var controller = go.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            try {
                controller.ConfigureGameSystem(system);controller.ConfigureGameplaySettingsDirectory(path+"/settings");controller.ConfigureCheckpointDirectory(path+"/checkpoints");controller.ConfigureProgressDirectory(path+"/progress");
                controller.ConfigureDungeon(Olca.Core.Domain.DungeonConfig.CreateTestDungeon(6,1,2));
                string key=(string)typeof(Olca.Application.Dungeon.DungeonBattleController).GetField("ManagedKeyboardScreen",BindingFlags.NonPublic|BindingFlags.Static).GetRawConstantValue();
                views.Register(controller,key,ct=>Task.FromResult(Panel("duplicate")),(panel,context)=>{},panel=>{});
                var start=typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("Start",flags);
                var failed=Assert.Throws<TargetInvocationException>(()=>start.Invoke(controller,null));StringAssert.Contains("画面登録",failed.InnerException.Message);
                Assert.IsNull(system.CurrentSession);Assert.AreEqual(0,views.RegisteredCount);
                start.Invoke(controller,null);var session=system.CurrentSession;Assert.IsNotNull(session);Assert.IsNotNull(session.Input);Assert.IsNotNull(session.Dungeon);
                Assert.IsFalse(sounds.Router.IsPaused);
                controller.enabled=false;typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("OnDisable",flags).Invoke(controller,null);Assert.IsTrue(sounds.Router.IsPaused);
                controller.enabled=true;typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("OnEnable",flags).Invoke(controller,null);Assert.IsFalse(sounds.Router.IsPaused);
                Assert.AreEqual(1,views.RegisteredCount);start.Invoke(controller,null);Assert.AreSame(session,system.CurrentSession);Assert.AreEqual(1,views.RegisteredCount);
            } finally {
                var canvas=(GameObject)typeof(Olca.Application.Dungeon.DungeonBattleController).GetField("pauseCanvas",flags).GetValue(controller);if(canvas!=null)UnityEngine.Object.DestroyImmediate(canvas);
                // 反射StartのEditMode fixtureでは破棄通知も明示し、実行中購読を残さない。
                typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("OnDestroy",flags).Invoke(controller,null);
                UnityEngine.Object.DestroyImmediate(go);if(Directory.Exists(path))Directory.Delete(path,true);
            }
            Assert.IsNull(system.CurrentSession);Assert.AreEqual(0,views.RegisteredCount);
        }
        [Test] public void 初期化済みと未完成Rootの再取得を分けて重複サービスを作らない()
        {
            var type=typeof(GameSystemRoot);var field=type.GetField("instance",BindingFlags.NonPublic|BindingFlags.Static);var prior=field.GetValue(null);
            var go=new GameObject("root-retry");var root=go.AddComponent<GameSystemRoot>();
            try {
                field.SetValue(null,root);Assert.AreSame(root,GameSystemRoot.Ensure());var first=root.Services;Assert.IsNotNull(first);
                Assert.AreSame(first,GameSystemRoot.Ensure().Services);Assert.AreEqual(3,go.GetComponentsInChildren<AudioSource>().Length);
                first.Dispose();type.GetField("<Services>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(root,null);
                var repaired=GameSystemRoot.Ensure().Services;Assert.IsNotNull(repaired);Assert.AreNotSame(first,repaired);Assert.AreEqual(3,go.GetComponentsInChildren<AudioSource>().Length);
            } finally { root.Services?.Dispose();field.SetValue(null,prior);UnityEngine.Object.DestroyImmediate(go); }
        }

    }
}
