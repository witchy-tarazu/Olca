using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Customization;
using Cell=Olca.Core.Domain.Vector2Int;
namespace Olca.Presentation.Dungeon
{
    /// <summary>装備下書きと分離した永続解放画面。固定領域を確認してから保存する。</summary>
    public sealed class CustomizationUnlockView : MonoBehaviour
    {
        private Font font;
        private CustomizationCatalog catalog;
        private CustomizationProgress state;
        private Action<string> apply;
        private Action cancel,retry;
        private Olca.Unity.MenuPadScope menu;
        private GameObject content;
        private string selected,message;
        private int page,offsetX,offsetY;
        private bool pending;
        public void Initialize(Font value,Action<string> applyAction,Action cancelAction,Action retryAction)
        {
            font=value;apply=applyAction;cancel=cancelAction;retry=retryAction;
            var rect=(RectTransform)transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            UiLook.Face(gameObject.AddComponent<Image>());
            menu=new Olca.Unity.MenuPadScope(()=>gameObject.activeInHierarchy&&Olca.Unity.MenuPadScope.IsTopmost(transform));gameObject.SetActive(false);
        }
        public void Open(CustomizationCatalog definition,CustomizationProgress progress)
        {
            catalog=definition??throw new ArgumentNullException(nameof(definition));state=(progress??new CustomizationProgress()).Copy();
            selected=null;page=offsetX=offsetY=0;pending=false;message="カイホウはホゾンされます。ソウビはヘンコウしません";
            gameObject.SetActive(true);transform.SetAsLastSibling();Render("CancelCustomizationUnlock");
        }
        public void Applied(CustomizationProgress progress){state=progress.Copy();pending=false;message="カイホウをホゾンしました";Render("CancelCustomizationUnlock");}
        public void SaveFailed(bool isPending){pending=isPending;message=isPending?"ホゾンがカンリョウしていません。サイシコウしてください":"カイホウできません。モドってカクニンしてください";Render(isPending?"RetryCustomizationUnlock":"CancelCustomizationUnlock");}
        public void Close(){state=null;gameObject.SetActive(false);}
        private void OnDisable(){menu?.Suspend();state=null;}
        private void OnDestroy(){menu?.Dispose();}
        private void LateUpdate(){menu?.Tick();}
        private Text Label(string name,string text,float x,float y,float w,float h,int size=26)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(content.transform,false);
            var t=go.GetComponent<Text>();t.font=font;t.fontSize=size;t.alignment=TextAnchor.MiddleCenter;t.text=text;UiLook.Label(t);
            t.rectTransform.anchorMin=t.rectTransform.anchorMax=new Vector2(.5f,.5f);t.rectTransform.anchoredPosition=new Vector2(x,y);t.rectTransform.sizeDelta=new Vector2(w,h);return t;
        }
        private Button Button(string name,string text,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(content.transform,false);
            var rect=(RectTransform)go.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(w,h);
            var button=go.GetComponent<Button>();button.navigation=new Navigation{mode=Navigation.Mode.None};button.interactable=enabled;
            button.onClick.AddListener(()=>{if(this!=null&&state!=null&&gameObject.activeInHierarchy&&button!=null&&button.gameObject.activeInHierarchy&&button.IsInteractable())action();});
            var label=Label(name+"Label",text,0,0,w-12,h-8);label.transform.SetParent(go.transform,false);label.rectTransform.anchoredPosition=Vector2.zero;UiLook.Button(button);return button;
        }
        private void Render(string focus)
        {
            menu?.Suspend();if(content!=null){content.SetActive(false);if(Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("UnlockContent",typeof(RectTransform));content.transform.SetParent(transform,false);
            var rect=(RectTransform)content.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            Label("UnlockTitle","マスカイホウ",0,455,1500,70,36);
            var items=state.OwnedUnlockItems;int pages=Math.Max(1,(items.Length+3)/4);page=Math.Min(page,pages-1);
            for(int i=0;i<4&&page*4+i<items.Length;i++){
                var item=items[page*4+i];string id=item.Id;bool applied=state.AppliedUnlockItemIds.Contains(id);
                Button("UnlockItem_"+id,id+(applied?" / カイホウズみ":" / ミカイホウ"),-590,290-i*100,650,80,()=>{selected=id;offsetX=offsetY=0;message="アカるいマスがこのアイテムのコテイリョウイキです";Render("ApplyCustomizationUnlock");},!pending&&catalog.UnlockItems.ContainsKey(id));
            }
            Button("UnlockPrevious","マエ",-760,-140,260,60,()=>{page--;Render("UnlockNext");},!pending&&page>0);
            Button("UnlockNext","ツギ",-420,-140,260,60,()=>{page++;Render("UnlockPrevious");},!pending&&page+1<pages);
            var fixedCells=selected!=null&&catalog.UnlockItems.TryGetValue(selected,out var cells)?cells:Array.Empty<Cell>();
            var unlocked=state.BoardId==null?catalog.Board.UnlockedCells:state.UnlockedCells.Select(v=>new Cell(v.X,v.Y));
            Label("UnlockLegend","アカるいマス: コテイリョウイキ / O: カイホウズみ",330,365,1000,80,24);
            for(int y=0;y<6&&offsetY+y<catalog.Board.Height;y++)for(int x=0;x<8&&offsetX+x<catalog.Board.Width;x++){
                int cx=offsetX+x,cy=offsetY+y;var cell=new Cell(cx,cy);bool target=fixedCells.Contains(cell),open=unlocked.Contains(cell);
                var bg=new GameObject("UnlockCell_"+cx+"_"+cy,typeof(RectTransform),typeof(Image));bg.transform.SetParent(content.transform,false);
                var br=(RectTransform)bg.transform;br.anchorMin=br.anchorMax=new Vector2(.5f,.5f);br.anchoredPosition=new Vector2(60+x*86,260-y*86);br.sizeDelta=new Vector2(78,78);
                bg.GetComponent<Image>().color=target?UiLook.Chosen:open?UiLook.ButtonFace:new Color(.25f,.38f,.43f,1);bg.GetComponent<Image>().raycastTarget=false;
                var label=Label("UnlockCellLabel_"+cx+"_"+cy,open?"O":"X",0,0,78,78,30);label.transform.SetParent(bg.transform,false);label.rectTransform.anchoredPosition=Vector2.zero;
            }
            Button("UnlockBoardLeft","←",120,-300,120,55,()=>{offsetX=Math.Max(0,offsetX-8);Render("UnlockBoardRight");},!pending&&offsetX>0);
            Button("UnlockBoardRight","→",280,-300,120,55,()=>{offsetX+=8;Render("UnlockBoardLeft");},!pending&&(long)offsetX+8<catalog.Board.Width);
            Button("UnlockBoardUp","↑",440,-300,120,55,()=>{offsetY=Math.Max(0,offsetY-6);Render("UnlockBoardDown");},!pending&&offsetY>0);
            Button("UnlockBoardDown","↓",600,-300,120,55,()=>{offsetY+=6;Render("UnlockBoardUp");},!pending&&(long)offsetY+6<catalog.Board.Height);
            Label("UnlockMessage",message,0,-390,1700,80,26);
            Button(pending?"RetryCustomizationUnlock":"ApplyCustomizationUnlock",pending?"ホゾンをサイシコウ":"カイホウしてホゾン",-420,-480,700,75,()=>{if(pending)retry();else apply(selected);},pending||selected!=null&&!state.AppliedUnlockItemIds.Contains(selected));
            Button("CancelCustomizationUnlock","モドル",420,-480,700,75,cancel,!pending);
            menu.Bind(transform,focus,()=>{if(!pending)cancel();});
        }
    }
}
