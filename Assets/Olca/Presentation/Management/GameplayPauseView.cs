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
        public GameObject Progress(out Text message,out Button confirm,out Button cancel)
        {
            var panel=Panel("ProgressActionOverlay",true);
            var obj=new GameObject("ProgressActionMessage",typeof(RectTransform),typeof(Text));obj.transform.SetParent(panel.transform,false);
            message=obj.GetComponent<Text>();message.font=Font;message.fontSize=30;UiLook.Label(message);message.alignment=TextAnchor.MiddleCenter;
            message.rectTransform.sizeDelta=new Vector2(1500,430);message.rectTransform.anchoredPosition=new Vector2(0,120);
            confirm=Button("ConfirmNewGame","進行を初期化して開始",panel.transform,new Vector2(.5f,.5f),new Vector2(-300,-180));
            cancel=Button("CancelProgressAction","取消して戻る",panel.transform,new Vector2(.5f,.5f),new Vector2(300,-180));
            foreach(var button in new[]{confirm,cancel})((RectTransform)button.transform).sizeDelta=new Vector2(500,80);
            return panel;
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
        public GameObject Retreat(out Button confirm,out Button cancel)
        {
            var panel=Panel("RetreatConfirmation",true);
            var text=Label("RetreatWarning",panel.transform,new Vector2(.5f,.5f),new Vector2(0,150),new Vector2(1600,200));
            text.text="撤退すると攻略中に獲得したカード・通貨をすべて失います。\n経験値は獲得しません。既存の所持・成長は保持します。";
            confirm=Button("ConfirmRetreat","撤退して帰還",panel.transform,new Vector2(.5f,.5f),new Vector2(-280,-100));
            cancel=Button("CancelRetreat","攻略へ戻る",panel.transform,new Vector2(.5f,.5f),new Vector2(280,-100));
            foreach(var button in new[]{confirm,cancel})((RectTransform)button.transform).sizeDelta=new Vector2(420,70);return panel;
        }
    }
}
