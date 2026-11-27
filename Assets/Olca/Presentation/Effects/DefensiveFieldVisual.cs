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

namespace Olca.Presentation.Effects
{
    /// <summary>試用カエンの識別レイヤー。衣装・能力・時計は変更せず本体後方だけに描く。</summary>
    public sealed class TrialFireKamuiVisual : System.IDisposable
    {
        public const float SizeWorld=2.6f,Opacity=.4f;
        public static bool IsActive(Olca.Core.Domain.PlayerCharacter player)=>player!=null&&!player.IsDead&&player.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id;
        public static Sprite LoadSprite()=>Resources.Load<Sprite>("OlcaCharacterMotion/trial_fire_aura");
        private SpriteRenderer field;private bool disposed;
        public SpriteRenderer Renderer=>field;
        public void Refresh(SpriteRenderer body,Olca.Core.Domain.PlayerCharacter player)
        {
            if(disposed||!IsActive(player)||body==null||body.sprite==null||!body.enabled||body.forceRenderingOff||!body.gameObject.activeInHierarchy){Clear();return;}
            var sprite=field!=null?field.sprite:LoadSprite();if(sprite==null){Clear();return;}
            var scale=body.transform.lossyScale;if(Mathf.Abs(scale.x)<.00001f||Mathf.Abs(scale.y)<.00001f){Clear();return;}
            if(field==null){var go=new GameObject("TrialFireKamuiVisual",typeof(SpriteRenderer));field=go.GetComponent<SpriteRenderer>();}
            if(field.transform.parent!=body.transform)field.transform.SetParent(body.transform,false);
            field.sprite=sprite;field.color=new Color(1,1,1,Opacity);field.sortingLayerID=body.sortingLayerID;
            field.sortingOrder=body.sortingOrder>int.MinValue+1?body.sortingOrder-2:int.MinValue;
            field.transform.localPosition=Vector3.zero;field.transform.localRotation=Quaternion.identity;
            field.transform.localScale=new Vector3(SizeWorld/(sprite.bounds.size.x*Mathf.Abs(scale.x)),SizeWorld/(sprite.bounds.size.y*Mathf.Abs(scale.y)),1);
            field.enabled=true;
        }
        public void Clear(){if(field!=null)field.enabled=false;}
        public void Dispose(){if(disposed)return;disposed=true;if(field==null)return;field.sprite=null;if(Application.isPlaying)Object.Destroy(field.gameObject);else Object.DestroyImmediate(field.gameObject);field=null;}
    }
}

namespace Olca.Presentation.Effects
{
    /// <summary>確定済みバリア吸収だけを示す外周の破片。判定・耐久・時計を所有しない。</summary>
    public sealed class BattleBarrierImpactVisual : System.IDisposable
    {
        public const int DurationFrames = 18;
        private Olca.Core.Logic.Battle.BattleSystem owner;
        private long started;
        private bool pending;
        private SpriteRenderer[] pieces;
        private GameObject container;
        private Olca.Unity.GeneratedSpriteAssets.Lease assets;
        public bool IsBroken { get; private set; }
        public bool IsVisible => pieces != null && pieces[0] != null && pieces[0].enabled;
        public void Notify(Olca.Core.Logic.Battle.BattleSystem battle)
        {
            if (battle == null || battle.IsGameOver) return;
            owner = battle; started = battle.LogicalFrame; pending = true;
        }
        public void Render(SpriteRenderer body, Olca.Core.Logic.Battle.BattleSystem battle)
        {
            if (owner == null || owner != battle || battle.IsGameOver || battle.Player.IsDead || body == null || body.sprite == null || !body.gameObject.activeInHierarchy) { Clear(); return; }
            // 通知時は同フレームの耐久集計中。描画時に確定した残耐久を読み取る。
            if (pending) { IsBroken = !battle.Player.HasBarrier; pending = false; }
            long age = battle.LogicalFrame - started;
            if (age < 0 || age >= DurationFrames) { Clear(); return; }
            // 移動の瞬間非表示は終了ではない。到着後に残る論理寿命だけ表示する。
            if (!body.enabled || body.forceRenderingOff) { HidePieces(); return; }
            if (pieces == null) {
                assets = Olca.Unity.GeneratedSpriteAssets.Acquire();
                var sprite = Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("barrier-impact449", owned => {
                    var texture = owned.CreateTexture(8, 16); var colors = new Color[128];
                    for (int y=0; y<16; y++) for (int x=0; x<8; x++)
                        if (Mathf.Abs(x-3.5f)/3.5f + Mathf.Abs(y-7.5f)/7.5f <= 1) colors[y*8+x] = Color.white;
                    texture.SetPixels(colors); texture.Apply(); texture.filterMode=FilterMode.Bilinear;
                    return owned.CreateSprite(texture,new Rect(0,0,8,16),new Vector2(.5f,.5f));
                }, assets);
                container = new GameObject("BarrierImpact449"); container.transform.SetParent(body.transform.parent,false);
                assets.BindNativeOwner(container);
                pieces = new SpriteRenderer[6];
                for(int n=0;n<pieces.Length;n++) { var go=new GameObject("BarrierImpact449_"+n); go.transform.SetParent(container.transform,false); pieces[n]=go.AddComponent<SpriteRenderer>(); pieces[n].sprite=sprite; }
            }
            var bounds=DefensiveFieldVisual.GetBodyBounds(body); float t=age/(float)DurationFrames;
            float radius=IsBroken ? .53f+t*.25f : .52f+Mathf.Sin(t*Mathf.PI)*.03f;
            for(int n=0;n<pieces.Length;n++) {
                float angle=(n*60+30)*Mathf.Deg2Rad; var piece=pieces[n];
                piece.transform.position=bounds.center+new Vector3(Mathf.Cos(angle)*bounds.size.y*radius*.65f,Mathf.Sin(angle)*bounds.size.y*radius, -.03f);
                piece.transform.rotation=Quaternion.Euler(0,0,n*60+30-90+(IsBroken?t*35:0));
                piece.transform.localScale=Vector3.one; var scale=piece.transform.lossyScale;
                piece.transform.localScale=new Vector3(bounds.size.y*.035f/piece.sprite.bounds.size.x/Mathf.Abs(scale.x),bounds.size.y*(IsBroken?.16f:.09f)/piece.sprite.bounds.size.y/Mathf.Abs(scale.y),1);
                piece.color=new Color(.88f,.68f,1f,(1-t)*.9f); piece.sortingLayerID=body.sortingLayerID; piece.sortingOrder=body.sortingOrder+1; piece.enabled=true;
            }
        }
        private void HidePieces() { if(pieces!=null)foreach(var piece in pieces)if(piece!=null)piece.enabled=false; }
        public void Clear() { owner=null;pending=false;HidePieces(); }
        public void Dispose() { Clear();if(pieces!=null)foreach(var piece in pieces)if(piece!=null){piece.sprite=null;if(Application.isPlaying)Object.Destroy(piece.gameObject);else Object.DestroyImmediate(piece.gameObject);}pieces=null;if(container!=null){if(Application.isPlaying)Object.Destroy(container);else Object.DestroyImmediate(container);}container=null;assets?.Dispose();assets=null; }
    }
}
