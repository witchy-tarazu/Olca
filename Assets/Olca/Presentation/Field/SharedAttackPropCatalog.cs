using System.Collections.Generic;
using UnityEngine;

namespace Olca.Presentation.Field
{
    /// <summary>通常盤面のボム着弾だけの自作6コマ。時計・範囲・命中は所有しない。</summary>
    public static class BattleBombLayers451
    {
        public static Sprite Frame(int frame,bool center,Olca.Unity.GeneratedSpriteAssets.Lease assets)
        {
            frame=Mathf.Clamp(frame,0,5);int age=frame;
            return Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("battle-bomb451/"+center+"/"+frame,owned=>{
                const int w=160,h=128;var pixels=new Color[w*h];float t=age/5f;
                void Ellipse(float cx,float cy,float rx,float ry,Color color){
                    for(int y=Mathf.Max(0,Mathf.FloorToInt(cy-ry));y<Mathf.Min(h,cy+ry+1);y++)for(int x=Mathf.Max(0,Mathf.FloorToInt(cx-rx));x<Mathf.Min(w,cx+rx+1);x++){
                        float d=Mathf.Pow((x-cx)/rx,2)+Mathf.Pow((y-cy)/ry,2);if(d>1)continue;
                        float a=color.a*Mathf.Clamp01((1-d)*9);var old=pixels[y*w+x];float alpha=a+old.a*(1-a);
                        pixels[y*w+x]=alpha>0?new Color((color.r*a+old.r*old.a*(1-a))/alpha,(color.g*a+old.g*old.a*(1-a))/alpha,(color.b*a+old.b*old.a*(1-a))/alpha,alpha):Color.clear;
                    }
                }
                // 暗い煙の輪郭→暖色の芯→細い地表の圧力輪。頭上の数字まで覆わない。
                for(int n=0;n<5;n++){
                    float cx=80+(n-2)*(12+5*t),cy=28+24*t+(n%2)*12;
                    Ellipse(cx,cy,19+10*t,23+12*t,new Color(.18f,.13f,.16f,.48f));
                    Ellipse(cx-2,cy+2,15+10*t,19+12*t,new Color(.52f,.38f,.29f,.52f));
                }
                float heat=1-t*.88f;
                Ellipse(80,24+8*t,36+10*t,32-12*t,new Color(1,.27f,.035f,.9f*heat));
                Ellipse(80,22,22+7*t,24-10*t,new Color(1,.73f,.13f,.95f*heat));
                if(center)Ellipse(80,19,12+5*t,20-12*t,new Color(1,.96f,.72f,.9f*heat));
                Ellipse(80,7,31+36*t,5,new Color(.45f,.18f,.08f,.7f*(1-t*.5f)));
                Ellipse(80,9,27+36*t,2,new Color(1,.65f,.2f,.8f*(1-t*.65f)));
                for(int n=0;n<5;n++)Ellipse(80+(n-2)*(18+8*t),23+(n%3)*13+24*t,1.5f,3,new Color(1,.8f,.35f,.85f*(1-t*.7f)));
                var texture=owned.CreateTexture(w,h);texture.SetPixels(pixels);texture.Apply(false,true);texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
                texture.name="Bomb451-"+center+"-"+age;var sprite=owned.CreateSprite(texture,new Rect(0,0,w,h),new Vector2(.5f,0));sprite.name=texture.name;return sprite;
            },assets);
        }
    }
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
