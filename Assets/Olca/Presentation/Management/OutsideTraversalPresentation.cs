using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Integration;

namespace Olca.Presentation.Exploration
{
    public sealed partial class OutsideExplorationView
    {
        private Sprite traversalDashSprite;
        private Sprite[] wallSlideFrames,wallKickFrames;
        private Olca.Unity.ResourceManager traversalResources;
        private OutsideTraversalProfile renderedTraversal;
        private readonly List<Image> wallImages=new List<Image>();
        private readonly Image[] afterimageImages=new Image[4];
        private void InitializeTraversal(Olca.Unity.ResourceManager resources,Olca.Presentation.Field.GameFieldView field)
        {
            traversalResources=resources;traversalDashSprite=field.AuthoredDashPlayerSprite;
            for(int i=0;i<afterimageImages.Length;i++){
                var go=new GameObject("OutsideDashAfterimage"+i,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);
                var image=afterimageImages[i]=go.GetComponent<Image>();image.raycastTarget=false;image.enabled=false;
                image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());
            }
        }
        private Sprite TraversalSprite(OutsideExplorationCore core)
        {
            if(core.IsWallSliding){
                if(wallSlideFrames==null)wallSlideFrames=new[]{traversalResources.GetShared<Sprite>("OlcaTraversal/WallSlide_01"),traversalResources.GetShared<Sprite>("OlcaTraversal/WallSlide_02")};
                return wallSlideFrames[(int)(core.WallSlideFrame/6%wallSlideFrames.Length)];
            }
            if(core.IsWallKicking){
                if(wallKickFrames==null)wallKickFrames=new[]{traversalResources.GetShared<Sprite>("OlcaTraversal/WallKick_01"),traversalResources.GetShared<Sprite>("OlcaTraversal/WallKick_02")};
                return wallKickFrames[Math.Min(wallKickFrames.Length-1,core.WallKickFrame/6)];
            }
            // 地形で実際に停止している間は立ち姿。ダッシュの残時間や再開条件は変えない。
            return core.IsDashing?core.IsMoving?traversalDashSprite:idleSprite:null;
        }
        private float ResolveVerticalViewOffset(OutsideExplorationCore core)
        {
            float Top(Sprite sprite)=>sprite!=null?sprite.bounds.max.y:0;
            float actorTop=Mathf.Max(Top(idleSprite),Mathf.Max(Top(riseSprite),Mathf.Max(Top(apexSprite),Mathf.Max(Top(fallSprite),Top(traversalDashSprite)))));
            // 壁4枚は共通256px/PPU100/pivotY14。未ロード時も同じ上端でView再生成の跳ねを防ぐ。
            if(core.TraversalProfile?.Walls.Count>0)actorTop=Mathf.Max(actorTop,2.42f);
            float height=((RectTransform)transform).rect.height;
            return presentation.Camera.ResolveVerticalOffset(core.Height,height/pixelsPerWorldUnit,height*.21f/pixelsPerWorldUnit,144/pixelsPerWorldUnit,actorTop);
        }
        private void RenderTraversal(OutsideExplorationCore core,float camera,float viewport,float verticalOffset)
        {
            const float unit=OutsideMovementDefinition.UnitsPerWorldUnit;
            float Origin(long x)=>(x/unit-camera+viewport*.5f)*pixelsPerWorldUnit;
            var profile=core.TraversalProfile;
            if(!ReferenceEquals(renderedTraversal,profile)){
                foreach(var image in wallImages)if(image!=null){if(UnityEngine.Application.isPlaying)Destroy(image.gameObject);else DestroyImmediate(image.gameObject);}wallImages.Clear();renderedTraversal=profile;
                if(profile!=null)foreach(var wall in profile.Walls){
                    var go=new GameObject("OutsideValidationWall-"+wall.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.sprite=terrain.sprite;image.type=Image.Type.Tiled;image.color=new Color(.6f,.85f,.94f,.8f);image.raycastTarget=false;
                    image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);image.rectTransform.pivot=new Vector2(.5f,0);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());wallImages.Add(image);
                }
                var title=transform.Find("OutsideExplorationTitle")?.GetComponent<Text>();
                if(title!=null)title.text=profile!=null&&profile.Walls.Count>0?"ガイカイ タンサク / ケンショウ カベ（"+(profile.WallActionsUnlocked?"ノウリョク カリカイホウ":"ノウリョク ミカイホウ")+"）":"ガイカイ タンサク  /  ケンショウ チケイ";
            }
            for(int i=0;i<wallImages.Count;i++){
                var wall=profile.Walls[i];var image=wallImages[i];image.gameObject.SetActive(true);
                float height=((RectTransform)transform).rect.height,ground=height*.21f/pixelsPerWorldUnit;
                float bottom=Math.Max(wall.Bottom/unit,verticalOffset-ground),top=Math.Min(wall.Top/unit,verticalOffset+height/pixelsPerWorldUnit-ground);
                image.enabled=top>bottom;
                float tile=image.sprite.rect.height/(image.pixelsPerUnit*image.pixelsPerUnitMultiplier);
                // 見える区間だけを生成し、壁のタイル位相はworldへ固定する。
                if(tile>0)bottom-=Mathf.Repeat((bottom-wall.Bottom/unit)*pixelsPerWorldUnit,tile)/pixelsPerWorldUnit;
                image.rectTransform.anchoredPosition=new Vector2(Origin(wall.X),(bottom-verticalOffset)*pixelsPerWorldUnit);image.rectTransform.sizeDelta=new Vector2(18,Math.Max(0,top-bottom)*pixelsPerWorldUnit);
            }
            var samples=core.Afterimages;
            for(int i=0;i<afterimageImages.Length;i++){
                var image=afterimageImages[i];if(i>=samples.Count){image.enabled=false;image.sprite=null;continue;}
                var sample=samples[i];var sprite=sample.Pose==OutsideAfterimagePose.Dash?traversalDashSprite:sample.Pose==OutsideAfterimagePose.JumpRise?riseSprite:sample.Pose==OutsideAfterimagePose.JumpApex?apexSprite:fallSprite;
                image.sprite=sprite;image.enabled=sprite!=null;if(sprite==null)continue;
                image.color=new Color(.65f,.9f,1,.25f*(12-(core.Frame-sample.Frame))/12f);
                var rect=image.rectTransform;rect.pivot=sprite.pivot/sprite.rect.size;rect.sizeDelta=new Vector2(sprite.bounds.size.x,sprite.bounds.size.y)*pixelsPerWorldUnit;
                rect.anchoredPosition=new Vector2(Origin(sample.X),(sample.Y/unit-verticalOffset)*pixelsPerWorldUnit);rect.localScale=new Vector3(-sample.Facing,1,1);
            }
            if(movementGuideText.text.Length>0){
                if(profile!=null&&profile.WallActionsUnlocked&&profile.Walls.Count>0)movementGuideText.text+="\nタイオウするカベ: ホウコウホジでスベル / ジャンプでケる";
                if(core.DashCooldownRemaining>0)movementGuideText.text+="\nダッシュ マチ: "+(Math.Ceiling(core.DashCooldownRemaining/6d)/10d).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
            }
        }
        private void ClearTraversalDisplay()
        {foreach(var image in afterimageImages)if(image!=null){image.enabled=false;image.sprite=null;}foreach(var image in wallImages)if(image!=null)image.gameObject.SetActive(false);}
        private void ReleaseTraversalReferences(){ClearTraversalDisplay();wallSlideFrames=wallKickFrames=null;traversalDashSprite=null;traversalResources=null;renderedTraversal=null;wallImages.Clear();}
    }
}

