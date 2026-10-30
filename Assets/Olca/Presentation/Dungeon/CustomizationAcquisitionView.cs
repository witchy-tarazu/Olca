using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Customization;
namespace Olca.Presentation.Dungeon
{
    /// <summary>保存済み候補の内容確認と受領結果。数量と可否は既存Plan/Availabilityを使う。</summary>
    public sealed class CustomizationAcquisitionView:MonoBehaviour
    {
        private Font font;private Action<string> claim;private Action close,retry;private Olca.Unity.MenuPadScope menu;private GameObject content;
        private readonly Dictionary<Text,int> sizes=new Dictionary<Text,int>();
        private CustomizationAcquisitionPlan plan;private CustomizationProgress progress;private CustomizationCatalog catalog;
        private const string ChoiceGuide="コウホをエラんでカクテイしてください";
        private string selected,lastReceived,message;private int page;private bool pending;
        public void Initialize(Font value,Action<string> receive,Action cancel,Action retrySave)
        {font=value;claim=receive;close=cancel;retry=retrySave;var r=(RectTransform)transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;UiLook.Face(gameObject.AddComponent<Image>());menu=new Olca.Unity.MenuPadScope(()=>gameObject.activeInHierarchy&&Olca.Unity.MenuPadScope.IsTopmost(transform));gameObject.SetActive(false);}
        public void Open(CustomizationAcquisitionPlan value,CustomizationProgress state,bool saving=false)=>Open(value,state,saving,null);
        public void Open(CustomizationAcquisitionPlan value,CustomizationProgress state,bool saving,CustomizationCatalog definitions)
        {plan=value?.Copy();progress=state?.Copy();catalog=definitions;selected=lastReceived=null;page=0;pending=saving;message=saving?"ホゾンをサイシコウしてください":ChoiceGuide;gameObject.SetActive(true);transform.SetAsLastSibling();Render();}
        public void Saved(CustomizationAcquisitionPlan value,CustomizationProgress state)
        {lastReceived=selected;plan=value.Copy();progress=state.Copy();pending=false;selected=null;message="ホゾンしました";Render();}
        public void Failed(bool saving){pending=saving;message=saving?"ホゾンをサイシコウしてください":"ウケトリできません。モドってカクニンしてください";Render();}
        public void Close(){gameObject.SetActive(false);plan=null;progress=null;catalog=null;}
        private void OnDisable(){menu?.Suspend();}private void OnDestroy(){menu?.Dispose();}
        private void LateUpdate(){CustomizationMenuElements.FitReference(transform,content,sizes);menu?.Tick();}
        private Text Label(string name,string value,float x,float y,float width,float height,int size=26,bool white=false,TextAnchor align=TextAnchor.MiddleLeft)
        {var t=CustomizationMenuElements.Label(content.transform,font,name,Olca.Presentation.CustomScreen.CustomDisplay.Katakana(value),x,y,width,height,size);t.color=white?Color.white:UiLook.Ink;t.alignment=align;sizes[t]=size;return t;}
        private Button Button(string name,string text,float x,float y,float width,Action action,bool enabled)
        {var b=CustomizationMenuElements.Button(content.transform,name,x,y,width,80);b.interactable=enabled;b.onClick.AddListener(()=>{if(this!=null&&gameObject.activeInHierarchy&&b!=null&&b.IsActive()&&b.IsInteractable())action();});var t=Label(name+"Label",text,0,0,width-24,72,28,false,TextAnchor.MiddleCenter);t.transform.SetParent(b.transform,false);t.rectTransform.anchoredPosition=Vector2.zero;UiLook.Button(b);return b;}
        private string Contents(CustomizationAcquisitionCandidate candidate,bool received,string idle)
        {
            if(candidate==null)return idle;
            var lines=new List<string>{candidate.DisplayName,"",received?"ウケトリズみ / ホゾンズみ":"ウケトリマエ / ミジッコウ"};
            if(candidate.Accessories.Length>0){lines.Add("ソウショクヒン");lines.AddRange(candidate.Accessories.Select(v=>"  "+v));}
            if(candidate.Parts.Length>0){lines.Add("パーツ");foreach(var p in candidate.Parts)lines.Add("  "+p.Id+" × "+p.Count+" / ショジ "+(progress.OwnedParts.SingleOrDefault(v=>v.Id==p.Id)?.Count??0));}
            if(candidate.UnlockItems.Length>0){lines.Add("マスカイホウアイテム");foreach(var p in candidate.UnlockItems)lines.Add("  "+p.Id+" × "+p.Count+" / ショジ "+(progress.OwnedUnlockItems.SingleOrDefault(v=>v.Id==p.Id)?.Count??0));}
            return string.Join("\n",lines);
        }
        private void Render()
        {
            menu.Suspend();sizes.Clear();content=CustomizationMenuElements.ReplaceContent(transform,content,"AcquisitionContent");CustomizationMenuElements.Backdrop(GetComponent<Image>());
            CustomizationMenuElements.Face(content.transform,"AcquisitionHeader",0,460,1800,110,true);CustomizationMenuElements.Face(content.transform,"AcquisitionCandidates",-620,20,680,720);CustomizationMenuElements.Face(content.transform,"AcquisitionDetails",410,20,1040,720,true);CustomizationMenuElements.Face(content.transform,"AcquisitionFooter",0,-490,1800,100,true);
            Label("AcquisitionTitle","ソウビのウケトリ",-600,465,540,60,36,true);Label("AcquisitionNotice","ナイヨウをカクニンしてホゾン / ソウビはヘンコウしません",330,465,1120,60,24,true);
            var availability=plan==null?null:new CustomizationAcquisitionAvailability(plan,progress);int received=availability?.ReceivedCount??0;
            Label("AcquisitionCount",plan==null?"ホゾンカンリョウをまっています":$"ウケトリ {received} / {plan.MaximumClaims}",-620,345,610,60,28);
            int count=plan?.Candidates.Length??0,pages=Math.Max(1,(count+3)/4);page=Math.Min(page,pages-1);
            bool limitReached=plan!=null&&received>=plan.MaximumClaims,receivable=plan!=null&&plan.Candidates.Any(v=>availability.ForCandidate(v.RequestId).CanReceive);
            string idle=pending||plan==null?"ホゾンのカンリョウをまっています":count==0?"ウケトリコウホはありません。\n\nモドルでトじてください。":limitReached?"このニュウシュモトのウケトリはカンリョウしています。\n\nモドルでトじてください。":!receivable?"ウケトレるコウホはありません。\n\nショジズみ・ウケトリズみのジョウタイをカクニンしてください。":"コウホをエラぶとナイヨウをカクニンできます。\n\nセンタクだけではウケトリません。";
            if(plan!=null)for(int i=0;i<4&&page*4+i<count;i++){
                var c=plan.Candidates[page*4+i];var status=availability.ForCandidate(c.RequestId);string badge=status.IsReceived?"ウケトリズみ":status.HasOwnedAccessory?"ショジズみ":status.LimitReached?"ジョウゲン":selected==c.RequestId?"センタク":"コウホ";float y=255-i*100;
                var b=Button("AcquisitionCandidate_"+c.RequestId,"",-620,y,620,()=>{selected=c.RequestId;Render();},!pending&&status.CanReceive);
                var t=Label(b.name+"Name",CustomizationMenuElements.ShortName(c.DisplayName,14),0,0,400,64,25);t.transform.SetParent(b.transform,false);t.rectTransform.anchoredPosition=new Vector2(-80,0);var state=Label(b.name+"State",badge,0,0,135,64,22,false,TextAnchor.MiddleCenter);state.transform.SetParent(b.transform,false);state.rectTransform.anchoredPosition=new Vector2(225,0);if(selected==c.RequestId)CustomizationMenuElements.SelectionMarker(b.transform,-300,60);
            }
            if(count==0)Label("NoAcquisitionCandidates",plan==null?"ホゾンのカンリョウをまっています":"ウケトリコウホはありません",-620,100,580,150,28);
            Button("AcquisitionPrevious","マエ",-790,-190,220,()=>{page--;Render();},!pending&&page>0);Button("AcquisitionNext","ツギ",-450,-190,220,()=>{page++;Render();},!pending&&page+1<pages);Label("AcquisitionPage",$"{page+1}/{pages}",-620,-190,90,50,23,false,TextAnchor.MiddleCenter);
            var candidate=plan?.Candidates.FirstOrDefault(v=>v.RequestId==(selected??lastReceived));bool isReceived=candidate!=null&&availability.ForCandidate(candidate.RequestId).IsReceived;
            string previewTitle=pending?"ホゾンミカンリョウ":isReceived?"ウケトリズみ":candidate!=null?"ウケトリナイヨウ / ミジッコウ":plan==null?"ホゾンカンリョウまち":count==0?"ウケトリコウホなし":limitReached?"ウケトリカンリョウ":!receivable?"ウケトリコウホなし":"ウケトリナイヨウ / ミジッコウ";
            Label("AcquisitionPreviewTitle",previewTitle,410,345,960,60,29,true);
            var shapes=new List<(string kind,string id,int count,IReadOnlyList<Olca.Core.Domain.Vector2Int> cells)>();if(candidate!=null&&catalog!=null&&catalog.BoardId==plan.BoardId){
                foreach(var p in candidate.Parts)if(catalog.Parts.TryGetValue(p.Id,out var shape))shapes.Add(("パーツ",p.Id,p.Count,shape.Cells));
                foreach(var p in candidate.UnlockItems)if(catalog.UnlockItems.TryGetValue(p.Id,out var cells))shapes.Add(("マス",p.Id,p.Count,cells));
            }
            int icons=Math.Min(4,shapes.Count);
            for(int i=0;i<icons;i++){var shape=shapes[i];float x=410+(i-(icons-1)/2f)*185;Label("AcquisitionShapeKind_"+i,shape.kind,x,292,170,26,20,true,TextAnchor.MiddleCenter);CustomizationMenuElements.Shape(content.transform,"AcquisitionShape_"+i,shape.cells,x,245,60);Label("AcquisitionShapeName_"+i,CustomizationMenuElements.ShortName(shape.id,8),x,195,170,26,20,true,TextAnchor.MiddleCenter);Label("AcquisitionShapeCount_"+i,"× "+shape.count,x,162,150,26,22,true,TextAnchor.MiddleCenter);}
            if(shapes.Count>icons)Label("AcquisitionMoreShapes","ほか "+(shapes.Count-icons)+" シュルイ",835,240,150,90,22,true,TextAnchor.MiddleCenter);
            var body=CustomizationMenuElements.ScrollText(content.transform,font,"AcquisitionPreviewInfo",Olca.Presentation.CustomScreen.CustomDisplay.Katakana(Contents(candidate,isReceived,idle)),410,icons>0?-40:50,960,icons>0?330:500,26);sizes[body]=26;
            string footer=message;if(!pending&&message==ChoiceGuide&&!receivable)footer=idle.Split('\n')[0];
            Label("AcquisitionMessage",footer,0,-405,1700,60,26,true,TextAnchor.MiddleCenter);
            Button(pending?"RetryCustomizationAcquisition":"ClaimCustomizationAcquisition",pending?"ホゾンをサイシコウ":"ウケトってホゾン",-420,-485,700,()=>{if(pending)retry();else claim(selected);},pending||selected!=null);Button("CancelCustomizationAcquisition","モドル",420,-485,700,close,!pending);
            CustomizationMenuElements.FitReference(transform,content,sizes);menu.Bind(transform,pending?"RetryCustomizationAcquisition":selected!=null?"ClaimCustomizationAcquisition":"CancelCustomizationAcquisition",()=>{if(!pending)close();});
        }
    }
}
