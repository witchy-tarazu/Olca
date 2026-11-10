using System.Collections.Generic;
using UnityEngine;

namespace Olca.Presentation.Effects
{
    public enum DefensiveFieldKind { Barrier, Aura }

    /// <summary>中央が透明な独立防御レイヤー。状態の寿命や判定・キャラ画像は所有しない。</summary>
    public sealed class DefensiveFieldVisual : System.IDisposable
    {
        private Olca.Unity.GeneratedSpriteAssets.Lease generatedAssets;
        [System.Serializable] private sealed class ContentBounds { public string name; public int width, height, x, y, w, h; }
        [System.Serializable] private sealed class BoundsDocument { public ContentBounds[] entries; }
        private static BoundsDocument contentBounds;
        private static bool TryContentBounds(Sprite sprite,out Bounds bounds)
        {
            bounds=new Bounds();if(sprite==null)return false;
            if(contentBounds==null){var json=Resources.Load<TextAsset>("OlcaDefensiveFields/actor-bounds");contentBounds=json==null?new BoundsDocument():JsonUtility.FromJson<BoundsDocument>(json.text);}
            foreach(var entry in contentBounds.entries??System.Array.Empty<ContentBounds>()){
                if(entry.name!=sprite.name||entry.width!=sprite.rect.width||entry.height!=sprite.rect.height)continue;
                bounds=new Bounds(new Vector3((entry.x+entry.w*.5f-sprite.pivot.x)/sprite.pixelsPerUnit,(entry.y+entry.h*.5f-sprite.pivot.y)/sprite.pixelsPerUnit,0),new Vector3(entry.w/sprite.pixelsPerUnit,entry.h/sprite.pixelsPerUnit,0));return true;
            }return false;
        }
        /// <summary>UIにも同じ不透明領域の校正を使い、透明余白の上へHUDを浮かせない。</summary>
        public static Bounds GetLocalBodyBounds(Sprite sprite)=>TryContentBounds(sprite,out var bounds)?bounds:sprite==null?new Bounds():sprite.bounds;
        /// <summary>既存敵PNGの不透明領域から生成した描画校正。未知の絵は既存boundsを使う。</summary>
        public static Bounds GetBodyBounds(SpriteRenderer body)
        {
            if(body==null||body.sprite==null)return new Bounds();if(!TryContentBounds(body.sprite,out var bounds))return body.bounds;
            var center=bounds.center;if(body.flipX)center.x=-center.x;if(body.flipY)center.y=-center.y;
            var x=body.transform.TransformVector(new Vector3(bounds.size.x,0,0));var y=body.transform.TransformVector(new Vector3(0,bounds.size.y,0));
            return new Bounds(body.transform.TransformPoint(center),new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x),Mathf.Abs(x.y)+Mathf.Abs(y.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)));
        }
        private SpriteRenderer field;
        private bool disposed;
        public DefensiveFieldKind? VisibleKind { get; private set; }
        public SpriteRenderer Renderer => field;
        public static Sprite GetSprite(DefensiveFieldKind kind,Olca.Unity.GeneratedSpriteAssets.Lease assets=null)
        {
            if (!System.Enum.IsDefined(typeof(DefensiveFieldKind), kind)) return null;
            return Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("defensive-field/"+kind,owned=>{
                var bytes=Resources.Load<TextAsset>("OlcaDefensiveFields/"+kind.ToString().ToLowerInvariant());if(bytes==null)return null;
                var texture=owned.CreateTexture(2,2);if(!texture.LoadImage(bytes.bytes,true))return null;
                texture.name="defensive-field/"+kind;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;texture.hideFlags=HideFlags.HideAndDontSave;
                var sprite=owned.CreateSprite(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));sprite.name=texture.name;sprite.hideFlags=HideFlags.HideAndDontSave;return sprite;
            },assets);
        }
        public void Refresh(SpriteRenderer body, bool barrier, bool aura)
        {
            if (disposed || body == null || !body.gameObject.activeInHierarchy || !body.enabled || body.forceRenderingOff || body.sprite == null || barrier == aura) { Clear(); return; }
            var kind = barrier ? DefensiveFieldKind.Barrier : DefensiveFieldKind.Aura;
            if(field==null){generatedAssets?.Dispose();generatedAssets=null;}
            var sprite = GetSprite(kind,generatedAssets??(generatedAssets=Olca.Unity.GeneratedSpriteAssets.Acquire()));
            if (sprite == null) { Clear();if(field==null){generatedAssets?.Dispose();generatedAssets=null;} return; }
            if (field == null) {
                var obj = new GameObject("DefensiveFieldVisual", typeof(SpriteRenderer)); obj.transform.SetParent(body.transform, false);
                field = obj.GetComponent<SpriteRenderer>();
            } else if (field.transform.parent != body.transform) field.transform.SetParent(body.transform, false);
            generatedAssets.BindNativeOwner(field);
            field.sprite = sprite; field.color = new Color(1, 1, 1, .55f);
            field.sortingLayerID = body.sortingLayerID;
            field.sortingOrder = body.sortingOrder == int.MinValue ? int.MinValue : body.sortingOrder - 1;
            var bounds = GetBodyBounds(body);
            float height = bounds.size.y * 1.15f, width = Mathf.Max(bounds.size.x * 1.15f, bounds.size.y * .62f);
            var parentScale = body.transform.lossyScale;
            if (height <= 0 || width <= 0 || Mathf.Abs(parentScale.x) < .00001f || Mathf.Abs(parentScale.y) < .00001f) { Clear(); return; }
            // 本体を動かさず外周だけを寄せ、足場と予兆の下端を覆わない。
            field.transform.position = bounds.center + Vector3.up * bounds.size.y * .1f;
            field.transform.localScale = new Vector3(width / sprite.bounds.size.x / Mathf.Abs(parentScale.x), height / sprite.bounds.size.y / Mathf.Abs(parentScale.y), 1);
            field.forceRenderingOff = false; field.enabled = true; VisibleKind = kind;
        }
        public void Clear() { if (field != null) field.enabled = false; VisibleKind = null; }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; Clear(); if (field != null) {field.sprite=null;DestroyOwned(field.gameObject);} field = null;generatedAssets?.Dispose();generatedAssets=null;
        }
        private static void DestroyOwned(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
