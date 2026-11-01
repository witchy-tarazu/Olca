using UnityEngine;
namespace Olca.Presentation.Effects
{
    /// <summary>全変身で共用する透明装飾。製品の姿・能力・演出時計を所有しない。</summary>
    public static class TransformationEffectSprites
    {
        public static Sprite Get(bool activating,Olca.Unity.GeneratedSpriteAssets.Lease assets=null)
            =>Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("transformation/"+(activating?"activation":"release"),owned=>Load(activating?"activation":"release",owned),assets);
        private static Sprite Load(string name,Olca.Unity.GeneratedSpriteAssets.Builder owned)
        {
            var bytes=Resources.Load<TextAsset>("OlcaTransformationEffects/"+name);if(bytes==null)return null;
            var texture=owned.CreateTexture(2,2);if(!texture.LoadImage(bytes.bytes,true))return null;
            texture.name="transformation-effect/"+name;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;texture.hideFlags=HideFlags.HideAndDontSave;
            var sprite=owned.CreateSprite(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f));sprite.name=texture.name;sprite.hideFlags=HideFlags.HideAndDontSave;return sprite;
        }
    }
}
