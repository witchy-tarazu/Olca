using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Customization;
using Cell=Olca.Core.Domain.Vector2Int;
namespace Olca.Presentation.Dungeon
{
    /// <summary>固定領域の確認と永続解放。表示は所持・定義・保存状態だけから組み立てる。</summary>
    public sealed class CustomizationUnlockView : MonoBehaviour
    {
        private Font font;private CustomizationCatalog catalog;private CustomizationProgress state;
        private Action<string> apply;private Action cancel,retry;private Olca.Unity.MenuPadScope menu;private GameObject content;
        private readonly Dictionary<Text,int> sizes=new Dictionary<Text,int>();
        private string selected,message;private int page,offsetX,offsetY;private bool pending;
        public void Initialize(Font value,Action<string> applyAction,Action cancelAction,Action retryAction)
        {
            font=value;apply=applyAction;cancel=cancelAction;retry=retryAction;var r=(RectTransform)transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            UiLook.Face(gameObject.AddComponent<Image>());menu=new Olca.Unity.MenuPadScope(()=>gameObject.activeInHierarchy&&Olca.Unity.MenuPadScope.IsTopmost(transform));gameObject.SetActive(false);
        }
        public void Open(CustomizationCatalog definition,CustomizationProgress progress)
        {catalog=definition??throw new ArgumentNullException(nameof(definition));state=(progress??new CustomizationProgress()).Copy();selected=null;page=offsetX=offsetY=0;pending=false;message="カイホウはホゾンされます。ソウビはヘンコウしません";gameObject.SetActive(true);transform.SetAsLastSibling();Render("CancelCustomizationUnlock");}
        public void Applied(CustomizationProgress progress){state=progress.Copy();pending=false;message="カイホウをホゾンしました";Render("CancelCustomizationUnlock");}
        public void SaveFailed(bool isPending){pending=isPending;message=isPending?"ホゾンがカンリョウしていません。サイシコウしてください":"カイホウできません。モドってカクニンしてください";Render(isPending?"RetryCustomizationUnlock":"CancelCustomizationUnlock");}
        public void Close(){state=null;gameObject.SetActive(false);}
        private void OnDisable(){menu?.Suspend();state=null;}private void OnDestroy(){menu?.Dispose();}
        private void LateUpdate(){CustomizationMenuElements.FitReference(transform,content,sizes);menu?.Tick();}
        private Text Label(string name,string text,float x,float y,float w,float h,int size=26,bool white=false,TextAnchor align=TextAnchor.MiddleLeft)
        {var t=CustomizationMenuElements.Label(content.transform,font,name,Olca.Presentation.CustomScreen.CustomDisplay.Katakana(text),x,y,w,h,size);t.alignment=align;t.color=white?Color.white:UiLook.Ink;sizes[t]=size;return t;}
        private Button Button(string name,string text,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var b=CustomizationMenuElements.Button(content.transform,name,x,y,w,h);b.interactable=enabled;b.onClick.AddListener(()=>{if(this!=null&&state!=null&&gameObject.activeInHierarchy&&b!=null&&b.gameObject.activeInHierarchy&&b.IsInteractable())action();});
            var t=Label(name+"Label",text,0,0,w-24,h-8,28,false,TextAnchor.MiddleCenter);t.transform.SetParent(b.transform,false);t.rectTransform.anchoredPosition=Vector2.zero;UiLook.Button(b);return b;
        }
        private void Render(string focus)
        {
            menu?.Suspend();sizes.Clear();content=CustomizationMenuElements.ReplaceContent(transform,content,"UnlockContent");CustomizationMenuElements.Backdrop(GetComponent<Image>());
            CustomizationMenuElements.Face(content.transform,"UnlockHeader",0,460,1800,110,true);CustomizationMenuElements.Face(content.transform,"UnlockInventory",-620,20,680,720);CustomizationMenuElements.Face(content.transform,"UnlockBoard",330,20,1160,720,true);CustomizationMenuElements.Face(content.transform,"UnlockFooter",0,-490,1800,100,true);
            Label("UnlockTitle","マスカイホウ",-600,465,540,60,36,true);Label("UnlockNotice","コテイリョウイキをカイホウ / ソウビはヘンコウしません",330,465,1120,60,24,true);
            Label("UnlockItemsTitle","ショジアイテム",-620,345,610,50,28);
            var items=state.OwnedUnlockItems;int pages=Math.Max(1,(items.Length+3)/4);page=Math.Min(page,pages-1);
            for(int i=0;i<4&&page*4+i<items.Length;i++){
                var item=items[page*4+i];string id=item.Id;bool applied=state.AppliedUnlockItemIds.Contains(id),known=catalog.UnlockItems.ContainsKey(id);float y=255-i*100;
                var b=Button("UnlockItem_"+id,"",-620,y,620,82,()=>{selected=id;var region=catalog.UnlockItems[id];offsetX=region.Min(v=>v.X)/8*8;offsetY=region.Min(v=>v.Y)/6*6;message="アカるいマスがこのアイテムのコテイリョウイキです";Render("ApplyCustomizationUnlock");},!pending&&known);
                var name=Label(b.name+"Name",CustomizationMenuElements.ShortName(id,14),0,0,380,42,25);name.transform.SetParent(b.transform,false);name.rectTransform.anchoredPosition=new Vector2(-70,13);
                var status=Label(b.name+"State",known?(applied?"カイホウズみ":"ミカイホウ"):"ミトウロク",0,0,380,32,22);status.transform.SetParent(b.transform,false);status.rectTransform.anchoredPosition=new Vector2(-70,-19);
                var count=Label(b.name+"Count","ショジ "+item.Count,0,0,130,48,22,false,TextAnchor.MiddleCenter);count.transform.SetParent(b.transform,false);count.rectTransform.anchoredPosition=new Vector2(222,0);
                if(selected==id)CustomizationMenuElements.SelectionMarker(b.transform,-300,60);
            }
            if(items.Length==0)Label("NoUnlockItems","カイホウアイテムはありません",-620,100,580,150,28);
            Button("UnlockPrevious","マエ",-790,-190,220,56,()=>{page--;Render("UnlockNext");},!pending&&page>0);Button("UnlockNext","ツギ",-450,-190,220,56,()=>{page++;Render("UnlockPrevious");},!pending&&page+1<pages);Label("UnlockPage",$"{page+1}/{pages}",-620,-190,90,50,23,false,TextAnchor.MiddleCenter);
            var fixedCells=selected!=null&&catalog.UnlockItems.TryGetValue(selected,out var cells)?cells:Array.Empty<Cell>();var unlocked=new HashSet<Cell>(state.BoardId==null?catalog.Board.UnlockedCells:state.UnlockedCells.Select(v=>new Cell(v.X,v.Y)));
            Label("UnlockLegend","アカるいマス: コテイリョウイキ / O: カイホウズみ",330,350,1080,60,24,true);
            for(int x=0;x<8&&offsetX+x<catalog.Board.Width;x++)Label("UnlockColumn_"+x,(offsetX+x+1).ToString(),-65+x*68,305,62,30,20,true,TextAnchor.MiddleCenter);
            for(int y=0;y<6&&offsetY+y<catalog.Board.Height;y++)Label("UnlockRow_"+y,(offsetY+y+1).ToString(),-120,255-y*68,38,30,20,true,TextAnchor.MiddleCenter);
            for(int y=0;y<6&&offsetY+y<catalog.Board.Height;y++)for(int x=0;x<8&&offsetX+x<catalog.Board.Width;x++){
                int cx=offsetX+x,cy=offsetY+y;var cell=new Cell(cx,cy);bool target=fixedCells.Contains(cell),open=unlocked.Contains(cell);var go=new GameObject("UnlockCell_"+cx+"_"+cy,typeof(RectTransform),typeof(Image));go.transform.SetParent(content.transform,false);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(-65+x*68,255-y*68);r.sizeDelta=new Vector2(60,60);go.GetComponent<Image>().color=target?UiLook.Chosen:open?UiLook.ButtonFace:new Color(.25f,.38f,.43f,1);go.GetComponent<Image>().raycastTarget=false;
                var label=Label("UnlockCellLabel_"+cx+"_"+cy,open?"O":"X",0,0,60,60,27,!open&&!target,TextAnchor.MiddleCenter);label.transform.SetParent(go.transform,false);label.rectTransform.anchoredPosition=Vector2.zero;if(target){var edge=go.AddComponent<Outline>();edge.effectColor=new Color(.7f,1,1,1);edge.effectDistance=new Vector2(2,-2);}
            }
            Label("UnlockPreviewTitle",selected==null?"アイテムをエラんでください":"コテイリョウイキ",690,290,300,75,26,true,TextAnchor.MiddleCenter);
            if(fixedCells.Count>0)CustomizationMenuElements.Shape(content.transform,"SelectedUnlockShape",fixedCells,690,180,100);
            string info=selected==null?"センタクしても、まだカイホウしません。":selected+"\n\nタイショウ "+fixedCells.Count+" マス\nアラたに "+fixedCells.Count(v=>!unlocked.Contains(v))+" マス\nヒョウジ "+fixedCells.Count(v=>v.X>=offsetX&&(long)v.X<((long)offsetX+8)&&v.Y>=offsetY&&(long)v.Y<((long)offsetY+6))+" / "+fixedCells.Count+" マス\nショジ "+(items.SingleOrDefault(v=>v.Id==selected)?.Count??0)+"\n"+(state.AppliedUnlockItemIds.Contains(selected)?"カイホウズみ":"ミカイホウ");
            var body=CustomizationMenuElements.ScrollText(content.transform,font,"UnlockPreviewInfo",Olca.Presentation.CustomScreen.CustomDisplay.Katakana(info),690,-35,310,280,24);sizes[body]=24;
            Button("UnlockBoardLeft","←",0,-220,110,52,()=>{offsetX=Math.Max(0,offsetX-8);Render("UnlockBoardRight");},!pending&&offsetX>0);Button("UnlockBoardRight","→",150,-220,110,52,()=>{offsetX+=8;Render("UnlockBoardLeft");},!pending&&(long)offsetX+8<catalog.Board.Width);Button("UnlockBoardUp","↑",300,-220,110,52,()=>{offsetY=Math.Max(0,offsetY-6);Render("UnlockBoardDown");},!pending&&offsetY>0);Button("UnlockBoardDown","↓",450,-220,110,52,()=>{offsetY+=6;Render("UnlockBoardUp");},!pending&&(long)offsetY+6<catalog.Board.Height);
            Label("UnlockMessage",message,0,-405,1700,60,26,true,TextAnchor.MiddleCenter);
            Button(pending?"RetryCustomizationUnlock":"ApplyCustomizationUnlock",pending?"ホゾンをサイシコウ":"カイホウしてホゾン",-420,-485,700,70,()=>{if(pending)retry();else apply(selected);},pending||selected!=null&&!state.AppliedUnlockItemIds.Contains(selected));Button("CancelCustomizationUnlock","モドル",420,-485,700,70,cancel,!pending);
            CustomizationMenuElements.FitReference(transform,content,sizes);menu.Bind(transform,focus,()=>{if(!pending)cancel();});
        }
    }
}
