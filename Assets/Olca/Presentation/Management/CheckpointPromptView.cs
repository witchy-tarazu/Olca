using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Olca.Presentation.Management
{
    /// <summary>checkpoint案内の表示世代と操作だけを管理し、復元/保存の判断は所有しない。</summary>
    public sealed class CheckpointPromptView : MonoBehaviour
    {
        private readonly Dictionary<Button,Button.ButtonClickedEvent> actions=new Dictionary<Button,Button.ButtonClickedEvent>();
        private Func<bool> allowed;
        private bool clicked;private int clickFrame;
        public ManagedScreenContext Context {get;private set;}
        public void Initialize(Func<bool> allowed){this.allowed=allowed;foreach(var b in GetComponentsInChildren<Button>(true))actions.Add(b,b.onClick);}
        public void Show(ManagedScreenContext context)
        {
            Context=context;clicked=false;
            foreach(var pair in actions){var action=pair.Value;var button=pair.Key;button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(()=>{if(this!=null&&button!=null&&button.isActiveAndEnabled&&button.IsInteractable()&&ReferenceEquals(Context,context)&&context.IsCurrent&&!context.DialogBlocksInput&&(allowed?.Invoke()??false)&&(!clicked||clickFrame!=Time.frameCount)){clicked=true;clickFrame=Time.frameCount;action.Invoke();}});}
            gameObject.SetActive(true);transform.SetAsLastSibling();
        }
        public void Hide(){Context=null;gameObject.SetActive(false);}
        private void OnDisable(){Context?.NotifyHidden();Context=null;}
        private void OnDestroy(){Context?.NotifyHidden();Context=null;allowed=null;actions.Clear();}
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>外界検証地形の表示。位置/時計/保存は所有せずSessionの連続座標を投影する。</summary>
    public sealed partial class OutsideExplorationView : MonoBehaviour
    {
        private Olca.Presentation.Management.ManagedScreenContext context;
        private Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease presentation;
        private Olca.Unity.MenuPadScope menu;
        private Button back,pause;private bool covered;private int uncoveredFrame=-1;
        private Action returned,entered;private Func<bool> canEnter;private Func<string> interactionHint;private Func<bool> trialRoute,transientInputBlocked;private Button entrance;private Text entranceTitle,entranceActionHint;private Text trialRouteHint;private Image trialDoor;private RectTransform reloadPoint;private Text reloadPointLabel;
        private Func<Olca.Core.Domain.Card,Sprite> cardIcon;
        private Olca.Core.Logic.Integration.OutsideExplorationCards renderedCards;
        private bool renderedDeckLocation;
        private Text handHeading,selectionGuideText,movementGuideText,cardUseText;private Func<string> cardUseGuide;private Action<Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease,Olca.Core.Logic.Integration.OutsideExplorationCards,int,long> handPressed;private Func<string> selectionGuide,movementGuide;
        private readonly Text[] handNames=new Text[3],handCosts=new Text[3];
        private readonly Image[] handIcons=new Image[3],handSelectionMarks=new Image[3];
        private readonly Button[] handButtons=new Button[3];
        private Sprite idleSprite,riseSprite,apexSprite,fallSprite;private AnimationClip moveClip,idleClip;private SpriteRenderer motionSample;
        private readonly System.Collections.Generic.List<(Image image,Text label)> landingViews=new System.Collections.Generic.List<(Image,Text)>();
        private Image player,terrain;private Text leftEnd,rightEnd,gaugeLabel;private RectTransform gaugeFill;private long lastGaugeUnits=-1,lastGaugeMaximum=-1;private float pixelsPerWorldUnit;
        public bool CanPlay=>IsOpen&&transientInputBlocked?.Invoke()!=true&&!covered&&Time.frameCount!=uncoveredFrame&&!context.DialogBlocksInput&&Olca.Unity.MenuPadScope.IsTopmost(transform);
        public bool IsOpen=>context?.IsCurrent==true&&presentation?.IsCurrent==true&&gameObject.activeInHierarchy;
        public static GameObject Create(Transform parent,Font font,Olca.Unity.ResourceManager resources)
        {
            var root=new GameObject("OutsideExplorationView",typeof(RectTransform),typeof(Image));root.SetActive(false);root.transform.SetParent(parent,false);
            try{root.AddComponent<OutsideExplorationView>().Initialize(font,resources);return root;}
            catch{if(UnityEngine.Application.isPlaying)Destroy(root);else DestroyImmediate(root);throw;}
        }
        private void Initialize(Font font,Olca.Unity.ResourceManager resources)
        {
            var rect=(RectTransform)transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;GetComponent<Image>().color=new Color(.02f,.05f,.08f,1);
            var source=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation");
            Sprite Find(string name){foreach(var sr in source.GetComponentsInChildren<SpriteRenderer>(true))if(sr.name==name)return sr.sprite;throw new InvalidOperationException("外界入口の共有素材がありません: "+name);}
            Image Picture(string name,Sprite sprite,Vector2 min,Vector2 max){if(sprite==null)throw new InvalidOperationException("外界入口の画像がありません: "+name);var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.sprite=sprite;image.raycastTarget=false;var r=image.rectTransform;r.anchorMin=min;r.anchorMax=max;r.offsetMin=r.offsetMax=Vector2.zero;return image;}
            var background=Picture("OutsideOriginalBackground",Find("DungeonBackground"),Vector2.zero,Vector2.one);var cover=background.gameObject.AddComponent<AspectRatioFitter>();cover.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;cover.aspectRatio=background.sprite.rect.width/background.sprite.rect.height;
            terrain=Picture("OutsideEntryGround",Find("Ground"),Vector2.zero,new Vector2(1,.21f));terrain.type=Image.Type.Tiled;
            var field=source.GetComponent<Olca.Presentation.Field.GameFieldView>();var idle=idleSprite=field.AuthoredIdlePlayerSprite;
            riseSprite=field.AuthoredJumpRiseSprite;apexSprite=field.AuthoredJumpApexSprite;fallSprite=field.AuthoredJumpFallSprite;moveClip=field.AuthoredMoveClip;idleClip=field.AuthoredIdleClip;
            if(moveClip!=null){var sampler=new GameObject("OutsideImportedMotionSample",typeof(SpriteRenderer),typeof(Animator));sampler.transform.SetParent(transform,false);motionSample=sampler.GetComponent<SpriteRenderer>();motionSample.enabled=false;var animator=sampler.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.enabled=false;}
            player=Picture("OutsideEntryPlayer",idle,new Vector2(0,.21f),new Vector2(0,.21f));player.rectTransform.pivot=idle.pivot/idle.rect.size;pixelsPerWorldUnit=1080f/(2*resources.GetShared<GameObject>("OlcaSystem/GameplayCamera").GetComponent<Camera>().orthographicSize);player.rectTransform.sizeDelta=new Vector2(idle.bounds.size.x,idle.bounds.size.y)*pixelsPerWorldUnit;player.rectTransform.localScale=new Vector3(-1,1,1);
            InitializeTraversal(resources,field);InitializeTrialSword(font,field);
            var header=Picture("OutsideHeadingSurface",resources.GetShared<Sprite>("OlcaUI/window"),new Vector2(0,1),Vector2.one);header.type=Image.Type.Sliced;header.rectTransform.pivot=new Vector2(.5f,1);header.rectTransform.sizeDelta=new Vector2(0,120);
            var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,font,null);var title=factory.Label("OutsideExplorationTitle",transform,new Vector2(0,1),new Vector2(630,-62),new Vector2(1180,70));title.text="ガイカイ タンサク  /  ケンショウ チケイ";title.alignment=TextAnchor.MiddleLeft;title.fontSize=28;title.color=Olca.Presentation.Dungeon.UiLook.Ink;title.rectTransform.anchorMin=new Vector2(0,1);title.rectTransform.anchorMax=Vector2.one;title.rectTransform.offsetMin=new Vector2(40,-97);title.rectTransform.offsetMax=new Vector2(-580,-27);
            InitializeItemHud(factory);
            recoveryPoint=factory.Button("OutsideRecoveryPoint","ケンショウ カイフク（カリ）",transform,new Vector2(0,.21f),Vector2.zero);recoveryPoint.GetComponent<RectTransform>().sizeDelta=new Vector2(350,104);Olca.Presentation.Dungeon.UiLook.Button(recoveryPoint);recoveryPoint.gameObject.SetActive(false);
            outsideHp=factory.Label("OutsidePlayerHp",transform,new Vector2(1,1),new Vector2(-255,-320),new Vector2(470,42));outsideHp.fontSize=24;outsideHp.color=Color.white;outsideHp.resizeTextForBestFit=true;outsideHp.resizeTextMinSize=18;outsideHp.resizeTextMaxSize=24;outsideHp.raycastTarget=false;
            outsidePoise=factory.Label("OutsidePlayerPoise",transform,Vector2.one,new Vector2(-255,-278),new Vector2(470,32));outsidePoise.fontSize=18;outsidePoise.color=Color.white;outsidePoise.raycastTarget=false;
            var poiseTrack=new GameObject("OutsidePlayerPoiseTrack",typeof(RectTransform),typeof(Image));poiseTrack.transform.SetParent(transform,false);var poiseRect=(RectTransform)poiseTrack.transform;poiseRect.anchorMin=poiseRect.anchorMax=Vector2.one;poiseRect.pivot=new Vector2(0,.5f);poiseRect.anchoredPosition=new Vector2(-470,-300);poiseRect.sizeDelta=new Vector2(430,6);var poiseBack=poiseTrack.GetComponent<Image>();poiseBack.color=new Color(.12f,.13f,.08f,1);poiseBack.raycastTarget=false;
            var poiseFill=new GameObject("OutsidePlayerPoiseFill",typeof(RectTransform),typeof(Image));poiseFill.transform.SetParent(poiseTrack.transform,false);outsidePoiseFill=poiseFill.GetComponent<Image>();outsidePoiseFill.color=new Color(.95f,.8f,.27f,1);outsidePoiseFill.raycastTarget=false;var poiseFillRect=(RectTransform)poiseFill.transform;poiseFillRect.anchorMin=poiseFillRect.anchorMax=new Vector2(0,.5f);poiseFillRect.pivot=new Vector2(0,.5f);poiseFillRect.anchoredPosition=Vector2.zero;poiseFillRect.sizeDelta=new Vector2(430,6);
            gaugeLabel=factory.Label("OutsideCardGaugeLabel",transform,new Vector2(0,1),new Vector2(240,-148),new Vector2(400,28));gaugeLabel.fontSize=22;gaugeLabel.alignment=TextAnchor.MiddleLeft;gaugeLabel.color=Color.white;gaugeLabel.raycastTarget=false;gaugeLabel.horizontalOverflow=HorizontalWrapMode.Wrap;gaugeLabel.verticalOverflow=VerticalWrapMode.Truncate;gaugeLabel.resizeTextForBestFit=true;gaugeLabel.resizeTextMinSize=18;gaugeLabel.resizeTextMaxSize=22;
            var track=new GameObject("OutsideCardGaugeTrack",typeof(RectTransform),typeof(Image));track.transform.SetParent(transform,false);var trackImage=track.GetComponent<Image>();trackImage.color=new Color(.05f,.14f,.2f,.94f);trackImage.raycastTarget=false;var trackRect=track.GetComponent<RectTransform>();trackRect.anchorMin=trackRect.anchorMax=new Vector2(0,1);trackRect.pivot=new Vector2(0,.5f);trackRect.anchoredPosition=new Vector2(40,-174);trackRect.sizeDelta=new Vector2(400,10);
            var fill=new GameObject("OutsideCardGaugeFill",typeof(RectTransform),typeof(Image));fill.transform.SetParent(track.transform,false);var fillImage=fill.GetComponent<Image>();fillImage.color=new Color(.55f,.88f,1,1);fillImage.raycastTarget=false;gaugeFill=fill.GetComponent<RectTransform>();gaugeFill.anchorMin=gaugeFill.anchorMax=new Vector2(0,.5f);gaugeFill.pivot=new Vector2(0,.5f);gaugeFill.anchoredPosition=new Vector2(2,0);gaugeFill.sizeDelta=new Vector2(396,6);
            movementGuideText=factory.Label("OutsideMovementGuide",transform,new Vector2(0,1),new Vector2(380,-238),new Vector2(680,116));movementGuideText.fontSize=18;movementGuideText.alignment=TextAnchor.MiddleLeft;movementGuideText.color=Color.white;movementGuideText.raycastTarget=false;movementGuideText.horizontalOverflow=HorizontalWrapMode.Wrap;movementGuideText.verticalOverflow=VerticalWrapMode.Truncate;movementGuideText.resizeTextForBestFit=true;movementGuideText.resizeTextMinSize=16;movementGuideText.resizeTextMaxSize=18;
            handHeading=factory.Label("OutsideHandHeading",transform,Vector2.one,new Vector2(-365,-145),new Vector2(650,36));handHeading.fontSize=20;handHeading.alignment=TextAnchor.MiddleLeft;handHeading.color=Color.white;handHeading.raycastTarget=false;
            for(int slot=0;slot<3;slot++){
                float x=-580+slot*220;
                var hit=new GameObject("OutsideHandButton"+slot,typeof(RectTransform),typeof(Image),typeof(Button));hit.transform.SetParent(transform,false);
                var hitRect=hit.GetComponent<RectTransform>();hitRect.anchorMin=hitRect.anchorMax=Vector2.one;hitRect.anchoredPosition=new Vector2(x,-195);hitRect.sizeDelta=new Vector2(204,66);
                var hitImage=hit.GetComponent<Image>();hitImage.color=new Color(.05f,.12f,.16f,.35f);handButtons[slot]=hit.GetComponent<Button>();handButtons[slot].targetGraphic=hitImage;handButtons[slot].transition=Selectable.Transition.None;handButtons[slot].navigation=new Navigation{mode=Navigation.Mode.None};
                var mark=new GameObject("OutsideHandSelected"+slot,typeof(RectTransform),typeof(Image));mark.transform.SetParent(hit.transform,false);handSelectionMarks[slot]=mark.GetComponent<Image>();handSelectionMarks[slot].color=new Color(.65f,.95f,1,1);handSelectionMarks[slot].raycastTarget=false;var mr=mark.GetComponent<RectTransform>();mr.anchorMin=new Vector2(0,0);mr.anchorMax=new Vector2(1,0);mr.anchoredPosition=new Vector2(0,2);mr.sizeDelta=new Vector2(0,3);
                var icon=new GameObject("OutsideHandIcon"+slot,typeof(RectTransform),typeof(Image));icon.transform.SetParent(transform,false);handIcons[slot]=icon.GetComponent<Image>();handIcons[slot].raycastTarget=false;handIcons[slot].preserveAspect=true;var ir=handIcons[slot].rectTransform;ir.anchorMin=ir.anchorMax=Vector2.one;ir.anchoredPosition=new Vector2(x-72,-193);ir.sizeDelta=new Vector2(36,36);
                handNames[slot]=factory.Label("OutsideHandName"+slot,transform,Vector2.one,new Vector2(x+20,-180),new Vector2(138,28));var label=handNames[slot];label.fontSize=20;label.alignment=TextAnchor.MiddleLeft;label.color=Color.white;label.raycastTarget=false;label.resizeTextForBestFit=true;label.resizeTextMinSize=16;label.resizeTextMaxSize=20;
                handCosts[slot]=factory.Label("OutsideHandCost"+slot,transform,Vector2.one,new Vector2(x+20,-209),new Vector2(138,24));handCosts[slot].fontSize=18;handCosts[slot].alignment=TextAnchor.MiddleLeft;handCosts[slot].color=new Color(.7f,.89f,1);handCosts[slot].raycastTarget=false;
            }
            selectionGuideText=factory.Label("OutsideSelectionGuide",transform,Vector2.one,new Vector2(-370,-246),new Vector2(650,26));selectionGuideText.fontSize=18;selectionGuideText.color=Color.white;selectionGuideText.raycastTarget=false;selectionGuideText.resizeTextForBestFit=true;selectionGuideText.resizeTextMinSize=16;selectionGuideText.resizeTextMaxSize=18;
            for(int i=0;i<4;i++){var button=factory.Button(i==0?"OutsideSupplyChest":i==1?"OutsidePoisonSupplyChest":i==2?"OutsideTrialPoisonPoint":"OutsideCardSupplyChest","ホキュウバコ（カリ）",transform,new Vector2(0,.21f),Vector2.zero);button.GetComponent<RectTransform>().sizeDelta=new Vector2(420,140);Olca.Presentation.Dungeon.UiLook.Button(button);button.gameObject.SetActive(false);var label=button.GetComponentInChildren<Text>();label.rectTransform.offsetMin=new Vector2(18,8);label.rectTransform.offsetMax=new Vector2(-18,-8);label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Truncate;supplySlots[i]=new SupplySlot{Button=button,Index=i,X=i==0?Olca.Core.Logic.Integration.OutsideTrialRoute.SupplyX:i==1?Olca.Core.Logic.Integration.OutsideTrialRoute.PoisonSupplyX:i==2?Olca.Core.Logic.Integration.OutsideTrialRoute.PoisonTrialX:Olca.Core.Logic.Integration.OutsideTrialRoute.CardSupplyX};}

            cardUseText=factory.Label("OutsideCardUseGuide",transform,new Vector2(1,0),new Vector2(-450,180),new Vector2(700,96));cardUseText.fontSize=20;cardUseText.alignment=TextAnchor.MiddleLeft;cardUseText.color=Color.white;cardUseText.raycastTarget=false;
            leftEnd=factory.Label("OutsideLeftBoundary",transform,new Vector2(0,.21f),new Vector2(0,-35),new Vector2(140,60));leftEnd.text="ヒダリ ハシ";leftEnd.fontSize=24;leftEnd.color=Color.white;leftEnd.raycastTarget=false;
            rightEnd=factory.Label("OutsideRightBoundary",transform,new Vector2(0,.21f),new Vector2(0,-35),new Vector2(140,60));rightEnd.text="ミギ ハシ";rightEnd.fontSize=24;rightEnd.color=Color.white;rightEnd.raycastTarget=false;
            pause=factory.Button("PauseOutsideExploration","ポーズ",transform,Vector2.one,new Vector2(-430,-62));Olca.Presentation.Dungeon.UiLook.Button(pause);
            back=factory.Button("CloseOutsideExploration","戻る",transform,Vector2.one,new Vector2(-160,-62));Olca.Presentation.Dungeon.UiLook.Button(back);
            var doorObject=new GameObject("OutsideTrialDungeonMarker",typeof(RectTransform),typeof(Image));doorObject.transform.SetParent(transform,false);trialDoor=doorObject.GetComponent<Image>();trialDoor.sprite=resources.GetShared<Sprite>("OlcaUI/window");trialDoor.type=Image.Type.Sliced;trialDoor.color=new Color(.15f,.45f,.55f,.92f);trialDoor.raycastTarget=false;var doorRect=trialDoor.rectTransform;doorRect.anchorMin=doorRect.anchorMax=new Vector2(0,.21f);doorRect.pivot=new Vector2(.5f,0);doorRect.sizeDelta=new Vector2(140,200);doorObject.transform.SetSiblingIndex(player.transform.GetSiblingIndex());
            var reloadObject=new GameObject("OutsideReloadPoint",typeof(RectTransform),typeof(Image));reloadObject.transform.SetParent(transform,false);reloadPoint=reloadObject.GetComponent<RectTransform>();reloadPoint.anchorMin=reloadPoint.anchorMax=new Vector2(0,.21f);reloadObject.GetComponent<Image>().color=new Color(.025f,.07f,.1f,.9f);reloadObject.GetComponent<Image>().raycastTarget=false;
            reloadPointLabel=factory.Label("OutsideReloadPointLabel",reloadPoint,new Vector2(.5f,.5f),Vector2.zero,new Vector2(210,48));reloadPointLabel.fontSize=24;reloadPointLabel.alignment=TextAnchor.MiddleCenter;reloadPointLabel.color=Color.white;reloadPointLabel.raycastTarget=false;reloadPointLabel.resizeTextForBestFit=true;reloadPointLabel.resizeTextMinSize=18;reloadPointLabel.resizeTextMaxSize=24;
            entrance=factory.Button("OutsideDungeonEntrance","ダンジョン イリグチ",transform,new Vector2(0,.21f),new Vector2(0,340));entrance.GetComponent<RectTransform>().sizeDelta=new Vector2(340,100);entrance.navigation=new Navigation{mode=Navigation.Mode.None};entranceTitle=entrance.GetComponentInChildren<Text>();entranceTitle.rectTransform.anchorMin=entranceTitle.rectTransform.anchorMax=new Vector2(.5f,.5f);entranceTitle.rectTransform.sizeDelta=new Vector2(320,42);entranceTitle.rectTransform.anchoredPosition=new Vector2(0,22);entranceTitle.fontSize=24;entranceTitle.color=new Color(.6f,.94f,1f);entranceTitle.raycastTarget=false;
            entranceActionHint=factory.Label("OutsideEntranceActionHint",entrance.transform,new Vector2(.5f,.5f),new Vector2(0,-23),new Vector2(320,40));entranceActionHint.fontSize=22;entranceActionHint.color=Color.white;entranceActionHint.raycastTarget=false;entranceActionHint.gameObject.SetActive(false);trialRouteHint=factory.Label("OutsideTrialRouteHint",transform,new Vector2(.5f,0),new Vector2(0,75),new Vector2(1100,60));trialRouteHint.fontSize=22;trialRouteHint.color=Color.white;trialRouteHint.resizeTextForBestFit=true;trialRouteHint.resizeTextMinSize=18;trialRouteHint.resizeTextMaxSize=22;
        }
        // 利用判定は呼出側の既存条件を使い、目印と操作案内だけを分離する。
        private void RenderEntranceHint(bool visible,bool available,bool grounded,string key)
        {
            entrance.gameObject.SetActive(visible);entrance.interactable=available;
            // 空中では以前の非表示時と同様に、背面の入力を遮らない。
            entrance.image.raycastTarget=grounded;entrance.image.enabled=grounded;
            bool show=visible&&available&&!string.IsNullOrWhiteSpace(key)&&key!="ミワリアテ"&&key!="未割当";
            entranceActionHint.gameObject.SetActive(show);entranceActionHint.text=show?Olca.Presentation.CustomScreen.CustomDisplay.Katakana("["+key+"] シラベル"):"";
        }
        private static float EntranceTitleOffset(Rect label,Rect actor,Rect viewport)
        {
            if(!label.Overlaps(actor))return 0;
            const float gap=16;float half=label.width*.5f;float left=actor.xMin-gap-half,right=actor.xMax+gap+half;
            bool Fits(float center)=>center-half>=viewport.xMin+gap&&center+half<=viewport.xMax-gap;
            float target=Fits(right)?right:left;
            if(!Fits(target))target=Fits(left)?left:right;
            return target-label.center.x;
        }
        private Rect BoundsInView(RectTransform value)
        {
            var corners=new Vector3[4];value.GetWorldCorners(corners);var min=(Vector2)transform.InverseTransformPoint(corners[0]);var max=min;
            foreach(var point in corners){var local=(Vector2)transform.InverseTransformPoint(point);min=Vector2.Min(min,local);max=Vector2.Max(max,local);}
            return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
        }
        // 操作領域は固定したまま、空中で重なる入口名だけをキャラの横へ避ける。
        private void PlaceEntranceTitle(bool grounded)
        {
            entranceTitle.rectTransform.anchoredPosition=new Vector2(0,22);entranceTitle.text="ダンジョン イリグチ";
            if(grounded||!entrance.gameObject.activeInHierarchy)return;
            float shift=EntranceTitleOffset(BoundsInView(entranceTitle.rectTransform),BoundsInView(player.rectTransform),((RectTransform)transform).rect);
            entranceTitle.rectTransform.anchoredPosition+=new Vector2(shift,0);
            if(shift!=0)entranceTitle.text=shift>0?"< ダンジョン イリグチ":"ダンジョン イリグチ >";
        }
        // ワールド追従の案内だけを避け、固定HUDやゲーム座標は動かさない。
        private void PlaceReloadBelowVitals()
        {
            Rect LocalRect(RectTransform value)=>new Rect((Vector2)value.localPosition+value.rect.min,value.rect.size);
            var hp=LocalRect(outsideHp.rectTransform);var poise=LocalRect(outsidePoise.rectTransform);
            var hud=Rect.MinMaxRect(Mathf.Min(hp.xMin,poise.xMin)-12,Mathf.Min(hp.yMin,poise.yMin)-12,Mathf.Max(hp.xMax,poise.xMax)+12,Mathf.Max(hp.yMax,poise.yMax)+12);
            var point=LocalRect(reloadPoint);if(!point.Overlaps(hud))return;
            var position=reloadPoint.localPosition;position.y-=point.yMax-hud.yMin;reloadPoint.localPosition=position;
        }
        public void Show(Olca.Presentation.Management.ManagedScreenContext owner,Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease lease,Action onBack,Action onPause=null,Func<Olca.Core.Domain.Card,Sprite> icon=null,Func<string> selectionGuide=null,Func<string> movementGuide=null,Action onEntrance=null,Func<bool> canEnter=null,Func<string> interactionHint=null,Func<bool> trialRoute=null,Func<bool> inputBlocked=null,Action<Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease,Olca.Core.Logic.Integration.OutsideExplorationCards,int,long> handPressed=null,Func<string> cardUseGuide=null)
        {
            Hide();this.handPressed=handPressed;this.cardUseGuide=cardUseGuide;transientInputBlocked=inputBlocked;this.trialRoute=trialRoute;this.entered=onEntrance;this.canEnter=canEnter;this.interactionHint=interactionHint;this.selectionGuide=selectionGuide;this.movementGuide=movementGuide;cardIcon=icon;renderedCards=null;handHeading.text="";covered=false;uncoveredFrame=-1;context=owner;presentation=lease;returned=onBack;gameObject.SetActive(true);transform.SetAsLastSibling();
            var captured=owner;var capturedLease=lease;var capturedCards=lease.Cards;BindItemHudUse(owner,lease);
            BindHandSelection(owner,lease,capturedCards);
            entrance.onClick=new Button.ButtonClickedEvent();entrance.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&capturedLease.IsCurrent&&CanPlay&&Olca.Core.Logic.Integration.OutsideTrialRoute.CanInspect(capturedLease.Core)&&(this.canEnter?.Invoke()??false))entered?.Invoke();});
            back.onClick=new Button.ButtonClickedEvent();back.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&capturedLease.IsCurrent&&transientInputBlocked?.Invoke()!=true&&!covered&&Time.frameCount!=uncoveredFrame&&!captured.DialogBlocksInput)returned?.Invoke();});
            if(pause!=null){pause.gameObject.SetActive(onPause!=null);pause.onClick=new Button.ButtonClickedEvent();pause.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&capturedLease.IsCurrent&&CanPlay)onPause?.Invoke();});}
            if(landingViews.Count==0){var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,back.GetComponentInChildren<Text>().font,null);
                foreach(var surface in lease.Core.Definition.LandingSurfaces){var go=new GameObject("OutsideLandingSurface-"+surface.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.sprite=terrain.sprite;image.type=Image.Type.Tiled;image.color=new Color(.8f,.95f,1,.9f);image.raycastTarget=false;var rect=image.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,.21f);rect.pivot=new Vector2(0,1);
                    var label=factory.Label("OutsideLandingLabel-"+surface.Id,transform,new Vector2(0,.21f),Vector2.zero,new Vector2(300,32));label.text="ケンショウ アシバ";label.fontSize=20;label.color=Color.white;label.raycastTarget=false;image.transform.SetSiblingIndex(player.transform.GetSiblingIndex());label.transform.SetSiblingIndex(player.transform.GetSiblingIndex());landingViews.Add((image,label));}}
            menu=new Olca.Unity.MenuPadScope(()=>CanPlay){CancelOnly=true,SuppressPadCancel=true};menu.Bind(transform,back.name,()=>back.onClick.Invoke());UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);Render();
        }
        public void SetCovered(bool value)
        {
            if(covered==value)return;covered=value;
            if(value){ClearItemPresses();menu?.Suspend();ClearFocus();transform.SetAsFirstSibling();}
            else if(IsOpen){uncoveredFrame=Time.frameCount;transform.SetAsLastSibling();menu?.Bind(transform,back.name,()=>back.onClick.Invoke());UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);}
        }
        // 小数表示を切り捨て、整数コストの消費可能量や満量を過大表示しない。
        public static string FormatCardGauge(Olca.Core.Logic.Integration.OutsideCardGauge gauge)
        {
            if(gauge==null)throw new ArgumentNullException(nameof(gauge));
            decimal shown=(gauge.CurrentUnits*10/Olca.Core.Logic.Integration.OutsideCardGauge.UnitsPerPoint)/10m;
            return "カードゲージ  "+shown.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" / "+gauge.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        private Button recoveryPoint;private Action recover;private Func<bool> canRecover;private Func<string> recoveryMessage;private long shownHandRevision=-1;
        public void ConfigureRecovery(Action action,Func<bool> canUse,Func<string> message)
        {
            recover=action;canRecover=canUse;recoveryMessage=message;var captured=context;var lease=presentation;
            recoveryPoint.onClick=new Button.ButtonClickedEvent();recoveryPoint.onClick.AddListener(()=>{if(this!=null&&captured!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&lease!=null&&lease.IsCurrent&&!covered&&!captured.DialogBlocksInput&&canRecover?.Invoke()==true)recover?.Invoke();});
        }
        private void ClearRecovery(){recover=null;canRecover=null;recoveryMessage=null;if(recoveryPoint!=null)recoveryPoint.gameObject.SetActive(false);}
        private sealed class SupplySlot{public Button Button;public int Index;public long X;public bool? Expanded;public string RenderedMessage;public Action Claim;public Func<bool> CanUse;public Func<string> Message;}
        private readonly SupplySlot[] supplySlots=new SupplySlot[4];
        // 遠方は名称/概要の2行だけ。操作範囲や入力可否は既存Core/呼出側に従う。
        private static string CompactSupplyMessage(string value,Text label)
        {
            var settings=label.GetGenerationSettings(Vector2.zero);settings.fontSize=18;settings.resizeTextForBestFit=false;settings.horizontalOverflow=HorizontalWrapMode.Overflow;
            float width=Mathf.Max(0,label.rectTransform.rect.width-2);
            float Measure(string text)=>label.cachedTextGeneratorForLayout.GetPreferredWidth(text,settings)/label.pixelsPerUnit;
            string Fit(string line){line=line.TrimEnd('\r');if(Measure(line)<=width)return line;int n=line.Length;while(n>0){n--;if(n>0&&char.IsHighSurrogate(line[n-1])&&char.IsLowSurrogate(line[n]))n--;string candidate=line.Substring(0,n)+"...";if(Measure(candidate)<=width)return candidate;}return "...";}
            int first=value.IndexOf('\n');if(first<0)return Fit(value);int second=value.IndexOf('\n',first+1);return Fit(value.Substring(0,first))+"\n"+Fit(second<0?value.Substring(first+1):value.Substring(first+1,second-first-1));
        }

        public void ConfigureSupply(Action action,Func<bool> canUse,Func<string> message)=>ConfigureSupplySlot(0,action,canUse,message);
        public void ConfigurePoisonSupply(Action action,Func<bool> canUse,Func<string> message)=>ConfigureSupplySlot(1,action,canUse,message);
        public void ConfigureCardSupply(Action action,Func<bool> canUse,Func<string> message)=>ConfigureSupplySlot(3,action,canUse,message);
        public void ConfigureTrialPoison(Action action,Func<bool> canUse,Func<string> message)=>ConfigureSupplySlot(2,action,canUse,message);
        private void ConfigureSupplySlot(int index,Action action,Func<bool> canUse,Func<string> message)
        {
            var slot=supplySlots[index];slot.Claim=action;slot.CanUse=canUse;slot.Message=message;var captured=context;var lease=presentation;
            slot.Button.onClick=new Button.ButtonClickedEvent();slot.Button.onClick.AddListener(()=>{if(this!=null&&slot.Button!=null&&slot.Button.isActiveAndEnabled&&slot.Button.IsInteractable()&&captured!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&lease!=null&&lease.IsCurrent&&CanPlay&&slot.CanUse?.Invoke()==true)slot.Claim?.Invoke();});
        }
        private void ClearSupply(){foreach(var slot in supplySlots)if(slot!=null){slot.Claim=null;slot.CanUse=null;slot.Message=null;slot.Expanded=null;slot.RenderedMessage=null;if(slot.Button!=null){slot.Button.onClick=new Button.ButtonClickedEvent();slot.Button.gameObject.SetActive(false);}}}
        private Text outsideHp,outsidePoise;private Image outsidePoiseFill;
        private Olca.Presentation.Battle.CombatStatusIconStrip outsideStatusIcons;
        // 戦闘と同じ読取/画像/残時間表示を使い、外界Viewは時計や状態を変更しない。
        private void RefreshOutsideStatusIcons()
        {
            var entries=Olca.Presentation.Battle.CombatStatusIcons.ForPlayer(presentation.Player);
            // 毒の視認用の調整色。既存の確定状態だけを読み、姿勢/alpha/時計は変えない。
            bool poisoned=false;foreach(var entry in entries)if(entry.Icon==Olca.Presentation.Battle.CombatIconId.Poison){poisoned=true;break;}
            if(player!=null)player.color=poisoned?new Color(.84f,.64f,1f,1f):Color.white;
            if(outsideStatusIcons==null){
                if(entries.Count==0)return;
                var row=new GameObject("OutsideCombatStatusIcons",typeof(RectTransform));row.transform.SetParent(transform,false);
                var rect=(RectTransform)row.transform;rect.anchorMin=rect.anchorMax=Vector2.one;rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(-470,-350);
                outsideStatusIcons=row.AddComponent<Olca.Presentation.Battle.CombatStatusIconStrip>();outsideStatusIcons.Configure(5);
            }
            outsideStatusIcons.Show(entries);
        }
        private void BindHandSelection(Olca.Presentation.Management.ManagedScreenContext captured,Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease capturedLease,Olca.Core.Logic.Integration.OutsideExplorationCards capturedCards)
        {
            for(int slot=0;slot<handButtons.Length;slot++){
                int index=slot;var button=handButtons[slot];button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(()=>{
                    if(this==null||button==null||!button.isActiveAndEnabled||!button.IsInteractable()||!ReferenceEquals(context,captured)||!captured.IsCurrent||!capturedLease.IsCurrent||!ReferenceEquals(presentation?.Cards,capturedCards)||capturedCards==null||!CanPlay||capturedLease.IsDeckEditingLocation)return;
                    if(handPressed!=null)handPressed(capturedLease,capturedCards,index,capturedCards.HandRevision);else if(capturedCards.TrySelect(index))Render();
                });
            }
        }
        public void Render()
        {
            if(!IsOpen||player==null)return;
            RefreshItemHud();
            RefreshOutsideStatusIcons();
            // Custom詳細と同じ残強靭度の読取。のけぞり/回復の計算は既存Playerが所有する。
            var poiseActor=presentation.Player;int remainingPoise=poiseActor==null||poiseActor.IsStaggered?0:System.Math.Max(0,poiseActor.Poise-poiseActor.AccumulatedDamage);
            outsidePoise.text=poiseActor==null?"キョウジン —":$"キョウジン {remainingPoise} / {poiseActor.Poise}";outsidePoiseFill.fillAmount=poiseActor==null||poiseActor.Poise<=0?0:Mathf.Clamp01(remainingPoise/(float)poiseActor.Poise);outsidePoiseFill.rectTransform.sizeDelta=new Vector2(430*outsidePoiseFill.fillAmount,6);
            if(outsideHp!=null){var actor=presentation.Player;outsideHp.text=actor==null?"HP —":$"HP  {actor.CurrentHp} / {actor.MaxHp}";}
            var core=presentation.Core;double unit=Olca.Core.Logic.Integration.OutsideMovementDefinition.UnitsPerWorldUnit;
            float worldX=(float)(core.Position/unit),width=(float)(core.Definition.Width/unit);
            float viewport=((RectTransform)transform).rect.width/pixelsPerWorldUnit;
            float camera=presentation.Camera.ResolveHorizontalCenter(core.Position,core.Definition.Width,viewport);
            float verticalOffset=ResolveVerticalViewOffset(core);
            var cards=presentation.Cards;bool editingLocation=presentation.IsDeckEditingLocation;cardUseText.gameObject.SetActive(!editingLocation);string feedback=presentation.CardUseMessageFrame>=0&&core.Frame>=presentation.CardUseMessageFrame&&core.Frame-presentation.CardUseMessageFrame<90?presentation.CardUseMessage:null;cardUseText.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana((cardUseGuide?.Invoke()??"")+(feedback==null?"":"\n"+feedback));movementGuideText.text=movementGuide?.Invoke()??"";selectionGuideText.text=editingLocation?"モドる→タンサク デッキ":cards==null?"":selectionGuide?.Invoke()??"";
            if(!ReferenceEquals(renderedCards,cards)||renderedDeckLocation!=editingLocation||shownHandRevision!=(cards?.HandRevision??-1)||handHeading.text==""){
                if(!ReferenceEquals(renderedCards,cards))BindHandSelection(context,presentation,cards);renderedDeckLocation=editingLocation;renderedCards=cards;shownHandRevision=cards?.HandRevision??-1;handHeading.text=editingLocation?"ヘンセイチテン（カリ）":cards==null?"テフダ  /  デッキ ミトウロク":"テフダ "+cards.Hand.Count+"  /  ヤマフダ "+cards.RemainingCount;
                for(int slot=0;slot<3;slot++){
                    var card=cards!=null&&slot<cards.Hand.Count?cards.Hand[slot]:null;handNames[slot].text=card==null?"—":Olca.Presentation.CustomScreen.CustomDisplay.Katakana(card.Name);handCosts[slot].text=card==null?"":"コスト "+card.GetFinalCost();handIcons[slot].sprite=card==null?null:cardIcon?.Invoke(card);handIcons[slot].enabled=handIcons[slot].sprite!=null;
                }
            }
            for(int slot=0;slot<handButtons.Length;slot++){
                handNames[slot].gameObject.SetActive(!editingLocation);handCosts[slot].gameObject.SetActive(!editingLocation);handIcons[slot].gameObject.SetActive(!editingLocation);handButtons[slot].gameObject.SetActive(cards!=null&&!editingLocation);handButtons[slot].interactable=cards!=null&&slot<cards.Hand.Count&&!editingLocation&&CanPlay;
                handSelectionMarks[slot].gameObject.SetActive(cards!=null&&!editingLocation&&cards.SelectedIndex==slot);
            }
            var gauge=core.CardGauge;
            if(lastGaugeUnits!=gauge.CurrentUnits||lastGaugeMaximum!=gauge.MaximumUnits){lastGaugeUnits=gauge.CurrentUnits;lastGaugeMaximum=gauge.MaximumUnits;gaugeLabel.text=FormatCardGauge(gauge);gaugeFill.sizeDelta=new Vector2(396*(gauge.CurrentUnits/(float)gauge.MaximumUnits),6);}

            float Origin(float x)=>(x-camera+viewport*.5f)*pixelsPerWorldUnit;
            bool reloadNearby=Olca.Core.Logic.Integration.OutsideTrialRoute.IsReloadZone(core);reloadPoint.gameObject.SetActive(trialRoute?.Invoke()==true);reloadPoint.anchoredPosition=new Vector2(Origin((float)(Olca.Core.Logic.Integration.OutsideTrialRoute.ReloadX/unit)),520-verticalOffset*pixelsPerWorldUnit);reloadPoint.sizeDelta=reloadPointLabel.rectTransform.sizeDelta=reloadNearby?new Vector2(350,100):new Vector2(210,48);reloadPointLabel.text=reloadNearby?"リロード（カリ）\nトオるとデッキ・ゲージカイフク\nモドる→デッキ":"リロード（カリ）";PlaceReloadBelowVitals();
            trialDoor.gameObject.SetActive(entered!=null&&trialRoute?.Invoke()==true);trialDoor.rectTransform.anchoredPosition=new Vector2(Origin((float)(Olca.Core.Logic.Integration.OutsideTrialRoute.EntranceX/unit)),-verticalOffset*pixelsPerWorldUnit);trialRouteHint.gameObject.SetActive(trialRoute?.Invoke()==true);trialRouteHint.text=core.TraversalProfile?.WallActionsUnlocked==true?"シヨウチケイ / ミギ18: ソード ヒョウテキ（カリ） / イリグチ・カベ":"シヨウチケイ / ミギ: ダンジョン イリグチ / カベハ ミカイホウ";
            if(entrance!=null){entrance.GetComponent<RectTransform>().anchoredPosition=new Vector2(Origin((float)(Olca.Core.Logic.Integration.OutsideTrialRoute.EntranceX/unit)),340-verticalOffset*pixelsPerWorldUnit);RenderEntranceHint(entered!=null&&trialRoute?.Invoke()==true,CanPlay&&Olca.Core.Logic.Integration.OutsideTrialRoute.CanInspect(core)&&(canEnter?.Invoke()??false),core.IsGrounded,interactionHint?.Invoke());}

            foreach(var slot in supplySlots)if(slot?.Button!=null){
                slot.Button.gameObject.SetActive(slot.Claim!=null&&trialRoute?.Invoke()==true);var rect=slot.Button.GetComponent<RectTransform>();rect.anchoredPosition=new Vector2(Origin((float)(slot.X/unit)),(slot.Index==3?510:340)-verticalOffset*pixelsPerWorldUnit);slot.Button.interactable=slot.CanUse?.Invoke()==true;
                // 遠方・使用済みでも案内を読めるようにする。操作可能時の既存配色は維持する。
                slot.Button.GetComponentInChildren<Text>().color=slot.Button.interactable?Olca.Presentation.Dungeon.UiLook.Ink:Color.white;
                bool expanded=slot.Index==0?Olca.Core.Logic.Integration.OutsideTrialRoute.CanClaimSupply(core):slot.Index==1?Olca.Core.Logic.Integration.OutsideTrialRoute.CanClaimPoisonSupply(core):slot.Index==2?Olca.Core.Logic.Integration.OutsideTrialRoute.CanTryPoison(core):Olca.Core.Logic.Integration.OutsideTrialRoute.CanClaimCardSupply(core);
                var message=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(slot.Message?.Invoke()??"ホキュウバコ（カリ）");
                if(slot.Expanded!=expanded||slot.RenderedMessage!=message){var label=slot.Button.GetComponentInChildren<Text>();rect.sizeDelta=expanded?new Vector2(420,140):new Vector2(320,82);label.resizeTextForBestFit=true;label.resizeTextMinSize=expanded?18:16;label.resizeTextMaxSize=expanded?24:18;label.text=expanded?message:CompactSupplyMessage(message,label);slot.Expanded=expanded;slot.RenderedMessage=message;}
            }

            if(recoveryPoint!=null){recoveryPoint.gameObject.SetActive(recover!=null&&trialRoute?.Invoke()==true);recoveryPoint.GetComponent<RectTransform>().anchoredPosition=new Vector2(Origin((float)(Olca.Core.Logic.Integration.OutsideTrialRoute.RecoveryX/unit)),340-verticalOffset*pixelsPerWorldUnit);recoveryPoint.interactable=canRecover?.Invoke()==true;var label=recoveryPoint.GetComponentInChildren<Text>();label.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(recoveryMessage?.Invoke()??"ケンショウ カイフク（カリ）");label.resizeTextForBestFit=true;label.resizeTextMinSize=18;label.resizeTextMaxSize=24;}
            Sprite sprite=TraversalSprite(core);
            if(sprite==null){sprite=idleSprite;
            if(!core.IsGrounded)sprite=System.Math.Abs(core.VerticalVelocity)<=Olca.Core.Logic.Integration.OutsideExplorationCore.ValidationGravity?apexSprite:core.VerticalVelocity>0?riseSprite:fallSprite;
            else{var clip=core.IsMoving?moveClip:idleClip;if(clip!=null&&motionSample!=null){long frame=core.IsMoving?core.MovementFrame:core.IdleFrame;clip.SampleAnimation(motionSample.gameObject,(float)((frame/60d)%clip.length));sprite=motionSample.sprite;}}
            }
            if(sprite==null)sprite=idleSprite;
            player.sprite=sprite;player.rectTransform.pivot=sprite.pivot/sprite.rect.size;player.rectTransform.sizeDelta=new Vector2(sprite.bounds.size.x,sprite.bounds.size.y)*pixelsPerWorldUnit;
            player.rectTransform.anchoredPosition=new Vector2(Origin(worldX),(float)(core.Height/unit-verticalOffset)*pixelsPerWorldUnit);player.rectTransform.localScale=new Vector3(-core.Facing,1,1);PlaceEntranceTitle(core.IsGrounded);
            RenderTraversal(core,camera,viewport,verticalOffset);RenderTrialSword(camera,viewport,verticalOffset);RenderMapName();
            float viewportPixels=((RectTransform)transform).rect.width;
            void Boundary(Text label,float x,bool left)
            {
                float half=label.rectTransform.rect.width*.5f;bool visible=viewportPixels>=half*2&&x>=0&&x<=viewportPixels;
                label.gameObject.SetActive(visible);
                if(visible)label.rectTransform.anchoredPosition=new Vector2(Mathf.Clamp(x+(left?half:-half),half,viewportPixels-half),-35-verticalOffset*pixelsPerWorldUnit);
            }
            for(int i=0;i<landingViews.Count;i++){var surface=core.Definition.LandingSurfaces[i];var view=landingViews[i];float x=Origin((float)(surface.Left/unit)),y=(float)(surface.Top/unit-verticalOffset)*pixelsPerWorldUnit;view.image.rectTransform.anchoredPosition=new Vector2(x,y);view.image.rectTransform.sizeDelta=new Vector2((float)((surface.Right-surface.Left)/unit)*pixelsPerWorldUnit,18);view.label.rectTransform.anchoredPosition=new Vector2(x+150,y-42);}
            Boundary(leftEnd,Origin(0),true);Boundary(rightEnd,Origin(width),false);
            RenderCombatFeedback(camera,verticalOffset);
            // border0の共有素材。画像の実UIタイル周期でwrapし、投影108とCanvas参照100を混同しない。
            float tile=terrain.sprite.rect.width/(terrain.pixelsPerUnit*terrain.pixelsPerUnitMultiplier);
            float shift=tile>0?Mathf.Repeat((camera-viewport*.5f)*pixelsPerWorldUnit,tile):0;
            terrain.rectTransform.offsetMin=new Vector2(-shift,-verticalOffset*pixelsPerWorldUnit);terrain.rectTransform.offsetMax=new Vector2(tile-shift,-verticalOffset*pixelsPerWorldUnit);
            terrain.enabled=((RectTransform)transform).rect.height*.21f>verticalOffset*pixelsPerWorldUnit;
        }
        private void ClearFocus(){var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;if(this!=null&&selected!=null&&selected.transform.IsChildOf(transform))UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);}
        private void ClearHandActions(){foreach(var button in handButtons)if(button!=null)button.onClick=new Button.ButtonClickedEvent();}
        private void ClearHandIcons(){foreach(var image in handIcons)if(image!=null){image.sprite=null;image.enabled=false;}}
        public void Hide(){if(player!=null)player.color=Color.white;if(outsideStatusIcons!=null)outsideStatusIcons.Clear();ClearCombatFeedback();ClearTrialSword();ClearRecovery();ClearSupply();ClearItemProviders();ClearTraversalDisplay();ClearHandActions();ClearFocus();menu?.Dispose();menu=null;presentation?.Dispose();presentation=null;context=null;returned=null;entered=null;canEnter=null;interactionHint=null;trialRoute=null;transientInputBlocked=null;cardIcon=null;selectionGuide=null;movementGuide=null;handPressed=null;cardUseGuide=null;renderedCards=null;ClearHandIcons();if(this!=null)gameObject.SetActive(false);}
        private void LateUpdate(){if(context!=null&&!context.IsCurrent){Hide();return;}menu?.Tick();}
        private void OnDisable(){if(player!=null)player.color=Color.white;ClearCombatFeedback();ClearTrialSword();ClearRecovery();ClearSupply();ClearItemProviders();ClearTraversalDisplay();ClearHandActions();ClearFocus();context?.NotifyHidden();menu?.Dispose();menu=null;presentation?.Dispose();presentation=null;context=null;returned=null;entered=null;canEnter=null;interactionHint=null;trialRoute=null;transientInputBlocked=null;cardIcon=null;selectionGuide=null;movementGuide=null;handPressed=null;cardUseGuide=null;renderedCards=null;ClearHandIcons();}
        private void OnDestroy(){OnDisable();ReleaseTraversalReferences();back=null;pause=null;}
    }
}

