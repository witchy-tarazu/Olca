using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Customization;
namespace Olca.Presentation.Dungeon
{
    /// <summary>保存済み候補だけを提示する。取消は受領しない。保存再試行は同一要求を使う。</summary>
    public sealed class CustomizationAcquisitionView:MonoBehaviour
    {
        private Font font;private Action<string> claim;private Action close,retry;
        private Olca.Unity.MenuPadScope menu;private GameObject content;
        private CustomizationAcquisitionPlan plan;private CustomizationProgress progress;
        private string selected;private int page;private bool pending;private string message;
        public void Initialize(Font value,Action<string> receive,Action cancel,Action retrySave)
        {
            font=value;claim=receive;close=cancel;retry=retrySave;
            var r=(RectTransform)transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            UiLook.Face(gameObject.AddComponent<Image>());menu=new Olca.Unity.MenuPadScope(()=>gameObject.activeInHierarchy&&Olca.Unity.MenuPadScope.IsTopmost(transform));gameObject.SetActive(false);
        }
        public void Open(CustomizationAcquisitionPlan value,CustomizationProgress state,bool saving=false)
        {
            plan=value?.Copy();progress=state?.Copy();selected=null;page=0;pending=saving;message=saving?"ホゾンをサイシコウしてください":"コウホをエラんでカクテイしてください";
            gameObject.SetActive(true);transform.SetAsLastSibling();Render();
        }
        public void Saved(CustomizationAcquisitionPlan value,CustomizationProgress state)
        {plan=value.Copy();progress=state.Copy();pending=false;selected=null;message="ホゾンしました";Render();}
        public void Failed(bool saving){pending=saving;message=saving?"ホゾンをサイシコウしてください":"ウケトリできません。モドってカクニンしてください";Render();}
        public void Close(){gameObject.SetActive(false);plan=null;progress=null;}
        private void OnDisable(){menu?.Suspend();}
        private void OnDestroy(){menu?.Dispose();}
        private void LateUpdate(){menu?.Tick();}
        private Text Label(string name,string text,float x,float y,float width,float height,int size=28)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(content.transform,false);
            var t=go.GetComponent<Text>();t.font=font;t.fontSize=size;t.alignment=TextAnchor.MiddleCenter;t.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(text);UiLook.Label(t);t.raycastTarget=false;
            t.rectTransform.anchorMin=t.rectTransform.anchorMax=new Vector2(.5f,.5f);t.rectTransform.anchoredPosition=new Vector2(x,y);t.rectTransform.sizeDelta=new Vector2(width,height);return t;
        }
        private void Button(string name,string text,float x,float y,float width,Action action,bool enabled)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(content.transform,false);
            var r=(RectTransform)go.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(width,80);
            var b=go.GetComponent<Button>();b.navigation=new Navigation{mode=Navigation.Mode.None};b.interactable=enabled;UiLook.Button(b);
            b.onClick.AddListener(()=>{if(this!=null&&gameObject.activeInHierarchy&&b!=null&&b.IsActive()&&b.IsInteractable())action();});
            var t=Label(name+"Label",text,0,0,width-24,72);t.transform.SetParent(go.transform,false);t.rectTransform.anchoredPosition=Vector2.zero;
        }
        private void Render()
        {
            menu.Suspend();if(content!=null){content.SetActive(false);if(Application.isPlaying)Destroy(content);else DestroyImmediate(content);}
            content=new GameObject("AcquisitionContent",typeof(RectTransform));content.transform.SetParent(transform,false);var r=(RectTransform)content.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
            Label("AcquisitionTitle","ソウビのウケトリ",0,430,1500,80,36);
            int received=plan?.Candidates.Count(v=>progress.Receipts.Any(t=>t.RequestId==v.RequestId))??0;
            Label("AcquisitionCount",plan==null?"ホゾンカンリョウをまっています":$"ウケトリ {received} / {plan.MaximumClaims}",0,335,1500,70);
            int pages=Math.Max(1,((plan?.Candidates.Length??0)+3)/4);page=Math.Min(page,pages-1);
            if(plan!=null)for(int i=0;i<4&&page*4+i<plan.Candidates.Length;i++){
                var c=plan.Candidates[page*4+i];bool done=progress.Receipts.Any(v=>v.RequestId==c.RequestId);
                bool owned=c.Accessories.Any(id=>progress.OwnedAccessoryIds.Contains(id));
                Button("AcquisitionCandidate_"+c.RequestId,c.DisplayName+(done?" / ウケトリズみ":owned?" / ショジズみ":selected==c.RequestId?" / センタク":""),0,220-i*110,1500,()=>{selected=c.RequestId;Render();},!pending&&!done&&!owned&&received<plan.MaximumClaims);
            }
            Button("AcquisitionPrevious","マエ",-350,-245,500,()=>{page--;Render();},!pending&&page>0);
            Button("AcquisitionNext","ツギ",350,-245,500,()=>{page++;Render();},!pending&&page+1<pages);
            Label("AcquisitionMessage",message,0,-340,1700,80,26);
            Button(pending?"RetryCustomizationAcquisition":"ClaimCustomizationAcquisition",pending?"ホゾンをサイシコウ":"ウケトってホゾン",-420,-455,700,()=>{if(pending)retry();else claim(selected);},pending||selected!=null);
            Button("CancelCustomizationAcquisition","モドル",420,-455,700,close,!pending);
            menu.Bind(transform,pending?"RetryCustomizationAcquisition":selected!=null?"ClaimCustomizationAcquisition":"CancelCustomizationAcquisition",()=>{if(!pending)close();});
        }
    }
}
