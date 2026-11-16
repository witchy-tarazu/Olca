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
        // 探索の実着弾だけの試用明色。元の輪郭/alphaを変えず、予告とは別keyで所有する。
        public const string BlastImpact = "shared/blast-impact";
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
                case BlastImpact: resource = "shared_blast"; pivot = new Vector2(.5f, .5f); break;
                default: return false;
            }
            sprite=Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("prop/"+key,owned=>{
                var source=Resources.Load<TextAsset>("OlcaAttackProps/"+resource);
                if(source==null)return null;
                var texture=owned.CreateTexture(2,2);
                if(!texture.LoadImage(source.bytes,key!=BlastImpact))return null;
                if(key==BlastImpact){
                    var pixels=texture.GetPixels32();
                    for(int i=0;i<pixels.Length;i++){var c=pixels[i];c.r=(byte)((c.r+255)/2);c.g=(byte)((c.g+255)/2);c.b=(byte)((c.b+255)/2);pixels[i]=c;}
                    texture.SetPixels32(pixels);texture.Apply(false,true);
                }
                texture.name=key;texture.hideFlags=HideFlags.HideAndDontSave;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                var result=owned.CreateSprite(texture,new Rect(0,0,texture.width,texture.height),pivot);
                result.name=key;result.hideFlags=HideFlags.HideAndDontSave;return result;
            },assets);
            return sprite!=null;
        }
    }
}
