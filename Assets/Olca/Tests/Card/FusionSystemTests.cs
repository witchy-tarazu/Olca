using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests.CardTests
{
    /// <summary>
    /// FusionSystemのユニットテスト
    /// </summary>
    [TestFixture]
    public class FusionSystemTests
    {
        private MockCardFactory cardFactory;
        private MockFusionRecipeProvider recipeProvider;
        private FusionSystem fusionSystem;

        private static Card MakeCard(string id)
        {
            return new Card(id, id + "Card", 2, CardRarity.Normal, CardCategory.Attack,
                ElementType.None, CardSeries.None, 10, "");
        }

        [SetUp]
        public void SetUp()
        {
            cardFactory = new MockCardFactory();
            recipeProvider = new MockFusionRecipeProvider();
            fusionSystem = new FusionSystem(recipeProvider, cardFactory);

            // 結果カードを登録
            cardFactory.Register("dragon", new Card("dragon", "ドラゴン", 5, CardRarity.Normal,
                CardCategory.Attack, ElementType.Fire, CardSeries.None, 50, ""));
        }

        // ===== Validate =====

        [Test]
        public void Validate_正常ケース_成功()
        {
            recipeProvider.Register("dragon", "sword", "shield");
            var materials = new List<Card> { MakeCard("sword"), MakeCard("shield") };

            var result = fusionSystem.Validate(materials);

            Assert.IsTrue(result.IsValid);
            Assert.AreEqual("dragon", result.ResultCardId);
        }

        [Test]
        public void Validate_素材1枚_失敗()
        {
            var result = fusionSystem.Validate(new List<Card> { MakeCard("sword") });

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("2枚以上", result.ErrorMessage);
        }

        [Test]
        public void Validate_null素材リスト_失敗()
        {
            var result = fusionSystem.Validate(null);

            Assert.IsFalse(result.IsValid);
        }

        [Test]
        public void Validate_nullカード含む_失敗()
        {
            var materials = new List<Card> { MakeCard("sword"), null };

            var result = fusionSystem.Validate(materials);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("null", result.ErrorMessage);
        }

        [Test]
        public void Validate_レシピ不一致_失敗()
        {
            var materials = new List<Card> { MakeCard("sword"), MakeCard("bomb") };

            var result = fusionSystem.Validate(materials);

            Assert.IsFalse(result.IsValid);
            StringAssert.Contains("レシピ", result.ErrorMessage);
        }

        [Test]
        public void Validate_素材順序不問_成功()
        {
            recipeProvider.Register("dragon", "sword", "shield");
            // 順序を逆に
            var materials = new List<Card> { MakeCard("shield"), MakeCard("sword") };

            var result = fusionSystem.Validate(materials);

            Assert.IsTrue(result.IsValid);
        }

        // ===== Execute =====

        [Test]
        public void Execute_融合カードを返す()
        {
            recipeProvider.Register("dragon", "sword", "shield");
            var materials = new List<Card> { MakeCard("sword"), MakeCard("shield") };

            var result = fusionSystem.Execute(materials);

            Assert.IsTrue(result.IsSuccess);
            Assert.IsNotNull(result.ResultCard);
            Assert.AreEqual("dragon", result.ResultCard.Id);
        }

        [Test]
        public void Execute_バリデーション失敗時はエラー()
        {
            var materials = new List<Card> { MakeCard("sword"), MakeCard("unknown") };

            var result = fusionSystem.Execute(materials);

            Assert.IsFalse(result.IsSuccess);
        }

        [Test]
        public void Execute_FactoryでカードNotFound_失敗()
        {
            recipeProvider.Register("nonexistent", "sword", "shield");
            // "nonexistent" をfactoryに登録しない
            var materials = new List<Card> { MakeCard("sword"), MakeCard("shield") };

            var result = fusionSystem.Execute(materials);

            Assert.IsFalse(result.IsSuccess);
            StringAssert.Contains("生成に失敗", result.ErrorMessage);
        }

        [Test]
        public void Execute_3素材の融合は不可()
        {
            var triCard = new Card("tri", "トリカード", 6, CardRarity.Normal,
                CardCategory.Attack, ElementType.None, CardSeries.None, 80, "");
            cardFactory.Register("tri", triCard);
            recipeProvider.Register("tri", "a", "b", "c");

            var materials = new List<Card> { MakeCard("a"), MakeCard("b"), MakeCard("c") };

            var result = fusionSystem.Execute(materials);

            Assert.IsFalse(result.IsSuccess);
            Assert.IsNull(result.ResultCard);
        }

        [Test]
        public void Execute_FactoryNull_例外()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new FusionSystem(recipeProvider, null));
        }

        [Test]
        public void Execute_RecipeProviderNull_例外()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new FusionSystem(null, cardFactory));
        }
    }
}

