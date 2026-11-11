using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Integration;

namespace Olca.Presentation.Exploration
{
    public sealed partial class OutsideExplorationView
    {
        private Text mapNameLabel;
        private void RenderMapName()
        {
            double opacity=presentation?.MapNameOpacity??0;
            if(opacity>0&&mapNameLabel==null){
                var go=new GameObject("OutsideMapName",typeof(RectTransform),typeof(Text),typeof(Shadow));go.transform.SetParent(transform,false);mapNameLabel=go.GetComponent<Text>();mapNameLabel.font=gaugeLabel.font;mapNameLabel.fontSize=44;mapNameLabel.alignment=TextAnchor.MiddleCenter;mapNameLabel.raycastTarget=false;mapNameLabel.horizontalOverflow=HorizontalWrapMode.Wrap;mapNameLabel.verticalOverflow=VerticalWrapMode.Overflow;
                var rect=mapNameLabel.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(0,90);rect.sizeDelta=new Vector2(1000,90);var shadow=go.GetComponent<Shadow>();shadow.effectColor=new Color(.005f,.025f,.04f,.95f);shadow.effectDistance=new Vector2(2,-2);
            }
            if(mapNameLabel==null)return;mapNameLabel.gameObject.SetActive(opacity>0);if(opacity<=0)return;mapNameLabel.text=presentation.MapName;mapNameLabel.color=new Color(.8f,.98f,1,(float)opacity);mapNameLabel.transform.SetAsLastSibling();
        }
        private Sprite traversalDashSprite;
        private Sprite[] wallSlideFrames;
        private Olca.Unity.ResourceManager traversalResources;
        private OutsideTraversalProfile renderedTraversal;
        private readonly List<Image> wallImages=new List<Image>();
        private readonly List<Image> gapImages=new List<Image>();
        private readonly Dictionary<Image,List<Image>> waterSurfaces=new Dictionary<Image,List<Image>>();private Button[] waterOccluders;private readonly Vector3[] waterCorners=new Vector3[4];private readonly List<Vector2> waterBlocked=new List<Vector2>();private readonly List<Vector2> waterVisible=new List<Vector2>();
        private readonly List<Image> waterImages=new List<Image>();private Image breathPanel,breathFill;private Text breathLabel;
        private void DestroyWaterImages(){foreach(var image in waterImages)if(image!=null){if(UnityEngine.Application.isPlaying)Destroy(image.gameObject);else DestroyImmediate(image.gameObject);}waterImages.Clear();waterSurfaces.Clear();waterOccluders=null;}
        private void RenderWaterSurface(Image area)
        {
            // 水位は動かさず、既存POIの読取面だけで高輝度の線を遮る。背景の透明感は保つ。
            float width=area.rectTransform.rect.width,top=area.rectTransform.rect.height;waterBlocked.Clear();waterVisible.Clear();
            if(waterOccluders==null)waterOccluders=GetComponentsInChildren<Button>(true);
            foreach(var button in waterOccluders){if(button==null||!button.gameObject.activeInHierarchy)continue;var rect=button.transform as RectTransform;if(rect==null)continue;rect.GetWorldCorners(waterCorners);float left=float.PositiveInfinity,right=float.NegativeInfinity,bottom=float.PositiveInfinity,high=float.NegativeInfinity;foreach(var corner in waterCorners){var local=area.rectTransform.InverseTransformPoint(corner);left=Mathf.Min(left,local.x);right=Mathf.Max(right,local.x);bottom=Mathf.Min(bottom,local.y);high=Mathf.Max(high,local.y);}if(high<top-2||bottom>top||right<0||left>width)continue;waterBlocked.Add(new Vector2(Mathf.Max(0,left-2),Mathf.Min(width,right+2)));}
            waterBlocked.Sort((a,b)=>a.x.CompareTo(b.x));float cursor=0;foreach(var block in waterBlocked){if(block.x>cursor)waterVisible.Add(new Vector2(cursor,block.x));cursor=Mathf.Max(cursor,block.y);}if(cursor<width)waterVisible.Add(new Vector2(cursor,width));
            var lines=waterSurfaces[area];for(int i=0;i<waterVisible.Count;i++){if(i>=lines.Count){var go=new GameObject("WaterSurface"+i,typeof(RectTransform),typeof(Image));go.transform.SetParent(area.transform,false);var line=go.GetComponent<Image>();line.color=new Color(.45f,.95f,1,.9f);line.raycastTarget=false;line.rectTransform.anchorMin=line.rectTransform.anchorMax=new Vector2(0,1);line.rectTransform.pivot=new Vector2(0,1);lines.Add(line);}var image=lines[i];image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=new Vector2(waterVisible[i].x,0);image.rectTransform.sizeDelta=new Vector2(waterVisible[i].y-waterVisible[i].x,2);}for(int i=waterVisible.Count;i<lines.Count;i++)lines[i].gameObject.SetActive(false);
        }
        private void RenderBreath()
        {
            bool visible=presentation!=null&&presentation.ShouldShowBreath;
            if(visible&&breathPanel==null){
                var go=new GameObject("OutsideBreathGauge",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);breathPanel=go.GetComponent<Image>();breathPanel.color=new Color(.015f,.055f,.08f,.95f);breathPanel.raycastTarget=false;var rect=breathPanel.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-150);rect.sizeDelta=new Vector2(220,14);
                var fill=new GameObject("BreathRemaining",typeof(RectTransform),typeof(Image));fill.transform.SetParent(go.transform,false);breathFill=fill.GetComponent<Image>();breathFill.color=new Color(.3f,.9f,1,1);breathFill.raycastTarget=false;breathFill.rectTransform.anchorMin=breathFill.rectTransform.anchorMax=new Vector2(0,.5f);breathFill.rectTransform.pivot=new Vector2(0,.5f);breathFill.rectTransform.anchoredPosition=new Vector2(2,0);breathFill.rectTransform.sizeDelta=new Vector2(216,10);
                var text=new GameObject("OutsideBreathLabel",typeof(RectTransform),typeof(Text));text.transform.SetParent(go.transform,false);breathLabel=text.GetComponent<Text>();breathLabel.font=movementGuideText.font;breathLabel.fontSize=18;breathLabel.verticalOverflow=VerticalWrapMode.Overflow;breathLabel.color=Color.white;breathLabel.alignment=TextAnchor.LowerCenter;breathLabel.raycastTarget=false;var tr=text.GetComponent<RectTransform>();tr.anchorMin=new Vector2(0,1);tr.anchorMax=Vector2.one;tr.pivot=new Vector2(.5f,0);tr.anchoredPosition=new Vector2(0,4);tr.sizeDelta=new Vector2(0,26);
            }
            if(breathPanel==null)return;breathPanel.gameObject.SetActive(visible);if(!visible)return;
            float fraction=(float)presentation.BreathFramesRemaining/presentation.BreathMaximumFrames;breathFill.rectTransform.sizeDelta=new Vector2(216*Mathf.Clamp01(fraction),10);breathLabel.text="コキュウ（カリ） "+(Math.Ceiling(presentation.BreathFramesRemaining/6d)/10d).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
        }
        private void DestroyGapImages(){foreach(var image in gapImages)if(image!=null){if(UnityEngine.Application.isPlaying)Destroy(image.gameObject);else DestroyImmediate(image.gameObject);}gapImages.Clear();}
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
            // 壁蹴り中も通常ジャンプの上昇姿勢を使う。専用PNGは保持するが表示には接続しない。
            if(core.IsWallKicking)return riseSprite;
            // 地形で実際に停止している間は立ち姿。ダッシュの残時間や再開条件は変えない。
            return core.IsDashing&&core.IsMoving?traversalDashSprite:null;
        }
        private float ResolveVerticalViewOffset(OutsideExplorationCore core)
        {
            float Top(Sprite sprite)=>sprite!=null?sprite.bounds.max.y:0;
            float actorTop=Mathf.Max(Top(idleSprite),Mathf.Max(Top(riseSprite),Mathf.Max(Top(apexSprite),Mathf.Max(Top(fallSprite),Top(traversalDashSprite)))));
            // 壁4枚は共通256px/PPU100/pivotY14。未ロード時も同じ上端でView再生成の跳ねを防ぐ。
            if(core.TraversalProfile?.Walls.Count>0)actorTop=Mathf.Max(actorTop,2.42f);
            float height=((RectTransform)transform).rect.height;
            return presentation.Camera.ResolveVerticalOffset(Math.Max(0,core.Height),height/pixelsPerWorldUnit,height*.21f/pixelsPerWorldUnit,144/pixelsPerWorldUnit,actorTop);
        }
        private void RenderTraversal(OutsideExplorationCore core,float camera,float viewport,float verticalOffset)
        {
            const float unit=OutsideMovementDefinition.UnitsPerWorldUnit;
            float Origin(long x)=>(x/unit-camera+viewport*.5f)*pixelsPerWorldUnit;
            var profile=core.TraversalProfile;
            if(!ReferenceEquals(renderedTraversal,profile)){
                foreach(var image in wallImages)if(image!=null){if(UnityEngine.Application.isPlaying)Destroy(image.gameObject);else DestroyImmediate(image.gameObject);}wallImages.Clear();DestroyGapImages();DestroyWaterImages();renderedTraversal=profile;
                if(profile!=null)foreach(var wall in profile.Walls){
                    var go=new GameObject("OutsideValidationWall-"+wall.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.sprite=terrain.sprite;image.type=Image.Type.Tiled;image.color=new Color(.6f,.85f,.94f,.8f);image.raycastTarget=false;
                    image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);image.rectTransform.pivot=new Vector2(.5f,0);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());wallImages.Add(image);
                }
                if(profile!=null)foreach(var gap in profile.FloorGaps){
                    var go=new GameObject("OutsideTrialFloorGap-"+gap.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.color=new Color(.015f,.035f,.055f,1);image.raycastTarget=false;image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);image.rectTransform.pivot=new Vector2(0,1);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());gapImages.Add(image);
                    for(int side=0;side<2;side++){var edge=new GameObject("VoidEdge"+side,typeof(RectTransform),typeof(Image));edge.transform.SetParent(go.transform,false);var line=edge.GetComponent<Image>();line.color=new Color(.3f,.85f,.9f,.85f);line.raycastTarget=false;line.rectTransform.anchorMin=line.rectTransform.anchorMax=new Vector2(side,1);line.rectTransform.pivot=new Vector2(side,1);line.rectTransform.sizeDelta=new Vector2(2,8);}
                }
                if(profile!=null)foreach(var area in profile.Waters){var go=new GameObject("OutsideTrialWater-"+area.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.color=new Color(.08f,.55f,.75f,.26f);image.raycastTarget=false;image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);image.rectTransform.pivot=Vector2.zero;go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());waterImages.Add(image);waterSurfaces[image]=new List<Image>();}

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
            for(int i=0;i<gapImages.Count;i++){
                var gap=profile.FloorGaps[i];var image=gapImages[i];image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=new Vector2(Origin(gap.Left),-verticalOffset*pixelsPerWorldUnit);image.rectTransform.sizeDelta=new Vector2((gap.Right-gap.Left)/unit*pixelsPerWorldUnit,profile.VoidDepth/unit*pixelsPerWorldUnit+8);
            }
            for(int i=0;i<waterImages.Count;i++){var area=profile.Waters[i];var image=waterImages[i];image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=new Vector2(Origin(area.Left),(area.Bottom/unit-verticalOffset)*pixelsPerWorldUnit);image.rectTransform.sizeDelta=new Vector2((area.Right-area.Left)/unit*pixelsPerWorldUnit,(area.Top-area.Bottom)/unit*pixelsPerWorldUnit);RenderWaterSurface(image);}
            RenderBreath();
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
                if(profile?.DoubleJumpUnlocked==true)movementGuideText.text+="\nニダンジャンプ: クウチュウでオシナオシ（カリ カイホウ）";
                if(profile?.Water!=null)movementGuideText.text+="\nアナのサラにヒダリ: ミズ（カリ） / コキュウ0でフッキ";
                if(profile?.FloorGaps.Count>0)movementGuideText.text+="\nカイフクのヒダリ: ラッカ フッキのシヨウアナ（カリ）";
                if(core.DashCooldownRemaining>0)movementGuideText.text+="\nダッシュ マチ: "+(Math.Ceiling(core.DashCooldownRemaining/6d)/10d).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
            }
        }
        private void ClearTraversalDisplay()
        {foreach(var image in afterimageImages)if(image!=null){image.enabled=false;image.sprite=null;}foreach(var image in wallImages)if(image!=null)image.gameObject.SetActive(false);foreach(var image in gapImages)if(image!=null)image.gameObject.SetActive(false);foreach(var image in waterImages)if(image!=null)image.gameObject.SetActive(false);if(breathPanel!=null)breathPanel.gameObject.SetActive(false);}
        private void ReleaseTraversalReferences(){ClearTraversalDisplay();DestroyGapImages();DestroyWaterImages();if(breathPanel!=null){if(UnityEngine.Application.isPlaying)Destroy(breathPanel.gameObject);else DestroyImmediate(breathPanel.gameObject);}breathPanel=breathFill=null;breathLabel=null;wallSlideFrames=null;traversalDashSprite=null;traversalResources=null;renderedTraversal=null;wallImages.Clear();}
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
        /// <summary>PCはトグル開閉。表示/所有/PauseはPadと同じDialogへ集め、入力方式だけを分ける。</summary>
        public bool OpenPc(DialogManager manager,IDialogOwner current,System.Func<bool> alive,System.Func<bool> allowed,System.Action<bool> paused,Olca.Unity.UnityInputProvider.OutsidePcWheelInput input,string openLabel="Tab")
        {
            bool result=Open(manager,current,alive,allowed,paused,input.EnterHeld||input.ClickHeld,UnityEngine.Vector2.zero,openLabel,"Enter");if(!result)return false;
            hint.text="マウス / WASD: センタク\nEnter / ヒダリクリック: カクテイ  /  "+openLabel+": トジル";
            SelectMouse(input.Pointer,input.PointerAvailable);
            // 開く前から方向を保持していた入力も初回sampleとして表示する。以後は実イベント順だけで切り替える。
            if(input.Direction!=UnityEngine.Vector2.zero)Select(input.Direction);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);return true;
        }
        private void SelectMouse(UnityEngine.Vector2 pointer,bool available)
        {bool hit=available&&UnityEngine.RectTransformUtility.RectangleContainsScreenPoint((UnityEngine.RectTransform)confirm.transform,pointer,null);Select(hit?UnityEngine.Vector2.up:UnityEngine.Vector2.zero);}
        public void TickPc(Olca.Unity.UnityInputProvider.OutsidePcWheelInput input)
        {
            if(!open)return;if(owner?.IsCurrent!=true||parentAlive?.Invoke()!=true||!input.KeyboardAvailable){Abort();return;}
            if(input.ToggleDown){cancel.onClick.Invoke();return;}
            if(input.KeyboardChanged&&(!input.PointerChanged||input.KeyboardOrder>=input.PointerOrder))Select(input.Direction);
            else if(input.PointerChanged)SelectMouse(input.Pointer,input.PointerAvailable);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            // クリックは新しいマウス操作。静止hoverとは区別し、その位置の項目を選んで確定する。
            if(input.ClickDown)SelectMouse(input.Pointer,input.PointerAvailable);
            // 選択入力が静止していても、現在の切替可否を確定直前に読み直す。
            confirm.interactable=HasSelection&&(canConfirm?.Invoke()??false);
            if((input.EnterDown||input.ClickDown)&&confirm.interactable)confirm.onClick.Invoke();
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

namespace Olca.Presentation.Management
{
    /// <summary>所持/4枠の表示と操作だけ。保存は既存の進行サービスへ委譲する。</summary>
    public sealed class OutsideItemRegistrationView : UnityEngine.MonoBehaviour
    {
        private ManagedScreenContext context;
        private Olca.Core.Domain.ClearReturnSession session;
        private Olca.Core.Domain.OutsideItemDisplayDefinition[] definitions;
        private System.Action close;
        private Olca.Unity.MenuPadScope menu;
        private UnityEngine.Font font;
        private UnityEngine.Transform content;
        private int selectedSlot,page;private long renderEpoch;
        private System.Func<string,string> useItem;private System.Func<string,int> cooldown;private string useMessage;
        public void ConfigureUse(System.Func<string,string> use,System.Func<string,int> remaining){useItem=use;cooldown=remaining;}
        private bool CanOperate=>this!=null&&isActiveAndEnabled&&context?.IsCurrent==true&&!context.DialogBlocksInput;
        public void Initialize(UnityEngine.Font value,Olca.Core.Domain.ClearReturnSession source,Olca.Core.Domain.OutsideItemDisplayDefinition[] catalog,System.Action closed){font=value;session=source;definitions=catalog.ToArray();close=closed;}
        public void Open(ManagedScreenContext owner){context=owner;useMessage=null;selectedSlot=0;gameObject.SetActive(true);transform.SetAsLastSibling();Render();}
        private UnityEngine.UI.Text Text(string name,string value,float x,float y,float width,float height,int size){var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));go.transform.SetParent(content,false);var label=go.GetComponent<UnityEngine.UI.Text>();label.font=font;label.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(value);label.fontSize=size;label.alignment=UnityEngine.TextAnchor.MiddleCenter;label.color=UnityEngine.Color.white;label.raycastTarget=false;var r=label.rectTransform;r.anchoredPosition=new UnityEngine.Vector2(x,y);r.sizeDelta=new UnityEngine.Vector2(width,height);return label;}
        private UnityEngine.UI.Button Button(string name,string text,float x,float y,float width,System.Action action){var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));go.transform.SetParent(content,false);var r=(UnityEngine.RectTransform)go.transform;r.anchoredPosition=new UnityEngine.Vector2(x,y);r.sizeDelta=new UnityEngine.Vector2(width,74);var b=go.GetComponent<UnityEngine.UI.Button>();Olca.Presentation.Dungeon.UiLook.MenuButton(b);var label=Text(name+"Label",text,0,0,width-20,68,25);label.transform.SetParent(go.transform,false);label.rectTransform.anchoredPosition=UnityEngine.Vector2.zero;label.resizeTextForBestFit=true;label.resizeTextMinSize=18;label.resizeTextMaxSize=25;var captured=context;long capturedRender=renderEpoch;b.onClick.AddListener(()=>{if(b!=null&&b.isActiveAndEnabled&&CanOperate&&capturedRender==renderEpoch&&ReferenceEquals(context,captured))action();});return b;}
        private void Render()
        {
            renderEpoch++;menu?.Dispose();menu=null;
            if(content!=null){content.gameObject.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content.gameObject);else DestroyImmediate(content.gameObject);}
            var root=new UnityEngine.GameObject("OutsideItemContents",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));root.transform.SetParent(transform,false);content=root.transform;((UnityEngine.RectTransform)content).sizeDelta=new UnityEngine.Vector2(1540,920);root.GetComponent<UnityEngine.UI.Image>().color=new UnityEngine.Color(.025f,.075f,.105f,.98f);
            var data=session.OutsideInventorySnapshot;bool pending=session.HasPendingSave;
            Text("OutsideItemsTitle","タンサク アイテム / ショートカット",0,385,1400,65,40);
            Text("OutsideWallet","エイゾク ツウカ: "+data.Currency+"  / カード チョウカ トークンとはベツ",0,310,1400,50,25);
            for(int n=0;n<4;n++){
                int index=n;string id=data.Shortcuts[n];var item=definitions.SingleOrDefault(v=>v.Id==id);int count=data.Items.FirstOrDefault(v=>v.Id==id)?.Count??0;
                var button=Button("OutsideItemSlot_"+n,new[]{"1 / ウエ","2 / ミギ","3 / シタ","4 / ヒダリ"}[n]+"\n"+(id==null?"ミトウロク":item?.Name??"ミトウロクID")+"  "+count,-555+n*370,195,340,()=>{selectedSlot=index;Render();});button.interactable=!pending;if(index==selectedSlot)button.GetComponent<UnityEngine.UI.Image>().color=Olca.Presentation.Dungeon.UiLook.Chosen;
            }
            Text("OutsideItemsHelp","ワクをエラんで、ショジ アイテムをトウロク。ジュウフクはイレカエ。",0,100,1420,55,25);
            var known=data.Items.Where(v=>definitions.Any(d=>d.Id==v.Id)).ToArray();
            if(known.Length==0)Text("OutsideNoItems","トウロクできる ショジ アイテムはありません。",0,0,1400,60,28);
            // 1ページを限定し、長い所持一覧で下部操作を押し出さない。
            int pages=System.Math.Max(1,(known.Length+1)/2);page=System.Math.Min(page,pages-1);var rows=known.Skip(page*2).Take(2).ToArray();
            for(int i=0;i<rows.Length;i++){string id=rows[i].Id;var definition=definitions.Single(d=>d.Id==id);var button=Button("RegisterOutsideItem_"+i,definition.Name+"  / ショジ: "+rows[i].Count,0,-i*90,1240,()=>{session.TryAssignOutsideItem(selectedSlot,id,definitions);Render();});button.interactable=!pending;}
            if(pages>1){Button("OutsideItemPrevious","マエ",-350,-185,270,()=>{page--;Render();}).interactable=!pending&&page>0;Text("OutsideItemPage",(page+1)+" / "+pages,0,-185,300,55,25);Button("OutsideItemNext","ツギ",350,-185,270,()=>{page++;Render();}).interactable=!pending&&page+1<pages;}
            int unknown=data.Items.Count(v=>!definitions.Any(d=>d.Id==v.Id));
            Text("OutsideItemsStatus",useMessage??session.Error??(unknown>0?"ミトウロク ID "+unknown+"ケンは ホゾンのままホジしています。":"ショジスウ0でも トウロクをホジします。"),0,-260,1400,44,25);
            string selectedId=data.Shortcuts[selectedSlot];int remaining=cooldown?.Invoke(selectedId)??0;
            Button("UseOutsideItem","トウロク アイテムを シヨウ"+(remaining>0?" / "+((remaining+59L)/60)+"ビョウ":""),0,-330,1240,()=>{useMessage=useItem?.Invoke(selectedId);Render();}).interactable=!pending&&useItem!=null&&selectedId!=null&&remaining==0;
            Text("OutsideItemsScope","カリ: HP+"+Olca.Core.Domain.OutsideRecoveryItemDefinition.Provisional.HealHp+"/CD"+((Olca.Core.Domain.OutsideRecoveryItemDefinition.Provisional.CooldownFrames+59L)/60)+"ビョウ・ドクケシ/CD"+((Olca.Core.Domain.OutsideRecoveryItemDefinition.PoisonProvisional.CooldownFrames+59L)/60)+"ビョウ。ホキュウバコでニュウシュ。",0,-200,1440,50,23);

            Button("ClearOutsideItemSlot","ワクをカラにする",-480,-410,420,()=>{session.TryAssignOutsideItem(selectedSlot,null,definitions);Render();}).interactable=!pending;
            Button("RetryOutsideItemsSave","ホゾンをサイシコウ",0,-410,420,()=>{session.Retry();Render();}).interactable=pending;
            Button("CloseOutsideItems","モドる",480,-410,420,()=>close?.Invoke());
            menu=new Olca.Unity.MenuPadScope(()=>CanOperate);menu.Bind(content,"OutsideItemSlot_"+selectedSlot,()=>close?.Invoke());
        }
        public void Hide(){renderEpoch++;menu?.Dispose();menu=null;var prior=context;context=null;if(this!=null)gameObject.SetActive(false);prior?.NotifyHidden();}
        private void LateUpdate(){if(context!=null&&!context.IsCurrent){Hide();return;}menu?.Tick();}
        private void OnDisable(){renderEpoch++;menu?.Dispose();menu=null;context?.NotifyHidden();context=null;}
        private void OnDestroy(){menu?.Dispose();menu=null;context=null;session=null;definitions=null;close=null;}
    }
}

