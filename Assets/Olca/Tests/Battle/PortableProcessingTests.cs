using System;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Application.Battle;
using Olca.Tests.Mocks;
namespace Olca.Tests.Battle
{
    public class PortableProcessingTests
    {
        public static PortableBattlePackage Package(bool savedTuning=false)
        {
            var f=new SimpleCardFactory();var deck=new Deck(new[]{"sword","sword","bomb","bomb","tuning","tuning","heal","heal","shield","shield"}.Select(f.CreateById).ToList());
            var map=DungeonConfig.CreateTestDungeon(100,30,50);var session=DungeonCheckpointSession.CreateDefault(map,deck);
            var known=new HashSet<string>(session.Definitions.CollectionDefinitions.Select(CollectionDisplay.DefinitionHash));
            foreach(var c in f.FusionCards)if(known.Add(CollectionDisplay.DefinitionHash(c)))session.Definitions.RegisterCard("processing/"+CollectionDisplay.DefinitionHash(c),c);
            var grant=f.GetTuningDefinition(f.CreateById("tuning"));session.Definitions.RegisterGrant("grant/product/v1",grant);
            _=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new SeededBattleRandom(1),new MockLogger(),checkpoints:session);
            var d=session.Store.Read();d.Enemies.First(e=>e.CellIndex==50).GridX=4;d.Player.GridX=2;
            if(savedTuning){var original=d.Cards.First(c=>c.DefinitionKey=="initial/v1/"+CollectionDisplay.DefinitionHash(f.CreateById("sword")));var tuned=TuningEvaluator.Evaluate(f.CreateById("sword"),grant).ResultCard;var n=session.Definitions.CaptureKnownCard(original.InstanceId,tuned);d.Cards[Array.IndexOf(d.Cards,original)]=n;}
            var encounter=new CheckpointBattleDto{ContactCell=50,ParticipantCells=new[]{50},HandLimit=10,GridOriginX=47};
            Assert.IsTrue(CheckpointBattlePlan.TryCreate(d,encounter,session.Definitions,73,null,c=>"fallback/v1/"+c.Rarity+"/"+c.Id,out var plan,out var error),error);
            var t=CheckpointBattleFactory.ProductBasicTiming;var p=PortableBattlePackage.Capture(session.Definitions,PortableCheckpointJson.Encode(plan.Snapshot),new PortableBattleRules{ChargeFrames=60,IntervalFrames=12,HitInvincibilityMs=600,StartupFrames=t.StartupFrames,RecoveryFrames=t.RecoveryFrames,CancelAfterFrames=t.CancelAfterFrames,GaugeSpeed=2},999);
            p.Processing=PortableProcessingCatalog.Capture("product-derived-fixture","v1",f.FusionCards.Concat(p.Cards.Select(c=>c.Restore())),f,new SimpleFusionRecipeProvider(f).FusionDefinitions,f.GetTuningDefinition);
            return PortableCheckpointJson.Deserialize<PortableBattlePackage>(PortableCheckpointJson.Serialize(p));
        }
        [Test]
        public void 正規の特殊融合調律超越を通常コマンドへ復帰し取消と消費とUndoを維持する()
        {
            var p=Package();var s=p.CreateSimulation();var hand=s.Battle.CustomScreen;var before=hand.CurrentHand.Select(CollectionDisplay.DefinitionHash).OrderBy(x=>x).ToArray();
            var commands=PortableProcessingReplay.Commands(p);Assert.AreEqual(3,commands.Count(c=>c.Custom?.Kind==CustomCommandKind.Undo));
            foreach(var c in commands.TakeWhile(c=>c.Custom?.Kind!=CustomCommandKind.Select))s.Step(c);
            CollectionAssert.AreEqual(before,hand.CurrentHand.Select(CollectionDisplay.DefinitionHash).OrderBy(x=>x));Assert.AreEqual(0,hand.Commands.Observe().UndoCount);
            Assert.AreEqual(PortableProcessingReplay.Trace(p),PortableProcessingReplay.Trace(p,7,()=>GC.KeepAlive(new int[100])));
        }
        [Test]
        public void 保存済み調律履歴と再開後の加工が同じ元定義へ戻る()
        {
            var p=Package(true);var state=p.Restore(out _);Assert.AreEqual(1,state.Deck.GetAllCards().Count(c=>c.TuningCount==1));Assert.IsTrue(state.Deck.GetAllCards().Any(c=>c.TuningCount==1&&c.GetFinalPower()==40));
            var before=(byte[])p.CheckpointEnvelope.Clone();Assert.AreEqual(PortableProcessingReplay.Trace(p),PortableProcessingReplay.Trace(p));CollectionAssert.AreEqual(before,p.CheckpointEnvelope);
            p.Grants=Array.Empty<PortableTuningGrant>();Assert.Catch(()=>p.CreateSimulation());
        }
        [TestCase("version")] [TestCase("missing")] [TestCase("result")] [TestCase("grant")] [TestCase("hash")] [TestCase("normal")]
        public void 不正または欠損した加工定義を別の製品値へfallbackしない(string mutation)
        {
            var p=Package();switch(mutation){case "version":p.Processing.Version=2;break;case "missing":p.Processing.Cards=Array.Empty<PortableCardDefinition>();break;case "result":p.Processing.Special[0].Result="absent";break;case "grant":p.Processing.Tuning[0].Grant.Id="different";break;case "hash":p.Processing.Hash="bad";break;case "normal":p.Processing.Normal=new[]{new PortableNormalFusion{A="missing",B="missing",Series="missing"}};break;}Assert.Catch(()=>p.CreateSimulation());
        }
        [Test]
        public void 通常融合の表と特殊優先を同じ登録カタログから復元する()
        {
            var f=new SimpleCardFactory();var special=new SimpleFusionRecipeProvider(f).FusionDefinitions;special.RegisterNormal(CardSeries.Sword.Name,CardSeries.Bomb.Name,CardRarity.Normal,"heal");
            var p=PortableProcessingCatalog.Capture("normal-fixture","v1",f.FusionCards,f,special,f.GetTuningDefinition);var r=p.Restore();Assert.AreEqual("fire_sword",r.Fusion.Execute(new[]{f.CreateById("sword"),f.CreateById("bomb")}).ResultCard.Id);
            var normal=new FusionDefinitionCatalog();foreach(var c in f.FusionCards)normal.RegisterCard(c,c.Series.Name,c.Id,(int)c.ExceedLevel);normal.RegisterNormal(CardSeries.Sword.Name,CardSeries.Bomb.Name,CardRarity.Normal,"heal");
            r=PortableProcessingCatalog.Capture("normal-only-fixture","v1",f.FusionCards,f,normal,f.GetTuningDefinition).Restore();Assert.AreEqual("heal",r.Fusion.Execute(new[]{f.CreateById("bomb"),f.CreateById("sword")}).ResultCard.Id);
        }
        [Test]
        public void 付与付きカードの対象条件と欠落段階を補完せず輸送する()
        {
            var sword=new SimpleCardFactory().CreateById("sword");var condition=new TuningTargetCondition(CardCategory.Attack,CardSeries.Sword,ElementType.None,CollectionDisplay.DefinitionHash(sword));
            var grant=new TuningGrantDefinition("fixture/restricted/v1",new[]{new TuningEffectDefinition("damage",2,new Dictionary<int,CardModifier>{[1]=new CardModifier{BonusDamage=7}},condition)},targetCondition:condition);
            var material=new SimpleCardFactory().CreateById("tuning").WithTuningDefinition(grant);
            var dto=PortableCheckpointJson.Deserialize<PortableCardDefinition>(PortableCheckpointJson.Serialize(PortableCardDefinition.Capture("fixture/tuning/v1",material)));var restored=dto.Restore();
            var system=new TuningSystem();var first=system.Execute(sword,restored);Assert.IsTrue(first.IsSuccess);Assert.AreEqual(37,first.ResultCard.GetFinalPower());Assert.IsFalse(system.Execute(first.ResultCard,restored).IsSuccess);
            Assert.IsFalse(system.Validate(new SimpleCardFactory().CreateById("bomb"),restored).IsValid);Assert.AreEqual(CollectionDisplay.DefinitionHash(material),CollectionDisplay.DefinitionHash(restored));
        }
        [Test]
        public void 同ID別定義を調律対象や超越元へ推測解決しない()
        {
            var p=Package();var r=p.Processing.Restore();var original=new SimpleCardFactory().CreateById("sword");var restored=PortableCardDefinition.Capture("series/v1",original).Restore();Assert.IsTrue(new SimpleCardFactory().HasExceededDefinition(restored,ExceedLevel.Lv1));var changed=original.WithAttackTiming(new PlayerAttackTiming(3,9,4));
            Assert.IsFalse(r.Exceed.Validate(original,changed).IsValid);Assert.IsFalse(r.HasExceededDefinition(changed,ExceedLevel.Lv1));Assert.IsNull(r.GetGrant(new Card("tuning","別",1,CardRarity.Normal,CardCategory.Tuning,ElementType.None,CardSeries.None,0,"fixture")));
        }
    }
}

