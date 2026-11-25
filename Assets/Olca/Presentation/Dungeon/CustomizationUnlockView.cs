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
        public Olca.Presentation.Management.ManagedScreenContext Context { get; private set; }
        private Action ensureManagedVisible,hideManaged;
        public bool CanInteract=>gameObject.activeInHierarchy&&(ensureManagedVisible==null||Context?.IsCurrent==true&&!Context.DialogBlocksInput);
        public void ConfigureManagement(Action show,Action hide){ensureManagedVisible=show;hideManaged=hide;}
        public void ShowManaged(Olca.Presentation.Management.ManagedScreenContext context){Context=context;gameObject.SetActive(true);}
        /// <summary>内容や焦点を変更せず、親を閉じる前に表示の寿命だけを確保する。</summary>
        public void PrepareManagedOpen()=>EnsureManaged();
        private void EnsureManaged(){if(ensureManagedVisible!=null&&Context?.IsCurrent!=true){ensureManagedVisible();if(Context?.IsCurrent!=true)throw new InvalidOperationException("装備画面の管理開始に失敗しました");}}
        public void HideManaged(){Context?.NotifyHidden();Context=null;menu?.Suspend();var selectedObject=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;if(this!=null&&selectedObject!=null&&selectedObject.transform.IsChildOf(transform))UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);state=null;catalog=null;selected=null;sizes.Clear();if(this!=null)gameObject.SetActive(false);}
        public void ReleaseManagement(){HideManaged();menu?.Dispose();menu=null;ensureManagedVisible=hideManaged=null;apply=null;cancel=retry=null;font=null;content=null;}
        private Action Live(Action action){var context=Context;var owner=content;return()=>{if(this!=null&&CanInteract&&ReferenceEquals(Context,context)&&content==owner&&owner!=null&&owner.activeInHierarchy)action?.Invoke();};}
        public void Initialize(Font value,Action<string> applyAction,Action cancelAction,Action retryAction)
        {
            font=value;apply=applyAction;cancel=cancelAction;retry=retryAction;var r=(RectTransform)transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            Olca.Presentation.Management.LiquidGlassMenu.Backdrop(gameObject.AddComponent<Image>());menu=new Olca.Unity.MenuPadScope(()=>CanInteract&&Olca.Unity.MenuPadScope.IsTopmost(transform));gameObject.SetActive(false);
        }
        public void Open(CustomizationCatalog definition,CustomizationProgress progress)
        {if(definition==null)throw new ArgumentNullException(nameof(definition));EnsureManaged();catalog=definition;state=(progress??new CustomizationProgress()).Copy();selected=null;page=offsetX=offsetY=0;pending=false;message="カイホウはホゾンされます。ソウビはヘンコウしません";gameObject.SetActive(true);transform.SetAsLastSibling();Render("CancelCustomizationUnlock");}
        public void Applied(CustomizationProgress progress){state=progress.Copy();pending=false;message="カイホウをホゾンしました";Render("CancelCustomizationUnlock");}
        public void SaveFailed(bool isPending){pending=isPending;message=isPending?"ホゾンがカンリョウしていません。サイシコウしてください":"カイホウできません。モドってカクニンしてください";Render(isPending?"RetryCustomizationUnlock":"CancelCustomizationUnlock");}
        public void Close(){if(ensureManagedVisible!=null&&Context?.IsCurrent==true)hideManaged();else HideManaged();}
        private void OnDisable(){Context?.NotifyHidden();Context=null;menu?.Suspend();state=null;catalog=null;}private void OnDestroy(){ReleaseManagement();}
        private void LateUpdate(){CustomizationMenuElements.FitReference(transform,content,sizes);menu?.Tick();}
        private Text Label(string name,string text,float x,float y,float w,float h,int size=26,bool white=false,TextAnchor align=TextAnchor.MiddleLeft)
        {var t=CustomizationMenuElements.Label(content.transform,font,name,text,x,y,w,h,size);t.alignment=align;Olca.Presentation.Management.LiquidGlassMenu.ReadingText(t);sizes[t]=size;return t;}
        private Button Button(string name,string text,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var invoke=Live(action);var b=CustomizationMenuElements.Button(content.transform,name,x,y,w,h);b.interactable=enabled;b.onClick.AddListener(()=>{if(this!=null&&state!=null&&b!=null&&b.gameObject.activeInHierarchy&&b.IsInteractable())Olca.Presentation.Management.LiquidGlassMenu.RunMenuAction(b,transform,invoke);});
            var t=Label(name+"Label",text,0,0,w-24,h-8,28,false,TextAnchor.MiddleCenter);t.transform.SetParent(b.transform,false);t.rectTransform.anchoredPosition=Vector2.zero;UiLook.Button(b);Olca.Presentation.Management.LiquidGlassMenu.Button(b);return b;
        }
        private void Render(string focus)
        {
            menu?.Suspend();sizes.Clear();content=CustomizationMenuElements.ReplaceContent(transform,content,"UnlockContent");
            CustomizationMenuElements.Face(content.transform,"UnlockHeader",0,460,1800,110,true);CustomizationMenuElements.Face(content.transform,"UnlockInventory",-620,20,680,720);CustomizationMenuElements.Face(content.transform,"UnlockBoard",330,20,1160,720,true);CustomizationMenuElements.Face(content.transform,"UnlockFooter",0,-490,1800,100,true);
            Label("UnlockTitle","マス解放",-600,465,540,60,36,true);Label("UnlockNotice","固定範囲をカイホウ / ソウビはヘンコウしません",330,465,1120,60,24,true);
            Label("UnlockItemsTitle","所持アイテム",-620,345,610,50,28);
            var items=state.OwnedUnlockItems;int pages=Math.Max(1,(items.Length+3)/4);page=Math.Min(page,pages-1);
            for(int i=0;i<4&&page*4+i<items.Length;i++){
                var item=items[page*4+i];string id=item.Id;bool applied=state.AppliedUnlockItemIds.Contains(id),known=catalog.UnlockItems.ContainsKey(id);float y=255-i*100;
                var b=Button("UnlockItem_"+id,"",-620,y,620,82,()=>{selected=id;var region=catalog.UnlockItems[id];offsetX=region.Min(v=>v.X)/8*8;offsetY=region.Min(v=>v.Y)/6*6;message="明るいマスがこのアイテムの固定範囲です";Render("ApplyCustomizationUnlock");},!pending&&known);
                var name=Label(b.name+"Name",CustomizationMenuElements.ShortName(id,14),0,0,380,42,25);name.transform.SetParent(b.transform,false);name.rectTransform.anchoredPosition=new Vector2(-70,13);
                var status=Label(b.name+"State",known?(applied?"カイホウズみ":"ミカイホウ"):"ミトウロク",0,0,380,32,22);status.transform.SetParent(b.transform,false);status.rectTransform.anchoredPosition=new Vector2(-70,-19);
                var count=Label(b.name+"Count","ショジ "+item.Count,0,0,130,48,22,false,TextAnchor.MiddleCenter);count.transform.SetParent(b.transform,false);count.rectTransform.anchoredPosition=new Vector2(222,0);
                if(selected==id)CustomizationMenuElements.SelectionMarker(b.transform,-300,60);
            }
            if(items.Length==0)Label("NoUnlockItems","解放アイテムはありません",-620,100,580,150,28);
            Button("UnlockPrevious","前へ",-790,-190,220,56,()=>{page--;Render("UnlockNext");},!pending&&page>0);Button("UnlockNext","次へ",-450,-190,220,56,()=>{page++;Render("UnlockPrevious");},!pending&&page+1<pages);Label("UnlockPage",$"{page+1}/{pages}",-620,-190,90,50,23,false,TextAnchor.MiddleCenter);
            var fixedCells=selected!=null&&catalog.UnlockItems.TryGetValue(selected,out var cells)?cells:Array.Empty<Cell>();var unlocked=new HashSet<Cell>(state.BoardId==null?catalog.Board.UnlockedCells:state.UnlockedCells.Select(v=>new Cell(v.X,v.Y)));
            Label("UnlockLegend","明るいマス: 固定範囲 / O: カイホウズみ",330,350,1080,60,24,true);
            for(int x=0;x<8&&offsetX+x<catalog.Board.Width;x++)Label("UnlockColumn_"+x,(offsetX+x+1).ToString(),-65+x*68,305,62,30,20,true,TextAnchor.MiddleCenter);
            for(int y=0;y<6&&offsetY+y<catalog.Board.Height;y++)Label("UnlockRow_"+y,(offsetY+y+1).ToString(),-120,255-y*68,38,30,20,true,TextAnchor.MiddleCenter);
            for(int y=0;y<6&&offsetY+y<catalog.Board.Height;y++)for(int x=0;x<8&&offsetX+x<catalog.Board.Width;x++){
                int cx=offsetX+x,cy=offsetY+y;var cell=new Cell(cx,cy);bool target=fixedCells.Contains(cell),open=unlocked.Contains(cell);var go=new GameObject("UnlockCell_"+cx+"_"+cy,typeof(RectTransform),typeof(Image));go.transform.SetParent(content.transform,false);var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(-65+x*68,255-y*68);r.sizeDelta=new Vector2(60,60);go.GetComponent<Image>().color=target?UiLook.Chosen:open?UiLook.ButtonFace:new Color(.25f,.38f,.43f,1);go.GetComponent<Image>().raycastTarget=false;
                var label=Label("UnlockCellLabel_"+cx+"_"+cy,open?"O":"X",0,0,60,60,27,!open&&!target,TextAnchor.MiddleCenter);label.transform.SetParent(go.transform,false);label.rectTransform.anchoredPosition=Vector2.zero;if(target){var edge=go.AddComponent<Outline>();edge.effectColor=new Color(.7f,1,1,1);edge.effectDistance=new Vector2(2,-2);}
            }
            Label("UnlockPreviewTitle",selected==null?"アイテムを選んでください":"固定範囲",690,290,300,75,26,true,TextAnchor.MiddleCenter);
            if(fixedCells.Count>0)CustomizationMenuElements.Shape(content.transform,"SelectedUnlockShape",fixedCells,690,180,100);
            string info=selected==null?"センタクしても、まだカイホウしません。":selected+"\n\nタイショウ "+fixedCells.Count+" マス\nアラたに "+fixedCells.Count(v=>!unlocked.Contains(v))+" マス\nヒョウジ "+fixedCells.Count(v=>v.X>=offsetX&&(long)v.X<((long)offsetX+8)&&v.Y>=offsetY&&(long)v.Y<((long)offsetY+6))+" / "+fixedCells.Count+" マス\nショジ "+(items.SingleOrDefault(v=>v.Id==selected)?.Count??0)+"\n"+(state.AppliedUnlockItemIds.Contains(selected)?"カイホウズみ":"ミカイホウ");
            var body=CustomizationMenuElements.ScrollText(content.transform,font,"UnlockPreviewInfo",Olca.Presentation.CustomScreen.CustomDisplay.Katakana(info),690,-35,310,280,24);sizes[body]=24;
            Button("UnlockBoardLeft","←",0,-220,110,52,()=>{offsetX=Math.Max(0,offsetX-8);Render("UnlockBoardRight");},!pending&&offsetX>0);Button("UnlockBoardRight","→",150,-220,110,52,()=>{offsetX+=8;Render("UnlockBoardLeft");},!pending&&(long)offsetX+8<catalog.Board.Width);Button("UnlockBoardUp","↑",300,-220,110,52,()=>{offsetY=Math.Max(0,offsetY-6);Render("UnlockBoardDown");},!pending&&offsetY>0);Button("UnlockBoardDown","↓",450,-220,110,52,()=>{offsetY+=6;Render("UnlockBoardUp");},!pending&&(long)offsetY+6<catalog.Board.Height);
            Label("UnlockMessage",message,0,-405,1700,60,26,true,TextAnchor.MiddleCenter);
            Button(pending?"RetryCustomizationUnlock":"ApplyCustomizationUnlock",pending?"保存を再試行":"解放して保存",-420,-485,700,70,()=>{if(pending)retry();else apply(selected);},pending||selected!=null&&!state.AppliedUnlockItemIds.Contains(selected));Button("CancelCustomizationUnlock","戻る",420,-485,700,70,cancel,!pending);
            CustomizationMenuElements.FitReference(transform,content,sizes);menu.Bind(transform,focus,Live(()=>{if(!pending)cancel();}));
        }
    }
}
