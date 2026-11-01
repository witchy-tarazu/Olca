using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Olca.Presentation.Dungeon;

namespace Olca.Presentation.Management
{
    /// <summary>pauseの表示寿命だけを所有する。時間停止・保存・進行はControllerが判断する。</summary>
    public sealed class GameplayPauseView : MonoBehaviour
    {
        private ManagedScreenContext context;
        private Func<bool> inputAllowed;
        private readonly Dictionary<Button,Button.ButtonClickedEvent> actions=new Dictionary<Button,Button.ButtonClickedEvent>();
        private bool covered;
        private CanvasGroup presentation;
        private GameObject previousFocus;
        private string restoredFocus;
        public string TakeRestoredFocus(){var value=restoredFocus;restoredFocus=null;return value;}
        public ManagedScreenContext Context=>context;
        public bool Covered=>covered;
        public bool BlocksInput=>context?.DialogBlocksInput==true;
        public void Initialize(Func<bool> allowed)
        {
            inputAllowed=allowed;presentation=gameObject.AddComponent<CanvasGroup>();
            foreach(var button in GetComponentsInChildren<Button>(true))actions.Add(button,button.onClick);
        }
        public void Show(ManagedScreenContext owner)
        {
            context=owner;covered=false;restoredFocus=null;presentation.alpha=1;presentation.blocksRaycasts=true;
            foreach(var pair in actions){
                var captured=owner;var action=pair.Value;var button=pair.Key;
                button.onClick=new Button.ButtonClickedEvent();
                button.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&!captured.DialogBlocksInput&&(inputAllowed?.Invoke()??false))action.Invoke();});
            }
            gameObject.SetActive(true);transform.SetAsLastSibling();
        }
        public void Hide(){context=null;covered=false;previousFocus=null;restoredFocus=null;presentation.alpha=1;presentation.blocksRaycasts=true;gameObject.SetActive(false);}
        public void CoverForDialog()
        {
            previousFocus=EventSystem.current?.currentSelectedGameObject;covered=true;presentation.alpha=0;presentation.blocksRaycasts=false;
        }
        public void RestoreAfterDialog()
        {
            if(context?.IsCurrent!=true)return;
            covered=false;presentation.alpha=1;presentation.blocksRaycasts=true;gameObject.SetActive(true);transform.SetAsLastSibling();
            var focus=previousFocus;previousFocus=null;
            if(focus!=null&&focus.activeInHierarchy&&focus.GetComponent<Selectable>()?.IsInteractable()==true){EventSystem.current?.SetSelectedGameObject(focus);restoredFocus=focus.name;}
        }
        private void OnDisable(){context?.NotifyHidden();context=null;}
        private void OnDestroy(){context?.NotifyHidden();context=null;inputAllowed=null;previousFocus=null;actions.Clear();}
    }
}