namespace Olca.Tests.Battle
{
    // 8cb8f684の旧serializerで採取した固定bytes。期待値を新しいserializerから再生成しない。
    public sealed class LegacyCardPowerContractTests
    {
        private static byte[] Golden(string name)
        {
            string encoded;
            switch(name){
                case "catalog": encoded=
                    "H4sIAAAAAAACA+2bXY9bVxWG/wry9Ujstb9X7vanEkEganPVUlUez5nJAceO7DNTRqgS4wGpIbRCEZSgViOKQIhEorRclKhD+C+4noar/AXWsSdTUkjHTg7T" +
                    "xLFkzdjnc+/t/Zx3vWsvv/qjlquqducHF/tV2e9dLq8WrXNsrRXaVbHVH+zOPvSHVescrLViMewMymv1ka1zrdzudtfp1G902oON1lordYurRa+anpJ+2CmK" +
                    "jW8XO0X3+HNV9DZeKjr9nWKwG7cHZW8rFoNyp12VO3TLzXZ3WKy1zreHV+jKDASzJka0KntnZc4+yKylDU5wLVRUPEQrpDaKOWscRKC/jHvOIThpqDHny+pC" +
                    "b6fsdcr1sltWuxeH03Zc2KDLD6+URbdu8YXhhd6waveqtLlZdKqTZnyroI63usVWu7P7vW8yOvJif6PcLItB6xwN2MZGWQ9Buzs7ja786mtrLd/vbQ9j+2p7" +
                    "qzgZNbfx/e1hdTIqs70Xt7tVea07vRqcDNt3aWAG5Qad29vudtdarxSD/mzgp416s27DThHLAd1xOv6zw2Yb6050ZretN3zpy/xOu35z3O3Xj7+tS+1e0Q1X" +
                    "2r2t4vLutZPbXip6RTVo1+dTn2fnX+qXw+Jhz44P679RN1/Uewf9a8WgKovZCL/UHtBwT3v28OsOdeO6brMqBnlAbRk+vMrDA/731pP2v0wTpd7dGo/+Pt4/" +
                    "HO9fp/a/XLUH1fa1R0893vjFmdvrj8zky+3BVlHN+lt/3O5N5+Fm2Su/GNQ31+aHgn8Zis8//MnkvY+Pfvnp0c39r2ICFmbC5Oy0D0YIC0oREDyZZIBmP2Sj" +
                    "vU6JqJFcoUJ0MThrY4o8ZnpljfwUJtb7V9fnJgKWhIjx/vvj/d/+89ZPHxy+NR7dGe/ffnB4/SnY4OyEDP14Mtj/g4xpT54ZLP5LKybX3z569y6M9w/Go7un" +
                    "07GwYgAHG4DmuufKaJMBEU3wqHIAlYVDk1LyPhBDJrHAmdDRap209ZolpU5TjDf6g/kFgy8LHv/xuH1SJEDNIxbshRCLM4ygSAqUYM6DcDIEblBm77XigguU" +
                    "kTFmQ4rSUXxFSsKNl8ajkcCj9EaHxE7hYZNm1dw4iCXBoe70Knp6lmRi4SAKSA5M0DGCA24RokySSyl1pHjKSgnos5WCJEMpchLOkjg4IKlgUWYncqMyIZdN" +
                    "JiiQakYppFopxVOBwRcFQ3AXBJJmgCAvgYYL4QxPFrIGmvsYBLfWhGCtdJI5I30MZEiiEdkBs7ZRMNQSgtEYG3oVRZ1ZFCUlGsF90goguuQQLaRsiRNQ6LVD" +
                    "gBSyR8mEkLSVQYwqokaVpIyaNZuH0qs81BNEUisomoZCy6QSYDY+IEsyW1Bc8sCTYylwZ2x0FCwFL6S2KjpF+8Ao2qtRa+5PgaLszO8szJIQQX1e4dAADurx" +
                    "8dOH9+5/9ME0x1c3dLx3Z3L4yeTn705+dvfo4P3x6OZnn9767G/vjPd+Mx7duH/7H5N7N04ObpQeZaIT3oXkyYVDwBRZsNk4zJEUhcxJQJ2155CkcqQjMTOh" +
                    "oskRtEXJ0yn0VNOhmxsguzSJ3L3x/h+mX9eN8f5fx6O/NIXSGZO0d3u89+tnd3XjERvy+eidyUcHRz/+48yPTHPoxw+CB4c3Zhnp8d6f7x+8PfnFW7PE+glH" +
                    "8NQcIc8OkwhGy5g1CY5DMvMQOICBbCLZdVIjkN4HsjQYlUsh+KCTkmizmiPB9fpirgWXhqVfjUf0qPz9ePS7JjLAYq5FEViFac2GaVoqK2TOWXgZjBIIFKxF" +
                    "QYoTciIsgmVcuGBYjjxS0AbcBKWdRu5NYFw36uhhadbQ626vksANMCEey8S/bn08ufdBszAYwbxQmryL0gaE1wGDJQZkFNnTBo0QRPIZrYWgBcuC6RitI8Pv" +
                    "yL00a+RhadbPR59MJ9Odp1OIR3gA9bxEXPz5FgiTSRJAoJbojdZSBs9RYdDKMW695RaZZcaB9cxrj17RfwDFHJcsAm9WIPhKIFZmvuEaq4WRQGljzBk0DzFb" +
                    "IgISRUMJvJSJIDEaGM18ek9mnUIphjZqz0CzYDInghqtsRJLVWTVTFWVfKGrqp4YBLEwCAlSDEJCQhGBZY7W0ePf+mgyUyQOhoKjFCl40sagoMhJxFRbaxEC" +
                    "eoRGQZDLVm04ezVfcwh8VXR4NrGT89kpLqOSRtR1VjFDoNlPjjoi4SASD1qCcNFxMtdJAeORATDPNIVP7LTs05Wi3Z2fj2VZLq97vbLWz1IZycK64UkbcmRM" +
                    "6GSt41ZInjjLiWIo8g+B00tGshnMchDSZmUwAzOQjCA7ElKznkIvZx1JY6UkuFo1PzPF8I6CJGONS9aIRJaaC5KCEHxdiogaQwzSM8UQUxAuOBtdIL0wybKA" +
                    "nIkGV81htWy+0ouvm4fIyUnUlYPKMekxZEkBFePZ0vx3HDUj+TDZyOR5lMyCQRVZNkqCFAK4abJAHeyqQn2Ve/raibDKWGmCsJYnlZXLErgyWWG9LGd11hk9" +
                    "BUqQhDJ1uS7FWd5TSBUVSJTYqKfAladYEdFsEmrhmvSYaH5bkVWQ5CDq1Wz6r1HYTHhIUEp4xQxysEIYFiwIgaQZkJFbCDY3mYTibAmTUM1noOyLmIASj0Xj" +
                    "/KXJeweTe39qVCeS1toLl0z2TiMz2SmUjnly0LH++UbK2jKrHYikDSORUBRRKYnGkpHQpxZ2LKQTfHl+CX5ztpLdlE7w56ZwEOYl4bV/AyaXSsItRAAA";break;
                case "default": encoded=
                    "H4sIAAAAAAACA21RQW7CQAz8S84ceuaGSCqilgoBHzCbSbC67EZeJ21U9e/NsoSUqjd7PB6P7a9spUrmfeuVvTvyBdnyaZGtSdF4GVLig16DHMEIt5GZLV1n" +
                    "7SIrLC5wqVx8GqB6RQ97yxWu2sP4HjLknbBrcgj3pNyPc2qyAYtsQ+E8yW1YS9ezM3xiyzpsw1WprCZCGUoXlJwWdQ2jd5EXDBNl6yuuGTLnPXKWkf3LdwKj" +
                    "kkkbR+DPEd4oBom/Iwe7PpNrcBzaGYaDCsW2cWxq23kOyOlCzUzzH9FPLIpvIcpIi+1Jxi1TeLvTOlqyq1ohzzI6CJPIRPgfvbs+jBeeywcl0a597LmBc0t3" +
                    "enj4kaSBpj1j2rnr52p2PN/w+wfd9NROOwIAAA==";break;
                case "variants": encoded=
                    "H4sIAAAAAAACA+1aW28bRRT+L/uc0rlf8jZXNSohVdsn2j5s7bG74Kyj9do0qiKRREJAKx6QoCAhJBAIqZW4iAeoKOXHbFP6M5j1xglxQilVXaF2ozx4bjvn" +
                    "fHO+b87x+tKNRJVl2nl7dVhmw/xith6SZbCUmLQM/WGx2TSGozJZRkuJDaNOkW3UM5Pl5NHt7/be/aba/n7v690/P7mVLCVuENZDXk4XueudELqvh0kY7LfL" +
                    "kHfPh85wEopNOy6yvG9DkU3SMpvETXvpYBSWkjPp6Fp8NoRSc2IF1coyZxxSGElCObYK8vgHOcBYOk6FklxSTinwxlAiGCAQeBuNOZOVK/kkyzvZ1WyQlZur" +
                    "o6kdK934+EHop53NU+vZ9dCNU1dGK/moTPPS9XqhUx4YczZszk2+fBrE+avDbtbLQpEsR/i63awGJB00i+Mul25E/PJOGKzlZ4aDQ9/MtTTLTVp0axvy8WBQ" +
                    "IzpINw9Qj6ikDbgR7OZxa0XWz/IGwWnHxc2NOJlO/TPD8T7a885eKNN+GM12OTfMRsN8JS9DMUkHvkjX58fOZnmNy8a0kdS979TuRTMupkU/lGu93ijErW5s" +
                    "xZ5oUr8eBVtXlhI9zMcjm67H/ZJl3gSL6r41HpVNKJyC0a/p8Op4UGYbgylu8DV6EC5rMSCKrBuXx6lvhmLYxNsUtK0a7EmwWRFdnyLTWN101mfWaaCrO+Zi" +
                    "+I20/pBUu+9VO/eq3fdrt9I8DOIx5P3Q4LiPQchD2UAfj7ZZXeMSZn7NoJqCgqOZ54rhRijKLDRBdT4tIujNx/0IbyJA9SLmRwGfTTi598D6C5Ebh8PxPIty" +
                    "vHF0zX7n4ZLx1SO8bY6ucbRujvMp53pZnh1iubX0P5UASj03ABvJtLaeOsko9lpgBCC22GgHEeYw/nOhqZXMIw2ZJggD5CwE8B8kAILnIAKwFYFWBJ5NBMBx" +
                    "BQBPoH9S7fxe7d6vdj9IjqkAeNklwBtEYLz4PVNQOOyBBtBgawCEUBvFjGeSKmusIUBJCZUyGitkWdQB4j1bpASgVgJaCXg2CSDHJUA+owTgl10CFCEaEimE" +
                    "15Qp6bnkglPM6pIAEuWxBlEBLEFaA2Mj85Fzikvl6hIhNhZXCOBWAF49AQBtFfCCUwCMlCUKC8+AcNoKKmPlL1AkPqSMRM7H6kA4SZ0wkmgskdQGmigAyBqO" +
                    "/SJTANIqQKsAbQmwUP4b6CmggBHNpYYSUMm5ocgbjYQ2HjBKvVMexYqfQGUwZMQIBSxDJgoB14vkP2353/K/zf8X+yLASMM4NgJKiSziSsaMH0tvLGBAaSiA" +
                    "RsgqiJiwQgskrfcyVgpYMuYk0ovL/1nL/leP/ZC2BcAL/gKAEey8RAJgF+97COqG9kRa4zgg1nqpEJXeM0QxNpxjLoimRHtqpINokQkAbyWglYC2AlisADjn" +
                    "uaBQiFjlcygpcThe7B5RoD1kCGkCpAHAMR0zAGeBxNA4EcUCQEixXOh7QNEKQCsAr04JQGcCAOcFYO+HB49/+qraudtYWW3f3bv/y96tT/c+vPfoyy+qnY8f" +
                    "/vbZw18/qrY/r3ZuPr7zx96DmweTn6taSKYRlgYjRA0UBOH6t0HWWcE41VJTS6N82Jg1ECWsMtQByCgCAFkLCbP/9r6gnEL3tCrRL+KEp5CIeeaA48wBJxLn" +
                    "BNo0QbwI5mxXu99OT+xmtftztfPj83qPBl5kHp1U23eq7dvJonLpqPbDvJv9HeQjV8H84EqjtDUQEf30+lSrpyc7U+246r/GD1xEAM0s27pSK72vL8AT9piV" +
                    "7U6xmHzHy5nxyDYhiBOa1V/MeROZpzzz3hsDiVLIRcIS5Y2HsQtryhGv79UGmWLYHXfKy6cb4l0+PYXq1KS+1RuqPWnGvBNbV/4CZlDJRfAoAAA=";break;
                case "package": encoded=
                    "H4sIAAAAAAACA+1dWY9cx3X+K8G8ZgTWvuitVkuI6RCSYGQhITRnLsmOm92Dnh5ajCDAnEkCKYqNQEgcBTaIKEhgWALi2HmwBTPKf0l7aOVJfyG13dvdw1nZ" +
                    "l+SdngaHZPe9tZw6db6zVNWpeX/D9Mbbuxuv//n7G2oy6W197/po0h8N3+nfrzZeB5vh9aS6Oxo/zF9Gu5ON1+Hmhq12t8b9nVhy4/UN3xsMboeqf7AV2trY" +
                    "3HCD6n41nKQq7r2tqtr+dvWgGpTvk2q4/Va1NXpQjR/avXF/eNdW4/6D3qT/IHR5pzfYrTY33ujt3gstQwwoBkpDrIgxiEvitWYUYYQlsQAAYZwlSlBPdXit" +
                    "CdeSE4gs0ZwZBwIxb/Qnbw4f9Idb/dv9QX/y8PpuouPN7dD8nf64CkXe3H1zuDvpDSfuzp1qa9IQ8UfVw1iojO7mtQfw5rVv9+/3J9X2zWul7vXRdv9Ovxpv" +
                    "vB4YuL3djyzpDXJDka23Njf0aLi3a3v3e3erhotq+y/2dicNl/Lb63uDSX9nkFqDDRv/ODBq3N8OdYd7g8Hmxp9V41GeiETmB5GGB5UN5Gzl+cjF8sM4rK3c" +
                    "bXxwZHK/04sfEhveLXN3ozesBuZeb3i3eufhTtPpjWpYTca9WDuMONe+MervVvW4SrHR9yPxOL4dj3aq8aRfZX6/1RsH5qdx1ZNvImkDdWdSjf04ULJbt1IX" +
                    "OP5pQ/3bQWzi643p/n9PD55MDz4K9L896Y0nezuLVcvDWc292wty/U5vfLea5PHGr3vDJJV3+sP+jKUfbHYSIkp7RRGxlHAcsWI9NI4SgLCVEmPskGEk4Mcq" +
                    "hKxyFAJkAYRAAwYUAvQMiNyreoPnhUipuwoQiUNZQ+SSQkQr7DQXXDnBsRM6YCPIvjHaQgUlk8YaogEFUjqDlVHCKhMAwp0ARiKAz4BIf+u5jUiuugoACSNZ" +
                    "4+OS4gMEN0twa2Xwo7QSxHttiGdEGIURw9RSZKzAhHEKVIARtDD8C5BGCBpF+Bn42L3XrwbbzwuRpvYqoCQPZg2USwoURmiAgfcea2I4xRJK4Cw2wW54B4k2" +
                    "IpgWZTjwNvhaXEDEDWWKSaS5AYidBZTvj8bPj5NSeSVgEseyRsklRYlF3AMIhKAKEC2NJyFAAciLABOFJANChRKcOB3idCAgl9QCzymBBOMAmfaD9u+Mxvd7" +
                    "g3XM/pwIAWuEtIwQQbkg3GAhkKOeKk8gotxTGa2FYJ55qR3H0GHKkTLYAqA1osZSSCSR7cfsNULWIfsaIV1ACGHEUQel59oEH4t4ASkiyCCngDMo+FZWEa+M" +
                    "DlGJoFbR8A5yGt4yyRjSrYfsNUDWEfsaHh2AB5EcI+0YhdAqp6QU0AUHC1BIpWZKQhhCEi0JwJiEpwBaS61kkjpCLAMvImKvEbIO2Nc46QpOuA8hOsSSEak5" +
                    "Y4QYjSSVhoXQBAktUICMAFxBoYFmWmoa/oeQAoUIsBC9gIC9gck6Xl+D5AIgOfzoh09//CWcHjye7n/59B9++/STg3a32hEUBnpkQ5zBGfdQSsmNltQbSD1W" +
                    "kjvntDbeK+6AQQAzKxhzTGgGHKWtQCWxK6IjIqUFilYBXPOy+bzYgvTKI4uejKxffPX1Lz+b7n+RCZ0++uLwya8P/+7Hh3/75dPHP53uf/K73376u9/8aPro" +
                    "n6f7H3/9+f8cfvVxU7hVCFJuFdbKOB3ifWiks8AIz5X0Njh1kBsjmWcaQUeoCq6c9QBTy72FTEiC3BkQnCTWXQyDLZC0Ehg8eDQ9+Pc05R9PD/5ruv+fbRm6" +
                    "l4zGR59PH/3Tq4YiPhGK//fprw6/+qzdPRuOgcaUEUYo4xBrZqQR2ghisdfhAZPQYKe9FAIahoHHgFkrVAiplIEtbW4ugKoFklbDsP06IeqL5WzbApySobsU" +
                    "eEIvwGlER/H0+1/81eFPfvUCvEVJhLXeQ4aM9SLEVdAhzR3UhLgQanEGQYifwudgJyw2QArLNIAMGO5RiMPOQNXt0f3bF8NUCwSthqH66fTgX5ZAEwINlshL" +
                    "tk016d2wTc9g6Y0bhz95fPjVz1vFkWOMaawc91oxCbhXVBIVrENwoTDE3HkmgGAKYsc4CD4YdUZRIrkwErEzTxScaydoAUctELQaOPok26a2DBO6NI4e7JJh" +
                    "ghc+y8a9V0wbjrGAlGoTghDuOOQKQs+ZZs5BDAiikkqprFFCWGeR9eHHM4naMEw749FWtbsb6Lx5rQV6Vscu/e+nf/3Nkw9T8Pz5N08+asdMsatoplpbJ7ww" +
                    "wKAPEYgJ4QhUEAkJLXEEEUKYDbIdHC8odfC/MKSeUmiUEgw4BZkVwBKvsG9lnXAeYS0QtFLLhAFk7awUErpeg18KW+ii2MLxrI8ENDpaTEuOMFYcOQF9CF8s" +
                    "kQYjIbgxQhBFgOJE2xC+eMuxV/HAXevYaoGgVcNWa/BiV32LC50Kr9/v/+jwl4+f/uBnGWfJbygj+ObJx9kKTx/9x9ePf3j49x9mZ6IBIlx6JV4ir6TDhjNi" +
                    "PQt2QslgVqBBEPLgt9lgOFz4QrQ2ARnSUuWM0Ya5EAoJT89xhPXdi6OxBapWw5f8x+n+Z9P9f5vu/2sbm2P4XL4kvJqYPG+ghi+8guigswYT6CS2EHgkhaIa" +
                    "CG25B1TomJAknLXYM84lxk5j66IkY2OklrDtQK0FelYqUMs/7YdrEK3jtSV8ygvjTAfZ9RYAzJwQCglMkEPAO+5CYEQNCj/EaiSBQBCTYCW49BBw6DjmXhrX" +
                    "uk/ZAkEr6FO25lbKtVv5nCbswuGadUBrgT01JIhvTA0M/zOJhXfUE0gp1hRwiaDAmAMjIMYScQ+9RAIa4ds2YS3Qs2omrH37JVbPfN1KOOoNRnfrNT4AAKde" +
                    "GCLjFpBhEIYPiDkvaPCLSPCAqISKGgc180wF0WLUKi4th1Sma3fMvWrrezujfhDbYUDXKFLw5xDhTUw2Awsl34QIbkIgNiGE6SsA8R0V8d9YUKL4ibH0ErL8" +
                    "N5YHsLwLZecK8tQShCCVDJWkTJ/iC1C6Cn+bqhRvErFJyHz9+JfkvyC3FRsJhDIansjSA296gUHxzmhJD3nT06xpkccQ2ziF0FlDAGWOiFIJntiWTIXzX/hs" +
                    "Q+ejSLBZd/wIf09sIXYl5uvV0wRoMz6BCh9lfkvSLDNRJgSVwqD8v1iZ41T6hZJyvvnjNYEksyw0zfMMJdLnaJQ168JLmoeYxWzuO43fKdikbJOiVD0UCS/j" +
                    "dxBfU1JeE1kDo1SkmepQLcx0eBt7XBgPTS1n7sL8JDSc+o7FYekqfkbxh8hUkNa9JCryR5DoYKV4QwSam7t64A2jXuGoc+ckvaepbVoTK0UaLSst5JHH0mUM" +
                    "shktjxUTFzKmYqui/HRquJl6IjK9qB4+rIcP0ztUlAtN+lZmLsk0TJrlJLVRz3eZ5jgWmQbdmcnlqSVWJDk9BfFLlumkLvMcF8Gl9cBzF6yWhFgfzESkDDNz" +
                    "qiuDJbW0lpHQYgAylWImwaHInHA2Qk2bukmgiajRXUiJPODdGi6rBRaU93F0sAygfMUNTlkiIHVHsiWJXSURybzJsl6rKMo6BduMQprNbD0EUMiOndMyXArL" +
                    "3DYPpSy1axmXiS1ldmuHpjuozTo0j7H0TIryqeVVzgl18gIzLRntWb1lVUVrZVxgnJnVpYlNhIG6aDOsODI2j2BZ/8tyB6CMXIp5I0tBMWYFEB2bWtaokpnZ" +
                    "SBomVkjzVCYSzDRVYY6srVeW2NI2aNR4aTjSlUeJ5xwxWKigqRiXc19x40yKeSc/M6NpYMFt48VFjI59cUCbikVsm4q4eJQwFT/em6xbp5u8uLgo+/jJgRTF" +
                    "F81jOIfXea4ucTOausOzWy7xV3JnMwtkzYEZf56NnBacZQlrqZubH7HA3jRUII8N1mhpFTcMmYUAZLFW/pAbq2WRsMJbOPPKQS0JKLVDExyPQCaGHpBkIuXi" +
                    "gOZnW+AFH3omTdm1mi8GZ/HpfNx0tGSZx4bWWWAyL3EQ0bOYec6W5ls5LgTjC20XPM0N5OhcH5nl4yRxMRRtYuiFEO7ZwHZhyIwtRthgJqgIHj++eiTHxHMS" +
                    "zkvcOYPOmir4jAAeU6uMRaLyQ3iKRLPuah4RWXudCVLhA0/qMpDEsj7GtUqm8RWRxUnLtjJUYazEJhyUFvJnmhYmSjFZ/7D0w2OBWJ6Wt0HAWVbYonhXLKtp" +
                    "kj7LEu+x9CQWTr1QfBRDRQnP6Yk5pp0mN7WuKiF0aUDmEPRotH/1vBdB5gS/ljU8Wy6a8ft4nfXMIs7MHEXdeISzqyGURXusYb6G+SWKttdIv9xIp7xIRGSh" +
                    "KAPNkQ9NHMoPizVAhWcszUMcX+ZHYlUpkFieX+WpaOwOE2W1Npfhad5o6iKyvxa5TAxNLhQRhZjCclomPJKHjlnruiIG/eUvIr5cnK+GVK5xvsb5pd3/WgP+" +
                    "kgM+eUKRnWn5Jvox2QHKbo0ozIg8S9ohPhf1+NIyX6xOFznEZtuSsamsVtKmcuZTNiZMlnX/4lHh0jhvHCacGqSlZVI7TEUKQCx/NQH/KrZ+XzLSV0Iu10hf" +
                    "I/2y7RWvcX65cV7WFUg91rRiwWQ9jsTg2dINrVcscBkWSxKdKzZDnzmTqSlee4qkXs+ItVKB0jutPShRVoVZmsDSEaoP5LB6ckCNE3I1cf4Kjkm8XJyvhlSu" +
                    "cb7G+aU7xbgG+uUGeh5QFo3CGFwvKZDaJuRBy+JDUVK7S7zsMLB6qzazk9frIYWvpCwLM164yLJvVa8gl1ekkJF3Qogoexp5W4PVfmYsTOttE3pFQ/SXeF7s" +
                    "VbnuKyGXa6SvkX75DsKukf58SJ+XsppZ9VnPWTZZvZNy5DzqaYdDT07VOjXJ7tjZYTNiAJ8d+QXNKcNOJV9drTSkq5lrdtVSVq5SZsMVy766YnmEVysj6Qql" +
                    "ml2lBNFXlXx1zlAwOoJNhogor05IVKlDxNIiAo0rQep0+7m7FppULZhfi2OC/prIOpGppDjVPmd4JchcfkrOATouej3t6oRZsFHyhE64saDOXJql1KQYhJZU" +
                    "leMbOTogWUrBWZ7aMfk5i/dgzPnlpyWIHb0B40hX6KTwsnbb50tKerxSOJKydyQ378Ri8vhggKM8tYsjeHbV4gI8YO3ygF2QB4vdvzo2JL/k4myQdVYjnRPJ" +
                    "cw18scPnHTgopvxoOMtLWhg6Y32el93z2Ul4Vr426+qzk/CsPldXL9rn03X5SFyM00Wt+VEJunNIkKP7HDbw9Daf1cub5iU8Z7VfwcrKfDkGUO/s5wMAkUJQ" +
                    "3MpntR+XeULKggadm0RwZuiMFxJE0wJdfUnP0ZTVxTzOuSTCI7agLKukW2rEnA6b3X7TWAo6sxTPLHKdc22BNtcElTt14npBnSoJS8YnWxSDi7UmwKylo0w5" +
                    "kjDM6suKaAO6c431+DWQuSuDUnorf8amLtk4b2o0iaY4wefYPNgjCZjHVc8GHR13FU9grSycaTjEjrdsp935s5DCTJZgwPz1PtFBeP6GTlF3YGld14QtR5Uc" +
                    "AucoO8v5rtc6I7IRWnLINSIaL28+BxqeVVTMXVtVu4uwyfeN81LTexFFcLSjpRFyQoNn5EiXJPP6ArM84mXl9uRBn9DhUsJ94d4EX0wiJy+hw+dXsSf3Nkvs" +
                    "T945boZzhkySZxtprrk7X89kIc5Jufp0TlcuWs9z4mHJSSGLbS1l0hZvOAAz5/G4TUU+N/BsKE6GZ7bxcw5Dc8FAdn2kmKdOzjyS59rHEKiJ317lDtzRfbCy" +
                    "4kwaVytUv/CFEbWw42Y77Li5nDdo8yF1DY4sI4m5s03PBkj0vFA4sdULXfV4ch/syALFGZiK4ps5XnMqTzUom37NBZblOG3eHoR1xMBmuyFlHZGWe0ryB9ac" +
                    "viElT2eWNZPyd2JYwOrQgZd9RVZvJOYQISfO5qNDOY+2nDmCJbYgss7xrTcVWZ3sU4Kb+UGeLD7z15bc2tzwg9Fo/NZob9Jcq/qtcW+Ybo19f8OMhtv9+ftc" +
                    "Z1fKHvMyXX+7k+5b3dy43nvv7Um+5ThdeHq3yrUuelUtfBF31daUfXAr3qLq++9V28f0UV+r6hRzwefShHFrrRDECc2EAtYbooTyzHtvDCRKIYewJMobD8Mj" +
                    "rClHPP42lcyZ8Wh7b2ty81r+jc43ryVWvfYg3kGfbwW+GzkfntcF06uj1N+KvN2J7DPVYFCmInwqt/oOJ+PRoJmuYXX/Yf3ljfhbBdO4qv7de3mIb06q+5G4" +
                    "MoHhW76NN3//49u7k97WoJqVqJ/Ml7pRhQksF+l+tzfYi5fWxpufM1Gwi0ShLhKFu0gU6SJRtItEsS4SxbtIlOgiUbKTyrObKr2TOh12UqnDTmp12Em1Djup" +
                    "12EnFTvspGaHnVTtsJO6HXVSt6Nu+uud1O2ok7oddVK3o07qdtRJ3Y46qdtRJ3U76qRux53U7biTuh13czGmk7odd1K3407qdtxJ3Y47qdtxJ3U77qRuJ53U" +
                    "7aSTup10UreTbq60d1K3k07qdtJJ3U46qdtJJ3U76aRupyfq9vc3bNoNzg/j51K7lJr77bRv7OSG0qZvFau/S+Pv1mx+g/CX04OfT/f/Lf+C0PQLS4/+QtHZ" +
                    "993yy1PjPi/YTH9u5f3ru3HTu/l9opO9xRJ/kmn40zS8V8XNTtok2kmbRDtpk2g3t387aZNoJ20S7aRNop20SbSTNol1Mt5gndTtrJO6nXVSt7NO6nbWzbM9" +
                    "ndTtrJO6nXVSt7NO6nbeSd3OO6nbeSd1O++kbued1O28k7qdd/PgZid1O++kbued1O3iBa0liVexliRe9VqS6KRNEp20SaKTNkl00iaJTtok0UmbJLqZTdBJ" +
                    "myQ6aZNkJ+MN2UndLjup22UndbvspG6XndTtspO6XXZSt8tuporJljx7BGrX/vZod3fOrT/4afDsX6hDH8fwKhz6W2XAk2p38u723vBuNRrGBOlhf9LvDWy1" +
                    "9b23+39Z5eyI2K25FwcYM+HKg5Ij3nDqb1IM9OH04AfTg/+a7v9mevCz8GEjjns8Gt7Npd+pxuNef5i/fLe/28+p5h+kVOqS5m2A0YgKoz0TUnKNjGLSCyex" +
                    "BYhAhZnXQHjiPXJQaY6EolZJ4bWlCMiYqn1jPAqTsduP3QZh6Y23c3q2mgTmfO/6KOa6v9OPhEc73JtUd0fjhyVvOyZ4Bztoq92tcX8np9Vv+N5gcDtU/YOt" +
                    "0NbGogC597aqavvb1YNqUL5PquF2LSR2bxzIsNW4/6A36T+oSvZ4k9QOIAaCWysF9VqJMCptiGdEGIURwzSMyViBCeMUKMEVtDD8C5BGCBpFeCDmjf7kzeGD" +
                    "/nCrf7s/6E8eXt/NohOnd/devxpEit/cfXMYhXSSM/wbMnLCewtUXPQ+gRdxncD1wHLbH4ce525DyA8zQFMf8cERGShSnLn1bpnkG71hNYhyf7d65+HOPJyq" +
                    "ybgX64cx5/pJP9QjK8XS/QvpBHoQx51qPOlXeWLe6o37EY5wpkpMJG6g7kyqsR8HWnaP6prjnzb0vx3kK77emO7/9/TgyfTgowS83niyt7NYtTyc1dy7vQCA" +
                    "d3rju9Ukjzd+TVck2OpOfzh3xURUwefFEjqKpd//4q8Of/Krp//w26efHJwGJXhhKHHvFdOGYywgpUGCkeOOwyCu0HOmmXNBzAmikkqprFFCWGeR9eHHM4nO" +
                    "gNLt0f3bZwNpeRpWAUjRcB78y/9++tffPPlwuv/F9ODzb558tASkEGgAxU4GFHgRgEoj6QyanrFMhx/98OmPv4TTg8fB/p4NqgvbJ4igMDCIZ7DJnHEPZbDJ" +
                    "RkvqDaQeK8mdc1qbIPbcAYMAZlYw5pjQDDhKz7JP3x+NzzZPLRCxEqiaU+7Pi6SY2322aQJXwjS9RDcvKH2KgdIQK2IM4pJ4rRlFON5TZAEAwjhLVHC/gs1A" +
                    "XBOuJScQWaI5Mw6cAaM7QarORtHyNKwCiiKv1i5el4zShT09GPQ+N8xaqCASElriCCKEMBscLkEIlNoLgoNtoDTEJ0oEK6BgsAnAEq+wb8coLU/EShml4O21" +
                    "Y5diqsvaLi2BJ3RRPGGkDJbBOkAcYhTJEcaKIyegZzBIqzQYCcGNEYIoAhQn2poQ6FiOvYJAiFbw1AIRq4an1iDF1q7eS3P1CJEcI+0YhdAqp6QU0HkRJBtS" +
                    "qZmSEDrjtSQAYxKeAmgttZJJ6gixDLSzotcCFesVvYu6e2sstY0lRhx1UHqujQSOeAEpIsggp4AzSHFhVXCmjMaECWoVDe8gp+Etk4whfQaW+ltnR00tkLAK" +
                    "QAqsWqOoBRTRk528X3z19S8/S6ulkdDpoy8On/z68O9+fPi3Xz59/NPp/ie/++2nv/vNj6aP/nm6//HXn//P4VcfN4VbBR3lVmGtjNMWAGiks8AIz5X0NliO" +
                    "EPMYyTzTCDpCVbAX1gNMLfcWMiEJcmeALt+6fCbuWqBiNVbSH00P/j3N8sdpY/U/20LgSwbgo8+nj/6pu7tSCyHW7/d/dPjLx09/8LMca6VNjKI/vnnycd4S" +
                    "mD76j68f//Dw7z/MOxsN/ODS8JPIK+mw4YxYz4JtUZI4Ag2CkEPPrVc4GB5ItDYhUpKWKmeMNsxRIoWn51gqfPd8EVkLlKwGBP9xuv9ZPNy9/69tLMHjc21m" +
                    "wbUv2a4vyQgVmHjvsSaGUyxhcOcsDpbFeBdk2AiAsDIceItscOsg4oYyxSTS3ADEWlnkaIGIlQjLIrPWi/AtIAmfiKT/+/RXh1991i6EOAYaUxZiIso4xJoZ" +
                    "aUQQWmKx1+EBk9Bgp70UAhqGgceAWSuUFFCFqKidpY0WqFiNhcJfJxH8YjlrtIAiSC+LU4gutzHiPmh+iCUjUnPGCDEaSSoNowogoQUSEgjAFRQaaKalpuF/" +
                    "CClQiAALUSvGqAUi1sZovbjR5qG/CwNJEmGt95AhY70IIgxdcJUc1IS4INWcQRBENXz2VgY/C0hhmQaQAcM9CiLfxqG/FmhYnUN/7ZzyI1f6lN9zwwdfGD4O" +
                    "OmswgfF4PQQeSaGClhfacg9osAE8OE7OBseKcS5x8KqwdTG+x8ZInQ7bLw+f5WlYqTOz+af9k7MQrY/OvhzvTmmvKCKWEo7jkTvroQnyGkJ7K4MAY4cMIxAr" +
                    "q1CI8h2FAFkAIdCABd8KnLWEdy8mCp2FqhZoWAVURV6tFxq6dDrpwjZKBxvgLQCYOSEUEpggh4B3wcMKIYlB4YfYELkAgSAmwlMuPQQcOo5DhGNcK7FSC0Ss" +
                    "4Omk1g4oyfWhipdmnbQKLhQXXDnBsQuxPcJB5Ruj40lWyaSxhmhAgZTOYGWUsMoEu8CdAEYigFs4VNECCetDFWvb9CpRZFGITuK5U6oA0dJ4EtwtgLwIIquQ" +
                    "ZCBYCe45cRpZAgTkklrgOSWQYAwRbyOjowUa1hkd6/W7V4sjQbkg3GAhkKOeKk8gotxTGfc9BfPMSx28KOgw5fGIeHDCtA7+lqWQSCLbiJVaoGEdK61x1OJC" +
                    "3oVTOKwLEimwp4aEoCQeMgj/M4mFDwJNIKVYU8AlggJjDoyAGMtgG6CXSEAjfBsLeS3QsGoLee2v4omruIiHTwTUGzcOf/L48Kuft2qTHGNMY+W414pJwL2i" +
                    "kiigQyhvY3qS80wAwRTEjnEQjAEN/hYlkosQnbAzjwqdyya1QMNqgOmTfMqhLZuELs25V3he/Nyq5T1dcvRWtRvmqh11/PZobxynaPl7TSLIG8paSJ1tKFv6" +
                    "bogFylpIQpxRtuwoFyhrYQGyoWzpUb6w2Vz6fOQLo2zpwzILlLVwT1BD2dLHDxYoa2E3tqFsaQ0UVZvf2w2qbnaJW/zUDgd9735/8HDeyZldSNAo8OT5pjsH" +
                    "c78t3EUz63cuYJn5Kk3XeWdivu/l5fm0vuFpfbegGU/rG53Wdwu5QLO+a4N8DMNLatJ8z8unQJx3tucSM+YIaOGw67mGXs7ezvXcwqmmEwEGTgFYCwroxH7x" +
                    "Kf22YGBPm2p8moC34KqdOGZ0yphbCDLOJV0p4onKvARYwnvggyApEhr3INhMYxS0gEAbDLvmBAQxCuxnDhAfZtxoEEiKiiYSq6JspUhqZzza3tuavLYdQ7lq" +
                    "+7U7/fcme2nh/Duj8f14nWoMct6qHvR3c7j4INmnnWqrn16GuKgdla7bAUzekM7k3bz2AN68FmfsD5O83Ly2kLrVGOylVVSclxxZJI58a9yLkd/78Wrd7f58" +
                    "2DaLHI95mecjxVjxNtf34u23VbYp8VOuddGIFL6IkLSm7INbyb3ov1dtH9NHfdWQU8xBhDRhQe/YYN1cmHWhgPWGKKE8C86qMZAoFeQVB/R4E3DpTUAsR1wc" +
                    "kdSb17KNuXktseq1JI854D+txDFxdQgTx0mIl7eOkQ3frcYZIvCDGBAPt0f37bj3/TpAjt/jBcZV/k1Lb1U7g97Dt1PImb83CLsT+fnaeG84CUFtpv6tvUGc" +
                    "/6h1nomq4yXc92KU2zwA9ZP5+UXPPpxRHMPfncEoLwFk4AeIV9vX+4NB/0Y1fjtE58PmiuRv9fbuVul9ava4hRqWLjkeBjIf9AY1YRA9G/3HZ0cC+3iTc0Ma" +
                    "+uD/Ab6PfeoiNAEA";break;
                case "tuned-package": encoded=
                    "H4sIAAAAAAACA+1dWY9cx3X+K8G8ZgTWvuitVkuI6RCSYGQhITRnLsmOm92Dnh5ajCDAnEkCKYqNQEgcBTaIKEhgWALi2HmwBTPKf0l7aOVJfyG13dvdsw/7" +
                    "krzsaXBIdt9by6lT5ztLVZ2a9zdMb7y9u/H6n7+/oSaT3tb3ro8m/dHwnf79auN1sBleT6q7o/HD/GW0O9l4HW5u2Gp3a9zfiSU3Xt/wvcHgdqj6B1uhrY3N" +
                    "DTeo7lfDSari3tuqqu1vVw+qQfk+qYbbb1VbowfV+KHdG/eHd2017j/oTfoPQpd3eoPdanPjjd7uvdAyxIBioDTEihiDuCRea0YRRlgSCwAQxlmiBPVUh9ea" +
                    "cC05gcgSzZlxIBDzRn/y5vBBf7jVv90f9CcPr+8mOt7cDs3f6Y+rUOTN3TeHu5PecOLu3Km2Jg0Rf1Q9jIXK6G5eewBvXvt2/35/Um3fvFbqXh9t9+/0q/HG" +
                    "64GB29v9yJLeIDcU2Xprc0OPhnu7tne/d7dquKi2/2Jvd9JwKb+9vjeY9HcGqTXYsPGPA6PG/e1Qd7g3GGxu/Fk1HuWJSGR+EGl4UNlAzlaej1wsP4zD2srd" +
                    "xgdHJvc7vfghseHdMnc3esNqYO71hnerdx7uNJ3eqIbVZNyLtcOIc+0bo/5uVY+rFBt9PxKP49vxaKcaT/pV5vdbvXFgfhpXPfkmkjZQdybV2I8DJbt1K3WB" +
                    "k5821L8dxCa+3pju//f04Mn04KNA/9uT3niyt7NYtTyc1dy7vSDX7/TGd6tJHm/8ujdMUnmnP+zPWPrBZichorRXFBFLCccRK9ZD4ygBCFspMcYOGUYCfqxC" +
                    "yCpHIUAWQAg0YEAhQM+ByL2qN3hWiJS6qwCROJQ1RF5RiGiFneaCKyc4dkIHbATZN0ZbqKBk0lhDNKBASmewMkpYZQJAuBPASATwORDpbz2zEclVVwEgYSRr" +
                    "fLyi+ADBzRLcWhn8KK0E8V4b4hkRRmHEMLUUGSswYZwCFWAELQz/AqQRgkYRfg4+du/1q8H2s0Kkqb0KKMmDWQPlFQUKIzTAwHuPNTGcYgklcBabYDe8g0Qb" +
                    "EUyLMhx4G3wtLiDihjLFJNLcAMTOA8r3R+Nnx0mpvBIwiWNZo+QVRYlF3AMIhKAKEC2NJyFAAciLABOFJANChRKcOB3idCAgl9QCzymBBOMAmfaD9u+Mxvd7" +
                    "g3XM/owIAWuEtIwQQbkg3GAhkKOeKk8gotxTGa2FYJ55qR3H0GHKkTLYAqA1osZSSCSR7cfsNULWIfsaIV1ACGHEUQel59oEH4t4ASkiyCCngDMo+FZWEa+M" +
                    "DlGJoFbR8A5yGt4yyRjSrYfsNUDWEfsaHh2AB5EcI+0YhdAqp6QU0AUHC1BIpWZKQhhCEi0JwJiEpwBaS61kkjpCLAPPI2KvEbIO2Nc46QpOuA8hOsSSEak5" +
                    "Y4QYjSSVhoXQBAktUICMAFxBoYFmWmoa/oeQAoUIsBA9h4C9gck6Xl+D5BIgOfzoh09//CWcHjye7n/59B9++/STg3a32hEUBnpkQ5zBGfdQSsmNltQbSD1W" +
                    "kjvntDbeK+6AQQAzKxhzTGgGHKWtQCWxK6IjIqUFilYBXPOy+azYgvTKI4uejqxffPX1Lz+b7n+RCZ0++uLwya8P/+7Hh3/75dPHP53uf/K73376u9/8aPro" +
                    "n6f7H3/9+f8cfvVxU7hVCFJuFdbKOB3ifWiks8AIz5X0Njh1kBsjmWcaQUeoCq6c9QBTy72FTEiC3DkQnCTWXQ6DLZC0Ehg8eDQ9+Pc05R9PD/5ruv+fbRm6" +
                    "F4zGR59PH/3Ty4YiPhWK//fprw6/+qzdPRuOgcaUEUYo4xBrZqQR2ghisdfhAZPQYKe9FAIahoHHgFkrVAiplIEtbW4ugKoFklbDsP06IeqL5WzbApySoXsl" +
                    "8ISeg9OIjuLp97/4q8Of/Oo5eIuSCGu9hwwZ60WIq6BDmjuoCXEh1OIMghA/hc/BTlhsgBSWaQAZMNyjEIedg6rbo/u3L4epFghaDUP10+nBvyyBJgQaLJEX" +
                    "bJtq0rthm45h6Y0bhz95fPjVz1vFkWOMaawc91oxCbhXVBIVrENwoTDE3HkmgGAKYsc4CD4YdUZRIrkwErFzTxRcaCdoAUctELQaOPok26a2DBN6ZRw92CXD" +
                    "BC99lo17r5g2HGMBKdUmBCHcccgVhJ4zzZyDGBBEJZVSWaOEsM4i68OPZxK1YZh2xqOtanc30HnzWgv0rI5d+t9P//qbJx+m4Pnzb5581I6ZYlfRTLW2Tnhp" +
                    "gEEfIhATwhGoIBISWuIIIoQwG2Q7OF5Q6uB/YUg9pdAoJRhwCjIrgCVeYd/KOuE8wlogaKWWCQPI2lkpJHS9Br8UttBlsYXjWR8JaHS0mJYcYaw4cgL6EL5Y" +
                    "Ig1GQnBjhCCKAMWJtiF88ZZjr+KBu9ax1QJBq4at1uDFrvoWFzoTXr/f/9HhLx8//cHPMs6S31BG8M2Tj7MVnj76j68f//Dw7z/MzkQDRLj0SrxEXkmHDWfE" +
                    "ehbshJLBrECDIOTBb7PBcLjwhWhtAjKkpcoZow1zIRQSnl7gCOu7l0djC1Sthi/5j9P9z6b7/zbd/9c2NsfwhXxJeDUxedFADV96BdFBZw0m0ElsIfBICkU1" +
                    "ENpyD6jQMSFJOGuxZ5xLjJ3G1kVJxsZILWHbgVoL9KxUoJZ/2g/XIFrHa0v4lJfGmQ6y6y0AmDkhFBKYIIeAd9yFwIgaFH6I1UgCgSAmwUpw6SHg0HHMvTSu" +
                    "dZ+yBYJW0Kdsza2Ua7fyGU3YpcM164DWAntqSBDfmBoY/mcSC++oJ5BSrCngEkGBMQdGQIwl4h56iQQ0wrdtwlqgZ9VMWPv2S6ye+bqVcNQbjO7Wa3wAAE69" +
                    "METGLSDDIAwfEHNe0OAXkeABUQkVNQ5q5pkKosWoVVxaDqlM1+6Ye9XW93ZG/SC2w4CuUaTgzyHCm5hsBhZKvgkR3IRAbEII01cA4jsq4r+xoETxE2PpJWT5" +
                    "bywPYHkXys4V5KklCEEqGSpJmT7FF6B0Ff42VSneJGKTkPn68S/Jf0FuKzYSCGU0PJGlB970AoPindGSHvKmp1nTIo8htnEGobOGAMocEaUSPLUtmQrnv/B4" +
                    "QxejSLBZd/wIf09tIXYl5uvV0wRoMz6BCh9lfkvSLDNRJgSVwqD8v1iZ41T6uZJysfnjNYEksyw0zfMMJdLnaJQ168JLCjZpKCXirIQPlMd/Iw1B8OJ3mP6l" +
                    "sVxsg7JYNj4C5UV6yuumcKwRBFZm7sXmUtNzo4lCzZIQANSUS02FejSBLjbDEl0y9Z2gF9uiuUeYXicCYx2aepB5kKlGFNc0woY7L22oqBAXxydTqdRhbJqn" +
                    "YcBMBiqjzn2m2gn6sQipe0+P03ApT3VygY6MNWMFpBqkEBk+kzTU2eBR/FzeJhYUTRJLJIxTWAabSsaeZSFhhvqXP7FJUqWoxU2UrjP9hWJUBDXWFoUEkpRA" +
                    "rjXjVizH6pEnklBXRpr7yrBMxWiR6plSLwKM0kgyCFn6IIrw5kFGrpAC53roWYZJZ6YVl+mTaYoLzIroEdkANZMNy2CkKPNLZBFdWHdL6jZqa0KTKe3KcFmt" +
                    "hov+jM5GmqVUCRYySjdZ6fJmsnnqQNZamzTNyaLiCqS7MdZATJ6zWrkUe1GkFc86KJqVyOL3ENFILJ9JO63FIgtDsUSiW9oYFgmUeR5x0cnFjBZ6SAKgKJMo" +
                    "M0pxeVtzqR5hbixxoDuobdqTvNGjtU/RAI8WyrIdygDPc5xFvJnE+ENK93nuG8RKPOdxwUJDJprLua+48RrFvDefaWoaWPDPePEFowdfPM2mYpbdWUVcXEeY" +
                    "ip/sNtat001emICyM588RVGczjyGC7iXF+oSN6OpOzy/5RJoJb81s0DWHJjx53iItOAVS1jP1dz8iAX2pqECeWJURkuruGHIzNcni7Xyh9xYLYmEFd7CmfsN" +
                    "aklAqZ0o9+AoXGKMAUkmUi4OaH62BS4Dh4t+fNZAC8XgLBCdD5COlizz2NA6i0DmJQ4ieh4zL9jSfCsnxVp8oe2Cp7mBHJ3rI7N8kiQuxpxNsLwQqx2PYBeG" +
                    "zNhiKA1mgorgyeOrR3JC4CbhvMRdMLqsqYLHBPCEWmUsEpUfwlPISeTCIyKL3qMJUuEDT55YIImxWufS4rdxUDvudQAWqsRiyRvkoLSQP9O0AlGKyfqHpR8e" +
                    "C8TytLwNAs6a8CBZKdYEOylazu4ES09i4exR46MYKkp4Tk/MMe2Y3BTe4aN1FnmGikAXVwHOL36cwNqjZqEowRKEl16SToPH1guupEskyByoajnGszWnhXk5" +
                    "QYkcWwmambqod48wlyUvgGUtiNMHEOUqS2COMFn9L0lMzUKeH2YHiTdVMjRQLcy4+Msc1D4DKH0xWlyu0hQoKGAJRGW9A80EmyXvi7ECB5oB1QA+a6a1CnnZ" +
                    "KuTVRPpLCeKfBefimXG+GlLZJZyX4J8lFooy0DL/iUP5YV6rzWqTyFrXsZofiVWlQGJ5fpWnogQ2qX0iZ2V41qKpi8h+XpYRMzFZTzZrFYXltEx41s8UXUWc" +
                    "v/hlyRcL8tUQyTXI1yB/tfbP1ih/xVGefB+W4xeYPJfs8mRHRhRmRJ4lUYnPRT2+tGRYIpp5DrHaH8xNZRmrQ6Hsc5VpyXEQLe5Vbpw3LhIuEVNumdQuUpEC" +
                    "EMtfRZS/hK3jFwzzlRDKNczXMH/1dpzXSH+1kV4WEshszZAlr6eMg9ULlby4eM1yZR5WWRTNRqQeeokQYXGgeB0CNraE0bn1UlJ8zmb2Cmt53RGqfU5WT06z" +
                    "RkquqNv+wg9bvFiYr4ZQrmG+hvmrdgxyjfNXG+dlhwXOMQbX6wmkPrpF6lAQFuenOEu8bCjkD5HBiZ28XgwpfCVFABkvXGTZs+Jlmbe8IoWMvPGRN3byOgxl" +
                    "tROVC9N6l4ReSXP+wg6dvSynfSVkco3yNcpfsWO0a5g/G8znRaxmVn1adJZ4Vp8DPHKi9azjpadndZ2Zj3fi7LAZMYDPDg2D5pxih/K0rlDy0lXKSbtaOS5X" +
                    "KRHiSuVqXal8wyuWvXSVEtOuTirpy8nTumDAFz2+JplElFen5LTUgWBpEYHGqyR1Cv7c/QtNVhfMr+cSB44RWec8lWyo2rkMrwSZS2XJ6UInxahnXacwiypK" +
                    "StEptxjUSU6z7JsUbNAmH+KkRo4OSJZScJbSdkIqz+LdGHMO+Fm5ZEdvxTjSFTotiKz98/mSktbic2JwW2cjHUnjO7WYPNnr5yhP7eIIjq9NXIIHrF0esEvy" +
                    "YLH7l8eGZNUvzwZZJ0DSOZG80MAXO3zWgYOixo7GrbxkkKFzVuB52R2fHW1n5Wuzcj472s7qY3P1snw+PMdqHymvohcXT5ZN+vmsl7IHj8pRvLwpXuJwVjuN" +
                    "rKy9l23+euc+b/BHCud8s6P8lnlCysoFnZtEcG6MjBdySdMyXH1xz9Hs1sWUz7l8wyO2oKyfpJtrxJwOm92I01gKOrMUx5ayLriIQJurg8o9O3FhAM6S0KBs" +
                    "clGfsTUBZi0dZcqR3GJWX2BEG9BdaKwnL3bMXSOUMmH5MZu6ZOO8qdHkpOKcwndSyuyRXM2TqmeDjk66nofR7N/QOQ6xky3bWfcALWQ7kyUYMH/lT3QQnr2h" +
                    "M9QdWFrXNUdtjyo5BC5QdpYeXi9qRmQjtOSQa0Q0Xt58ujQ8r6iYu8qqdhdhkxoc56Wm9zKK4GhHSyPklAbPSacu+ej1pWZ5xMvK7emDPqXDpYT70r0Jvphv" +
                    "Tl5Ah8+uYk/vbXYHQPLOcTOcc2SSHG+kufruYj2ThTgnpfXTOV25aD0viIclJ4UstrWUSVu8DAHMnMeTtg753MCzoTgdntnGzzkMzV0E2fWRYp46OfNInmnD" +
                    "QqAmfnt5+2xHt7twHhZpHK1Q89I3S9Sijptdr5Nmct6czQfUNTSyhCTWzjY2GxjRiwLh1FYvdfnj6X2wI8sT5yAqCm/meM0pMjc7ZHalZd66o3V0wOrFRVKn" +
                    "vjSJrGW3rzmrg8puXNk+hLXzL2fHckprKdwoeTW4JNCW0IDOsmpzkk9Ory/xCCthS1narVeqGSsJtETMDfJ08Zm/3+TW5oYfjEbjt0Z7k+ai1W+Ne8N0j+z7" +
                    "G2Y03O7P3/A6u2T2hJfpQtyddAPr5sb13ntvT/K9x+kK1LtVrnXZy2vh87i9tqbsg1vxXlXff6/aPqGP+qJVp5gLHpcmjFtrhSBOaCYUsN4QJZRn3ntjIFEK" +
                    "OYQlUd54GB5hTTni8ferZM6MR9t7W5Ob1/LveL55LbHqtQfxVvp8T/DdyPnwvC6YXh2l/lbk7U5kn6kGgzIV4VO553c4GY8GzXQNq/sP6y9vxN8zmMZV9e/e" +
                    "y0N8c1Ldj8SVCQzf8v28+fsf396d9LYG1axE/WS+1I0qTGC5Wve7vcFevMY23gWdiYJdJAp1kSjcRaJIF4miXSSKdZEo3kWiRBeJkp1Unt1U6Z3U6bCTSh12" +
                    "UqvDTqp12Em9Djup2GEnNTvspGqHndTtqJO6HXXTX++kbked1O2ok7oddVK3o07qdtRJ3Y46qdtRJ3U77qRux53U7bibizGd1O24k7odd1K3407qdtxJ3Y47" +
                    "qdtxJ3U76aRuJ53U7aSTup10c6W9k7qddFK3k07qdtJJ3U46qdtJJ3U7PVW3v79h025wfhg/l9ql1Nzvq31jJzeUNn2rWP1dGn/bZvM7hb+cHvx8uv9v+VeG" +
                    "pl9hevRXjM6+75Zfpxr3ecFm+nMr71/fjZvezW8YnewtlviTTMOfpuG9LG520ibRTtok2kmbRLu5/dtJm0Q7aZNoJ20S7aRNop20SayT8QbrpG5nndTtrJO6" +
                    "nXVSt7Nunu3ppG5nndTtrJO6nXVSt/NO6nbeSd3OO6nbeSd1O++kbued1O28mwc3O6nbeSd1O++kbhfPaS1JvIy1JPGy15JEJ22S6KRNEp20SaKTNkl00iaJ" +
                    "Ttok0c1sgk7aJNFJmyQ7GW/ITup22UndLjup22UndbvspG6XndTtspO6XXYzVUy25NkjULv2t0e7u3Nu/cFPg2f/XB36OIaX4dDfKgOeVLuTd7f3hner0TAm" +
                    "SA/7k35vYKut773d/8sqZ0fEbs29OMCYCVcelBzxhlN/k2KgD6cHP5ge/Nd0/zfTg5+FDxtx3OPR8G4u/U41Hvf6w/zlu/3dfk41/yClUpc0bwOMRlQY7ZmQ" +
                    "kmtkFJNeOIktQAQqzLwGwhPvkYNKcyQUtUoKry1FQMZU7RvjUZiM3X7sNghLb7yd07PVJDDne9dHMdf9nX4kPNrh3qS6Oxo/LHnbMcE72EFb7W6N+zs5rX7D" +
                    "9waD26HqH2yFtjYWBci9t1VV29+uHlSD8n1SDbdrIbF740CGrcb9B71J/0FVssebpHYAMRDcWimo10qEUWlDPCPCKIwYpmFMxgpMGKdACa6gheFfgDRC0CjC" +
                    "AzFv9CdvDh/0h1v92/1Bf/Lw+m4WnTi9u/f61SBS/Obum8MopJOc4d+QkRPeW6DisvcJPI/rBK4Hltv+OPQ4dxtCfpgBmvqID47IQJHizK13yyTf6A2rQZT7" +
                    "u9U7D3fm4VRNxr1YP4w510/6oR5ZKZbuX0gn0IM47lTjSb/KE/NWb9yPcIQzVWIicQN1Z1KN/TjQsntU15z8tKH/7SBf8fXGdP+/pwdPpgcfJeD1xpO9ncWq" +
                    "5eGs5t7tBQC80xvfrSZ5vPFruiLBVnf6w7krJqIKviiW0FEs/f4Xf3X4k189/YffPv3k4CwowUtDiXuvmDYcYwEpDRKMHHccBnGFnjPNnAtiThCVVEpljRLC" +
                    "OousDz+eSXQOlG6P7t8+H0jL07AKQIqG8+Bf/vfTv/7myYfT/S+mB59/8+SjJSCFQAModjqgwPMAVBpJZ9B0zDIdfvTDpz/+Ek4PHgf7ez6oLm2fIILCwCCe" +
                    "wSZzxj2UwSYbLak3kHqsJHfOaW2C2HMHDAKYWcGYY0Iz4Cg9zz59fzQ+3zy1QMRKoGpOuT8rkmJu9/mmCVwJ0/QC3byg9CkGSkOsiDGIS+K1ZhTheE+RBQAI" +
                    "4yxRwf0KNgNxTbiWnEBkiebMOHAOjO4EqTofRcvTsAooirxau3hdMkqX9vRg0PvcMGuhgkhIaIkjiBDCbHC4BCFQai8IDraB0hCfKBGsgILBJgBLvMK+HaO0" +
                    "PBErZZSCt9eOXYqpLmu7tASe0GXxhJEyWAbrAHGIUSRHGCuOnICewSCt0mAkBDdGCKIIUJxoa0KgYzn2CgIhWsFTC0SsGp5agxRbu3ovzNUjRHKMtGMUQquc" +
                    "klJA50WQbEilZkpC6IzXkgCMSXgKoLXUSiapI8Qy0M6KXgtUrFf0LuvurbHUNpYYcdRB6bk2EjjiBaSIIIOcAs4gxYVVwZkyGhMmqFU0vIOchrdMMob0OVjq" +
                    "b50fNbVAwioAKbBqjaIWUERPd/J+8dXXv/wsrZZGQqePvjh88uvDv/vx4d9++fTxT6f7n/zut5/+7jc/mj765+n+x19//j+HX33cFG4VdJRbhbUyTlsAoJHO" +
                    "AiM8V9LbYDlCzGMk80wj6AhVwV5YDzC13FvIhCTInQO6fOvyubhrgYrVWEl/ND349zTLH6eN1f9sC4EvGICPPp8++qfu7kothFi/3//R4S8fP/3Bz3KslTYx" +
                    "iv745snHeUtg+ug/vn78w8O//zDvbDTwg0vDTyKvpMOGM2I9C7ZFSeIINAhCDj23XuFgeCDR2oRISVqqnDHaMEeJFJ5eYKnw3YtFZC1QshoQ/Mfp/mfxcPf+" +
                    "v7axBI8vtJkF175ku74kI1Rg4r3HmhhOsYTBnbM4WBbjXZBhIwDCynDgLbLBrYOIG8oUk0hzAxBrZZGjBSJWIiyLzFovwreAJHwqkv7v018dfvVZuxDiGGhM" +
                    "WYiJKOMQa2akEUFoicVehwdMQoOd9lIIaBgGHgNmrVBSQBWionaWNlqgYjUWCn+dRPCL5azRAoogfVWcQvRqGyPug+aHWDIiNWeMEKORpNIwqgASWiAhgQBc" +
                    "QaGBZlpqGv6HkAKFCLAQtWKMWiBibYzWixttHvq7NJAkEdZ6Dxky1osgwtAFV8lBTYgLUs0ZBEFUw2dvZfCzgBSWaQAZMNyjIPJtHPprgYbVOfTXzik/cqVP" +
                    "+T0zfPCl4eOgswYTGI/XQ+CRFCpoeaEt94AGG8CD4+RscKwY5xIHrwpbF+N7bIzU6bD98vBZnoaVOjObf9o/OQvR+ujsi/HulPaKImIp4TgeubMemiCvIbS3" +
                    "MggwdsgwArGyCoUo31EIkAUQAg1Y8K3AeUt492Ki0HmoaoGGVUBV5NV6oaFLp5MubaN0sAHeAoCZE0IhgQlyCHgXPKwQkhgUfogNkQsQCGIiPOXSQ8Ch4zhE" +
                    "OMa1Eiu1QMQKnk5q7YCSXB+qeGHWSavgQnHBlRMcuxDbIxxUvjE6nmSVTBpriAYUSOkMVkYJq0ywC9wJYCQCuIVDFS2QsD5UsbZNLxNFFoXoJJ47pQoQLY0n" +
                    "wd0CyIsgsgpJBoKV4J4Tp5ElQEAuqQWeUwIJxhDxNjI6WqBhndGxXr97uTgSlAvCDRYCOeqp8gQiyj2Vcd9TMM+81MGLgg5THo+IBydM6+BvWQqJJLKNWKkF" +
                    "Gtax0hpHLS7kXTqFw7ogkQJ7akgISuIhg/A/k1j4INAEUoo1BVwiKDDmwAiIsQy2AXqJBDTCt7GQ1wINq7aQ1/4qnriKi3j4VEC9cePwJ48Pv/p5qzbJMcY0" +
                    "Vo57rZgE3CsqiQI6hPI2pic5zwQQTEHsGAfBGNDgb1EiuQjRCTv3qNCFbFILNKwGmD7JpxzasknolTn3Ci+Kn1u1vKdLjt6qdsNctaOO3x7tjeMULX+vSQR5" +
                    "Q1kLqbMNZUvfDbFAWQtJiDPKlh3lAmUtLEA2lC09yuc2m0ufj3xulC19WGaBshbuCWooW/r4wQJlLezGNpQtrYGiavN7u0HVzS5xi5/a4aDv3e8PHs47ObML" +
                    "CRoFnjzfdOdg7reFu2hm/c4FLDNfpek670zM9728PJ/VNzyr7xY041l9o7P6biEXaNZ3bZBPYHhJTZrvefkUiIvO9lxixhwBLRx2vdDQy9nbuZ5bONV0KsDA" +
                    "GQBrQQGd2i8+o98WDOxZU43PEvAWXLVTx4zOGHMLQcaFpCtFPFGZlwBLeA98ECRFQuMeBJtpjIIWEGiDYdecgCBGgf3MAeLDjBsNAklR0URiVZStFEntjEfb" +
                    "e1uT17ZjKFdtv3an/95kLy2cf2c0vh+vU41BzlvVg/5uDhcfJPu0U23108sQF7Wj0nU7gMkb0pm8m9cewJvX4oz9YZKXm9cWUrcag720iorzkiOLxJFvjXsx" +
                    "8ns/Xq273Z8P22aR4wkv83ykGCve5vpevP22yjYlfsq1LhuRwucRktaUfXAruRf996rtE/qorxpyijmIkCYs6B0brJsLsy4UsN4QJZRnwVk1BhKlgrzigB5v" +
                    "Ai69CYjliIsjknrzWrYxN68lVr2W5DEH/GeVOCGuDmHiOAnx8tYxsuG71ThDBH4QA+Lh9ui+Hfe+XwfI8Xu8wLjKv2nprWpn0Hv4dgo58/cGYXciP18b7w0n" +
                    "IajN1L+1N4jzH7XOsag6XsJ9L0a5zQNQP5mfX3T84YziGP7uDEZ5CSADP0C82r7eHwz6N6rx2yE6HzZXJH+rt3e3Su9Tsyct1LB0yfEwkPmgN6gJg+h49B+f" +
                    "HQns403ODWnog/8HANdkhzQ0AQA=";break;
                default:throw new System.ArgumentOutOfRangeException(nameof(name));
            }
            using(var source=new System.IO.MemoryStream(System.Convert.FromBase64String(encoded)))
            using(var zip=new System.IO.Compression.GZipStream(source,System.IO.Compression.CompressionMode.Decompress))
            using(var target=new System.IO.MemoryStream()){zip.CopyTo(target);return target.ToArray();}
        }
        [TestCase("catalog")] [TestCase("default")] [TestCase("variants")] [TestCase("package")] [TestCase("tuned-package")]
        public void 旧カード定義と完全輸送パッケージの全bytesと定義hashを維持する(string name)
        {
            var bytes=Golden(name);
            if(name=="default"){
                var dto=PortableCheckpointJson.Deserialize<PortableCardDefinition>(bytes);CollectionAssert.AreEqual(bytes,PortableCheckpointJson.Serialize(dto));Assert.Throws<System.ArgumentException>(()=>dto.Restore());
            }else if(name=="catalog"||name=="variants"){
                var dtos=PortableCheckpointJson.Deserialize<PortableCardDefinition[]>(bytes);CollectionAssert.AreEqual(bytes,PortableCheckpointJson.Serialize(dtos));
                foreach(var dto in dtos)Assert.AreEqual(dto.Hash,CollectionDisplay.DefinitionHash(dto.Restore()));
            }else{
                var package=PortableCheckpointJson.Deserialize<PortableBattlePackage>(bytes);CollectionAssert.AreEqual(bytes,PortableCheckpointJson.Serialize(package));Assert.IsNotNull(package.Restore(out var catalog));Assert.IsNotNull(catalog);
            }
        }
        [Test] public void 現行の旧登録二十二定義も固定bytesと一致し新規品目を混ぜない()
        {
            var legacy=PortableCheckpointJson.Deserialize<PortableCardDefinition[]>(Golden("catalog"));var hashes=new System.Collections.Generic.HashSet<string>(legacy.Select(c=>c.Hash));var factory=new SimpleCardFactory();
            var cards=factory.CollectionCards.Concat(factory.FusionCards).GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First()).Where(c=>hashes.Contains(CollectionDisplay.DefinitionHash(c))).OrderBy(CollectionDisplay.DefinitionHash,System.StringComparer.Ordinal).ToArray();
            Assert.AreEqual(22,cards.Length);CollectionAssert.AreEqual(Golden("catalog"),PortableCheckpointJson.Serialize(cards.Select((c,i)=>PortableCardDefinition.Capture("legacy/"+i,c)).ToArray()));
        }
    }
}

