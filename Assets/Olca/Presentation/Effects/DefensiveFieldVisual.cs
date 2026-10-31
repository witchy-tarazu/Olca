using System.Collections.Generic;
using UnityEngine;

namespace Olca.Presentation.Effects
{
    public enum DefensiveFieldKind { Barrier, Aura }

    /// <summary>中央が透明な独立防御レイヤー。状態の寿命や判定・キャラ画像は所有しない。</summary>
    public sealed class DefensiveFieldVisual : System.IDisposable
    {
        private static readonly Dictionary<DefensiveFieldKind, Sprite> sprites = new Dictionary<DefensiveFieldKind, Sprite>();
        [System.Serializable] private sealed class ContentBounds { public string name; public int width, height, x, y, w, h; }
        [System.Serializable] private sealed class BoundsDocument { public ContentBounds[] entries; }
        private static BoundsDocument contentBounds;
        /// <summary>既存敵PNGの不透明領域から生成した描画校正。未知の絵は既存boundsを使う。</summary>
        public static Bounds GetBodyBounds(SpriteRenderer body)
        {
            if (body == null || body.sprite == null) return new Bounds();
            if (contentBounds == null) {
                var json = Resources.Load<TextAsset>("OlcaDefensiveFields/actor-bounds");
                contentBounds = json == null ? new BoundsDocument() : JsonUtility.FromJson<BoundsDocument>(json.text);
            }
            var sprite = body.sprite;
            foreach (var entry in contentBounds.entries ?? System.Array.Empty<ContentBounds>()) {
                if (entry.name != sprite.name || entry.width != sprite.rect.width || entry.height != sprite.rect.height) continue;
                var center = new Vector3((entry.x + entry.w * .5f - sprite.pivot.x) / sprite.pixelsPerUnit,
                    (entry.y + entry.h * .5f - sprite.pivot.y) / sprite.pixelsPerUnit, 0);
                if (body.flipX) center.x = -center.x; if (body.flipY) center.y = -center.y;
                var x = body.transform.TransformVector(new Vector3(entry.w / sprite.pixelsPerUnit, 0, 0));
                var y = body.transform.TransformVector(new Vector3(0, entry.h / sprite.pixelsPerUnit, 0));
                return new Bounds(body.transform.TransformPoint(center), new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x), Mathf.Abs(x.y)+Mathf.Abs(y.y), Mathf.Abs(x.z)+Mathf.Abs(y.z)));
            }
            return body.bounds;
        }
        private SpriteRenderer field;
        private bool disposed;
        public DefensiveFieldKind? VisibleKind { get; private set; }
        public SpriteRenderer Renderer => field;
        public static Sprite GetSprite(DefensiveFieldKind kind)
        {
            if (!System.Enum.IsDefined(typeof(DefensiveFieldKind), kind)) return null;
            if (sprites.TryGetValue(kind, out var sprite) && sprite != null) return sprite;
            var bytes = Resources.Load<TextAsset>("OlcaDefensiveFields/" + kind.ToString().ToLowerInvariant());
            if (bytes == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes.bytes, true)) { DestroyOwned(texture); return null; }
            texture.name = "defensive-field/" + kind; texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp; texture.hideFlags = HideFlags.HideAndDontSave;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name; sprite.hideFlags = HideFlags.HideAndDontSave; sprites[kind] = sprite;
            return sprite;
        }
        public void Refresh(SpriteRenderer body, bool barrier, bool aura)
        {
            if (disposed || body == null || !body.gameObject.activeInHierarchy || !body.enabled || body.forceRenderingOff || body.sprite == null || barrier == aura) { Clear(); return; }
            var kind = barrier ? DefensiveFieldKind.Barrier : DefensiveFieldKind.Aura;
            var sprite = GetSprite(kind);
            if (sprite == null) { Clear(); return; }
            if (field == null) {
                var obj = new GameObject("DefensiveFieldVisual", typeof(SpriteRenderer)); obj.transform.SetParent(body.transform, false);
                field = obj.GetComponent<SpriteRenderer>();
            } else if (field.transform.parent != body.transform) field.transform.SetParent(body.transform, false);
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
            disposed = true; Clear(); if (field != null) DestroyOwned(field.gameObject); field = null;
        }
        private static void DestroyOwned(Object value) { if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
