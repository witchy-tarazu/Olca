using System.Collections.Generic;
using UnityEngine;

namespace Olca.Presentation.Field
{
    /// <summary>仮の共用物体Sprite。カード性能・本体ポーズ・フレーム配分を所有しない。</summary>
    public static class SharedAttackPropCatalog
    {
        public const string Sword = "shared/sword";
        public const string ThrowingKnife = "shared/throwing-knife";
        public const string ShotBolt = "shared/shot-bolt";
        public const string Bomb = "shared/bomb";
        public const string Blast = "shared/blast";
        public const float BlastVisibleWidth = 54f / 100f, BlastVisibleHeight = 8f / 100f;
        public const float BombVisibleHeight = 38f / 100f;

        public static bool TryGetSprite(string key, out Sprite sprite, Olca.Unity.GeneratedSpriteAssets.Lease assets=null)
        {
            sprite = null;
            string resource;
            Vector2 pivot;
            switch (key)
            {
                case Sword: resource = "shared_sword"; pivot = new Vector2(90f / 256f, .5f); break;
                case ThrowingKnife: resource = "shared_throwing_knife"; pivot = new Vector2(.5f, .5f); break;
                case ShotBolt: resource = "shared_shot_bolt"; pivot = new Vector2(.5f, .5f); break;
                case Bomb: resource = "shared_bomb"; pivot = new Vector2(.5f, .5f); break;
                case Blast: resource = "shared_blast"; pivot = new Vector2(.5f, .5f); break;
                default: return false;
            }
            sprite=Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("prop/"+key,owned=>{
                var source=Resources.Load<TextAsset>("OlcaAttackProps/"+resource);
                if(source==null)return null;
                var texture=owned.CreateTexture(2,2);
                if(!texture.LoadImage(source.bytes,true))return null;
                texture.name=key;texture.hideFlags=HideFlags.HideAndDontSave;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                var result=owned.CreateSprite(texture,new Rect(0,0,texture.width,texture.height),pivot);
                result.name=key;result.hideFlags=HideFlags.HideAndDontSave;return result;
            },assets);
            return sprite!=null;
        }
    }
}