namespace Olca.Presentation.Management
{
    /// <summary>起動の表示だけ。保存判断やhost生成は注入された開始要求へ委譲する。</summary>
    public sealed class BootstrapTitleView:MonoBehaviour
    {
        private ManagedScreenContext context;private Action hidden;private Button start,newGame;private Text status;private Olca.Unity.MenuPadScope menu;
        private GameplayPanelFactory factory;private ProgressActionDialog newGameDialog;private bool busy,canContinue,canCreate;private string hint,statusError;private Text inputGuide;private GameObject newGameFocus,startFocus;
        public static GameObject Create(Transform parent,Olca.Unity.ResourceManager resources){
            var root=new GameObject("BootstrapTitle",typeof(RectTransform),typeof(Image));root.SetActive(false);root.transform.SetParent(parent,false);var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;root.GetComponent<Image>().color=new Color(.015f,.04f,.06f,1);
            try{var view=root.AddComponent<BootstrapTitleView>();var source=resources.GetShared<GameObject>("OlcaSystem/WorldPresentation");Sprite background=null;foreach(var sr in source.GetComponentsInChildren<SpriteRenderer>(true))if(sr.name=="DungeonBackground")background=sr.sprite;if(background==null)throw new InvalidOperationException("タイトルの共有背景がありません");
                var bg=new GameObject("TitleOriginalBackground",typeof(RectTransform),typeof(Image),typeof(AspectRatioFitter));bg.transform.SetParent(root.transform,false);var image=bg.GetComponent<Image>();image.sprite=background;image.raycastTarget=false;var br=image.rectTransform;br.anchorMin=Vector2.zero;br.anchorMax=Vector2.one;br.offsetMin=br.offsetMax=Vector2.zero;var fit=bg.GetComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=background.rect.width/background.rect.height;
                var factory=view.factory=new GameplayPanelFactory(root.transform,resources.GetShared<Font>("OlcaUI/MenuText"),null);var title=factory.Label("TitleHeading",root.transform,new Vector2(.5f,.5f),new Vector2(0,200),new Vector2(1000,130));title.text="OLCA";title.fontSize=80;title.color=Color.white;
                view.newGame=factory.Button("TitleNewGame","はじめから",root.transform,new Vector2(.5f,.5f),new Vector2(0,30));view.start=factory.Button("TitleStart","つづきから",root.transform,new Vector2(.5f,.5f),new Vector2(0,-90));
                foreach(var button in new[]{view.newGame,view.start})button.GetComponent<RectTransform>().sizeDelta=new Vector2(460,90);
                view.status=factory.Label("TitleStatus",root.transform,new Vector2(.5f,.5f),new Vector2(0,-240),new Vector2(1100,150));view.status.color=Color.white;view.status.fontSize=24;view.newGameFocus=AddFocusFrame(view.newGame);view.startFocus=AddFocusFrame(view.start);
                view.inputGuide=factory.Label("TitleInputGuide",root.transform,new Vector2(.5f,.5f),new Vector2(0,-365),new Vector2(1400,100));view.inputGuide.color=Color.white;view.inputGuide.fontSize=24;return root;
            }catch{if(Application.isPlaying)Destroy(root);else DestroyImmediate(root);throw;}
        }
        // 既存のView寿命検証向け互換入口。通常起動は保存判定を渡す。
        public void Show(ManagedScreenContext value,Action begin,Action hidden=null)=>Show(value,begin,null,true,false,null,hidden);
        public void Show(ManagedScreenContext value,Action resume,Action create,bool continueAvailable,bool createAvailable,string message,Action hidden=null)
        {
            Hide();context=value;this.hidden=hidden;canContinue=continueAvailable;canCreate=createAvailable;hint=message??(continueAvailable?"ホゾンした シンコウから サイカイします":"ツヅキの セーブが ありません");
            void Bind(Button button,Action action){button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(()=>{if(this!=null&&context==value&&value.IsCurrent&&!value.DialogBlocksInput&&button.IsInteractable())action?.Invoke();});}
            Bind(start,resume);Bind(newGame,create);SetBusy(false,null);gameObject.SetActive(true);transform.SetAsLastSibling();menu=new Olca.Unity.MenuPadScope(()=>context?.IsCurrent==true&&!context.DialogBlocksInput&&!busy);var focus=canContinue?start:newGame;menu.Bind(transform,focus.name,null);UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(focus.IsInteractable()?focus.gameObject:null);
        }
        public void SetContinueAvailable(){canContinue=true;hint="ホゾンした シンコウから サイカイします";SetBusy(busy,null);}
        public void OpenNewGame(IProgressActionOperations operations,DialogManager manager,Action saved)
        {
            if(context?.IsCurrent!=true||busy||context.DialogBlocksInput||!canCreate)return;var owner=context;
            if(newGameDialog==null){var panel=factory.Panel("TitleNewGameConfirmation",true);panel.GetComponent<Image>().color=new Color(.025f,.07f,.10f,1f);newGameDialog=panel.AddComponent<ProgressActionDialog>();newGameDialog.Initialize(factory);newGameDialog.ConfigureTitlePresentation(factory);}
            // 再表示時にも同じserviceの未保存計画を使う。保存/初期化そのものはDialogへ複製しない。
            newGameDialog.Completed-=completed;completed=result=>{if(this==null||context!=owner||!owner.IsCurrent)return;if(result.Outcome==ProgressActionOutcome.NewGameStarted){SetContinueAvailable();saved?.Invoke();}};newGameDialog.Completed+=completed;
            newGameDialog.OpenNewGame(operations,manager,owner,()=>this!=null&&context==owner&&owner.IsCurrent);
        }
        private Action<ProgressActionResult> completed;
        // タイトルだけの表示。開始/保存の判断や入力処理は既存の経路を使う。
        internal static GameObject AddFocusFrame(Button button)
        {
            var frame=new GameObject("TitleSelectionFrame",typeof(RectTransform));frame.transform.SetParent(button.transform,false);var rect=(RectTransform)frame.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=new Vector2(-5,-5);rect.offsetMax=new Vector2(5,5);
            void Edge(string name,Vector2 min,Vector2 max,Vector2 size){var edge=new GameObject(name,typeof(RectTransform),typeof(Image));edge.transform.SetParent(frame.transform,false);var image=edge.GetComponent<Image>();image.color=new Color(.3f,.95f,1f);image.raycastTarget=false;var area=(RectTransform)edge.transform;area.anchorMin=min;area.anchorMax=max;area.anchoredPosition=Vector2.zero;area.sizeDelta=size;}
            Edge("Top",new Vector2(0,1),Vector2.one,new Vector2(0,4));Edge("Bottom",Vector2.zero,new Vector2(1,0),new Vector2(0,4));Edge("Left",Vector2.zero,new Vector2(0,1),new Vector2(4,0));Edge("Right",new Vector2(1,0),Vector2.one,new Vector2(4,0));frame.SetActive(false);return frame;
        }
        public static string InputGuideText(Olca.Core.Domain.InputBindingProfile profile,bool gamepad,bool confirmation=false,bool canCancel=true)
        {
            string Label(string action)=>Olca.Unity.UnityInputProvider.GamepadBindingLabel(profile!=null&&profile.Bindings.TryGetValue(("gamepad","menu",action),out var value)?value:null);
            string direction=gamepad?"ホウコウ":confirmation?"← / →":"↑ / ↓";
            string decide=gamepad?Label("Decide"):"Enter",cancel=gamepad?Label("Cancel"):"Esc";
            return direction+" : センタク    "+decide+" : ケッテイ"+(canCancel?"\n"+cancel+(confirmation?" : トリケシ":" : カクニンガメンで モドる"):"\nホゾンの サイシコウが ヒツヨウです");
        }
        public void RefreshPresentation()
        {
            if(status==null)return;var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;string focus=selected!=null?selected.name:menu?.FocusName;
            bool visible=context?.IsCurrent==true&&!context.DialogBlocksInput&&!busy;
            newGameFocus.SetActive(visible&&focus==newGame.name&&newGame.IsInteractable());startFocus.SetActive(visible&&focus==start.name&&start.IsInteractable());
            status.text=busy?"ヨミコミチュウ":statusError!=null?Olca.Presentation.CustomScreen.CustomDisplay.Katakana(statusError):!canCreate?hint:focus==newGame.name?"アタラしい シンコウを カイシします。\nカクニンガメンで トリケシできます。"+(!canContinue?"\nツヅキの セーブが ありません":""):hint;
            inputGuide.gameObject.SetActive(visible);if(visible)inputGuide.text=InputGuideText(Olca.Unity.UnityInputProvider.ActiveGamepadProfile,Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Gamepad);
        }
        public void SetBusy(bool value,string error){busy=value;statusError=error;start.interactable=!busy&&canContinue;newGame.interactable=!busy&&canCreate;RefreshPresentation();}
        public void Hide(){newGameDialog?.Abort();var ended=hidden;hidden=null;ended?.Invoke();menu?.Dispose();menu=null;context=null;if(start!=null)start.onClick=new Button.ButtonClickedEvent();if(newGame!=null)newGame.onClick=new Button.ButtonClickedEvent();if(this!=null)gameObject.SetActive(false);}
        private void LateUpdate(){newGameDialog?.RefreshLifetime();menu?.Tick();RefreshPresentation();}private void OnDisable(){newGameDialog?.Abort();var ended=hidden;hidden=null;ended?.Invoke();menu?.Dispose();menu=null;context?.NotifyHidden();context=null;}private void OnDestroy(){OnDisable();completed=null;}
    }
}
