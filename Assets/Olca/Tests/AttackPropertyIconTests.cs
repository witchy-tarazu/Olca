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
        [TestCase(false)] [TestCase(true)] public void 明示相性は八属性全組合せを受ける属性の耐性へ一度だけ解決する(bool enemy)
        {
            var elements=new[]{ElementType.Fire,ElementType.Water,ElementType.Metal,ElementType.Wood,ElementType.Earth,ElementType.Yin,ElementType.Yang,ElementType.None};
            var table=new[]{new[]{0,-50,50,0,0,0,0,0},new[]{50,0,0,0,-50,0,0,0},new[]{-50,0,0,50,0,0,0,0},new[]{0,0,-50,0,50,0,0,0},new[]{0,50,0,-50,0,0,0,0},new[]{0,0,0,0,0,0,-50,0},new[]{0,0,0,0,0,-50,0,0},new int[8]};
            for(int own=0;own<8;own++)for(int attack=0;attack<8;attack++){
                var value=enemy?ElementAffinity.CreateEnemyBase(elements[own]):ElementAffinity.CreatePlayerBase(elements[own]);
                int expected=enemy&&table[own][attack]>0?0:table[own][attack];Assert.AreEqual(expected,value.Get(elements[attack]),$"{elements[own]} <- {elements[attack]}");
                Assert.AreEqual(100-expected,value.CalculateDamage(100,elements[attack],false));Assert.AreEqual(0,value.Wind);
            }
        }
        [Test] public void 明示実基本値は全置換で旧風と役割違いを拒否し基準値も調整できる()
        {
            var actual=ElementResistances.CreateCurrent(water:17,yang:-23);var d=CurrentElementDefenseDefinition.ForPlayer(ElementType.Fire,actual);Assert.AreSame(actual,d.BaseResistances);Assert.AreEqual(17,d.BaseResistances.Water);Assert.AreEqual(0,d.BaseResistances.Metal);
            Assert.AreEqual(-60,ElementAffinity.CreatePlayerBase(ElementType.Fire,-60,40).Water);Assert.AreEqual(40,ElementAffinity.CreatePlayerBase(ElementType.Fire,-60,40).Metal);Assert.AreEqual(25,ElementAffinity.CreateEnemyBase(ElementType.Fire,-40,25).Metal);
            foreach(var value in new[]{ElementType.Wind,(ElementType)99}){Assert.Throws<System.ArgumentOutOfRangeException>(()=>ElementAffinity.CreatePlayerBase(value));Assert.Throws<System.ArgumentOutOfRangeException>(()=>CurrentElementDefenseDefinition.ForEnemy(value));}
            Assert.Throws<System.ArgumentException>(()=>CurrentElementDefenseDefinition.ForEnemy(ElementType.Fire,new ElementResistances(wind:1)));
            Assert.Throws<System.InvalidOperationException>(()=>d.CreateEnemy("x","検証",100,new Olca.Core.Domain.Vector2Int(0,0)));
            Assert.Throws<System.InvalidOperationException>(()=>CurrentElementDefenseDefinition.ForEnemy(ElementType.Fire).CreatePlayer(100,DepartureStyleDefinition.Basic));
            Assert.Throws<System.ArgumentException>(()=>d.CreatePlayer(100,DepartureStyleDefinition.Basic));Assert.Throws<System.ArgumentNullException>(()=>d.CreatePlayer(100,null));
        }
        [Test] public void 明示プレイヤーは装備加算と端数処理を既存式で行い解決済み値を復元する()
        {
            var style=new DepartureStyleDefinition("explicit-fire","検証",null,new TransformationAttackDefinition(ElementType.Fire));
            var player=CurrentElementDefenseDefinition.ForPlayer(ElementType.Fire).CreatePlayer(200,style);
            player.SetDepartureAbilities(new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(new Olca.Core.Logic.Customization.CustomizationAbilitiesDto{DefinitionId="explicit",Sources=new[]{new Olca.Core.Logic.Customization.CustomizationAbilitySourceDto{Kind="part",SourceId="one",ItemId="one",Water=20,Fire=150,AdditionalElements=new[]{0,0,-150,0}}}}));
            Assert.AreEqual(-30,player.ActiveResistances.Get(ElementType.Water));Assert.AreEqual(100,player.ActiveResistances.Get(ElementType.Fire));Assert.AreEqual(-100,player.ActiveResistances.Get(ElementType.Yin));
            player.TakeDamage(41,affectsPoise:false,attackElement:ElementType.Water);Assert.AreEqual(147,player.CurrentHp);
            var mapper=typeof(Olca.Core.Logic.Dungeon.DungeonCheckpointSession).Assembly.GetType("Olca.Core.Logic.Dungeon.CheckpointPlayerMapper");var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
            var dto=(CheckpointPlayerDto)mapper.GetMethod("Capture",flags).Invoke(null,new object[]{player});var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(dto);
            var restored=(PlayerCharacter)mapper.GetMethod("Restore",flags).Invoke(null,new object[]{Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<CheckpointPlayerDto>(bytes)});
            Assert.AreEqual(ElementType.Fire,restored.ActiveElement);Assert.IsTrue(player.BaseElementResistances.HasSameValues(restored.BaseElementResistances));Assert.IsTrue(player.Resistances.HasSameValues(restored.Resistances));Assert.AreEqual(147,restored.CurrentHp);
            restored.TakeDamage(10,affectsPoise:false,attackElement:ElementType.Metal);Assert.AreEqual(142,restored.CurrentHp);
            Assert.Throws<System.InvalidOperationException>(()=>player.SetDepartureStyle(DepartureStyleDefinition.Basic));
        }
        [Test] public void 明示敵は弱点だけを標準適用し個別値と保存復元と実被弾に接続する()
        {
            var enemy=CurrentElementDefenseDefinition.ForEnemy(ElementType.Metal).CreateEnemy("explicit-metal","検証",200,new Olca.Core.Domain.Vector2Int(4,1));
            Assert.AreEqual(-50,enemy.Resistances.Fire);Assert.AreEqual(0,enemy.Resistances.Wood);enemy.TakeDamage(41,ElementType.Fire);Assert.AreEqual(139,enemy.CurrentHp);
            var dto=Olca.Core.Logic.Dungeon.PortableEnemyDefinition.Capture(enemy);var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(dto);var restored=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<Olca.Core.Logic.Dungeon.PortableEnemyDefinition>(bytes).Restore();
            Assert.AreEqual(ElementType.Metal,restored.Element);Assert.IsTrue(enemy.Resistances.HasSameValues(restored.Resistances));restored.TakeDamage(41,ElementType.Wood);Assert.AreEqual(159,restored.CurrentHp);
            var exception=CurrentElementDefenseDefinition.ForEnemy(ElementType.Metal,ElementResistances.CreateCurrent(fire:-25,wood:50)).CreateEnemy("exception","検証",100,new Olca.Core.Domain.Vector2Int(0,0));exception.TakeDamage(41,ElementType.Wood);Assert.AreEqual(80,exception.CurrentHp);
        }
        [Test] public void 従来生成と復元へ相性を自動適用せず明示生成も既存製品定義を変えない()
        {
            var products=new Olca.Application.Battle.SimpleCardFactory().CollectionCards.Select(c=>CollectionDisplay.DefinitionHash(c)).ToArray();
            var old=new Enemy("legacy","検証",100,ElementType.Fire,new Olca.Core.Domain.Vector2Int(0,0));Assert.IsTrue(old.Resistances.IsNeutral);old.TakeDamage(40,ElementType.Water);Assert.AreEqual(60,old.CurrentHp);
            var player=new PlayerCharacter(100);player.SetDepartureStyle(new DepartureStyleDefinition("legacy","検証",null,new TransformationAttackDefinition(ElementType.Fire)));Assert.IsTrue(player.Resistances.IsNeutral);
            CurrentElementDefenseDefinition.ForPlayer(ElementType.Fire).CreatePlayer(100,new DepartureStyleDefinition("explicit","検証",null,new TransformationAttackDefinition(ElementType.Fire)));
            CollectionAssert.AreEqual(products,new Olca.Application.Battle.SimpleCardFactory().CollectionCards.Select(c=>CollectionDisplay.DefinitionHash(c)).ToArray());Assert.IsTrue(player.Resistances.IsNeutral);
        }
        public sealed class OldElementEnvelope { public int Version; public string Payload,Sha256; }
        [Test] public void 旧raw封筒は元payloadのhashを検証してから追加欄欠損を読む()
        {
            var text=System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String("eyJBdHRhY2tHcm93dGgiOm51bGwsIkF0dGFja1Bvd2VyIjoxMCwiQmxpbmRSZXNpc3RhbmNlIjowLCJDb25mdXNpb25SZXNpc3RhbmNlIjowLCJDdXN0b21TcGVlZCI6MSwiQ3VzdG9taXphdGlvbkFiaWxpdGllcyI6bnVsbCwiRWFydGhSZXNpc3RhbmNlIjo0NCwiRmlyZVJlc2lzdGFuY2UiOjExLCJHcmlkWCI6MCwiR3JpZFkiOjAsIkhwIjo3MCwiTWF4SHAiOjEwMCwiUGFyYWx5emVSZXNpc3RhbmNlIjowLCJQb2lzZSI6MzAsIlBvaXNlUmVjb3ZlcnlGcmFtZXMiOm51bGwsIlBvaXNvblJlc2lzdGFuY2UiOjAsIlBvaXNvbnMiOm51bGwsIlJhd0Jhc2VFYXJ0aFJlc2lzdGFuY2UiOm51bGwsIlJhd0Jhc2VGaXJlUmVzaXN0YW5jZSI6bnVsbCwiUmF3QmFzZVdhdGVyUmVzaXN0YW5jZSI6bnVsbCwiUmF3QmFzZVdpbmRSZXNpc3RhbmNlIjpudWxsLCJTdGFnZ2VyRnJhbWVzIjowLCJTdHlsZSI6bnVsbCwiV2F0ZXJSZXNpc3RhbmNlIjotMjIsIldpbmRSZXNpc3RhbmNlIjozM30="));var payload="{\"Player\":"+text+"}";
            var value=new OldElementEnvelope{Version=1,Payload=payload,Sha256=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Hash(System.Text.Encoding.UTF8.GetBytes(payload))};
            var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(value);var decoded=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Decode(bytes);Assert.AreEqual(33,decoded.Player.WindResistance);Assert.IsNull(decoded.Player.AdditionalElementResistances);
            value.Payload=payload+" ";Assert.Throws<System.IO.InvalidDataException>(()=>Olca.Core.Logic.Dungeon.PortableCheckpointJson.Decode(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(value)));
        }
        [Test] public void 追加耐性は装備sourceの保存複製再解決で失われず基礎値と一度だけ合算する()
        {
            var source=new Olca.Core.Logic.Customization.CustomizationAbilitySourceDto{Kind="part",SourceId="instance",ItemId="part",Wind=33,AdditionalElements=new[]{150,-50,20,30}};
            var dto=new Olca.Core.Logic.Customization.CustomizationAbilitiesDto{DefinitionId="explicit",Sources=new[]{source}};
            var resolved=new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(dto);source.AdditionalElements[0]=999;
            var copy=resolved.Capture();var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(copy);copy.Sources[0].AdditionalElements[0]=999;
            var restored=new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<Olca.Core.Logic.Customization.CustomizationAbilitiesDto>(bytes));
            var result=restored.CombineElements(ElementResistances.CreateCurrent(metal:-100));Assert.AreEqual(50,result.Metal);Assert.AreEqual(-50,result.Wood);Assert.AreEqual(33,result.Wind);
            dto.Sources[0].AdditionalElements=new int[3];Assert.Throws<System.ArgumentException>(()=>dto.Validate());
        }
        [Test] public void 新耐性だけの差をmap定義hashで検出し追加ゼロは旧四耐性と同一になる()
        {
            string Hash(ElementResistances resistances){var map=DungeonConfig.CreateTestDungeon(6,30,100);map.MapCells[1]=new MapCell(1,new Enemy("explicit","検証",100,ElementType.Fire,new Olca.Core.Domain.Vector2Int(4,1),resistances:resistances));return new Olca.Core.Logic.Dungeon.CheckpointDefinitions("explicit",map).MapHash;}
            var old=Hash(new ElementResistances(11,-22,33,44));Assert.AreEqual(old,Hash(new ElementResistances(11,-22,33,44,0,0,0,0)));Assert.AreNotEqual(old,Hash(new ElementResistances(11,-22,33,44,1,0,0,0)));
        }
        [Test] public void 未知の属性上書きも構築復元と保存前に拒否し旧風上書きは変換しない()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>new Card("invalid","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,17,"",modifier:new CardModifier{ElementOverride=(ElementType)99}));
            var c=new Card("override","検証",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,17,"");
            var dto=Olca.Core.Logic.Dungeon.PortableCardDefinition.Capture("override",c);dto.Modifier.ElementOverride=(ElementType)99;Assert.Throws<System.ArgumentOutOfRangeException>(()=>dto.Restore());
            c.Modifier.ElementOverride=(ElementType)99;Assert.Throws<System.ArgumentOutOfRangeException>(()=>CollectionDisplay.DefinitionHash(c));Assert.Throws<System.ArgumentOutOfRangeException>(()=>Olca.Core.Logic.Dungeon.PortableCardDefinition.Capture("bad",c));
            c.Modifier.ElementOverride=ElementType.Wind;Assert.AreEqual(ElementType.Wind,c.Clone().GetFinalElement());Assert.AreEqual(AttackProperty.None,c.Properties);
            c.Modifier.ElementOverride=ElementType.Yang;Assert.AreEqual(ElementType.Yang,c.Clone().GetFinalElement());
        }
        [Test] public void 旧五属性の数値と定義hashは移行せず新規登録だけ旧風を拒否する()
        {
            var hashes=new[]{"783273C4EBBA0FCD0134AAF913ADB40FC58404C03D7E6A96029A3060C6CFCB3D","7885BE65F98F74A369C6D4BF808F29183CF66FACF4C88BB69E12AAB72C65E11B","B79048C52EA2F661EF2145A8D3F00A12A97F4C41FC40D621A51172DF2CEF5281","FFECE248A1C1CE40C10C5C972654556839FA637BFC604BEFD24F03CDF5645A75","49CEE3E9ABFCD4A4C442B7CF2057D7A801C4EDF1586C82AC7130028A2A7C6C81"};
            for(int i=0;i<5;i++) {
                var c=new Card("legacy-element","検証",1,CardRarity.Normal,CardCategory.Attack,(ElementType)i,CardSeries.None,17,"");
                Assert.AreEqual(hashes[i],CollectionDisplay.DefinitionHash(c));
                Assert.AreEqual((ElementType)i,Olca.Core.Logic.Dungeon.PortableCardDefinition.Capture("legacy",c).Restore().Element);
            }
            var legacy=new Olca.Core.Domain.Master.CardMaster{Id="wind",Category=CardCategory.Attack,Element=ElementType.Wind};
            Assert.AreEqual(ElementType.Wind,Olca.Core.Domain.Card.FromMaster(legacy).Element);Assert.Throws<System.ArgumentOutOfRangeException>(()=>Olca.Core.Domain.Card.FromCurrentMaster(legacy));
            Assert.AreEqual(AttackProperty.None,Olca.Core.Domain.Card.FromMaster(legacy).Properties);
            foreach(int unknown in new[]{-1,9,99}){legacy.Element=(ElementType)unknown;Assert.Throws<System.ArgumentOutOfRangeException>(()=>Olca.Core.Domain.Card.FromMaster(legacy));Assert.AreEqual("ミテイギ",CustomDisplay.ElementName((ElementType)unknown));}
            Assert.AreEqual(8,System.Enum.GetValues(typeof(ElementType)).Cast<ElementType>().Count(ElementRules.IsCurrent));
        }
        [TestCase(ElementType.Metal,"キン")][TestCase(ElementType.Wood,"キ")][TestCase(ElementType.Yin,"イン")][TestCase(ElementType.Yang,"ヨウ")]
        public void 新規明示属性は保存復元と実読取詳細へ接続し特性を推測しない(ElementType element,string label)
        {
            var c=Olca.Core.Domain.Card.FromCurrentMaster(new Olca.Core.Domain.Master.CardMaster{Id="explicit-element",Name="検証",Element=element,Category=CardCategory.Attack,Power=17});
            var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(Olca.Core.Logic.Dungeon.PortableCardDefinition.Capture("explicit",c));
            var restored=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<Olca.Core.Logic.Dungeon.PortableCardDefinition>(bytes).Restore();
            Assert.AreEqual(element,restored.Element);Assert.AreEqual(AttackProperty.None,restored.Properties);Assert.AreEqual(CollectionDisplay.DefinitionHash(c),CollectionDisplay.DefinitionHash(restored));StringAssert.Contains("ゾクセイ "+label,CustomDisplay.CardDetails(restored));
            var obj=new GameObject("EightDetail",typeof(RectTransform));try {
                var view=obj.AddComponent<Olca.Presentation.Dungeon.ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});
                view.RenderCollection(new[]{"検証"},System.Array.Empty<CollectionRecipeRow>(),false,null,()=>{},()=>{},cardDetails:new[]{restored});
                typeof(Olca.Presentation.Dungeon.ClearReturnView).GetMethod("OpenCardDetails",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,new object[]{restored,"CloseCollection"});
                StringAssert.Contains("ゾクセイ "+label,view.GetComponentsInChildren<Text>().Single(v=>v.name=="ReadonlyCardPerformance").text);
            }finally{Object.DestroyImmediate(obj);}
        }
        [Test] public void 七耐性と旧風欄は独立して合算し敵保存は旧四欄または新八欄だけを許可する()
        {
            var a=ElementResistances.CreateCurrent(10,20,30,40,50,60,70);Assert.AreEqual(0,a.Wind);Assert.AreEqual(70,a.Get(ElementType.Yang));
            var sum=ElementResistances.Combine(a,ElementResistances.CreateCurrent(metal:100,wood:-200));Assert.AreEqual(100,sum.Get(ElementType.Metal));Assert.AreEqual(-100,sum.Get(ElementType.Wood));
            Assert.AreEqual(0,sum.CalculateDamage(100,ElementType.Metal,false));Assert.AreEqual(200,sum.CalculateDamage(100,ElementType.Wood,false));Assert.AreEqual(100,sum.CalculateDamage(100,ElementType.None,false));
            var enemy=new Enemy("new","検証",100,ElementType.Metal,new Olca.Core.Domain.Vector2Int(4,1),resistances:a);var dto=Olca.Core.Logic.Dungeon.PortableEnemyDefinition.Capture(enemy);Assert.AreEqual(8,dto.Resistances.Length);
            var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(dto);var restored=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<Olca.Core.Logic.Dungeon.PortableEnemyDefinition>(bytes).Restore();Assert.IsTrue(a.HasSameValues(restored.Resistances));
            dto.Resistances=new int[7];Assert.Throws<System.ArgumentException>(()=>dto.Restore());dto.Resistances=new int[8];dto.Element=99;Assert.Throws<System.ArgumentOutOfRangeException>(()=>dto.Restore());
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>a.Get((ElementType)99));
        }
        [Test] public void 旧保存の風耐性をそのまま読み追加欄は欠損ゼロでプレイヤー往復し複製は共有しない()
        {
            var oldEnemy=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<Olca.Core.Logic.Dungeon.PortableEnemyDefinition>(System.Convert.FromBase64String("eyJEYXNoIjpudWxsLCJEYXNoVmFsdWUiOm51bGwsIkVsZW1lbnQiOjMsIkhwIjoxMDAsIklkIjoibGVnYWN5LXdpbmQiLCJOYW1lIjoi5pen6aKoIiwiUG9pc2UiOm51bGwsIlJlY292ZXJ5IjpudWxsLCJSZXNpc3RhbmNlcyI6WzExLC0yMiwzMyw0NF0sIlN0YWdnZXIiOm51bGwsIlN0YXR1cyI6WzAsMCwwLDBdLCJYIjo0LCJZIjoxfQ=="));
            var e=oldEnemy.Restore();Assert.AreEqual(ElementType.Wind,e.Element);Assert.AreEqual(33,e.Resistances.Wind);Assert.AreEqual(0,e.Resistances.Metal);Assert.AreEqual(4,Olca.Core.Logic.Dungeon.PortableEnemyDefinition.Capture(e).Resistances.Length);
            var old=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<CheckpointPlayerDto>(System.Convert.FromBase64String("eyJBdHRhY2tHcm93dGgiOm51bGwsIkF0dGFja1Bvd2VyIjoxMCwiQmxpbmRSZXNpc3RhbmNlIjowLCJDb25mdXNpb25SZXNpc3RhbmNlIjowLCJDdXN0b21TcGVlZCI6MSwiQ3VzdG9taXphdGlvbkFiaWxpdGllcyI6bnVsbCwiRWFydGhSZXNpc3RhbmNlIjo0NCwiRmlyZVJlc2lzdGFuY2UiOjExLCJHcmlkWCI6MCwiR3JpZFkiOjAsIkhwIjo3MCwiTWF4SHAiOjEwMCwiUGFyYWx5emVSZXNpc3RhbmNlIjowLCJQb2lzZSI6MzAsIlBvaXNlUmVjb3ZlcnlGcmFtZXMiOm51bGwsIlBvaXNvblJlc2lzdGFuY2UiOjAsIlBvaXNvbnMiOm51bGwsIlJhd0Jhc2VFYXJ0aFJlc2lzdGFuY2UiOm51bGwsIlJhd0Jhc2VGaXJlUmVzaXN0YW5jZSI6bnVsbCwiUmF3QmFzZVdhdGVyUmVzaXN0YW5jZSI6bnVsbCwiUmF3QmFzZVdpbmRSZXNpc3RhbmNlIjpudWxsLCJTdGFnZ2VyRnJhbWVzIjowLCJTdHlsZSI6bnVsbCwiV2F0ZXJSZXNpc3RhbmNlIjotMjIsIldpbmRSZXNpc3RhbmNlIjozM30="));Assert.IsNull(old.AdditionalElementResistances);old.ValidatePermanentAbilities();
            var mapper=typeof(Olca.Core.Logic.Dungeon.DungeonCheckpointSession).Assembly.GetType("Olca.Core.Logic.Dungeon.CheckpointPlayerMapper");var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
            var legacy=(PlayerCharacter)mapper.GetMethod("Restore",flags).Invoke(null,new object[]{old});Assert.AreEqual(33,legacy.BaseElementResistances.Wind);
            legacy.SetElementResistances(new ElementResistances(11,-22,33,44,55,-66,77,-88));var captured=(CheckpointPlayerDto)mapper.GetMethod("Capture",flags).Invoke(null,new object[]{legacy});var copy=captured.Copy();copy.AdditionalElementResistances[0]=999;Assert.AreEqual(55,captured.AdditionalElementResistances[0]);
            var bytes=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(captured);var again=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Deserialize<CheckpointPlayerDto>(bytes);again.ValidatePermanentAbilities();var player=(PlayerCharacter)mapper.GetMethod("Restore",flags).Invoke(null,new object[]{again});Assert.IsTrue(legacy.BaseElementResistances.HasSameValues(player.BaseElementResistances));Assert.AreEqual(70,player.CurrentHp);
            again.AdditionalElementResistances=new int[3];Assert.Throws<System.ArgumentException>(()=>again.ValidatePermanentAbilities());
        }
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
            var bombUpgrades=products.Where(c=>c.Id=="bomb"&&(c.ExceedLevel==ExceedLevel.Lv1||c.ExceedLevel==ExceedLevel.Lv2)).ToArray();
            Assert.AreEqual(2,bombUpgrades.Length);Assert.IsTrue(bombUpgrades.All(c=>c.Properties==AttackProperty.None));
            Assert.AreEqual(2,bombUpgrades.Select(CollectionDisplay.DefinitionHash).Distinct().Count());
            var oldProducts=products.Except(bombUpgrades).ToArray();
            Assert.IsFalse(oldProducts.Select(CollectionDisplay.DefinitionHash).Intersect(bombUpgrades.Select(CollectionDisplay.DefinitionHash)).Any());
            CollectionAssert.AreEqual(new[]{"1218C1F2DB25767F19997CB95FC15F3A97EEEBBCFFA7E0C2036D866E68B60E55","948DDFF162CDF844C1E2B7E1B44E9B776100D14E9FD9D3C098D6B0160C7F2644","E666B3AE7FBA6907FA594A0B37FD3137EF68086A13E670D005ECA54978C92626","6730B35646456713B6C9C8BC84D3FB713691C3EBF9881C630F306DD8A981AC17","57DA3BACEBD001C9ED0C8F7A9FD9B617CC96F6B21E45A95EDF035D7FD168942E","592FA9E3C764DF64FAA94E41C21171F7DFA3EC214BBC3769D5AECCBC6E5498F5","1FC17C6DD1A12891D4E424446DBC484419BF84315F551CAA860EA16D80D4FA3F","32AC390531376B97233A72E81F610D49C32887CC884A40A74BDC7FFD73FA1088","7F919139649B76644CB2959C65A028B82890807A18B0B6B9B5B0B1150A240D12","D857847C3882E5F5AF41257F5956A686F6F9BE731E3572AC3D00BB25CD514949","449732BE6511DAEA9981EF8905159B6A911ECFB94033490501DD5D9695E44D60","D27F010885A04B9CF4A5202F89ECA29608A27F74EB2D4081795D0F7541433127","464E5E19F7BC90E4F815242C2EA0EC2A78DA4FACB34685DA542C175EA069662B","645834FFF3B4C7539190ED3C9ECFE14BC8023AC70FD2DA78127C56A692B7C026","ABFA524D5473530ADF1CE54023D99333E2C6413ADA22DAE5102D0110B060A205","013087DD985FBA84FFBC4F648CA32635D52CD8346750A87A1D187A02B221CA47","130530AB13A4CC2794FBB65232394D0008CED4A85F5BC27B47B97412D4B76CE0","BA3EB787AE873E8B02360ACCBD1A1969CDC4B05099EC3ACA8DAC10B7E80C9203"},oldProducts.Select(CollectionDisplay.DefinitionHash));
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
