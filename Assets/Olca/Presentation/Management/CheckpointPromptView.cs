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
    /// <summary>外界入口の初期表示。Session・保存・移動/戦闘の計算は所有しない。</summary>
    public sealed class OutsideExplorationView : MonoBehaviour
    {
        private Olca.Presentation.Management.ManagedScreenContext context;
        private Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease presentation;
        private Olca.Unity.MenuPadScope menu;
        private Button back;
        private Action returned;
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
            var ground=Picture("OutsideEntryGround",Find("Ground"),Vector2.zero,new Vector2(1,.21f));ground.type=Image.Type.Tiled;
            var idle=source.GetComponent<Olca.Presentation.Field.GameFieldView>().AuthoredIdlePlayerSprite;var player=Picture("OutsideEntryPlayer",idle,new Vector2(.3f,.21f),new Vector2(.3f,.21f));player.rectTransform.pivot=idle.pivot/idle.rect.size;float pixelsPerWorldUnit=1080f/(2*resources.GetShared<GameObject>("OlcaSystem/GameplayCamera").GetComponent<Camera>().orthographicSize);player.rectTransform.sizeDelta=new Vector2(idle.bounds.size.x,idle.bounds.size.y)*pixelsPerWorldUnit;player.rectTransform.localScale=new Vector3(-1,1,1);
            var header=Picture("OutsideHeadingSurface",resources.GetShared<Sprite>("OlcaUI/window"),new Vector2(0,1),Vector2.one);header.type=Image.Type.Sliced;header.rectTransform.pivot=new Vector2(.5f,1);header.rectTransform.sizeDelta=new Vector2(0,120);
            var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,font,null);var title=factory.Label("OutsideExplorationTitle",transform,new Vector2(0,1),new Vector2(340,-62),new Vector2(600,70));title.text="ガイカイ タンサク";title.alignment=TextAnchor.MiddleLeft;title.fontSize=36;title.color=Olca.Presentation.Dungeon.UiLook.Ink;
            back=factory.Button("CloseOutsideExploration","戻る",transform,Vector2.one,new Vector2(-160,-62));Olca.Presentation.Dungeon.UiLook.Button(back);
        }
        public void Show(Olca.Presentation.Management.ManagedScreenContext owner,Olca.Core.Logic.Integration.OutsideExplorationSession.PresentationLease lease,Action onBack)
        {
            Hide();context=owner;presentation=lease;returned=onBack;gameObject.SetActive(true);transform.SetAsLastSibling();
            var captured=owner;var capturedLease=lease;back.onClick=new Button.ButtonClickedEvent();back.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&capturedLease.IsCurrent&&!captured.DialogBlocksInput)returned?.Invoke();});
            menu=new Olca.Unity.MenuPadScope(()=>IsOpen&&!context.DialogBlocksInput&&Olca.Unity.MenuPadScope.IsTopmost(transform));menu.Bind(transform,back.name,()=>back.onClick.Invoke());UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(back.gameObject);
        }
        private void ClearFocus(){var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;if(this!=null&&selected!=null&&selected.transform.IsChildOf(transform))UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);}
        public void Hide(){ClearFocus();menu?.Dispose();menu=null;presentation?.Dispose();presentation=null;context=null;returned=null;if(this!=null)gameObject.SetActive(false);}
        private void LateUpdate(){if(context!=null&&!context.IsCurrent){Hide();return;}menu?.Tick();}
        private void OnDisable(){ClearFocus();context?.NotifyHidden();menu?.Dispose();menu=null;presentation?.Dispose();presentation=null;context=null;returned=null;}
        private void OnDestroy(){OnDisable();back=null;}
    }
}