namespace Olca.Tests.Battle
{
    public sealed class CardAttackPowerBasisTests
    {
        private static Card Untuned(Card card)=>(Card)typeof(Card).GetMethod("CreateUntunedDefinitionCopy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(card,null);
        private static Card Dynamic(int bps=10000,int power=0,float multiplier=1,int bonus=0,string id="ratio")=>new Card(id,"割合テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,power,"未登録の計算検証定義",targetType:Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile,modifier:new CardModifier{DamageMultiplier=multiplier,BonusDamage=bonus},isInstantEffect:true,poiseDamage:0,attackPowerBasisPoints:bps);
        private static BattleSystem Battle(int attack=13)=>new BattleSystem(new PlayerCharacter(100,new Vector2Int(2,0),attackPower:attack),new List<Enemy>{new Enemy("fixture","テスト",1000,ElementType.None,new Vector2Int(3,0))},new Deck(),customGaugeSpeed:0);
        [TestCase(13,10000,0,1f,0,13)] [TestCase(13,15000,5,1.5f,7,43)]
        [TestCase(3,3333,0,1f,0,0)] [TestCase(3,3334,0,1f,0,1)]
        [TestCase(0,10000,5,2f,7,17)] [TestCase(7,5000,0,1.5f,1,5)]
        [TestCase(16777217,10000,0,1f,0,16777216)]
        public void 基準分整数切捨て固定加算既存float倍率Bonusの順を維持する(int actor,int bps,int power,float multiplier,int bonus,int expected)
        {
            var card=Dynamic(bps,power,multiplier,bonus);string hash=CollectionDisplay.DefinitionHash(card);Assert.AreEqual(expected,card.GetFinalPower(actor));
            var copy=card.ResolveAttackPower(actor);Assert.AreEqual(0,copy.AttackPowerBasisPoints);Assert.IsTrue(copy.IsEffectCopy);Assert.AreEqual(expected,copy.GetFinalPower());
            Assert.AreEqual(hash,CollectionDisplay.DefinitionHash(card));Assert.AreEqual(bps,card.AttackPowerBasisPoints);Assert.AreEqual(power,card.Power);Assert.IsFalse(card.IsEffectCopy);
        }
        [Test] public void 旧零基準は負の固定値を含め既存の切捨てと表示を維持する()
        {
            var card=Dynamic(0,-31,1.5f,7);Assert.AreEqual(-39,card.GetFinalPower());Assert.AreEqual(-39,card.GetFinalPower(200));Assert.AreEqual("-39",CollectionDisplay.PowerText(card));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void 不正な割合定義と使用者攻撃力を拒否する(int kind)
        {
            if(kind==0)Assert.Throws<ArgumentOutOfRangeException>(()=>Dynamic(-1));
            if(kind==1)Assert.Throws<ArgumentException>(()=>Dynamic(10000,-1));
            if(kind==2)Assert.Throws<ArgumentException>(()=>new Card("bad","不正",1,CardRarity.Normal,CardCategory.Defense,ElementType.None,CardSeries.None,0,"",attackPowerBasisPoints:10000));
            var valid=Dynamic();Assert.Throws<ArgumentOutOfRangeException>(()=>valid.GetFinalPower(-1));Assert.Throws<ArgumentOutOfRangeException>(()=>valid.ResolveAttackPower(-1));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void 基準加算補正最終加算の範囲外と非有限値をwrapせず拒否する(int kind)
        {
            var card=kind==0?Dynamic(int.MaxValue):kind==1?Dynamic(10000,int.MaxValue):kind==2?Dynamic(multiplier:float.PositiveInfinity):kind==3?Dynamic(multiplier:float.NaN):kind==4?Dynamic(multiplier:1e30f):Dynamic(bonus:int.MaxValue);
            int actor=kind==0?int.MaxValue:13;Assert.Throws<OverflowException>(()=>card.GetFinalPower(actor));Assert.Throws<OverflowException>(()=>card.ResolveAttackPower(actor));Assert.IsFalse(card.IsEffectCopy);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void 使用時倍率の不正値と範囲外を定義へ反映しない(int kind)
        {
            var card=Dynamic();string hash=CollectionDisplay.DefinitionHash(card);float factor=kind==0?-1:kind==1?float.NaN:kind==2?float.PositiveInfinity:1e30f;
            if(kind==3)Assert.Throws<OverflowException>(()=>card.WithPowerMultiplier(factor,13));else Assert.Throws<ArgumentOutOfRangeException>(()=>card.WithPowerMultiplier(factor,13));
            Assert.AreEqual(hash,CollectionDisplay.DefinitionHash(card));
        }
        [Test] public void 未解決の固定威力APIと自己派生を拒否し明示解決後だけ既存派生を使える()
        {
            var card=Dynamic(15000,5,1.5f,7);Assert.Throws<InvalidOperationException>(()=>card.GetFinalPower());Assert.Throws<InvalidOperationException>(()=>card.WithPowerMultiplier(2));Assert.Throws<InvalidOperationException>(()=>card.CreateSelfChainCopy(50));
            var resolved=card.ResolveAttackPower(13);var chain=resolved.CreateSelfChainCopy(50);Assert.AreEqual(12,chain.GetFinalPower());Assert.AreEqual(0,chain.AttackPowerBasisPoints);Assert.IsTrue(chain.IsEffectCopy);Assert.AreEqual(43,resolved.GetFinalPower());
            var concentrated=card.WithPowerMultiplier(2,13);Assert.AreEqual(86,concentrated.GetFinalPower());Assert.AreEqual(0,concentrated.Modifier.BonusDamage);Assert.AreEqual(1,concentrated.Modifier.DamageMultiplier);
        }
        [TestCase(100000000,50,50000000)] [TestCase(1000000000,200,2000000000)]
        public void 解決後の自己派生もint積で溢れず正しい整数値を返す(int actor,int percent,int expected)
        {var card=Dynamic().ResolveAttackPower(actor);Assert.AreEqual(expected,card.CreateSelfChainCopy(percent).Power);}
        [Test] public void 自己派生の最終範囲超過は元copyを変更せず拒否する()
        {var card=Dynamic().ResolveAttackPower(1500000000);Assert.Throws<OverflowException>(()=>card.CreateSelfChainCopy(200));Assert.AreEqual(1500000000,card.Power);}
        [Test] public void 定義のclone調律と時間コピーは割合を保持し原本を変更しない()
        {
            var card=Dynamic();string hash=CollectionDisplay.DefinitionHash(card);var clone=card.Clone();clone.Modifier.BonusDamage=7;Assert.AreEqual(13,card.GetFinalPower(13));Assert.AreEqual(20,clone.GetFinalPower(13));
            var timing=card.WithAttackTiming(new PlayerAttackTiming(3,9,4));Assert.AreEqual(10000,timing.AttackPowerBasisPoints);Assert.AreEqual(3,timing.StartupFrames);
            var material=new Card("tuning","調律",1,CardRarity.Normal,CardCategory.Tuning,ElementType.None,CardSeries.None,0,"",modifier:new CardModifier{BonusDamage=7});var tuned=card.ApplyTuning(material);
            Assert.AreEqual(10000,tuned.AttackPowerBasisPoints);Assert.AreEqual(20,tuned.GetFinalPower(13));Assert.AreEqual(hash,CollectionDisplay.DefinitionHash(Untuned(tuned)));Assert.AreEqual(hash,CollectionDisplay.DefinitionHash(card));Assert.Throws<NotSupportedException>(()=>card.ApplyTuning(Dynamic(id:"material")));
        }
        [Test] public void 非零は輸送とinstance定義参照で保持し零と欠損は旧bytesのまま扱う()
        {
            var source=Dynamic();var dto=PortableCardDefinition.Capture("ratio/v1",source);var bytes=PortableCheckpointJson.Serialize(dto);StringAssert.Contains("\"AttackPowerBasisPoints\":10000",System.Text.Encoding.UTF8.GetString(bytes));
            var restored=PortableCheckpointJson.Deserialize<PortableCardDefinition>(bytes).Restore();Assert.AreEqual(10000,restored.AttackPowerBasisPoints);Assert.AreEqual(13,restored.GetFinalPower(13));Assert.AreEqual(dto.Hash,CollectionDisplay.DefinitionHash(restored));
            var zero=PortableCardDefinition.Capture("zero",Dynamic(0));var missing=PortableCheckpointJson.Serialize(zero);StringAssert.DoesNotContain("AttackPowerBasisPoints",System.Text.Encoding.UTF8.GetString(missing));
            var explicitZero=System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(missing).Insert(1,"\"AttackPowerBasisPoints\":0,"));var round=PortableCheckpointJson.Deserialize<PortableCardDefinition>(explicitZero);CollectionAssert.AreEqual(missing,PortableCheckpointJson.Serialize(round));Assert.AreEqual(0,round.Restore().AttackPowerBasisPoints);
            Assert.AreNotEqual(CollectionDisplay.DefinitionHash(source),CollectionDisplay.DefinitionHash(Dynamic(0)));dto.AttackPowerBasisPoints=0;Assert.Throws<ArgumentException>(()=>dto.Restore());dto.AttackPowerBasisPoints=-1;Assert.Throws<ArgumentOutOfRangeException>(()=>dto.Restore());
        }
        [Test] public void 割合と調律履歴をcheckpointへ保存復元し値hash改ざんは元状態を変えない()
        {
            var source=Dynamic();var factory=new SimpleCardFactory();var cards=factory.CreateStarterDeck().GetAllCards().ToArray();cards[0]=source;var deck=new Deck(cards.ToList());var map=DungeonConfig.CreateTestDungeon(6,1,2);var session=DungeonCheckpointSession.CreateDefault(map,deck);
            var grant=new TuningGrantDefinition("ratio-grant",new[]{new TuningEffectDefinition("power",1,new Dictionary<int,CardModifier>{[1]=new CardModifier{BonusDamage=7}})});session.Definitions.RegisterGrant("ratio-grant",grant);
            var manager=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new SeededBattleRandom(270),new MockLogger(),checkpoints:session);var state=session.Store.Read();Assert.IsNotNull(state);
            int index=Array.FindIndex(state.Cards,c=>c.DefinitionKey=="initial/v1/"+CollectionDisplay.DefinitionHash(source));Assert.GreaterOrEqual(index,0);var tuned=TuningEvaluator.Evaluate(source,grant).ResultCard;state.Cards[index]=session.Definitions.CaptureKnownCard(state.Cards[index].InstanceId,tuned);
            var encoded=PortableCheckpointJson.Encode(state);Assert.IsTrue(CheckpointCodec.TryRestore(PortableCheckpointJson.Decode(encoded),session.Definitions,out var loaded,out var error),error);var restored=loaded.Deck.GetAllCards().Single(c=>c.Id=="ratio");Assert.AreEqual(10000,restored.AttackPowerBasisPoints);Assert.AreEqual(1,restored.TuningCount);Assert.AreEqual(20,restored.GetFinalPower(13));
            StringAssert.DoesNotContain("AttackPowerBasisPoints",System.Text.Encoding.UTF8.GetString(PortableCheckpointJson.Serialize(state.Cards[index])));
            state.Cards[index].ValueHash=CollectionDisplay.DefinitionHash(Dynamic(0));Assert.IsFalse(CheckpointCodec.TryRestore(state,session.Definitions,out _,out _));Assert.AreEqual(10000,source.AttackPowerBasisPoints);Assert.AreEqual(0,source.Power);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void 解決済みcopyはclone時間変更調律解除を経ても保存登録加工へ戻せない(int variant)
        {
            var raw=Dynamic();var copy=raw.ResolveAttackPower(13);var fixedValue=Dynamic(0,13);
            if(variant==0){copy=copy.Clone();fixedValue=fixedValue.Clone();}if(variant==1){copy=copy.WithAttackTiming(new PlayerAttackTiming(3,9,4));fixedValue=fixedValue.WithAttackTiming(new PlayerAttackTiming(3,9,4));}if(variant==2){copy=Untuned(copy);fixedValue=Untuned(fixedValue);}
            Assert.IsTrue(copy.IsEffectCopy);Assert.AreEqual(CollectionDisplay.DefinitionHash(fixedValue),CollectionDisplay.DefinitionHash(copy));var defs=new CheckpointDefinitions("ratio-copy",DungeonConfig.CreateTestDungeon(6,1,2));defs.RegisterCard("fixed",fixedValue);
            Assert.Throws<ArgumentException>(()=>defs.RegisterCard("bad",copy));Assert.Throws<ArgumentException>(()=>defs.CaptureKnownCard("copy",copy));Assert.Throws<NotSupportedException>(()=>PortableCardDefinition.Capture("copy",copy));Assert.Throws<ArgumentException>(()=>new FusionDefinitionCatalog().RegisterCard(copy,"family","series",0));
            var factory=new SimpleCardFactory();Assert.IsFalse(new ExceedSystem(factory).Validate(copy,copy.Clone()).IsValid);Assert.Throws<NotSupportedException>(()=>factory.CreateExceeded(copy,ExceedLevel.Lv1));Assert.IsNotNull(FusionSystem.MaterialRoleError(new[]{copy,factory.CreateById("bomb")}));Assert.IsFalse(new TuningSystem().Validate(copy,factory.CreateById("tuning")).IsValid);var tuningSystem=new TuningSystem();Assert.IsTrue(tuningSystem.Execute(copy,factory.CreateById("tuning")).IsUndefined);Assert.IsFalse(tuningSystem.Execute(copy,null).IsUndefined);Assert.IsFalse(tuningSystem.Execute(copy,factory.CreateById("bomb")).IsUndefined);Assert.Throws<NotSupportedException>(()=>copy.ApplyTuning(factory.CreateById("tuning")));
        }
        [TestCase(false)] [TestCase(true)]
        public void 未接続の通常使用は先に拒否しカード集中予約動作を消費しない(bool asynchronous)
        {
            var battle=Battle();var card=Dynamic();battle.Player.SetConcentrateBuff(2);battle.CardUsage.EnqueueCards(new List<Card>{card});long generation=battle.CardUsage.SelectionGeneration;
            Assert.IsFalse(battle.CardUsage.CanUseCurrentCard);Assert.IsFalse(asynchronous?battle.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult():battle.CardUsage.TryUseCurrentCard());
            Assert.AreSame(card,battle.CardUsage.CurrentCard);Assert.AreEqual(1,battle.CardUsage.RemainingCount);Assert.AreEqual(generation,battle.CardUsage.SelectionGeneration);Assert.AreEqual(2,battle.Player.NextCardMultiplier);Assert.IsFalse(battle.HasPlayerAction);Assert.IsNull(battle.CardUsage.InFlightCard);Assert.AreEqual(1000,battle.Enemies[0].CurrentHp);
            Assert.Throws<NotSupportedException>(()=>new Olca.Core.Logic.CardEffects.BattleContext(battle,card));Assert.Throws<NotSupportedException>(()=>Olca.Core.Logic.CardEffects.CardEffectFactory.CreateEffect(card));Assert.Throws<NotSupportedException>(()=>new EnemyCardAction(card,100));
        }
        [Test] public void 明示解決したcopyは既存実効果へ届き集中とBonusを一度だけ適用する()
        {
            var source=PortableCardDefinition.Capture("ratio",Dynamic(15000,5,1.5f,7)).Restore();string hash=CollectionDisplay.DefinitionHash(source);var battle=Battle();battle.Player.SetConcentrateBuff(2);var resolved=source.ResolveAttackPower(battle.Player.AttackPower);battle.CardUsage.EnqueueCards(new List<Card>{resolved});
            Assert.IsTrue(battle.CardUsage.TryUseCurrentCard());Assert.AreEqual(914,battle.Enemies[0].CurrentHp);Assert.IsFalse(battle.Player.IsConcentrating);Assert.AreEqual(0,battle.CardUsage.RemainingCount);Assert.AreEqual(hash,CollectionDisplay.DefinitionHash(source));Assert.AreEqual(15000,source.AttackPowerBasisPoints);
        }
        [Test] public void 外界の旧ソードIDへ割合を置いても未対応使用は無消費で拒否する()
        {
            var factory=new SimpleCardFactory();var source=factory.CreateStarterDeck().GetAllCards().Select(c=>c.Id=="sword"?Dynamic(id:"sword"):c).ToArray();var owned=source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());
            using(var session=new OutsideExplorationSession()){Assert.IsTrue(session.TryInitializePlayer(()=>new PlayerCharacter(100)));Assert.IsTrue(session.TryInitializeCards(source,owned,new SeededBattleRandom(270),out _));for(int i=0;i<100&&!session.Cards.Hand.Any(c=>c.Id=="sword");i++)session.Cards.Reload();Assert.IsTrue(session.Cards.TrySelect(session.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword")));using(var lease=session.AttachPresentation()){long revision=session.Cards.HandRevision,gauge=session.Core.CardGauge.CurrentUnits;Assert.IsFalse(session.TryUseSelectedCard(session.Cards.SelectedCard,revision,out _));Assert.AreEqual(revision,session.Cards.HandRevision);Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);Assert.IsFalse(session.IsAttacking);}}
        }
        [Test] public void 使用者なしの詳細一覧は零の威力でなく式を表示する()
        {
            var card=Dynamic();Assert.AreEqual("コウゲキリョク100%",CollectionDisplay.PowerText(card));StringAssert.Contains("コウゲキリョク100%",CollectionDisplay.PerformanceText(card));StringAssert.Contains("コウゲキリョク100%",Olca.Presentation.CustomScreen.CustomDisplay.CardDetails(card));StringAssert.DoesNotContain("イリョク: 0",Olca.Presentation.CustomScreen.CustomDisplay.CardDetails(card));
        }
    }
}

namespace Olca.Tests.Battle
{
    public sealed class TrialInitialCardBattleTests
    {
        [TestCase(TrialInitialCardDefinitions.Shot)] [TestCase(TrialInitialCardDefinitions.Spear)]
        [TestCase(TrialInitialCardDefinitions.WideSword)] [TestCase(TrialInitialCardDefinitions.AirShot)]
        public void 新四品の通常使用は実命中から三フレームのヒットストップを要求する(string id)
        {
            var b=Battle271();b.ConfigureHitStop(Olca.Core.Logic.Battle.AttackHitStopSettings.Provisional);
            b.CardUsage.EnqueueCards(new List<Card>{new SimpleCardFactory().CreateById(id)});var use=b.CardUsage.TryUseCurrentCardAsync();
            for(int i=0;i<90&&!b.IsHitStopped;i++)b.TickFrame(16);
            Assert.AreEqual(987,b.Enemies[0].CurrentHp);Assert.AreEqual(3,b.HitStopRemainingFrames);
            Frames(b,70);Assert.IsTrue(use.GetAwaiter().GetResult());Assert.AreEqual(987,b.Enemies[0].CurrentHp);
        }
        private static Olca.Core.Logic.Battle.BattleSystem Battle271(int x=3,int y=0,int actor=13)
            =>new Olca.Core.Logic.Battle.BattleSystem(new PlayerCharacter(100,new Vector2Int(2,0),attackPower:actor),new System.Collections.Generic.List<Enemy>{new Enemy("first","first",1000,ElementType.None,new Vector2Int(x,y)),new Enemy("second","second",1000,ElementType.None,new Vector2Int(5,0))},new Deck(new System.Collections.Generic.List<Card>()),customGaugeSpeed:0);
        private static void Frames(Olca.Core.Logic.Battle.BattleSystem b,int count){for(int i=0;i<count;i++)b.TickFrame(16);}
        [TestCase(TrialInitialCardDefinitions.Shot,1)] [TestCase(TrialInitialCardDefinitions.Spear,2)]
        [TestCase(TrialInitialCardDefinitions.WideSword,2)] [TestCase(TrialInitialCardDefinitions.AirShot,1)]
        public void 明示仮登録は無印Normalと割合をPortable往復して旧定義と混ぜない(string id,int cost)
        {
            var f=new SimpleCardFactory();var c=f.CreateById(id);Assert.NotNull(c);Assert.AreEqual(cost,c.Cost);Assert.AreEqual(ExceedLevel.Normal,c.ExceedLevel);Assert.AreEqual(CardRarity.Normal,c.Rarity);Assert.AreEqual(ElementType.None,c.Element);Assert.AreEqual(10000,c.AttackPowerBasisPoints);Assert.AreEqual(13,c.GetFinalPower(13));Assert.IsTrue(TrialInitialCardDefinitions.IsRegistered(c));
            var dto=PortableCardDefinition.Capture("trial271",c);var restored=PortableCheckpointJson.Deserialize<PortableCardDefinition>(PortableCheckpointJson.Serialize(dto)).Restore();Assert.AreEqual(CollectionDisplay.DefinitionHash(c),CollectionDisplay.DefinitionHash(restored));Assert.IsTrue(TrialInitialCardDefinitions.IsRegistered(restored));
            Assert.AreNotSame(c,f.CreateById(id));Assert.IsFalse(f.FusionCards.Any(v=>v.Id==id));Assert.AreEqual(20,f.CreateStarterDeck().GetAllCards().Count); // 旧fixture入口は維持。
        }
        [TestCase(TrialInitialCardDefinitions.Spear,4,0)] [TestCase(TrialInitialCardDefinitions.WideSword,3,1)]
        public void 新近接は拡張範囲内の一体だけに割合威力で命中する(string id,int x,int y)
        {
            var b=Battle271(x,y);var c=new SimpleCardFactory().CreateById(id);b.CardUsage.EnqueueCards(new System.Collections.Generic.List<Card>{c});var use=b.CardUsage.TryUseCurrentCardAsync();Frames(b,5);Assert.AreEqual(1000,b.Enemies[0].CurrentHp);Frames(b,1);Assert.AreEqual(987,b.Enemies[0].CurrentHp);Assert.AreEqual(1000,b.Enemies[1].CurrentHp);Frames(b,12);Assert.IsTrue(use.GetAwaiter().GetResult());Assert.IsFalse(b.HasPlayerAction);Assert.AreEqual(0,b.CardUsage.RemainingCount);Assert.AreEqual(10000,c.AttackPowerBasisPoints);
        }
        [TestCase(TrialInitialCardDefinitions.Shot)] [TestCase(TrialInitialCardDefinitions.AirShot)]
        public void 直進弾は前隙後に発射して八論理frameで隣接に一回命中する(string id)
        {
            var b=Battle271();var c=new SimpleCardFactory().CreateById(id);b.Player.SetConcentrateBuff(2);b.CardUsage.EnqueueCards(new System.Collections.Generic.List<Card>{c});var use=b.CardUsage.TryUseCurrentCardAsync();Frames(b,6);Assert.AreEqual(1,b.DelayedEffects.ActiveEffectCount);Assert.AreEqual(1000,b.Enemies[0].CurrentHp);Frames(b,7);Assert.AreEqual(1000,b.Enemies[0].CurrentHp);Frames(b,1);Assert.AreEqual(974,b.Enemies[0].CurrentHp);Assert.AreEqual(1000,b.Enemies[1].CurrentHp);Assert.AreEqual(0,b.DelayedEffects.ActiveEffectCount);Frames(b,50);Assert.AreEqual(974,b.Enemies[0].CurrentHp);Assert.IsTrue(use.GetAwaiter().GetResult());Assert.IsFalse(b.Player.IsConcentrating);
        }
        [Test] public void 弾の時計はポーズで停止し空振りは六十frameで終了する()
        {
            var b=Battle271(2,1);b.Enemies[1].TakeDamage(1000,ElementType.None);var c=new SimpleCardFactory().CreateById(TrialInitialCardDefinitions.Shot);b.CardUsage.EnqueueCards(new System.Collections.Generic.List<Card>{c});var use=b.CardUsage.TryUseCurrentCardAsync();Frames(b,6);var flight=b.DelayedEffects.GetActiveEffects().Single();Assert.AreEqual(0,flight.Progress);b.Pause(Olca.Core.Logic.Battle.BattlePauseReason.User);Frames(b,100);Assert.AreEqual(0,flight.ElapsedDisplayFrames);b.Resume(Olca.Core.Logic.Battle.BattlePauseReason.User);Frames(b,59);Assert.AreEqual(1,b.DelayedEffects.ActiveEffectCount);Frames(b,1);Assert.AreEqual(0,b.DelayedEffects.ActiveEffectCount);Assert.AreEqual(1000,b.Enemies[0].CurrentHp);Assert.IsTrue(use.GetAwaiter().GetResult());
        }
        [TestCase(false)] [TestCase(true)]
        public void 偽の同IDと割合overflowは使用開始前にカードと集中を保持する(bool overflow)
        {
            var b=Battle271(actor:overflow?int.MaxValue:13);var good=new SimpleCardFactory().CreateById(TrialInitialCardDefinitions.Shot);var c=overflow?good:new Card(good.Id,"偽",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,0,"",attackPowerBasisPoints:10000);b.Player.SetConcentrateBuff(2);b.CardUsage.EnqueueCards(new System.Collections.Generic.List<Card>{c});Assert.IsFalse(b.CardUsage.CanUseCurrentCard);Assert.IsFalse(b.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());Assert.AreEqual(1,b.CardUsage.RemainingCount);Assert.AreEqual(2,b.Player.NextCardMultiplier);Assert.IsFalse(b.HasPlayerAction);
        }
        [TestCase(false)] [TestCase(true)]
        public void 偽固定威力の同IDは集中の有無に関わらず無消費拒否し直接copyでも実行しない(bool concentrate)
        {
            var b=Battle271();var c=new Card(TrialInitialCardDefinitions.Shot,"偽",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,999,"");if(concentrate)b.Player.SetConcentrateBuff(2);b.CardUsage.EnqueueCards(new System.Collections.Generic.List<Card>{c});Assert.IsFalse(b.CardUsage.CanUseCurrentCard);Assert.IsFalse(b.CardUsage.TryUseCurrentCard());Assert.IsFalse(b.CardUsage.TryUseCurrentCardAsync().GetAwaiter().GetResult());Assert.AreEqual(1,b.CardUsage.RemainingCount);Assert.AreEqual(concentrate?2:1,b.Player.NextCardMultiplier);Assert.Throws<NotSupportedException>(()=>Olca.Core.Logic.CardEffects.CardEffectFactory.CreateEffect(c.WithPowerMultiplier(2)));Assert.Throws<NotSupportedException>(()=>Olca.Core.Logic.CardEffects.CardEffectFactory.CreateEffect(c.ResolveAttackPower(13)));
        }
        [Test] public void 真正IDの不正modifierも使用開始前に拒否する()
        {var b=Battle271();var c=new SimpleCardFactory().CreateById(TrialInitialCardDefinitions.Shot);c.Modifier.AdditionalEffects.Add(null);b.CardUsage.EnqueueCards(new List<Card>{c});Assert.IsFalse(b.CardUsage.CanUseCurrentCard);Assert.IsFalse(b.CardUsage.TryUseCurrentCard());Assert.AreEqual(1,b.CardUsage.RemainingCount);Assert.IsFalse(b.HasPlayerAction);var unknown=new Card(null,"unknown",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,1,"");Assert.IsFalse(TrialInitialCardDefinitions.IsRegistered(unknown));Assert.IsFalse(TrialInitialCardDefinitions.IsResolved(unknown.WithPowerMultiplier(2)));}
        [TestCase(false)] [TestCase(true)]
        public void 新試用品の未登録時間を生成時に拒否し既存の時間注入は保つ(bool different)
        {
            var timings=new Dictionary<string,PlayerAttackTiming>{{TrialInitialCardDefinitions.Shot,new PlayerAttackTiming(different?3:6,12)}};
            if(different)Assert.Throws<ArgumentException>(()=>new SimpleCardFactory(cardTimings:timings));else Assert.IsTrue(TrialInitialCardDefinitions.IsRegistered(new SimpleCardFactory(cardTimings:timings).CreateById(TrialInitialCardDefinitions.Shot)));
            var old=new SimpleCardFactory(cardTimings:new Dictionary<string,PlayerAttackTiming>{{"sword",new PlayerAttackTiming(3,9)}}).CreateById("sword");Assert.AreEqual(3,old.StartupFrames);Assert.AreEqual(9,old.RecoveryFrames);
        }
        [TestCase(TrialInitialCardDefinitions.Shot)] [TestCase(TrialInitialCardDefinitions.Spear)]
        [TestCase(TrialInitialCardDefinitions.WideSword)] [TestCase(TrialInitialCardDefinitions.AirShot)]
        public void 新四品の調律個体は定義参照保存から復元し集中を一度だけ適用する(string id)
        {
            var f=new SimpleCardFactory();var source=f.CreateById(id);var cards=f.CreateStarterDeck().GetAllCards().ToArray();cards[0]=source;var deck=new Deck(cards.ToList());var map=DungeonConfig.CreateTestDungeon(6,1,2);var session=DungeonCheckpointSession.CreateDefault(map,deck);var material=f.CreateById("tuning");var grant=f.GetTuningDefinition(material);session.Definitions.RegisterGrant("initial-power271",grant);
            var manager=new DungeonBattleManager(map,new PlayerCharacter(100),deck,new SeededBattleRandom(271),new MockLogger(),checkpoints:session);var state=session.Store.Read();var tuned=new TuningSystem(f.GetTuningDefinition).Execute(source,material).ResultCard;Assert.NotNull(tuned);Assert.IsTrue(TrialInitialCardDefinitions.IsRegistered(tuned));int index=Array.FindIndex(state.Cards,c=>c.DefinitionKey=="initial/v1/"+CollectionDisplay.DefinitionHash(source));Assert.GreaterOrEqual(index,0);state.Cards[index]=session.Definitions.CaptureKnownCard(state.Cards[index].InstanceId,tuned);
            Assert.IsTrue(CheckpointCodec.TryRestore(PortableCheckpointJson.Decode(PortableCheckpointJson.Encode(state)),session.Definitions,out var loaded,out var error),error);var restored=loaded.Deck.GetAllCards().Single(c=>c.Id==id);Assert.AreEqual(23,restored.GetFinalPower(13));var battle=Battle271();battle.Player.SetConcentrateBuff(2);battle.CardUsage.EnqueueCards(new List<Card>{restored});var use=battle.CardUsage.TryUseCurrentCardAsync();Frames(battle,20);Assert.IsTrue(use.GetAwaiter().GetResult());Assert.AreEqual(954,battle.Enemies[0].CurrentHp);Assert.IsFalse(battle.Player.IsConcentrating);Assert.AreEqual(10000,restored.AttackPowerBasisPoints);Assert.AreEqual(0,source.TuningCount);
        }
    }
}

namespace Olca.Tests.Battle
{
    public sealed class TrialInitialCardOutsideTests
    {
        [TestCase(TrialInitialCardDefinitions.Shot)] [TestCase(TrialInitialCardDefinitions.Spear)]
        [TestCase(TrialInitialCardDefinitions.WideSword)] [TestCase(TrialInitialCardDefinitions.AirShot)]
        public void 探索の新四品も実命中でヒットストップし弾と攻撃の時計を止める(string id)
        {
            using(var s=Outside271(id,out _))using(var lease=s.AttachPresentation()){
                s.EnableTrialHitStop();Go(s,(TrialInitialCardDefinitions.IsShot(id)?14:17)*60000);
                Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out var error),error);
                for(int i=0;i<90&&!s.IsHitStopped;i++)s.TickTraversal(0);
                Assert.AreEqual(107,s.TrialTarget.CurrentHp);Assert.AreEqual(3,s.HitStopRemainingFrames);
                long frame=s.Core.Frame;Frames(s,3);Assert.AreEqual(frame,s.Core.Frame);Assert.IsFalse(s.IsHitStopped);
                Frames(s,1);Assert.AreEqual(frame+1,s.Core.Frame);Assert.AreEqual(107,s.TrialTarget.CurrentHp);
            }
        }
        private static OutsideExplorationSession Outside271(string id,out CheckpointDefinitions defs)
        {
            var f=new SimpleCardFactory();var source=f.CreateStarterDeck().GetAllCards().Select(c=>c.Id=="sword"?f.CreateById(id):c).ToArray();defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(source.ToList())).Definitions;
            var s=new OutsideExplorationSession();Assert.IsTrue(s.TryInitializePlayer(()=>new PlayerCharacter(100,attackPower:13)));Assert.IsTrue(s.TryInitializeCards(source,source.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new SeededBattleRandom(271),out var error),error);
            for(int n=0;n<100&&!s.Cards.Hand.Any(c=>c.Id==id);n++)s.Cards.Reload();Assert.IsTrue(s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id==id)));return s;
        }
        private static void Frames(OutsideExplorationSession s,int n){for(int i=0;i<n;i++)s.TickTraversal(0);}
        private static void Go(OutsideExplorationSession s,long target){for(int i=0;i<300&&s.Core.Position<target;i++)s.TickTraversal(1);Assert.AreEqual(target,s.Core.Position);}
        [TestCase(TrialInitialCardDefinitions.Shot)] [TestCase(TrialInitialCardDefinitions.Spear)]
        [TestCase(TrialInitialCardDefinitions.WideSword)] [TestCase(TrialInitialCardDefinitions.AirShot)]
        public void 新四品は探索選択から一回消費し割合威力を一体に適用する(string id)
        {
            using(var s=Outside271(id,out _))using(var lease=s.AttachPresentation()){
                Go(s,(TrialInitialCardDefinitions.IsShot(id)?14:17)*60000);long rev=s.Cards.HandRevision;Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,rev,out var error),error);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.AreEqual(13,s.AttackState.ResolvedPower);Frames(s,5);Assert.AreEqual(120,s.TrialTarget.CurrentHp);Frames(s,TrialInitialCardDefinitions.IsShot(id)?31:1);Assert.AreEqual(107,s.TrialTarget.CurrentHp);Assert.AreEqual(120,s.SecondTrialTarget.CurrentHp);Frames(s,70);Assert.AreEqual(107,s.TrialTarget.CurrentHp);Assert.IsEmpty(s.InitialShots);
            }
        }
        [TestCase(TrialInitialCardDefinitions.Shot,3)] [TestCase(TrialInitialCardDefinitions.Shot,8)] [TestCase(TrialInitialCardDefinitions.Shot,20)] [TestCase(TrialInitialCardDefinitions.Shot,36)]
        [TestCase(TrialInitialCardDefinitions.Spear,3)] [TestCase(TrialInitialCardDefinitions.Spear,8)] [TestCase(TrialInitialCardDefinitions.Spear,20)] [TestCase(TrialInitialCardDefinitions.Spear,36)]
        [TestCase(TrialInitialCardDefinitions.WideSword,3)] [TestCase(TrialInitialCardDefinitions.WideSword,8)] [TestCase(TrialInitialCardDefinitions.WideSword,20)] [TestCase(TrialInitialCardDefinitions.WideSword,36)]
        [TestCase(TrialInitialCardDefinitions.AirShot,3)] [TestCase(TrialInitialCardDefinitions.AirShot,8)] [TestCase(TrialInitialCardDefinitions.AirShot,20)] [TestCase(TrialInitialCardDefinitions.AirShot,36)]
        public void 前隙飛翔命中後の冷復元は消費と一度命中を保つ(string id,int saveAt)
        {
            using(var warm=Outside271(id,out var defs)){
                var lease=warm.AttachPresentation();Go(warm,(TrialInitialCardDefinitions.IsShot(id)?14:17)*60000);Assert.IsTrue(warm.TryUseSelectedCard(warm.Cards.SelectedCard,warm.Cards.HandRevision,out _));Frames(warm,saveAt);lease.Dispose();var runtime=warm.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(warm.Player);actor.Id=runtime.Id;
                var encoded=PortableCheckpointJson.Serialize(runtime);var data=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(encoded);Assert.AreEqual(runtime.Key(),data.Key());using(var cold=new OutsideExplorationSession()){
                    Assert.IsTrue(cold.RestoreSavedState(actor,data,null,defs));Assert.IsFalse(cold.RestoreSavedState(actor,data,null,defs));using(var view=cold.AttachPresentation()){Assert.IsFalse(cold.AdvancePlayerFrame());Frames(cold,90);Assert.AreEqual(107,cold.TrialTarget.CurrentHp);Assert.IsEmpty(cold.InitialShots);}var after=cold.CaptureRuntime(defs);Assert.AreEqual(1,after.TrialTarget.Hits);Assert.IsNull(after.InitialShots);
                }
            }
        }
        [Test] public void 発射後の行動中断と手札再装填は既存弾を消さず同じframeの再評価も進めない()
        {
            using(var s=Outside271(TrialInitialCardDefinitions.Shot,out _))using(var lease=s.AttachPresentation()){
                Go(s,14*60000);Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Frames(s,6);Assert.AreEqual(1,s.InitialShots.Count);long x=s.InitialShots[0].Position(s.Core.Frame);for(int i=0;i<20;i++)Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(x,s.InitialShots[0].Position(s.Core.Frame));s.Player.ApplyParalysis(60);s.Cards.Reload();Frames(s,30);Assert.IsNull(s.AttackState);Assert.AreEqual(107,s.TrialTarget.CurrentHp);Assert.IsEmpty(s.InitialShots);
            }
        }
        [TestCase(TrialInitialCardDefinitions.Shot,false)] [TestCase(TrialInitialCardDefinitions.AirShot,true)]
        [TestCase(TrialInitialCardDefinitions.Spear,true)] [TestCase(TrialInitialCardDefinitions.WideSword,true)]
        public void 空中可否は明示登録で判定し不許可は無消費(string id,bool allowed)
        {
            using(var s=Outside271(id,out _))using(var lease=s.AttachPresentation()){
                Go(s,14*60000);s.TickTraversal(0,true,true);Assert.IsFalse(s.Core.IsGrounded);long revision=s.Cards.HandRevision,units=s.Core.CardGauge.CurrentUnits;Assert.AreEqual(allowed,s.TryUseSelectedCard(s.Cards.SelectedCard,revision,out _));Assert.AreEqual(revision+(allowed?1:0),s.Cards.HandRevision);if(!allowed)Assert.AreEqual(units,s.Core.CardGauge.CurrentUnits);
            }
        }
        [Test] public void 旧攻撃保存の五欄は新欄nullでも完全な旧bytesを保つ()
        {
            const string old="[{\"CardIndex\":0,\"EffectApplied\":false,\"RecoveryFrames\":0,\"StartedAt\":0,\"StartupFrames\":0},{\"CardIndex\":3,\"EffectApplied\":false,\"RecoveryFrames\":12,\"StartedAt\":100,\"StartupFrames\":6},{\"CardIndex\":3,\"EffectApplied\":true,\"RecoveryFrames\":12,\"StartedAt\":100,\"StartupFrames\":6}]";
            var bytes=System.Text.Encoding.UTF8.GetBytes(old);var values=PortableCheckpointJson.Deserialize<OutsideAttackStateData[]>(bytes);CollectionAssert.AreEqual(bytes,PortableCheckpointJson.Serialize(values));Assert.IsTrue(values.All(v=>v.ResolvedPower==null));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void 不正弾は旧Sessionを部分上書きせず拒否する(int mode)
        {
            using(var s=Outside271(TrialInitialCardDefinitions.Shot,out var defs)){
                var lease=s.AttachPresentation();Go(s,14*60000);Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Frames(s,8);lease.Dispose();var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var invalid=runtime.Copy();if(mode==0)invalid.InitialShots[0].CardId="unknown";if(mode==1)invalid.InitialShots[0].Power=-1;if(mode==2)invalid.InitialShots[0].StartedAt=invalid.Core.Frame+1;if(mode==3)invalid.InitialShots[0].Height=long.MaxValue;
                using(var target=new OutsideExplorationSession()){target.TryInitializePlayer(()=>new PlayerCharacter(99));var before=target.Player;Assert.Throws<ArgumentException>(()=>target.RestoreSavedState(actor,invalid,null,defs));Assert.AreSame(before,target.Player);Assert.AreEqual(0,target.Core.Frame);}
            }
        }
        [TestCase(1,0,true)] [TestCase(1,1,false)] [TestCase(-1,0,true)] [TestCase(-1,1,false)]
        public void スピアの左右射程端一単位は判定を超えて命中しない(int facing,int outside,bool expected)
        {
            using(var s=Outside271(TrialInitialCardDefinitions.Spear,out _))using(var lease=s.AttachPresentation()){
                var state=s.Core.CaptureState();state.Position=OutsideExplorationSession.TrialTargetX-facing*(OutsideExplorationSession.TrialSwordRange*3/2+outside);state.Facing=facing;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{state});
                Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Frames(s,6);Assert.AreEqual(expected?107:120,s.TrialTarget.CurrentHp);Assert.AreEqual(facing<0&&!expected?107:120,s.SecondTrialTarget.CurrentHp);
            }
        }
        [TestCase(1,TrialInitialCardDefinitions.Shot)] [TestCase(-1,TrialInitialCardDefinitions.Shot)]
        [TestCase(1,TrialInitialCardDefinitions.AirShot)] [TestCase(-1,TrialInitialCardDefinitions.AirShot)]
        public void 射撃は左右とも経路上の先着一体だけに命中する(int facing,string id)
        {
            using(var s=Outside271(id,out _))using(var lease=s.AttachPresentation()){
                Go(s,(facing>0?14:22)*60000);if(facing<0)s.TickTraversal(-1);Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Frames(s,66);Assert.AreEqual(facing>0?107:120,s.TrialTarget.CurrentHp);Assert.AreEqual(facing<0?107:120,s.SecondTrialTarget.CurrentHp);Assert.IsEmpty(s.InitialShots);
            }
        }
        [TestCase(90000,true)] [TestCase(90001,false)] [TestCase(-90000,true)] [TestCase(-90001,false)]
        public void 固定高度の試用弾は上下端で一単位外れると標的へ当たらない(long height,bool hit)
        {
            using(var s=Outside271(TrialInitialCardDefinitions.AirShot,out var defs)){
                var lease=s.AttachPresentation();Go(s,14*60000);Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Frames(s,6);lease.Dispose();var runtime=s.CaptureRuntime(defs);runtime.InitialShots[0].Height=height;var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                using(var cold=new OutsideExplorationSession()){Assert.IsTrue(cold.RestoreSavedState(actor,runtime,null,defs));using(var view=cold.AttachPresentation()){Frames(cold,60);Assert.AreEqual(hit?107:120,cold.TrialTarget.CurrentHp);}}
            }
        }
        [Test] public void 空振り弾は五十九frameでは保存可能で六十frame目に消える()
        {
            using(var s=Outside271(TrialInitialCardDefinitions.Shot,out var defs)){
                var lease=s.AttachPresentation();Go(s,14*60000);s.TrialTarget.TakeDamage(120,ElementType.None);s.SecondTrialTarget.TakeDamage(120,ElementType.None);Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Frames(s,6+59);Assert.AreEqual(1,s.InitialShots.Count);lease.Dispose();var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){Assert.IsTrue(cold.RestoreSavedState(actor,runtime,null,defs));using(var view=cold.AttachPresentation()){Frames(cold,1);Assert.IsEmpty(cold.InitialShots);}}
            }
        }
        private sealed class FlyingItemStore:IClearReturnStore
        {
            public bool Fail=true;public ClearReturnProfile Value=new ClearReturnProfile{OutsideInventory=new OutsideInventoryData{Items=new[]{new OutsideItemCount{Id=OutsideRecoveryItemDefinition.Provisional.Id,Count=2}}}};
            public ClearReturnProfile Read(out string error){error=null;return Value.Copy();}
            public bool TryWrite(long revision,ClearReturnProfile plan,out string error){error=Fail?"flying item failure":null;if(Fail)return false;if(revision!=Value.Revision)return false;Value=plan.Copy();return true;}
        }
        [Test] public void 飛翔中の即時保存失敗は効果消費CDと弾を変えず再試行後coldでも一命中()
        {
            using(var s=Outside271(TrialInitialCardDefinitions.Shot,out var defs))using(var lease=s.AttachPresentation()){
                Go(s,14*60000);s.Player.SetHp(50);Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Frames(s,18);var before=PortableCheckpointJson.Serialize(s.InitialShots.ToArray());var store=new FlyingItemStore();var save=new ClearReturnSession(store);var item=OutsideRecoveryItemDefinition.Provisional;
                Assert.IsFalse(s.TryUseRecoveryItem(save,item,defs,out _));Assert.AreEqual(50,s.Player.CurrentHp);Assert.AreEqual(2,store.Value.OutsideInventory.Items[0].Count);Assert.AreEqual(0,s.ItemCooldownRemaining(item.Id));CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(s.InitialShots.ToArray()));
                store.Fail=false;Assert.IsTrue(s.TryUseRecoveryItem(save,item,defs,out var error),error);Assert.AreEqual(75,s.Player.CurrentHp);Assert.AreEqual(1,store.Value.OutsideInventory.Items[0].Count);Assert.AreEqual(item.CooldownFrames,s.ItemCooldownRemaining(item.Id));using(var cold=new OutsideExplorationSession()){Assert.IsTrue(cold.RestoreSavedState(store.Value.OutsidePlayerSave,store.Value.OutsideRuntimeSave,null,defs));using(var view=cold.AttachPresentation()){Frames(cold,60);Assert.AreEqual(107,cold.TrialTarget.CurrentHp);Assert.IsEmpty(cold.InitialShots);}}
            }
        }
        [TestCase(false)] [TestCase(true)]
        public void 未登録マップへ移した弾保存と復元は拒否し現個体を変更しない(bool capture)
        {
            using(var source=Outside271(TrialInitialCardDefinitions.Shot,out var defs)){
                var lease=source.AttachPresentation();Go(source,14*60000);Assert.IsTrue(source.TryUseSelectedCard(source.Cards.SelectedCard,source.Cards.HandRevision,out _));Frames(source,6);lease.Dispose();var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;var d=source.Core.Definition;var foreign=new OutsideMovementDefinition("foreign-initial271",d.Width,d.Start,d.SpeedPerSecond,d.LandingSurfaces.ToArray());var core=new OutsideExplorationCore(foreign,source.Core.TraversalProfile);runtime.Core.DefinitionKey=core.CaptureState().DefinitionKey;
                var field=typeof(OutsideExplorationSession).GetField("<Core>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Assert.NotNull(field);
                if(capture){typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(core,new object[]{runtime.Core});field.SetValue(source,core);var player=source.Player;Assert.Throws<ArgumentException>(()=>source.CaptureRuntime(defs));Assert.AreSame(player,source.Player);Assert.AreEqual(1,source.InitialShots.Count);}
                else using(var target=new OutsideExplorationSession()){field.SetValue(target,core);target.TryInitializePlayer(()=>new PlayerCharacter(99));var before=target.Player;Assert.Throws<ArgumentException>(()=>target.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(before,target.Player);Assert.AreEqual(0,target.Core.Frame);Assert.IsEmpty(target.InitialShots);}
            }
        }
    }
}

