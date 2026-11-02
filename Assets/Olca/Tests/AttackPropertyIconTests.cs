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
        private Olca.Unity.GeneratedSpriteAssets.Lease generatedScope;
        [SetUp] public void AcquireGeneratedScope()=>generatedScope=Olca.Unity.GeneratedSpriteAssets.Acquire();
        [TearDown] public void ReleaseGeneratedScope(){generatedScope?.Dispose();generatedScope=null;Olca.Unity.GeneratedSpriteAssets.Prune();}

        private static Card Card(AttackProperty properties,ElementType element=ElementType.None)=>new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,element,CardSeries.Sword,1,"",properties:properties);
        [Test] public void 六特性は明示マスターから定義保存復元し未知bitと単一弱点の誤指定は拒否する()
        {
            for(int bits=0;bits<64;bits++) {
                var master=new Olca.Core.Domain.Master.CardMaster{Id="explicit",Name="検証",Category=CardCategory.Attack,Series=Olca.Core.Domain.SeriesType.Sword,Properties=(AttackProperty)bits,Power=17};
                var card=Olca.Core.Domain.Card.FromMaster(master);
                var dto=Olca.Core.Logic.Dungeon.PortableCardDefinition.Capture("explicit/v1",card);
                var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(dto);
                var restored=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<Olca.Core.Logic.Dungeon.PortableCardDefinition>(bytes).Restore();
                Assert.AreEqual((AttackProperty)bits,restored.Properties);Assert.AreEqual(CollectionDisplay.DefinitionHash(card),CollectionDisplay.DefinitionHash(restored));
                Assert.AreEqual(17,restored.Power);Assert.AreEqual(ElementType.None,restored.Element);Assert.AreEqual(card.Properties,card.Clone().Properties);
            }
            foreach(int bits in new[]{64,65,128,-1}) {
                Assert.Throws<System.ArgumentOutOfRangeException>(()=>Card((AttackProperty)bits));
                var dto=Olca.Core.Logic.Dungeon.PortableCardDefinition.Capture("invalid/v1",Card(AttackProperty.None));dto.Properties=(AttackProperty)bits;
                Assert.Throws<System.ArgumentOutOfRangeException>(()=>dto.Restore());
            }
            foreach(var property in new[]{AttackProperty.Shooting,AttackProperty.Healing,AttackProperty.Support,AttackProperty.Sword|AttackProperty.Support})
                Assert.Throws<System.ArgumentOutOfRangeException>(()=>new TransformationWeakness(property));
        }
        [Test] public void 追加三分類はガード貫通や防御剥離を追加せず旧フラグの意味を保つ()
        {
            var guard=new BattleGuardState();guard.Set(true,false);
            foreach(var property in new[]{AttackProperty.Shooting,AttackProperty.Healing,AttackProperty.Support}) {
                Assert.IsTrue(guard.Blocks(10,property));Assert.IsFalse(guard.Blocks(10,property|AttackProperty.Break));
                var player=new PlayerCharacter(100);player.TakeDamage(10,attackProperties:property);Assert.AreEqual(90,player.CurrentHp);
                player.SetBarrier(100);player.TakeDamage(10,attackProperties:property);Assert.AreEqual(90,player.CurrentHp);Assert.AreEqual(90,player.BarrierHp);
            }
        }
        [Test] public void 旧bit定義ハッシュと未指定製品カードを変更しない()
        {
            Assert.AreEqual(1,(int)AttackProperty.Sword);Assert.AreEqual(2,(int)AttackProperty.Break);Assert.AreEqual(4,(int)AttackProperty.Wind);
            var hashes=new[]{"D684D748DBF338D8E994A56D4B7B664A262EF05460634E8F472CFFFFD82EAA93","DA1CD7AE3A163EBE9DB61D7CCD91B51731AFF53270BDD7821ED03506FF460BA0","232D65C72C19E5528369587E6B78BA9E4618D6AFA81A8F1F7573A95FAF09D52F","31F9BCB0152A9A7463D308A52E52D0CA48F26D3EBD5C46FA5D44F40ED3463EE3","F218076D2370B4027FD7EE33C1911BEC0C41C441C5B0C4DD7B82F37788E1EA09","81D58A5CF95CAE569CA1082BE0C3C44BF7BA241C5E9B53143B9D2CB3C9F82EDB","951685B8B44A5B29B6E625D985F4C9920BF87D43D12B9329A18B583BAB4C7CF8","E6582E127E5B9FF2CBD077910DCDFC354C0126AB9E71E182EFD28EC0CC7829CD"};
            for(int i=0;i<hashes.Length;i++)Assert.AreEqual(hashes[i],CollectionDisplay.DefinitionHash(Card((AttackProperty)i)));
            var products=new Olca.Application.Battle.SimpleCardFactory().CollectionCards.ToArray();
            CollectionAssert.AreEqual(new[]{"1218C1F2DB25767F19997CB95FC15F3A97EEEBBCFFA7E0C2036D866E68B60E55","948DDFF162CDF844C1E2B7E1B44E9B776100D14E9FD9D3C098D6B0160C7F2644","E666B3AE7FBA6907FA594A0B37FD3137EF68086A13E670D005ECA54978C92626","6730B35646456713B6C9C8BC84D3FB713691C3EBF9881C630F306DD8A981AC17","57DA3BACEBD001C9ED0C8F7A9FD9B617CC96F6B21E45A95EDF035D7FD168942E","592FA9E3C764DF64FAA94E41C21171F7DFA3EC214BBC3769D5AECCBC6E5498F5","1FC17C6DD1A12891D4E424446DBC484419BF84315F551CAA860EA16D80D4FA3F","32AC390531376B97233A72E81F610D49C32887CC884A40A74BDC7FFD73FA1088","7F919139649B76644CB2959C65A028B82890807A18B0B6B9B5B0B1150A240D12","D857847C3882E5F5AF41257F5956A686F6F9BE731E3572AC3D00BB25CD514949","449732BE6511DAEA9981EF8905159B6A911ECFB94033490501DD5D9695E44D60","D27F010885A04B9CF4A5202F89ECA29608A27F74EB2D4081795D0F7541433127","464E5E19F7BC90E4F815242C2EA0EC2A78DA4FACB34685DA542C175EA069662B","645834FFF3B4C7539190ED3C9ECFE14BC8023AC70FD2DA78127C56A692B7C026","ABFA524D5473530ADF1CE54023D99333E2C6413ADA22DAE5102D0110B060A205","013087DD985FBA84FFBC4F648CA32635D52CD8346750A87A1D187A02B221CA47","130530AB13A4CC2794FBB65232394D0008CED4A85F5BC27B47B97412D4B76CE0","BA3EB787AE873E8B02360ACCBD1A1969CDC4B05099EC3ACA8DAC10B7E80C9203"},products.Select(CollectionDisplay.DefinitionHash));
            Assert.IsTrue(products.All(c=>c.Properties==AttackProperty.None));
        }
        [Test] public void 追加分類の名称は実詳細本文で折り返せて旧画像だけを借用し別カードへ残さない()
        {
            var obj=new GameObject("SixDetails",typeof(RectTransform),typeof(Text));try {
                var text=obj.GetComponent<Text>();var all=(AttackProperty)63;
                CollectionAssert.AreEqual(new[]{AttackProperty.Sword,AttackProperty.Break,AttackProperty.Wind,AttackProperty.Shooting,AttackProperty.Healing,AttackProperty.Support},AttackPropertyIconCatalog.Resolve(all));
                CardAttackPropertyIcons.Bind(text,Card(all),"カード本文");
                Assert.AreEqual("\n\nトクセイ ザンゲキ / ショウゲキ / フウアツ / シャゲキ / カイフク / シエン\nカード本文",text.text);
                Assert.AreEqual(3,text.GetComponentInChildren<CardAttackPropertyIcons>(true).VisibleCount);
                CardAttackPropertyIcons.Bind(text,Card(AttackProperty.Healing|AttackProperty.Support),"別カード");
                Assert.AreEqual("トクセイ カイフク / シエン\n別カード",text.text);Assert.AreEqual(0,text.GetComponentInChildren<CardAttackPropertyIcons>(true).VisibleCount);
                CardAttackPropertyIcons.Bind(text,Card(AttackProperty.None),"未指定");Assert.AreEqual("未指定",text.text);
                Assert.Throws<System.ArgumentOutOfRangeException>(()=>AttackPropertyIconCatalog.Name(AttackProperty.Sword|AttackProperty.Break));
            }finally{Object.DestroyImmediate(obj);}
        }
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
