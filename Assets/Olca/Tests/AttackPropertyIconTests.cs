using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;
using Olca.Presentation.CustomScreen;
namespace Olca.Tests
{
    public class AttackPropertyIconTests
    {
        private static Card Card(AttackProperty properties,ElementType element=ElementType.None)=>new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,element,CardSeries.Sword,1,"",properties:properties);
        [Test] public void 複合特性は明示フラグだけを安定順で一度ずつ解決する()
        {
            CollectionAssert.AreEqual(new[]{AttackProperty.Sword,AttackProperty.Break,AttackProperty.Wind},AttackPropertyIconCatalog.Resolve(AttackProperty.Sword|AttackProperty.Break|AttackProperty.Wind));
            Assert.IsEmpty(AttackPropertyIconCatalog.Resolve(AttackProperty.None));Assert.IsNull(AttackPropertyIconCatalog.Get((AttackProperty)128));Assert.IsNull(AttackPropertyIconCatalog.Get(AttackProperty.Sword|AttackProperty.Break));
        }
        [TestCase(AttackProperty.Sword)] [TestCase(AttackProperty.Break)] [TestCase(AttackProperty.Wind)]
        public void 専用素材は真透過の六十四角で非圧縮共有し状態素材に依存しない(AttackProperty property)
        {
            var name=property.ToString().ToLowerInvariant();var source=UnityEngine.Resources.Load<TextAsset>("OlcaAttackProperties/"+name);Assert.IsNotNull(source);
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Art/UI/AttackProperties",name+".png")),source.bytes);
            var texture=new Texture2D(2,2);try{Assert.IsTrue(texture.LoadImage(source.bytes));Assert.AreEqual(64,texture.width);Assert.AreEqual(64,texture.height);var a=texture.GetPixels32().Select(p=>p.a).Distinct().OrderBy(v=>v).ToArray();CollectionAssert.AreEqual(new byte[]{0,255},a);}finally{Object.DestroyImmediate(texture);}
            var sprite=AttackPropertyIconCatalog.Get(property);Assert.IsNotNull(sprite);Assert.AreSame(sprite,AttackPropertyIconCatalog.Get(property));Assert.AreEqual(FilterMode.Point,sprite.texture.filterMode);Assert.IsFalse(sprite.texture.isReadable);
        }
        [Test] public void 特性行は複数を表示し反復更新切替消去再表示で残留や入力判定を作らない()
        {
            var obj=new GameObject("Details",typeof(RectTransform),typeof(Text));try{
                var text=obj.GetComponent<Text>();var card=Card(AttackProperty.Sword|AttackProperty.Break);var before=card.Properties;
                for(int n=0;n<5;n++)CardAttackPropertyIcons.Bind(text,card,"カード\n内容");
                var row=obj.GetComponentInChildren<CardAttackPropertyIcons>();Assert.AreEqual(2,row.VisibleCount);Assert.AreEqual("\n\nカード\n内容",text.text);
                Assert.IsTrue(row.GetComponentsInChildren<Image>().All(i=>!i.raycastTarget&&i.preserveAspect));Assert.AreEqual(2,row.GetComponentsInChildren<Image>(true).Length);
                CardAttackPropertyIcons.Bind(text,Card(AttackProperty.None,ElementType.Wind),"風属性");Assert.AreEqual(0,row.VisibleCount);Assert.AreEqual("風属性",text.text);
                CardAttackPropertyIcons.Bind(text,Card(AttackProperty.Wind),"別カード");Assert.AreEqual(1,row.VisibleCount);Assert.AreEqual("Property_Wind",row.GetComponentsInChildren<Image>().Single().name);
                obj.SetActive(false);typeof(CardAttackPropertyIcons).GetMethod("OnDisable",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(row,null);Assert.AreEqual(0,row.VisibleCount);obj.SetActive(true);CardAttackPropertyIcons.Bind(text,card,"戻る");Assert.AreEqual(2,row.VisibleCount);
                CardAttackPropertyIcons.Clear(text);Assert.AreEqual(0,row.VisibleCount);Assert.AreEqual(before,card.Properties);
            }finally{Object.DestroyImmediate(obj);}
        }
        [Test] public void 読取専用カード詳細は特性の追加後も漢字だけの変換と本文を保持する()
        {
            var obj=new GameObject("Readonly",typeof(RectTransform));try{
                var view=obj.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();
                view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});
                var card=Card(AttackProperty.Sword|AttackProperty.Break);
                view.RenderCollection(new[]{"テスト"},System.Array.Empty<Olca.Core.Domain.CollectionRecipeRow>(),false,null,()=>{},()=>{},cardDetails:new[]{card});
                typeof(Olca.Presentation.Dungeon.ClearReturnView).GetMethod("OpenCardDetails",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,new object[]{card,"CloseCollection"});
                var body=view.GetComponentsInChildren<Text>().Single(t=>t.name=="ReadonlyCardPerformance");
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(body.text,"[一-鿿]"));StringAssert.Contains("ブンルイ",body.text);
                Assert.AreEqual(2,body.GetComponentInChildren<CardAttackPropertyIcons>().VisibleCount);
                Assert.AreEqual("\n\n"+CustomDisplay.Katakana(Olca.Core.Domain.CollectionDisplay.PerformanceText(card)),body.text);
            }finally{Object.DestroyImmediate(obj);}
        }
        [Test] public void 無特性や空カードは行を作らず風属性やソード系統から特性を推測しない()
        {
            var obj=new GameObject("Plain",typeof(RectTransform),typeof(Text));try{var text=obj.GetComponent<Text>();CardAttackPropertyIcons.Bind(text,Card(AttackProperty.None,ElementType.Wind),"元の文字");Assert.IsNull(obj.GetComponentInChildren<CardAttackPropertyIcons>(true));Assert.AreEqual("元の文字",text.text);CardAttackPropertyIcons.Bind(text,null,"カードなし");Assert.AreEqual("カードなし",text.text);}finally{Object.DestroyImmediate(obj);}
        }
    }
}
