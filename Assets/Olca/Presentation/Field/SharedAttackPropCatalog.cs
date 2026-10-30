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
        public const float BombVisibleHeight = 38f / 100f;
        private static readonly Dictionary<string, Sprite> loaded = new Dictionary<string, Sprite>();

        public static bool TryGetSprite(string key, out Sprite sprite)
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
                default: return false;
            }
            if (loaded.TryGetValue(key, out sprite) && sprite != null) return true;
            // PNGの同一bytesを読み、既定import圧縮による色・輪郭の変化を避ける。
            var source = Resources.Load<TextAsset>("OlcaAttackProps/" + resource);
            if (source == null) return false;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(source.bytes, true)) {
                if (UnityEngine.Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
                return false;
            }
            texture.name = key;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, 100f, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            loaded[key] = sprite;
            return true;
        }
    }
}
