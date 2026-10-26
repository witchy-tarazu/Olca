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
            var label=go.GetComponent<Text>();label.font=font;label.fontSize=size;label.alignment=TextAnchor.MiddleCenter;label.text=text;UiLook.Label(label);
            Position(label.rectTransform,x,y,width,height);return label;
        }
        internal static Button Button(Transform parent,string name,float x,float y,float width,float height)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(parent,false);
            Position((RectTransform)go.transform,x,y,width,height);
            var button=go.GetComponent<Button>();button.navigation=new Navigation{mode=Navigation.Mode.None};return button;
        }
        private static void Position(RectTransform rect,float x,float y,float width,float height)
        {rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);}
    }
}
