using System;
using System.Threading;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Olca.Unity;
namespace Olca.Tests.Application
{
    public sealed class UIRuntimeManagementTests
    {
        // EditModeではOnEnableが自動実行されない。既存管理テストと同じ登録/解除経路を明示する。
        private static void Lifecycle(EventSystem value,string method) { if(value!=null)typeof(EventSystem).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null); }
        [Test] public void Prefabs_既存の投影不要UI設定と入力参照を保持する()
        {
            var ui=Resources.Load<GameObject>("OlcaSystem/UIRoot");Assert.NotNull(ui);
            var canvas=ui.GetComponent<Canvas>();Assert.AreEqual(RenderMode.ScreenSpaceOverlay,canvas.renderMode);Assert.AreEqual(32000,canvas.sortingOrder);
            var scaler=ui.GetComponent<CanvasScaler>();Assert.AreEqual(new Vector2(1920,1080),scaler.referenceResolution);Assert.AreEqual(.5f,scaler.matchWidthOrHeight);
            Assert.NotNull(ui.GetComponent<GraphicRaycaster>());
            var events=Resources.Load<GameObject>("OlcaSystem/EventSystem");Assert.NotNull(events.GetComponent<EventSystem>());
            var module=events.GetComponent<InputSystemUIInputModule>();Assert.NotNull(module.actionsAsset);Assert.NotNull(module.point.action);Assert.NotNull(module.leftClick.action);Assert.NotNull(module.submit.action);Assert.NotNull(module.cancel.action);
            var screen=Resources.Load<GameObject>("OlcaSystem/KeyboardSettings").GetComponent<RectTransform>();Assert.AreEqual(Vector2.zero,screen.anchorMin);Assert.AreEqual(Vector2.one,screen.anchorMax);
        }
        [Test] public void Content_再取得と所有拒否と解放後再生成を分離する()
        {
            var root=new GameObject("UIRuntimeTest");var resources=new ResourceManager();var ui=new UIRuntimeManager(resources,root.transform);var owner=new object();EventSystem ownedEvents=null;
            try {
                Assert.IsNull(EventSystem.current,"前テストのEventSystem残留を見逃さない");
                var first=ui.AcquireContent(owner);var firstId=first.GetInstanceID();Assert.AreSame(first,ui.AcquireContent(owner));Assert.Throws<InvalidOperationException>(()=>ui.AcquireContent(new object()));
                Assert.AreEqual(2,resources.OwnedInstanceCount);ownedEvents=root.GetComponentInChildren<EventSystem>();Lifecycle(ownedEvents,"OnEnable");EventSystem.current=ownedEvents;var eventId=EventSystem.current.GetInstanceID();var canvasId=first.GetComponentInParent<Canvas>().GetInstanceID();
                var button=new GameObject("Selected",typeof(RectTransform),typeof(Button));button.transform.SetParent(first.transform,false);EventSystem.current.SetSelectedGameObject(button);
                ui.ReleaseContent(new object());Assert.AreSame(button,EventSystem.current.currentSelectedGameObject);
                ui.ReleaseContent(owner);Assert.IsNull(EventSystem.current.currentSelectedGameObject);Assert.IsTrue(first==null);
                var second=ui.AcquireContent(new object());Assert.AreNotEqual(firstId,second.GetInstanceID());Assert.AreEqual(eventId,EventSystem.current.GetInstanceID());Assert.AreEqual(canvasId,second.GetComponentInParent<Canvas>().GetInstanceID());
                Lifecycle(ownedEvents,"OnDisable");ui.Dispose();Assert.AreEqual(0,resources.OwnedInstanceCount);
            } finally {Lifecycle(ownedEvents,"OnDisable");ui.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void Acquire_外部EventSystemを二重所有せず失敗する()
        {
            var root=new GameObject("ExternalEvents",typeof(EventSystem));var resources=new ResourceManager();var ui=new UIRuntimeManager(resources,root.transform);
            try {Lifecycle(root.GetComponent<EventSystem>(),"OnEnable");EventSystem.current=root.GetComponent<EventSystem>();Assert.Throws<InvalidOperationException>(()=>ui.AcquireContent(new object()));Assert.AreEqual(0,resources.OwnedInstanceCount);ui.Dispose();Assert.IsTrue(root!=null);Assert.AreSame(root.GetComponent<EventSystem>(),EventSystem.current);}
            finally {Lifecycle(root.GetComponent<EventSystem>(),"OnDisable");ui.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void Resource_欠損と事前取消は生成物を残さない()
        {
            using(var resources=new ResourceManager()){
                Assert.Throws<InvalidOperationException>(()=>resources.InstantiatePrefab("OlcaSystem/Missing",null));
                var cancel=new CancellationTokenSource();cancel.Cancel();Assert.CatchAsync<OperationCanceledException>(async()=>await resources.InstantiatePrefabAsync("OlcaSystem/UIRoot",null,cancel.Token));Assert.AreEqual(0,resources.OwnedInstanceCount);cancel.Dispose();
            }
        }
    }
}

namespace Olca.Tests.Application
{
    public sealed class OutsideKamuiWheelDialogTests
    {
        private sealed class Owner:Olca.Presentation.Management.IDialogOwner {public bool Alive=true;public bool IsCurrent=>Alive;}
        private sealed class Fixture:System.IDisposable
        {
            public readonly GameObject Root=new GameObject("WheelFixture",typeof(RectTransform));
            public readonly Olca.Presentation.Management.DialogManager Dialogs=new Olca.Presentation.Management.DialogManager(()=>17);
            public readonly Owner Parent=new Owner();public readonly Olca.Presentation.Management.OutsideKamuiWheelDialog View;
            public bool Allowed=true,Paused;public int PauseTransitions;
            public Fixture(){var panel=new GameObject("Wheel",typeof(RectTransform));panel.transform.SetParent(Root.transform,false);View=panel.AddComponent<Olca.Presentation.Management.OutsideKamuiWheelDialog>();View.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));}
            public bool Open(bool confirm=false,Vector2 direction=default)=>View.Open(Dialogs,Parent,()=>Parent.Alive,()=>Allowed,value=>{Paused=value;PauseTransitions++;},confirm,direction);
            public void Dispose(){try{if(View!=null)View.Abort();Dialogs.Dispose();}finally{UnityEngine.Object.DestroyImmediate(Root);}}
        }
        private static Olca.Unity.UnityInputProvider.OutsidePcWheelInput Pc(Vector2 direction=default,bool keyChanged=false,bool pointerChanged=false,long keyOrder=0,long pointerOrder=0,bool enter=false,bool click=false,bool toggle=false,bool available=true,Vector2 pointer=default)
            =>new Olca.Unity.UnityInputProvider.OutsidePcWheelInput(available,toggle,enter,click,direction,pointer,true,keyChanged,pointerChanged,keyOrder,pointerOrder);
        private static bool OpenPc(Fixture f,Olca.Unity.UnityInputProvider.OutsidePcWheelInput input)=>f.View.OpenPc(f.Dialogs,f.Parent,()=>f.Parent.Alive,()=>f.Allowed,value=>{f.Paused=value;f.PauseTransitions++;},input);
        [Test] public void PCホイールは初期hoverと既保持方向を表示しTab解放では閉じず二度目だけ取消す()
        {
            using(var f=new Fixture()){
                Assert.IsTrue(OpenPc(f,Pc()));Assert.IsTrue(f.View.HasSelection,"項目上の初期pointerを読む");f.View.TickPc(Pc());Assert.IsTrue(f.View.IsOpen,"トグルキーの解放は閉じない");
                f.View.TickPc(Pc(toggle:true));Assert.IsFalse(f.View.IsOpen);Assert.IsFalse(f.Paused);Assert.AreEqual(Olca.Presentation.Management.DialogResult.Cancelled,f.View.LastResult);
            }
            using(var f=new Fixture()){Assert.IsTrue(OpenPc(f,Pc(direction:Vector2.up,pointer:new Vector2(10000,10000))));Assert.IsTrue(f.View.HasSelection,"既保持WASDは初期hoverより優先");}
        }
        [Test] public void PCホイールは方向全解放で未選択を維持しEnterを拒否して新たな項目クリックで確定する()
        {
            using(var f=new Fixture()){
                Assert.IsTrue(OpenPc(f,Pc(direction:Vector2.up)));f.View.TickPc(Pc(keyChanged:true,keyOrder:2));Assert.IsFalse(f.View.HasSelection);
                f.View.TickPc(Pc(enter:true));Assert.IsTrue(f.View.IsOpen);Assert.IsFalse(f.View.HasSelection,"静止hoverでは復帰しない");
                f.View.TickPc(Pc(click:true,pointer:new Vector2(10000,10000)));Assert.IsTrue(f.View.IsOpen,"未選択領域のクリックは無効");
                f.View.TickPc(Pc(click:true));Assert.IsFalse(f.View.IsOpen);Assert.AreEqual(Olca.Presentation.Management.DialogResult.Confirmed,f.View.LastResult);Assert.IsTrue(f.Dialogs.Blocks(f.Parent));Assert.AreEqual(2,f.PauseTransitions);
            }
        }
        [Test] public void PCホイールは最後の操作側だけ選択し不許可では開いたまま所有keyboard消失で返す()
        {
            using(var f=new Fixture()){
                f.Allowed=false;Assert.IsTrue(OpenPc(f,Pc()));f.View.TickPc(Pc(direction:Vector2.zero,keyChanged:true,pointerChanged:true,keyOrder:4,pointerOrder:3));Assert.IsFalse(f.View.HasSelection);
                f.View.TickPc(Pc(direction:Vector2.zero,keyChanged:true,pointerChanged:true,keyOrder:5,pointerOrder:6));Assert.IsTrue(f.View.HasSelection);f.View.TickPc(Pc(enter:true));Assert.IsTrue(f.View.IsOpen);Assert.IsTrue(f.Paused);
                f.View.TickPc(Pc(available:false));Assert.IsFalse(f.View.IsOpen);Assert.IsFalse(f.Paused);Assert.AreEqual(0,f.Dialogs.ActiveCount);int transitions=f.PauseTransitions;f.View.Abort();Assert.AreEqual(transitions,f.PauseTransitions);
            }
        }
        [TestCase(true)][TestCase(false)] public void PCホイールは静止選択でも最新の切替可否を確定時に再判定する(bool initiallyAllowed)
        {
            using(var f=new Fixture()){
                f.Allowed=initiallyAllowed;Assert.IsTrue(OpenPc(f,Pc(direction:Vector2.up)));f.Allowed=!initiallyAllowed;
                f.View.TickPc(Pc(enter:true));
                if(initiallyAllowed){Assert.IsTrue(f.View.IsOpen);Assert.IsTrue(f.Paused);f.Allowed=true;f.View.TickPc(Pc());f.View.TickPc(Pc(enter:true));}
                Assert.IsFalse(f.View.IsOpen);Assert.IsFalse(f.Paused);Assert.AreEqual(Olca.Presentation.Management.DialogResult.Confirmed,f.View.LastResult);
            }
        }
        [Test] public void ホイールは中央未選択と開く前A保持を守り初期stickを即選択して新Aだけで同状態を閉じる()
        {
            using(var f=new Fixture()){
                Assert.NotNull(f.View.GetComponentInChildren<Olca.Presentation.Management.OutsideKamuiWheelRing>(true).GetComponent<CanvasRenderer>(),"Nativeで欠落した描画依存を実在させる");
                Assert.IsTrue(f.Open(true,Vector2.right));Assert.IsTrue(f.Paused);Assert.IsTrue(f.View.HasSelection);f.View.Tick(true,true,Vector2.right);Assert.IsTrue(f.View.IsOpen,"既に保持したAは確定しない");
                f.View.Tick(true,false,Vector2.zero);Assert.IsFalse(f.View.HasSelection);f.View.Tick(true,true,Vector2.zero);Assert.IsTrue(f.View.IsOpen);f.View.Tick(true,false,Vector2.right);f.View.Tick(true,true,Vector2.right);
                Assert.IsFalse(f.View.IsOpen);Assert.IsFalse(f.Paused);Assert.AreEqual(Olca.Presentation.Management.DialogResult.Confirmed,f.View.LastResult);Assert.AreEqual(2,f.PauseTransitions);Assert.IsTrue(f.Dialogs.Blocks(f.Parent),"閉鎖frameの親操作を遮断する");
            }
        }
        [Test] public void ホイールは不許可のAで閉じずLB解放だけで取消しPauseを返す()
        {
            using(var f=new Fixture()){
                f.Allowed=false;Assert.IsTrue(f.Open(false,Vector2.left));f.View.Tick(true,true,Vector2.left);Assert.IsTrue(f.View.IsOpen);Assert.IsTrue(f.Paused);
                f.View.Tick(false,false,Vector2.left);Assert.IsFalse(f.View.IsOpen);Assert.IsFalse(f.Paused);Assert.AreEqual(Olca.Presentation.Management.DialogResult.Cancelled,f.View.LastResult);
            }
        }
        [Test] public void ホイールは旧確定callbackを次表示へ流さず親失効で一度だけPauseを解放する()
        {
            using(var f=new Fixture()){
                Assert.IsTrue(f.Open(false,Vector2.up));var button=f.View.transform.Find("KamuiWheelNormal").GetComponent<Button>();var old=button.onClick;f.View.Tick(false,false,Vector2.zero);Assert.IsTrue(f.Open(false,Vector2.right));old.Invoke();Assert.IsTrue(f.View.IsOpen);
                f.Parent.Alive=false;f.View.Tick(true,true,Vector2.right);Assert.IsFalse(f.View.IsOpen);Assert.IsFalse(f.Paused);Assert.AreEqual(0,f.Dialogs.ActiveCount);int transitions=f.PauseTransitions;f.View.Abort();Assert.AreEqual(transitions,f.PauseTransitions);
            }
        }
        [TestCase(false)][TestCase(true)] public void ホイールは外部非表示か破棄でもDialog所有とPauseを解放する(bool destroy)
        {
            using(var f=new Fixture()){
                Assert.IsTrue(f.Open(false,Vector2.up));
                // EditModeの動的AddComponentはEngineのAwake済フラグに依存させない。Native自然通知は別検証。
                typeof(Olca.Presentation.Management.OutsideKamuiWheelDialog).GetMethod(destroy?"OnDestroy":"OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(f.View,null);
                if(destroy)UnityEngine.Object.DestroyImmediate(f.View.gameObject);else f.View.gameObject.SetActive(false);f.Dialogs.Tick();Assert.AreEqual(0,f.Dialogs.ActiveCount);Assert.IsFalse(f.Paused);Assert.AreEqual(2,f.PauseTransitions);
            }
        }
        [Test] public void ホイールの文脈再入は既保持を初回sampleへ同期し解放後の新押下だけを採用する()
        {
            var root=new GameObject("WheelControllerLatch");root.SetActive(false);var c=root.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();
            try{
                var method=typeof(Olca.Application.Dungeon.DungeonBattleController).GetMethod("SampleOutsideWheelOpen",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                bool Sample(object context,bool held)=>(bool)method.Invoke(c,new object[]{context,held});var a=new object();var b=new object();
                Assert.IsFalse(Sample(a,true));Assert.IsFalse(Sample(a,true));Assert.IsFalse(Sample(a,false));Assert.IsTrue(Sample(a,true));Assert.IsFalse(Sample(a,true));
                Assert.IsFalse(Sample(b,true));Assert.IsFalse(Sample(b,false));Assert.IsTrue(Sample(b,true));Assert.IsFalse(Sample(null,true));Assert.IsFalse(Sample(a,true));Assert.IsFalse(Sample(a,false));Assert.IsTrue(Sample(a,true));
            }finally{c.StopManagedHost();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void ホイールは他Dialogの所有を奪わずbusy時Pauseを増やさない()
        {
            using(var f=new Fixture()){
                var other=new GameObject("OtherDialog",typeof(RectTransform));other.transform.SetParent(f.Root.transform,false);var close=other.AddComponent<Button>();var owner=new Owner();f.Dialogs.ShowNotice(owner,other,close,null,_=>{},manageMenu:false);Assert.IsFalse(f.Open());Assert.IsFalse(f.Paused);Assert.AreEqual(0,f.PauseTransitions);Assert.AreEqual(1,f.Dialogs.ActiveCount);f.Dialogs.CancelOwner(owner);Assert.IsTrue(f.Open());
            }
        }
    }
}

namespace Olca.Tests.Application
{
    public sealed class BootstrapTitleRouteTests
    {
        [Test] public void タイトルは開始中の二重決定と閉じた旧callbackを受けず管理所有を返す()
        {
            var root=new GameObject("TitleRouteFixture",typeof(RectTransform));var resources=new ResourceManager();var dialogs=new Olca.Presentation.Management.DialogManager(()=>1);var views=new Olca.Presentation.Management.ViewManager(resources,dialogs);var owner=new object();Olca.Presentation.Management.BootstrapTitleView title=null;int starts=0,cancelled=0;
            try{
                views.Register(owner,"title",ct=>System.Threading.Tasks.Task.FromResult(Olca.Presentation.Management.BootstrapTitleView.Create(root.transform,resources)),(panel,context)=>{title=panel.GetComponent<Olca.Presentation.Management.BootstrapTitleView>();title.Show(context,()=>{starts++;title.SetBusy(true,null);},()=>cancelled++);},panel=>panel.GetComponent<Olca.Presentation.Management.BootstrapTitleView>().Hide());
                Assert.IsTrue(views.ShowAsync("title").GetAwaiter().GetResult().Succeeded);var button=title.transform.Find("TitleStart").GetComponent<Button>();var old=button.onClick;old.Invoke();old.Invoke();Assert.AreEqual(1,starts);Assert.IsFalse(button.interactable);
                title.SetBusy(false,"再試行");button.onClick.Invoke();Assert.AreEqual(2,starts);views.CloseCurrent();Assert.AreEqual(1,cancelled,"未完了loadを閉鎖で取消へ渡す");old.Invoke();Assert.AreEqual(2,starts);Assert.IsTrue(views.ShowAsync("title").GetAwaiter().GetResult().Succeeded);old.Invoke();Assert.AreEqual(2,starts,"再表示でも旧callbackは開始しない");title.transform.Find("TitleStart").GetComponent<Button>().onClick.Invoke();Assert.AreEqual(3,starts);views.CloseCurrent();Assert.AreEqual(2,cancelled);views.UnregisterOwner(owner);Assert.IsTrue(title==null);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }finally{views.Dispose();dialogs.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void タイトルは新規と継続を分け保存なし継続と読込中と旧callbackを拒否する()
        {
            var root=new GameObject("TitleChoices",typeof(RectTransform));var resources=new ResourceManager();int frame=1;var dialogs=new Olca.Presentation.Management.DialogManager(()=>frame);var views=new Olca.Presentation.Management.ViewManager(resources,dialogs);Olca.Presentation.Management.BootstrapTitleView title=null;int creates=0,resumes=0;
            try{
                views.Register(this,"title",ct=>System.Threading.Tasks.Task.FromResult(Olca.Presentation.Management.BootstrapTitleView.Create(root.transform,resources)),(p,c)=>{title=p.GetComponent<Olca.Presentation.Management.BootstrapTitleView>();title.Show(c,()=>resumes++,()=>creates++,false,true,null);},p=>p.GetComponent<Olca.Presentation.Management.BootstrapTitleView>().Hide());
                Assert.IsTrue(views.ShowAsync("title").GetAwaiter().GetResult().Succeeded);var resume=title.transform.Find("TitleStart").GetComponent<Button>();var create=title.transform.Find("TitleNewGame").GetComponent<Button>();Assert.AreEqual("つづきから",resume.GetComponentInChildren<Text>().text);Assert.AreEqual("はじめから",create.GetComponentInChildren<Text>().text);Assert.IsFalse(resume.interactable);resume.onClick.Invoke();Assert.AreEqual(0,resumes);create.onClick.Invoke();Assert.AreEqual(1,creates);
                var old=create.onClick;title.SetBusy(true,null);old.Invoke();Assert.AreEqual(1,creates);title.SetContinueAvailable();Assert.IsFalse(resume.interactable);title.SetBusy(false,"起動失敗");resume.onClick.Invoke();Assert.AreEqual(1,resumes,"新規保存成功後の起動失敗は確定済み保存を継続できる");views.CloseCurrent();old.Invoke();Assert.AreEqual(1,creates);Assert.IsTrue(views.ShowAsync("title").GetAwaiter().GetResult().Succeeded);old.Invoke();Assert.AreEqual(1,creates);
            }finally{views.Dispose();dialogs.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        private sealed class TitleFailIo:CheckpointFileIo
        {
            public bool Fail;
            public override void WriteAndFlush(string path,byte[] bytes){if(Fail)throw new System.IO.IOException("title save fixture");base.WriteAndFlush(path,bytes);}
        }
        [Test] public void タイトル新規確認は取消で保存不変かつ失敗後同一計画を再試行し旧進行を保全する()
        {
            string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"OlcaTitle194-"+Guid.NewGuid().ToString("N"));var io=new TitleFailIo();var store=new ClearReturnFileStore(directory,io);var session=new Olca.Core.Domain.ClearReturnSession(store);Assert.IsTrue(store.IsNewProfile);Assert.IsTrue(session.Ready);Assert.IsFalse(System.IO.Directory.Exists(directory),"タイトルの読取は未作成保存を書かない");
            var prefab=Resources.Load<GameObject>(Olca.Application.Management.BootstrapEntry.GameplayHostPrefabPath);var controller=prefab.GetComponentInChildren<Olca.Application.Dungeon.DungeonBattleController>(true);var starter=controller.CreateNewGameStarterCards();Assert.IsTrue(session.InitializeStarter(starter));byte[] before=System.IO.File.ReadAllBytes(store.ActivePath);string generation=session.Snapshot.CheckpointGeneration;int made=0,saved=0,frame=1;
            var root=new GameObject("TitleSaveChoices",typeof(RectTransform));var resources=new ResourceManager();var dialogs=new Olca.Presentation.Management.DialogManager(()=>frame);var views=new Olca.Presentation.Management.ViewManager(resources,dialogs);Olca.Presentation.Management.BootstrapTitleView title=null;Olca.Application.Management.ProgressActionOperations operations=null;
            try{
                views.Register(this,"title",ct=>System.Threading.Tasks.Task.FromResult(Olca.Presentation.Management.BootstrapTitleView.Create(root.transform,resources)),(p,c)=>{title=p.GetComponent<Olca.Presentation.Management.BootstrapTitleView>();operations=new Olca.Application.Management.ProgressActionOperations(session,()=>c.IsCurrent,()=>{made++;return starter;},()=>c.IsCurrent);title.Show(c,()=>{},()=>title.OpenNewGame(operations,dialogs,()=>saved++),true,true,null);},p=>p.GetComponent<Olca.Presentation.Management.BootstrapTitleView>().Hide());
                Assert.IsTrue(views.ShowAsync("title").GetAwaiter().GetResult().Succeeded);var create=title.transform.Find("TitleNewGame").GetComponent<Button>();create.onClick.Invoke();var popup=title.GetComponentInChildren<Olca.Presentation.Management.ProgressActionDialog>(true);Assert.Less(popup.GetComponent<Image>().color.r,.1f);Assert.AreEqual(1f,popup.GetComponent<Image>().color.a,"背面のタイトル文字を透かさない");Assert.AreEqual(Color.white,popup.Message.color);Assert.AreEqual(38,popup.Message.fontSize);var stale=popup.ConfirmButton.onClick;popup.CancelButton.onClick.Invoke();CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(store.ActivePath));Assert.AreEqual(0,made);Assert.AreEqual(0,saved);stale.Invoke();Assert.AreEqual(0,made);
                frame++;create.onClick.Invoke();io.Fail=true;popup.ConfirmButton.onClick.Invoke();Assert.IsTrue(session.HasPendingSave);Assert.AreEqual(1,made);Assert.IsFalse(popup.CancelButton.interactable);CollectionAssert.AreEqual(before,System.IO.File.ReadAllBytes(store.ActivePath));popup.ConfirmButton.onClick.Invoke();Assert.AreEqual(1,made,"同frame二重決定で新計画を作らない");frame++;io.Fail=false;popup.ConfirmButton.onClick.Invoke();Assert.AreEqual(1,made,"再試行もスターターを再生成しない");Assert.AreEqual(1,saved);Assert.IsFalse(session.HasPendingSave);Assert.AreNotEqual(generation,session.Snapshot.CheckpointGeneration);Assert.AreEqual(20,session.Snapshot.Owned.Sum(c=>c.Count));Assert.IsTrue(System.IO.Directory.GetFiles(directory,"*",System.IO.SearchOption.AllDirectories).Any(f=>System.IO.File.ReadAllBytes(f).SequenceEqual(before)),"旧進行文書の復旧用bytesが残る");var fresh=new Olca.Core.Domain.ClearReturnSession(new ClearReturnFileStore(directory));Assert.IsTrue(fresh.Ready);Assert.AreEqual(session.Snapshot.CheckpointGeneration,fresh.Snapshot.CheckpointGeneration);views.CloseCurrent();stale.Invoke();Assert.AreEqual(1,saved);
            }finally{views.Dispose();dialogs.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);if(System.IO.Directory.Exists(directory))System.IO.Directory.Delete(directory,true);}
        }
        [Test] public void 外界決定は初回中立sample後の最初の新押下を採用し文脈再入の既保持を捨てる()
        {
            var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            try{using(var input=new UnityInputProvider()){
                input.GamepadContext=()=>"outside";input.GamepadGameplayActive=()=>true;input.StartEventCapture();
                void State(UnityEngine.InputSystem.LowLevel.GamepadState value){UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,value);UnityEngine.InputSystem.InputSystem.Update();}
                State(new UnityEngine.InputSystem.LowLevel.GamepadState());var a=new object();Assert.IsFalse(input.OutsideInteractionPressed(a));
                State(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.East));Assert.IsTrue(input.OutsideInteractionPressed(a),"近接前にsample済みなら最初の決定を捨てない");Assert.IsFalse(input.OutsideInteractionPressed(a),"同edgeは一回だけ消費");
                var b=new object();Assert.IsFalse(input.OutsideInteractionPressed(b),"再入時の既保持は新決定にしない");State(new UnityEngine.InputSystem.LowLevel.GamepadState());Assert.IsFalse(input.OutsideInteractionPressed(b));
                State(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.East));Assert.IsTrue(input.OutsideInteractionPressed(b));Assert.IsFalse(input.OutsideInteractionPressed(b));input.QueueSnapshot();Assert.IsFalse(input.OutsideInteractionPressed(b),"snapshotの保持状態は新決定にしない");
            }}finally{UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState());UnityEngine.InputSystem.InputSystem.Update();UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);}
        }
        [Test] public void 入口の目印は空中も残し実行可能時だけ現在キーを案内する()
        {
            var root=new GameObject("EntranceHintTest",typeof(RectTransform));try{
                var view=root.AddComponent<Olca.Presentation.Exploration.OutsideExplorationView>();var buttonObject=new GameObject("OutsideDungeonEntrance",typeof(RectTransform),typeof(Image),typeof(Button));buttonObject.transform.SetParent(root.transform,false);var button=buttonObject.GetComponent<Button>();button.targetGraphic=buttonObject.GetComponent<Image>();
                var hintObject=new GameObject("OutsideEntranceActionHint",typeof(RectTransform),typeof(Text));hintObject.transform.SetParent(buttonObject.transform,false);var hint=hintObject.GetComponent<Text>();
                var type=view.GetType();type.GetField("entrance",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,button);type.GetField("entranceActionHint",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,hint);var render=type.GetMethod("RenderEntranceHint",BindingFlags.Instance|BindingFlags.NonPublic);
                void Draw(bool visible,bool available,bool ground,string key)=>render.Invoke(view,new object[]{visible,available,ground,key});
                Draw(true,true,true,"Enter");Assert.IsTrue(buttonObject.activeSelf);Assert.IsTrue(button.interactable);Assert.IsTrue(hintObject.activeSelf);Assert.AreEqual("[Enter] シラベル",hint.text);
                Draw(true,false,false,"Enter");Assert.IsTrue(buttonObject.activeSelf);Assert.IsFalse(button.interactable);Assert.IsFalse(button.image.raycastTarget);Assert.IsFalse(button.image.enabled);Assert.IsFalse(hintObject.activeSelf);Assert.IsEmpty(hint.text);
                Draw(true,false,true,"Enter");Assert.IsTrue(buttonObject.activeSelf);Assert.IsTrue(button.image.raycastTarget);Assert.IsTrue(button.image.enabled);Assert.IsFalse(hintObject.activeSelf);
                Draw(true,true,true,"B / ○");Assert.AreEqual("[B / ○] シラベル",hint.text);Draw(true,true,true,"X / □");Assert.AreEqual("[X / □] シラベル",hint.text);
                Draw(true,true,true,"ミワリアテ");Assert.IsFalse(hintObject.activeSelf);Draw(false,false,true,"Enter");Assert.IsFalse(buttonObject.activeSelf);Draw(true,true,true,"Enter");Assert.IsTrue(hintObject.activeSelf);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }

        [Test] public void 近接入口は通常移動の範囲だけで接地を要求し空中ではJump入力を奪わない()
        {
            var core=new Olca.Core.Logic.Integration.OutsideExplorationCore(Olca.Core.Logic.Integration.OutsideMovementDefinition.ValidationTerrain,Olca.Core.Logic.Integration.OutsideTraversalProfile.ValidationDash);
            Assert.IsFalse(Olca.Core.Logic.Integration.OutsideTrialRoute.CanInspect(core));
            for(int i=0;i<120;i++)core.TickFrame(1,false,false,false,false);
            Assert.IsTrue(Olca.Core.Logic.Integration.OutsideTrialRoute.CanInspect(core));core.TickFrame(0,true,true,false,false);Assert.IsFalse(core.IsGrounded);Assert.IsFalse(Olca.Core.Logic.Integration.OutsideTrialRoute.CanInspect(core));
        }
    }
}

namespace Olca.Tests.Application
{
    public sealed class OutsideItemRegistrationTests
    {
        private sealed class Store:Olca.Core.Domain.IClearReturnStore
        {
            public bool Fail;public Olca.Core.Domain.ClearReturnProfile Value=new Olca.Core.Domain.ClearReturnProfile{OutsideInventory=new Olca.Core.Domain.OutsideInventoryData{Items=new[]{new Olca.Core.Domain.OutsideItemCount{Id=Olca.Core.Domain.OutsideItemDisplayDefinition.RecoverySample.Id,Count=0}}}};
            public Olca.Core.Domain.ClearReturnProfile Read(out string error){error=null;return Value.Copy();}
            public bool TryWrite(long expected,Olca.Core.Domain.ClearReturnProfile value,out string error){error=Fail?"fixture save failure":null;if(Fail)return false;if(Value.Revision!=expected)return false;Value=value.Copy();return true;}
        }
        [Test] public async System.Threading.Tasks.Task 登録Viewは旧描画と旧世代callbackを無効化し0個の登録と失敗再試行を守る()
        {
            var root=new GameObject("InventoryViewFixture",typeof(RectTransform));var resources=new ResourceManager();var dialogs=new Olca.Presentation.Management.DialogManager(()=>17);var views=new Olca.Presentation.Management.ViewManager(resources,dialogs);var store=new Store();var session=new Olca.Core.Domain.ClearReturnSession(store);Olca.Presentation.Management.OutsideItemRegistrationView view=null;
            try{
                views.Register(this,"items",ct=>{var panel=new GameObject("Items",typeof(RectTransform));panel.transform.SetParent(root.transform,false);view=panel.AddComponent<Olca.Presentation.Management.OutsideItemRegistrationView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),session,new[]{Olca.Core.Domain.OutsideItemDisplayDefinition.RecoverySample},()=>views.CloseCurrent());return System.Threading.Tasks.Task.FromResult(panel);},(p,c)=>view.Open(c),p=>p.GetComponent<Olca.Presentation.Management.OutsideItemRegistrationView>().Hide());
                Assert.IsTrue((await views.ShowAsync("items")).Succeeded);Button B(string name)=>view.GetComponentsInChildren<Button>().Single(b=>b.name==name);var old=B("RegisterOutsideItem_0").onClick;B("OutsideItemSlot_1").onClick.Invoke();old.Invoke();Assert.AreEqual(0,session.InventoryRevision,"旧描画が新slotへ作用しない");store.Fail=true;B("RegisterOutsideItem_0").onClick.Invoke();Assert.IsTrue(session.HasPendingSave);Assert.IsNull(session.OutsideInventorySnapshot.Shortcuts[1]);Assert.IsTrue(B("RetryOutsideItemsSave").interactable);store.Fail=false;B("RetryOutsideItemsSave").onClick.Invoke();Assert.AreEqual(Olca.Core.Domain.OutsideItemDisplayDefinition.RecoverySample.Id,session.OutsideInventorySnapshot.Shortcuts[1]);Assert.AreEqual(0,session.OutsideInventorySnapshot.Items[0].Count);Assert.AreEqual(1,session.InventoryRevision);var previous=B("RegisterOutsideItem_0").onClick;views.CloseCurrent();previous.Invoke();Assert.AreEqual(1,session.InventoryRevision);Assert.IsTrue((await views.ShowAsync("items")).Succeeded);previous.Invoke();Assert.AreEqual(1,session.InventoryRevision);Assert.AreEqual(Olca.Core.Domain.OutsideItemDisplayDefinition.RecoverySample.Id,session.OutsideInventorySnapshot.Shortcuts[1]);
            }finally{views.Dispose();dialogs.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