namespace Olca.Presentation.Management
{
    /// <summary>host表示の組立。進行・保存・入力判定は注入側が所有する。</summary>
    public sealed class GameplayPanelFactory
    {
        public Font Font { get; }
        public void Feedback(string name)=>sound?.Invoke(name);
        private readonly Transform parent;
        private readonly Action<string> sound;
        public GameplayPanelFactory(Transform parent,Font font,Action<string> sound){this.parent=parent;Font=font;this.sound=sound;}
        public sealed class ChromeBinding
        {
            public Button Pause,Save,NewGame,Collection,Loadout,Unlock,Acquisition;
            public Text Currency;
        }
        public static GameObject LegacyCanvas()
        {
            var panel=new GameObject("GameplayPauseCanvas",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
            var canvas=panel.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32000;
            var scaler=panel.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);scaler.matchWidthOrHeight=.5f;return panel;
        }
        public GameObject Template(GameObject template,string name,Action<GameObject> bind)
        {
            var panel=template!=null?UnityEngine.Object.Instantiate(template):new GameObject(name,typeof(RectTransform));panel.name=name;
            try{panel.transform.SetParent(parent,false);bind(panel);return panel;}
            catch{if(UnityEngine.Application.isPlaying)UnityEngine.Object.Destroy(panel);else UnityEngine.Object.DestroyImmediate(panel);throw;}
        }
        public ChromeBinding Chrome()
        {
            var chrome=new ChromeBinding();
            chrome.Pause=Button("PauseButton","ポーズ",parent,Vector2.one,new Vector2(-132,-64));
            chrome.Currency=Label("CurrencyBalance",parent,Vector2.one,new Vector2(-182,-125),new Vector2(300,45));chrome.Currency.alignment=TextAnchor.MiddleRight;chrome.Currency.fontSize=26;chrome.Currency.color=Color.white;
            chrome.Save=Button("ManualSaveButton","保存（スロット1）",parent,new Vector2(0,1),new Vector2(260,-85));
            chrome.NewGame=Button("NewGameButton","はじめから",parent,new Vector2(0,1),new Vector2(580,-85));
            foreach(var button in new[]{chrome.Save,chrome.NewGame})((RectTransform)button.transform).sizeDelta=new Vector2(280,60);
            chrome.Collection=Button("CardCollectionButton","カード図鑑",parent,Vector2.one,new Vector2(-402,-64));
            ((RectTransform)chrome.Collection.transform).sizeDelta=new Vector2(300,60);
            var label=chrome.Collection.GetComponentInChildren<Text>();label.fontSize=label.resizeTextMaxSize=26;label.rectTransform.offsetMin=new Vector2(16,4);label.rectTransform.offsetMax=new Vector2(-16,-4);
            chrome.Loadout=Button("OpenCustomizationEditor","ソウビ・パーツ",parent,Vector2.one,new Vector2(-310,-160));
            chrome.Unlock=Button("OpenCustomizationUnlocks","マスカイホウ",parent,Vector2.one,new Vector2(-310,-235));
            chrome.Acquisition=Button("OpenCustomizationAcquisitions","ソウビのウケトリ",parent,Vector2.one,new Vector2(-310,-310));
            foreach(var button in new[]{chrome.Collection,chrome.Loadout,chrome.Unlock,chrome.Acquisition}){((RectTransform)button.transform).sizeDelta=new Vector2(300,60);button.gameObject.SetActive(false);}return chrome;
        }
        public GameObject Panel(string name,bool surface=false)
        {
            var panel=surface?new GameObject(name,typeof(RectTransform),typeof(Image)):new GameObject(name,typeof(RectTransform));
            panel.SetActive(false);panel.transform.SetParent(parent,false);
            var rect=(RectTransform)panel.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            if(surface)panel.GetComponent<Image>().color=UiLook.Surface;
            return panel;
        }
        public T Component<T>(string name,Action<T> bind) where T:Component
        {
            // Initialize失敗時も、親contentに途中生成物を残さない。
            var panel=new GameObject(name,typeof(RectTransform));panel.SetActive(false);panel.transform.SetParent(parent,false);
            try{var view=panel.AddComponent<T>();bind(view);return view;}
            catch{if(UnityEngine.Application.isPlaying)UnityEngine.Object.Destroy(panel);else UnityEngine.Object.DestroyImmediate(panel);throw;}
        }
        public Button Button(string name, string label, Transform parent, Vector2 anchor, Vector2 position)
            {
                var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
                var rect = (RectTransform)obj.transform; rect.anchorMin = rect.anchorMax = anchor; rect.sizeDelta = new Vector2(200, 70); rect.anchoredPosition = position;
                obj.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.32f, 1f);
                var button = obj.GetComponent<Button>(); button.navigation = new Navigation { mode = Navigation.Mode.None };
                var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text)); textObj.transform.SetParent(obj.transform, false);
                var text = textObj.GetComponent<Text>(); text.font = Font; text.text = label; text.fontSize = 30; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
                var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
                UiLook.Button(button);
                // 常設の補助操作は出撃などの主操作と同じ強さにしない。位置と入力範囲は維持する。
                if(name=="ManualSaveButton"||name=="NewGameButton"||name=="CardCollectionButton"){
                    obj.GetComponent<Image>().color=new Color(.35f,.55f,.65f,.75f);
                    text.color=new Color(.82f,.93f,.98f,1f);
                }
                button.onClick.AddListener(()=>sound?.Invoke(name)); return button;
            }
        // ポーズの操作を攻略と設定へまとめ、既存の水鏡素材と余白を共有する。入力・購読は既存ボタンを維持。
        private void LayoutPauseMenu(GameObject pauseOverlay)
        {
            var panel = new GameObject("PauseMenuSurface", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(pauseOverlay.transform, false);
            panel.transform.SetAsFirstSibling();
            var rect = (RectTransform)panel.transform;
            rect.sizeDelta = new Vector2(1440, 840);
            UiLook.Face(panel.GetComponent<Image>());
            panel.GetComponent<Image>().color = new Color(1, 1, 1, .96f);
            panel.GetComponent<Image>().raycastTarget = false;
            Text Heading(string name, string text, Vector2 position, Vector2 size, int fontSize)
            {
                var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
                obj.transform.SetParent(panel.transform, false);
                var label = obj.GetComponent<Text>(); label.font = Font; label.text = text;
                UiLook.Label(label); label.fontSize = fontSize; label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.sizeDelta = size; label.rectTransform.anchoredPosition = position;
                return label;
            }
            Heading("PauseMenuTitle", "ポーズ", new Vector2(-480, 320), new Vector2(360, 70), 44);
            Heading("PausePlayHeading", "コウリャク", new Vector2(-480, 210), new Vector2(360, 40), 24);
            Heading("PauseSettingsHeading", "セッテイ", new Vector2(210, 210), new Vector2(740, 40), 24);
            void Rule(string name, Vector2 position, Vector2 size)
            {
                var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(panel.transform, false);
                var line = obj.GetComponent<Image>(); line.color = new Color(.17f, .63f, .71f, .3f); line.raycastTarget = false;
                ((RectTransform)obj.transform).sizeDelta = size; ((RectTransform)obj.transform).anchoredPosition = position;
            }
            Rule("PauseHeaderRule", new Vector2(0, 260), new Vector2(1296, 2));
            Rule("PauseSectionRule", new Vector2(-210, -40), new Vector2(2, 460));
            void Place(string name, float x, float y, float width)
            {
                var button = pauseOverlay.transform.Find(name).GetComponent<Button>();
                var target = (RectTransform)button.transform;
                target.anchoredPosition = new Vector2(x, y); target.sizeDelta = new Vector2(width, 88);
                var label = button.GetComponentInChildren<Text>();
                label.rectTransform.offsetMin = new Vector2(18, 6); label.rectTransform.offsetMax = new Vector2(-18, -6);
                label.fontSize = label.resizeTextMaxSize = 28;

            }
            Place("ResumeButton", -480, 120, 360); Place("LoadCheckpointButton", -480, 12, 360);
            Place("OwnedCardsButton", -480, -96, 360); Place("RetreatButton", -480, -244, 360);
            Place("KeyboardSettingsButton", 20, 120, 340); Place("GamepadSettingsButton", 400, 120, 340);
            Place("AudioSettingsButton", 20, 0, 340); Place("VibrationSetting", 400, 0, 340);
            Place("TransformationAnimationSetting", 20, -120, 340); Place("DialogueTextSpeedButton", 400, -120, 340);
        }

        public GameObject Pause()
        {
            var panel=Panel("PauseOverlay",true);panel.GetComponent<Image>().color=new Color(.025f,.07f,.10f,.86f);
            foreach(var pair in new[]{("ResumeButton","再開"),("LoadCheckpointButton","続きから"),("KeyboardSettingsButton","キー設定"),("GamepadSettingsButton","パッド設定"),("VibrationSetting",""),("TransformationAnimationSetting",""),("OwnedCardsButton","所持カード"),("RetreatButton","撤退して\n帰還"),("AudioSettingsButton","音量設定"),("DialogueTextSpeedButton","")})
                Button(pair.Item1,pair.Item2,panel.transform,new Vector2(.5f,.5f),Vector2.zero);
            LayoutPauseMenu(panel);return panel;
        }
        public Text Label(string name,Transform target,Vector2 anchor,Vector2 position,Vector2 size)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Text));obj.transform.SetParent(target,false);
            var text=obj.GetComponent<Text>();text.font=Font;text.fontSize=30;UiLook.Label(text);text.color=UiLook.Ink;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;
            var rect=text.rectTransform;rect.anchorMin=rect.anchorMax=anchor;rect.anchoredPosition=position;rect.sizeDelta=size;return text;
        }
        public Text Countdown()
        {
            var text=Label("ExplorationResumeCountdown",parent,new Vector2(.5f,.5f),Vector2.zero,new Vector2(240,160));
            text.fontSize=96;text.color=Color.white;text.gameObject.SetActive(false);return text;
        }
        public GameObject Reward(bool currency,out Text message,out Button receive)
        {
            var panel=Panel(currency?"CurrencyRewardPanel":"ItemRewardPanel",true);
            message=Label(currency?"CurrencyRewardText":"ItemRewardText",panel.transform,new Vector2(.5f,.5f),new Vector2(0,100),currency?new Vector2(1000,180):new Vector2(1200,220));
            receive=Button(currency?"ReceiveCurrencyButton":"ReceiveItemReward","ウケトル",panel.transform,new Vector2(.5f,.5f),new Vector2(-240,currency?-80:-100));
            Button(currency?"SkipCurrencyButton":"SkipItemReward","スキップ",panel.transform,new Vector2(.5f,.5f),new Vector2(240,currency?-80:-100));
            Button(currency?"PauseCurrencyReward":"PauseItemReward","ポーズ",panel.transform,Vector2.one,new Vector2(-120,-60));return panel;
        }
        public GameObject Checkpoint(out Text message,out Button resume,out Button retry)
        {
            var panel=Panel("CheckpointOverlay",true);
            message=Label("CheckpointMessage",panel.transform,new Vector2(.5f,.5f),new Vector2(0,90),new Vector2(1360,560));message.horizontalOverflow=HorizontalWrapMode.Wrap;
            resume=Button("ContinueCheckpointButton","続きから",panel.transform,new Vector2(.5f,.5f),new Vector2(0,-270));
            retry=Button("RetryCheckpointButton","保存を再試行",panel.transform,new Vector2(.5f,.5f),new Vector2(0,-270));((RectTransform)retry.transform).sizeDelta=new Vector2(320,70);return panel;
        }

    }
}