namespace Olca.Presentation.Management
{
    // 通常状態だけの製品入口。神依定義/効果/解放順をここで仮登録しない。
    public sealed class OutsideKamuiWheelDialog : UnityEngine.MonoBehaviour
    {
        private DialogManager dialogs;
        private IDialogOwner owner;
        private System.Func<bool> parentAlive,canConfirm;
        private System.Action<bool> pause;
        private UnityEngine.UI.Button confirm,cancel;
        private UnityEngine.UI.Text status,hint;
        private OutsideKamuiWheelRing ring;
        private bool open,confirmHeld,pauseOwned;private long epoch;
        public bool IsOpen=>open;
        public bool HasSelection{get;private set;}
        public DialogResult? LastResult{get;private set;}
        public void Initialize(UnityEngine.Font font)
        {
            var rect=(UnityEngine.RectTransform)transform;rect.anchorMin=UnityEngine.Vector2.zero;rect.anchorMax=UnityEngine.Vector2.one;rect.offsetMin=rect.offsetMax=UnityEngine.Vector2.zero;
            var overlay=gameObject.AddComponent<UnityEngine.UI.Image>();overlay.color=new UnityEngine.Color(.015f,.04f,.06f,.75f);
            var face=new UnityEngine.GameObject("KamuiWheelSurface",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));face.transform.SetParent(transform,false);
            var fr=(UnityEngine.RectTransform)face.transform;fr.sizeDelta=new UnityEngine.Vector2(800,670);var image=face.GetComponent<UnityEngine.UI.Image>();image.sprite=UnityEngine.Resources.Load<UnityEngine.Sprite>("OlcaUI/description");image.type=UnityEngine.UI.Image.Type.Sliced;image.color=image.sprite!=null?UnityEngine.Color.white:new UnityEngine.Color(.025f,.075f,.105f,.98f);image.raycastTarget=false;
            Text("KamuiWheelTitle","カムイ ホイール",font,new UnityEngine.Vector2(0,255),new UnityEngine.Vector2(700,60),32);
            var circle=new UnityEngine.GameObject("KamuiWheelRing",typeof(UnityEngine.RectTransform),typeof(OutsideKamuiWheelRing));circle.transform.SetParent(transform,false);((UnityEngine.RectTransform)circle.transform).sizeDelta=new UnityEngine.Vector2(300,300);ring=circle.GetComponent<OutsideKamuiWheelRing>();ring.raycastTarget=false;
            var item=new UnityEngine.GameObject("KamuiWheelNormal",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));item.transform.SetParent(transform,false);((UnityEngine.RectTransform)item.transform).sizeDelta=new UnityEngine.Vector2(250,90);confirm=item.GetComponent<UnityEngine.UI.Button>();confirm.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};Olca.Presentation.Dungeon.UiLook.MenuButton(confirm);item.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            Text("KamuiWheelNormalLabel","ツウジョウ ジョウタイ",font,UnityEngine.Vector2.zero,new UnityEngine.Vector2(240,80),24,item.transform);
            status=Text("KamuiWheelSelection","ミセンタク",font,new UnityEngine.Vector2(0,-195),new UnityEngine.Vector2(700,50),24);
            hint=Text("KamuiWheelHint","ヒダリ スティック: センタク / ケッテイ ボタン: カクテイ\nヒラク ボタンヲ ハナス: トジル",font,new UnityEngine.Vector2(0,-270),new UnityEngine.Vector2(740,85),22);
            var hidden=new UnityEngine.GameObject("KamuiWheelRelease",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Button));hidden.transform.SetParent(transform,false);((UnityEngine.RectTransform)hidden.transform).sizeDelta=UnityEngine.Vector2.zero;cancel=hidden.GetComponent<UnityEngine.UI.Button>();cancel.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            gameObject.SetActive(false);
        }
        private UnityEngine.UI.Text Text(string name,string value,UnityEngine.Font font,UnityEngine.Vector2 at,UnityEngine.Vector2 size,int points,UnityEngine.Transform parent=null)
        {
            var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));go.transform.SetParent(parent??transform,false);var r=(UnityEngine.RectTransform)go.transform;r.sizeDelta=size;r.anchoredPosition=at;var text=go.GetComponent<UnityEngine.UI.Text>();text.font=font;text.fontSize=points;text.alignment=UnityEngine.TextAnchor.MiddleCenter;text.text=value;Olca.Presentation.Dungeon.UiLook.Label(text);text.color=UnityEngine.Color.white;text.raycastTarget=false;return text;
        }
        public bool Open(DialogManager manager,IDialogOwner current,System.Func<bool> alive,System.Func<bool> allowed,System.Action<bool> paused,bool wasConfirmHeld,UnityEngine.Vector2 direction,string openLabel="LB",string confirmLabel="South")
        {
            if(open||manager==null||manager.ActiveCount!=0||current?.IsCurrent!=true||alive==null||!alive())return false;
            hint.text="ヒダリ スティック: センタク / "+confirmLabel+": カクテイ\n"+openLabel+" ヲ ハナス: トジル";
            dialogs=manager;owner=current;parentAlive=alive;canConfirm=allowed;pause=paused;confirmHeld=wasConfirmHeld;open=true;LastResult=null;confirm.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();cancel.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();Select(direction);
            long ticket=++epoch;
            try{var task=manager.Show(current,gameObject,confirm,cancel,result=>Finish(ticket,result),manageMenu:false,inputAllowed:()=>open&&this!=null&&parentAlive?.Invoke()==true);if(task.IsCompleted){Finish(ticket,task.Result);return false;}if(!open)return false;pauseOwned=true;pause?.Invoke(true);return true;}
            catch{Abort();throw;}
        }
        private void Select(UnityEngine.Vector2 direction)
        {
            HasSelection=direction.sqrMagnitude>=.19f*.19f;confirm.interactable=HasSelection&&(canConfirm?.Invoke()??false);ring.Selected=HasSelection;status.text=HasSelection?"センタク: ツウジョウ ジョウタイ":"ミセンタク";
        }
        public void Tick(bool held,bool decide,UnityEngine.Vector2 direction)
        {
            if(!open)return;if(owner?.IsCurrent!=true||parentAlive?.Invoke()!=true){Abort();return;}
            if(!held){cancel.onClick.Invoke();return;}
            bool edge=decide&&!confirmHeld;confirmHeld=decide;Select(direction);
            // 標準UI SubmitやB取消は所有しない。開始時A保持を新しい確定へ合成しない。
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            if(edge&&confirm.interactable)confirm.onClick.Invoke();
        }
        private void Finish(long ticket,DialogResult result)
        {
            if(ticket!=epoch)return;if(!open){if(LastResult==DialogResult.ParentClosed&&result!=DialogResult.ParentClosed)LastResult=result;return;}open=false;HasSelection=false;LastResult=result;var callback=pause;pause=null;parentAlive=canConfirm=null;owner=null;dialogs=null;bool release=pauseOwned;pauseOwned=false;if(release)callback?.Invoke(false);
        }
        public void Abort(){if(!open)return;var manager=dialogs;var current=owner;manager?.CancelActiveOwner(current);Finish(epoch,DialogResult.ParentClosed);if(this!=null&&gameObject.activeSelf)gameObject.SetActive(false);}
        private void OnDisable(){Abort();}
        private void OnDestroy(){Abort();confirm=cancel=null;status=null;ring=null;}
    }
    // 生成Texture/Spriteを持たない、単一項目ホイールの既存UI上の輪郭。
    [UnityEngine.RequireComponent(typeof(UnityEngine.CanvasRenderer))]
    public sealed class OutsideKamuiWheelRing : UnityEngine.UI.MaskableGraphic
    {
        private bool selected;
        public bool Selected{get=>selected;set{if(selected==value)return;selected=value;SetVerticesDirty();}}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();const int count=64;float outer=UnityEngine.Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f,inner=outer-8;
            var c=selected?new UnityEngine.Color(.55f,.95f,1,1):new UnityEngine.Color(.25f,.6f,.7f,1);
            for(int n=0;n<count;n++){float a=n*UnityEngine.Mathf.PI*2/count,b=(n+1)*UnityEngine.Mathf.PI*2/count;int i=vh.currentVertCount;vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*inner,UnityEngine.Mathf.Sin(a)*inner),c,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*outer,UnityEngine.Mathf.Sin(a)*outer),c,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(b)*outer,UnityEngine.Mathf.Sin(b)*outer),c,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(b)*inner,UnityEngine.Mathf.Sin(b)*inner),c,UnityEngine.Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
        }
    }
}
