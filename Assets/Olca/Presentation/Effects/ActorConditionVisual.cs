using System.Collections.Generic;
using UnityEngine;
namespace Olca.Presentation.Effects
{
    public enum ActorConditionKind { Guard, Paralyze }
    /// <summary>確定済みのガード/麻痺だけを描く独立レイヤー。時計や本体姿勢を所有しない。</summary>
    public sealed class ActorConditionVisual : System.IDisposable
    {
        private Olca.Unity.GeneratedSpriteAssets.Lease generatedAssets;
        private readonly ActorConditionKind kind;
        private SpriteRenderer renderer;
        private bool disposed;
        public bool IsVisible => renderer != null && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy;
        public SpriteRenderer Renderer => renderer;
        public ActorConditionVisual(ActorConditionKind kind)
        {
            if (!System.Enum.IsDefined(typeof(ActorConditionKind), kind)) throw new System.ArgumentOutOfRangeException(nameof(kind));
            this.kind = kind;
        }
        public static Sprite GetSprite(ActorConditionKind kind,Olca.Unity.GeneratedSpriteAssets.Lease assets=null)
        {
            if (!System.Enum.IsDefined(typeof(ActorConditionKind), kind)) return null;
            return Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("actor-condition/"+kind,owned=>{
                var bytes=Resources.Load<TextAsset>("OlcaActorConditions/"+kind.ToString().ToLowerInvariant());if(bytes==null)return null;
                var texture=owned.CreateTexture(2,2);if(!texture.LoadImage(bytes.bytes,true))return null;
                texture.name="actor-condition/"+kind;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;texture.hideFlags=HideFlags.HideAndDontSave;
                var sprite=owned.CreateSprite(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));sprite.name=texture.name;sprite.hideFlags=HideFlags.HideAndDontSave;return sprite;
            },assets);
        }
        public void Refresh(SpriteRenderer body, bool active)
        {
            if (disposed || !active || body == null || !body.enabled || !body.gameObject.activeInHierarchy || body.forceRenderingOff || body.sprite == null) { Clear(); return; }
            if(renderer==null){generatedAssets?.Dispose();generatedAssets=null;}
            var sprite = GetSprite(kind,generatedAssets??(generatedAssets=Olca.Unity.GeneratedSpriteAssets.Acquire())); if (sprite == null) { Clear();ReleaseUnusedBorrow(); return; }
            var bounds = DefensiveFieldVisual.GetBodyBounds(body); var scale = body.transform.lossyScale;
            if (bounds.size.y <= 0 || Mathf.Abs(scale.x) < .00001f || Mathf.Abs(scale.y) < .00001f) { Clear();ReleaseUnusedBorrow(); return; }
            if (renderer == null) { var obj = new GameObject("ActorCondition_" + kind, typeof(SpriteRenderer)); obj.transform.SetParent(body.transform, false); renderer = obj.GetComponent<SpriteRenderer>(); }
            else if (renderer.transform.parent != body.transform) renderer.transform.SetParent(body.transform, false);
            renderer.gameObject.layer = body.gameObject.layer; renderer.maskInteraction = body.maskInteraction;
            renderer.sprite = sprite; renderer.color = new Color(1, 1, 1, kind == ActorConditionKind.Guard ? .42f : .65f);
            renderer.sortingLayerID = body.sortingLayerID;
            renderer.sortingOrder = Mathf.Clamp(body.sortingOrder + (kind == ActorConditionKind.Guard ? -2 : 1), -32768, 32767);
            // 隣の本体や予兆角枠へ横に張り出さず、状態の所有者の周囲へまとめる。
            float width = Mathf.Max(bounds.size.x * (kind == ActorConditionKind.Guard ? 1.22f : 1.25f), bounds.size.y * (kind == ActorConditionKind.Guard ? .6f : .62f));
            renderer.transform.position = bounds.center;
            renderer.transform.localScale = new Vector3(width / sprite.bounds.size.x / Mathf.Abs(scale.x), bounds.size.y * 1.35f / sprite.bounds.size.y / Mathf.Abs(scale.y), 1);
            generatedAssets.BindNativeOwner(renderer);
            renderer.forceRenderingOff = false; renderer.enabled = true;
        }
        private void ReleaseUnusedBorrow(){if(renderer==null){generatedAssets?.Dispose();generatedAssets=null;}}
        public void Clear() { if (renderer != null) renderer.enabled = false; }
        public void Dispose() { if (disposed) return; disposed = true; Clear(); if (renderer != null) {renderer.sprite=null;DestroyOwned(renderer.gameObject);} renderer = null;generatedAssets?.Dispose();generatedAssets=null; }
        private static void DestroyOwned(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