namespace Olca.Presentation.Management
{
    // 画面固有の内容を持たず、借用Popupと共通ticketの接続だけをまとめる。
    public abstract class OwnedPopup : MonoBehaviour
    {
        protected Button confirm,cancel;
        protected ScrollRect inputScroll;protected bool directionScroll=true;
        private DialogManager manager;
        private IDialogOwner owner;
        private DialogOwnerScope scope;
        private Func<bool> valid;
        private object generation;
        private System.Threading.Tasks.Task<DialogResult> request;
        private Olca.Unity.MenuPadScope legacyMenu;
        private GameObject origin;
        private bool suspended,attempting;
        private Button.ButtonClickedEvent confirmFeedback,cancelFeedback;
        public bool IsOpen {get;private set;}
        public IDialogOwner Owner=>owner;
        public event Action VisibilityChanged;
        protected void CaptureFeedback(){confirmFeedback=confirm!=null?confirm.onClick:null;cancelFeedback=cancel.onClick;}
        protected void CompleteAccepted()=>Finish(DialogResult.Confirmed);
        protected bool Begin(DialogManager service,IDialogOwner parent,Func<bool> live)
        {
            if(IsOpen||!(live?.Invoke()??false)||service!=null&&(service.ActiveCount!=0||parent!=null&&(!parent.IsCurrent||service.Blocks(parent))))return false;
            manager=service;owner=parent;valid=live;origin=EventSystem.current?.currentSelectedGameObject;IsOpen=true;suspended=false;
            if(Attach())return true;
            Abort();return false;
        }
        private bool Attach()
        {
            if(!(valid?.Invoke()??false)||owner!=null&&!owner.IsCurrent)return false;
            if(manager!=null&&manager.ActiveCount!=0)return false;
            var token=generation=new object();
            gameObject.SetActive(true);transform.SetAsLastSibling();
            if(confirm!=null){confirm.onClick=new Button.ButtonClickedEvent();confirm.onClick.AddListener(()=>{if(Live(token)&&confirm.IsInteractable()&&confirm.gameObject.activeSelf)confirmFeedback?.Invoke();});}
            cancel.onClick=new Button.ButtonClickedEvent();
            cancel.onClick.AddListener(()=>{if(Live(token)&&cancel.IsInteractable())cancelFeedback?.Invoke();});
            if(manager!=null){
                if(owner==null){scope=manager.CreateOwner(()=>IsOpen&&!suspended&&ReferenceEquals(generation,token)&&(valid?.Invoke()??false));owner=scope;}
                Action<DialogResult> completed=result=>{if(Live(token))Finish(result);};
                request=confirm==null?manager.ShowNotice(owner,gameObject,cancel,inputScroll,completed):manager.ShowPersistent(owner,gameObject,confirm,cancel,()=>Live(token)&&Attempt(),completed,inputScroll,directionScroll);
                if(request.IsCompleted){if(request.Result==DialogResult.ParentClosed||request.Result==DialogResult.Busy){Finish(DialogResult.ParentClosed);return false;}}
            }else{
                if(confirm!=null)confirm.onClick.AddListener(()=>{if(!Live(token)||!confirm.IsInteractable()||!confirm.gameObject.activeSelf)return;attempting=true;bool close;try{close=Attempt();}finally{attempting=false;}if(close&&Live(token))Finish(DialogResult.Confirmed);});
                cancel.onClick.AddListener(()=>{if(Live(token)&&cancel.IsInteractable())Finish(DialogResult.Cancelled);});
                legacyMenu=new Olca.Unity.MenuPadScope(()=>Live(token)&&Olca.Unity.MenuPadScope.IsTopmost(transform));
                legacyMenu.Bind(transform,cancel.name,()=>cancel.onClick.Invoke(),inputScroll,directionScroll:directionScroll);
            }
            return true;
        }
        private bool Live(object token)=>this!=null&&IsOpen&&!suspended&&!attempting&&ReferenceEquals(generation,token)&&(valid?.Invoke()??false)&&(owner==null||owner.IsCurrent);
        protected void Rendered(){if(!IsOpen||suspended)return;gameObject.SetActive(true);transform.SetAsLastSibling();VisibilityChanged?.Invoke();EventSystem.current?.SetSelectedGameObject(cancel.IsInteractable()?cancel.gameObject:confirm!=null?confirm.gameObject:null);}
        public void RefreshLifetime()
        {
            if(!IsOpen)return;
            if(!(valid?.Invoke()??false)||owner!=null&&!owner.IsCurrent){Abort();return;}
            if(suspended){if(manager==null||manager.ActiveCount==0){suspended=false;if(Attach())Redraw();}return;}
            if(request?.IsCompleted==true){Finish(DialogResult.ParentClosed);return;}
            legacyMenu?.Tick();if(this!=null&&gameObject.activeInHierarchy)transform.SetAsLastSibling();
        }
        public void Suspend()
        {
            if(!IsOpen||suspended)return;suspended=true;generation=null;Detach();if(this!=null)gameObject.SetActive(false);
        }
        public void Resume(DialogManager service,IDialogOwner parent,Func<bool> live)
        {
            if(!IsOpen||!suspended)return;manager=service;owner=parent;valid=live;RefreshLifetime();
        }
        private void Detach()
        {
            var old=owner;generation=null;if(old!=null)manager?.CancelActiveOwner(old);scope?.Dispose();scope=null;owner=null;request=null;legacyMenu?.Dispose();legacyMenu=null;
        }
        private void Finish(DialogResult result)
        {
            if(!IsOpen)return;
            bool restore=result!=DialogResult.ParentClosed&&(valid?.Invoke()??false);var focus=origin;string focusName=restore&&focus!=null?focus.name:null;
            IsOpen=false;suspended=false;Detach();origin=null;if(this!=null)gameObject.SetActive(false);VisibilityChanged?.Invoke();
            if(restore&&focus!=null&&focus.activeInHierarchy&&focus.GetComponent<Selectable>()?.IsInteractable()==true)EventSystem.current?.SetSelectedGameObject(focus);
            if(EventSystem.current!=null){var selected=EventSystem.current.currentSelectedGameObject;if(selected!=null&&selected.transform.IsChildOf(transform))EventSystem.current.SetSelectedGameObject(null);}
            valid=null;manager=null;OnResult(result,focusName);
        }
        public void Abort()=>Finish(DialogResult.ParentClosed);
        protected abstract bool Attempt();
        protected abstract void Redraw();
        protected abstract void OnResult(DialogResult result,string focusName);
        protected virtual void OnDestroy(){Abort();VisibilityChanged=null;confirmFeedback=cancelFeedback=null;confirm=cancel=null;inputScroll=null;}
    }
    public enum ProgressPendingAction { None,ManualSave,NewGame }
    public interface IProgressActionOperations
    {
        long Revision {get;} bool Pending {get;} ProgressPendingAction PendingAction {get;} string Error {get;} bool CanStartNewGame {get;}
        bool ManualSave();bool Retry();bool StartNewGame(long confirmedRevision);
    }
    public enum ProgressActionOutcome { Closed,Cancelled,NewGameStarted }
    public readonly struct ProgressActionResult
    {
        public readonly ProgressActionOutcome Outcome;public readonly string FocusName;
        public ProgressActionResult(ProgressActionOutcome outcome,string focusName){Outcome=outcome;FocusName=focusName;}
    }
    /// <summary>進行確認と保存結果/同一計画再試行の表示・操作状態を所有する。</summary>
    public sealed class ProgressActionDialog : OwnedPopup
    {
        private enum Purpose { Notice,ManualSave,NewGame }
        private Purpose purpose;
        private IProgressActionOperations operations;
        private long revision;
        private bool pending,newGameSucceeded,showConfirm,canClose;
        private string body,confirmText;
        public Text Message {get;private set;}
        public Button ConfirmButton=>confirm;public Button CancelButton=>cancel;
        public event Action<ProgressActionResult> Completed;
        public void Initialize(GameplayPanelFactory factory)
        {
            var obj=new GameObject("ProgressActionMessage",typeof(RectTransform),typeof(Text));obj.transform.SetParent(transform,false);
            Message=obj.GetComponent<Text>();Message.font=factory.Font;Message.fontSize=30;Olca.Presentation.Dungeon.UiLook.Label(Message);Message.alignment=TextAnchor.MiddleCenter;
            Message.rectTransform.sizeDelta=new Vector2(1500,430);Message.rectTransform.anchoredPosition=new Vector2(0,120);
            confirm=factory.Button("ConfirmNewGame","進行を初期化して開始",transform,new Vector2(.5f,.5f),new Vector2(-300,-180));
            cancel=factory.Button("CancelProgressAction","取消して戻る",transform,new Vector2(.5f,.5f),new Vector2(300,-180));
            foreach(var b in new[]{confirm,cancel})((RectTransform)b.transform).sizeDelta=new Vector2(500,80);CaptureFeedback();
        }
        public void OpenManualSave(IProgressActionOperations service,DialogManager manager,IDialogOwner parent,Func<bool> live)
        {
            if(IsOpen||service.Pending&&service.PendingAction==ProgressPendingAction.None)return;
            purpose=service.PendingAction==ProgressPendingAction.NewGame?Purpose.NewGame:Purpose.ManualSave;operations=service;pending=service.Pending;newGameSucceeded=false;confirm.interactable=cancel.interactable=false;
            if(!Begin(manager,parent,live)){operations=null;return;}
            if(pending){ShowPending();return;}
            bool saved=service.ManualSave();pending=!saved&&service.Pending;
            Set(saved?"スロット1に保存しました。\n拠点の所持・成長・編成・図鑑を保存しました。\nダンジョンの途中状態は保存していません。":"保存できませんでした。スロット1の元データを保持しています。\n"+service.Error+"\n同じ内容で再試行してください。",pending,!pending,"同じ保存を再試行");
        }
        public void OpenNewGame(IProgressActionOperations service,DialogManager manager,IDialogOwner parent,Func<bool> live)
        {
            if(IsOpen||service.Pending&&service.PendingAction!=ProgressPendingAction.NewGame)return;
            purpose=Purpose.NewGame;operations=service;revision=service.Revision;pending=service.Pending;newGameSucceeded=false;confirm.interactable=cancel.interactable=false;
            if(!Begin(manager,parent,live)){operations=null;return;}
            if(pending){ShowPending();return;}
            Set("スロット1を「はじめから」にしますか？\n所持カード・保存デッキ・成長・通貨・図鑑・解放履歴・攻略進行を初期化し、\nスターター20枚で拠点から開始します。\n操作設定・演出設定・音量設定は保持します。\n実行前の進行文書と旧攻略データは復旧用に保全します。",true,true,"進行を初期化して開始");
        }
        public void OpenNotice(string message,bool accept,bool close,DialogManager manager,IDialogOwner parent,Func<bool> live)
        {
            if(IsOpen){if(purpose==Purpose.Notice)Set(message,accept,close,confirmText);return;}
            purpose=Purpose.Notice;operations=null;pending=false;newGameSucceeded=false;confirm.interactable=cancel.interactable=false;
            if(Begin(manager,parent,live))Set(message,accept,close,"進行を初期化して開始");
        }
        private void ShowPending()
        {
            if(purpose==Purpose.NewGame)Set("新規開始の保存が完了していません。元の進行と同じ再試行計画を保持しています。\n"+operations.Error,true,false,"同じ新規開始を再試行");
            else Set("保存できませんでした。スロット1の元データを保持しています。\n"+operations.Error+"\n同じ内容で再試行してください。",true,false,"同じ保存を再試行");
        }
        private void Set(string message,bool accept,bool close,string label){body=message;showConfirm=accept;canClose=close;confirmText=label;Redraw();}
        protected override void Redraw()
        {
            if(newGameSucceeded){CompleteAccepted();return;}
            Message.text=body;confirm.gameObject.SetActive(showConfirm);confirm.interactable=true;cancel.interactable=canClose;
            confirm.GetComponentInChildren<Text>().text=confirmText;cancel.GetComponentInChildren<Text>().text=purpose==Purpose.NewGame?"取消して戻る":"閉じる";
            cancel.navigation=showConfirm?new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=confirm,selectOnRight=confirm}:new Navigation{mode=Navigation.Mode.None};
            confirm.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=cancel,selectOnRight=cancel};Rendered();
        }
        protected override bool Attempt()
        {
            if(newGameSucceeded)return true;
            if(purpose==Purpose.ManualSave&&pending){if(operations.Retry()){pending=false;Set("スロット1に保存しました。",false,true,confirmText);}else Set("保存を再試行できませんでした。\n"+operations.Error,true,false,confirmText);return false;}
            if(purpose!=Purpose.NewGame)return false;
            bool saved=pending?operations.Retry():operations.CanStartNewGame&&operations.StartNewGame(revision);
            if(!saved){pending=operations.Pending;Set("新規開始の保存が完了していません。元の進行と同じ再試行計画を保持しています。\n"+operations.Error,true,!pending,"同じ新規開始を再試行");return false;}
            pending=false;newGameSucceeded=true;return true;
        }
        public void ConfirmFromCompatibilityApi(){confirm.onClick.Invoke();}
        protected override void OnResult(DialogResult result,string focusName)
        {
            var outcome=newGameSucceeded?ProgressActionOutcome.NewGameStarted:result==DialogResult.Cancelled?ProgressActionOutcome.Cancelled:ProgressActionOutcome.Closed;
            operations=null;pending=false;body=null;newGameSucceeded=false;
            if(result!=DialogResult.ParentClosed)Completed?.Invoke(new ProgressActionResult(outcome,focusName));
        }
        protected override void OnDestroy(){base.OnDestroy();Completed=null;operations=null;}
    }
    public readonly struct RetreatDialogState
    {
        public readonly bool Paused,Returning,Ending,Suspended,CheckpointBlocked,Finished;
        public RetreatDialogState(bool paused,bool returning,bool ending,bool suspended,bool blocked,bool finished){Paused=paused;Returning=returning;Ending=ending;Suspended=suspended;CheckpointBlocked=blocked;Finished=finished;}
        public bool CanOpen=>Paused&&!Returning&&!Ending&&!Suspended&&!CheckpointBlocked&&!Finished;
        public bool CanConfirm=>Paused&&!Returning;
    }
    /// <summary>撤退の警告・可否表示・確認結果とPauseの覆いを所有し、損失計算は行わない。</summary>
    /// <summary>撤退固有の最新可否とPause復帰だけを調整し、確認表示/入力は共通Dialogへ委譲する。</summary>
    public sealed class RetreatConfirmationDialog : MonoBehaviour
    {
        private Func<RetreatDialogState> state;private GameplayPauseView pause;private GameObject legacyPause,legacyOrigin;private Func<bool> hostLive;private Font font;private Action<string> feedback;
        private ConfirmationDialog dialog;
        public bool IsOpen=>dialog!=null&&dialog.IsOpen;
        public Button ConfirmButton=>dialog!=null?dialog.ConfirmButton:null;public Button CancelButton=>dialog!=null?dialog.CancelButton:null;
        public event Action Confirmed;
        public void Initialize(GameplayPanelFactory factory){font=factory.Font;feedback=factory.Feedback;}
        public static bool CanRequest(RetreatDialogState state,DialogManager manager,GameplayPauseView parent)=>state.CanOpen&&(manager==null||manager.ActiveCount==0&&parent?.Context?.IsCurrent==true&&!parent.Context.DialogBlocksInput);
        public void Open(Func<RetreatDialogState> source,DialogManager manager,GameplayPauseView parent,GameObject legacy,Func<bool> live)
        {
            if(IsOpen||!CanRequest(source(),manager,parent))return;
            state=source;pause=parent;legacyPause=legacy;hostLive=live;legacyOrigin=EventSystem.current?.currentSelectedGameObject;
            var owner=parent?.Context;if(manager!=null&&(owner?.IsCurrent!=true||owner.DialogBlocksInput||manager.ActiveCount!=0)){Clear();return;}
            if(parent!=null)parent.CoverForDialog();gameObject.SetActive(true);transform.SetAsLastSibling();
            var popup=ConfirmationDialog.Create(transform,font,new ConfirmationContent{ButtonFeedback=feedback,RootName="RetreatConfirmationContents",MessageName="RetreatWarning",Message="撤退すると攻略中に獲得したカード・通貨をすべて失います。\n経験値は獲得しません。既存の所持・成長は保持します。",ConfirmName="ConfirmRetreat",ConfirmLabel="撤退して帰還",CancelName="CancelRetreat",CancelLabel="攻略へ戻る",Layout=new ConfirmationLayout{ButtonFontSize=30,MessagePosition=new Vector2(0,150),MessageSize=new Vector2(1600,200),ConfirmPosition=new Vector2(-280,-100),CancelPosition=new Vector2(280,-100),ButtonSize=new Vector2(420,70),Surface=Color.clear}});
            dialog=popup;popup.CanConfirm=()=>this!=null&&state?.Invoke().CanConfirm==true;
            popup.Finished+=result=>{if(this!=null&&ReferenceEquals(dialog,popup))Finish(result);};
            if(!popup.Open(gameObject,manager,owner,()=>this!=null&&(hostLive?.Invoke()??false),false)){if(parent!=null)parent.RestoreAfterDialog();dialog=null;Clear();gameObject.SetActive(false);return;}
            if(parent==null&&legacy!=null)legacy.SetActive(false);
        }
        public void ConfirmFromCompatibilityApi(){ConfirmButton?.onClick.Invoke();}
        public void RefreshLifetime(){if(dialog!=null)dialog.RefreshLifetime();}
        public void Abort(){if(dialog!=null)dialog.Abort();}
        private void Finish(DialogResult result)
        {
            dialog=null;
            if(result!=DialogResult.ParentClosed&&(hostLive?.Invoke()??false)){if(pause!=null)pause.RestoreAfterDialog();else if(legacyPause!=null){legacyPause.SetActive(true);if(legacyOrigin!=null&&legacyOrigin.activeInHierarchy&&legacyOrigin.GetComponent<Selectable>()?.IsInteractable()==true)EventSystem.current?.SetSelectedGameObject(legacyOrigin);}}
            bool accepted=result==DialogResult.Confirmed;Clear();gameObject.SetActive(false);if(accepted)Confirmed?.Invoke();
        }
        private void Clear(){state=null;pause=null;legacyPause=null;hostLive=null;legacyOrigin=null;}
        private void OnDisable(){Abort();}
        private void OnDestroy(){Abort();Clear();dialog=null;font=null;feedback=null;Confirmed=null;}
    }
}