namespace Olca.Presentation.Exploration
{
    public sealed partial class OutsideExplorationView
    {
        private System.Func<Olca.Core.Domain.OutsideInventoryData> itemInventory;
        private System.Func<long> itemRevision;
        private Olca.Core.Domain.OutsideItemDisplayDefinition[] itemDefinitions;
        private readonly UnityEngine.UI.Text[] itemMarks=new UnityEngine.UI.Text[4],itemCounts=new UnityEngine.UI.Text[4];
        private long shownItemRevision=long.MinValue;
        public void ConfigureItemInventory(System.Func<Olca.Core.Domain.OutsideInventoryData> read,System.Func<long> revision,Olca.Core.Domain.OutsideItemDisplayDefinition[] definitions){itemInventory=read;itemRevision=revision;itemDefinitions=definitions;shownItemRevision=long.MinValue;RefreshItemHud();}
        private System.Action<int> queueItemUse;private System.Func<string,int> readItemCooldown,readItemDuration;private System.Func<int,string> readItemKey;
        private Olca.Core.Domain.OutsideInventoryData cachedItems;
        private readonly UnityEngine.UI.Button[] itemButtons=new UnityEngine.UI.Button[4];
        private readonly OutsideItemPressGuard[] itemPresses=new OutsideItemPressGuard[4];
        private readonly UnityEngine.UI.Text[] itemSeconds=new UnityEngine.UI.Text[4],itemKeys=new UnityEngine.UI.Text[4];
        private readonly OutsideItemCooldownRing[] itemRings=new OutsideItemCooldownRing[4];
        private UnityEngine.UI.Text itemFeedback;
        public void ConfigureItemUse(System.Action<int> queue,System.Func<string,int> remaining,System.Func<int,string> key)=>ConfigureItemUse(queue,remaining,key,null);
        public void ConfigureItemUse(System.Action<int> queue,System.Func<string,int> remaining,System.Func<int,string> key,System.Func<string,int> duration){queueItemUse=queue;readItemCooldown=remaining;readItemKey=key;readItemDuration=duration;RefreshItemHud();}
        public void ShowItemFeedback(string message){if(itemFeedback!=null)itemFeedback.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(message??"");}
        private void BindItemHudUse(Olca.Presentation.Management.ManagedScreenContext owner,OutsideExplorationSession.PresentationLease lease)
        {
            for(int i=0;i<4;i++){int slot=i;var button=itemButtons[i];if(button==null)continue;button.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();itemPresses[i].BeginAllowed=()=>this!=null&&button!=null&&button.IsInteractable()&&ReferenceEquals(context,owner)&&owner.IsCurrent&&lease.IsCurrent&&CanPlay;button.onClick.AddListener(()=>{
                if(itemPresses[slot].Consume()&&this!=null&&button!=null&&button.isActiveAndEnabled&&button.IsInteractable()&&ReferenceEquals(context,owner)&&owner.IsCurrent&&ReferenceEquals(presentation,lease)&&lease.IsCurrent&&CanPlay)queueItemUse?.Invoke(slot);
            });}
        }
        private void InitializeItemHud(Olca.Presentation.Management.GameplayPanelFactory factory)
        {
            for(int i=0;i<4;i++){
                float x=80+i*110;var go=new UnityEngine.GameObject("OutsideItemHud_"+i,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));go.transform.SetParent(transform,false);var image=go.GetComponent<UnityEngine.UI.Image>();image.color=new UnityEngine.Color(.04f,.18f,.23f,.9f);image.raycastTarget=true;itemButtons[i]=go.AddComponent<UnityEngine.UI.Button>();itemPresses[i]=go.AddComponent<OutsideItemPressGuard>();go.AddComponent<Olca.Unity.TouchUiCancelGuard>();var rect=(UnityEngine.RectTransform)go.transform;rect.anchorMin=rect.anchorMax=UnityEngine.Vector2.zero;rect.anchoredPosition=new UnityEngine.Vector2(x,84);rect.sizeDelta=new UnityEngine.Vector2(88,88);
                var ringGo=new UnityEngine.GameObject("OutsideItemCooldownRing_"+i,typeof(UnityEngine.RectTransform),typeof(OutsideItemCooldownRing));ringGo.transform.SetParent(go.transform,false);var ring=itemRings[i]=ringGo.GetComponent<OutsideItemCooldownRing>();ring.raycastTarget=false;ring.rectTransform.sizeDelta=new UnityEngine.Vector2(88,88);ring.enabled=false;
                var seconds=itemSeconds[i]=factory.Label("OutsideItemCooldownSeconds_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,142.5f),new UnityEngine.Vector2(90,25));seconds.fontSize=16;seconds.color=new UnityEngine.Color(.65f,1,1,1);seconds.raycastTarget=false;seconds.text="";
                var mark=itemMarks[i]=factory.Label("OutsideItemMark_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,96),new UnityEngine.Vector2(82,55));mark.fontSize=24;mark.raycastTarget=false;
                var count=itemCounts[i]=factory.Label("OutsideItemCount_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,53),new UnityEngine.Vector2(84,28));count.fontSize=22;count.resizeTextForBestFit=true;count.resizeTextMinSize=12;count.resizeTextMaxSize=22;count.raycastTarget=false;
                var key=itemKeys[i]=factory.Label("OutsideItemKey_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,130),new UnityEngine.Vector2(90,32));key.text=new[]{"1 / ↑","2 / →","3 / ↓","4 / ←"}[i];key.fontSize=20;key.resizeTextForBestFit=true;key.resizeTextMinSize=12;key.resizeTextMaxSize=20;key.color=UnityEngine.Color.white;key.raycastTarget=false;
            }
            var hint=itemFeedback=factory.Label("OutsideItemsUnconnected",transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(250,17),new UnityEngine.Vector2(470,26));hint.text="アイテム: 1〜4 / ジュウジキー / タップ";hint.resizeTextForBestFit=true;hint.resizeTextMinSize=12;hint.resizeTextMaxSize=18;hint.fontSize=18;hint.color=new UnityEngine.Color(.65f,.82f,.86f,1);hint.raycastTarget=false;
            RefreshItemHud();
        }
        private void RefreshItemHud()
        {
            if(itemMarks[0]==null)return;long revision=itemRevision?.Invoke()??-1;
            if(cachedItems==null||revision!=shownItemRevision){if(cachedItems!=null)ClearItemPresses();shownItemRevision=revision;cachedItems=itemInventory?.Invoke()??new Olca.Core.Domain.OutsideInventoryData();}
            for(int i=0;i<4;i++){
                string id=cachedItems.Shortcuts[i];var definition=itemDefinitions?.FirstOrDefault(v=>v.Id==id);int count=cachedItems.Items.FirstOrDefault(v=>v.Id==id)?.Count??0;int remaining=readItemCooldown?.Invoke(id)??0;
                itemMarks[i].text=id==null?"—":definition?.Mark??"?";itemMarks[i].color=count==0||remaining>0?new UnityEngine.Color(.45f,.58f,.62f,1):UnityEngine.Color.white;itemCounts[i].text=id==null?"":count.ToString();itemCounts[i].color=UnityEngine.Color.white;
                itemButtons[i].interactable=CanPlay&&queueItemUse!=null&&definition!=null&&count>0&&remaining==0;
                itemKeys[i].text=readItemKey?.Invoke(i)??new[]{"1 / ↑","2 / →","3 / ↓","4 / ←"}[i];itemKeys[i].enabled=remaining==0;
                itemSeconds[i].text=remaining>0?((remaining+59L)/60).ToString():"";itemSeconds[i].enabled=remaining>0;
                itemRings[i].Fraction=remaining<=0?0:UnityEngine.Mathf.Clamp01((float)remaining/Mathf.Max(1,readItemDuration?.Invoke(id)??Olca.Core.Domain.OutsideRecoveryItemDefinition.Provisional.CooldownFrames));itemRings[i].enabled=remaining>0;
            }
        }
        private void ClearItemPresses(){foreach(var press in itemPresses)if(press!=null)press.Clear();}
        private void ClearItemProviders(){ClearItemPresses();foreach(var press in itemPresses)if(press!=null)press.BeginAllowed=null;itemInventory=null;itemRevision=null;itemDefinitions=null;queueItemUse=null;readItemCooldown=readItemDuration=null;readItemKey=null;cachedItems=null;shownItemRevision=long.MinValue;foreach(var button in itemButtons)if(button!=null){button.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();button.interactable=false;}}
    }

    // 使用可になるまで保持した入力を、解放時のクリックとして後から受理しない。
    public sealed class OutsideItemPressGuard : UnityEngine.MonoBehaviour,UnityEngine.EventSystems.IPointerDownHandler,UnityEngine.EventSystems.IPointerUpHandler
    {
        public System.Func<bool> BeginAllowed;
        private readonly System.Collections.Generic.Dictionary<int,bool> presses=new System.Collections.Generic.Dictionary<int,bool>();
        private bool released;private int releaseFrame=-1;
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData data){if(data==null)return;released=false;presses[data.pointerId]=data.button==UnityEngine.EventSystems.PointerEventData.InputButton.Left&&BeginAllowed?.Invoke()==true;}
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData data){if(data==null)return;released=presses.TryGetValue(data.pointerId,out var allowed)&&allowed;presses.Remove(data.pointerId);releaseFrame=UnityEngine.Time.frameCount;}
        public bool Consume(){bool result=released&&releaseFrame==UnityEngine.Time.frameCount;released=false;return result;}
        public void Clear(){presses.Clear();released=false;releaseFrame=-1;}
        private void OnDisable()=>Clear();
        private void OnDestroy(){Clear();BeginAllowed=null;}
    }
    [UnityEngine.RequireComponent(typeof(UnityEngine.CanvasRenderer))]
    public sealed class OutsideItemCooldownRing : UnityEngine.UI.MaskableGraphic
    {
        private float fraction;
        public float Fraction{get=>fraction;set{value=UnityEngine.Mathf.Clamp01(value);if(fraction==value)return;fraction=value;SetVerticesDirty();}}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();if(fraction<=0)return;const int segments=64;float outer=UnityEngine.Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f,inner=outer-3;var tint=new UnityEngine.Color(.55f,.95f,1,1);
            for(int n=0;n<segments;n++){float start=(float)n/segments;if(start>=fraction)break;float end=UnityEngine.Mathf.Min((n+1f)/segments,fraction);float a=(.25f-start)*UnityEngine.Mathf.PI*2,b=(.25f-end)*UnityEngine.Mathf.PI*2;int v=vh.currentVertCount;vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*inner,UnityEngine.Mathf.Sin(a)*inner),tint,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*outer,UnityEngine.Mathf.Sin(a)*outer),tint,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(b)*outer,UnityEngine.Mathf.Sin(b)*outer),tint,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(b)*inner,UnityEngine.Mathf.Sin(b)*inner),tint,UnityEngine.Vector2.zero);vh.AddTriangle(v,v+1,v+2);vh.AddTriangle(v,v+2,v+3);}
        }
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>Overlay座標だけを適応する数値View。既存World用の書体/色/軌道を借用する。</summary>
    public sealed class OutsideCombatFeedbackCanvas:MonoBehaviour
    {
        private sealed class Entry{public Text Text;public Vector2 World;public float Age,Lift;public bool Active,Placed;}
        private readonly List<Entry> entries=new List<Entry>();
        private Olca.Core.Domain.PlayerCharacter actor;private Olca.Core.Domain.Enemy enemy;private Olca.Presentation.Effects.CombatNumberStyle style;
        private Func<bool> live;private Func<Vector2> origin;private float pixelsPerWorldUnit;private int lastFrame=-1;
        public int ActiveCount{get;private set;}
        public void Bind(Olca.Core.Domain.PlayerCharacter value,Olca.Presentation.Effects.CombatNumberStyle visual,Func<bool> isLive,Func<Vector2> anchor,float scale)
        {Unbind();if(value==null||visual==null)return;actor=value;style=visual;live=isLive;origin=anchor;pixelsPerWorldUnit=scale;actor.OnCombatFeedback+=OnFeedback;}
        public void BindEnemy(Olca.Core.Domain.Enemy value,Olca.Presentation.Effects.CombatNumberStyle visual,Func<bool> isLive,Func<Vector2> anchor,float scale){Unbind();if(value==null||visual==null)return;enemy=value;style=visual;live=isLive;origin=anchor;pixelsPerWorldUnit=scale;enemy.OnCombatFeedback+=OnFeedback;}
        private void OnFeedback(Olca.Core.Domain.CombatFeedback feedback)
        {
            if(actor==null&&enemy==null||style==null||live?.Invoke()!=true||origin==null)return;
            var entry=entries.FirstOrDefault(e=>!e.Active);
            if(entry==null){var go=new GameObject("OutsideCombatNumber_"+entries.Count,typeof(RectTransform),typeof(Text),typeof(Outline));go.transform.SetParent(transform,false);var text=go.GetComponent<Text>();text.raycastTarget=false;text.alignment=TextAnchor.MiddleCenter;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;text.supportRichText=false;text.lineSpacing=1;entry=new Entry{Text=text};entries.Add(entry);}
            entry.Text.font=style.Font;entry.Text.fontStyle=style.FontStyle;entry.Text.fontSize=Mathf.Max(1,Mathf.RoundToInt(style.FontWorldUnits*pixelsPerWorldUnit));entry.Text.text=Olca.Presentation.Effects.CombatNumberPresentation.Format(feedback);entry.Text.color=style.Color;
            var outline=entry.Text.GetComponent<Outline>();outline.effectColor=new Color(.02f,.04f,.06f,.9f);outline.effectDistance=Vector2.one*(style.OutlineWorldUnits*pixelsPerWorldUnit);outline.useGraphicAlpha=true;
            entry.World=origin();entry.Age=entry.Lift=0;entry.Placed=false;entry.Active=true;entry.Text.enabled=true;ActiveCount++;entry.Text.gameObject.SetActive(true);entry.Text.transform.SetAsLastSibling();
        }
        public void Paint(Func<Vector2,Vector2> project,IReadOnlyList<RectTransform> obstacles)
        {
            if(style==null||ActiveCount==0)return;if(live?.Invoke()!=true){Unbind();return;}
            bool advance=lastFrame!=Time.frameCount;lastFrame=Time.frameCount;var root=(RectTransform)transform;var occupied=new List<Rect>();
            if(obstacles!=null)foreach(var obstacle in obstacles)if(obstacle!=null&&obstacle.gameObject.activeInHierarchy){var corners=new Vector3[4];obstacle.GetWorldCorners(corners);var local=corners.Select(p=>root.InverseTransformPoint(p)).ToArray();occupied.Add(Rect.MinMaxRect(local.Min(p=>p.x),local.Min(p=>p.y),local.Max(p=>p.x),local.Max(p=>p.y)));}
            var visible=new List<(Entry Entry,Vector2 Position,Vector2 Size)>();float top=float.NegativeInfinity;
            foreach(var entry in entries){if(!entry.Active)continue;if(advance)entry.Age+=Olca.Unity.BattleVisualClock.DeltaTime;if(entry.Age>=style.Duration){entry.Active=false;ActiveCount--;entry.Text.gameObject.SetActive(false);continue;}
                var position=project(entry.World+Vector2.up*(Olca.Presentation.Effects.CombatNumberPresentation.Rise(style.FloatSpeed,style.Duration,entry.Age)+entry.Lift));
                var size=new Vector2(Mathf.Max(1,entry.Text.preferredWidth+4),Mathf.Max(1,entry.Text.preferredHeight));entry.Text.rectTransform.sizeDelta=size;
                // 地点案内と先行の数値の上へ積む。World poolと同じく過去の効果を捨てない。
                float beforeLift=position.y;
                if(!entry.Placed)for(int pass=0;pass<=occupied.Count;pass++){var box=new Rect(position-size*.5f,size);bool moved=false;foreach(var area in occupied)if(box.Overlaps(area)){position.y=area.yMax+size.y*.5f+6;box=new Rect(position-size*.5f,size);moved=true;}if(!moved)break;}
                if(!entry.Placed){entry.Lift+=(position.y-beforeLift)/pixelsPerWorldUnit;entry.Placed=true;}
                occupied.Add(new Rect(position-size*.5f,size));visible.Add((entry,position,size));top=Mathf.Max(top,position.y+size.y*.5f);
            }
            float shift=Mathf.Max(0,top-(root.rect.yMax-144));
            foreach(var row in visible){var position=row.Position-Vector2.up*shift;row.Entry.Text.rectTransform.anchoredPosition=position;var color=style.Color;color.a=Olca.Presentation.Effects.CombatNumberPresentation.Alpha(style.Duration,row.Entry.Age);row.Entry.Text.color=color;row.Entry.Text.enabled=position.y-row.Size.y*.5f>=root.rect.yMin+160;}
        }
        public void Unbind(){if(actor!=null)actor.OnCombatFeedback-=OnFeedback;if(enemy!=null)enemy.OnCombatFeedback-=OnFeedback;actor=null;enemy=null;style=null;live=null;origin=null;lastFrame=-1;ActiveCount=0;foreach(var entry in entries){entry.Active=false;entry.Age=0;if(entry.Text!=null){entry.Text.text="";entry.Text.font=null;entry.Text.gameObject.SetActive(false);}}}
        private void OnDisable()=>Unbind();
        private void OnDestroy(){Unbind();entries.Clear();}
    }
    public sealed partial class OutsideExplorationView
    {
        private OutsideCombatFeedbackCanvas feedbackCanvas;
        private readonly List<RectTransform> feedbackObstacles=new List<RectTransform>();
        public void ConfigureCombatFeedback(Olca.Presentation.Effects.CombatNumberStyle style)
        {
            trialNumberStyle=style;BindTrialTargetFeedback();
            if(feedbackCanvas==null&&style!=null)feedbackCanvas=gameObject.AddComponent<OutsideCombatFeedbackCanvas>();if(feedbackCanvas==null)return;var owner=context;var lease=presentation;
            feedbackCanvas.Bind(lease?.Player,style,()=>this!=null&&ReferenceEquals(context,owner)&&owner?.IsCurrent==true&&ReferenceEquals(presentation,lease)&&lease.IsCurrent,()=>new Vector2((float)lease.Core.Position/OutsideMovementDefinition.UnitsPerWorldUnit,(float)lease.Core.Height/OutsideMovementDefinition.UnitsPerWorldUnit+player.rectTransform.rect.height/pixelsPerWorldUnit+.2f),pixelsPerWorldUnit);
        }
        private void ClearCombatFeedback(){feedbackCanvas?.Unbind();feedbackObstacles.Clear();}
        private void RenderCombatFeedback(float camera,float verticalOffset)
        {
            if(feedbackCanvas==null||feedbackCanvas.ActiveCount==0)return;feedbackObstacles.Clear();if(recoveryPoint!=null)feedbackObstacles.Add((RectTransform)recoveryPoint.transform);if(entrance!=null)feedbackObstacles.Add((RectTransform)entrance.transform);foreach(var slot in supplySlots)if(slot?.Button!=null)feedbackObstacles.Add((RectTransform)slot.Button.transform);
            if(trialTargetName!=null)feedbackObstacles.Add(trialTargetPlate);if(trialTargetGauge!=null&&trialTargetGauge.enabled)feedbackObstacles.Add(trialTargetGauge.rectTransform);float floor=-((RectTransform)transform).rect.height*.29f;feedbackCanvas.Paint(world=>new Vector2((world.x-camera)*pixelsPerWorldUnit,floor+(world.y-verticalOffset)*pixelsPerWorldUnit),feedbackObstacles);
        }
    }
}

