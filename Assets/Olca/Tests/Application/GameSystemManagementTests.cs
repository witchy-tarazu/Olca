using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        [TestCase(false)] [TestCase(true)] public async Task 設定の初期化Dialogを取消すと起点ボタンへ戻る(bool gamepad)
        {
            var panel=Panel("keyboard");var view=panel.AddComponent<Olca.Presentation.Field.GameplayKeyboardSettingsView>();
            // EditModeで手動更新したViewの入力購読も明示解放する。
            viewCleanup=()=>{if(view!=null)view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);};
            view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),profile=>null,gamepad);
            Register("keyboard",ct=>Task.FromResult(panel),(go,context)=>{view.ConfigureManagement(context);view.Open(gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile());});
            await views.ShowAsync("keyboard");var update=view.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);update.Invoke(view,null);
            Assert.AreEqual("CloseKeyboardSettings",EventSystem.current.currentSelectedGameObject.name);
            var buttons=panel.GetComponentsInChildren<Button>(true);Array.Find(buttons,x=>x.name=="ResetKeyboardSettings").onClick.Invoke();update.Invoke(view,null);
            panel.GetComponentsInChildren<Button>().Single(x=>x.name=="CancelKeyboardReset").onClick.Invoke();frame++;dialogs.Tick();update.Invoke(view,null);
            Assert.AreEqual(0,dialogs.ActiveCount);Assert.AreEqual("ResetKeyboardSettings",EventSystem.current.currentSelectedGameObject.name);
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public async Task 割当初期化は共通確認を必要時生成し候補だけ変更して旧世代と閉鎖後保存を拒否する(bool managed,bool gamepad)
        {
            var panel=Panel("reset-shared");var view=panel.AddComponent<Olca.Presentation.Field.GameplayKeyboardSettingsView>();
            viewCleanup=()=>{if(view!=null)view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);};
            var defaults=gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile();string device=gamepad?"gamepad":"keyboard";
            var slot=(device,"battle","AttackPrimary");var changed=defaults.BeginEdit();Assert.IsTrue(changed.TryAssign(slot,defaults.Bindings[(device,"battle","CardPrimary")],out var error),error);var profile=changed.Preview();int saves=0;
            view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),candidate=>{saves++;return "fixture save failure";},gamepad);
            if(managed){Register("reset-shared",ct=>Task.FromResult(panel),(go,context)=>{view.ConfigureManagement(context);view.Open(profile);});await views.ShowAsync("reset-shared");}else view.Open(profile);
            var update=view.GetType().GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);update.Invoke(view,null);
            Button Find(string name)=>panel.GetComponentsInChildren<Button>().Single(x=>x.name==name);string Draft()=>Find("Bind_battle_AttackPrimary").GetComponentInChildren<Text>().text;
            string original=Draft();var save=Find("SaveKeyboardSettings").onClick;var close=Find("CloseKeyboardSettings").onClick;Assert.IsNull(panel.GetComponentInChildren<ConfirmationDialog>());
            Find("ResetKeyboardSettings").Select();Find("ResetKeyboardSettings").onClick.Invoke();var first=panel.GetComponentInChildren<ConfirmationDialog>();Assert.IsNotNull(first);var oldConfirm=first.ConfirmButton.onClick;var oldCancel=first.CancelButton.onClick;save.Invoke();close.Invoke();Assert.AreEqual(0,saves);Assert.IsTrue(first.IsOpen);Assert.IsTrue(panel.activeSelf);
            first.CancelButton.onClick.Invoke();frame++;dialogs.Tick();update.Invoke(view,null);Assert.AreEqual(original,Draft());Assert.AreEqual("ResetKeyboardSettings",EventSystem.current.currentSelectedGameObject.name);
            Find("ResetKeyboardSettings").onClick.Invoke();var second=panel.GetComponentInChildren<ConfirmationDialog>();Assert.IsNotNull(second);Assert.AreNotSame(first,second);oldConfirm.Invoke();oldCancel.Invoke();Assert.IsTrue(second.IsOpen);Assert.AreEqual(original,Draft());
            second.ConfirmButton.onClick.Invoke();frame++;dialogs.Tick();update.Invoke(view,null);Assert.AreEqual(0,saves);Assert.AreNotEqual(original,Draft());Assert.AreSame(profile,view.GetType().GetField("applied",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view));
            save.Invoke();Assert.AreEqual(1,saves);StringAssert.Contains("保存できませんでした",panel.GetComponentsInChildren<Text>().Single(x=>x.name=="Status").text);
            view.DiscardAndClose();view.GetType().GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);save.Invoke();oldConfirm.Invoke();Assert.AreEqual(1,saves);Assert.AreEqual(0,dialogs.ActiveCount);
        }
        [Test] public async Task 音量画面は閉鎖通知し旧クリックを拒否して再表示できる()
        {
            var panel=Panel("audio");var view=panel.AddComponent<Olca.Presentation.Field.GameplayAudioSettingsView>();
            int saved=0,closed=0;
            viewCleanup=()=>{if(view!=null)view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);};
            view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),value=>{saved++;return null;},()=>{closed++;view.DiscardAndClose();});
            Register("audio",ct=>Task.FromResult(panel),(go,context)=>{view.ConfigureManagement(context);view.Open(new AudioVolumeSettings(),null);});
            await views.ShowAsync("audio");
            var buttons=panel.GetComponentsInChildren<Button>(true);var save=Array.Find(buttons,x=>x.name=="SaveAudioSettings");var cancel=Array.Find(buttons,x=>x.name=="CancelAudioSettings");
            save.onClick.Invoke();Assert.AreEqual(1,saved);
            views.CloseCurrent();save.onClick.Invoke();cancel.onClick.Invoke();Assert.AreEqual(1,saved);Assert.AreEqual(0,closed);
            await views.ShowAsync("audio");save.onClick.Invoke();Assert.AreEqual(2,saved);
            cancel.onClick.Invoke();Assert.AreEqual(1,closed);
            // EditModeの手動View操作ではMonoBehaviourの非表示通知も明示する。
            view.GetType().GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Assert.IsNull(views.CurrentKey);
            save.onClick.Invoke();Assert.AreEqual(2,saved);
            var oldSave=save.onClick;var oldCancel=cancel.onClick;var oldSlider=panel.GetComponentInChildren<Slider>(true).onValueChanged;
            view.GetType().GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);viewCleanup=null;
            views.UnregisterOwner(owner);oldSave.Invoke();oldCancel.Invoke();oldSlider.Invoke(50);Assert.AreEqual(2,saved);Assert.AreEqual(1,closed);
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
                Assert.AreEqual(4,views.RegisteredCount);start.Invoke(controller,null);Assert.AreSame(session,system.CurrentSession);Assert.AreEqual(4,views.RegisteredCount);
                var entries=(System.Collections.IDictionary)typeof(ViewManager).GetField("entries",flags).GetValue(views);
                CollectionAssert.AreEquivalent(new[]{"keyboard-settings","gamepad-settings","audio-settings","outside-item-registration"},entries.Keys);
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

        [Test] public async Task Pause画面は閉じた世代のクリックを再表示後も実行しない()
        {
            int calls=0;bool allowed=true;var panel=Panel("pause");var resume=Button(panel,"Resume");resume.onClick.AddListener(()=>calls++);
            var pause=panel.AddComponent<GameplayPauseView>();pause.Initialize(()=>allowed);var layer=views.GetLayer("pause-test");
            layer.Register(owner,"pause",ct=>Task.FromResult(panel),(go,context)=>pause.Show(context),go=>pause.Hide());
            Assert.IsTrue((await layer.ShowAsync("pause")).Succeeded);var old=resume.onClick;old.Invoke();Assert.AreEqual(1,calls);
            allowed=false;old.Invoke();Assert.AreEqual(1,calls);allowed=true;layer.CloseCurrent();old.Invoke();Assert.AreEqual(1,calls);
            Assert.IsTrue((await layer.ShowAsync("pause")).Succeeded);old.Invoke();Assert.AreEqual(1,calls);resume.onClick.Invoke();Assert.AreEqual(2,calls);
            views.UnregisterOwner(owner);old.Invoke();Assert.AreEqual(2,calls);Assert.AreEqual(0,resources.OwnedInstanceCount);
        }
        [Test] public async Task Pauseを覆う確認は取消で戻り親終了では旧確定を実行しない()
        {
            int confirms=0;var panel=Panel("pause");var source=Button(panel,"Retreat");var pause=panel.AddComponent<GameplayPauseView>();pause.Initialize(()=>true);
            var popup=Panel("retreat");var confirm=Button(popup,"ConfirmRetreat");var cancel=Button(popup,"CancelRetreat");
            var layer=views.GetLayer("pause-dialog-test");layer.Register(owner,"pause",ct=>Task.FromResult(panel),(go,context)=>pause.Show(context),go=>pause.Hide());
            await layer.ShowAsync("pause");var context=pause.Context;EventSystem.current.SetSelectedGameObject(source.gameObject);
            pause.CoverForDialog();var request=context.ShowDialog(popup,confirm,cancel,result=>{pause.RestoreAfterDialog();if(result==DialogResult.Confirmed)confirms++;});
            Assert.IsTrue(context.IsCurrent);Assert.IsTrue(panel.activeSelf);Assert.AreEqual(0,panel.GetComponent<CanvasGroup>().alpha);
            Assert.IsTrue((await layer.ShowAsync("pause")).Succeeded);Assert.AreEqual(1,dialogs.ActiveCount);
            cancel.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await request);
            Assert.IsTrue(panel.activeSelf);Assert.AreSame(source.gameObject,EventSystem.current.currentSelectedGameObject);
            Assert.AreEqual("Retreat",pause.TakeRestoredFocus());Assert.IsNull(pause.TakeRestoredFocus());
            frame++;pause.CoverForDialog();request=context.ShowDialog(popup,confirm,cancel,result=>{pause.RestoreAfterDialog();if(result==DialogResult.Confirmed)confirms++;});
            var stale=confirm.onClick;layer.CloseCurrent();Assert.AreEqual(DialogResult.ParentClosed,await request);stale.Invoke();Assert.AreEqual(0,confirms);Assert.IsFalse(panel.activeSelf);Assert.AreEqual(0,dialogs.ActiveCount);
            UnityEngine.Object.DestroyImmediate(popup);
        }
        [Test] public async Task Checkpoint案内は内容更新で世代を変えず再表示後の旧入力を拒否する()
        {
            int calls=0;bool allowed=true;var panel=Panel("checkpoint");var button=Button(panel,"Continue");button.onClick.AddListener(()=>calls++);
            var view=panel.AddComponent<CheckpointPromptView>();view.Initialize(()=>allowed);var layer=views.GetLayer("checkpoint-test");
            layer.Register(owner,"checkpoint",ct=>Task.FromResult(panel),(go,context)=>view.Show(context),go=>view.Hide());
            await layer.ShowAsync("checkpoint");var first=view.Context;var old=button.onClick;
            await layer.ShowAsync("checkpoint");Assert.AreSame(first,view.Context);old.Invoke();old.Invoke();Assert.AreEqual(1,calls);
            allowed=false;old.Invoke();Assert.AreEqual(1,calls);allowed=true;button.gameObject.SetActive(false);old.Invoke();Assert.AreEqual(1,calls);button.gameObject.SetActive(true);
            layer.CloseCurrent();await layer.ShowAsync("checkpoint");old.Invoke();Assert.AreEqual(1,calls);
            button.onClick.Invoke();Assert.AreEqual(2,calls);views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);
        }
        [Test] public async Task 保存再試行Dialogは失敗で閉じず無効取消と同frame再入を拒否する()
        {
            int attempts=0,completed=0;bool parent=true;var panel=Panel("retry");var confirm=Button(panel,"Retry");var cancel=Button(panel,"Close");cancel.interactable=false;
            using(var scope=dialogs.CreateOwner(()=>parent)){
                var task=dialogs.ShowPersistent(scope,panel,confirm,cancel,()=>{attempts++;confirm.onClick.Invoke();if(attempts==2){confirm.gameObject.SetActive(false);cancel.interactable=true;}return false;},_=>completed++);
                confirm.onClick.Invoke();confirm.onClick.Invoke();cancel.onClick.Invoke();Assert.AreEqual(1,attempts);Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsFalse(task.IsCompleted);
                frame++;confirm.onClick.Invoke();Assert.AreEqual(2,attempts);Assert.AreEqual(1,dialogs.ActiveCount);
                cancel.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await task);Assert.AreEqual(1,completed);
            }
        }
        [Test] public async Task 未移行親のDialog寿命も終了後と再表示後の旧確定を拒否する()
        {
            bool parent=true;int effects=0;var panel=Panel("progress");var confirm=Button(panel,"Confirm");var cancel=Button(panel,"Cancel");
            using(var scope=dialogs.CreateOwner(()=>parent)){
                var task=dialogs.ShowPersistent(scope,panel,confirm,cancel,()=>{effects++;return true;},_=>effects++);
                var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);
                var stale=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("ConfirmAction").GetValue(ticket);
                parent=false;dialogs.Tick();Assert.AreEqual(DialogResult.ParentClosed,await task);stale();Assert.AreEqual(0,effects);
                parent=true;frame++;var next=dialogs.ShowPersistent(scope,panel,confirm,cancel,()=>{effects++;return true;},_=>{});stale();Assert.AreEqual(0,effects);
                confirm.onClick.Invoke();Assert.AreEqual(DialogResult.Confirmed,await next);Assert.AreEqual(1,effects);
            }
        }
        [Test] public async Task Checkpointの進行確認取消は同frameの親クリック遮断を解除しない()
        {
            var parent=Panel("checkpoint-parent");var prompt=parent.AddComponent<CheckpointPromptView>();prompt.Initialize(()=>true);var layer=views.GetLayer("checkpoint");
            layer.Register(owner,"checkpoint",ct=>Task.FromResult(parent),(go,context)=>prompt.Show(context),go=>prompt.Hide());await layer.ShowAsync("checkpoint");
            var popup=Panel("progress-popup");var dialog=popup.AddComponent<ProgressActionDialog>();dialog.Initialize(new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{}));var cancel=dialog.CancelButton;
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
            Set("gameSystem",system);Set("managedCheckpointView",prompt);Set("progressActionDialog",dialog);
            type.GetMethod("ShowProgressAction",flags).Invoke(controller,new object[]{"fixture",false,true});Assert.AreEqual(1,dialogs.ActiveCount);
            cancel.onClick.Invoke();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(dialog.IsOpen);Assert.IsTrue(prompt.Context.DialogBlocksInput);
            frame++;dialogs.Tick();Assert.IsFalse(prompt.Context.DialogBlocksInput);
            // 未起動fixtureに所有サービスを残してOnDestroyへ流さない。
            Set("gameSystem",null);UnityEngine.Object.DestroyImmediate(controller);
        }
        [Test] public void 未移行親の進行Dialogもhost終了時点でUnity破棄より先に解除する()
        {
            var parent=Panel("hub-parent");parent.SetActive(true);var hub=parent.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();
            var popup=Panel("progress-popup");var dialog=popup.AddComponent<ProgressActionDialog>();dialog.Initialize(new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{}));var cancel=dialog.CancelButton;
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
            Set("gameSystem",system);Set("clearReturnView",hub);Set("progressActionDialog",dialog);
            type.GetMethod("ShowProgressAction",flags).Invoke(controller,new object[]{"fixture",false,true});Assert.AreEqual(1,dialogs.ActiveCount);
            var scope=dialog.Owner;Assert.IsTrue(scope.IsCurrent);
            controller.StopManagedHost();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(scope.IsCurrent);Assert.IsFalse(popup.activeSelf);
            // TickやUnity側のDestroyを待つ前に終了境界が成立している。
            UnityEngine.Object.DestroyImmediate(controller);
        }
        [Test] public void host終了後の旧手動保存イベントはUnity破棄前でも保存と世代を変更しない()
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-stopped-progress-"+Guid.NewGuid().ToString("N"));
            try{
                var store=new ClearReturnFileStore(directory);var progress=new Olca.Core.Domain.ClearReturnSession(store);
                Assert.IsTrue(progress.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()),progress.Error);
                var before=File.ReadAllBytes(store.ActivePath);long revision=progress.Snapshot.Revision;
                var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();
                void Set(string name,object value)=>type.GetField(name,flags).SetValue(controller,value);
                Set("gameSystem",system);Set("clearReturnSession",progress);Set("initialHub",true);Set("clearReturnVisible",true);
                var button=Button(host,"ManualSaveButton");button.onClick.AddListener((UnityEngine.Events.UnityAction)Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction),controller,type.GetMethod("SaveProgressFromUi",flags)));var old=button.onClick;
                controller.StopManagedHost();Assert.DoesNotThrow(()=>old.Invoke());CollectionAssert.AreEqual(before,File.ReadAllBytes(store.ActivePath));Assert.AreEqual(revision,progress.Snapshot.Revision);Assert.IsFalse(progress.HasPendingSave);
                UnityEngine.Object.DestroyImmediate(controller);
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [Test] public async Task 読取専用Dialogは閉鎖世代と親終了を守りスクロール所有を返す()
        {
            var screen=await Screen();var scroll=screen.Panel.AddComponent<ScrollRect>();int completed=0;
            var request=dialogs.ShowNotice(screen.Context,screen.Panel,screen.Cancel,scroll,_=>completed++);
            Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsTrue(screen.Context.DialogBlocksInput);
            var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);
            var stale=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("CancelAction").GetValue(ticket);
            screen.Cancel.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await request);Assert.AreEqual(1,completed);
            Assert.IsTrue(screen.Context.DialogBlocksInput);frame++;dialogs.Tick();Assert.IsFalse(screen.Context.DialogBlocksInput);
            var next=dialogs.ShowNotice(screen.Context,screen.Panel,screen.Cancel,scroll,_=>completed++);stale();Assert.IsFalse(next.IsCompleted);
            views.CloseCurrent();Assert.AreEqual(DialogResult.ParentClosed,await next);Assert.AreEqual(1,completed);Assert.AreEqual(0,dialogs.ActiveCount);
            stale();Assert.AreEqual(1,completed);
        }
        [Test] public async Task 帰還共有画面は世代入力と詳細Popupを管理し別層図鑑を独立閉鎖する()
        {
            var panel=Panel("return-hub");var view=panel.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();
            int actions=0;view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{},()=>actions++);
            var layer=views.GetLayer("return-test");layer.Register(owner,"return",ct=>Task.FromResult(panel),(go,ctx)=>view.ShowManaged(ctx),go=>view.HideManaged());
            view.ConfigureManagement(()=>{var result=layer.ShowAsync("return");Assert.IsTrue(result.IsCompleted&&result.Result.Succeeded);},layer.CloseCurrent,dialogs);
            void Render()=>view.Render(new Olca.Core.Domain.ClearReturnProfile(),new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null);
            Render();var first=view.ManagementContext;Assert.IsTrue(first.IsCurrent);
            var other=views.GetLayer("collection-test");var collection=Panel("collection");other.Register(owner,"collection",ct=>Task.FromResult(collection),(go,ctx)=>go.SetActive(true),go=>go.SetActive(false));
            await other.ShowAsync("collection");Assert.IsTrue(first.IsCurrent);other.CloseCurrent();Assert.IsTrue(first.IsCurrent);
            var method=view.GetType().GetMethod("OpenReadonlyText",BindingFlags.Instance|BindingFlags.NonPublic);
            var parameters=method.GetParameters();Assert.AreEqual(4,parameters.Length);
            method.Invoke(view,new object[]{"詳細","表示内容",null,null});Assert.AreEqual(1,dialogs.ActiveCount);
            layer.CloseCurrent();Assert.IsFalse(first.IsCurrent);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(panel.activeSelf);
            Render();Assert.IsTrue(view.ManagementContext.IsCurrent);Assert.AreNotSame(first,view.ManagementContext);
            views.UnregisterOwner(owner);Assert.AreEqual(0,layer.RegisteredCount);Assert.AreEqual(0,dialogs.ActiveCount);
        }
        private Olca.Presentation.Dungeon.ClearReturnView ReturnView()
        {
            var panel=Panel("return15");var view=panel.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});
            var layer=views.GetLayer("return15");layer.Register(owner,"return15",ct=>Task.FromResult(panel),(go,ctx)=>view.ShowManaged(ctx),go=>view.HideManaged());
            view.ConfigureManagement(()=>{var request=layer.ShowAsync("return15");Assert.IsTrue(request.IsCompleted&&request.Result.Succeeded);},layer.CloseCurrent,dialogs);return view;
        }
        private UnityEngine.Events.UnityAction CurrentConfirm()
        {
            var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);
            return (UnityEngine.Events.UnityAction)ticket.GetType().GetField("ConfirmAction").GetValue(ticket);
        }
        [Test] public void 編成破棄Dialogは親を保持し取消再描画後のframe遮断と旧確定を守る()
        {
            var view=ReturnView();int resumes=0,discards=0;var profile=new Olca.Core.Domain.ClearReturnProfile();var draft=new List<Olca.Core.Domain.Card>();
            void Render(bool discard=false)=>view.RenderOwnedDeparture(profile,new Olca.Core.Domain.Card[0],draft,null,false,(_,__)=>{},()=>{},_=>discards++,()=>{resumes++;Render();},discard);
            Render();var parent=view.transform.Find("DepartureContents").gameObject;Render(true);var old=CurrentConfirm();Render(true);
            Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsFalse(parent.activeSelf);Assert.AreSame(parent,view.transform.Find("DepartureContents").gameObject);
            view.GetComponentsInChildren<Button>().Single(x=>x.name=="KeepEditingDeparture").onClick.Invoke();
            Assert.AreEqual(1,resumes);Assert.AreEqual(0,discards);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsTrue(view.ManagementContext.DialogBlocksInput);
            Assert.AreEqual("CloseOwnedDeparture",EventSystem.current.currentSelectedGameObject.name);old();Assert.AreEqual(0,discards);
            frame++;dialogs.Tick();Render(true);var confirm=CurrentConfirm();confirm();confirm();Assert.AreEqual(1,discards);
        }
        [Test] public void 保存デッキ削除Dialogは取消focusと左右選択を保ち旧確定を再使用しない()
        {
            var view=ReturnView();int deletes=0;var profile=new Olca.Core.Domain.ClearReturnProfile{SavedDecks=new[]{new Olca.Core.Domain.SavedDepartureDeck{Id="isolated",Name=new string('長',300),Cards=new Olca.Core.Domain.SavedDeckCard[0]}}};
            void Render(string deleting=null)=>view.RenderSavedDeckMenu(profile,null,false,()=>{},_=>{},_=>{},_=>{},id=>Render(id),()=>{},()=>{},deleting,()=>{deletes++;Render();});
            Render();Render("isolated");var old=CurrentConfirm();var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);
            Assert.IsFalse((bool)ticket.GetType().GetField("DirectionScroll").GetValue(ticket));Assert.NotNull(ticket.GetType().GetField("Scroll").GetValue(ticket));
            view.GetComponentsInChildren<Button>().Single(x=>x.name=="CancelDeleteDeck").onClick.Invoke();Assert.AreEqual(0,deletes);Assert.AreEqual("DeleteDeck_isolated",EventSystem.current.currentSelectedGameObject.name);Assert.IsTrue(view.ManagementContext.DialogBlocksInput);
            old();Assert.AreEqual(0,deletes);frame++;dialogs.Tick();Render("isolated");var next=CurrentConfirm();old();Assert.AreEqual(1,dialogs.ActiveCount);next();next();Assert.AreEqual(1,deletes);Assert.AreEqual(0,dialogs.ActiveCount);
        }
        [Test] public void 親終了で編成削除Dialogの業務callbackと後着確定を破棄する()
        {
            var view=ReturnView();int effects=0;var profile=new Olca.Core.Domain.ClearReturnProfile();
            view.RenderOwnedDeparture(profile,new Olca.Core.Domain.Card[0],new List<Olca.Core.Domain.Card>(),null,false,(_,__)=>{},()=>{},_=>effects++,()=>effects++,true);
            var old=CurrentConfirm();views.GetLayer("return15").CloseCurrent();Assert.AreEqual(0,dialogs.ActiveCount);old();Assert.AreEqual(0,effects);Assert.IsFalse(view.gameObject.activeSelf);Assert.IsNull(view.ManagementContext);
        }
        [Test] public void Dialog取消後の再描画で閉鎖frameを消さず新拠点の保存入口も遮断する()
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-stage15-frame-"+Guid.NewGuid().ToString("N"));
            try{
                var store=new ClearReturnFileStore(directory);var progress=new Olca.Core.Domain.ClearReturnSession(store);Assert.IsTrue(progress.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()));
                var view=ReturnView();view.Render(progress.Snapshot,new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null);
                var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();
                void Set(string n,object v)=>type.GetField(n,flags).SetValue(controller,v);
                Set("gameSystem",system);Set("clearReturnSession",progress);Set("initialHub",true);Set("clearReturnVisible",true);Set("clearReturnView",view);
                Assert.IsTrue((bool)type.GetProperty("IsProgressHub",flags).GetValue(controller),"保存入口自体が有効な拠点で遮断を検証する");
                var popup=Panel("frame-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");dialogs.Show(view.ManagementContext,popup,yes,no,_=>view.Render(progress.Snapshot,new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null));
                var bytes=File.ReadAllBytes(store.ActivePath);no.onClick.Invoke();Assert.IsTrue(view.ManagementContext.DialogBlocksInput);
                Assert.DoesNotThrow(()=>type.GetMethod("SaveProgressFromUi",flags).Invoke(controller,null));CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));
                UnityEngine.Object.DestroyImmediate(controller);
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)]
        public void 装備三画面は管理世代とDialog入力を守り明示終端で参照を返す(int kind)
        {
            var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var panel=Panel("equipment");var layer=views.GetLayer("equipment16");Component view;Action open,hide,release;Action<ManagedScreenContext> show;Action<Action,Action> configure;Func<ManagedScreenContext> context;int cancels=0;
            var catalog=new Olca.Core.Logic.Customization.CustomizationCatalog("fixture16",new Olca.Core.Logic.Customization.PartsBoardDefinition(2,1,new[]{new Olca.Core.Domain.Vector2Int(0,0)}),Array.Empty<string>(),new Dictionary<string,Olca.Core.Logic.Customization.PartShape>(),null);
            var progress=new Olca.Core.Logic.Customization.CustomizationProgress();
            if(kind==0){var v=panel.AddComponent<Olca.Presentation.Dungeon.CustomizationLoadoutView>();v.Initialize(font,()=>{},()=>{cancels++;v.Close();},()=>{});view=v;show=v.ShowManaged;hide=v.HideManaged;release=v.ReleaseManagement;configure=v.ConfigureManagement;context=()=>v.Context;open=()=>v.Open(new Olca.Core.Logic.Customization.CustomizationEditDraft(catalog,progress,0));}
            else if(kind==1){var v=panel.AddComponent<Olca.Presentation.Dungeon.CustomizationUnlockView>();v.Initialize(font,_=>{},()=>{cancels++;v.Close();},()=>{});view=v;show=v.ShowManaged;hide=v.HideManaged;release=v.ReleaseManagement;configure=v.ConfigureManagement;context=()=>v.Context;open=()=>v.Open(catalog,progress);}
            else{var v=panel.AddComponent<Olca.Presentation.Dungeon.CustomizationAcquisitionView>();v.Initialize(font,_=>{},()=>{cancels++;v.Close();},()=>{});view=v;show=v.ShowManaged;hide=v.HideManaged;release=v.ReleaseManagement;configure=v.ConfigureManagement;context=()=>v.Context;open=()=>v.Open(new Olca.Core.Logic.Customization.CustomizationAcquisitionPlan{ContextId="fixture16",BoardId=catalog.BoardId,MaximumClaims=0,Candidates=Array.Empty<Olca.Core.Logic.Customization.CustomizationAcquisitionCandidate>()},progress,false,catalog);}
            layer.Register(owner,"equipment",ct=>Task.FromResult(panel),(go,ctx)=>show(ctx),go=>hide());configure(()=>Assert.IsTrue(layer.ShowAsync("equipment").GetAwaiter().GetResult().Succeeded),layer.CloseCurrent);
            var origin=Button(host,"origin16");EventSystem.current.SetSelectedGameObject(origin.gameObject);view.GetType().GetMethod("PrepareManagedOpen").Invoke(view,null);Assert.IsTrue(context().IsCurrent);Assert.AreSame(origin.gameObject,EventSystem.current.currentSelectedGameObject);Assert.IsNull(view.GetType().GetField(kind==0?"draft":kind==1?"state":"plan",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view));
            open();var first=context();Assert.IsTrue(first.IsCurrent);var cancel=view.GetComponentsInChildren<Button>().Single(b=>b.name==(kind==0?"CancelCustomization":kind==1?"CancelCustomizationUnlock":"CancelCustomizationAcquisition"));var old=cancel.onClick;var cancelName=cancel.name;
            var popup=Panel("equipment-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");dialogs.Show(first,popup,yes,no,_=>{});old.Invoke();Assert.AreEqual(0,cancels);Assert.IsTrue(first.IsCurrent);
            layer.CloseCurrent();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(first.IsCurrent);Assert.IsNull(context());Assert.IsFalse(panel.activeSelf);old.Invoke();Assert.AreEqual(0,cancels);
            open();Assert.AreNotSame(first,context());old.Invoke();Assert.AreEqual(0,cancels);Assert.IsTrue(context().IsCurrent);
            var current=view.GetComponentsInChildren<Button>().Single(b=>b.name==cancelName).onClick;current.Invoke();Assert.AreEqual(1,cancels);Assert.IsNull(context());Assert.IsNull(EventSystem.current.currentSelectedGameObject);current.Invoke();Assert.AreEqual(1,cancels);
            open();release();Assert.IsNull(context());Assert.IsFalse(panel.activeSelf);Assert.IsNull(layer.CurrentKey);
            foreach(var name in new[]{"menu","ensureManagedVisible","hideManaged",kind==0?"draft":kind==1?"state":"plan"})Assert.IsNull(view.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view),name);
            layer.UnregisterOwner(owner);Assert.AreEqual(0,layer.RegisteredCount);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.DoesNotThrow(()=>old.Invoke());Assert.AreEqual(1,cancels);
        }
        [TestCase(false)][TestCase(true)]
        public void 帰還面の必要時生成後も装備入口は前面にあり確認中だけ隠れる(bool registered)
        {
            // Chrome表示のfixtureは読込済み拠点を使い、新規配布の契約を要求しない。
            var progress=new Olca.Core.Domain.ClearReturnSession(new Olca.Core.Logic.Dungeon.MemoryClearReturnStore(new Olca.Core.Domain.ClearReturnProfile()));
            Assert.IsTrue(progress.Ready);
            var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});var chrome=factory.Chrome();
            var view=ReturnView();view.transform.SetParent(host.transform,false);view.gameObject.SetActive(true);
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();void Set(string n,object v)=>type.GetField(n,flags).SetValue(controller,v);
            Set("clearReturnSession",progress);Set("initialHub",true);Set("clearReturnVisible",true);Set("clearReturnView",view);
            Set("manualSaveButton",chrome.Save);Set("newGameButton",chrome.NewGame);Set("customizationEditorButton",chrome.Loadout);Set("customizationUnlockButton",chrome.Unlock);Set("customizationAcquisitionButton",chrome.Acquisition);Set("customizationAcquisitionProvider",new EquipmentSourceProbe());
            if(registered)Set("customizationCatalog",new Olca.Core.Logic.Customization.CustomizationCatalog("chrome20",new Olca.Core.Logic.Customization.PartsBoardDefinition(1,1,new[]{new Olca.Core.Domain.Vector2Int(0,0)}),Array.Empty<string>(),new Dictionary<string,Olca.Core.Logic.Customization.PartShape>(),null));
            type.GetMethod("RefreshProgressActions",flags).Invoke(controller,null);
            foreach(var button in new[]{chrome.Loadout,chrome.Unlock,chrome.Acquisition}){Assert.IsTrue(button.gameObject.activeSelf);Assert.AreEqual(registered,button.interactable);Assert.Greater(button.transform.GetSiblingIndex(),view.transform.GetSiblingIndex());}
            var popup=Panel("progress-overlay");var dialog=popup.AddComponent<ProgressActionDialog>();dialog.Initialize(factory);Set("progressActionDialog",dialog);dialog.OpenNotice("fixture",false,true,null,null,()=>true);type.GetMethod("RefreshProgressActions",flags).Invoke(controller,null);
            foreach(var button in new[]{chrome.Loadout,chrome.Unlock,chrome.Acquisition})Assert.IsFalse(button.gameObject.activeSelf);
        }
        private sealed class EquipmentSourceProbe:Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider
        {
            public int Calls;
            public Olca.Core.Logic.Customization.CustomizationAcquisitionDefinition Resolve(Olca.Core.Logic.Customization.CustomizationAcquisitionContext context){Calls++;return null;}
        }
        [TestCase("OpenCustomizationEditor")][TestCase("OpenCustomizationUnlocks")][TestCase("OpenCustomizationAcquisitions")]
        public void 帰還確認を閉じた同frameの旧装備入口は草稿も取得計画も作らない(string method)
        {
            string directory=Path.Combine(Path.GetTempPath(),"olca-stage16-fence-"+Guid.NewGuid().ToString("N"));
            try{
                var store=new ClearReturnFileStore(directory);var progress=new Olca.Core.Domain.ClearReturnSession(store);Assert.IsTrue(progress.InitializeStarter(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards()));
                var view=ReturnView();view.Render(progress.Snapshot,new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null);var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();
                void Set(string n,object value)=>type.GetField(n,flags).SetValue(controller,value);
                var catalog=new Olca.Core.Logic.Customization.CustomizationCatalog("fence16",new Olca.Core.Logic.Customization.PartsBoardDefinition(1,1,new[]{new Olca.Core.Domain.Vector2Int(0,0)}),Array.Empty<string>(),new Dictionary<string,Olca.Core.Logic.Customization.PartShape>(),null);var source=new EquipmentSourceProbe();
                Set("gameSystem",system);Set("clearReturnSession",progress);Set("initialHub",true);Set("clearReturnVisible",true);Set("clearReturnView",view);Set("customizationCatalog",catalog);Set("customizationAcquisitionProvider",source);
                var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var load=Panel("load").AddComponent<Olca.Presentation.Dungeon.CustomizationLoadoutView>();load.Initialize(font,()=>{},()=>{},()=>{});var unlock=Panel("unlock").AddComponent<Olca.Presentation.Dungeon.CustomizationUnlockView>();unlock.Initialize(font,_=>{},()=>{},()=>{});var acquire=Panel("acquire").AddComponent<Olca.Presentation.Dungeon.CustomizationAcquisitionView>();acquire.Initialize(font,_=>{},()=>{},()=>{});Set("customizationEditorView",load);Set("customizationUnlockView",unlock);Set("customizationAcquisitionView",acquire);
                Assert.IsTrue((bool)type.GetProperty("IsProgressHub",flags).GetValue(controller));Assert.IsTrue(progress.Ready);Assert.IsFalse(progress.HasPendingSave);
                var popup=Panel("fence16-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");dialogs.Show(view.ManagementContext,popup,yes,no,_=>view.Render(progress.Snapshot,new Olca.Core.Domain.ClearReturnData{Stage=Olca.Core.Domain.ClearReturnStage.Returned},false,null));var bytes=File.ReadAllBytes(store.ActivePath);no.onClick.Invoke();Assert.IsTrue(view.ManagementContext.DialogBlocksInput);
                Assert.DoesNotThrow(()=>type.GetMethod(method,flags).Invoke(controller,null));Assert.IsFalse(load.gameObject.activeSelf);Assert.IsFalse(unlock.gameObject.activeSelf);Assert.IsFalse(acquire.gameObject.activeSelf);Assert.IsNull(load.Draft);Assert.AreEqual(0,source.Calls);CollectionAssert.AreEqual(bytes,File.ReadAllBytes(store.ActivePath));
                UnityEngine.Object.DestroyImmediate(controller);
            }finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        }
        [TestCase(false)][TestCase(true)]
        public void 資源報酬画面は旧選択を次の待機へ渡さず親終了で取消す(bool item)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var pause=typeof(GameplayPause);var release=pause.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var frames=pause.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);bool wasPaused=GameplayPause.IsPaused;var wasRelease=release.GetValue(null);var wasFrames=frames.GetValue(null);
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var type=controller.GetType();
            void Set(string n,object v)=>type.GetField(n,flags).SetValue(controller,v);
            object Call(string n,params object[] args)=>type.GetMethod(n,flags).Invoke(controller,args);
            try{
                GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();Assert.IsFalse(GameplayPause.InputBlocked);
                var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});
                var binding=new HostPanelBindings(controller,host.transform,resources,views);Set("gameSystem",system);Set("panelFactory",factory);Set("hostPanels",binding);Call("RegisterResourceRewardViews");
                Assert.IsNull(type.GetField("currencyOverlay",flags).GetValue(controller));Assert.IsNull(type.GetField("itemRewardOverlay",flags).GetValue(controller));Assert.AreEqual(0,resources.OwnedInstanceCount);
                var root=binding.Show(item?"item":"currency");var yes=root.GetComponentsInChildren<Button>(true).Single(x=>x.name==(item?"ReceiveItemReward":"ReceiveCurrencyButton"));var no=root.GetComponentsInChildren<Button>(true).Single(x=>x.name==(item?"SkipItemReward":"SkipCurrencyButton"));
                var first=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",root,no.name,first,CancellationToken.None);var old=yes.onClick;var context=(ManagedScreenContext)type.GetField("resourceRewardContext",flags).GetValue(controller);Assert.IsTrue(context.IsCurrent);
                var popup=Panel("reward-popup");var confirm=Button(popup,"confirm");var cancel=Button(popup,"cancel");dialogs.Show(context,popup,confirm,cancel,_=>{});old.Invoke();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,first.Task.Status);
                views.GetLayer("resource-reward").CloseCurrent();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Canceled,first.Task.Status);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsNull(EventSystem.current.currentSelectedGameObject);
                frame++;var next=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",root,no.name,next,CancellationToken.None);old.Invoke();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,next.Task.Status);
                yes.onClick.Invoke();no.onClick.Invoke();Assert.IsTrue(next.Task.GetAwaiter().GetResult());Call("CloseResourceReward",root,next);Assert.IsFalse(root.activeSelf);Assert.IsNull(type.GetField("activeResourceRewardChoice",flags).GetValue(controller));
                frame++;var final=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",root,no.name,final,CancellationToken.None);Call("OnDisable");Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,final.Task.Status);Call("OnEnable");Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,final.Task.Status);views.GetLayer("resource-reward").CloseCurrent();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Canceled,final.Task.Status);Assert.IsNull(type.GetField("resourceRewardRoot",flags).GetValue(controller));
                var crossOld=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",root,no.name,crossOld,CancellationToken.None);var other=binding.Show(item?"currency":"item");var otherNo=other.GetComponentsInChildren<Button>(true).Single(x=>x.name==(item?"SkipCurrencyButton":"SkipItemReward"));var crossNext=new Cysharp.Threading.Tasks.UniTaskCompletionSource<bool>();Call("BindResourceReward",other,otherNo.name,crossNext,CancellationToken.None);Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Canceled,crossOld.Task.Status);Assert.IsTrue(((ManagedScreenContext)type.GetField("resourceRewardContext",flags).GetValue(controller)).IsCurrent);otherNo.onClick.Invoke();Assert.IsFalse(crossNext.Task.GetAwaiter().GetResult());Call("CloseResourceReward",other,crossNext);
                Call("ReleasePresentationBindings");Assert.IsNull(type.GetField("resourceRewardMenu",flags).GetValue(controller));Assert.IsNull(type.GetField("resourceRewardViews",flags).GetValue(controller));views.UnregisterOwner(controller);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }finally{UnityEngine.Object.DestroyImmediate(controller);GameplayPause.SetPaused(wasPaused);release.SetValue(null,wasRelease);frames.SetValue(null,wasFrames);}
        }
        [TestCase(false)][TestCase(true)]
        public void Custom詳細はDialog世代と親終了を守り閲覧で手札と選択を変更しない(bool parentClose)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var pause=typeof(GameplayPause);var release=pause.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var frames=pause.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);bool wasPaused=GameplayPause.IsPaused;var wasRelease=release.GetValue(null);var wasFrames=frames.GetValue(null);
            var binding=new BattlePresentationBinding(owner,resources,views,host.transform);var v=binding.Custom;var type=v.GetType();
            object Call(string n,params object[] args)=>type.GetMethod(n,flags).Invoke(v,args);
            try{
                GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();
                binding.Battle.gameObject.SetActive(true);v.gameObject.SetActive(true);v.ConfigureDetailsManagement(dialogs);
                var f=new Olca.Application.Battle.SimpleCardFactory();var model=new Olca.Core.Logic.Battle.CustomScreenSystem(new Olca.Core.Domain.Deck(Enumerable.Range(0,5).Select(_=>f.CreateById("sword")).ToList()));model.Open(Array.Empty<Olca.Core.Domain.Card>(),new Olca.Tests.Mocks.MockRandomProvider());v.Initialize(model);
                var hand=model.CurrentHand.ToArray();var cost=model.GetTotalUsedCost();var origin=Button(host,"detail-origin");EventSystem.current.SetSelectedGameObject(origin.gameObject);Action queued=null;v.ConfigureLogicalConfirmation((_,action)=>queued=action,()=>0L);Call("OnConfirmClicked");Assert.NotNull(queued);
                Call("ShowDetailsValue","セイノウ\n"+string.Concat(Enumerable.Repeat("ナガイ セツメイ\n",80))+"オワリ");Assert.AreEqual(1,dialogs.ActiveCount);var panel=v.transform.Find("CustomPadDetails").gameObject;var text=panel.GetComponentsInChildren<Text>().Single(x=>x.name=="CustomPadDetailsText");var scroll=panel.GetComponentInChildren<ScrollRect>();Assert.Greater(scroll.content.rect.height,scroll.viewport.rect.height);StringAssert.EndsWith("オワリ",text.text);
                var ticket=typeof(DialogManager).GetField("current",flags).GetValue(dialogs);var stale=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("CancelAction").GetValue(ticket);Assert.IsNull(ticket.GetType().GetField("Menu").GetValue(ticket),"Custom割当と標準menuの二重入力は作らない");Assert.DoesNotThrow(()=>dialogs.Tick(),"Custom固有入力のticketもRootの更新を通す");
                var materials=(List<Olca.Core.Domain.Card>)type.GetField("customizeSelectedCards",flags).GetValue(v);materials.Add(hand[0]);queued();v.OnCardClicked(hand[0]);Call("CancelSelection");Call("OnCustomizeModeCancelled");Assert.AreEqual(1,materials.Count);Call("OnConfirmClicked");Assert.IsFalse(model.IsConfirming);CollectionAssert.AreEqual(hand,model.CurrentHand);Assert.AreEqual(0,model.SelectedCards.Count);Assert.AreEqual(cost,model.GetTotalUsedCost());
                var imported=panel.GetComponent<Image>().sprite;Assert.NotNull(imported);
                // pause側の確認が同じDialogManagerを使えるよう、Customのモデルを維持して所有を返す。
                v.CloseDetailsForSystemPause();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(panel.activeSelf);Assert.IsTrue(model.IsOpen);Assert.IsFalse(model.IsConfirming);CollectionAssert.AreEqual(hand,model.CurrentHand);Assert.AreEqual(cost,model.GetTotalUsedCost());
                using(var pauseOwner=dialogs.CreateOwner(()=>true)){var pausePanel=Panel("pause-detail-handoff");var pauseClose=Button(pausePanel,"pause-close");var pauseRequest=dialogs.ShowNotice(pauseOwner,pausePanel,pauseClose,null,_=>{});Assert.AreEqual(1,dialogs.ActiveCount);stale();Assert.AreEqual(1,dialogs.ActiveCount);pauseClose.onClick.Invoke();Assert.IsTrue(pauseRequest.IsCompleted);}
                frame++;EventSystem.current.SetSelectedGameObject(origin.gameObject);Call("ShowDetailsValue","ポーズ カラ フッキ");Assert.AreEqual(1,dialogs.ActiveCount);ticket=typeof(DialogManager).GetField("current",flags).GetValue(dialogs);stale=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("CancelAction").GetValue(ticket);
                if(parentClose){v.ReleaseSessionPresentation();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsTrue(panel==null||!panel.activeSelf);Assert.IsNull(type.GetField("detailsOwner",flags).GetValue(v));stale();Assert.DoesNotThrow(()=>v.OnCardClicked(hand[0]));Assert.DoesNotThrow(()=>Call("OnConfirmClicked"));Assert.DoesNotThrow(()=>Call("OnCustomizeModeCancelled"));Assert.IsFalse(model.IsConfirming);}
                else{stale();Assert.AreEqual(0,dialogs.ActiveCount);Assert.AreSame(origin.gameObject,EventSystem.current.currentSelectedGameObject);queued();v.OnCardClicked(hand[0]);Call("OnConfirmClicked");Assert.IsFalse(model.IsConfirming);Assert.AreEqual(0,model.SelectedCards.Count);frame++;Call("ShowDetailsValue","ミジカイ セツメイ");Assert.AreEqual(1,dialogs.ActiveCount);stale();Assert.AreEqual(1,dialogs.ActiveCount);model.Close();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(panel.activeSelf);}
                CollectionAssert.AreEqual(hand,model.CurrentHand);Assert.AreEqual(cost,model.GetTotalUsedCost());binding.Dispose();Assert.IsTrue(imported!=null);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }finally{binding.Dispose();GameplayPause.SetPaused(wasPaused);release.SetValue(null,wasRelease);frames.SetValue(null,wasFrames);}
        }
        [TestCase(false)][TestCase(true)]
        public void Custom専用詳細は読取末尾と固有入力を持ち終端破棄後の旧操作を次世代へ渡さない(bool managed)
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;var pause=typeof(GameplayPause);var release=pause.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var frames=pause.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);bool paused=GameplayPause.IsPaused;var priorRelease=release.GetValue(null);var priorFrames=frames.GetValue(null);
            var binding=new BattlePresentationBinding(owner,resources,views,host.transform);var view=binding.Custom;
            try{
                GameplayPause.SetPaused(false);GameplayPause.UpdateInputBarrier(false);GameplayPause.AdvanceLogicalFrame();GameplayPause.AdvanceLogicalFrame();binding.Battle.gameObject.SetActive(true);view.gameObject.SetActive(true);view.ConfigureDetailsManagement(managed?dialogs:null);
                var factory=new Olca.Application.Battle.SimpleCardFactory();var model=new Olca.Core.Logic.Battle.CustomScreenSystem(new Olca.Core.Domain.Deck(Enumerable.Range(0,5).Select(_=>factory.CreateById("sword")).ToList()));model.Open(Array.Empty<Olca.Core.Domain.Card>(),new Olca.Tests.Mocks.MockRandomProvider());view.Initialize(model);var hand=model.CurrentHand.ToArray();int cost=model.GetTotalUsedCost();
                var show=typeof(Olca.Presentation.CustomScreen.CustomScreenView).GetMethod("ShowDetailsValue",flags);show.Invoke(view,new object[]{string.Concat(Enumerable.Repeat("ナガイ ブン\n",80))+"オワリ"});var first=view.GetComponentInChildren<Olca.Presentation.CustomScreen.CustomCardDetailsDialog>();Assert.NotNull(first);Assert.Greater(first.Scroll.content.rect.height,first.Scroll.viewport.rect.height);first.Scroll.verticalNormalizedPosition=0;Assert.AreEqual(0,first.Scroll.verticalNormalizedPosition,.001);StringAssert.EndsWith("オワリ",first.Body.text);
                first.HandlePad(CustomPadCommand.Up);Assert.AreEqual(.2f,first.Scroll.verticalNormalizedPosition,.001);first.HandlePad(CustomPadCommand.Down);Assert.AreEqual(0,first.Scroll.verticalNormalizedPosition,.001);var oldClose=first.GetComponentsInChildren<Button>().Single(x=>x.name=="CloseCustomPadDetails").onClick;var imported=first.GetComponent<Image>().sprite;
                // EditModeではMonoBehaviour終了を明示配送する。自然DestroyはPlay実入口で別途確認する。
                typeof(Olca.Presentation.CustomScreen.CustomCardDetailsDialog).GetMethod("OnDestroy",flags).Invoke(first,null);UnityEngine.Object.DestroyImmediate(first.gameObject);Assert.AreEqual(0,dialogs.ActiveCount);frame++;show.Invoke(view,new object[]{"ツギ ノ ショウサイ"});var second=view.GetComponentInChildren<Olca.Presentation.CustomScreen.CustomCardDetailsDialog>();Assert.NotNull(second);Assert.IsTrue(second.IsOpen);oldClose.Invoke();Assert.IsTrue(second.IsOpen);second.Dismiss();Assert.IsFalse(second.IsOpen);CollectionAssert.AreEqual(hand,model.CurrentHand);Assert.AreEqual(0,model.SelectedCards.Count);Assert.AreEqual(cost,model.GetTotalUsedCost());Assert.IsFalse(model.IsConfirming);binding.Dispose();Assert.IsTrue(imported!=null);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }finally{binding.Dispose();GameplayPause.SetPaused(paused);release.SetValue(null,priorRelease);frames.SetValue(null,priorFrames);}
        }
        [TestCase(false)][TestCase(true)]
        public async Task 会話Popupは元ViewとSessionを保持し世代入力とpause親終了を管理する(bool skipMode)
        {
            var root=Panel("managed-dialogue");var v=root.AddComponent<Olca.Presentation.Field.DialogueView>();v.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),()=>{});
            var session=new Olca.Core.Domain.DialogueSession(new Olca.Core.Domain.DialogueScript("dialogue-owner",new[]{new Olca.Core.Domain.DialogueLine(string.Concat(Enumerable.Repeat("ナガイ カイワ\n",80))+"オワリ"),new Olca.Core.Domain.DialogueLine("ツギ")}),Olca.Core.Domain.DialogueTextSpeed.Instant);
            var layer=views.GetLayer("dialogue");layer.Register(owner,"dialogue",_=>Task.FromResult(root),(panel,ctx)=>v.BindManaged(session,ctx,dialogs),panel=>v.HideManaged());
            Assert.IsTrue((await layer.ShowAsync("dialogue")).Succeeded);
            Button Find(string name)=>root.GetComponentsInChildren<Button>().Single(b=>b.name==name);
            var next=Find("DialogueNext");var history=Find("DialogueHistory");var entry=Find(skipMode?"DialogueSkip":"DialogueHistory");var oldNormal=next.onClick;EventSystem.current.SetSelectedGameObject(entry.gameObject);int index=session.Index;long initialVersion=session.Version;
            entry.onClick.Invoke();v.Refresh();var livePopup=root.GetComponentInChildren<ReturnDialog>();livePopup.RefreshLifetime();Assert.Greater(Find("DialoguePause").transform.GetSiblingIndex(),livePopup.transform.GetSiblingIndex(),"毎frameの前面化でも親のPause操作を覆わない");Assert.AreEqual(1,dialogs.ActiveCount);Assert.AreEqual(skipMode,session.SkipConfirmationOpen);Assert.AreEqual(!skipMode,session.BacklogOpen);var oldClose=Find("DialogueHistory").onClick;oldNormal.Invoke();Assert.AreEqual(index,session.Index);Assert.DoesNotThrow(()=>dialogs.Tick());
            session.IsPaused=true;v.SetPresentationPaused(true);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsTrue(root.activeSelf);Assert.AreEqual(skipMode,session.SkipConfirmationOpen);Assert.AreEqual(!skipMode,session.BacklogOpen);
            using(var other=dialogs.CreateOwner(()=>true)){var panel=Panel("pause-dialogue-confirm");var close=Button(panel,"close-pause-dialogue");var otherRequest=dialogs.ShowNotice(other,panel,close,null,_=>{});Assert.AreEqual(1,dialogs.ActiveCount);close.onClick.Invoke();Assert.AreEqual(DialogResult.Cancelled,await otherRequest);}
            frame++;session.IsPaused=false;v.SetPresentationPaused(false);Assert.AreEqual(1,dialogs.ActiveCount);oldClose.Invoke();Assert.AreEqual(1,dialogs.ActiveCount);Find("DialogueHistory").onClick.Invoke();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(session.SkipConfirmationOpen);Assert.IsFalse(session.BacklogOpen);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);long afterClose=session.Version;oldNormal.Invoke();next.onClick.Invoke();Assert.AreEqual(afterClose,session.Version);Assert.AreEqual(index,session.Index);
            frame++;entry.onClick.Invoke();v.Refresh();Assert.AreEqual(1,dialogs.ActiveCount);var stalePopup=Find("DialogueHistory").onClick;var beforeHide=Find("DialogueNext").onClick;
            if(skipMode){var confirm=Find("DialogueNext").onClick;confirm.Invoke();Assert.IsFalse(session.IsOpen);long ended=session.Version;confirm.Invoke();Assert.AreEqual(ended,session.Version);Assert.AreEqual(0,dialogs.ActiveCount);}
            else{var scroll=root.GetComponentInChildren<ScrollRect>();Assert.Greater(scroll.content.rect.height,scroll.viewport.rect.height);layer.CloseCurrent();Assert.IsTrue(session.IsOpen);Assert.IsTrue(session.BacklogOpen);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsFalse(root.activeSelf);}
            layer.CloseCurrent();Assert.AreNotSame(beforeHide,next.onClick);stalePopup.Invoke();var oldSession=session;long oldVersion=oldSession.Version;
            session=new Olca.Core.Domain.DialogueSession(new Olca.Core.Domain.DialogueScript("dialogue-reopen",new[]{new Olca.Core.Domain.DialogueLine("フッキ"),new Olca.Core.Domain.DialogueLine("ツギ")}),Olca.Core.Domain.DialogueTextSpeed.Instant);frame++;Assert.IsTrue((await layer.ShowAsync("dialogue")).Succeeded);oldNormal.Invoke();stalePopup.Invoke();Assert.AreEqual(0,session.Index);Assert.AreEqual(oldVersion,oldSession.Version);Find("DialogueNext").onClick.Invoke();Assert.AreEqual(1,session.Index);layer.CloseCurrent();views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);
        }
        [Test] public void 旧Bindで会話を交換した後のskip取消は新入口へ戻り前の履歴callbackを拒否する()
        {
            var root=Panel("legacy-dialogue-rebind");var view=root.AddComponent<Olca.Presentation.Field.DialogueView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),()=>{});
            Olca.Core.Domain.DialogueSession Session(string id)=>new Olca.Core.Domain.DialogueSession(new Olca.Core.Domain.DialogueScript(id,new[]{new Olca.Core.Domain.DialogueLine("カイワ"),new Olca.Core.Domain.DialogueLine("ツギ")}),Olca.Core.Domain.DialogueTextSpeed.Instant);
            Button Find(string name)=>root.GetComponentsInChildren<Button>().Single(b=>b.name==name);
            var first=Session("first");view.Bind(first);EventSystem.current.SetSelectedGameObject(Find("DialogueHistory").gameObject);Find("DialogueHistory").onClick.Invoke();view.Refresh();var oldClose=Find("DialogueHistory").onClick;long oldVersion=first.Version;
            var second=Session("second");view.Bind(second);var entry=Find("DialogueSkip");EventSystem.current.SetSelectedGameObject(entry.gameObject);entry.onClick.Invoke();view.Refresh();Assert.IsTrue(second.SkipConfirmationOpen);
            oldClose.Invoke();Assert.IsTrue(second.SkipConfirmationOpen);Assert.AreEqual(oldVersion,first.Version);Find("DialogueHistory").onClick.Invoke();Assert.IsFalse(second.SkipConfirmationOpen);Assert.AreEqual(0,second.Index);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);Assert.AreEqual(oldVersion,first.Version);view.Bind(null);
        }
        [TestCase(false)][TestCase(true)]
        public void 履歴Dialogは読取ページと末尾位置を所有し同ticketの閉じると旧世代を分離する(bool managed)
        {
            var parent=Panel("backlog-underlay");parent.SetActive(true);bool live=true;int closed=0;
            var data=new[]{"ミジカイ ブン",string.Concat(Enumerable.Repeat("ナガイ リレキ\n",80))+"オワリ"};
            using(var scope=dialogs.CreateOwner(()=>live)){
                var first=DialogueBacklogDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),data);first.Finished+=_=>closed++;
                Assert.IsTrue(first.Open(parent,managed?dialogs:null,managed?scope:null,()=>live,true));Canvas.ForceUpdateCanvases();Assert.AreEqual(1,first.Position.Page);Assert.Greater(first.Scroll.content.rect.height,first.Scroll.viewport.rect.height);first.Scroll.verticalNormalizedPosition=0;
                var previous=first.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueNext").onClick;var close=first.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueHistory").onClick;
                previous.Invoke();Assert.AreEqual(0,first.Position.Page);Assert.AreSame(close,first.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueHistory").onClick,"ページ更新でticketのclose listenerを作り直さない");first.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueFast").onClick.Invoke();Assert.AreEqual(1,first.Position.Page);first.Scroll.verticalNormalizedPosition=0;var reading=first.Position;
                first.Abort();Assert.AreEqual(1,closed);frame++;var second=DialogueBacklogDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),data,reading);second.Finished+=_=>closed++;
                Assert.IsTrue(second.Open(parent,managed?dialogs:null,managed?scope:null,()=>live,true));Canvas.ForceUpdateCanvases();Assert.AreEqual(1,second.Position.Page);Assert.AreEqual(0,second.Scroll.verticalNormalizedPosition,.01);previous.Invoke();close.Invoke();Assert.IsTrue(second.IsOpen);Assert.AreEqual(1,second.Position.Page);Assert.AreEqual(1,closed);
                second.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueHistory").onClick.Invoke();Assert.IsFalse(second.IsOpen);Assert.AreEqual(2,closed);Assert.AreEqual(0,dialogs.ActiveCount);
            }
        }
        [Test] public void 旧SceneのCheckpoint親でも帰還Viewなしで進行Popupを開閉できる()
        {
            var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=controller.GetType();
            var checkpoint=Panel("legacy-checkpoint");checkpoint.SetActive(true);var popup=ProgressPopup();type.GetField("checkpointOverlay",flags).SetValue(controller,checkpoint);type.GetField("progressActionDialog",flags).SetValue(controller,popup);
            type.GetMethod("ShowProgressAction",flags).Invoke(controller,new object[]{"checkpoint legacy",false,true});Assert.IsTrue(popup.IsOpen);Assert.AreEqual("checkpoint legacy",popup.Message.text);popup.CancelButton.onClick.Invoke();Assert.IsFalse(popup.IsOpen);Assert.IsTrue(checkpoint.activeSelf);
        }
        private ConfirmationDialog DiscardPopup()=>ConfirmationDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),new ConfirmationContent{Title="編成",Message="破棄しますか",ConfirmName="DiscardDepartureDraft",CancelName="KeepEditingDeparture"});
        private ConfirmationDialog DeletePopup(string name,bool pending=false)=>ConfirmationDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),new ConfirmationContent{Title="削除",Subject=name,Message="削除しますか",ConfirmName="ConfirmDeleteDeck",CancelName="CancelDeleteDeck",ConfirmEnabled=!pending});
        [TestCase(false)][TestCase(true)]
        public void 共通確認Dialogの編成破棄用途は取消確定と旧世代を自分で所有する(bool managed)
        {
            var parent=Panel("deck-parent");parent.SetActive(true);int discarded=0,kept=0;using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=DiscardPopup();popup.Completed+=yes=>{if(yes)discarded++;else kept++;};Assert.IsTrue(popup.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true));Assert.IsFalse(parent.activeSelf);
                var old=popup.GetComponentsInChildren<Button>().Single(b=>b.name=="DiscardDepartureDraft").onClick;popup.GetComponentsInChildren<Button>().Single(b=>b.name=="KeepEditingDeparture").onClick.Invoke();Assert.AreEqual(1,kept);Assert.IsTrue(parent.activeSelf);if(managed)Assert.IsTrue(dialogs.Blocks(scope));frame++;
                var next=DiscardPopup();next.Completed+=yes=>{if(yes)discarded++;};Assert.IsTrue(next.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true));old.Invoke();Assert.AreEqual(0,discarded);next.GetComponentsInChildren<Button>().Single(b=>b.name=="DiscardDepartureDraft").onClick.Invoke();Assert.AreEqual(1,discarded);Assert.IsTrue(parent.activeSelf);Assert.AreEqual(0,dialogs.ActiveCount);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void 共通確認Dialogの削除用途は対象Idと長名を保持し旧確定を別対象へ渡さない(bool managed)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);var parent=Panel("saved-parent");parent.SetActive(true);string deleted=null;using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=DeletePopup(new string('名',2000));popup.Completed+=accepted=>{if(accepted)deleted="first";};Assert.IsTrue(popup.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true));Assert.NotNull(popup.GetComponentInChildren<ScrollRect>());
                var old=popup.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmDeleteDeck").onClick;popup.GetComponentsInChildren<Button>().Single(b=>b.name=="CancelDeleteDeck").onClick.Invoke();Assert.IsNull(deleted);frame++;
                var next=DeletePopup("別の保存名");next.Completed+=accepted=>{if(accepted)deleted="second";};next.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true);old.Invoke();Assert.IsNull(deleted);next.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmDeleteDeck").onClick.Invoke();Assert.AreEqual("second",deleted);Assert.IsTrue(parent.activeSelf);
            }
        }
        [Test] public void 削除待機Dialogの無効確定と親終了は業務結果を返さない()
        {
            var parent=Panel("pending-parent");parent.SetActive(true);bool live=true;int results=0;using(var scope=dialogs.CreateOwner(()=>live)){
                var popup=DeletePopup("保存名",true);popup.Completed+=r=>results++;popup.Open(parent,dialogs,scope,()=>live,true);var button=popup.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmDeleteDeck");Assert.IsFalse(button.interactable);var old=button.onClick;old.Invoke();Assert.AreEqual(0,results);live=false;popup.RefreshLifetime();old.Invoke();Assert.AreEqual(0,results);Assert.AreEqual(0,dialogs.ActiveCount);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void 個別読取Dialogは長文末尾とresizeを保持し閉じた世代を拒否する(bool managed)
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);var parent=Panel("read-parent");parent.SetActive(true);int closes=0;using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=ReadOnlyDetailsDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),"詳細",string.Join("\n",Enumerable.Repeat("長文の検証",200))+"\nオワリシルシ",true);popup.Closed+=()=>closes++;popup.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,false);Canvas.ForceUpdateCanvases();popup.RefreshLayout();Assert.Greater(popup.Scroll.content.rect.height,popup.Scroll.viewport.rect.height);popup.Scroll.verticalNormalizedPosition=0;popup.RefreshLayout();((RectTransform)host.transform).sizeDelta=new Vector2(1440,1080);Canvas.ForceUpdateCanvases();popup.RefreshLayout();Assert.AreEqual(0,popup.Scroll.verticalNormalizedPosition,.001);Assert.IsTrue(popup.Scroll.content.GetComponent<Text>().text.EndsWith("オワリシルシ"));
                var old=popup.GetComponentInChildren<Button>().onClick;old.Invoke();Assert.AreEqual(1,closes);frame++;var next=ReadOnlyDetailsDialog.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),"短文","短い説明",false);next.Closed+=()=>closes++;next.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,false);old.Invoke();Assert.AreEqual(1,closes);next.GetComponentInChildren<Button>().onClick.Invoke();Assert.AreEqual(2,closes);Assert.IsTrue(parent.activeSelf);
            }
        }
        [TestCase(false)][TestCase(true)]
        public void 共通確認の直接破棄と拒否終了はcallbackと描画参照を返す(bool managed)
        {
            var parent=Panel("destroy-parent");parent.SetActive(true);int results=0;using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=DiscardPopup();popup.Completed+=_=>results++;popup.Terminated+=()=>{};popup.Open(parent,managed?dialogs:null,managed?scope:null,()=>true,true);var old=popup.GetComponentsInChildren<Button>().Single(b=>b.name=="DiscardDepartureDraft").onClick;
                // EditMode fixtureではMonoBehaviourの破棄messageを明示し、実Playでの自然な破棄も別途確認する。
                Assert.DoesNotThrow(()=>typeof(ConfirmationDialog).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(popup,null));Assert.DoesNotThrow(()=>UnityEngine.Object.DestroyImmediate(popup.gameObject));old.Invoke();Assert.AreEqual(0,results);Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsNull(typeof(ReturnDialog).GetField("Terminated",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup));Assert.IsNull(typeof(ReturnDialog).GetField("widgets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(popup));
                if(managed){frame++;var other=Panel("busy-other");dialogs.ShowPersistent(scope,other,Button(other,"yes"),Button(other,"no"),()=>true,_=>{});var rejected=DiscardPopup();rejected.Completed+=_=>results++;rejected.Terminated+=()=>{};Assert.IsFalse(rejected.Open(parent,dialogs,scope,()=>true,true));Assert.IsNull(typeof(ReturnDialog).GetField("Terminated",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(rejected));Assert.AreEqual(0,results);Assert.AreEqual(1,dialogs.ActiveCount);}
            }
        }
        private sealed class ProgressOperationsProbe : IProgressActionOperations
        {
            public long Revision=>47;public bool Pending {get;private set;}public ProgressPendingAction PendingAction {get;private set;}public string Error=>"fixture-write-failure";public bool CanStartNewGame=>true;
            public int Saves,Starts,Retries;public long ConfirmedRevision;public bool RetrySucceeds;
            public bool ManualSave(){Saves++;Pending=true;PendingAction=ProgressPendingAction.ManualSave;return false;}
            public bool StartNewGame(long revision){Starts++;ConfirmedRevision=revision;Pending=true;PendingAction=ProgressPendingAction.NewGame;return false;}
            public bool Retry(){Retries++;if(!RetrySucceeds)return false;Pending=false;PendingAction=ProgressPendingAction.None;return true;}
        }
        private ProgressActionDialog ProgressPopup(string name="progress-owned")
        {
            var popup=Panel(name);var value=popup.AddComponent<ProgressActionDialog>();value.Initialize(new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{}));return value;
        }
        [TestCase(false)][TestCase(true)]
        public void 個別進行Dialogは失敗と同計画再試行を所有し同frame二重実行を拒否する(bool newGame)
        {
            var parent=Panel("progress-parent");parent.SetActive(true);var focus=Button(parent,"origin");EventSystem.current.SetSelectedGameObject(focus.gameObject);
            using(var scope=dialogs.CreateOwner(()=>true)){
                var popup=ProgressPopup();var service=new ProgressOperationsProbe();int results=0;ProgressActionOutcome outcome=ProgressActionOutcome.Closed;popup.Completed+=r=>{results++;outcome=r.Outcome;};
                if(newGame){popup.OpenNewGame(service,dialogs,scope,()=>true);Assert.AreEqual(0,service.Starts);popup.ConfirmButton.onClick.Invoke();Assert.AreEqual(47,service.ConfirmedRevision);}
                else popup.OpenManualSave(service,dialogs,scope,()=>true);
                Assert.AreEqual(1,dialogs.ActiveCount);Assert.IsFalse(popup.CancelButton.interactable);popup.CancelButton.onClick.Invoke();Assert.IsTrue(popup.IsOpen);
                var old=popup.ConfirmButton.onClick;frame++;old.Invoke();old.Invoke();Assert.AreEqual(1,service.Retries);Assert.AreEqual(1,dialogs.ActiveCount);
                service.RetrySucceeds=true;frame++;old.Invoke();Assert.AreEqual(2,service.Retries);Assert.AreEqual(newGame?1:0,service.Starts);Assert.AreEqual(newGame?0:1,service.Saves);
                if(newGame){Assert.IsFalse(popup.IsOpen);Assert.AreEqual(ProgressActionOutcome.NewGameStarted,outcome);}
                else{Assert.IsTrue(popup.IsOpen);Assert.IsFalse(popup.ConfirmButton.gameObject.activeSelf);Assert.IsTrue(popup.CancelButton.interactable);popup.CancelButton.onClick.Invoke();}
                Assert.AreEqual(1,results);Assert.IsTrue(dialogs.Blocks(scope));old.Invoke();Assert.AreEqual(2,service.Retries);Assert.AreSame(focus.gameObject,EventSystem.current.currentSelectedGameObject);
            }
        }
        [Test] public void 進行Dialogの再生成は既知の未保存計画を新規保存せず引き継ぐ()
        {
            var service=new ProgressOperationsProbe();var popup=ProgressPopup();popup.OpenManualSave(service,dialogs,null,()=>true);var old=popup.ConfirmButton.onClick;popup.Abort();UnityEngine.Object.DestroyImmediate(popup.gameObject);
            var next=ProgressPopup("recreated");next.OpenManualSave(service,dialogs,null,()=>true);old.Invoke();Assert.AreEqual(1,service.Saves);Assert.AreEqual(0,service.Retries);Assert.IsFalse(next.CancelButton.interactable);
            service.RetrySucceeds=true;next.ConfirmButton.onClick.Invoke();Assert.AreEqual(1,service.Retries);Assert.AreEqual(1,service.Saves);next.CancelButton.onClick.Invoke();Assert.IsFalse(next.IsOpen);
        }
        [Test] public void 進行DialogのSuspendResumeと親終了は旧callbackを失効させる()
        {
            bool live=true;var service=new ProgressOperationsProbe();var popup=ProgressPopup();popup.OpenManualSave(service,dialogs,null,()=>live);var old=popup.ConfirmButton.onClick;popup.Suspend();Assert.AreEqual(0,dialogs.ActiveCount);Assert.IsTrue(popup.IsOpen);
            popup.Resume(dialogs,null,()=>live);old.Invoke();Assert.AreEqual(0,service.Retries);Assert.AreEqual(1,service.Saves);Assert.AreEqual(1,dialogs.ActiveCount);
            var current=popup.ConfirmButton.onClick;live=false;popup.RefreshLifetime();current.Invoke();Assert.AreEqual(0,dialogs.ActiveCount);Assert.AreEqual(0,service.Retries);Assert.IsFalse(popup.IsOpen);Assert.IsTrue(service.Pending);
        }
        [Test] public void 他Dialogが占有中の進行要求は保存前に拒否する()
        {
            var panel=Panel("other");using(var scope=dialogs.CreateOwner(()=>true)){dialogs.ShowPersistent(scope,panel,Button(panel,"confirm"),Button(panel,"cancel"),()=>true,_=>{});var service=new ProgressOperationsProbe();var popup=ProgressPopup();popup.OpenManualSave(service,dialogs,null,()=>true);Assert.AreEqual(0,service.Saves);Assert.IsFalse(popup.IsOpen);Assert.AreEqual(1,dialogs.ActiveCount);}
        }
        [Test] public void 撤退Dialogは最新可否で判断し取消後の旧確定は次世代へ作用しない()
        {
            var factory=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});var pause=Panel("legacy-pause");pause.SetActive(true);var origin=Button(pause,"RetreatButton");EventSystem.current.SetSelectedGameObject(origin.gameObject);
            var root=factory.Panel("retreat-owned",true);var popup=root.AddComponent<RetreatConfirmationDialog>();popup.Initialize(factory);bool paused=true,live=true;int effects=0;popup.Confirmed+=()=>effects++;
            RetreatDialogState State()=>new RetreatDialogState(paused,false,false,false,false,false);
            popup.Open(State,null,null,pause,()=>live);Assert.IsFalse(pause.activeSelf);var old=popup.ConfirmButton.onClick;paused=false;old.Invoke();Assert.AreEqual(0,effects);Assert.IsTrue(popup.IsOpen);
            paused=true;popup.CancelButton.onClick.Invoke();Assert.AreEqual(0,effects);Assert.IsTrue(pause.activeSelf);Assert.AreSame(origin.gameObject,EventSystem.current.currentSelectedGameObject);
            popup.Open(State,null,null,pause,()=>live);old.Invoke();Assert.AreEqual(0,effects);popup.ConfirmButton.onClick.Invoke();Assert.AreEqual(1,effects);Assert.IsFalse(popup.IsOpen);
            popup.Open(State,null,null,pause,()=>live);var stale=popup.ConfirmButton.onClick;live=false;popup.RefreshLifetime();stale.Invoke();Assert.AreEqual(1,effects);Assert.IsFalse(popup.IsOpen);
        }
        [TestCase(false)][TestCase(true)]
        public void Host表示は初回要求だけ生成し再表示とPopupを共有して終了後に返す(bool managed)
        {
            var content=Panel("host-factory-content");content.SetActive(true);var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var factory=new GameplayPanelFactory(content.transform,font,_=>{});var bindings=new HostPanelBindings(owner,content.transform,managed?resources:null,managed?views:null);
            int generated=0,actions=0;GameplayPauseView pause=null;
            bindings.Register("pause","fixture-host-ui",()=>{generated++;var root=factory.Pause();pause=root.AddComponent<GameplayPauseView>();root.GetComponentsInChildren<Button>(true).Single(b=>b.name=="ResumeButton").onClick.AddListener(()=>{if(bindings.IsAlive)actions++;});pause.Initialize(()=>bindings.IsAlive);return root;},(root,ctx)=>pause.Show(ctx),root=>{if(root!=null)root.GetComponent<GameplayPauseView>().Hide();});
            Assert.AreEqual(0,generated);Assert.AreEqual(0,content.transform.childCount);Assert.AreEqual(0,resources.OwnedInstanceCount);
            var first=bindings.Show("pause");Assert.AreEqual(1,generated);if(!managed)first.SetActive(true);var old=first.GetComponentsInChildren<Button>(true).Single(b=>b.name=="ResumeButton").onClick;
            Assert.AreSame(first,bindings.Show("pause"));Assert.AreEqual(1,generated);
            if(managed){old.Invoke();Assert.AreEqual(1,actions);views.GetLayer("fixture-host-ui").CloseCurrent();old.Invoke();Assert.AreEqual(1,actions);frame++;bindings.Show("pause");old.Invoke();Assert.AreEqual(1,actions);}
            int prompts=0;GameObject CreatePrompt(){prompts++;var panel=factory.Panel("ProgressActionOverlay",true);panel.AddComponent<ProgressActionDialog>().Initialize(factory);return panel;}
            var popup=bindings.Popup("prompt",CreatePrompt);Assert.AreSame(popup,bindings.Popup("prompt",CreatePrompt));Assert.AreEqual(1,prompts);
            Assert.AreEqual(managed?2:0,resources.OwnedInstanceCount);bindings.Dispose();bindings.Dispose();Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.IsTrue(first==null);Assert.IsTrue(popup==null);Assert.IsTrue(font!=null);Assert.Throws<ObjectDisposedException>(()=>bindings.Show("pause"));int beforeOld=actions;using(var replacement=new HostPanelBindings(owner,content.transform,managed?resources:null,managed?views:null)){Assert.DoesNotThrow(()=>old.Invoke());Assert.AreEqual(beforeOld,actions,"旧bindingの入力は新binding生成後も作用しない");}
            UnityEngine.Object.DestroyImmediate(content);
        }
        [Test]
        public void Host表示factory失敗は旧Viewを保ち同じ登録で再試行できる()
        {
            var content=Panel("host-factory-failure");content.SetActive(true);var factory=new GameplayPanelFactory(content.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{});
            using(var bindings=new HostPanelBindings(owner,content.transform,resources,views)){
                bindings.Register("base","fixture-host-ui",()=>factory.Panel("base"),(root,ctx)=>root.SetActive(true),root=>{if(root!=null)root.SetActive(false);});var prior=bindings.Show("base");
                bool fail=true;bindings.Register("retry","fixture-host-ui",()=>factory.Component<GameplayPauseView>("retry",v=>{if(fail)throw new InvalidOperationException("fixture20");v.Initialize(()=>true);}).gameObject,(root,ctx)=>root.GetComponent<GameplayPauseView>().Show(ctx),root=>{if(root!=null)root.GetComponent<GameplayPauseView>().Hide();});
                Assert.Throws<InvalidOperationException>(()=>bindings.Show("retry"));Assert.IsTrue(prior.activeSelf);Assert.AreEqual("base",views.GetLayer("fixture-host-ui").CurrentKey);Assert.AreEqual(1,resources.OwnedInstanceCount);Assert.AreEqual(1,content.transform.childCount);
                fail=false;var next=bindings.Show("retry");Assert.NotNull(next);Assert.IsFalse(prior.activeSelf);Assert.AreEqual(2,resources.OwnedInstanceCount);
                views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.IsTrue(next==null);Assert.IsTrue(prior==null);
            }
            UnityEngine.Object.DestroyImmediate(content);
        }
        [TestCase(false)][TestCase(true)]
        public void 同画面の再表示は旧contextを閉じ現在の実体だけ保持する(bool destroyed)
        {
            int created=0;ManagedScreenContext context=null;var layer=views.GetLayer("recreate20");
            layer.Register(owner,"screen",_=>{created++;return Task.FromResult(Panel("recreate20"));},(root,ctx)=>{context=ctx;root.SetActive(true);},root=>{if(root!=null)root.SetActive(false);});
            Assert.IsTrue(layer.ShowAsync("screen").GetAwaiter().GetResult().Succeeded);var priorContext=context;var first=host.transform.Find("recreate20").gameObject;
            var popup=Panel("recreate20-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");var pending=dialogs.Show(priorContext,popup,yes,no,_=>Assert.Fail("親終了は業務callbackを呼ばない"));
            if(destroyed)UnityEngine.Object.DestroyImmediate(first);else first.SetActive(false);
            Assert.IsTrue(layer.ShowAsync("screen").GetAwaiter().GetResult().Succeeded);var next=host.transform.Find("recreate20").gameObject;
            Assert.AreEqual(destroyed?2:1,created);Assert.AreEqual(1,resources.OwnedInstanceCount);Assert.IsTrue(next.activeSelf);Assert.IsTrue(context.IsCurrent);Assert.IsFalse(priorContext.IsCurrent);Assert.AreNotSame(priorContext,context);Assert.AreEqual(DialogResult.ParentClosed,pending.GetAwaiter().GetResult());Assert.AreEqual(0,dialogs.ActiveCount);
            if(!destroyed)Assert.AreSame(first,next);views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.IsTrue(next==null);
        }
        [Test]
        public void 同画面の再表示失敗は無効なcurrentとleaseを残さず再生成できる()
        {
            bool fail=false;ManagedScreenContext context=null;var layer=views.GetLayer("retry20");
            layer.Register(owner,"screen",_=>Task.FromResult(Panel("retry20")),(root,ctx)=>{context=ctx;root.SetActive(true);if(fail)throw new InvalidOperationException("fixture20-show");},root=>{if(root!=null)root.SetActive(false);});
            Assert.IsTrue(layer.ShowAsync("screen").GetAwaiter().GetResult().Succeeded);var prior=context;
            var popup=Panel("retry20-popup");var yes=Button(popup,"yes");var no=Button(popup,"no");int callbacks=0;var pending=dialogs.Show(prior,popup,yes,no,_=>callbacks++);var ticket=typeof(DialogManager).GetField("current",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dialogs);var old=(UnityEngine.Events.UnityAction)ticket.GetType().GetField("ConfirmAction").GetValue(ticket);
            host.transform.Find("retry20").gameObject.SetActive(false);fail=true;
            var failed=layer.ShowAsync("screen").GetAwaiter().GetResult();Assert.IsFalse(failed.Succeeded);Assert.IsFalse(failed.Cancelled);Assert.IsNull(layer.CurrentKey);Assert.IsFalse(prior.IsCurrent);Assert.IsFalse(context.IsCurrent);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.AreEqual(0,dialogs.ActiveCount,"Tick前に旧ticketを返す");Assert.AreEqual(DialogResult.ParentClosed,pending.GetAwaiter().GetResult());old.Invoke();Assert.AreEqual(0,callbacks);
            fail=false;Assert.IsTrue(layer.ShowAsync("screen").GetAwaiter().GetResult().Succeeded);Assert.IsTrue(context.IsCurrent);Assert.AreEqual(1,resources.OwnedInstanceCount);views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);
        }
        [TestCase(0,30,1f)] [TestCase(10,30,2f/3)] [TestCase(31,30,0f)] [TestCase(0,0,0f)] public async Task 外界強靭HUDは既存被弾と回復を読み再表示で状態を変更しない(int damage,int poise,float fraction)
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100,poise:30,staggerDurationFrames:6,poiseRecoveryDelayFrames:10));model.Player.SetPoise(poise);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;if(damage>0)model.Player.TakeDamage(damage);view.Render();var fill=view.transform.Find("OutsidePlayerPoiseTrack/OutsidePlayerPoiseFill").GetComponent<Image>();var text=view.transform.Find("OutsidePlayerPoise").GetComponent<Text>();Assert.AreEqual(fraction,fill.fillAmount,.0001f);Assert.AreEqual(430*fraction,fill.rectTransform.rect.width,.01f);Assert.IsFalse(fill.raycastTarget);Assert.IsFalse(text.raycastTarget);StringAssert.Contains("/ "+poise,text.text);Assert.IsFalse(float.IsNaN(fill.fillAmount));
                long frame=model.Core.Frame;int hp=model.Player.CurrentHp,accumulated=model.Player.AccumulatedDamage;bool staggered=model.Player.IsStaggered;binding.SetCovered(true);for(int i=0;i<4;i++)view.Render();views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreEqual(fraction,fill.fillAmount,.0001f);Assert.AreEqual(430*fraction,fill.rectTransform.rect.width,.01f);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(hp,model.Player.CurrentHp);Assert.AreEqual(accumulated,model.Player.AccumulatedDamage);Assert.AreEqual(staggered,model.Player.IsStaggered);
                for(int i=0;i<32;i++){model.Core.TickFrame(0);model.AdvancePlayerFrame();}view.Render();Assert.IsFalse(model.Player.IsStaggered);Assert.AreEqual(0,model.Player.AccumulatedDamage);Assert.AreEqual(poise==0?0:1,fill.fillAmount);Assert.AreEqual(poise==0?0:430,fill.rectTransform.rect.width,.01f);Assert.AreEqual(hp,model.Player.CurrentHp);
            }
        }
        [Test] public async Task 外界の毒本体色は確定状態だけを読み再表示と解除と別個体で残らない()
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var body=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();Assert.AreEqual(Color.white,body.color);var sprite=body.sprite;var size=body.rectTransform.sizeDelta;var pivot=body.rectTransform.pivot;
                model.Player.ApplyPoison("tint153",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,1000,1),60);view.Render();Assert.AreEqual(Color.white,body.color,"予約だけでは未確定の毒を描かない");model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();Assert.AreEqual(new Color(.84f,.64f,1f,1f),body.color);Assert.AreEqual(1,body.color.a);Assert.AreSame(sprite,body.sprite);Assert.AreEqual(size,body.rectTransform.sizeDelta);Assert.AreEqual(pivot,body.rectTransform.pivot);
                long frame=model.Core.Frame;int hp=model.Player.CurrentHp;var states=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Player.CapturePoisons());view.SetCovered(true);for(int i=0;i<4;i++)view.Render();Assert.AreEqual(new Color(.84f,.64f,1f,1f),body.color);Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(hp,model.Player.CurrentHp);CollectionAssert.AreEqual(states,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Player.CapturePoisons()));
                views.GetLayer("return-hub").CloseCurrent();Assert.AreEqual(Color.white,body.color);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreEqual(new Color(.84f,.64f,1f,1f),body.color);
                model.Player.ClearPoisons();model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();Assert.AreEqual(Color.white,body.color);
                model.Player.ApplyPoison("tint153-short",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,50,1),60);model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();Assert.AreEqual(new Color(.84f,.64f,1f,1f),body.color);for(int i=0;i<4;i++){model.Core.TickFrame(0);model.AdvancePlayerFrame();}view.Render();Assert.AreEqual(Color.white,body.color,"自然失効でも戻る");
                UnityEngine.Object.DestroyImmediate(view.gameObject);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(Color.white,binding.View.transform.Find("OutsideEntryPlayer").GetComponent<Image>().color);
            }
        }
        [Test] public async Task 外界状態HUDは既存毒の割合を読み停止再表示解除で時計と保存を変更しない()
        {
            var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;Assert.IsNull(view.transform.Find("OutsideCombatStatusIcons"),"無状態なら生成しない");
                model.Player.ApplyPoison("hud149",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,10000,1),60);model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();
                var strip=view.GetComponentInChildren<Olca.Presentation.Battle.CombatStatusIconStrip>();Assert.IsNotNull(strip);Assert.AreEqual(1,strip.VisibleCount);Assert.IsTrue(strip.GetComponentsInChildren<Image>().All(i=>!i.raycastTarget));
                for(int i=0;i<149;i++){model.Core.TickFrame(0);model.AdvancePlayerFrame();}view.Render();var fill=strip.GetComponentsInChildren<Image>().Single(i=>i.name=="Remaining");Assert.AreEqual(.75f,fill.fillAmount,.0001f);
                var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Player.CapturePoisons());long frame=model.Core.Frame;int hp=model.Player.CurrentHp;
                binding.SetCovered(true);for(int i=0;i<4;i++)view.Render();Assert.AreEqual(frame,model.Core.Frame);Assert.AreEqual(hp,model.Player.CurrentHp);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Player.CapturePoisons()));
                views.GetLayer("return-hub").CloseCurrent();Assert.AreEqual(0,strip.VisibleCount);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreEqual(1,strip.VisibleCount);Assert.AreEqual(.75f,fill.fillAmount,.0001f);Assert.AreEqual(frame,model.Core.Frame);
                model.Player.ClearPoisons();model.Core.TickFrame(0);model.AdvancePlayerFrame();frame++;view.Render();Assert.AreEqual(0,strip.VisibleCount);Assert.AreEqual(frame,model.Core.Frame);
                model.Player.ApplyPoison("hud149b",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,10000,1),60);model.Core.TickFrame(0);model.AdvancePlayerFrame();view.Render();Assert.AreSame(strip,view.GetComponentInChildren<Olca.Presentation.Battle.CombatStatusIconStrip>());Assert.AreEqual(1,strip.VisibleCount);Assert.AreEqual(599f/600,fill.fillAmount,.0001f);
                UnityEngine.Object.DestroyImmediate(strip.gameObject);Assert.DoesNotThrow(()=>binding.ReturnToHub());Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(1,binding.View.GetComponentInChildren<Olca.Presentation.Battle.CombatStatusIconStrip>().VisibleCount);
            }
        }
        [Test] public async Task 所有者の回復保存待ちだけ画面を再生成でき戻る条件と旧世代を緩めない()
        {
            bool allowed=true,pending=false;int returned=0;var model=system.OutsideExploration;model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));model.Player.SetHp(40);
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>allowed,()=>returned++,restoreAllowed:()=>pending)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var old=binding.View.GetComponentsInChildren<Button>().Single(v=>v.name=="CloseOutsideExploration").onClick;allowed=false;pending=true;var lost=binding.View;typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lost,null);UnityEngine.Object.DestroyImmediate(lost.gameObject);Assert.IsTrue(binding.HasLostView);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(40,model.Player.CurrentHp);Assert.IsFalse(binding.ReturnToHub());old.Invoke();Assert.AreEqual(0,returned);Assert.IsTrue(binding.IsOpen);pending=false;allowed=true;Assert.IsTrue(binding.ReturnToHub());Assert.AreEqual(1,returned);
            }
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] public async Task 遠方の試用案内は概要へ縮み近接では既存詳細を保持して操作状態を変えない(int index)
        {
            var model=system.OutsideExploration;int calls=0;bool allowed=false;string message="カリのハコ\n3コ / ジョウゲン99\nA / × デ ウケトル";
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;if(index==0)view.ConfigureSupply(()=>calls++,()=>allowed,()=>message);else if(index==1)view.ConfigurePoisonSupply(()=>calls++,()=>allowed,()=>message);else view.ConfigureTrialPoison(()=>calls++,()=>allowed,()=>message);view.Render();
                var button=view.GetComponentsInChildren<Button>().Single(b=>b.name==(index==0?"OutsideSupplyChest":index==1?"OutsidePoisonSupplyChest":"OutsideTrialPoisonPoint"));var rect=(RectTransform)button.transform;var label=button.GetComponentInChildren<Text>();Assert.AreEqual(new Vector2(320,82),rect.sizeDelta);Assert.AreEqual(2,label.text.Split('\n').Length);StringAssert.DoesNotContain("デ ウケトル",label.text);Assert.IsFalse(button.IsInteractable());
                long target=index==0?Olca.Core.Logic.Integration.OutsideTrialRoute.SupplyX:index==1?Olca.Core.Logic.Integration.OutsideTrialRoute.PoisonSupplyX:Olca.Core.Logic.Integration.OutsideTrialRoute.PoisonTrialX;
                void Move(long x){for(int i=0;i<200&&model.Core.Position!=x;i++)model.Core.TickFrame(System.Math.Sign(x-model.Core.Position));model.Core.TickFrame(0);Assert.AreEqual(x,model.Core.Position);}
                Move(target);allowed=true;view.Render();Assert.AreEqual(new Vector2(420,140),rect.sizeDelta);Assert.AreEqual(Olca.Presentation.CustomScreen.CustomDisplay.Katakana(message),label.text);Assert.IsTrue(button.IsInteractable());var before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState());view.SetCovered(true);for(int i=0;i<4;i++)view.Render();Assert.AreEqual(new Vector2(420,140),rect.sizeDelta);CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(model.Core.CaptureState()));Assert.AreEqual(0,calls);
                allowed=false;message="カリのハコ\nウケトリズミ";Move(model.Core.Definition.Start);view.Render();Assert.AreEqual(new Vector2(320,82),rect.sizeDelta);StringAssert.Contains("ウケトリズミ",label.text);Assert.IsFalse(button.IsInteractable());Assert.AreEqual(0,calls);message="カリのハコ\n"+new string('カ',80)+"\nホゾン ケッカ";view.Render();Assert.AreEqual(2,label.text.Split('\n').Length);StringAssert.EndsWith("...",label.text);Assert.Less(label.text.Length,message.Length);Move(target);view.Render();Assert.AreEqual(Olca.Presentation.CustomScreen.CustomDisplay.Katakana(message),label.text);Assert.AreEqual(new Vector2(420,140),rect.sizeDelta);Assert.AreEqual(0,calls);
            }
        }
        [Test] public async Task 補給表示は覆いと親終了と再表示後の旧操作を拒否する()
        {
            int calls=0;Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,factory,()=>true,()=>{},trialRoute:()=>true)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);binding.View.ConfigureSupply(()=>calls++,()=>true,()=>"カリのハコ");binding.View.Render();var button=binding.View.GetComponentsInChildren<Button>().Single(b=>b.name=="OutsideSupplyChest");Assert.IsTrue(button.IsInteractable());var old=button.onClick;old.Invoke();Assert.AreEqual(1,calls);
                binding.View.SetCovered(true);old.Invoke();Assert.AreEqual(1,calls);Assert.IsTrue(binding.ReturnToHub());old.Invoke();Assert.AreEqual(1,calls);
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);binding.View.ConfigureSupply(()=>calls++,()=>true,()=>"カリのハコ");binding.View.Render();old.Invoke();Assert.AreEqual(1,calls);binding.View.GetComponentsInChildren<Button>().Single(b=>b.name=="OutsideSupplyChest").onClick.Invoke();Assert.AreEqual(2,calls);
            }
        }
        [Test] public async Task 外界入口は同レイヤーを切替え旧操作を拒否しView再生成でRootSessionを失わない()
        {
            var layer=views.GetLayer("return-hub");var hub=Panel("outside-fixture-hub");var entry=Button(hub,"outside-fixture-entry");ManagedScreenContext hubContext=null;int hubCalls=0,returned=0;
            layer.Register(owner,"outside-fixture-hub",_=>Task.FromResult(hub),(go,ctx)=>{hubContext=ctx;entry.onClick=new Button.ButtonClickedEvent();entry.onClick.AddListener(()=>{if(ctx.IsCurrent)hubCalls++;});go.SetActive(true);EventSystem.current.SetSelectedGameObject(entry.gameObject);},go=>go.SetActive(false));
            Assert.IsTrue((await layer.ShowAsync("outside-fixture-hub")).Succeeded);var oldHub=entry.onClick;var previous=hubContext;var model=system.OutsideExploration;int before=resources.OwnedInstanceCount;
            Func<CancellationToken,Task<GameObject>> factory=ct=>{ct.ThrowIfCancellationRequested();return Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));};
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{returned++;layer.ShowAsync("outside-fixture-hub").GetAwaiter().GetResult();})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(model.IsPresented);Assert.IsFalse(previous.IsCurrent);Assert.IsFalse(hub.activeSelf);oldHub.Invoke();Assert.AreEqual(0,hubCalls);
                var back=binding.View.GetComponentsInChildren<Button>().Single(v=>v.name=="CloseOutsideExploration");Assert.AreSame(back.gameObject,EventSystem.current.currentSelectedGameObject);var oldBack=back.onClick;oldBack.Invoke();oldBack.Invoke();Assert.AreEqual(1,returned);Assert.IsFalse(model.IsPresented);Assert.IsTrue(hub.activeSelf);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);oldBack.Invoke();Assert.AreEqual(1,returned);Assert.IsTrue(binding.IsOpen);Assert.AreSame(model,system.OutsideExploration);
                var lost=binding.View;typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(lost,null);UnityEngine.Object.DestroyImmediate(lost.gameObject);Assert.IsTrue(binding.HasLostView);Assert.IsFalse(model.IsPresented);Assert.IsTrue((await binding.OpenAsync()).Succeeded);oldBack.Invoke();Assert.AreEqual(1,returned);Assert.IsTrue(binding.IsOpen);Assert.AreSame(model,system.OutsideExploration);
            }
            Assert.IsFalse(model.IsEnded);Assert.IsFalse(model.IsPresented);Assert.AreEqual(before,resources.OwnedInstanceCount);
            using(var recreated=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){Assert.IsTrue((await recreated.OpenAsync()).Succeeded);Assert.IsTrue(model.IsPresented);Assert.AreSame(model,system.OutsideExploration);}
            system.Dispose();Assert.IsTrue(model.IsEnded);Assert.IsFalse(model.IsPresented);
        }
        [Test] public async Task 外界入口は管理側で閉じたViewの外部破棄を再表示要求と見なさない()
        {
            var layer=views.GetLayer("return-hub");var hub=Panel("outside-closed-hub");var entry=Button(hub,"outside-entry");
            layer.Register(owner,"outside-closed-hub",_=>Task.FromResult(hub),(go,ctx)=>{go.SetActive(true);EventSystem.current.SetSelectedGameObject(entry.gameObject);},go=>go.SetActive(false));
            var model=system.OutsideExploration;var core=model.Core;var gauge=core.CardGauge;int created=0,returned=0;
            Func<CancellationToken,Task<GameObject>> factory=ct=>{created++;return Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));};
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{returned++;layer.ShowAsync("outside-closed-hub").GetAwaiter().GetResult();})){
                for(int route=0;route<3;route++){
                    Assert.IsTrue((await binding.OpenAsync()).Succeeded);var closed=binding.View;var oldBack=closed.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseOutsideExploration").onClick;
                    if(route==0)oldBack.Invoke();else if(route==1)Assert.IsTrue((await layer.ShowAsync("outside-closed-hub")).Succeeded);else {layer.CloseCurrent();Assert.IsTrue((await layer.ShowAsync("outside-closed-hub")).Succeeded);}
                    Assert.IsNull(binding.View);Assert.IsFalse(binding.IsOpen);Assert.IsFalse(model.IsPresented);
                    typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(closed,null);UnityEngine.Object.DestroyImmediate(closed.gameObject);
                    Assert.IsFalse(binding.HasLostView);oldBack.Invoke();binding.Pump(true);
                    Assert.IsTrue(hub.activeSelf);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);Assert.AreEqual(1,returned);Assert.AreEqual(route+1,created);
                    Assert.AreSame(core,model.Core);Assert.AreSame(gauge,core.CardGauge);Assert.AreEqual(0,core.Frame);
                }
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.IsTrue(binding.IsOpen);Assert.AreEqual(4,created);
            }
            Assert.IsFalse(model.IsPresented);
        }

        [TestCase(-1)][TestCase(1)] public async Task 外界壁ずりは下降二コマで壁蹴りは通常上昇姿勢を同尺度アンカーで表示し閉鎖で参照を返す(int side)
        {
            var session=system.OutsideExploration;var p=Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true);
            if(side<0)p=new Olca.Core.Logic.Integration.OutsideTraversalProfile(p.Id,p.DashSpeedPerSecond,p.DashFrames,p.DashCooldownFrames,p.DashResetFrames,p.DashJumpSpeedPerSecond,p.BodyHalfWidth,p.BodyHeight,p.WallSlidePerFrame,p.WallKickVelocity,p.WallKickSpeedPerSecond,p.WallKickPushFrames,p.WallKickMotionFrames,true,new Olca.Core.Logic.Integration.OutsideValidationWall("left-wall",4*60000,0,1000*60000));Assert.IsTrue(session.TryConfigureTraversal(p));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var image=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var c=session.Core;
                c.TickFrame(side,dashDown:true,dashHeld:true);view.Render();
                var trails=view.GetComponentsInChildren<Image>(true).Where(i=>i.name.StartsWith("OutsideDashAfterimage",StringComparison.Ordinal)).ToArray();Assert.AreEqual(4,trails.Length);Assert.IsTrue(trails.Any(i=>i.enabled&&i.sprite!=null));
                long dashFrame=c.Frame,dashX=c.Position;views.GetLayer("return-hub").CloseCurrent();foreach(var trail in trails){Assert.IsFalse(trail.enabled);Assert.IsNull(trail.sprite);Assert.IsFalse(trail.raycastTarget);}
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(dashFrame,c.Frame);Assert.AreEqual(dashX,c.Position);Assert.IsTrue(c.IsDashing);Assert.IsTrue(trails.Any(i=>i.enabled&&i.sprite!=null));
                long wall=c.TraversalProfile.Walls[0].X-side*c.TraversalProfile.BodyHalfWidth;for(int i=0;i<300&&c.Position!=wall;i++)c.TickFrame(side);Assert.AreEqual(wall,c.Position);
                c.TickFrame(side,true,true);view.Render();Assert.IsFalse(c.IsWallSliding);while(c.VerticalVelocity>=0)c.TickFrame(side,false,true);c.TickFrame(side,false,true);view.Render();var slide1=resources.GetShared<Sprite>("OlcaTraversal/WallSlide_01");Assert.AreSame(slide1,image.sprite);
                Assert.AreEqual(new Vector2(128,14),image.sprite.pivot);Assert.AreEqual(100,image.sprite.pixelsPerUnit);Assert.AreEqual(new Vector2(256,256),image.sprite.rect.size);Assert.IsFalse(image.raycastTarget);
                var size=image.rectTransform.sizeDelta;var pivot=image.rectTransform.pivot;
                for(int i=0;i<6;i++)c.TickFrame(side,false,true);view.Render();Assert.AreSame(resources.GetShared<Sprite>("OlcaTraversal/WallSlide_02"),image.sprite);Assert.AreEqual(size,image.rectTransform.sizeDelta);Assert.AreEqual(pivot,image.rectTransform.pivot);
                c.TickFrame(side);long kickStartX=c.Position,kickStartY=c.Height;c.TickFrame(side,true,true);Assert.AreEqual(0,c.WallKickFrame);Assert.AreEqual(kickStartX-side*8000,c.Position);Assert.AreEqual(kickStartY+12000,c.Height);view.Render();var rise=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredJumpRiseSprite;Assert.AreSame(rise,image.sprite);Assert.AreEqual(-c.Facing,image.rectTransform.localScale.x);Assert.AreEqual(rise.pivot/rise.rect.size,image.rectTransform.pivot);size=image.rectTransform.sizeDelta;pivot=image.rectTransform.pivot;
                for(int i=0;i<6;i++)c.TickFrame(side,false,true);view.Render();Assert.AreSame(resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredJumpRiseSprite,image.sprite);Assert.AreEqual(size,image.rectTransform.sizeDelta);Assert.AreEqual(pivot,image.rectTransform.pivot);
                long frame=c.Frame,y=c.Height;views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue(slide1!=null);Assert.AreEqual(frame,c.Frame);Assert.AreEqual(y,c.Height);
                foreach(var trail in view.GetComponentsInChildren<Image>(true).Where(i=>i.name.StartsWith("OutsideDashAfterimage",StringComparison.Ordinal))){Assert.IsFalse(trail.enabled);Assert.IsNull(trail.sprite);Assert.IsFalse(trail.raycastTarget);}
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(frame,c.Frame);Assert.AreEqual(y,c.Height);Assert.AreSame(resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredJumpRiseSprite,image.sprite);
                for(int i=0;i<12&&c.IsWallKicking;i++)c.TickFrame(0,false,true);Assert.IsFalse(c.IsWallKicking);view.Render();var source=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>();var expected=Math.Abs(c.VerticalVelocity)<=Olca.Core.Logic.Integration.OutsideExplorationCore.ValidationGravity?source.AuthoredJumpApexSprite:c.VerticalVelocity>0?source.AuthoredJumpRiseSprite:source.AuthoredJumpFallSprite;Assert.AreSame(expected,image.sprite);Assert.AreEqual(-c.Facing,image.rectTransform.localScale.x);
            }
        }

        [Test] public async Task 接地壁のShift中も実IdleClipの同じ位相を表示し固定立ち絵へ切り替えない()
        {
            var session=system.OutsideExploration;Assert.IsTrue(session.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true)));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var image=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var c=session.Core;long wall=c.TraversalProfile.Walls[0].X-c.TraversalProfile.BodyHalfWidth;for(int i=0;i<300&&c.Position<wall;i++)c.TickFrame(1);for(int i=0;i<8;i++)c.TickFrame(1);long idle=c.IdleFrame;
                var sample=new GameObject("ExpectedIdle",typeof(SpriteRenderer),typeof(Animator));try{var renderer=sample.GetComponent<SpriteRenderer>();renderer.enabled=false;var animator=sample.GetComponent<Animator>();animator.enabled=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var clip=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation").GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredIdleClip;
                    for(int i=0;i<12;i++){c.TickFrame(1,dashDown:i==0,dashHeld:true);view.Render();Assert.IsTrue(c.IsDashing);Assert.IsFalse(c.IsMoving);Assert.AreEqual(idle+i+1,c.IdleFrame);clip.SampleAnimation(sample,(float)((c.IdleFrame/60d)%clip.length));Assert.AreSame(renderer.sprite,image.sprite);Assert.AreEqual(-c.Facing,image.rectTransform.localScale.x);}
                }finally{UnityEngine.Object.DestroyImmediate(sample);}
            }
        }

        [Test] public async Task 高い壁蹴りでも本体は固定ヘッダーより下に映り足場壁と再入位置を一致させる()
        {
            ((RectTransform)host.transform).sizeDelta=new Vector2(1920,1080);
            var session=system.OutsideExploration;Assert.IsTrue(session.TryConfigureTraversal(Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationWall(true)));
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,session,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var c=session.Core;long wall=c.TraversalProfile.Walls[0].X-c.TraversalProfile.BodyHalfWidth;
                for(int i=0;i<300&&c.Position<wall;i++)c.TickFrame(1);
                c.TickFrame(1,true,true);c.TickFrame(1);
                for(int i=0;i<400&&c.Height<7*60000;i++){bool kick=!c.IsGrounded&&c.WallContactDirection==1&&!c.IsWallKicking;c.TickFrame(1,kick,kick);}
                Assert.GreaterOrEqual(c.Height,7*60000);long frame=c.Frame,height=c.Height;view.Render();Assert.AreEqual(frame,c.Frame);Assert.AreEqual(height,c.Height);
                var image=view.transform.Find("OutsideEntryPlayer").GetComponent<Image>();var rect=(RectTransform)view.transform;
                float top=rect.rect.height*.21f+image.rectTransform.anchoredPosition.y+(1-image.rectTransform.pivot.y)*image.rectTransform.sizeDelta.y;
                Assert.LessOrEqual(top,rect.rect.height-144+.05f,"本体上端を固定HUDの下へ保つ");
                Assert.IsFalse(view.transform.Find("OutsideEntryGround").GetComponent<Image>().enabled,"地表が画面下へ出た後に床を反転表示しない");
                var wallImage=view.transform.Find("OutsideValidationWall-validation-wall").GetComponent<Image>();Assert.IsTrue(wallImage.enabled);Assert.Greater(wallImage.rectTransform.rect.height,0);Assert.LessOrEqual(wallImage.rectTransform.rect.height,rect.rect.height+wallImage.sprite.rect.height/(wallImage.pixelsPerUnit*wallImage.pixelsPerUnitMultiplier)+.1f);
                var before=image.rectTransform.anchoredPosition;views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreEqual(before,image.rectTransform.anchoredPosition);Assert.AreEqual(frame,c.Frame);Assert.AreEqual(height,c.Height);
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] public async Task 外界移動案内は未登録手札でも現在割当へ追従し未接続Touchを案内しない(int device)
        {
            var flags=BindingFlags.Static|BindingFlags.NonPublic;var activity=typeof(InputGuideActivity).GetField("<Current>k__BackingField",flags);var priorDevice=activity.GetValue(null);var active=typeof(UnityInputProvider).GetField("<ActiveGamepadProfile>k__BackingField",flags);var version=typeof(UnityInputProvider).GetField("<GamepadProfileVersion>k__BackingField",flags);var priorActive=active.GetValue(null);var priorVersion=version.GetValue(null);
            using(var input=new UnityInputProvider())try{
                activity.SetValue(null,(InputGuideDevice)device);var model=system.OutsideExploration;Assert.IsNull(model.Cards);
                Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
                using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{},input:input)){
                    Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;var guide=view.transform.Find("OutsideMovementGuide").GetComponent<Text>();Assert.IsFalse(guide.raycastTarget);Assert.IsFalse(view.transform.Find("OutsideHandButton0").gameObject.activeSelf);
                    if(device==0){StringAssert.Contains("LeftArrow/A",guide.text);StringAssert.Contains("RightArrow/D",guide.text);StringAssert.Contains("Space",guide.text);}else if(device==1){StringAssert.Contains("スティック",guide.text);StringAssert.DoesNotContain("ジュウジ",guide.text);StringAssert.Contains("ダッシュ: RB",guide.text);StringAssert.Contains("A / ×",guide.text);StringAssert.DoesNotContain("South",guide.text);}else Assert.IsEmpty(guide.text);
                    var edit=(device==1?input.GamepadProfile:input.KeyboardProfile).BeginEdit();Assert.IsTrue(edit.TryAssign((device==1?"gamepad":"keyboard","battle","LeftPrimary"),device==1?"LeftShoulder":"F12",out _));Assert.IsTrue(edit.TryAssign((device==1?"gamepad":"keyboard",device==1?"outside":"exploration","JumpPrimary"),device==1?"RightStick":"J",out _));
                    if(device==1)Assert.IsTrue(edit.TryAssign(("gamepad","outside","DashPrimary"),"LeftShoulder",out _));
                    if(device==1)Assert.IsTrue(input.TryRestoreGamepadProfile(edit.Preview(),out _));else Assert.IsTrue(input.TryRestoreKeyboardProfile(edit.Preview(),out _));view.Render();
                    if(device==0){StringAssert.Contains("F12/A",guide.text);StringAssert.Contains("ジャンプ: J",guide.text);StringAssert.DoesNotContain("LeftArrow",guide.text);}else if(device==1){StringAssert.Contains("LB",guide.text);StringAssert.Contains("R3",guide.text);StringAssert.DoesNotContain("South",guide.text);StringAssert.DoesNotContain("A / ×",guide.text);}else Assert.IsEmpty(guide.text);
                    views.GetLayer("return-hub").CloseCurrent();Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreEqual(0,model.Core.Frame);Assert.AreEqual(6000,model.Core.CardGauge.CurrentUnits);Assert.IsNull(model.Cards);Assert.AreEqual(new Vector2(204,66),view.transform.Find("OutsideHandButton0").GetComponent<RectTransform>().sizeDelta);
                }
            }finally{activity.SetValue(null,priorDevice);active.SetValue(null,priorActive);version.SetValue(null,priorVersion);}
        }

        [Test] public async Task 外界の直接選択は閉鎖再生成で保持し旧操作と覆い中を拒否する()
        {
            var source=Enumerable.Range(0,5).SelectMany(id=>Enumerable.Range(0,4).Select(n=>new Olca.Core.Domain.Card("outside-select-"+id,"fixture",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))).ToArray();var owned=source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());var model=system.OutsideExploration;
            Assert.IsTrue(model.TryInitializeCards(source,owned,new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=model.Cards;var hand=cards.Hand.ToArray();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;Button Pick(int slot)=>view.transform.Find("OutsideHandButton"+slot).GetComponent<Button>();
                Assert.AreEqual(0,cards.SelectedIndex);Assert.IsTrue(view.transform.Find("OutsideHandButton0/OutsideHandSelected0").gameObject.activeSelf);
                var old=Pick(0).onClick;Pick(2).onClick.Invoke();Assert.AreEqual(2,cards.SelectedIndex);Assert.IsTrue(view.transform.Find("OutsideHandButton2/OutsideHandSelected2").gameObject.activeSelf);
                view.SetCovered(true);Pick(0).onClick.Invoke();Assert.AreEqual(2,cards.SelectedIndex);
                views.GetLayer("return-hub").CloseCurrent();Assert.IsFalse(model.IsPresented);UnityEngine.Object.DestroyImmediate(view.gameObject);
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);view=binding.View;Assert.AreEqual(2,cards.SelectedIndex);old.Invoke();Assert.AreEqual(2,cards.SelectedIndex);Pick(0).onClick.Invoke();Assert.AreEqual(0,cards.SelectedIndex);
                CollectionAssert.AreEqual(hand,cards.Hand);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(6000,model.Core.CardGauge.CurrentUnits);Assert.AreEqual(0,model.Core.Frame);
                Assert.AreEqual(new Vector2(36,36),view.transform.Find("OutsideHandIcon0").GetComponent<RectTransform>().sizeDelta);
            }
        }
        [TestCase("攻撃", "コウゲキ")] [TestCase("ひらがな Latin", "ひらがな Latin")] public async Task 外界初期手札HUDはRootの三枚を表示し閉鎖と再表示で再抽選しない(string name,string shown)
        {
            var source=Enumerable.Range(0,5).SelectMany(id=>Enumerable.Range(0,4).Select(n=>new Olca.Core.Domain.Card("outside-hud-"+id,name,1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))).ToArray();var owned=source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());var model=system.OutsideExploration;
            Assert.IsTrue(model.TryInitializeCards(source,owned,new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=model.Cards;var hand=cards.Hand.ToArray();
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,model,factory,()=>true,()=>{})){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);var view=binding.View;
                for(int slot=0;slot<3;slot++){Assert.AreEqual(shown,view.transform.Find("OutsideHandName"+slot).GetComponent<Text>().text);Assert.AreEqual(name,hand[slot].Name);Assert.AreEqual(name,source[slot].Name);Assert.AreEqual("コスト "+hand[slot].GetFinalCost(),view.transform.Find("OutsideHandCost"+slot).GetComponent<Text>().text);Assert.IsFalse(view.transform.Find("OutsideHandName"+slot).GetComponent<Text>().raycastTarget);}
                StringAssert.Contains("17",view.transform.Find("OutsideHandHeading").GetComponent<Text>().text);
                views.GetLayer("return-hub").CloseCurrent();Assert.IsFalse(model.IsPresented);Assert.IsTrue((await binding.OpenAsync()).Succeeded);Assert.AreSame(view,binding.View);Assert.AreSame(cards,model.Cards);CollectionAssert.AreEqual(hand,model.Cards.Hand);Assert.AreEqual(0,model.Core.Frame);Assert.AreEqual(6000,model.Core.CardGauge.CurrentUnits);
            }
        }

        [Test] public async Task 外界入口の取消と欠損失敗は前画面のfocusとSessionを変更せず再試行できる()
        {
            var layer=views.GetLayer("return-hub");var hub=Panel("outside-failure-hub");var entry=Button(hub,"outside-failure-entry");ManagedScreenContext prior=null;
            layer.Register(owner,"outside-failure-hub",_=>Task.FromResult(hub),(go,ctx)=>{prior=ctx;go.SetActive(true);EventSystem.current.SetSelectedGameObject(entry.gameObject);},go=>go.SetActive(false));Assert.IsTrue((await layer.ShowAsync("outside-failure-hub")).Succeeded);int before=resources.OwnedInstanceCount;
            var source=new TaskCompletionSource<GameObject>();using(var cancel=new CancellationTokenSource())using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,_=>source.Task,()=>true,null)){
                var pending=binding.OpenAsync(cancel.Token);cancel.Cancel();source.SetResult(Panel("outside-cancelled"));Assert.IsTrue((await pending).Cancelled);
            }
            using(var missing=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,_=>throw new InvalidOperationException("outside-fixture-missing"),()=>true,null)){var failed=await missing.OpenAsync();Assert.IsFalse(failed.Succeeded);StringAssert.Contains("outside-fixture-missing",failed.Error);}
            using(var incomplete=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,_=>Task.FromResult(Panel("outside-no-view")),()=>true,null)){Assert.IsFalse((await incomplete.OpenAsync()).Succeeded);}
            Assert.IsTrue(prior.IsCurrent);Assert.IsTrue(hub.activeSelf);Assert.AreSame(entry.gameObject,EventSystem.current.currentSelectedGameObject);Assert.IsFalse(system.OutsideExploration.IsPresented);Assert.IsFalse(system.OutsideExploration.IsEnded);Assert.AreEqual(before,resources.OwnedInstanceCount);
        }

        [TestCase(null)] [TestCase("")] [TestCase("not-a-generation")]
        public void 外界新規進行は保存済みGUIDでない要求を拒否し現状態を保持する(string generation)
        {
            var model=system.OutsideExploration;var runtime=system.OutsideRuntime;
            model.Core.TickFrame(1,false,false);long position=model.Core.Position;
            Assert.Throws<ArgumentException>(()=>system.ResetOutsideForNewProgress(generation));
            Assert.AreSame(model,system.OutsideExploration);Assert.AreSame(runtime,system.OutsideRuntime);
            Assert.IsFalse(model.IsEnded);Assert.AreEqual(position,model.Core.Position);Assert.IsNull(system.OutsideProgressGeneration);
        }
        [Test] public void 外界新規進行は旧表示時計手札を終了し同世代の重複通知では再初期化しない()
        {
            var model=system.OutsideExploration;var runtime=system.OutsideRuntime;
            var cards=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards();
            var owned=cards.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());
            Assert.IsTrue(model.TryInitializeCards(cards,owned,new Olca.Core.Logic.Battle.SeededBattleRandom(7),out var error),error);
            Assert.IsTrue(model.TryConfigureCamera(Olca.Core.Logic.Integration.OutsideCameraPolicy.FixedAt(60000)));
            using(var input=new UnityInputProvider())using(var lease=model.AttachPresentation()){
                var driver=new object();runtime.Attach(driver,input);model.Core.TickFrame(1,false,false);Assert.IsTrue(model.Core.CardGauge.TrySpend(20));
                string generation=Guid.NewGuid().ToString("N");Assert.IsTrue(system.ResetOutsideForNewProgress(generation));
                Assert.IsTrue(model.IsEnded);Assert.IsFalse(lease.IsCurrent);Assert.IsNull(lease.Core);
                Assert.Throws<ObjectDisposedException>(()=>runtime.Attach(driver,input));runtime.Pump(driver,true);
                Assert.AreNotSame(model,system.OutsideExploration);Assert.AreNotSame(runtime,system.OutsideRuntime);
                using(var defaults=new Olca.Core.Logic.Integration.OutsideExplorationSession()){
                    var fresh=system.OutsideExploration;Assert.AreEqual(defaults.Core.Position,fresh.Core.Position);
                    Assert.AreEqual(0,fresh.Core.Frame);Assert.AreEqual(0,system.OutsideRuntime.Frame);
                    Assert.AreEqual(defaults.Core.CardGauge.CurrentUnits,fresh.Core.CardGauge.CurrentUnits);
                    Assert.IsNull(fresh.Cards);Assert.IsNull(fresh.Camera.FixedCenter);Assert.IsFalse(fresh.IsPresented);
                    fresh.Core.TickFrame(-1,false,false);Assert.IsFalse(system.ResetOutsideForNewProgress(generation));
                    Assert.AreSame(fresh,system.OutsideExploration);Assert.AreEqual(1,fresh.Core.Frame);Assert.AreEqual(generation,system.OutsideProgressGeneration);
                }
            }
            system.Dispose();Assert.Throws<ObjectDisposedException>(()=>system.ResetOutsideForNewProgress(Guid.NewGuid().ToString("N")));
        }
        [Test] public void 通常host終了再開は新規進行と見なさず外界状態を保持する()
        {
            var model=system.OutsideExploration;var runtime=system.OutsideRuntime;model.Core.TickFrame(1,false,false);
            string path=Path.Combine(Path.GetTempPath(),"olca-outside-normal-"+Guid.NewGuid().ToString("N"));
            try{
                using(system.BeginSession(owner,path+"/settings",path+"/checkpoints",path+"/progress")){}
                using(system.BeginSession(new object(),path+"/settings",path+"/checkpoints",path+"/progress")){}
                Assert.AreSame(model,system.OutsideExploration);Assert.AreSame(runtime,system.OutsideRuntime);
                Assert.AreEqual(1,model.Core.Frame);Assert.IsFalse(model.IsEnded);Assert.IsNull(system.OutsideProgressGeneration);
            }finally{if(Directory.Exists(path))Directory.Delete(path,true);}
        }

        [Test] public void 外界の追いつき中に死亡したら同じPumpの残frameで移動を続けない()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();model.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(1));bool paused=Olca.Unity.GameplayPause.IsPaused;var pt=typeof(Olca.Unity.GameplayPause);var release=pt.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var resume=pt.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);var oldRelease=release.GetValue(null);var oldResume=resume.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);release.SetValue(null,false);resume.SetValue(null,0);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                var driver=new object();runtime.Attach(driver,input);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);long freq=System.Diagnostics.Stopwatch.Frequency,now=System.Diagnostics.Stopwatch.GetTimestamp()+freq*10;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);loop.AfterFrames(1,()=>model.Player.TakeDamage(1,affectsPoise:false));now+=freq/10;runtime.Pump(driver,true);Assert.IsTrue(model.Player.IsDead);Assert.AreEqual(1,model.Core.Frame);Assert.AreEqual(1,runtime.Frame);long x=model.Core.Position;now+=freq;runtime.Pump(driver,true);Assert.AreEqual(1,model.Core.Frame);Assert.AreEqual(x,model.Core.Position);
            }}finally{Olca.Unity.GameplayPause.SetPaused(paused);release.SetValue(null,oldRelease);resume.SetValue(null,oldResume);model.Dispose();}
        }
        [Test] public void 外界Runtimeは単一driverだけを許し解放後も同じRoot状態と時計を再利用する()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();
            using(var runtime=new OutsideExplorationRuntime(model))using(var input=new Olca.Unity.UnityInputProvider()){
                var first=new object();var second=new object();var core=model.Core;
                runtime.Attach(first,input);Assert.Throws<InvalidOperationException>(()=>runtime.Attach(second,input));
                runtime.Pump(second,true);Assert.AreEqual(0,runtime.Frame);Assert.AreEqual(0,core.Frame);
                runtime.Detach(second);Assert.Throws<InvalidOperationException>(()=>runtime.Attach(second,input));
                runtime.Detach(first);runtime.Attach(second,input);runtime.Pump(second,false);
                Assert.AreSame(core,model.Core);Assert.AreEqual(0,runtime.Frame);Assert.AreEqual(0,core.Frame);
                runtime.Dispose();Assert.Throws<ObjectDisposedException>(()=>runtime.Attach(first,input));
            }
        }

        [Test] public async Task 外界Pause被覆は状態を保ち背面Backと復帰同frameの旧操作を拒否する()
        {
            int returned=0,paused=0;
            Func<CancellationToken,Task<GameObject>> factory=ct=>Task.FromResult(Olca.Presentation.Exploration.OutsideExplorationView.Create(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),resources));
            using(var binding=new OutsideExplorationPresentationBinding(views,system.OutsideExploration,factory,()=>true,()=>returned++,paused:()=>paused++)){
                Assert.IsTrue((await binding.OpenAsync()).Succeeded);
                var back=binding.View.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseOutsideExploration");var pause=binding.View.GetComponentsInChildren<Button>().Single(b=>b.name=="PauseOutsideExploration");
                pause.onClick.Invoke();Assert.AreEqual(1,paused);binding.SetCovered(true);back.onClick.Invoke();pause.onClick.Invoke();Assert.AreEqual(0,returned);Assert.AreEqual(1,paused);Assert.IsTrue(binding.IsOpen);Assert.IsFalse(binding.View.CanPlay);
                binding.SetCovered(false);back.onClick.Invoke();Assert.AreEqual(0,returned);Assert.AreSame(back.gameObject,EventSystem.current.currentSelectedGameObject);Assert.AreEqual(0,system.OutsideExploration.Core.Frame);
            }
            Assert.IsFalse(system.OutsideExploration.IsPresented);
        }

        [Test] public async Task Pause一時制限は旧イベントも拒否し終了時に元の各ボタン状態だけを復元する()
        {
            var panel=Panel("restricted-pause");var resume=Button(panel,"ResumeButton");var load=Button(panel,"LoadCheckpointButton");resume.interactable=false;int loaded=0;load.onClick.AddListener(()=>loaded++);
            var view=panel.AddComponent<GameplayPauseView>();view.Initialize(()=>true);var layer=views.GetLayer("restriction-fixture");
            layer.Register(owner,"restricted",_=>Task.FromResult(panel),(go,ctx)=>view.Show(ctx),go=>view.Hide());
            Assert.IsTrue((await layer.ShowAsync("restricted")).Succeeded);var old=load.onClick;view.RestrictControls(new[]{"LoadCheckpointButton"});Assert.IsFalse(load.interactable);old.Invoke();Assert.AreEqual(0,loaded);
            layer.CloseCurrent();Assert.IsTrue(load.interactable);Assert.IsFalse(resume.interactable);old.Invoke();Assert.AreEqual(0,loaded);
            Assert.IsTrue((await layer.ShowAsync("restricted")).Succeeded);load.onClick.Invoke();Assert.AreEqual(1,loaded);Assert.IsFalse(resume.interactable);
        }

        [Test] public void 外界巡回は採用済み時刻だけを使い選択保持が移動neutralを妨げない()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();bool paused=Olca.Unity.GameplayPause.IsPaused;var pauseType=typeof(Olca.Unity.GameplayPause);var release=pauseType.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var resume=pauseType.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);var priorRelease=release.GetValue(null);var priorResume=resume.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);
            try{
                var source=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.SelectMany(System.Linq.Enumerable.Range(0,5),id=>System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,4),n=>new Olca.Core.Domain.Card("cycle-"+id,"fixture",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))));
                var owned=System.Linq.Enumerable.ToDictionary(System.Linq.Enumerable.GroupBy(source,c=>(c.Id,c.ExceedLevel)),g=>g.Key,g=>System.Linq.Enumerable.Count(g));Assert.IsTrue(model.TryInitializeCards(source,owned,new Olca.Tests.Mocks.MockRandomProvider(),out _));
                using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                    var driver=new object();runtime.Attach(driver,input);var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);
                    long frequency=System.Diagnostics.Stopwatch.Frequency,origin=System.Diagnostics.Stopwatch.GetTimestamp()+frequency*10,now=origin;typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);
                    var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",flags).GetValue(input);
                    var neutral=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,HasSelectionSample=true};
                    var selection=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,HasSelectionSample=true,AnyKey=true,AnyKeyExceptSelection=false,SelectionNextHeld=true,SelectionNextDown=true,SelectionNextAt=origin+frequency/20};
                    events.Add(new Olca.Unity.TimestampedLogicalInput(origin-frequency,selection));events.Add(new Olca.Unity.TimestampedLogicalInput(origin,neutral));events.Add(new Olca.Unity.TimestampedLogicalInput(origin+frequency*2,selection));
                    now=origin+frequency/10;runtime.Pump(driver,true);Assert.AreEqual(0,model.Cards.SelectedIndex,"旧時刻と未来の押下は未採用");
                    events.Add(new Olca.Unity.TimestampedLogicalInput(now,selection));now+=frequency/10;runtime.Pump(driver,true);Assert.AreEqual(1,model.Cards.SelectedIndex);
                    var moving=selection.Copy();moving.SelectionNextDown=false;moving.AnyKeyExceptSelection=true;moving.Horizontal=1;events.Add(new Olca.Unity.TimestampedLogicalInput(now,moving));now+=frequency/10;runtime.Pump(driver,true);Assert.Greater(model.Core.Position,model.Core.Definition.Start,"選択キーだけ保持中にも移動neutralは成立済み");Assert.AreEqual(runtime.Frame,model.Core.Frame);
                }
            }finally{Olca.Unity.GameplayPause.SetPaused(paused);release.SetValue(null,priorRelease);resume.SetValue(null,priorResume);model.Dispose();}
        }

        [Test] public void 外界入力epochは古い履歴を捨て未来neutralを採用前に評価しない()
        {
            var model=new Olca.Core.Logic.Integration.OutsideExplorationSession();bool paused=Olca.Unity.GameplayPause.IsPaused;var pauseType=typeof(Olca.Unity.GameplayPause);var release=pauseType.GetField("awaitRelease",BindingFlags.Static|BindingFlags.NonPublic);var resume=pauseType.GetField("resumeFrames",BindingFlags.Static|BindingFlags.NonPublic);var priorRelease=release.GetValue(null);var priorResume=resume.GetValue(null);Olca.Unity.GameplayPause.SetPaused(false);
            try{using(var input=new Olca.Unity.UnityInputProvider())using(var runtime=new OutsideExplorationRuntime(model))using(var lease=model.AttachPresentation()){
                var driver=new object();input.QueueSnapshot();runtime.Attach(driver,input);Assert.AreEqual(1,input.CaptureTimestamped().Length,"所有境界前の履歴を残さない");
                // 製品入力とは別の時刻列fixture。実入口はnative InputSystemで別途確認する。
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;var loop=(Olca.Unity.UnityLogicalLoop)typeof(OutsideExplorationRuntime).GetField("loop",flags).GetValue(runtime);
                long frequency=System.Diagnostics.Stopwatch.Frequency,origin=System.Diagnostics.Stopwatch.GetTimestamp()+frequency*10,now=origin;
                typeof(Olca.Unity.UnityLogicalLoop).GetField("readTimestamp",flags).SetValue(loop,(Func<long>)(()=>now));runtime.Pump(driver,true);
                var events=(List<Olca.Unity.TimestampedLogicalInput>)typeof(Olca.Unity.UnityInputProvider).GetField("events",flags).GetValue(input);
                var held=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true,AnyKey=true,Horizontal=1};var neutral=new Olca.Core.Logic.Integration.LogicalInput{HasInputSample=true};
                events.Add(new Olca.Unity.TimestampedLogicalInput(origin,held));events.Add(new Olca.Unity.TimestampedLogicalInput(origin+frequency*2,neutral));events.Add(new Olca.Unity.TimestampedLogicalInput(origin+frequency/20,held));
                now=origin+frequency/10;runtime.Pump(driver,true);Assert.Greater(model.Core.Frame,0);Assert.AreEqual(model.Core.Definition.Start,model.Core.Position,"未来neutralでheldを先に許可しない");Assert.AreEqual(runtime.Frame,model.Core.Frame);
                now=origin+frequency*2+frequency/10;runtime.Pump(driver,true);Assert.AreEqual(model.Core.Definition.Start,model.Core.Position);
                events.Add(new Olca.Unity.TimestampedLogicalInput(now,held));now+=frequency/10;runtime.Pump(driver,true);Assert.Greater(model.Core.Position,model.Core.Definition.Start);Assert.AreEqual(runtime.Frame,model.Core.Frame);
            }}finally{Olca.Unity.GameplayPause.SetPaused(paused);release.SetValue(null,priorRelease);resume.SetValue(null,priorResume);model.Dispose();}
        }

        [Test] public void Chrome遮蔽は背面描画とraycastだけを止め各元状態とactiveを復元する()
        {
            var chrome=new GameplayPanelFactory(host.transform,Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),null).Chrome();
            var original=chrome.Save.gameObject.AddComponent<CanvasGroup>();original.alpha=.4f;original.interactable=false;original.blocksRaycasts=true;original.enabled=false;
            bool collectionActive=chrome.Collection.gameObject.activeSelf;chrome.SetCovered(true);chrome.SetCovered(true);
            Assert.IsTrue(original.enabled);Assert.AreEqual(0,original.alpha);Assert.IsFalse(original.interactable);Assert.IsFalse(original.blocksRaycasts);Assert.AreEqual(collectionActive,chrome.Collection.gameObject.activeSelf);
            chrome.SetCovered(false);Assert.AreEqual(.4f,original.alpha);Assert.IsFalse(original.interactable);Assert.IsTrue(original.blocksRaycasts);Assert.IsFalse(original.enabled);Assert.AreEqual(collectionActive,chrome.Collection.gameObject.activeSelf);
            var added=chrome.Pause.GetComponent<CanvasGroup>();Assert.AreEqual(1,added.alpha);Assert.IsTrue(added.blocksRaycasts);Assert.IsTrue(added.interactable);chrome.SetCovered(false);
        }
    }
}
