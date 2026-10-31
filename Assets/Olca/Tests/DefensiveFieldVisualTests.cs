using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
using Olca.Presentation.Effects;
using Olca.Presentation.Battle;
using Olca.Presentation.Field;
using Olca.Tests.Mocks;
namespace Olca.Tests
{
    public class DefensiveFieldVisualTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(object target,string name,object value)=>target.GetType().GetField(name,Hidden).SetValue(target,value);
        private static void Call(object target,string name)=>target.GetType().GetMethod(name,Hidden).Invoke(target,null);
        private Texture2D texture;private Sprite sprite;private GameObject host;private SpriteRenderer body;
        [SetUp] public void Setup(){texture=new Texture2D(8,16);texture.SetPixels(Enumerable.Repeat(Color.white,128).ToArray());texture.Apply();sprite=Sprite.Create(texture,new Rect(0,0,8,16),new Vector2(.5f,.5f),8);host=new GameObject("Body",typeof(SpriteRenderer));body=host.GetComponent<SpriteRenderer>();body.sprite=sprite;body.sortingOrder=3;}
        [TearDown] public void Cleanup(){Object.DestroyImmediate(host);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        [TestCase(DefensiveFieldKind.Barrier)] [TestCase(DefensiveFieldKind.Aura)]
        public void 防御外周は足場の上に収まり既存の頭上余白を越えず本体を変えない(DefensiveFieldKind kind)
        {
            host.transform.position=new Vector3(3,4,0);host.transform.localScale=Vector3.one*2;var original=body.bounds;
            using(var view=new DefensiveFieldVisual()){
                view.Refresh(body,kind==DefensiveFieldKind.Barrier,kind==DefensiveFieldKind.Aura);
                Assert.GreaterOrEqual(view.Renderer.bounds.min.y,original.min.y,"予兆と足先の下へ防御面を広げない");
                Assert.LessOrEqual(view.Renderer.bounds.max.y,original.max.y+original.size.y*.1751f,"既存HUDが空ける最大外周内");
                Assert.AreEqual(original,body.bounds);Assert.AreEqual(new Vector3(3,4,0),host.transform.position);Assert.Less(view.Renderer.sortingOrder,body.sortingOrder);
            }
        }
        [TestCase(DefensiveFieldKind.Barrier)] [TestCase(DefensiveFieldKind.Aura)]
        public void 専用防御素材は中央透明の二百五十六角で共有包装と一致する(DefensiveFieldKind kind)
        {
            var name=kind.ToString().ToLowerInvariant();var bytes=Resources.Load<TextAsset>("OlcaDefensiveFields/"+name);Assert.IsNotNull(bytes);CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Art/Effects/DefensiveFields",name+".png")),bytes.bytes);
            var image=new Texture2D(2,2);try{Assert.IsTrue(image.LoadImage(bytes.bytes));Assert.AreEqual(256,image.width);Assert.AreEqual(256,image.height);CollectionAssert.AreEqual(new byte[]{0,255},image.GetPixels32().Select(p=>p.a).Distinct().OrderBy(v=>v).ToArray());Assert.IsTrue(image.GetPixels(80,80,96,96).All(p=>p.a==0));}finally{Object.DestroyImmediate(image);}
            var shared=DefensiveFieldVisual.GetSprite(kind);Assert.IsNotNull(shared);Assert.AreSame(shared,DefensiveFieldVisual.GetSprite(kind));Assert.AreEqual(FilterMode.Point,shared.texture.filterMode);Assert.IsFalse(shared.texture.isReadable);
        }
        [TestCase("grunt_256")] [TestCase("boss_512")]
        public void 既存敵の透明余白を含めず元PNGの不透明領域に輪郭を合わせる(string name)
        {
            var data=File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Art/Sprites/Enemies",name+".png"));
            var image=new Texture2D(2,2);Sprite actor=null;try{
                Assert.IsTrue(image.LoadImage(data));actor=Sprite.Create(image,new Rect(0,0,image.width,image.height),new Vector2(.5f,.5f),100);actor.name=name;body.sprite=actor;body.flipX=true;host.transform.position=new Vector3(3,4,0);host.transform.localScale=new Vector3(2,1,1);
                int minX=image.width,minY=image.height,maxX=-1,maxY=-1;var pixels=image.GetPixels32();for(int y=0;y<image.height;y++)for(int x=0;x<image.width;x++)if(pixels[y*image.width+x].a>=128){minX=System.Math.Min(minX,x);minY=System.Math.Min(minY,y);maxX=System.Math.Max(maxX,x);maxY=System.Math.Max(maxY,y);}
                var center=new Vector3(-((minX+maxX+1)*.5f-actor.pivot.x)/100,((minY+maxY+1)*.5f-actor.pivot.y)/100,0);
                var bounds=DefensiveFieldVisual.GetBodyBounds(body);Assert.That(Vector3.Distance(host.transform.TransformPoint(center),bounds.center),Is.LessThan(.0001f));Assert.That(bounds.size.x,Is.EqualTo((maxX-minX+1)/100f*2).Within(.0001f));Assert.That(bounds.size.y,Is.EqualTo((maxY-minY+1)/100f).Within(.0001f));
            }finally{body.sprite=sprite;if(actor!=null)Object.DestroyImmediate(actor);Object.DestroyImmediate(image);}
        }
        [Test] public void 連続付与と種類置換は一描画を共有し本体の画像位置回転倍率を変更しない()
        {
            host.transform.position=new Vector3(3,4,0);host.transform.localScale=new Vector3(2,2,1);var position=host.transform.position;var rotation=host.transform.rotation;var scale=host.transform.localScale;
            using(var view=new DefensiveFieldVisual()){
                for(int n=0;n<20;n++)view.Refresh(body,true,false);var renderer=view.Renderer;Assert.AreEqual(1,host.GetComponentsInChildren<SpriteRenderer>().Count(r=>r!=body));Assert.AreEqual(DefensiveFieldKind.Barrier,view.VisibleKind);Assert.Less(renderer.sortingOrder,body.sortingOrder);
                view.Refresh(body,false,true);Assert.AreSame(renderer,view.Renderer);Assert.AreEqual(DefensiveFieldKind.Aura,view.VisibleKind);Assert.AreSame(DefensiveFieldVisual.GetSprite(DefensiveFieldKind.Aura),renderer.sprite);
                Assert.AreEqual(position,host.transform.position);Assert.AreEqual(rotation,host.transform.rotation);Assert.AreEqual(scale,host.transform.localScale);Assert.AreSame(sprite,body.sprite);
                view.Refresh(body,false,false);Assert.IsNull(view.VisibleKind);Assert.IsFalse(renderer.enabled);view.Refresh(body,true,false);Assert.AreSame(renderer,view.Renderer);
            }
            Assert.AreEqual(1,host.GetComponentsInChildren<SpriteRenderer>().Length);
        }
        [Test] public void 隠蔽と無効化と破棄で残留せず無効な二重状態を勝手に選ばない()
        {
            var view=new DefensiveFieldVisual();try{view.Refresh(body,true,false);body.forceRenderingOff=true;view.Refresh(body,true,false);Assert.IsNull(view.VisibleKind);Assert.IsFalse(view.Renderer.enabled);body.forceRenderingOff=false;view.Refresh(body,true,false);Assert.IsNotNull(view.VisibleKind);body.enabled=false;view.Refresh(body,true,false);Assert.IsNull(view.VisibleKind);body.enabled=true;view.Refresh(body,true,true);Assert.IsNull(view.VisibleKind);Assert.IsNull(DefensiveFieldVisual.GetSprite((DefensiveFieldKind)99));view.Refresh(body,true,false);view.Dispose();view.Refresh(body,true,false);Assert.IsNull(view.VisibleKind);Assert.IsNull(view.Renderer);}finally{view.Dispose();}
        }
        [Test] public void 敵の通常更新は死亡再束縛と無効化時に防御を消し隠蔽復帰だけで古い状態を戻さない()
        {
            var view=host.AddComponent<EnemyView>();Set(view,"enemyImage",body);var enemy=new Enemy("grunt","テスト",100,ElementType.None,new Olca.Core.Domain.Vector2Int(2,0));enemy.SetBarrier(50,120);view.Initialize(enemy);view.Refresh();var field=(DefensiveFieldVisual)view.GetType().GetField("defensiveField",Hidden).GetValue(view);Assert.AreEqual(DefensiveFieldKind.Barrier,field.VisibleKind);
            body.forceRenderingOff=true;view.Refresh();Assert.IsNull(field.VisibleKind);enemy.SetAura(60);body.forceRenderingOff=false;view.Refresh();Assert.AreEqual(DefensiveFieldKind.Aura,field.VisibleKind);
            view.enabled=false;Call(view,"OnDisable");view.Refresh();Assert.IsNull(field.VisibleKind);view.enabled=true;view.Refresh();Assert.AreEqual(DefensiveFieldKind.Aura,field.VisibleKind);
            var next=new Enemy("grunt","テスト",100,ElementType.None,new Olca.Core.Domain.Vector2Int(3,0));view.Initialize(next);view.Refresh();Assert.IsNull(field.VisibleKind);next.SetBarrier(50);view.Refresh();next.SetBarrier(0);next.TakeDamage(10000,ElementType.None);view.Refresh();Assert.IsNull(field.VisibleKind);
        }
        [Test] public void プレイヤーの読取更新は効果時計を進めず終了と無効化で消去する()
        {
            var player=new PlayerCharacter(100);var card=new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,1,"");var dungeon=new DungeonSystem(DungeonConfig.CreateTestDungeon(6,30,4),player,new Deck(new System.Collections.Generic.List<Card>{card}),new MockRandomProvider(),new MockLogger());
            var view=host.AddComponent<GameFieldView>();Set(view,"playerSprite",body);Set(view,"dungeonSystem",dungeon);player.SetBarrier(50,120);for(int n=0;n<30;n++)view.RefreshBattleField();var field=(DefensiveFieldVisual)view.GetType().GetField("defensiveField",Hidden).GetValue(view);Assert.AreEqual(DefensiveFieldKind.Barrier,field.VisibleKind);Assert.AreEqual(120,player.ProtectionRemainingFrames);Assert.AreEqual(50,player.BarrierHp);
            view.enabled=false;Call(view,"OnDisable");Call(view,"RefreshDefensiveField");Assert.IsNull(field.VisibleKind);view.enabled=true;Call(view,"RefreshDefensiveField");Assert.IsNotNull(field.VisibleKind);view.OnBattleEnded();Assert.IsNull(field.VisibleKind);player.SetBarrier(0);view.RefreshBattleField();Assert.IsNull(field.VisibleKind);
        }
    }
}