namespace Olca.Tests.CardTests
{
    [TestFixture]
    public class NormalFusionRuleTests
    {
        private static Card Card(string id,string name,CardRarity rarity=CardRarity.Normal,ExceedLevel level=ExceedLevel.Normal,CardCategory category=CardCategory.Attack)
            =>new Card(id,name,1,rarity,category,ElementType.None,CardSeries.Sword,10,"外部検証",exceedLevel:level);
        private static FusionSystem BuildSystem(FusionDefinitionCatalog data)=>new FusionSystem(new MockFusionRecipeProvider(),new MockCardFactory(),data);
        [Test]
        public void 系統組合せと上位レギュレーションと最大レベルで標準結果を選び特殊が優先する()
        {
            var a=Card("a","A");var b=Card("b","B",CardRarity.Limited,ExceedLevel.Lv1);var output=Card("out","結果★",CardRarity.Limited,ExceedLevel.Lv1);var special=Card("special","特殊");var data=new FusionDefinitionCatalog();
            data.RegisterCard(a,"攻撃","a",0);data.RegisterCard(b,"攻撃","b",1);data.RegisterCard(output,"結果","out",1);data.RegisterCard(special,"特殊","special",0);data.RegisterNormal("攻撃","攻撃",CardRarity.Limited,"out");var system=BuildSystem(data);
            var grant=Card("t","調律",category:CardCategory.Tuning);grant.Modifier.BonusDamage=9;a=a.ApplyTuning(grant);var first=system.Execute(new[]{a,b});Assert.IsTrue(first.IsSuccess);Assert.AreEqual("out",first.ResultCard.Id);Assert.AreEqual(ExceedLevel.Lv1,first.ResultCard.ExceedLevel);Assert.AreEqual(CardRarity.Limited,first.ResultCard.Rarity);Assert.AreEqual(0,first.ResultCard.Modifier.BonusDamage);Assert.AreEqual("out",system.Execute(new[]{b,a}).ResultCard.Id);
            data.RegisterSpecial("fixture/special",a,b,special);Assert.AreEqual("special",system.Execute(new[]{a,b}).ResultCard.Id);Assert.AreEqual(ExceedLevel.Normal,system.Execute(new[]{b,a}).ResultCard.ExceedLevel);
        }
        [Test]
        public void 同シリーズ別レベルと未登録定義と同ID別定義と不足表を拒否する()
        {
            var a=Card("same","A");var up=Card("same","A★",level:ExceedLevel.Lv1);var b=Card("same","別定義");var data=new FusionDefinitionCatalog();data.RegisterCard(a,"攻撃","a",0);data.RegisterCard(up,"攻撃","a",1);data.RegisterCard(b,"攻撃","b",0);var system=BuildSystem(data);
            StringAssert.Contains("同じシリーズ",system.Validate(new[]{a,up}).ErrorMessage);StringAssert.Contains("レシピ",system.Validate(new[]{a,b}).ErrorMessage);StringAssert.Contains("未登録",system.Validate(new[]{a,Card("same","未登録")}).ErrorMessage);
            data.RegisterNormal("攻撃","攻撃",CardRarity.Normal,"missing");StringAssert.Contains("未登録",system.Validate(new[]{b,a}).ErrorMessage);Assert.Throws<System.ArgumentException>(()=>data.RegisterNormal("攻撃","攻撃",CardRarity.Normal,"other"));
        }
        [Test]
        public void 素材役割と登録不正と製品特殊の定義照合を共通化する()
        {
            var a=Card("a","A",category:CardCategory.Tuning);var b=Card("b","B",category:CardCategory.Tuning);var result=Card("r","R",category:CardCategory.Tuning);var data=new FusionDefinitionCatalog();data.RegisterCard(a,"補助","a",0);data.RegisterCard(b,"補助","b",0);data.RegisterCard(result,"補助","r",0);data.RegisterNormal("補助","補助",CardRarity.Normal,"r");Assert.IsTrue(BuildSystem(data).Execute(new[]{a,b}).IsSuccess);
            Assert.IsFalse(BuildSystem(data).Execute(new[]{a,Card("x","X")}).IsSuccess);Assert.IsFalse(BuildSystem(data).Execute(new[]{a,a}).IsSuccess);Assert.Throws<System.ArgumentException>(()=>data.RegisterCard(result,"別","r",0));
            var factory=new Olca.Application.Battle.SimpleCardFactory();var recipes=new Olca.Application.Battle.SimpleFusionRecipeProvider(factory);var system=new FusionSystem(recipes,factory);var sword=factory.CreateById("sword");var bomb=factory.CreateById("bomb");Assert.IsTrue(system.Execute(new[]{sword,bomb}).IsSuccess);
            Assert.IsFalse(system.Execute(new[]{Card("sword","別性能"),bomb}).IsSuccess);Assert.IsFalse(system.Execute(new[]{factory.CreateExceeded(sword,ExceedLevel.Lv1),bomb}).IsSuccess);
            Assert.AreEqual("special/v1/bomb+sword/fire_sword",recipes.CollectionRecipes[0].Id);
        }
        [Test]
        public void 通常融合のプレビューと実行と連鎖とUndoと図鑑の元定義識別が一致する()
        {
            var a=Card("same","A");var b=Card("same","別シリーズ");var result=Card("result","結果");var c=Card("c","C");var final=Card("final","最終");var data=new FusionDefinitionCatalog();foreach(var entry in new[]{a,b,result,c,final})data.RegisterCard(entry,"共通",entry.Name,0);data.RegisterNormal("共通","共通",CardRarity.Normal,result.Name);
            var model=new CustomScreenSystem(new Deck(new List<Card>{a,b,c}),fusionSystem:BuildSystem(data));model.Open(new Card[0],new MockRandomProvider(0));Assert.IsTrue(model.IsCompatibleWithCurrentSelection(new[]{a},b));var before=model.CurrentHand.ToArray();
            Assert.IsTrue(model.PreviewCustomization(new[]{a,b}).CanExecute);CollectionAssert.AreEqual(before,model.CurrentHand);int notices=0;model.OnFusionExecuted+=(inputs,output)=>{notices++;Assert.IsTrue(data.CollectionRecipes.Any(r=>r.Matches(inputs,output)));};
            var fused=model.ExecuteFusion(new[]{a,b});Assert.IsTrue(fused.IsSuccess);Assert.AreEqual(2,model.CurrentHand.Count);Assert.IsFalse(model.ExecuteFusion(new[]{a,b}).IsSuccess);Assert.AreEqual(1,notices);Assert.IsFalse(data.CollectionRecipes.Any(r=>r.Matches(new[]{a,Card("same","未登録")},fused.ResultCard)));
            var second=model.ExecuteFusion(new[]{fused.ResultCard,c});Assert.IsTrue(second.IsSuccess);Assert.AreEqual(1,model.CurrentHand.Count);Assert.IsTrue(model.TryUndoCustomization());Assert.IsTrue(model.TryUndoCustomization());CollectionAssert.AreEqual(before.Select(CollectionDisplay.DefinitionHash),model.CurrentHand.Select(CollectionDisplay.DefinitionHash));Assert.IsFalse(model.TryUndoCustomization());
        }
        [Test]
        public void 元定義と通常結果を保存復帰してもレベル判定と公開条件が変わらない()
        {
            var a=Card("same","A");var b=Card("same","B",CardRarity.Ultimate,ExceedLevel.Lv2);var output=Card("out","結果★★",CardRarity.Ultimate,ExceedLevel.Lv2);var table=new FusionDefinitionCatalog();table.RegisterCard(a,"A","a",0);table.RegisterCard(b,"B","b",2);table.RegisterCard(output,"結果","out",2);table.RegisterNormal("A","B",CardRarity.Ultimate,"out");
            var definitions=new Olca.Core.Logic.Dungeon.CheckpointDefinitions("normal-fusion",DungeonConfig.CreateTestDungeon());definitions.RegisterCard("a",a);definitions.RegisterCard("b",b);definitions.RegisterCard("output",output);
            var data=new[]{definitions.CaptureKnownCard("one",a),definitions.CaptureKnownCard("two",b),definitions.CaptureKnownCard("three",output)};var serializer=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(CheckpointCardDto[]));
            using(var stream=new System.IO.MemoryStream()){serializer.WriteObject(stream,data);stream.Position=0;data=(CheckpointCardDto[])serializer.ReadObject(stream);}
            var restore=typeof(Olca.Core.Logic.Dungeon.CheckpointDefinitions).GetMethod("RestoreCard",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var restored=data.Select(dto=>(Card)restore.Invoke(definitions,new object[]{dto})).ToArray();
            var result=BuildSystem(table).Execute(new[]{restored[1],restored[0]});Assert.IsTrue(result.IsSuccess);Assert.AreEqual(CollectionDisplay.DefinitionHash(restored[2]),CollectionDisplay.DefinitionHash(result.ResultCard));Assert.AreEqual(CollectionDisplay.DefinitionHash(a),data[0].ValueHash);
            var recipe=table.CollectionRecipes.Single(r=>r.Matches(new[]{a,b},output));var profile=new ClearReturnProfile();Assert.IsFalse(recipe.TryGetResult(profile,out _));profile.RevealedRecipes=new[]{recipe.Id};Assert.IsTrue(recipe.TryGetResult(profile,out var detail));Assert.AreEqual(output.Name,detail.Name);Assert.AreEqual("？？？",recipe.MaterialName(profile,0));Assert.IsFalse(CollectionDisplay.IsKnown(profile,output));
        }
    }
}
