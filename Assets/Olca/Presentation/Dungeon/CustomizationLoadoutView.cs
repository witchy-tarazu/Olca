using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Customization;
using Cell=Olca.Core.Domain.Vector2Int;

namespace Olca.Presentation.Dungeon
{
    /// <summary>既存FHD Canvasと共通テーマを使う編集面。性能/保存判定はCoreとControllerへ渡す。</summary>
    public sealed class CustomizationLoadoutView:MonoBehaviour
    {
        private Font font;
        private CustomizationEditDraft draft;
        private Action save,cancel,retry;
        private GameObject content;
        private Olca.Unity.MenuPadScope menu;
        private string selectedPart,selectedInstance,message="";
        private PartRotation rotation;
        private int accessoryPage,partPage,offsetX,offsetY;
        private bool pending;
        private GameObject nameReadout;
        private Text nameReadoutText;
        private readonly System.Collections.Generic.Dictionary<Text,int> labelSizes=new System.Collections.Generic.Dictionary<Text,int>();
        private const int PageSize=4,Columns=8,Rows=6;
        public CustomizationEditDraft Draft=>draft;
        public void Initialize(Font value,Action saveAction,Action cancelAction,Action retryAction)
        {
            font=value;save=saveAction;cancel=cancelAction;retry=retryAction;
            var rect=(RectTransform)transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            UiLook.Face(gameObject.AddComponent<Image>());
            menu=new Olca.Unity.MenuPadScope(()=>gameObject.activeInHierarchy&&Olca.Unity.MenuPadScope.IsTopmost(transform));
            gameObject.SetActive(false);
        }
        public void Open(CustomizationEditDraft value)
        {
            draft=value??throw new ArgumentNullException(nameof(value));selectedPart=selectedInstance=null;rotation=PartRotation.None;
            accessoryPage=partPage=offsetX=offsetY=0;message="ヘンコウはまだホゾンしていません";pending=false;
            gameObject.SetActive(true);transform.SetAsLastSibling();Render("CancelCustomization");
        }
        public void Close(){draft=null;gameObject.SetActive(false);}
        public void SaveFailed(bool isPending){pending=isPending;message=isPending?"ホゾンがカンリョウしていません。サイシコウしてください":"ホゾンできません。トリケシしてヒラきナオしてください";Render(isPending?"RetryCustomizationSave":"CancelCustomization");}
        private void LateUpdate(){FitReference();menu?.Tick();}
        private void FitReference(){if(content==null)return;var available=((RectTransform)transform).rect.size;float scale=Mathf.Min(1,available.x/1920,available.y/1080);var rect=(RectTransform)content.transform;rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(1920,1080);rect.localScale=Vector3.one*Mathf.Max(.01f,scale);foreach(var pair in labelSizes)if(pair.Key!=null)pair.Key.fontSize=pair.Value<34?Mathf.RoundToInt(pair.Value*(scale<.9f?1.12f:1)):pair.Value;}
        private void OnDisable(){menu?.Suspend();draft=null;}
        private void OnDestroy(){menu?.Dispose();}
        private Text Label(string name,string text,float x,float y,float w,float h,int size=26)
        {
            var label=CustomizationMenuElements.Label(content.transform,font,name,Olca.Presentation.CustomScreen.CustomDisplay.Katakana(text),x,y,w,h,size);labelSizes[label]=size;return label;
        }
        private Button Button(string name,string label,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var b=CustomizationMenuElements.Button(content.transform,name,x,y,w,h);
            b.onClick.AddListener(()=>{if(this!=null&&draft!=null&&gameObject.activeInHierarchy&&b!=null&&b.gameObject.activeInHierarchy&&b.IsInteractable())action();});b.interactable=enabled;
            var text=Label(name+"Label",label,0,0,w-12,h-8);text.transform.SetParent(b.transform,false);text.rectTransform.anchoredPosition=Vector2.zero;UiLook.Button(b);return b;
        }
        private Image Face(string name,float x,float y,float w,float h,bool dark=false)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(content.transform,false);var image=go.GetComponent<Image>();image.sprite=Resources.Load<Sprite>(dark?"OlcaUI/description":"OlcaUI/window");image.type=Image.Type.Sliced;image.color=Color.white;image.raycastTarget=false;var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);return image;
        }
        private Text Reading(Text text,bool white=false,TextAnchor alignment=TextAnchor.MiddleLeft)
        {text.color=white?Color.white:UiLook.Ink;text.alignment=alignment;text.horizontalOverflow=HorizontalWrapMode.Wrap;return text;}
        private void Shape(Transform parent,string name,System.Collections.Generic.IReadOnlyList<Cell> cells,float x,float y,float size)
        {
            if(cells==null||cells.Count==0)return;float width=(float)((long)cells.Max(v=>v.X)+1),height=(float)((long)cells.Max(v=>v.Y)+1);float unit=Mathf.Min(size/width,size/height);
            var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var root=(RectTransform)go.transform;root.anchorMin=root.anchorMax=new Vector2(.5f,.5f);root.anchoredPosition=new Vector2(x,y);root.sizeDelta=new Vector2(size,size);
            foreach(var cell in cells){var tile=new GameObject("ShapeCell",typeof(RectTransform),typeof(Image));tile.transform.SetParent(root,false);var image=tile.GetComponent<Image>();image.color=new Color(.20f,.58f,.69f,1);image.raycastTarget=false;var r=(RectTransform)tile.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.sizeDelta=Vector2.one*Mathf.Max(.2f,unit-2);r.anchoredPosition=new Vector2((cell.X+.5f-width/2)*unit,(height/2-cell.Y-.5f)*unit);}
        }
        private void Candidate(Button b,string name,string state,bool selected)
        {
            var t=b.GetComponentInChildren<Text>();t.text="";string display=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(name);var elements=System.Globalization.StringInfo.ParseCombiningCharacters(display);bool longName=elements.Length>12;string shown=longName?display.Substring(0,elements[12])+"…":display;var label=Label(b.name+"Name",shown,0,0,380,52,26);label.transform.SetParent(b.transform,false);label.rectTransform.anchoredPosition=new Vector2(-45,0);Reading(label);label.supportRichText=false;
            var badge=Label(b.name+"State",state,0,0,110,48,22);badge.transform.SetParent(b.transform,false);badge.rectTransform.anchoredPosition=new Vector2(205,0);badge.color=UiLook.Ink;
            if(selected){var marker=new GameObject("SelectionMarker",typeof(RectTransform),typeof(Image));marker.transform.SetParent(b.transform,false);var image=marker.GetComponent<Image>();image.color=new Color(.07f,.42f,.53f,1);image.raycastTarget=false;var r=(RectTransform)marker.transform;r.anchoredPosition=new Vector2(-260,0);r.sizeDelta=new Vector2(5,42);}
            {var trigger=b.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();var owner=content;
                void Event(UnityEngine.EventSystems.EventTriggerType type,Action action){var entry=new UnityEngine.EventSystems.EventTrigger.Entry{eventID=type};entry.callback.AddListener(_=>{if(this!=null&&draft!=null&&content==owner&&owner!=null&&owner.activeInHierarchy)action();});trigger.triggers.Add(entry);}
                Event(UnityEngine.EventSystems.EventTriggerType.PointerEnter,()=>{if(longName)ShowName(name);else HideName();});Event(UnityEngine.EventSystems.EventTriggerType.Select,()=>{if(longName)ShowName(name);else HideName();});
            }
        }
        private void ShowName(string value){if(nameReadout==null)return;content.transform.Find("CustomizationSelection").gameObject.SetActive(false);var shape=content.transform.Find("SelectedPartShape");if(shape!=null)shape.gameObject.SetActive(false);nameReadoutText.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana("アイテムID（ジョウホウ）\n\n"+value);nameReadout.SetActive(true);Canvas.ForceUpdateCanvases();nameReadout.GetComponent<ScrollRect>().verticalNormalizedPosition=1;}
        private void HideName(){if(nameReadout!=null)nameReadout.SetActive(false);var title=content.transform.Find("CustomizationSelection");if(title!=null)title.gameObject.SetActive(true);var shape=content.transform.Find("SelectedPartShape");if(shape!=null)shape.gameObject.SetActive(true);}
        private void NameReadout()
        {
            nameReadout=Face("CustomizationNameReadout",690,170,310,300,true).gameObject;nameReadout.GetComponent<Image>().raycastTarget=true;var scroll=nameReadout.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            var viewport=new GameObject("Viewport",typeof(RectTransform),typeof(Image),typeof(Mask));viewport.transform.SetParent(nameReadout.transform,false);viewport.GetComponent<Mask>().showMaskGraphic=false;var vr=(RectTransform)viewport.transform;vr.anchorMin=Vector2.zero;vr.anchorMax=Vector2.one;vr.offsetMin=new Vector2(14,12);vr.offsetMax=new Vector2(-28,-12);
            var body=new GameObject("CustomizationFullName",typeof(RectTransform),typeof(Text),typeof(ContentSizeFitter));body.transform.SetParent(viewport.transform,false);nameReadoutText=body.GetComponent<Text>();nameReadoutText.font=font;nameReadoutText.fontSize=24;nameReadoutText.supportRichText=false;nameReadoutText.color=Color.white;nameReadoutText.alignment=TextAnchor.UpperLeft;nameReadoutText.raycastTarget=false;var br=(RectTransform)body.transform;br.anchorMin=new Vector2(0,1);br.anchorMax=Vector2.one;br.pivot=new Vector2(.5f,1);br.sizeDelta=Vector2.zero;body.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.viewport=vr;scroll.content=br;
            var bar=new GameObject("NameScrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar));bar.transform.SetParent(nameReadout.transform,false);var rr=(RectTransform)bar.transform;rr.anchorMin=new Vector2(1,0);rr.anchorMax=Vector2.one;rr.pivot=new Vector2(1,.5f);rr.offsetMin=new Vector2(-13,12);rr.offsetMax=new Vector2(-5,-12);bar.GetComponent<Image>().color=new Color(.5f,.8f,.9f,.15f);var handle=new GameObject("Handle",typeof(RectTransform),typeof(Image));handle.transform.SetParent(bar.transform,false);var hr=(RectTransform)handle.transform;hr.anchorMin=Vector2.zero;hr.anchorMax=Vector2.one;hr.offsetMin=hr.offsetMax=Vector2.zero;handle.GetComponent<Image>().color=new Color(.7f,.94f,1,.85f);var scrollbar=bar.GetComponent<Scrollbar>();scrollbar.handleRect=(RectTransform)handle.transform;scrollbar.targetGraphic=handle.GetComponent<Image>();scrollbar.direction=Scrollbar.Direction.BottomToTop;scrollbar.navigation=new Navigation{mode=Navigation.Mode.None};scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;nameReadout.SetActive(false);
        }
        private void Changed(string focus){message="ヘンコウはまだホゾンしていません";Render(focus);}
        private void CellClicked(int x,int y)
        {
            var point=new Cell(x,y);
            if(selectedPart==null){
                if(draft.Layout.OccupiedCells.TryGetValue(point,out var id)){var p=draft.Placements.Single(v=>v.InstanceId==id);selectedInstance=id;selectedPart=p.PartId;rotation=p.Rotation;Changed("RotateCustomizationPart");}
                return;
            }
            string instance=selectedInstance??Guid.NewGuid().ToString("N");
            if(draft.TryPlace(instance,selectedPart,point,rotation,out var reason)){selectedInstance=instance;Changed("Cell_"+x+"_"+y);}
            else{message=reason==PartsLayoutError.LockedCell?"ミカイホウのマスです":reason==PartsLayoutError.Overlap?"パーツがカサなります":reason==PartsLayoutError.NotOwned?"ショジスウがタりません":"ハイチできないイチです";Render("Cell_"+x+"_"+y);}
        }
        private void Rotate()
        {
            var next=(PartRotation)(((int)rotation+90)%360);
            if(selectedInstance!=null){var p=draft.Placements.Single(v=>v.InstanceId==selectedInstance);if(!draft.TryPlace(p.InstanceId,p.PartId,p.Anchor,next,out _)){message="このイチではカイテンできません";Render("RotateCustomizationPart");return;}}
            rotation=next;Changed("RotateCustomizationPart");
        }
        private void Render(string focus)
        {
            menu?.Suspend();labelSizes.Clear();content=CustomizationMenuElements.ReplaceContent(transform,content,"CustomizationContent");
            var backdrop=GetComponent<Image>();backdrop.sprite=null;backdrop.type=Image.Type.Simple;backdrop.color=new Color(.025f,.075f,.10f,1);
            Face("EquipmentHeader",0,460,1800,110,true);Face("EquipmentInventory",-650,-5,600,850);Face("EquipmentBoard",330,90,1160,650,true);Face("EquipmentTools",330,-310,1160,165);Face("EquipmentFooter",0,-490,1800,100,true);
            Reading(Label("CustomizationTitle","ソウビ・パーツ",-600,465,540,60,36),true);
            Reading(Label("CustomizationDraftNotice","シュツゲキマエのヘンシュウ / ホゾンするまでハンエイしません",330,465,1120,60,24),true);
            Face("AccessoriesHeading",-650,390,570,60,true);Face("PartsHeading",-650,-65,570,55,true);
            Reading(Label("CustomizationAccessoriesTitle",$"ソウショクヒン  {draft.Accessories.Count} / {draft.Catalog.AccessorySlots} ワク",-650,390,530,55),true);
            var accessories=draft.OwnedAccessories.ToArray();int ap=Math.Max(1,(accessories.Length+PageSize-1)/PageSize);accessoryPage=Math.Min(accessoryPage,ap-1);
            for(int i=0;i<PageSize&&accessoryPage*PageSize+i<accessories.Length;i++){string id=accessories[accessoryPage*PageSize+i];var row=Button("Accessory_"+id,"",-650,315-i*75,540,62,()=>{if(!draft.TryToggleAccessory(id))message="ソウビのワクがタりません";else message="ヘンコウはまだホゾンしていません";Render("Accessory_"+id);},!pending);Candidate(row,id,draft.Accessories.Contains(id)?"ソウビ中":"コウホ",draft.Accessories.Contains(id));}
            if(accessories.Length==0)Reading(Label("NoCustomizationAccessories","ソウショクヒンはありません",-650,240,520,90));
            Button("AccessoriesPrevious","マエ",-790,5,200,50,()=>{accessoryPage--;Render("AccessoriesNext");},!pending&&accessoryPage>0);Button("AccessoriesNext","ツギ",-510,5,200,50,()=>{accessoryPage++;Render("AccessoriesPrevious");},!pending&&accessoryPage+1<ap);
            Reading(Label("CustomizationPartsTitle","ショジパーツ / ハイチスウ・ショジスウ",-650,-65,530,50,24),true);
            var parts=draft.OwnedParts.ToArray();int pp=Math.Max(1,(parts.Length+PageSize-1)/PageSize);partPage=Math.Min(partPage,pp-1);
            for(int i=0;i<PageSize&&partPage*PageSize+i<parts.Length;i++){var part=parts[partPage*PageSize+i];string id=part.Id;int placed=draft.Placements.Count(v=>v.PartId==id);var row=Button("Part_"+id,"",-650,-130-i*68,540,60,()=>{selectedPart=id;selectedInstance=null;rotation=PartRotation.None;Changed("RotateCustomizationPart");},!pending&&draft.Catalog.Parts.ContainsKey(id));Candidate(row,id,$"{placed} / {part.Count}",selectedPart==id);row.transform.Find(row.name+"Name").GetComponent<RectTransform>().anchoredPosition=new Vector2(-10,0);row.transform.Find(row.name+"Name").GetComponent<RectTransform>().sizeDelta=new Vector2(330,52);if(draft.Catalog.Parts.TryGetValue(id,out var shape))Shape(row.transform,"PartShape_"+id,shape.Cells,-225,0,42);}
            if(parts.Length==0)Reading(Label("NoCustomizationParts","ショジパーツはありません",-650,-220,520,90));
            Button("PartsPrevious","マエ",-790,-395,200,44,()=>{partPage--;Render("PartsNext");},!pending&&partPage>0);Button("PartsNext","ツギ",-510,-395,200,44,()=>{partPage++;Render("PartsPrevious");},!pending&&partPage+1<pp);
            Reading(Label("CustomizationBoardTitle",$"ハイチバンメン  {draft.Board.Width} x {draft.Board.Height} / 90ドカイテン・ハンテンなし",330,390,1080,55,26),true);
            Reading(Label("AccessoriesPage",$"{accessoryPage+1}/{ap}",-650,5,80,44,22),false,TextAnchor.MiddleCenter);
            Reading(Label("PartsPage",$"{partPage+1}/{pp}",-650,-395,80,44,22),false,TextAnchor.MiddleCenter);
            var layout=draft.Layout;
            for(int col=0;col<Columns&&offsetX+col<draft.Board.Width;col++)Reading(Label("BoardColumn_"+col,(offsetX+col+1).ToString(),-65+col*68,335,62,30,20),true,TextAnchor.MiddleCenter);
            for(int row=0;row<Rows&&offsetY+row<draft.Board.Height;row++)Reading(Label("BoardRow_"+row,(offsetY+row+1).ToString(),-120,285-row*68,38,30,20),true,TextAnchor.MiddleCenter);
            for(int row=0;row<Rows&&offsetY+row<draft.Board.Height;row++)for(int col=0;col<Columns&&offsetX+col<draft.Board.Width;col++){
                int x=offsetX+col,y=offsetY+row;var point=new Cell(x,y);bool unlocked=draft.Board.IsUnlocked(point);bool occupied=layout.OccupiedCells.TryGetValue(point,out var instance);
                string text=!unlocked?"X":occupied?(draft.Placements.ToList().FindIndex(p=>p.InstanceId==instance)+1).ToString():"";
                var b=Button("Cell_"+x+"_"+y,text,-65+col*68,285-row*68,62,62,()=>CellClicked(x,y),!pending);
                var colors=b.colors;colors.normalColor=!unlocked?new Color(.25f,.38f,.43f,1):occupied?UiLook.Chosen:UiLook.ButtonFace;b.colors=colors;if(occupied&&instance==selectedInstance){var outline=b.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.8f,1,1,1);outline.effectDistance=new Vector2(2,-2);}
            }
            Button("BoardLeft","←",0,-155,110,52,()=>{offsetX=Math.Max(0,offsetX-Columns);Render("BoardRight");},!pending&&offsetX>0);Button("BoardRight","→",150,-155,110,52,()=>{offsetX+=Columns;Render("BoardLeft");},!pending&&(long)offsetX+Columns<draft.Board.Width);
            Button("BoardUp","↑",300,-155,110,52,()=>{offsetY=Math.Max(0,offsetY-Rows);Render("BoardDown");},!pending&&offsetY>0);Button("BoardDown","↓",450,-155,110,52,()=>{offsetY+=Rows;Render("BoardUp");},!pending&&(long)offsetY+Rows<draft.Board.Height);
            Reading(Label("CustomizationSelection",selectedPart==null?"パーツをエラんで\nマスをオしてください":selectedPart,690,285,300,100,26),true,TextAnchor.MiddleCenter);
            if(selectedPart!=null&&draft.Catalog.Parts.TryGetValue(selectedPart,out var selectedShape))Shape(content.transform,"SelectedPartShape",selectedShape.RotatedCells(rotation),690,130,130);
            Reading(Label("CustomizationSelectionState",selectedPart==null?"ミカイホウ: X\nハイチズミ: バンゴウ":(selectedInstance==null?"ハイチマエ":"ハイチズミをヘンシュウ")+" / "+(int)rotation+"ド",690,-10,300,100,22),true,TextAnchor.MiddleCenter);
            Button("RotateCustomizationPart","カイテン",130,-270,300,65,Rotate,!pending&&selectedPart!=null);
            Button("RemoveCustomizationPart","ハズす",480,-270,300,65,()=>{draft.Remove(selectedInstance);selectedPart=selectedInstance=null;Changed("ClearCustomizationSelection");},!pending&&selectedInstance!=null);
            Button("ClearCustomizationSelection","センタクをやめる",305,-350,650,60,()=>{selectedPart=selectedInstance=null;Changed("RotateCustomizationPart");},!pending);
            Reading(Label("CustomizationMessage",message,0,-419,1700,36,24),true,TextAnchor.MiddleCenter);
            Button(pending?"RetryCustomizationSave":"SaveCustomization",pending?"ホゾンをサイシコウ":"ホゾン",-400,-485,650,70,pending?retry:save);
            Button("CancelCustomization","トリケシ",400,-485,650,70,cancel,!pending);
            NameReadout();FitReference();menu.Bind(transform,pending?"RetryCustomizationSave":focus,()=>{if(!pending)cancel();});
        }
    }
}