namespace Olca.Presentation.Exploration
{
    // World EnemyViewと同じ円/外周半円の配色と5度分割をOverlayへ適応する。生成Textureは不要。
    /// <summary>既存の即時効果を遅らせず、接触から12論理frameだけ振り抜く暫定表示。</summary>
    public sealed class OutsideSwordTrailGraphic:Graphic
    {
        private float angle,alpha,radius;
        public static bool TrySample(long age,int facing,out float angle,out float alpha){if(facing!=1&&facing!=-1)throw new ArgumentOutOfRangeException(nameof(facing));angle=alpha=0;if(age<0||age>=12)return false;angle=-14f*Math.Min((int)age,5)*facing;alpha=age<=6?1:(12-age)/6f;return true;}
        public void SetPose(float rightAngle,float opacity,float size){if(float.IsNaN(rightAngle)||float.IsInfinity(rightAngle)||float.IsNaN(opacity)||float.IsInfinity(opacity)||float.IsNaN(size)||float.IsInfinity(size)||size<=0)throw new ArgumentOutOfRangeException();angle=rightAngle;alpha=Mathf.Clamp01(opacity);radius=size;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh){mesh.Clear();if(alpha<=0||radius<=0)return;const int count=12;for(int n=0;n<count;n++){float a=(angle+8+60f*n/count)*Mathf.Deg2Rad,b=(angle+8+60f*(n+1)/count)*Mathf.Deg2Rad;int first=mesh.currentVertCount;var ca=new Color(.65f,.95f,1,alpha*.45f*(1-n/(float)count));var cb=new Color(.65f,.95f,1,alpha*.45f*(1-(n+1)/(float)count));mesh.AddVert(new Vector3(Mathf.Cos(a),Mathf.Sin(a))*radius,ca,Vector2.zero);mesh.AddVert(new Vector3(Mathf.Cos(a),Mathf.Sin(a))*(radius-8),ca,Vector2.zero);mesh.AddVert(new Vector3(Mathf.Cos(b),Mathf.Sin(b))*(radius-8),cb,Vector2.zero);mesh.AddVert(new Vector3(Mathf.Cos(b),Mathf.Sin(b))*radius,cb,Vector2.zero);mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);}}
    }
    public sealed class OutsideTrialHpArc:Graphic
    {
        private float fraction=1;public void SetFraction(float value){if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));value=Mathf.Clamp01(value);if(value==fraction)return;fraction=value;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh){mesh.Clear();Arc(mesh,29.7f,4.752f,360,new Color(.035f,.1f,.13f,1));Arc(mesh,29.7f,3.267f,360*fraction,new Color(.42f,.85f,.87f,1));Arc(mesh,38.016f,2.673f,180,new Color(.12f,.13f,.08f,1));}
        private static void Arc(VertexHelper mesh,float radius,float width,float degrees,Color color){if(degrees<=0)return;int steps=Mathf.Max(1,Mathf.CeilToInt(degrees/5));for(int i=0;i<steps;i++){float a=(90-degrees*i/steps)*Mathf.Deg2Rad,b=(90-degrees*(i+1)/steps)*Mathf.Deg2Rad;Vector2 pa=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),pb=new Vector2(Mathf.Cos(b),Mathf.Sin(b));int n=mesh.currentVertCount;mesh.AddVert(pa*(radius-width*.5f),color,Vector2.zero);mesh.AddVert(pa*(radius+width*.5f),color,Vector2.zero);mesh.AddVert(pb*(radius+width*.5f),color,Vector2.zero);mesh.AddVert(pb*(radius-width*.5f),color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);}}
    }
    public sealed partial class OutsideExplorationView
    {
        private OutsideSwordTrailGraphic trialSwordTrail;private RectTransform trialTargetPlate;private Image trialTargetImage,trialSwordImage;private Text trialTargetName,trialTargetHp;private OutsideTrialHpArc trialTargetGauge;
        private Olca.Unity.GeneratedSpriteAssets.Lease trialSwordAssets;private OutsideCombatFeedbackCanvas trialTargetFeedback;private Olca.Core.Domain.Enemy feedbackTarget;
        private Olca.Presentation.Effects.CombatNumberStyle trialNumberStyle;private OutsideExplorationSession.PresentationLease feedbackLease;
        private void InitializeTrialSword(Font font,Olca.Presentation.Field.GameFieldView field){
            var body=new GameObject("OutsideTrialTarget",typeof(RectTransform),typeof(Image));body.transform.SetParent(transform,false);trialTargetImage=body.GetComponent<Image>();trialTargetImage.sprite=field.AuthoredTrialEnemySprite;if(trialTargetImage.sprite==null)throw new InvalidOperationException("試用標的の既存素材がありません");trialTargetImage.raycastTarget=false;var rect=trialTargetImage.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,.21f);rect.pivot=trialTargetImage.sprite.pivot/trialTargetImage.sprite.rect.size;rect.sizeDelta=trialTargetImage.sprite.bounds.size*pixelsPerWorldUnit*.8f;rect.localScale=new Vector3(-1,1,1);
            var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,font,null);trialTargetName=factory.Label("OutsideTrialTargetName",transform,new Vector2(0,.21f),Vector2.zero,new Vector2(300,40));trialTargetName.font=field.AuthoredTrialEnemyFont??font;trialTargetName.fontSize=18;trialTargetName.alignment=TextAnchor.MiddleCenter;trialTargetName.color=Color.white;trialTargetName.raycastTarget=false;var plate=new GameObject("OutsideTrialTargetNamePlate",typeof(RectTransform),typeof(Image));plate.transform.SetParent(transform,false);var pr=(RectTransform)plate.transform;pr.anchorMin=pr.anchorMax=new Vector2(0,.21f);pr.sizeDelta=new Vector2(316,48);plate.GetComponent<Image>().color=new Color(.025f,.07f,.1f,.9f);plate.GetComponent<Image>().raycastTarget=false;pr.SetSiblingIndex(trialTargetName.transform.GetSiblingIndex());trialTargetPlate=pr;
            trialTargetHp=factory.Label("OutsideTrialTargetHp",transform,new Vector2(0,.21f),Vector2.zero,new Vector2(58,42));trialTargetHp.font=field.AuthoredTrialEnemyFont??font;trialTargetHp.fontSize=22;trialTargetHp.alignment=TextAnchor.MiddleCenter;trialTargetHp.color=Color.white;trialTargetHp.raycastTarget=false;
            var gauge=new GameObject("OutsideTrialTargetGauge",typeof(RectTransform),typeof(OutsideTrialHpArc));gauge.transform.SetParent(transform,false);trialTargetGauge=gauge.GetComponent<OutsideTrialHpArc>();trialTargetGauge.raycastTarget=false;var gr=(RectTransform)gauge.transform;gr.anchorMin=gr.anchorMax=new Vector2(0,.21f);gr.sizeDelta=new Vector2(82,82);gauge.transform.SetSiblingIndex(trialTargetHp.transform.GetSiblingIndex());
            var sword=new GameObject("OutsideTrialSword",typeof(RectTransform),typeof(Image));sword.transform.SetParent(transform,false);trialSwordImage=sword.GetComponent<Image>();trialSwordImage.raycastTarget=false;trialSwordImage.rectTransform.anchorMin=trialSwordImage.rectTransform.anchorMax=new Vector2(0,.21f);trialSwordImage.enabled=false;trialSwordImage.transform.SetSiblingIndex(trialTargetPlate.GetSiblingIndex());
            var trail=new GameObject("OutsideSwordTrail",typeof(RectTransform),typeof(OutsideSwordTrailGraphic));trail.transform.SetParent(transform,false);trialSwordTrail=trail.GetComponent<OutsideSwordTrailGraphic>();trialSwordTrail.raycastTarget=false;trialSwordTrail.rectTransform.anchorMin=trialSwordTrail.rectTransform.anchorMax=new Vector2(0,.21f);trialSwordTrail.rectTransform.sizeDelta=new Vector2(300,300);trail.transform.SetSiblingIndex(trialSwordImage.transform.GetSiblingIndex());trialSwordTrail.enabled=false;
            var numbers=new GameObject("OutsideTargetCombatFeedback",typeof(RectTransform));numbers.transform.SetParent(transform,false);var nr=(RectTransform)numbers.transform;nr.anchorMin=Vector2.zero;nr.anchorMax=Vector2.one;nr.offsetMin=nr.offsetMax=Vector2.zero;trialTargetFeedback=numbers.AddComponent<OutsideCombatFeedbackCanvas>();
        }
        private float TrialTargetTop()=>Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(trialTargetImage.sprite).size.y*pixelsPerWorldUnit*.8f;
        private void BindTrialTargetFeedback(){if(trialTargetFeedback==null||trialNumberStyle==null||presentation?.IsCurrent!=true)return;var lease=presentation;var target=lease.TrialTarget;if(ReferenceEquals(feedbackTarget,target)&&ReferenceEquals(feedbackLease,lease))return;feedbackTarget=target;feedbackLease=lease;trialTargetFeedback.BindEnemy(target,trialNumberStyle,()=>this!=null&&IsOpen&&ReferenceEquals(presentation,lease)&&lease.IsCurrent,()=>new Vector2((float)OutsideExplorationSession.TrialTargetX/OutsideMovementDefinition.UnitsPerWorldUnit,TrialTargetTop()/pixelsPerWorldUnit+.5f),pixelsPerWorldUnit);}
        private void RenderTrialSword(float camera,float viewport,float verticalOffset){
            if(trialTargetImage==null)return;BindTrialTargetFeedback();var core=presentation.Core;var target=presentation.TrialTarget;float x=((float)OutsideExplorationSession.TrialTargetX/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit;float y=-verticalOffset*pixelsPerWorldUnit;trialTargetImage.rectTransform.anchoredPosition=new Vector2(x,y-Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(trialTargetImage.sprite).min.y*pixelsPerWorldUnit*.8f);trialTargetImage.enabled=!target.IsDead;
            float top=TrialTargetTop()+44.55f+y;trialTargetHp.rectTransform.anchoredPosition=new Vector2(x,top);trialTargetHp.text=target.CurrentHp.ToString();trialTargetHp.enabled=!target.IsDead;trialTargetGauge.rectTransform.anchoredPosition=new Vector2(x,top);trialTargetGauge.SetFraction(target.CurrentHp/(float)target.MaxHp);trialTargetGauge.enabled=!target.IsDead;trialTargetName.rectTransform.anchoredPosition=new Vector2(x+210,top+50);trialTargetName.text=target.IsDead?"カイフクデ サイセイ":"ヒョウテキ（カリ） / ∞";
            trialTargetPlate.anchoredPosition=trialTargetName.rectTransform.anchoredPosition;
            var strike=presentation.TrialStrike;float swordAngle=0,swordAlpha=0;bool show=strike!=null&&OutsideSwordTrailGraphic.TrySample(core.Frame>=strike.Frame?core.Frame-strike.Frame:-1,strike.Facing,out swordAngle,out swordAlpha);trialSwordImage.enabled=show;trialSwordTrail.enabled=show;if(show){if(trialSwordAssets==null)trialSwordAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);if(trialSwordImage.sprite==null){if(!Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.Sword,out var sprite,trialSwordAssets))throw new InvalidOperationException("共用ソード素材がありません");trialSwordImage.sprite=sprite;trialSwordImage.rectTransform.pivot=sprite.pivot/sprite.rect.size;trialSwordImage.rectTransform.sizeDelta=sprite.bounds.size*pixelsPerWorldUnit;}trialSwordImage.rectTransform.anchoredPosition=new Vector2(((float)strike.X/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f+.35f*strike.Facing)*pixelsPerWorldUnit,((float)strike.Y/OutsideMovementDefinition.UnitsPerWorldUnit+1.2f-verticalOffset)*pixelsPerWorldUnit);float tip=(float)OutsideExplorationSession.TrialSwordRange/OutsideMovementDefinition.UnitsPerWorldUnit-.35f;float scale=tip/trialSwordImage.sprite.bounds.max.x;trialSwordImage.rectTransform.localScale=new Vector3(strike.Facing*scale,scale,1);trialSwordImage.rectTransform.localRotation=Quaternion.Euler(0,0,swordAngle);trialSwordImage.color=new Color(1,1,1,swordAlpha);trialSwordTrail.rectTransform.anchoredPosition=trialSwordImage.rectTransform.anchoredPosition;trialSwordTrail.rectTransform.localScale=new Vector3(strike.Facing,1,1);trialSwordTrail.SetPose(swordAngle*strike.Facing,swordAlpha,tip*pixelsPerWorldUnit);}
            if(trialTargetFeedback.ActiveCount>0){var obstacles=new List<RectTransform>{trialTargetPlate,trialTargetGauge.rectTransform};trialTargetFeedback.Paint(world=>new Vector2((world.x-camera)*pixelsPerWorldUnit,-((RectTransform)transform).rect.height*.29f+(world.y-verticalOffset)*pixelsPerWorldUnit),obstacles);}
        }
        private void ClearTrialSword(){if(trialSwordTrail!=null)trialSwordTrail.enabled=false;trialTargetFeedback?.Unbind();feedbackTarget=null;feedbackLease=null;trialNumberStyle=null;if(trialSwordImage!=null){trialSwordImage.enabled=false;trialSwordImage.sprite=null;}trialSwordAssets?.Dispose();trialSwordAssets=null;}
    }
}
