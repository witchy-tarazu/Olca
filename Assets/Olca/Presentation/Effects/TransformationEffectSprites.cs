using UnityEngine;
namespace Olca.Presentation.Effects
{
    /// <summary>全変身で共用する透明装飾。製品の姿・能力・演出時計を所有しない。</summary>
    public static class TransformationEffectSprites
    {
        private static Sprite activation, release;
        public static Sprite Get(bool activating)
        {
            if (activating) return activation != null ? activation : activation = Load("activation");
            return release != null ? release : release = Load("release");
        }
        private static Sprite Load(string name)
        {
            var bytes = Resources.Load<TextAsset>("OlcaTransformationEffects/" + name);
            if (bytes == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes.bytes, true)) { if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture); return null; }
            texture.name = "transformation-effect/" + name; texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp; texture.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name; sprite.hideFlags = HideFlags.HideAndDontSave; return sprite;
        }
    }
}
