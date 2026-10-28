using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Presentation.Effects;
using Olca.Presentation.Battle;
using Olca.Presentation.Field;
using Olca.Tests.Mocks;
namespace Olca.Tests
{
    public class ActorConditionVisualTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Hidden).SetValue(target,value);
        private static ActorConditionVisual Visual(object owner,string name)=>(ActorConditionVisual)owner.GetType().GetField(name,Hidden).GetValue(owner);
        private Texture2D texture;private Sprite sprite;private GameObject host;private SpriteRenderer body;
        [SetUp] public void Setup(){texture=new Texture2D(8,16);texture.SetPixels(Enumerable.Repeat(Color.white,128).ToArray());texture.Apply();sprite=Sprite.Create(texture,new Rect(0,0,8,16),new Vector2(.5f,.5f),8);host=new GameObject("Actor",typeof(SpriteRenderer));body=host.GetComponent<SpriteRenderer>();body.sprite=sprite;body.sortingOrder=3;}
        [TearDown] public void Cleanup(){Object.DestroyImmediate(host);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        private BattleSystem Battle(out PlayerCharacter player,out Enemy enemy){player=new PlayerCharacter(500);enemy=new Enemy("grunt","テスト",500,ElementType.None,new Olca.Core.Domain.Vector2Int(5,0));var card=new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,1,"");return new BattleSystem(player,new System.Collections.Generic.List<Enemy>{enemy},new Deck(new System.Collections.Generic.List<Card>{card}),logger:new MockLogger());}
        [TestCase(ActorConditionKind.Guard)] [TestCase(ActorConditionKind.Paralyze)]
        public void 独立状態素材は中央透明の二百五十六角で共有包装と一致する(ActorConditionKind kind)
        {
            var name=kind.ToString().ToLowerInvariant();var bytes=Resources.Load<TextAsset>("OlcaActorConditions/"+name);Assert.IsNotNull(bytes);CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Art/Effects/ActorConditions",name+".png")),bytes.bytes);
            var image=new Texture2D(2,2);try{Assert.IsTrue(image.LoadImage(bytes.bytes));Assert.AreEqual(256,image.width);Assert.AreEqual(256,image.height);CollectionAssert.AreEqual(new byte[]{0,255},image.GetPixels32().Select(p=>p.a).Distinct().OrderBy(v=>v).ToArray());Assert.IsTrue(image.GetPixels(80,80,96,96).All(p=>p.a==0));}finally{Object.DestroyImmediate(image);}
            var shared=ActorConditionVisual.GetSprite(kind);Assert.IsNotNull(shared);Assert.AreSame(shared,ActorConditionVisual.GetSprite(kind));Assert.AreEqual(FilterMode.Point,shared.texture.filterMode);Assert.IsFalse(shared.texture.isReadable);
        }
        [TestCase(ActorConditionKind.Guard)] [TestCase(ActorConditionKind.Paralyze)]
        public void 反復発動は一描画を再利用し本体と可視性設定を保持する(ActorConditionKind kind)
        {
            host.transform.position=new Vector3(3,4,0);host.transform.localScale=new Vector3(2,2,1);var pos=host.transform.position;var scale=host.transform.localScale;var rotation=host.transform.rotation;
            using(var view=new ActorConditionVisual(kind)){
                for(int n=0;n<20;n++)view.Refresh(body,true);var renderer=view.Renderer;Assert.IsTrue(view.IsVisible);Assert.AreEqual(body.gameObject.layer,renderer.gameObject.layer);Assert.AreEqual(body.maskInteraction,renderer.maskInteraction);
                Assert.AreEqual(pos,host.transform.position);Assert.AreEqual(scale,host.transform.localScale);Assert.AreEqual(rotation,host.transform.rotation);Assert.AreSame(sprite,body.sprite);Assert.AreEqual(2,host.GetComponentsInChildren<SpriteRenderer>().Length);
                body.forceRenderingOff=true;view.Refresh(body,true);Assert.IsFalse(view.IsVisible);body.forceRenderingOff=false;view.Refresh(body,true);Assert.AreSame(renderer,view.Renderer);Assert.IsTrue(view.IsVisible);view.Refresh(body,false);Assert.IsFalse(view.IsVisible);view.Refresh(body,true);Assert.AreSame(renderer,view.Renderer);
                view.Dispose();view.Refresh(body,true);Assert.IsFalse(view.IsVisible);Assert.IsNull(view.Renderer);
            }
            Assert.IsNull(ActorConditionVisual.GetSprite((ActorConditionKind)99));Assert.Throws<System.ArgumentOutOfRangeException>(()=>new ActorConditionVisual((ActorConditionKind)99));
        }
        [Test] public void ガードと麻痺と防御フィールドは別レイヤーで併存し独立して消える()
        {
            using(var guard=new ActorConditionVisual(ActorConditionKind.Guard))using(var paralysis=new ActorConditionVisual(ActorConditionKind.Paralyze))using(var defense=new DefensiveFieldVisual()){
                guard.Refresh(body,true);paralysis.Refresh(body,true);defense.Refresh(body,true,false);Assert.IsTrue(guard.IsVisible&&paralysis.IsVisible);Assert.IsNotNull(defense.VisibleKind);Assert.Less(guard.Renderer.sortingOrder,defense.Renderer.sortingOrder);Assert.Less(defense.Renderer.sortingOrder,body.sortingOrder);Assert.Greater(paralysis.Renderer.sortingOrder,body.sortingOrder);
                guard.Clear();Assert.IsFalse(guard.IsVisible);Assert.IsTrue(paralysis.IsVisible);Assert.IsNotNull(defense.VisibleKind);paralysis.Clear();Assert.IsNotNull(defense.VisibleKind);
            }
        }
        [Test] public void 敵の付与フレーム内は麻痺を先行表示せず解除再束縛死亡で残さない()
        {
            var battle=Battle(out _,out var enemy);var view=host.AddComponent<EnemyView>();Set(view,"enemyImage",body);view.Initialize(enemy);enemy.Guard.Set(true,false);view.Refresh();Assert.IsTrue(Visual(view,"guardVisual").IsVisible);
            battle.RunLogicalFrame(()=>{enemy.ApplyStatus(new StatusEffect(StatusEffectType.Paralyze,2000));view.Refresh();Assert.IsNull(Visual(view,"paralysisVisual"));});
            view.Refresh();Assert.IsTrue(Visual(view,"paralysisVisual").IsVisible);int remaining=enemy.InspectStatuses().Single().RemainingFrames;for(int n=0;n<20;n++)view.Refresh();Assert.AreEqual(remaining,enemy.InspectStatuses().Single().RemainingFrames);
            battle.RunLogicalFrame(()=>enemy.ClearStatus(StatusEffectType.Paralyze));view.Refresh();Assert.IsFalse(Visual(view,"paralysisVisual").IsVisible);Assert.IsTrue(Visual(view,"guardVisual").IsVisible);
            enemy.Guard.Clear();view.Refresh();Assert.IsFalse(Visual(view,"guardVisual").IsVisible);var next=new Enemy("grunt","テスト",10,ElementType.None,new Olca.Core.Domain.Vector2Int(3,0));view.Initialize(next);view.Refresh();Assert.IsFalse(Visual(view,"paralysisVisual").IsVisible);next.Guard.Set(true,false);view.Refresh();next.Guard.Clear();next.TakeDamage(999,ElementType.None);view.Refresh();Assert.IsFalse(Visual(view,"guardVisual").IsVisible);
        }
        [Test] public void プレイヤーの予約を確定せず既存二フレームの麻痺だけを表示し終了で消す()
        {
            var battle=Battle(out var player,out _);var card=new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,1,"");var dungeon=new DungeonSystem(DungeonConfig.CreateTestDungeon(6,30,4),player,new Deck(new System.Collections.Generic.List<Card>{card}),new MockRandomProvider(),new MockLogger());var view=host.AddComponent<GameFieldView>();Set(view,"playerSprite",body);Set(view,"dungeonSystem",dungeon);Set(view,"currentBattle",battle);
            player.Guard.Set(true,false);player.ApplyParalysis(2);view.RefreshBattleField();Assert.IsNull(Visual(view,"paralysisVisual"));Assert.IsTrue(Visual(view,"guardVisual").IsVisible);
            battle.RunLogicalFrame(()=>{});view.RefreshBattleField();Assert.IsTrue(Visual(view,"paralysisVisual").IsVisible);Assert.AreEqual(2,player.ParalysisRemainingFrames);for(int n=0;n<20;n++)view.RefreshBattleField();Assert.AreEqual(2,player.ParalysisRemainingFrames);
            battle.TickFrame(16);view.RefreshBattleField();Assert.IsTrue(Visual(view,"paralysisVisual").IsVisible);battle.TickFrame(17);view.RefreshBattleField();Assert.IsFalse(Visual(view,"paralysisVisual").IsVisible);Assert.IsTrue(Visual(view,"guardVisual").IsVisible);
            view.OnBattleEnded();Assert.IsFalse(Visual(view,"guardVisual").IsVisible);Assert.IsFalse(Visual(view,"paralysisVisual").IsVisible);
        }
    }
}
