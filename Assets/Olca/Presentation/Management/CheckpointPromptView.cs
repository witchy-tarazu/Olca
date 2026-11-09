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
        private Action returned;
        private Func<Olca.Core.Domain.Card,Sprite> cardIcon;
        private Olca.Core.Logic.Integration.OutsideExplorationCards renderedCards;
        private Text handHeading,selectionGuideText,movementGuideText;private Func<string> selectionGuide,movementGuide;
        private readonly Text[] handNames=new Text[3],handCosts=new Text[3];
        private readonly Image[] handIcons=new Image[3],handSelectionMarks=new Image[3];
        private readonly Button[] handButtons=new Button[3];
        private Sprite idleSprite,riseSprite,apexSprite,fallSprite;private AnimationClip moveClip,idleClip;private SpriteRenderer motionSample;
        private readonly System.Collections.Generic.List<(Image image,Text label)> landingViews=new System.Collections.Generic.List<(Image,Text)>();
        private Image player,terrain;private Text leftEnd,rightEnd,gaugeLabel;private RectTransform gaugeFill;private long lastGaugeUnits=-1,lastGaugeMaximum=-1;private float pixelsPerWorldUnit;
        public bool CanPlay=>IsOpen&&!covered&&Time.frameCount!=uncoveredFrame&&!context.DialogBlocksInput&&Olca.Unity.MenuPadScope.IsTopmost(transform);
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
            InitializeTraversal(resources,field);
            var header=Picture("OutsideHeadingSurface",resources.GetShared<Sprite>("OlcaUI/window"),new Vector2(0,1),Vector2.one);header.type=Image.Type.Sliced;header.rectTransform.pivot=new Vector2(.5f,1);header.rectTransform.sizeDelta=new Vector2(0,120);
            var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,font,null);var title=factory.Label("OutsideExplorationTitle",transform,new Vector2(0,1),new Vector2(630,-62),new Vector2(1180,70));title.text="ガイカイ タンサク  /  ケンショウ チケイ";title.alignment=TextAnchor.MiddleLeft;title.fontSize=28;title.color=Olca.Presentation.Dungeon.UiLook.Ink;title.rectTransform.anchorMin=new Vector2(0,1);title.rectTransform.anchorMax=Vector2.one;title.rectTransform.offsetMin=new Vector2(40,-97);title.rectTransform.offsetMax=new Vector2(-580,-27);
            gaugeLabel=factory.Label("OutsideCardGaugeLabel",transform,new Vector2(0,1),new Vector2(240,-148),new Vector2(400,28));gaugeLabel.fontSize=22;gaugeLabel.alignment=TextAnchor.MiddleLeft;gaugeLabel.color=Color.white;gaugeLabel.raycastTarget=false;gaugeLabel.horizontalOverflow=HorizontalWrapMode.Wrap;gaugeLabel.verticalOverflow=VerticalWrapMode.Truncate;gaugeLabel.resizeTextForBestFit=true;gaugeLabel.resizeTextMinSize=18;gaugeLabel.resizeTextMaxSize=22;
            var track=new GameObject("OutsideCardGaugeTrack",typeof(RectTransform),typeof(Image));track.transform.SetParent(transform,false);var trackImage=track.GetComponent<Image>();trackImage.color=new Color(.05f,.14f,.2f,.94f);trackImage.raycastTarget=false;var trackRect=track.GetComponent<RectTransform>();trackRect.anchorMin=trackRect.anchorMax=new Vector2(0,1);trackRect.pivot=new Vector2(0,.5f);trackRect.anchoredPosition=new Vector2(40,-174);trackRect.sizeDelta=new Vector2(400,10);
            var fill=new GameObject("OutsideCardGaugeFill",typeof(RectTransform),typeof(Image));fill.transform.SetParent(track.transform,false);var fillImage=fill.GetComponent<Image>();fillImage.color=new Color(.55f,.88f,1,1);fillImage.raycastTarget=false;gaugeFill=fill.GetComponent<RectTransform>();gaugeFill.anchorMin=gaugeFill.anchorMax=new Vector2(0,.5f);gaugeFill.pivot=new Vector2(0,.5f);gaugeFill.anchoredPosition=new Vector2(2,0);gaugeFill.sizeDelta=new Vector2(396,6);
            movementGuideText=factory.Label("OutsideMovementGuide",transform,new Vector2(0,1),new Vector2(380,-238),new Vector2(680,116));movementGuideText.fontSize=18;movementGuideText.alignment=TextAnchor.MiddleLeft;movementGuideText.color=Color.white;movementGuideText.raycastTarget=false;movementGuideText.horizontalOverflow=HorizontalWrapMode.Wrap;movementGuideText.verticalOverflow=VerticalWrapMode.Truncate;movementGuideText.resizeTextForBestFit=true;movementGuideText.resizeTextMinSize=16;movementGuideText.resizeTextMaxSize=18;
            handHeading=factory.Label("OutsideHandHeading",transform,Vector2.one,new Vector2(-365,-145),new Vector2(650,28));handHeading.fontSize=20;handHeading.alignment=TextAnchor.MiddleLeft;handHeading.color=Color.white;handHeading.raycastTarget=false;
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
            leftEnd=factory.Label("OutsideLeftBoundary",transform,new Vector2(0,.21f),new Vector2(0,-45),new Vector2(140,60));leftEnd.text="ヒダリ ハシ";leftEnd.fontSize=24;leftEnd.color=Color.white;leftEnd.raycastTarget=false;
            rightEnd=factory.Label("OutsideRightBoundary",transform,new Vector2(0,.21f),new Vector2(0,-45),new Vector2(140,60));rightEnd.text="ミギ ハシ";rightEnd.fontSize=24;rightEnd.color=Color.white;rightEnd.raycastTarget=false;
            pause=factory.Button("PauseOutsideExploration","ポーズ",transform,Vector2.one,new Vector2(-430,-62));Olca.Presentation.Dungeon.UiLook.Button(pause);
            back=factory.Button("CloseOutsideExploration","戻る",transform,Vector2.one,new Vector2(-160,-62));Olca.Presentation.Dungeon.UiLook.Button(back);
        }
        public void Show(Olca.Presentation.Management.ManagedScreenContext owner,Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease lease,Action onBack,Action onPause=null,Func<Olca.Core.Domain.Card,Sprite> icon=null,Func<string> selectionGuide=null,Func<string> movementGuide=null)
        {
            Hide();this.selectionGuide=selectionGuide;this.movementGuide=movementGuide;cardIcon=icon;renderedCards=null;handHeading.text="";covered=false;uncoveredFrame=-1;context=owner;presentation=lease;returned=onBack;gameObject.SetActive(true);transform.SetAsLastSibling();
            var captured=owner;var capturedLease=lease;var capturedCards=lease.Cards;
            for(int slot=0;slot<handButtons.Length;slot++){
                int index=slot;var button=handButtons[slot];button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(()=>{
                    if(this==null||button==null||!button.isActiveAndEnabled||!button.IsInteractable()||!ReferenceEquals(context,captured)||!captured.IsCurrent||!capturedLease.IsCurrent||!ReferenceEquals(presentation?.Cards,capturedCards)||capturedCards==null||!CanPlay)return;
                    if(capturedCards.TrySelect(index))Render();
                });
            }
            back.onClick=new Button.ButtonClickedEvent();back.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&capturedLease.IsCurrent&&!covered&&Time.frameCount!=uncoveredFrame&&!captured.DialogBlocksInput)returned?.Invoke();});
            if(pause!=null){pause.gameObject.SetActive(onPause!=null);pause.onClick=new Button.ButtonClickedEvent();pause.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&capturedLease.IsCurrent&&CanPlay)onPause?.Invoke();});}
            if(landingViews.Count==0){var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,back.GetComponentInChildren<Text>().font,null);
                foreach(var surface in lease.Core.Definition.LandingSurfaces){var go=new GameObject("OutsideLandingSurface-"+surface.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.sprite=terrain.sprite;image.type=Image.Type.Tiled;image.color=new Color(.8f,.95f,1,.9f);image.raycastTarget=false;var rect=image.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,.21f);rect.pivot=new Vector2(0,1);
                    var label=factory.Label("OutsideLandingLabel-"+surface.Id,transform,new Vector2(0,.21f),Vector2.zero,new Vector2(300,32));label.text="ケンショウ アシバ";label.fontSize=20;label.color=Color.white;label.raycastTarget=false;image.transform.SetSiblingIndex(player.transform.GetSiblingIndex());label.transform.SetSiblingIndex(player.transform.GetSiblingIndex());landingViews.Add((image,label));}}
            menu=new Olca.Unity.MenuPadScope(()=>CanPlay){CancelOnly=true};menu.Bind(transform,back.name,()=>back.onClick.Invoke());UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);Render();
        }
        public void SetCovered(bool value)
        {
            if(covered==value)return;covered=value;
            if(value){menu?.Suspend();ClearFocus();transform.SetAsFirstSibling();}
            else if(IsOpen){uncoveredFrame=Time.frameCount;transform.SetAsLastSibling();menu?.Bind(transform,back.name,()=>back.onClick.Invoke());UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);}
        }
        // 小数表示を切り捨て、整数コストの消費可能量や満量を過大表示しない。
        public static string FormatCardGauge(Olca.Core.Logic.Integration.OutsideCardGauge gauge)
        {
            if(gauge==null)throw new ArgumentNullException(nameof(gauge));
            decimal shown=(gauge.CurrentUnits*10/Olca.Core.Logic.Integration.OutsideCardGauge.UnitsPerPoint)/10m;
            return "カードゲージ  "+shown.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+" / "+gauge.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        public void Render()
        {
            if(!IsOpen||player==null)return;
            var core=presentation.Core;double unit=Olca.Core.Logic.Integration.OutsideMovementDefinition.UnitsPerWorldUnit;
            float worldX=(float)(core.Position/unit),width=(float)(core.Definition.Width/unit);
            float viewport=((RectTransform)transform).rect.width/pixelsPerWorldUnit;
            float camera=presentation.Camera.ResolveHorizontalCenter(core.Position,core.Definition.Width,viewport);
            float verticalOffset=ResolveVerticalViewOffset(core);
            var cards=presentation.Cards;movementGuideText.text=movementGuide?.Invoke()??"";selectionGuideText.text=cards==null?"":selectionGuide?.Invoke()??"";
            if(!ReferenceEquals(renderedCards,cards)||handHeading.text==""){
                renderedCards=cards;handHeading.text=cards==null?"テフダ  /  デッキ ミトウロク":"テフダ 3  /  ヤマフダ "+cards.RemainingCount;
                for(int slot=0;slot<3;slot++){
                    var card=cards?.Hand[slot];handNames[slot].text=card==null?"—":Olca.Presentation.CustomScreen.CustomDisplay.Katakana(card.Name);handCosts[slot].text=card==null?"":"コスト "+card.GetFinalCost();handIcons[slot].sprite=card==null?null:cardIcon?.Invoke(card);handIcons[slot].enabled=handIcons[slot].sprite!=null;
                }
            }
            for(int slot=0;slot<handButtons.Length;slot++){
                handButtons[slot].gameObject.SetActive(cards!=null);handButtons[slot].interactable=cards!=null&&CanPlay;
                handSelectionMarks[slot].gameObject.SetActive(cards!=null&&cards.SelectedIndex==slot);
            }
            var gauge=core.CardGauge;
            if(lastGaugeUnits!=gauge.CurrentUnits||lastGaugeMaximum!=gauge.MaximumUnits){lastGaugeUnits=gauge.CurrentUnits;lastGaugeMaximum=gauge.MaximumUnits;gaugeLabel.text=FormatCardGauge(gauge);gaugeFill.sizeDelta=new Vector2(396*(gauge.CurrentUnits/(float)gauge.MaximumUnits),6);}

            float Origin(float x)=>(x-camera+viewport*.5f)*pixelsPerWorldUnit;
            Sprite sprite=TraversalSprite(core);
            if(sprite==null){sprite=idleSprite;
            if(!core.IsGrounded)sprite=System.Math.Abs(core.VerticalVelocity)<=Olca.Core.Logic.Integration.OutsideExplorationCore.ValidationGravity?apexSprite:core.VerticalVelocity>0?riseSprite:fallSprite;
            else{var clip=core.IsMoving?moveClip:idleClip;if(clip!=null&&motionSample!=null){long frame=core.IsMoving?core.MovementFrame:core.IdleFrame;clip.SampleAnimation(motionSample.gameObject,(float)((frame/60d)%clip.length));sprite=motionSample.sprite;}}
            }
            if(sprite==null)sprite=idleSprite;
            player.sprite=sprite;player.rectTransform.pivot=sprite.pivot/sprite.rect.size;player.rectTransform.sizeDelta=new Vector2(sprite.bounds.size.x,sprite.bounds.size.y)*pixelsPerWorldUnit;
            player.rectTransform.anchoredPosition=new Vector2(Origin(worldX),(float)(core.Height/unit-verticalOffset)*pixelsPerWorldUnit);player.rectTransform.localScale=new Vector3(core.IsWallKicking&&!core.IsWallSliding?core.Facing:-core.Facing,1,1);
            RenderTraversal(core,camera,viewport,verticalOffset);
            float viewportPixels=((RectTransform)transform).rect.width;
            void Boundary(Text label,float x,bool left)
            {
                float half=label.rectTransform.rect.width*.5f;bool visible=viewportPixels>=half*2&&x>=0&&x<=viewportPixels;
                label.gameObject.SetActive(visible);
                if(visible)label.rectTransform.anchoredPosition=new Vector2(Mathf.Clamp(x+(left?half:-half),half,viewportPixels-half),-45-verticalOffset*pixelsPerWorldUnit);
            }
            for(int i=0;i<landingViews.Count;i++){var surface=core.Definition.LandingSurfaces[i];var view=landingViews[i];float x=Origin((float)(surface.Left/unit)),y=(float)(surface.Top/unit-verticalOffset)*pixelsPerWorldUnit;view.image.rectTransform.anchoredPosition=new Vector2(x,y);view.image.rectTransform.sizeDelta=new Vector2((float)((surface.Right-surface.Left)/unit)*pixelsPerWorldUnit,18);view.label.rectTransform.anchoredPosition=new Vector2(x+150,y-42);}
            Boundary(leftEnd,Origin(0),true);Boundary(rightEnd,Origin(width),false);
            // border0の共有素材。画像の実UIタイル周期でwrapし、投影108とCanvas参照100を混同しない。
            float tile=terrain.sprite.rect.width/(terrain.pixelsPerUnit*terrain.pixelsPerUnitMultiplier);
            float shift=tile>0?Mathf.Repeat((camera-viewport*.5f)*pixelsPerWorldUnit,tile):0;
            terrain.rectTransform.offsetMin=new Vector2(-shift,-verticalOffset*pixelsPerWorldUnit);terrain.rectTransform.offsetMax=new Vector2(tile-shift,-verticalOffset*pixelsPerWorldUnit);
            terrain.enabled=((RectTransform)transform).rect.height*.21f>verticalOffset*pixelsPerWorldUnit;
        }
        private void ClearFocus(){var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;if(this!=null&&selected!=null&&selected.transform.IsChildOf(transform))UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);}
        private void ClearHandActions(){foreach(var button in handButtons)if(button!=null)button.onClick=new Button.ButtonClickedEvent();}
        private void ClearHandIcons(){foreach(var image in handIcons)if(image!=null){image.sprite=null;image.enabled=false;}}
        public void Hide(){ClearTraversalDisplay();ClearHandActions();ClearFocus();menu?.Dispose();menu=null;presentation?.Dispose();presentation=null;context=null;returned=null;cardIcon=null;selectionGuide=null;movementGuide=null;renderedCards=null;ClearHandIcons();if(this!=null)gameObject.SetActive(false);}
        private void LateUpdate(){if(context!=null&&!context.IsCurrent){Hide();return;}menu?.Tick();}
        private void OnDisable(){ClearTraversalDisplay();ClearHandActions();ClearFocus();context?.NotifyHidden();menu?.Dispose();menu=null;presentation?.Dispose();presentation=null;context=null;returned=null;cardIcon=null;selectionGuide=null;movementGuide=null;renderedCards=null;ClearHandIcons();}
        private void OnDestroy(){OnDisable();ReleaseTraversalReferences();back=null;pause=null;}
    }
}
