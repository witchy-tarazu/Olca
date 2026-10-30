using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Olca.Presentation.Dungeon
{
    /// <summary>装備各画面のUI部品生成だけを共有する。入力寿命・文言変換・テーマ適用順は各Viewが所有する。</summary>
    internal static class CustomizationMenuElements
    {
        internal static GameObject ReplaceContent(Transform parent,GameObject previous,string name)
        {
            if(previous!=null){previous.SetActive(false);if(Application.isPlaying)Object.Destroy(previous);else Object.DestroyImmediate(previous);}
            var root=new GameObject(name,typeof(RectTransform));root.transform.SetParent(parent,false);
            var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            return root;
        }
        internal static Text Label(Transform parent,Font font,string name,string text,float x,float y,float width,float height,int size)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);
            var label=go.GetComponent<Text>();label.font=font;label.fontSize=size;label.supportRichText=false;label.alignment=TextAnchor.MiddleCenter;label.text=text;UiLook.Label(label);
            Position(label.rectTransform,x,y,width,height);return label;
        }
        internal static Button Button(Transform parent,string name,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(parent,false);
            Position((RectTransform)go.transform,x,y,width,height);
            var button=go.GetComponent<Button>();button.navigation=new Navigation{mode=Navigation.Mode.None};return button;
        }
        internal static void Backdrop(Image image){image.sprite=null;image.type=Image.Type.Simple;image.color=new Color(.025f,.075f,.10f,1);}
        internal static void FitReference(Transform parent,GameObject content,IReadOnlyDictionary<Text,int> sizes)
        {if(content==null)return;var available=((RectTransform)parent).rect.size;float scale=Mathf.Max(.01f,Mathf.Min(1,available.x/1920,available.y/1080));var r=(RectTransform)content.transform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=Vector2.zero;r.sizeDelta=new Vector2(1920,1080);r.localScale=Vector3.one*scale;foreach(var pair in sizes)if(pair.Key!=null)pair.Key.fontSize=pair.Value<34?Mathf.RoundToInt(pair.Value*(scale<.9f?1.12f:1)):pair.Value;}
        internal static Image Face(Transform parent,string name,float x,float y,float width,float height,bool dark=false)
        {var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);var image=go.GetComponent<Image>();image.sprite=Resources.Load<Sprite>(dark?"OlcaUI/description":"OlcaUI/window");image.type=Image.Type.Sliced;image.color=Color.white;image.raycastTarget=false;Position((RectTransform)go.transform,x,y,width,height);return image;}
        internal static string ShortName(string value,int maximum)
        {string display=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(value);var elements=System.Globalization.StringInfo.ParseCombiningCharacters(display);return elements.Length>maximum?display.Substring(0,elements[maximum])+"…":display;}
        internal static void SelectionMarker(Transform parent,float x,float height)
        {var go=new GameObject("SelectionMarker",typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);go.GetComponent<Image>().color=new Color(.07f,.42f,.53f,1);go.GetComponent<Image>().raycastTarget=false;Position((RectTransform)go.transform,x,0,5,height);}
        internal static void Shape(Transform parent,string name,IReadOnlyList<Olca.Core.Domain.Vector2Int> cells,float x,float y,float size)
        {if(cells==null||cells.Count==0)return;int minX=cells.Min(v=>v.X),minY=cells.Min(v=>v.Y);float width=(float)((long)cells.Max(v=>v.X)-minX+1),height=(float)((long)cells.Max(v=>v.Y)-minY+1),unit=Mathf.Min(size/width,size/height);var root=new GameObject(name,typeof(RectTransform));root.transform.SetParent(parent,false);Position((RectTransform)root.transform,x,y,size,size);foreach(var c in cells){var go=new GameObject("ShapeCell",typeof(RectTransform),typeof(Image));go.transform.SetParent(root.transform,false);go.GetComponent<Image>().color=new Color(.20f,.58f,.69f,1);go.GetComponent<Image>().raycastTarget=false;Position((RectTransform)go.transform,((long)c.X-minX+.5f-width/2)*unit,(height/2-((long)c.Y-minY)-.5f)*unit,Mathf.Max(.2f,unit-2),Mathf.Max(.2f,unit-2));}}
        internal static Text ScrollText(Transform parent,Font font,string name,string value,float x,float y,float width,float height,int size)
        {
            var root=Face(parent,name,x,y,width,height,true);root.raycastTarget=true;var scroll=root.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=35;
            var viewport=new GameObject("Viewport",typeof(RectTransform),typeof(Image),typeof(Mask));viewport.transform.SetParent(root.transform,false);viewport.GetComponent<Mask>().showMaskGraphic=false;var vr=(RectTransform)viewport.transform;vr.anchorMin=Vector2.zero;vr.anchorMax=Vector2.one;vr.offsetMin=new Vector2(28,24);vr.offsetMax=new Vector2(-40,-24);
            var body=new GameObject(name+"Text",typeof(RectTransform),typeof(Text),typeof(ContentSizeFitter));body.transform.SetParent(viewport.transform,false);var text=body.GetComponent<Text>();text.font=font;text.fontSize=size;text.color=Color.white;text.text=value;text.supportRichText=false;text.alignment=TextAnchor.UpperLeft;text.raycastTarget=false;var br=(RectTransform)body.transform;br.anchorMin=new Vector2(0,1);br.anchorMax=Vector2.one;br.pivot=new Vector2(.5f,1);br.sizeDelta=Vector2.zero;body.GetComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;scroll.viewport=vr;scroll.content=br;
            var bar=new GameObject("Scrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar));bar.transform.SetParent(root.transform,false);var rr=(RectTransform)bar.transform;rr.anchorMin=new Vector2(1,0);rr.anchorMax=Vector2.one;rr.pivot=new Vector2(1,.5f);rr.offsetMin=new Vector2(-13,12);rr.offsetMax=new Vector2(-5,-12);bar.GetComponent<Image>().color=new Color(.5f,.8f,.9f,.15f);var handle=new GameObject("Handle",typeof(RectTransform),typeof(Image));handle.transform.SetParent(bar.transform,false);var hr=(RectTransform)handle.transform;hr.anchorMin=Vector2.zero;hr.anchorMax=Vector2.one;hr.offsetMin=hr.offsetMax=Vector2.zero;handle.GetComponent<Image>().color=new Color(.7f,.94f,1,.85f);var scrollbar=bar.GetComponent<Scrollbar>();scrollbar.handleRect=hr;scrollbar.targetGraphic=handle.GetComponent<Image>();scrollbar.direction=Scrollbar.Direction.BottomToTop;scrollbar.navigation=new Navigation{mode=Navigation.Mode.None};scroll.verticalScrollbar=scrollbar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;return text;
        }
        private static void Position(RectTransform rect,float x,float y,float width,float height)
        {rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);}
    }
}
