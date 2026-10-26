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
        private void LateUpdate(){menu?.Tick();}
        private void OnDisable(){menu?.Suspend();draft=null;}
        private void OnDestroy(){menu?.Dispose();}
        private Text Label(string name,string text,float x,float y,float w,float h,int size=26)
        {
            return CustomizationMenuElements.Label(content.transform,font,name,Olca.Presentation.CustomScreen.CustomDisplay.Katakana(text),x,y,w,h,size);
        }
        private Button Button(string name,string label,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var b=CustomizationMenuElements.Button(content.transform,name,x,y,w,h);
            b.onClick.AddListener(()=>{if(this!=null&&draft!=null&&gameObject.activeInHierarchy&&b!=null&&b.gameObject.activeInHierarchy&&b.IsInteractable())action();});b.interactable=enabled;
            var text=Label(name+"Label",label,0,0,w-12,h-8);text.transform.SetParent(b.transform,false);text.rectTransform.anchoredPosition=Vector2.zero;UiLook.Button(b);return b;
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
            menu?.Suspend();content=CustomizationMenuElements.ReplaceContent(transform,content,"CustomizationContent");
            Label("CustomizationTitle","ソウビ・パーツ",0,465,1400,60,36);
            Label("CustomizationAccessoriesTitle",$"ソウショクヒン {draft.Accessories.Count}/{draft.Catalog.AccessorySlots}",-650,390,550,55);
            var accessories=draft.OwnedAccessories.ToArray();int ap=Math.Max(1,(accessories.Length+PageSize-1)/PageSize);accessoryPage=Math.Min(accessoryPage,ap-1);
            for(int i=0;i<PageSize&&accessoryPage*PageSize+i<accessories.Length;i++){string id=accessories[accessoryPage*PageSize+i];Button("Accessory_"+id,(draft.Accessories.Contains(id)?"[ON] ":"[OFF] ")+id,-650,315-i*75,540,62,()=>{if(!draft.TryToggleAccessory(id))message="ソウビのワクがタりません";else message="ヘンコウはまだホゾンしていません";Render("Accessory_"+id);},!pending);}
            Button("AccessoriesPrevious","マエ",-790,5,230,50,()=>{accessoryPage--;Render("AccessoriesNext");},!pending&&accessoryPage>0);Button("AccessoriesNext","ツギ",-510,5,230,50,()=>{accessoryPage++;Render("AccessoriesPrevious");},!pending&&accessoryPage+1<ap);
            Label("CustomizationPartsTitle","ショジパーツ",-650,-65,550,50);
            var parts=draft.OwnedParts.ToArray();int pp=Math.Max(1,(parts.Length+PageSize-1)/PageSize);partPage=Math.Min(partPage,pp-1);
            for(int i=0;i<PageSize&&partPage*PageSize+i<parts.Length;i++){var part=parts[partPage*PageSize+i];string id=part.Id;int placed=draft.Placements.Count(v=>v.PartId==id);Button("Part_"+id,$"{id}  {placed}/{part.Count}",-650,-130-i*72,540,60,()=>{selectedPart=id;selectedInstance=null;rotation=PartRotation.None;Changed("RotateCustomizationPart");},!pending&&draft.Catalog.Parts.ContainsKey(id));}
            Button("PartsPrevious","マエ",-790,-410,230,50,()=>{partPage--;Render("PartsNext");},!pending&&partPage>0);Button("PartsNext","ツギ",-510,-410,230,50,()=>{partPage++;Render("PartsPrevious");},!pending&&partPage+1<pp);
            Label("CustomizationBoardTitle",$"パーツ {draft.Board.Width} x {draft.Board.Height} / ハンテンなし",250,390,1000,55);
            var layout=draft.Layout;
            for(int row=0;row<Rows&&offsetY+row<draft.Board.Height;row++)for(int col=0;col<Columns&&offsetX+col<draft.Board.Width;col++){
                int x=offsetX+col,y=offsetY+row;var point=new Cell(x,y);bool unlocked=draft.Board.IsUnlocked(point);bool occupied=layout.OccupiedCells.TryGetValue(point,out var instance);
                string text=!unlocked?"X":occupied?(draft.Placements.ToList().FindIndex(p=>p.InstanceId==instance)+1).ToString():$"{x+1},{y+1}";
                var b=Button("Cell_"+x+"_"+y,text,-65+col*68,285-row*68,62,62,()=>CellClicked(x,y),!pending);
                var colors=b.colors;colors.normalColor=!unlocked?new Color(.25f,.38f,.43f,1):occupied?UiLook.Chosen:UiLook.ButtonFace;b.colors=colors;
            }
            Button("BoardLeft","←",0,-155,110,52,()=>{offsetX=Math.Max(0,offsetX-Columns);Render("BoardRight");},!pending&&offsetX>0);Button("BoardRight","→",150,-155,110,52,()=>{offsetX+=Columns;Render("BoardLeft");},!pending&&(long)offsetX+Columns<draft.Board.Width);
            Button("BoardUp","↑",300,-155,110,52,()=>{offsetY=Math.Max(0,offsetY-Rows);Render("BoardDown");},!pending&&offsetY>0);Button("BoardDown","↓",450,-155,110,52,()=>{offsetY+=Rows;Render("BoardUp");},!pending&&(long)offsetY+Rows<draft.Board.Height);
            Label("CustomizationSelection",selectedPart==null?"パーツをエラんでマスをオしてください":selectedPart+" / "+(int)rotation+"ド",690,150,330,250);
            Button("RotateCustomizationPart","カイテン",130,-270,300,65,Rotate,!pending&&selectedPart!=null);
            Button("RemoveCustomizationPart","ハズす",480,-270,300,65,()=>{draft.Remove(selectedInstance);selectedPart=selectedInstance=null;Changed("ClearCustomizationSelection");},!pending&&selectedInstance!=null);
            Button("ClearCustomizationSelection","センタクをやめる",305,-350,650,60,()=>{selectedPart=selectedInstance=null;Changed("RotateCustomizationPart");},!pending);
            Label("CustomizationMessage",message,250,-410,1250,52,24);
            Button(pending?"RetryCustomizationSave":"SaveCustomization",pending?"ホゾンをサイシコウ":"ホゾン",-400,-485,650,70,pending?retry:save);
            Button("CancelCustomization","トリケシ",400,-485,650,70,cancel,!pending);
            menu.Bind(transform,pending?"RetryCustomizationSave":focus,()=>{if(!pending)cancel();});
        }
    }
}