namespace Olca.Tests.Battle
{
    public sealed class InitialDeckLoadoutTests
    {
        private sealed class ProgressStore:IClearReturnStore,INewClearReturnStore
        {
            public ClearReturnProfile Value=new ClearReturnProfile();public bool FailBefore,FailAfter;public int Commits,Attempts;public bool IsNewProfile=>Commits==0;
            public ClearReturnProfile Read(out string error){error=null;return Value.Copy();}
            public bool TryWrite(long revision,ClearReturnProfile plan,out string error){Attempts++;error=null;if(FailBefore){error="progress before";return false;}if(Value.CommitId==plan.CommitId&&Value.Revision==plan.Revision)return true;if(revision!=Value.Revision){error="revision";return false;}Value=plan.Copy();Commits++;if(FailAfter){error="progress ack";return false;}return true;}
        }
        private sealed class SeedWriter:IInitialExplorationDeckWriter
        {
            public readonly Dictionary<string,ExplorationDeckProfile> Seeds=new Dictionary<string,ExplorationDeckProfile>();public bool FailBefore,FailAfter;public string Generation,Commit;public int Commits;
            public bool TryPrepare(string generation,ExplorationDeckProfile plan,out string error){error=null;if(Generation!=null){Assert.AreEqual(Generation,generation);Assert.AreEqual(Commit,plan.CommitId);}Generation=generation;Commit=plan.CommitId;if(FailBefore){error="seed before";return false;}if(Seeds.TryGetValue(generation,out var previous)){CollectionAssert.AreEqual(PortableCheckpointJson.Serialize(previous),PortableCheckpointJson.Serialize(plan));return true;}Seeds.Add(generation,plan.Copy());Commits++;if(FailAfter){error="seed ack";return false;}return true;}
        }
        private static InitialDeckLoadout Plan()=>new SimpleCardFactory().CreateTrialInitialLoadout();
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void 新規五掛五盤面は二系統保存の成功まで旧盤面を保持し同計画で復旧する(int mode)
        {
            var store=new ProgressStore();var session=new ClearReturnSession(store);Assert.IsTrue(session.InitializeStarter(new SimpleCardFactory().CreateStarterDeck().GetAllCards()));
            var old=PortableCheckpointJson.Serialize(session.Snapshot);var writer=new SeedWriter();var cards=Plan();var seed=Olca.Application.Management.TrialCustomizationDefinitions.NewGameProgress();
            var plan=new InitialDeckLoadout(cards.Dungeon,cards.Outside,seed);seed.UnlockedCells[0].X=99;Assert.AreNotEqual(99,plan.InitialCustomization.UnlockedCells[0].X);
            writer.FailBefore=mode==0;writer.FailAfter=mode==1;store.FailBefore=mode==2;store.FailAfter=mode==3;
            Assert.IsFalse(session.StartNewGameWithLoadout(session.Snapshot.Revision,plan,writer));CollectionAssert.AreEqual(old,PortableCheckpointJson.Serialize(session.Snapshot));
            writer.FailBefore=writer.FailAfter=store.FailBefore=store.FailAfter=false;Assert.IsTrue(session.Retry(),session.Error);
            var cold=new ClearReturnSession(store);Assert.AreEqual(Olca.Application.Management.TrialCustomizationDefinitions.BoardV2Id,cold.CustomizationSnapshot.BoardId);Assert.AreEqual(9,cold.CustomizationSnapshot.UnlockedCells.Length);Assert.IsEmpty(cold.CustomizationSnapshot.OwnedAccessoryIds);Assert.IsEmpty(cold.CustomizationSnapshot.OwnedParts);Assert.IsEmpty(cold.CustomizationSnapshot.OwnedUnlockItems);AssertBoth(store,writer);Assert.AreEqual(1,writer.Commits);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void 四辺解放は開始辺によらず全マスを保持し再送と再読込で増殖しない(int first)
        {
            var store=new ProgressStore();var session=new ClearReturnSession(store);Assert.IsTrue(session.InitializeStarter(new SimpleCardFactory().CreateStarterDeck().GetAllCards()));var cards=Plan();var defs=Olca.Application.Management.TrialCustomizationDefinitions.NewGameProgress();Assert.IsTrue(session.StartNewGameWithLoadout(session.Snapshot.Revision,new InitialDeckLoadout(cards.Dungeon,cards.Outside,defs),new SeedWriter()));
            var catalog=Olca.Application.Management.TrialCustomizationDefinitions.CatalogV2();var provider=Olca.Application.Management.TrialCustomizationDefinitions.ProviderFor(defs);var candidates=provider.Resolve(new Olca.Core.Logic.Customization.CustomizationAcquisitionContext(null)).Snapshot;Assert.AreEqual(2,candidates.MaximumClaims);
            foreach(var grant in candidates.Candidates)Assert.IsTrue(session.TryGrantCustomization(catalog,grant.ToGrant()),session.Error);
            for(int i=0;i<4;i++){var id=Olca.Application.Management.TrialCustomizationDefinitions.EdgeV2Ids[(first+i)%4];Assert.IsTrue(session.TryApplyCustomizationUnlock(catalog,id),session.Error);long revision=session.Snapshot.Revision;Assert.IsTrue(session.TryApplyCustomizationUnlock(catalog,id));Assert.AreEqual(revision,session.Snapshot.Revision);}
            var cold=new ClearReturnSession(store);Assert.AreEqual(25,cold.CustomizationSnapshot.UnlockedCells.Length);Assert.AreEqual(25,cold.CustomizationSnapshot.UnlockedCells.Select(v=>(v.X,v.Y)).Distinct().Count());Assert.AreEqual(4,cold.CustomizationSnapshot.AppliedUnlockItemIds.Length);Assert.AreEqual(2,cold.CustomizationSnapshot.OwnedParts.Single().Count);
        }
        [Test] public void 旧初期計画には盤面を付加せず所持品入りの初期盤面を拒否する()
        {
            var cards=Plan();Assert.IsNull(cards.InitialCustomization);var seed=Olca.Application.Management.TrialCustomizationDefinitions.NewGameProgress();seed.OwnedAccessoryIds=new[]{Olca.Application.Management.TrialCustomizationDefinitions.Accessory};Assert.Throws<ArgumentException>(()=>new InitialDeckLoadout(cards.Dungeon,cards.Outside,seed));
        }

        private static void AssertBoth(ProgressStore store,SeedWriter seeds)
        {
            var p=store.Value;Assert.AreEqual(30,p.Owned.Sum(v=>v.Count));Assert.AreEqual(8,p.Owned.Length);Assert.AreEqual(20,p.SavedDecks.Single().Cards.Length);Assert.AreEqual(p.SavedDecks.Single().Id,p.EquippedDeckId);var outside=seeds.Seeds[p.CheckpointGeneration];Assert.AreEqual(30,outside.Decks.Single().Cards.Length);Assert.AreEqual(outside.Decks.Single().Id,outside.SelectedDeckId);Assert.IsTrue(ExplorationDeckRules.TryValidate(p,outside.Decks[0],out var error),error);Assert.IsTrue(p.Owned.All(v=>v.Rarity==CardRarity.Normal&&v.Level==ExceedLevel.Normal));Assert.AreEqual(0,p.Tokens);Assert.AreEqual(0,p.OutsideInventory?.Currency??0);Assert.AreEqual(0,p.OutsideInventory?.Items.Length??0);
        }
        [Test] public void 仮初期編成は二十と三十を最大共有数だけ配り各個体を分離する()
        {
            var plan=Plan();Assert.AreEqual(20,plan.Dungeon.Count);Assert.AreEqual(30,plan.Outside.Count);Assert.AreEqual(30,plan.Owned.Sum(v=>v.Count));var counts=plan.Owned.ToDictionary(v=>v.Id,v=>v.Count);foreach(var id in new[]{"sword","bomb","heal","shield",TrialInitialCardDefinitions.Shot,TrialInitialCardDefinitions.Spear,TrialInitialCardDefinitions.WideSword})Assert.AreEqual(4,counts[id]);Assert.AreEqual(2,counts[TrialInitialCardDefinitions.AirShot]);Assert.IsFalse(counts.ContainsKey("tuning"));var d=plan.Dungeon;d[0].Modifier.BonusDamage=999;Assert.AreEqual(0,plan.Dungeon[0].Modifier.BonusDamage);Assert.AreEqual(0,plan.Outside[0].Modifier.BonusDamage);Assert.AreNotSame(plan.Dungeon[0],plan.Outside[0]);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void 片側保存失敗と応答不明は同じ世代と計画を再試行して二重配布しない(int mode)
        {
            var store=new ProgressStore();var legacy=new SimpleCardFactory().CreateStarterDeck().GetAllCards();var s=new ClearReturnSession(store);Assert.IsTrue(s.InitializeStarter(legacy));var old=PortableCheckpointJson.Serialize(s.Snapshot);store.Commits=0;store.Attempts=0;var writer=new SeedWriter();writer.FailBefore=mode==0;writer.FailAfter=mode==1;store.FailBefore=mode==2;store.FailAfter=mode==3;
            Assert.IsFalse(s.StartNewGameWithLoadout(s.Snapshot.Revision,Plan(),writer));Assert.IsTrue(s.HasPendingSave);CollectionAssert.AreEqual(old,PortableCheckpointJson.Serialize(s.Snapshot));if(mode<2)Assert.AreEqual(0,store.Attempts);if(mode<3)CollectionAssert.AreEqual(old,PortableCheckpointJson.Serialize(new ClearReturnSession(store).Snapshot));else AssertBoth(store,writer);
            writer.FailBefore=writer.FailAfter=store.FailBefore=store.FailAfter=false;Assert.IsTrue(s.Retry(),s.Error);AssertBoth(store,writer);Assert.AreEqual(1,writer.Commits);Assert.AreEqual(1,store.Commits);Assert.IsTrue(s.Retry());Assert.AreEqual(1,store.Commits);
        }
        [Test] public void 既存継続と確認revision不一致は無断配布せず書込みを開始しない()
        {
            var store=new ProgressStore();var s=new ClearReturnSession(store);Assert.IsTrue(s.InitializeStarter(new SimpleCardFactory().CreateStarterDeck().GetAllCards()));var old=PortableCheckpointJson.Serialize(store.Value);var writer=new SeedWriter();Assert.IsFalse(s.StartNewGameWithLoadout(s.Snapshot.Revision-1,Plan(),writer));Assert.AreEqual(0,writer.Commits);var cold=new ClearReturnSession(store);Assert.AreEqual(20,cold.Snapshot.Owned.Sum(v=>v.Count));Assert.IsFalse(cold.Snapshot.Owned.Any(v=>TrialInitialCardDefinitions.IsId(v.Id)));CollectionAssert.AreEqual(old,PortableCheckpointJson.Serialize(store.Value));
        }
        private sealed class SeedIo:Olca.Unity.CheckpointFileIo{public bool Fail;public override void WriteAndFlush(string path,byte[] bytes){if(Fail)throw new System.IO.IOException("initial seed fault");base.WriteAndFlush(path,bytes);}}
        [Test] public void 実探索保存の失敗再試行と冷読取は使用選択済三十枚を保つ()
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"initial271-"+Guid.NewGuid().ToString("N"));try{
                var io=new SeedIo{Fail=true};var writer=new Olca.Unity.InitialExplorationDeckWriter(dir,io);var store=new ProgressStore();var s=new ClearReturnSession(store);Assert.IsFalse(s.StartNewGameWithLoadout(0,Plan(),writer));Assert.AreEqual(0,store.Commits);Assert.IsTrue(s.HasPendingSave);io.Fail=false;Assert.IsTrue(s.Retry(),s.Error);var p=new ClearReturnSession(store).Snapshot;var decks=Olca.Unity.SaveManager.OpenExplorationDecks(dir,p.CheckpointGeneration).Read(out var error);Assert.NotNull(decks,error);Assert.AreEqual(30,decks.Decks.Single().Cards.Length);Assert.AreEqual(decks.Decks.Single().Id,decks.SelectedDeckId);Assert.IsTrue(ExplorationDeckRules.TryValidate(p,decks.Decks.Single(),out error),error);Assert.AreEqual(30,p.Owned.Sum(v=>v.Count));
            }finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void 不正な初期計画は枚数や上位品や予備を黙って補完しない(int mode)
        {
            var good=Plan();var d=good.Dungeon.ToList();var o=good.Outside.ToList();if(mode==0)o.RemoveAt(0);if(mode==1)d[0]=new SimpleCardFactory().CreateExceeded(d[0],ExceedLevel.Lv1);if(mode==2)o[0]=new Card(d[0].Id,d[0].Name,1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,999,"");Assert.Throws<ArgumentException>(()=>new InitialDeckLoadout(d,o));
        }
        [Test] public void 旧二十枚と進行を保持したまま実ファイルの進行失敗を同一計画で再試行する()
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"initial-progress271-"+Guid.NewGuid().ToString("N"));try{
                var io=new SeedIo();var store=new Olca.Unity.ClearReturnFileStore(dir,io);var s=new ClearReturnSession(store);var cards=new SimpleCardFactory().CreateStarterDeck().GetAllCards();Assert.IsTrue(s.InitializeStarter(cards));var oldProgress=System.IO.File.ReadAllBytes(store.ActivePath);
                var oldDeck=new ExplorationDeckProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),Decks=new[]{new SavedDepartureDeck{Id=Guid.NewGuid().ToString("N"),Name="旧20",Cards=cards.Select(c=>new SavedDeckCard{Id=c.Id,Level=c.ExceedLevel,DefinitionKey=CollectionDisplay.DefinitionHash(c)}).ToArray()}}};var legacy=Olca.Unity.SaveManager.OpenExplorationDecks(dir,s.Snapshot.CheckpointGeneration);Assert.IsTrue(legacy.TryWrite(0,oldDeck,out _));var oldBytes=System.IO.File.ReadAllBytes(legacy.ActivePath);
                io.Fail=true;Assert.IsFalse(s.StartNewGameWithLoadout(s.Snapshot.Revision,Plan(),new Olca.Unity.InitialExplorationDeckWriter(dir)));CollectionAssert.AreEqual(oldProgress,System.IO.File.ReadAllBytes(store.ActivePath));Assert.AreEqual(20,new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(dir)).Snapshot.Owned.Sum(v=>v.Count));CollectionAssert.AreEqual(oldBytes,System.IO.File.ReadAllBytes(legacy.ActivePath));
                io.Fail=false;Assert.IsTrue(s.Retry(),s.Error);var cold=new ClearReturnSession(new Olca.Unity.ClearReturnFileStore(dir));Assert.AreEqual(30,cold.Snapshot.Owned.Sum(v=>v.Count));var outside=Olca.Unity.SaveManager.OpenExplorationDecks(dir,cold.Snapshot.CheckpointGeneration).Read(out var error);Assert.NotNull(outside,error);Assert.AreEqual(30,outside.Decks.Single().Cards.Length);Assert.AreEqual(outside.Decks[0].Id,outside.SelectedDeckId);Assert.AreEqual(20,cold.Snapshot.SavedDecks.Single().Cards.Length);CollectionAssert.AreEqual(oldBytes,System.IO.File.ReadAllBytes(legacy.ActivePath));
            }finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [Test] public void 新世代に既存の別seedがあれば上書きせず同計画だけを再読取する()
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"initial-conflict271-"+Guid.NewGuid().ToString("N"));try{
                string generation=Guid.NewGuid().ToString("N"),id=Guid.NewGuid().ToString("N");var plan=new ExplorationDeckProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),SelectedDeckId=id,Decks=new[]{new SavedDepartureDeck{Id=id,Name="スターター",Cards=Plan().Outside.Select(c=>new SavedDeckCard{Id=c.Id,Level=c.ExceedLevel,DefinitionKey=CollectionDisplay.DefinitionHash(c)}).ToArray()}}};var writer=new Olca.Unity.InitialExplorationDeckWriter(dir);Assert.IsTrue(writer.TryPrepare(generation,plan,out _));var store=Olca.Unity.SaveManager.OpenExplorationDecks(dir,generation);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(writer.TryPrepare(generation,plan.Copy(),out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var other=plan.Copy();other.CommitId=Guid.NewGuid().ToString("N");other.Decks[0].Name="別計画";Assert.IsFalse(writer.TryPrepare(generation,other,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));
            }finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
    }
}

namespace Olca.Tests.Battle
{
    public sealed class InitialCardObservationTests
    {
        [TestCase(false)] [TestCase(true)]
        public void 通常選択の観測は使用者不在で式を保持し使用者があれば実威力を解決する(bool attached)
        {
            var card=new SimpleCardFactory().CreateById(TrialInitialCardDefinitions.Shot);var deck=new Deck();var screen=attached?new BattleSystem(new PlayerCharacter(100,attackPower:13),new List<Enemy>{new Enemy("fixture","fixture",1000,ElementType.None,new Vector2Int(3,0))},deck).CustomScreen:new CustomScreenSystem(deck);
            screen.Open(new[]{card},new SeededBattleRandom(271));var value=screen.Commands.Observe().Available.Single();if(attached)Assert.AreEqual(13,value.Power);else{Assert.IsNull(value.Power);StringAssert.Contains("コウゲキリョク100%",value.PowerText);}Assert.AreEqual(CollectionDisplay.DefinitionHash(card),value.DefinitionHash);Assert.IsFalse(card.IsEffectCopy);
        }
    }
}
