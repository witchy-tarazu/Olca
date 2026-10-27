using System.IO;
using NUnit.Framework;
using UnityEngine;
using Olca.Presentation.Field;

namespace Olca.Tests
{
    public class SharedAttackPropCatalogTests
    {
        [TestCase(SharedAttackPropCatalog.Sword, "shared_sword", 256)]
        [TestCase(SharedAttackPropCatalog.ThrowingKnife, "shared_throwing_knife", 64)]
        [TestCase(SharedAttackPropCatalog.ShotBolt, "shared_shot_bolt", 32)]
        public void 共用素材は同じSpriteを返し実ファイルに透明と不透明の画素がある(string key,string file,int size)
        {
            Assert.IsTrue(SharedAttackPropCatalog.TryGetSprite(key,out var first));
            Assert.IsTrue(SharedAttackPropCatalog.TryGetSprite(key,out var second));Assert.AreSame(first,second);
            Assert.AreEqual(100,first.pixelsPerUnit);Assert.AreEqual(FilterMode.Point,first.texture.filterMode);
            // PNGデコーダのチャンネル順は問わず、アルファ付き非圧縮32bitだけを許す。
            Assert.That(first.texture.format,Is.EqualTo(TextureFormat.RGBA32).Or.EqualTo(TextureFormat.ARGB32));Assert.IsFalse(first.texture.isReadable);
            var original=File.ReadAllBytes("Assets/Art/Sprites/AttackProps/"+file+".png");
            CollectionAssert.AreEqual(original,Resources.Load<TextAsset>("OlcaAttackProps/"+file).bytes);
            Assert.AreEqual(size,first.texture.width);Assert.AreEqual(size,first.texture.height);
            var image=new Texture2D(2,2);
            try {
                Assert.IsTrue(image.LoadImage(original));
                int transparent=0,opaque=0;
                foreach(var pixel in image.GetPixels32()){if(pixel.a==0)transparent++;else if(pixel.a==255)opaque++;else Assert.Fail("半透明の縁が残っています");}
                Assert.Greater(transparent,0);Assert.Greater(opaque,0);
            }finally{Object.DestroyImmediate(image);}
        }
        [Test] public void 未登録キーは既存の別素材へ置換しない(){Assert.IsFalse(SharedAttackPropCatalog.TryGetSprite("missing",out var sprite));Assert.IsNull(sprite);Assert.IsFalse(SharedAttackPropCatalog.TryGetSprite(null,out sprite));}
    }
}
