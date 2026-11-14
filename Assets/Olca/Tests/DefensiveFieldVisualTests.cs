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
        private Olca.Unity.GeneratedSpriteAssets.Lease generatedScope;
        [SetUp] public void AcquireGeneratedScope()=>generatedScope=Olca.Unity.GeneratedSpriteAssets.Acquire();
        [TearDown] public void ReleaseGeneratedScope(){generatedScope?.Dispose();generatedScope=null;Olca.Unity.GeneratedSpriteAssets.Prune();}

        [TestCase(-1f)][TestCase(1f)] public void 試用カエン識別は通常と死亡を描かず本体と能力を変えず後方だけに保持する(float facing)
        {
            var normal=new PlayerCharacter(100);var fire=new PlayerCharacter(100);fire.SetDepartureStyle(DepartureStyleDefinition.TrialFire);var original=body.sprite;host.transform.localScale=new Vector3(facing*2,2,1);
            using(var view=new TrialFireKamuiVisual()){
                view.Refresh(body,normal);Assert.IsNull(view.Renderer);view.Refresh(body,fire);var layer=view.Renderer;Assert.IsNotNull(layer);Assert.IsTrue(layer.enabled);Assert.AreEqual(new Rect(0,0,256,256),layer.sprite.rect);Assert.AreEqual(100f,layer.sprite.pixelsPerUnit,.001f);Assert.AreEqual(TrialFireKamuiVisual.SizeWorld,layer.bounds.size.x,.001f);Assert.AreEqual(TrialFireKamuiVisual.SizeWorld,layer.bounds.size.y,.001f);Assert.Less(layer.sortingOrder,body.sortingOrder);Assert.AreSame(body.transform,layer.transform.parent);Assert.AreEqual(TrialFireKamuiVisual.Opacity,layer.color.a);Assert.AreSame(original,body.sprite);Assert.AreEqual(Color.white,body.color);Assert.AreEqual(100,fire.CurrentHp);
                view.Refresh(body,fire);Assert.AreSame(layer,view.Renderer);view.Refresh(body,normal);Assert.IsFalse(layer.enabled);view.Refresh(body,fire);Assert.IsTrue(layer.enabled);body.forceRenderingOff=true;view.Refresh(body,fire);Assert.IsFalse(layer.enabled);body.forceRenderingOff=false;fire.SetHp(0);view.Refresh(body,fire);Assert.IsFalse(layer.enabled);
            }
            Assert.IsFalse(host.GetComponentsInChildren<SpriteRenderer>(true).Any(r=>r!=body));Assert.AreSame(original,body.sprite);
        }

        [Test] public void カエン本体は全姿勢を一括検証し通常や未知姿勢では読込せず借用素材を破棄しない()
        {
            var fire=new PlayerCharacter(100);fire.SetDepartureStyle(DepartureStyleDefinition.TrialFire);var normal=new PlayerCharacter(100);int loads=0;
            using(var costume=new TrialFireBodyMotion(key=>{loads++;return Resources.Load<Sprite>(TrialFireBodyMotion.ResourceRoot+key);})){
                sprite.name="move_01";Assert.AreSame(sprite,costume.Resolve(sprite,normal));Assert.AreEqual(0,loads);
                sprite.name="future_attack";Assert.AreSame(sprite,costume.Resolve(sprite,fire));Assert.AreEqual(0,loads);
                string[] originals={"heroine_idle_10_0","move_01","move_02","move_03","move_04","move_05","move_06","move_07","move_08","jump_rise","jump_apex","jump_fall","heroine_dash_low_v4_nn256","WallSlide_01","WallSlide_02","trial_ground_kick_prepare","trial_ground_kick_contact","trial_air_forward_kick_contact"};
                string[] keys={"idle","run_01","run_02","run_03","run_04","run_05","run_06","run_07","run_08","jump_rise","jump_apex","jump_fall","dash","wall_slide_01","wall_slide_02","ground_kick_prepare","ground_kick_contact","air_forward_contact"};
                for(int n=0;n<keys.Length;n++){sprite.name=originals[n];var actual=costume.Resolve(sprite,fire);Assert.AreEqual("trial_fire_"+keys[n],actual.name);Assert.AreEqual(256,actual.texture.width);Assert.AreEqual(256,actual.texture.height);Assert.AreEqual(100,actual.pixelsPerUnit);}
                Assert.AreEqual(23,loads);Assert.AreEqual(100,fire.CurrentHp);fire.SetHp(0);Assert.AreSame(sprite,costume.Resolve(sprite,fire));Assert.AreEqual(23,loads);
                fire.SetHp(100);costume.Dispose();Assert.AreSame(sprite,costume.Resolve(sprite,fire));Assert.IsNotNull(Resources.Load<Sprite>(TrialFireBodyMotion.ResourceRoot+"idle"));
            }
        }
        [TestCase("run_06")][TestCase("air_forward_contact")][TestCase("idle_breathe_high")][TestCase("knockback_02")]
        public void カエン本体の素材欠損は全姿勢を通常へ戻し毎描画で再読込しない(string missing)
        {
            var fire=new PlayerCharacter(100);fire.SetDepartureStyle(DepartureStyleDefinition.TrialFire);int loads=0;
            using(var costume=new TrialFireBodyMotion(key=>{loads++;return key==missing?null:Resources.Load<Sprite>(TrialFireBodyMotion.ResourceRoot+key);})){sprite.name="move_01";Assert.AreSame(sprite,costume.Resolve(sprite,fire));int count=loads;for(int n=0;n<10;n++){sprite.name=n%2==0?"jump_rise":"move_08";Assert.AreSame(sprite,costume.Resolve(sprite,fire));}Assert.AreEqual(count,loads);}
        }

        [Test] public void カエン本体の誤登録素材は全体を不採用にする()
        {
            var fire=new PlayerCharacter(100);fire.SetDepartureStyle(DepartureStyleDefinition.TrialFire);sprite.name="jump_rise";int loads=0;
            using(var costume=new TrialFireBodyMotion(key=>{loads++;return key=="run_02"?sprite:Resources.Load<Sprite>(TrialFireBodyMotion.ResourceRoot+key);})){Assert.AreSame(sprite,costume.Resolve(sprite,fire));int count=loads;sprite.name="move_01";Assert.AreSame(sprite,costume.Resolve(sprite,fire));Assert.AreEqual(count,loads);}
        }

        [Test] public void カエン呼吸は既存十コマを三姿勢で往復し接敵三姿勢も同じ一式から借りる()
        {
            var fire=new PlayerCharacter(100);fire.SetDepartureStyle(DepartureStyleDefinition.TrialFire);
            using(var costume=new TrialFireBodyMotion()){
                string[] poses={"idle","idle_breathe_mid","idle_breathe_high","idle_breathe_high","idle_breathe_high","idle_breathe_mid","idle","idle","idle","idle"};
                for(int n=0;n<10;n++){sprite.name="heroine_idle_"+(n+1).ToString("00");var next=costume.Resolve(sprite,fire);Assert.AreEqual("trial_fire_"+poses[n],next.name);Assert.AreEqual(new Vector2(127,14),next.rect.position+next.pivot);}
                for(int n=1;n<=3;n++){sprite.name="knockback_"+n.ToString("00")+"_0";Assert.AreEqual("trial_fire_knockback_"+n.ToString("00"),costume.Resolve(sprite,fire).name);}
            }
        }
        private static Olca.Core.Logic.Battle.BattleSystem BodyBattle(bool fire)
        {
            var p=new PlayerCharacter(100);if(fire)p.SetDepartureStyle(DepartureStyleDefinition.TrialFire);var e=new Enemy("body257","シヨウ",1000,ElementType.None,new Olca.Core.Domain.Vector2Int(1,0));e.ApplyStun(60000);
            var b=new Olca.Core.Logic.Battle.BattleSystem(p,new System.Collections.Generic.List<Enemy>{e},new Deck(),logger:new MockLogger());b.ConfigureBasicAttackTiming(new PlayerAttackTiming(3,8));return b;
        }
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public void 戦闘試用蹴りは既存phaseだけ読み停止取消とカードへの切替で元姿勢とAnimatorを復元する(bool enabled,bool fire)
        {
            var animator=host.AddComponent<Animator>();animator.enabled=enabled;body.flipX=true;var original=body.sprite;var position=host.transform.position;var b=BodyBattle(fire);
            using(var pose=new TrialBasicKickBody(body,animator)){
                Assert.IsFalse(b.IsPlayerBasicAction);Assert.IsFalse(pose.Apply(b));Assert.IsTrue(b.TryStartPlayerBasicAttack(false));Assert.IsTrue(b.IsPlayerBasicAction);
                for(int n=0;n<3;n++){Assert.IsTrue(pose.Apply(b));Assert.AreEqual("trial_ground_kick_prepare",body.sprite.name);Assert.IsFalse(animator.enabled);b.TickFrame(17);}
                Assert.AreEqual(Olca.Core.Domain.PlayerAttackPhase.Recovery,b.PlayerActionPhase);Assert.AreEqual(0,b.RecoveryElapsedFrames);Assert.AreEqual(990,b.Enemies[0].CurrentHp);
                b.Pause(Olca.Core.Logic.Battle.BattlePauseReason.User);for(int n=0;n<4;n++){b.TickFrame(17);Assert.IsTrue(pose.Apply(b));Assert.AreEqual("trial_ground_kick_contact",body.sprite.name);Assert.AreEqual(0,b.RecoveryElapsedFrames);}b.Resume(Olca.Core.Logic.Battle.BattlePauseReason.User);
                for(int n=0;n<3;n++){int age=b.RecoveryElapsedFrames;for(int i=0;i<3;i++)Assert.IsTrue(pose.Apply(b));Assert.AreEqual(age,b.RecoveryElapsedFrames);Assert.AreEqual("trial_ground_kick_contact",body.sprite.name);b.TickFrame(17);}
                Assert.IsTrue(pose.Apply(b));Assert.AreEqual("trial_ground_kick_prepare",body.sprite.name);for(int n=0;n<8&&b.HasPlayerAction;n++)b.TickFrame(17);Assert.IsFalse(b.IsPlayerBasicAction);Assert.IsFalse(pose.Apply(b));Assert.AreSame(original,body.sprite);Assert.AreEqual(enabled,animator.enabled);Assert.AreEqual(990,b.Enemies[0].CurrentHp);
                Assert.IsTrue(b.TryStartPlayerBasicAttack(false));pose.Apply(b);b.Pause(Olca.Core.Logic.Battle.BattlePauseReason.User);b.CancelBasicAction();Assert.IsFalse(pose.Apply(b));Assert.AreSame(original,body.sprite);Assert.AreEqual(enabled,animator.enabled);Assert.IsTrue(b.IsPaused);b.Resume(Olca.Core.Logic.Battle.BattlePauseReason.User);
                b.CardUsage.EnqueueCards(new System.Collections.Generic.List<Card>{new Card("sword","シヨウ",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,1,"",startupTime:.1f,recoveryTime:.2f)});var use=b.CardUsage.TryUseCurrentCardAsync();Assert.IsTrue(b.HasPlayerAction);Assert.IsFalse(b.IsPlayerBasicAction);Assert.IsFalse(pose.Apply(b));Assert.AreSame(original,body.sprite);b.CardUsage.Clear();Assert.IsTrue(body.flipX);Assert.AreEqual(position,host.transform.position);Assert.AreEqual(100,b.Player.CurrentHp);
            }
        }
        [Test] public void 戦闘試用蹴りは素材欠損で元Animatorを所有せず借用Spriteを破棄しない()
        {
            var animator=host.AddComponent<Animator>();var b=BodyBattle(false);var original=body.sprite;int loads=0;
            using(var pose=new TrialBasicKickBody(body,animator,key=>{loads++;return key=="contact"?null:Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_kick_"+key);})){
                Assert.IsTrue(b.TryStartPlayerBasicAttack(false));for(int n=0;n<5;n++)Assert.IsFalse(pose.Apply(b));Assert.AreSame(original,body.sprite);Assert.IsTrue(animator.enabled);Assert.AreEqual(2,loads);b.CancelBasicAction();
            }
            Assert.IsNotNull(Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_kick_prepare"));
        }
        [TestCase(false)][TestCase(true)] public void 戦闘カエン本体はAnimator付き接敵解除直後と停止中断と再表示で通常衣装を残さない(bool enabled)
        {
            var animator=host.AddComponent<Animator>();var template=Resources.Load<GameObject>("OlcaSystem/WorldPresentation").GetComponent<GameFieldView>();var authored=(Animator)typeof(GameFieldView).GetField("playerAnimator",Hidden).GetValue(template);animator.runtimeAnimatorController=authored.runtimeAnimatorController;animator.Rebind();animator.Update(0);animator.enabled=enabled;
            var idle=UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprites/Characters/Heroine/Idle/heroine_idle_01.png");body.sprite=idle;var b=BodyBattle(true);var view=host.AddComponent<GameFieldView>();Set(view,"playerSprite",body);Set(view,"playerAnimator",animator);Set(view,"idlePlayerSprite",idle);Set(view,"currentBattle",b);Set(view,"contactKnockbackSprites",Enumerable.Range(1,3).Select(n=>UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Sprites/Characters/Heroine/Contact/knockback_"+n.ToString("00")+".png")).ToArray());var pauseOwner=new object();
            try{
                Call(view,"UpdatePlayerSprite");Assert.AreEqual("trial_fire_idle",body.sprite.name);
                for(int n=0;n<3;n++){view.SetContactPose(Olca.Presentation.Dungeon.ContactVisualPhase.Knockback,n/2f);StringAssert.StartsWith("trial_fire_knockback_",body.sprite.name);view.SetContactPose(Olca.Presentation.Dungeon.ContactVisualPhase.None,0);Assert.AreEqual("trial_fire_idle",body.sprite.name);Assert.AreEqual(enabled,animator.enabled);}
                Assert.IsTrue(b.TryStartPlayerBasicAttack(false));Call(view,"UpdatePlayerSprite");Assert.AreEqual("trial_fire_ground_kick_prepare",body.sprite.name);for(int n=0;n<3;n++)b.TickFrame(17);Call(view,"UpdatePlayerSprite");Assert.AreEqual("trial_fire_ground_kick_contact",body.sprite.name);
                b.Pause(Olca.Core.Logic.Battle.BattlePauseReason.User);Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,true);for(int n=0;n<4;n++){b.TickFrame(17);Call(view,"UpdatePlayerSprite");Assert.AreEqual("trial_fire_ground_kick_contact",body.sprite.name);Assert.AreEqual(0,b.RecoveryElapsedFrames);}b.CancelBasicAction();Call(view,"UpdatePlayerSprite");Assert.AreEqual("trial_fire_idle",body.sprite.name);Assert.AreEqual(enabled,animator.enabled);Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,false);b.Resume(Olca.Core.Logic.Battle.BattlePauseReason.User);
                view.enabled=false;Call(view,"OnDisable");Assert.AreSame(idle,body.sprite,"無効化callback後");view.enabled=true;Call(view,"UpdatePlayerSprite");Assert.AreEqual("trial_fire_idle",body.sprite.name);Set(view,"currentBattle",BodyBattle(false));Call(view,"UpdatePlayerSprite");Assert.AreSame(idle,body.sprite);
                Set(view,"currentBattle",b);Assert.IsTrue(b.TryStartPlayerBasicAttack(false));Call(view,"UpdatePlayerSprite");Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,true);b.Player.SetHp(0);Call(view,"UpdatePlayerSprite");Assert.AreSame(idle,body.sprite);Assert.AreEqual(enabled,animator.enabled);Assert.IsTrue(Olca.Unity.BattleVisualClock.IsPaused);
            }finally{Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,false);b.CancelBasicAction();}
        }

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
