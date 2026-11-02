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
    public sealed class OutsideExplorationView : MonoBehaviour
    {
        private Olca.Presentation.Management.ManagedScreenContext context;
        private Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease presentation;
        private Olca.Unity.MenuPadScope menu;
        private Button back,pause;private bool covered;private int uncoveredFrame=-1;
        private Action returned;
        private Sprite idleSprite,riseSprite,apexSprite,fallSprite;private AnimationClip moveClip,idleClip;private SpriteRenderer motionSample;
        private Image player,terrain;private Text leftEnd,rightEnd;private float pixelsPerWorldUnit;
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
            var header=Picture("OutsideHeadingSurface",resources.GetShared<Sprite>("OlcaUI/window"),new Vector2(0,1),Vector2.one);header.type=Image.Type.Sliced;header.rectTransform.pivot=new Vector2(.5f,1);header.rectTransform.sizeDelta=new Vector2(0,120);
            var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,font,null);var title=factory.Label("OutsideExplorationTitle",transform,new Vector2(0,1),new Vector2(630,-62),new Vector2(1180,70));title.text="ガイカイ タンサク  /  ケンショウ チケイ";title.alignment=TextAnchor.MiddleLeft;title.fontSize=28;title.color=Olca.Presentation.Dungeon.UiLook.Ink;title.rectTransform.anchorMin=new Vector2(0,1);title.rectTransform.anchorMax=Vector2.one;title.rectTransform.offsetMin=new Vector2(40,-97);title.rectTransform.offsetMax=new Vector2(-580,-27);
            leftEnd=factory.Label("OutsideLeftBoundary",transform,new Vector2(0,.21f),new Vector2(0,-45),new Vector2(140,60));leftEnd.text="ヒダリ ハシ";leftEnd.fontSize=24;leftEnd.color=Color.white;leftEnd.raycastTarget=false;
            rightEnd=factory.Label("OutsideRightBoundary",transform,new Vector2(0,.21f),new Vector2(0,-45),new Vector2(140,60));rightEnd.text="ミギ ハシ";rightEnd.fontSize=24;rightEnd.color=Color.white;rightEnd.raycastTarget=false;
            pause=factory.Button("PauseOutsideExploration","ポーズ",transform,Vector2.one,new Vector2(-430,-62));Olca.Presentation.Dungeon.UiLook.Button(pause);
            back=factory.Button("CloseOutsideExploration","戻る",transform,Vector2.one,new Vector2(-160,-62));Olca.Presentation.Dungeon.UiLook.Button(back);
        }
        public void Show(Olca.Presentation.Management.ManagedScreenContext owner,Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease lease,Action onBack,Action onPause=null)
        {
            Hide();covered=false;uncoveredFrame=-1;context=owner;presentation=lease;returned=onBack;gameObject.SetActive(true);transform.SetAsLastSibling();
            var captured=owner;var capturedLease=lease;back.onClick=new Button.ButtonClickedEvent();back.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&capturedLease.IsCurrent&&!covered&&Time.frameCount!=uncoveredFrame&&!captured.DialogBlocksInput)returned?.Invoke();});
            if(pause!=null){pause.gameObject.SetActive(onPause!=null);pause.onClick=new Button.ButtonClickedEvent();pause.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&capturedLease.IsCurrent&&CanPlay)onPause?.Invoke();});}
            menu=new Olca.Unity.MenuPadScope(()=>CanPlay){CancelOnly=true};menu.Bind(transform,back.name,()=>back.onClick.Invoke());UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);Render();
        }
        public void SetCovered(bool value)
        {
            if(covered==value)return;covered=value;
            if(value){menu?.Suspend();ClearFocus();transform.SetAsFirstSibling();}
            else if(IsOpen){uncoveredFrame=Time.frameCount;transform.SetAsLastSibling();menu?.Bind(transform,back.name,()=>back.onClick.Invoke());UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);}
        }
        public void Render()
        {
            if(!IsOpen||player==null)return;
            var core=presentation.Core;double unit=Olca.Core.Logic.Integration.OutsideMovementDefinition.UnitsPerWorldUnit;
            float worldX=(float)(core.Position/unit),width=(float)(core.Definition.Width/unit);
            float viewport=((RectTransform)transform).rect.width/pixelsPerWorldUnit;
            float camera=width<=viewport?width*.5f:Mathf.Clamp(worldX,viewport*.5f-1,width-viewport*.5f+1);
            float Origin(float x)=>(x-camera+viewport*.5f)*pixelsPerWorldUnit;
            Sprite sprite=idleSprite;
            if(!core.IsGrounded)sprite=System.Math.Abs(core.VerticalVelocity)<=Olca.Core.Logic.Integration.OutsideExplorationCore.ValidationGravity?apexSprite:core.VerticalVelocity>0?riseSprite:fallSprite;
            else{var clip=core.IsMoving?moveClip:idleClip;if(clip!=null&&motionSample!=null){long frame=core.IsMoving?core.MovementFrame:core.Frame;clip.SampleAnimation(motionSample.gameObject,(float)((frame/60d)%clip.length));sprite=motionSample.sprite;}}
            if(sprite==null)sprite=idleSprite;
            player.sprite=sprite;player.rectTransform.pivot=sprite.pivot/sprite.rect.size;player.rectTransform.sizeDelta=new Vector2(sprite.bounds.size.x,sprite.bounds.size.y)*pixelsPerWorldUnit;
            player.rectTransform.anchoredPosition=new Vector2(Origin(worldX),(float)(core.Height/unit)*pixelsPerWorldUnit);player.rectTransform.localScale=new Vector3(-core.Facing,1,1);
            float viewportPixels=((RectTransform)transform).rect.width;
            void Boundary(Text label,float x,bool left)
            {
                float half=label.rectTransform.rect.width*.5f;bool visible=viewportPixels>=half*2&&x>=0&&x<=viewportPixels;
                label.gameObject.SetActive(visible);
                if(visible)label.rectTransform.anchoredPosition=new Vector2(Mathf.Clamp(x+(left?half:-half),half,viewportPixels-half),-45);
            }
            Boundary(leftEnd,Origin(0),true);Boundary(rightEnd,Origin(width),false);
            // border0の共有素材。画像の実UIタイル周期でwrapし、投影108とCanvas参照100を混同しない。
            float tile=terrain.sprite.rect.width/(terrain.pixelsPerUnit*terrain.pixelsPerUnitMultiplier);
            float shift=tile>0?Mathf.Repeat((camera-viewport*.5f)*pixelsPerWorldUnit,tile):0;
            terrain.rectTransform.offsetMin=new Vector2(-shift,0);terrain.rectTransform.offsetMax=new Vector2(tile-shift,0);
        }
        private void ClearFocus(){var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;if(this!=null&&selected!=null&&selected.transform.IsChildOf(transform))UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);}
        public void Hide(){ClearFocus();menu?.Dispose();menu=null;presentation?.Dispose();presentation=null;context=null;returned=null;if(this!=null)gameObject.SetActive(false);}
        private void LateUpdate(){if(context!=null&&!context.IsCurrent){Hide();return;}menu?.Tick();}
        private void OnDisable(){ClearFocus();context?.NotifyHidden();menu?.Dispose();menu=null;presentation?.Dispose();presentation=null;context=null;returned=null;}
        private void OnDestroy(){OnDisable();back=null;pause=null;}
    }
}
