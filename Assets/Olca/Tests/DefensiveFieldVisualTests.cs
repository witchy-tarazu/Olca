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

        [Test] public void ピクセル軌跡は各段を区別し矩形内でフェードして無描画終端を持つ()
        {
            var shapes=new System.Collections.Generic.HashSet<string>();
            for(int kind=0;kind<7;kind++){
                var keys=new System.Collections.Generic.List<string>();
                for(int n=0;n<ProvisionalPixelPattern.ArcCount;n++){
                    Assert.IsTrue(ProvisionalPixelPattern.Arc(n,kind,1,1,Color.cyan,out var rect,out var ink));
                    Assert.GreaterOrEqual(rect.xMin,0);Assert.GreaterOrEqual(rect.yMin,0);Assert.LessOrEqual(rect.xMax,1);Assert.LessOrEqual(rect.yMax,1);Assert.Greater(ink.a,0);keys.Add(rect.ToString("F4"));
                    Assert.IsFalse(ProvisionalPixelPattern.Arc(n,kind,1,0,Color.cyan,out _,out _));
                }
                if(kind<3)Assert.IsTrue(shapes.Add(string.Join(";",keys)),"地上3段を別の軌跡にする");
            }
            for(int n=0;n<ProvisionalPixelPattern.BurstCount;n++){Assert.IsFalse(ProvisionalPixelPattern.Burst(n,1,Color.cyan,out _,out _));for(int f=0;f<14;f++)if(ProvisionalPixelPattern.Burst(n,f/14f,Color.cyan,out var rect,out _)){Assert.GreaterOrEqual(rect.xMin,0);Assert.GreaterOrEqual(rect.yMin,0);Assert.LessOrEqual(rect.xMax,1);Assert.LessOrEqual(rect.yMax,1);}}
            for(int kind=0;kind<3;kind++)StringAssert.Contains((kind+1)+"ダンメ",Olca.Presentation.Exploration.OutsideExplorationView.ProvisionalKickCaption((Olca.Core.Logic.Integration.OutsideKickKind)kind,true,false));
        }
        [TestCase(-1f)][TestCase(1f)] public void Worldピクセルは百回再利用し反転拡大でも領域内で借用Textureを破棄しない(float facing)
        {
            var white=Texture2D.whiteTexture;host.transform.localScale=new Vector3(facing*2,3,1);var area=new Rect(2,4,3,2);
            using(var pool=new PixelCombatWorld(host.transform,"Pool265")){
                pool.Warmup();Assert.AreEqual(24,pool.Capacity);var first=host.GetComponentsInChildren<SpriteRenderer>(true);var owned=first.First(v=>v!=body).sprite;
                for(int use=0;use<100;use++){pool.Arc(area,use%7,1,1,Color.cyan,body,facing<0);Assert.Greater(pool.ActiveCount,0);foreach(var q in host.GetComponentsInChildren<SpriteRenderer>(true).Where(v=>v!=body&&v.enabled)){Assert.GreaterOrEqual(q.bounds.min.x,area.xMin-.001f);Assert.LessOrEqual(q.bounds.max.x,area.xMax+.001f);Assert.GreaterOrEqual(q.bounds.min.y,area.yMin-.001f);Assert.LessOrEqual(q.bounds.max.y,area.yMax+.001f);Assert.AreSame(owned,q.sprite);}pool.Clear();Assert.AreEqual(0,pool.ActiveCount);}
                CollectionAssert.AreEqual(first,host.GetComponentsInChildren<SpriteRenderer>(true));pool.Burst(area,1,Color.cyan,body);Assert.AreEqual(0,pool.ActiveCount);
            }
            Assert.AreEqual(1,host.GetComponentsInChildren<SpriteRenderer>(true).Length);Assert.IsTrue(white!=null);Assert.AreSame(white,Texture2D.whiteTexture);
        }
        [TestCase(false)][TestCase(true)] public void 戦闘蹴り軌跡は読取だけで一攻撃一通知と停止再入を守る(bool fire)
        {
            var gridHost=new GameObject("Grid265");var grid=gridHost.AddComponent<WorldBattleGridView>();typeof(WorldBattleGridView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,null);var b=BodyBattle(fire);grid.Show(b.Field,0);
            try{using(var effect=new BattleKickVisual(host.transform,()=>body,()=>grid)){
                effect.Warmup();effect.Bind(b);Assert.IsTrue(b.TryStartPlayerBasicAttack(false));effect.Tick(0);Assert.AreEqual(0,effect.ActivePixels);Assert.AreEqual(0,effect.EmissionCount);
                for(int n=0;n<3;n++)b.TickFrame(17);Assert.AreEqual(1,effect.EmissionCount);int hp=b.Enemies[0].CurrentHp;int age=b.RecoveryElapsedFrames;
                for(int n=0;n<100;n++)effect.Tick(0);Assert.Greater(effect.ActivePixels,0);Assert.AreEqual(hp,b.Enemies[0].CurrentHp);Assert.AreEqual(age,b.RecoveryElapsedFrames);Assert.AreEqual(1,effect.EmissionCount);
                b.Pause(Olca.Core.Logic.Battle.BattlePauseReason.User);for(int n=0;n<10;n++)effect.Tick(1);Assert.Greater(effect.ActivePixels,0);Assert.AreEqual(age,b.RecoveryElapsedFrames);b.Resume(Olca.Core.Logic.Battle.BattlePauseReason.User);
                effect.Bind(null);Assert.AreEqual(0,effect.ActivePixels);for(int n=0;n<12;n++)b.TickFrame(17);Assert.IsTrue(b.TryStartPlayerBasicAttack(false));for(int n=0;n<3;n++)b.TickFrame(17);Assert.AreEqual(1,effect.EmissionCount,"購読解除後は古い戦闘を追わない");
                var next=BodyBattle(fire);effect.Bind(next);grid.Show(next.Field,0);Assert.IsTrue(next.TryStartPlayerBasicAttack(false));for(int n=0;n<3;n++)next.TickFrame(17);Assert.AreEqual(2,effect.EmissionCount);Assert.AreEqual(0,effect.ObservationErrors);
            }}finally{Object.DestroyImmediate(gridHost);}
        }
        [TestCase(false)] [TestCase(true)] public void 戦闘タメ進捗arcは攻撃通知やダメージを発生せず取消とowner交替で消える(bool fire)
        {
            var gridHost=new GameObject("ChargeGrid295");var grid=gridHost.AddComponent<WorldBattleGridView>();typeof(WorldBattleGridView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,null);var b=BodyBattle(fire);grid.Show(b.Field,0);
            try{using(var effect=new BattleKickVisual(host.transform,()=>body,()=>grid)){
                effect.Warmup();effect.Bind(b);var text=host.GetComponentsInChildren<TextMesh>(true).Single(t=>t.name=="Text");
                effect.Tick(0,new BattleChargeVisualState(b,true,1,60));StringAssert.Contains("タメチュウ",text.text);Assert.Greater(effect.ActivePixels,0);int first=effect.ActivePixels;
                for(int n=0;n<20;n++)effect.Tick(1,new BattleChargeVisualState(b,true,60,60));StringAssert.Contains("カンリョウ",text.text);Assert.GreaterOrEqual(effect.ActivePixels,first);Assert.AreEqual(1000,b.Enemies[0].CurrentHp);Assert.IsFalse(b.HasPlayerAction);Assert.AreEqual(0,effect.EmissionCount);
                effect.Tick(0);Assert.AreEqual(0,effect.ActivePixels);Assert.IsFalse(text.gameObject.activeInHierarchy);
                Assert.IsTrue(b.TryStartPlayerBasicAttack(true));for(int n=0;n<3;n++)b.TickFrame(17);effect.Tick(0);StringAssert.Contains("タメ（カリ）",text.text);Assert.AreEqual(1,effect.EmissionCount);Assert.AreEqual(980,b.Enemies[0].CurrentHp);
                var old=new BattleChargeVisualState(b,true,60,60);var next=BodyBattle(fire);effect.Bind(next);effect.Tick(0,old);Assert.AreEqual(0,effect.ActivePixels);Assert.AreEqual(0,effect.ObservationErrors);
            }}finally{Object.DestroyImmediate(gridHost);}
        }
        [TestCase(false)] [TestCase(true)] public void 戦闘移動中もタメ演出を表示し本体Animatorを奪わない(bool fire)
        {
            var b=BodyBattle(fire);var animator=host.AddComponent<Animator>();var original=body.sprite;Assert.IsTrue(b.PlayerMovement.TryStartNormalMove(new Olca.Core.Domain.Vector2Int(1,0),new System.Collections.Generic.List<Enemy>()));Assert.IsTrue(b.PlayerMovement.IsNormalMoving);
            var gridHost=new GameObject("Charge350MovingGrid");var grid=gridHost.AddComponent<WorldBattleGridView>();typeof(WorldBattleGridView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,null);grid.Show(b.Field,0);
            try{using(var pose=new TrialBasicKickBody(body,animator))using(var effect=new BattleKickVisual(host.transform,()=>body,()=>grid)){
                effect.Bind(b);var charge=new BattleChargeVisualState(b,true,60,60);Assert.IsTrue(charge.CanDisplayFor(b));Assert.IsFalse(pose.Apply(b,charge));effect.Tick(0,charge);Assert.Greater(effect.ActivePixels,0);Assert.AreSame(original,body.sprite);Assert.IsTrue(animator.enabled);Assert.IsTrue(b.PlayerMovement.IsNormalMoving);Assert.IsFalse(b.HasPlayerAction);Assert.AreEqual(100,b.Player.CurrentHp);
            }}finally{Object.DestroyImmediate(gridHost);}
        }
        [TestCase(false)][TestCase(true)]
        public void 戦闘溜め476は同じ粒の年齢を保ち四frameで白金へ移り解放再開で初期化する(bool fire)
        {
            var gridHost=new GameObject("Charge476Grid");var grid=gridHost.AddComponent<WorldBattleGridView>();typeof(WorldBattleGridView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,null);var b=BodyBattle(fire);grid.Show(b.Field,0);var animator=host.AddComponent<Animator>();var original=body.sprite;
            try{using(var effect=new BattleKickVisual(host.transform,()=>body,()=>grid))using(var pose=new TrialBasicKickBody(body,animator)){
                effect.Bind(b);host.transform.localScale=new Vector3(.6f,1,1);
                for(int f=1;f<=64;f++){
                    b.TickFrame(17);effect.Tick(f%2==0?1000:0,new BattleChargeVisualState(b,true,System.Math.Min(f,60),60));
                    var graphic=host.GetComponentInChildren<ProvisionalCombatGraphic>(true);Assert.AreEqual(f-1,graphic.ChargeParticleAge);Assert.AreEqual(f<60?-1:f-60,graphic.ChargeParticleFullAge);Assert.AreEqual(Mathf.Clamp01((f-60)/4f),graphic.ChargeParticleGold);
                    Assert.That(Mathf.Abs(graphic.transform.lossyScale.x),Is.EqualTo(body.bounds.size.y).Within(.001f),"縦長の本体でも既存輪郭を身体の外へ置く表示幅");Assert.AreEqual(10,effect.ActivePixels);Assert.IsFalse(pose.Apply(b));Assert.IsTrue(animator.enabled);Assert.AreSame(original,body.sprite);
                }
                Assert.AreEqual(100,b.Player.CurrentHp);Assert.AreEqual(1000,b.Enemies[0].CurrentHp);Assert.AreEqual(0,effect.EmissionCount);Assert.IsFalse(b.HasPlayerAction);
                effect.Tick(0);Assert.AreEqual(0,effect.ActivePixels);var fx=host.GetComponentInChildren<ProvisionalCombatGraphic>(true);Assert.IsFalse(fx.IsChargeParticleMode);
                b.TickFrame(17);effect.Tick(0,new BattleChargeVisualState(b,true,1,60));Assert.AreEqual(0,fx.ChargeParticleAge);Assert.AreEqual(-1,fx.ChargeParticleFullAge);Assert.AreEqual(0,fx.ChargeParticleGold);
                effect.Bind(BodyBattle(fire));effect.Tick(0,new BattleChargeVisualState(b,true,60,60));Assert.AreEqual(0,effect.ActivePixels);
            }}finally{Object.DestroyImmediate(gridHost);}
        }
        [TestCase(Olca.Core.Logic.Battle.BattlePauseReason.User)][TestCase(Olca.Core.Logic.Battle.BattlePauseReason.CustomScreen)]
        public void 戦闘溜め476は停止中に描画年齢と頂点を進めず再開後のみ進む(Olca.Core.Logic.Battle.BattlePauseReason reason)
        {
            var gridHost=new GameObject("Charge476PauseGrid");var grid=gridHost.AddComponent<WorldBattleGridView>();typeof(WorldBattleGridView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,null);var b=BodyBattle(false);grid.Show(b.Field,0);
            try{using(var effect=new BattleKickVisual(host.transform,()=>body,()=>grid)){
                effect.Bind(b);for(int f=1;f<=62;f++){b.TickFrame(17);effect.Tick(0,new BattleChargeVisualState(b,true,System.Math.Min(f,60),60));}
                var fx=host.GetComponentInChildren<ProvisionalCombatGraphic>(true);var fill=typeof(ProvisionalCombatGraphic).GetMethod("OnPopulateMesh",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(UnityEngine.UI.VertexHelper)},null);var before=new System.Collections.Generic.List<UIVertex>();
                using(var vh=new UnityEngine.UI.VertexHelper()){fill.Invoke(fx,new object[]{vh});vh.GetUIVertexStream(before);}
                long age=fx.ChargeParticleAge,frame=b.LogicalFrame;b.Pause(reason);
                for(int n=0;n<20;n++){b.TickFrame(17);effect.Tick(999,new BattleChargeVisualState(b,true,60,60));}
                Assert.AreEqual(frame,b.LogicalFrame);Assert.AreEqual(age,fx.ChargeParticleAge);Assert.AreEqual(.5f,fx.ChargeParticleGold);
                using(var vh=new UnityEngine.UI.VertexHelper()){fill.Invoke(fx,new object[]{vh});var after=new System.Collections.Generic.List<UIVertex>();vh.GetUIVertexStream(after);CollectionAssert.AreEqual(before,after);}
                b.Resume(reason);b.TickFrame(17);effect.Tick(0,new BattleChargeVisualState(b,true,60,60));Assert.AreEqual(age+1,fx.ChargeParticleAge);Assert.AreEqual(.75f,fx.ChargeParticleGold);
                effect.Bind(null);Assert.AreEqual(0,effect.ActivePixels);
            }}finally{Object.DestroyImmediate(gridHost);}
        }
        [TestCase(-1f)][TestCase(1f)]
        public void 戦闘溜め476のWorld表示は反転拡大と移動非表示で位相を保ち破棄時に資源を残さない(float facing)
        {
            host.transform.localScale=new Vector3(facing*2,3,1);var area=new Rect(2,4,3,2);var original=body.sprite;
            using(var effect=new BattleChargeParticleWorld(host.transform)){
                effect.Warmup();var all=host.GetComponentsInChildren<Transform>(true);var fx=host.GetComponentInChildren<ProvisionalCombatGraphic>(true);
                effect.Show(area,59,59,60,body);Assert.AreEqual(58,fx.ChargeParticleAge);Assert.AreEqual(0,fx.ChargeParticleGold);Assert.IsFalse(fx.raycastTarget);Assert.IsNotNull(fx.GetComponent<CanvasRenderer>());
                body.forceRenderingOff=true;effect.Show(area,60,60,60,body);Assert.AreEqual(0,effect.ActiveCount);Assert.AreEqual(59,fx.ChargeParticleAge);
                body.forceRenderingOff=false;effect.Show(area,64,60,60,body);Assert.AreEqual(63,fx.ChargeParticleAge);Assert.AreEqual(1,fx.ChargeParticleGold);Assert.AreEqual(10,effect.ActiveCount);
                Assert.That(fx.transform.position.x,Is.EqualTo(area.center.x).Within(.001));Assert.That(fx.transform.position.y,Is.EqualTo(area.center.y).Within(.001));Assert.That(Mathf.Abs(fx.transform.lossyScale.x),Is.EqualTo(area.width).Within(.001));Assert.That(Mathf.Abs(fx.transform.lossyScale.y),Is.EqualTo(area.height).Within(.001));Assert.AreSame(original,body.sprite);CollectionAssert.AreEqual(all,host.GetComponentsInChildren<Transform>(true));
                effect.Clear();Assert.AreEqual(0,effect.ActiveCount);Assert.IsFalse(fx.IsChargeParticleMode);
            }
            Assert.IsEmpty(host.GetComponentsInChildren<ProvisionalCombatGraphic>(true));Assert.IsEmpty(host.GetComponentsInChildren<Canvas>(true));Assert.AreSame(original,body.sprite);
        }
        [TestCase(-1f)][TestCase(1f)] public void World撃破メッシュは百回再利用し非入力で破棄後に資源を残さない(float facing)
        {
            host.transform.localScale=new Vector3(facing*2,3,1);var area=new Rect(2,4,3,2);
            using(var burst=new PixelBurstWorld(host.transform)){
                burst.Warmup();var all=host.GetComponentsInChildren<Transform>(true);Assert.AreEqual(13,burst.Capacity);
                for(int n=0;n<100;n++){burst.Burst(area,.2f,Color.cyan,body);Assert.Greater(burst.ActiveCount,0);var graphic=host.GetComponentInChildren<ProvisionalCombatGraphic>(true);Assert.IsNotNull(graphic.GetComponent<CanvasRenderer>(),"描画に必須のRendererを持つ");Assert.IsFalse(graphic.raycastTarget);Assert.AreEqual(area.center.x,graphic.transform.position.x,.001f);Assert.AreEqual(area.center.y,graphic.transform.position.y,.001f);Assert.AreEqual(area.width,Mathf.Abs(graphic.transform.lossyScale.x),.001f);burst.Clear();Assert.AreEqual(0,burst.ActiveCount);}
                CollectionAssert.AreEqual(all,host.GetComponentsInChildren<Transform>(true));burst.Burst(area,1,Color.cyan,body);Assert.AreEqual(0,burst.ActiveCount);
            }
            Assert.IsEmpty(host.GetComponentsInChildren<Canvas>(true));Assert.IsEmpty(host.GetComponentsInChildren<ProvisionalCombatGraphic>(true));
        }
        [Test] public void 最後の敵への通常命中は終端でも短い軌跡を残し停止と購読解除で重複しない()
        {
            var gridHost=new GameObject("Terminal265");var grid=gridHost.AddComponent<WorldBattleGridView>();typeof(WorldBattleGridView).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,null);var b=BodyBattle(false);b.Enemies[0].TakeDamage(990,ElementType.None);grid.Show(b.Field,0);
            try{using(var effect=new BattleKickVisual(host.transform,()=>body,()=>grid)){
                effect.Bind(b);Assert.IsTrue(b.TryStartPlayerBasicAttack(false));for(int n=0;n<3;n++)b.TickFrame(17);Assert.IsTrue(b.IsGameOver);Assert.AreEqual(1,effect.EmissionCount);
                effect.Tick(0);Assert.Greater(effect.ActivePixels,0);for(int n=0;n<20;n++)effect.Tick(0);Assert.Greater(effect.ActivePixels,0);Assert.AreEqual(1,effect.EmissionCount);effect.Tick(.2f);Assert.AreEqual(0,effect.ActivePixels);effect.Bind(null);Assert.AreEqual(0,effect.ObservationErrors);
            }}finally{Object.DestroyImmediate(gridHost);}
        }
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
                string[] originals={"heroine_idle_10_0","move_01","move_02","move_03","move_04","move_05","move_06","move_07","move_08","jump_rise","jump_apex","jump_fall","heroine_dash_low_v4_nn256","WallSlide_01","WallSlide_02","trial_ground_kick_prepare","trial_ground_kick_contact","trial_air_forward_kick_contact","trial_dash_kick_contact","trial_ground_second_prepare","trial_ground_second_contact","trial_ground_third_prepare","trial_ground_third_contact","trial_air_down_contact","trial_air_down_recovery"};
                string[] keys={"idle","run_01","run_02","run_03","run_04","run_05","run_06","run_07","run_08","jump_rise","jump_apex","jump_fall","dash","wall_slide_01","wall_slide_02","ground_kick_prepare","ground_kick_contact","air_forward_contact","dash_kick_contact","ground_second_prepare","ground_second_contact","ground_third_prepare","ground_third_contact","air_down_contact","air_down_recovery"};
                for(int n=0;n<keys.Length;n++){sprite.name=originals[n];var actual=costume.Resolve(sprite,fire);Assert.AreEqual("trial_fire_"+keys[n],actual.name);Assert.AreEqual(256,actual.texture.width);Assert.AreEqual(256,actual.texture.height);Assert.AreEqual(100,actual.pixelsPerUnit);}
                Assert.AreEqual(30,loads);Assert.AreEqual(100,fire.CurrentHp);fire.SetHp(0);Assert.AreSame(sprite,costume.Resolve(sprite,fire));Assert.AreEqual(30,loads);
                fire.SetHp(100);costume.Dispose();Assert.AreSame(sprite,costume.Resolve(sprite,fire));Assert.IsNotNull(Resources.Load<Sprite>(TrialFireBodyMotion.ResourceRoot+"idle"));
            }
        }
        [TestCase("run_06")][TestCase("air_forward_contact")][TestCase("idle_breathe_high")][TestCase("knockback_02")][TestCase("dash_kick_contact")][TestCase("ground_second_contact")][TestCase("ground_third_prepare")][TestCase("air_down_contact")][TestCase("air_down_recovery")]
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
        [TestCase(false,false)] [TestCase(false,true)] [TestCase(true,false)] [TestCase(true,true)]
        public void 戦闘タメ本体は保持中の姿勢を上書きせず解放攻撃だけを表示する(bool fire,bool flip)
        {
            var animator=host.AddComponent<Animator>();animator.enabled=true;body.flipX=flip;var original=body.sprite;var position=host.transform.position;var b=BodyBattle(fire);string prefix=fire?"trial_fire_charge_ground_":"trial_charge_ground_";
            using(var pose=new TrialBasicKickBody(body,animator)){
                for(int f=1;f<=60;f++){var charge=new BattleChargeVisualState(b,true,f,60);Assert.IsFalse(pose.Apply(b,charge));Assert.AreSame(original,body.sprite);Assert.AreEqual(flip,body.flipX);Assert.IsFalse(b.HasPlayerAction);Assert.IsTrue(animator.enabled);}
                Assert.IsTrue(b.TryStartPlayerBasicAttack(true));Assert.IsTrue(b.IsPlayerChargedBasicAction);Assert.IsTrue(pose.Apply(b));Assert.AreEqual(prefix+1,body.sprite.name);
                for(int n=0;n<3;n++)b.TickFrame(17);Assert.AreEqual(980,b.Enemies[0].CurrentHp);Assert.IsTrue(pose.Apply(b));Assert.AreEqual(prefix+2,body.sprite.name);
                b.Pause(Olca.Core.Logic.Battle.BattlePauseReason.User);for(int n=0;n<4;n++){Assert.IsTrue(pose.Apply(b));b.TickFrame(17);Assert.AreEqual(0,b.RecoveryElapsedFrames);}b.Resume(Olca.Core.Logic.Battle.BattlePauseReason.User);
                for(int n=0;n<3;n++)b.TickFrame(17);Assert.IsTrue(pose.Apply(b));Assert.AreEqual(prefix+3,body.sprite.name);for(int n=0;n<6;n++)b.TickFrame(17);Assert.IsFalse(b.IsPlayerChargedBasicAction);Assert.IsFalse(pose.Apply(b));Assert.AreSame(original,body.sprite);Assert.AreEqual(flip,body.flipX);Assert.IsTrue(animator.enabled);Assert.AreEqual(position,host.transform.position);
                Assert.IsTrue(b.TryStartPlayerBasicAttack(false));Assert.IsFalse(b.IsPlayerChargedBasicAction);Assert.IsTrue(pose.Apply(b));Assert.AreEqual("trial_ground_kick_prepare",body.sprite.name);Assert.AreEqual(flip,body.flipX);b.CancelBasicAction();Assert.IsFalse(pose.Apply(b));Assert.AreSame(original,body.sprite);
            }
        }
        [Test] public void 戦闘タメの旧snapshotは別Battleや攻撃開始取消後へ復活しない()
        {
            var b=BodyBattle(false);var charge=new BattleChargeVisualState(b,true,60,60);Assert.IsTrue(charge.CanDisplayFor(b));Assert.IsFalse(charge.CanDisplayFor(BodyBattle(false)));Assert.IsFalse(default(BattleChargeVisualState).CanDisplayFor(b));
            Assert.IsTrue(b.TryStartPlayerBasicAttack(true));Assert.IsFalse(charge.CanDisplayFor(b));b.CancelBasicAction();Assert.IsFalse(b.HasPlayerAction);Assert.IsFalse(b.IsPlayerChargedBasicAction);Assert.IsFalse(charge.CanDisplayFor(b));
            var updated=new BattleChargeVisualState(b,true,1,60);Assert.IsTrue(updated.CanDisplayFor(b));using(var pose=new TrialBasicKickBody(body,null)){
                var original=body.sprite;Assert.IsFalse(pose.Apply(b,updated));b.Player.SetHp(0);Assert.IsFalse(pose.Apply(b,updated));Assert.AreSame(original,body.sprite);
            }
        }
        [Test] public void 戦闘タメ素材の欠損は旧姿勢と反転を所有せず共有Spriteを破棄しない()
        {
            int loads=0;var b=BodyBattle(false);var original=body.sprite;body.flipX=true;
            using(var pose=new TrialBasicKickBody(body,null,null,key=>{loads++;return new Sprite[0];})){
                for(int n=0;n<4;n++)Assert.IsFalse(pose.Apply(b,new BattleChargeVisualState(b,true,60,60)));Assert.AreEqual(0,loads);Assert.AreSame(original,body.sprite);Assert.IsTrue(body.flipX);
            }
        }
        [Test] public void 戦闘タメsnapshotはFieldの停止取消とreleaseで姿勢を残さない()
        {
            var b=BodyBattle(false);var animator=host.AddComponent<Animator>();var original=body.sprite;var view=host.AddComponent<GameFieldView>();Set(view,"playerSprite",body);Set(view,"playerAnimator",animator);Set(view,"idlePlayerSprite",original);Set(view,"currentBattle",b);var pauseOwner=new object();
            try{
                view.SetBasicChargePresentation(b,true,60,60);Call(view,"UpdatePlayerSprite");Assert.AreSame(original,body.sprite);Assert.IsTrue(animator.enabled);
                Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,true);view.SetBasicChargePresentation(b,false,0,60);Call(view,"UpdatePlayerSprite");Assert.AreSame(original,body.sprite);Assert.IsTrue(animator.enabled);Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,false);
                view.SetBasicChargePresentation(b,true,60,60);Call(view,"UpdatePlayerSprite");view.ReleasePresentation();Assert.AreSame(original,body.sprite);Assert.IsTrue(animator.enabled);Set(view,"currentBattle",BodyBattle(false));Call(view,"UpdatePlayerSprite");Assert.AreSame(original,body.sprite);
            }finally{Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,false);view.ReleasePresentation();}
        }
        [Test] public void 戦闘タメ開始時は停止中にも直前の解放攻撃姿勢を残さない()
        {
            var b=BodyBattle(false);var animator=host.AddComponent<Animator>();var original=body.sprite;var view=host.AddComponent<GameFieldView>();Set(view,"playerSprite",body);Set(view,"playerAnimator",animator);Set(view,"idlePlayerSprite",original);Set(view,"currentBattle",b);var pauseOwner=new object();
            try{
                Assert.IsTrue(b.TryStartPlayerBasicAttack(true));Call(view,"UpdatePlayerSprite");Assert.AreEqual("trial_charge_ground_1",body.sprite.name);Assert.IsFalse(animator.enabled);
                b.CancelBasicAction();view.SetBasicChargePresentation(b,true,1,60);Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,true);Call(view,"UpdatePlayerSprite");Assert.AreSame(original,body.sprite);Assert.IsTrue(animator.enabled);
            }finally{Olca.Unity.BattleVisualClock.SetPaused(pauseOwner,false);view.ReleasePresentation();}
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

namespace Olca.Tests
{
    public class TrialGroundComboMotionTests
    {
        [NUnit.Framework.TestCase(0,0,0)]
        [NUnit.Framework.TestCase(0,1,0)]
        [NUnit.Framework.TestCase(0,2,1)]
        [NUnit.Framework.TestCase(0,5,1)]
        [NUnit.Framework.TestCase(0,6,2)]
        [NUnit.Framework.TestCase(0,8,2)]
        [NUnit.Framework.TestCase(0,9,3)]
        [NUnit.Framework.TestCase(0,14,3)]
        [NUnit.Framework.TestCase(0,15,0)]
        [NUnit.Framework.TestCase(0,20,0)]
        [NUnit.Framework.TestCase(0,21,-1)]
        [NUnit.Framework.TestCase(1,0,0)]
        [NUnit.Framework.TestCase(1,1,0)]
        [NUnit.Framework.TestCase(1,2,1)]
        [NUnit.Framework.TestCase(1,5,1)]
        [NUnit.Framework.TestCase(1,6,2)]
        [NUnit.Framework.TestCase(1,8,2)]
        [NUnit.Framework.TestCase(1,9,3)]
        [NUnit.Framework.TestCase(1,14,3)]
        [NUnit.Framework.TestCase(1,15,0)]
        [NUnit.Framework.TestCase(1,20,0)]
        [NUnit.Framework.TestCase(1,21,-1)]
        [NUnit.Framework.TestCase(2,0,0)]
        [NUnit.Framework.TestCase(2,1,0)]
        [NUnit.Framework.TestCase(2,2,1)]
        [NUnit.Framework.TestCase(2,5,1)]
        [NUnit.Framework.TestCase(2,6,2)]
        [NUnit.Framework.TestCase(2,8,2)]
        [NUnit.Framework.TestCase(2,9,3)]
        [NUnit.Framework.TestCase(2,14,3)]
        [NUnit.Framework.TestCase(2,15,0)]
        [NUnit.Framework.TestCase(2,20,0)]
        [NUnit.Framework.TestCase(2,21,-1)]
        public void 地上三段は論理phase境界から同じ連番を選び読取りで進まない(int stage,int elapsed,int expected)
        {
            var kind=(Olca.Core.Logic.Integration.OutsideKickKind)stage;var timing=new Olca.Core.Logic.Integration.OutsideKickTiming(6,3,12);
            for(int n=0;n<3;n++)NUnit.Framework.Assert.AreEqual(expected,Olca.Presentation.Exploration.TrialGroundComboMotion.FrameIndex(kind,elapsed,timing));
        }
        [NUnit.Framework.TestCase(false)] [NUnit.Framework.TestCase(true)]
        public void 通常とカエンの各段は別の四姿勢を読み全枠が256で足元と透過を保つ(bool fire)
        {
            using(var motion=new Olca.Presentation.Exploration.TrialGroundComboMotion()){
                var seen=new System.Collections.Generic.HashSet<UnityEngine.Sprite>();var timing=new Olca.Core.Logic.Integration.OutsideKickTiming(6,3,12);
                for(int stage=0;stage<3;stage++)foreach(int elapsed in new[]{0,2,6,9}){
                    var frame=motion.Resolve((Olca.Core.Logic.Integration.OutsideKickKind)stage,elapsed,timing,fire);NUnit.Framework.Assert.IsNotNull(frame);NUnit.Framework.Assert.IsTrue(seen.Add(frame));NUnit.Framework.Assert.IsTrue(motion.Contains(frame));NUnit.Framework.Assert.AreEqual(new UnityEngine.Vector2(256,256),frame.rect.size);NUnit.Framework.Assert.AreEqual(128,frame.pivot.x);NUnit.Framework.Assert.That(frame.pivot.y,NUnit.Framework.Is.InRange(10f,25f));
                    var importer=(UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(frame));NUnit.Framework.Assert.IsTrue(importer.alphaIsTransparency);NUnit.Framework.Assert.IsFalse(importer.mipmapEnabled);NUnit.Framework.Assert.AreEqual(UnityEngine.FilterMode.Point,importer.filterMode);
                }
                NUnit.Framework.Assert.IsNull(motion.Resolve(Olca.Core.Logic.Integration.OutsideKickKind.AirForward,0,timing,fire));motion.Dispose();NUnit.Framework.Assert.IsNull(motion.Resolve(Olca.Core.Logic.Integration.OutsideKickKind.GroundFirst,6,timing,fire));
            }
        }
        [NUnit.Framework.Test] public void 欠損した連番は部分採用せず再読込ループも作らない()
        {
            int loads=0;using(var motion=new Olca.Presentation.Exploration.TrialGroundComboMotion(key=>{loads++;return new UnityEngine.Sprite[11];})){
                var t=new Olca.Core.Logic.Integration.OutsideKickTiming(6,3,12);for(int n=0;n<10;n++)NUnit.Framework.Assert.IsNull(motion.Resolve(Olca.Core.Logic.Integration.OutsideKickKind.GroundFirst,n,t,false));NUnit.Framework.Assert.AreEqual(1,loads);
            }
        }
    }
}

namespace Olca.Tests {
 public class TrialChargeMotionTests {
  [NUnit.Framework.TestCase(true,false,0,0)] [NUnit.Framework.TestCase(true,true,60,1)]
  [NUnit.Framework.TestCase(false,false,-1,-1)] [NUnit.Framework.TestCase(false,false,0,1)]
  [NUnit.Framework.TestCase(false,false,5,1)] [NUnit.Framework.TestCase(false,false,6,2)]
  [NUnit.Framework.TestCase(false,false,8,2)] [NUnit.Framework.TestCase(false,false,9,3)]
  [NUnit.Framework.TestCase(false,false,20,3)] [NUnit.Framework.TestCase(false,false,21,-1)]
  public void タメ専用姿勢は蓄積完了解放復帰の境界だけを読み取る(bool charging,bool full,int elapsed,int expected){
   var t=new Olca.Core.Logic.Integration.OutsideKickTiming(6,3,12);for(int n=0;n<3;n++)NUnit.Framework.Assert.AreEqual(expected,Olca.Presentation.Exploration.TrialChargeMotion.FrameIndex(charging,full,elapsed,t));
  }
  [NUnit.Framework.TestCase(false)] [NUnit.Framework.TestCase(true)]
  public void 地上空中は専用八姿勢を保持し共有画像を破棄しない(bool fire){
   var t=new Olca.Core.Logic.Integration.OutsideKickTiming(6,3,12);var seen=new System.Collections.Generic.HashSet<UnityEngine.Sprite>();using(var m=new Olca.Presentation.Exploration.TrialChargeMotion()){
    foreach(bool grounded in new[]{true,false})foreach(int phase in new[]{0,1,2,3}){
     var pose=m.Resolve(grounded,phase<2,phase==1,phase==2?6:9,t,fire);NUnit.Framework.Assert.IsNotNull(pose);NUnit.Framework.Assert.IsTrue(seen.Add(pose));NUnit.Framework.Assert.IsTrue(m.Contains(pose));NUnit.Framework.Assert.AreEqual(256,pose.rect.width,.001f);NUnit.Framework.Assert.AreEqual(256,pose.rect.height,.001f);NUnit.Framework.Assert.That(pose.pivot.y,NUnit.Framework.Is.InRange(6f,12f));
     var i=(UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(pose));NUnit.Framework.Assert.IsTrue(i.alphaIsTransparency);NUnit.Framework.Assert.IsFalse(i.mipmapEnabled);NUnit.Framework.Assert.AreEqual(UnityEngine.FilterMode.Point,i.filterMode);
    }
    var retained=m.Resolve(true,false,false,6,t,fire);m.Dispose();NUnit.Framework.Assert.IsFalse(m.Contains(retained));NUnit.Framework.Assert.IsNull(m.Resolve(true,true,true,60,t,fire));NUnit.Framework.Assert.IsTrue(retained!=null);
   }
  }
  [NUnit.Framework.Test]public void 不完全なタメ連番を部分採用せず毎描画で再読込しない(){int loads=0;using(var m=new Olca.Presentation.Exploration.TrialChargeMotion(k=>{loads++;return new UnityEngine.Sprite[7];})){for(int n=0;n<10;n++)NUnit.Framework.Assert.IsNull(m.Resolve(true,true,false,n,null,false));NUnit.Framework.Assert.AreEqual(1,loads);}}
 }
}

namespace Olca.Tests {
 public class TrialSlashSpriteTests {
  [NUnit.Framework.TestCase(-1,6,-1,9,-1)] [NUnit.Framework.TestCase(0,6,-1,9,0)] [NUnit.Framework.TestCase(2,6,-1,9,0)]
  [NUnit.Framework.TestCase(3,6,-1,9,1)] [NUnit.Framework.TestCase(5,6,-1,9,1)] [NUnit.Framework.TestCase(-1,6,0,9,2)]
  [NUnit.Framework.TestCase(-1,6,4,9,3)] [NUnit.Framework.TestCase(-1,6,9,60,-1)] [NUnit.Framework.TestCase(0,0,0,9,-1)]
  public void 斬撃連番は命中窓後九frame以内の消失へ進み前隙には出ない(int active,int length,int tail,int recovery,int expected){NUnit.Framework.Assert.AreEqual(expected,Olca.Presentation.Effects.TrialSlashSprites.FrameIndex(active,length,tail,recovery));}
  [NUnit.Framework.Test]public void 斬撃は四種四コマの独立256資産でキャラを借りず共有参照を壊さない(){
   var seen=new System.Collections.Generic.HashSet<UnityEngine.Sprite>();using(var m=new Olca.Presentation.Effects.TrialSlashSprites()){
    for(int row=0;row<4;row++)for(int frame=0;frame<4;frame++){var p=m.Resolve(row,frame);NUnit.Framework.Assert.IsNotNull(p);NUnit.Framework.Assert.IsTrue(seen.Add(p));NUnit.Framework.Assert.AreEqual(256,p.rect.width);NUnit.Framework.Assert.AreEqual(256,p.rect.height);NUnit.Framework.Assert.AreEqual(100,p.pixelsPerUnit,.001f);}
    NUnit.Framework.Assert.IsNull(m.Resolve(-1,0));NUnit.Framework.Assert.IsNull(m.Resolve(0,4));m.Dispose();NUnit.Framework.Assert.IsNull(m.Resolve(0,0));foreach(var p in seen)NUnit.Framework.Assert.IsTrue(p!=null);
   }
  }
  [NUnit.Framework.Test]public void 欠けた斬撃は部分採用せず一度だけロードする(){int calls=0;using(var m=new Olca.Presentation.Effects.TrialSlashSprites(k=>{calls++;return new UnityEngine.Sprite[15];})){for(int n=0;n<10;n++)NUnit.Framework.Assert.IsNull(m.Resolve(0,1));NUnit.Framework.Assert.AreEqual(1,calls);}}
  [NUnit.Framework.TestCase(false)] [NUnit.Framework.TestCase(true)] public void World斬撃は判定矩形内で左右反転し百回再利用して共有画像を残す(bool mirror){
   var host=new UnityEngine.GameObject("SlashTest298");var reference=host.AddComponent<UnityEngine.SpriteRenderer>();var texture=new UnityEngine.Texture2D(2,2);var sprite=UnityEngine.Sprite.Create(texture,new UnityEngine.Rect(0,0,2,2),UnityEngine.Vector2.one*.5f,1);reference.sprite=sprite;host.transform.localScale=new UnityEngine.Vector3(-2,3,1);
   try{using(var assets=new Olca.Presentation.Effects.TrialSlashSprites())using(var effect=new Olca.Presentation.Effects.SlashSpriteWorld(host.transform)){
    effect.Warmup();var retained=assets.Resolve(0,1);var before=host.GetComponentsInChildren<UnityEngine.Transform>(true);var bounds=new UnityEngine.Rect(2,4,3,2);
    for(int n=0;n<100;n++){effect.Show(bounds,retained,1,reference,mirror);NUnit.Framework.Assert.IsTrue(effect.IsVisible);var view=host.GetComponentsInChildren<UnityEngine.SpriteRenderer>(true).Single(v=>v!=reference);NUnit.Framework.Assert.AreSame(retained,view.sprite);NUnit.Framework.Assert.AreEqual(mirror,view.flipX);NUnit.Framework.Assert.Greater(view.transform.TransformVector(UnityEngine.Vector3.right).x,0,"親の負scaleを相殺しmirrorだけで実向きを変える");NUnit.Framework.Assert.AreEqual(bounds.xMin,view.bounds.min.x,.001f);NUnit.Framework.Assert.AreEqual(bounds.xMax,view.bounds.max.x,.001f);NUnit.Framework.Assert.AreEqual(bounds.yMin,view.bounds.min.y,.001f);NUnit.Framework.Assert.AreEqual(bounds.yMax,view.bounds.max.y,.001f);effect.Clear();NUnit.Framework.Assert.IsFalse(effect.IsVisible);}
    NUnit.Framework.CollectionAssert.AreEqual(before,host.GetComponentsInChildren<UnityEngine.Transform>(true));effect.Show(bounds,null,1,reference);NUnit.Framework.Assert.IsFalse(effect.IsVisible);effect.Dispose();NUnit.Framework.Assert.IsTrue(retained!=null);
   }}finally{UnityEngine.Object.DestroyImmediate(host);UnityEngine.Object.DestroyImmediate(sprite);UnityEngine.Object.DestroyImmediate(texture);}
  }
 }
}

namespace Olca.Tests {
 public class TrialBombThrowMotionTests {
  [NUnit.Framework.TestCase(-1,3,5,-1)] [NUnit.Framework.TestCase(0,3,5,0)] [NUnit.Framework.TestCase(1,3,5,1)]
  [NUnit.Framework.TestCase(2,3,5,1)] [NUnit.Framework.TestCase(3,3,5,2)] [NUnit.Framework.TestCase(5,3,5,3)]
  [NUnit.Framework.TestCase(8,3,5,-1)] [NUnit.Framework.TestCase(0,0,2,2)] [NUnit.Framework.TestCase(0,0,0,-1)] [NUnit.Framework.TestCase(5,0,12,2)] [NUnit.Framework.TestCase(6,0,12,3)] [NUnit.Framework.TestCase(12,0,12,-1)]
  public void 投擲の表示phaseは指定された論理frame境界だけを読む(int age,int start,int end,int pose){NUnit.Framework.Assert.AreEqual(pose,Olca.Presentation.Exploration.TrialBombThrowMotion.FrameIndex(age,start,end));}
  [NUnit.Framework.TestCase(0)] [NUnit.Framework.TestCase(1)] [NUnit.Framework.TestCase(2)] [NUnit.Framework.TestCase(3)] [NUnit.Framework.TestCase(4)] [NUnit.Framework.TestCase(5)] [NUnit.Framework.TestCase(6)] [NUnit.Framework.TestCase(7)] [NUnit.Framework.TestCase(8)] [NUnit.Framework.TestCase(9)]
  public void 投擲Viewは登録形態と地上空中を採用し未知形態と行動不能へ流用しない(int state){
   var session=new Olca.Core.Logic.Integration.OutsideExplorationSession();var go=new UnityEngine.GameObject("BombProjection299");go.SetActive(false);var view=go.AddComponent<Olca.Presentation.Exploration.OutsideExplorationView>();
   var field=typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetField("presentation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   try{session.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100,staggerDurationFrames:3));var factory=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,Olca.Core.Domain.PlayerAttackTiming>{{"bomb",new Olca.Core.Domain.PlayerAttackTiming(state>=7?0:3,state>=7?0:8)}});var cards=factory.CreateStarterDeck().GetAllCards().ToArray();
    NUnit.Framework.Assert.IsTrue(session.TryInitializeCards(cards,cards.GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count()),new Olca.Core.Logic.Battle.SeededBattleRandom(299),out _));for(int n=0;n<1000&&!session.Cards.Hand.Any(c=>c.Id=="bomb");n++)session.Cards.Reload();NUnit.Framework.Assert.IsTrue(session.Cards.TrySelect(session.Cards.Hand.ToList().FindIndex(c=>c.Id=="bomb")));
    using(var lease=session.AttachPresentation()){NUnit.Framework.Assert.IsTrue(session.TryUseSelectedCard(session.Cards.SelectedCard,session.Cards.HandRevision,out _));
     if(state==1)session.Player.SetDepartureStyle(Olca.Core.Domain.DepartureStyleDefinition.TrialFire);
     if(state==2)session.Player.SetDepartureStyle(new Olca.Core.Domain.DepartureStyleDefinition("unknown299","未登録",null));
     if(state==3){var data=session.Core.CaptureState();data.Height=60000;data.SupportId=null;typeof(Olca.Core.Logic.Integration.OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(session.Core,new object[]{data});}
     if(state==4)session.Player.SetHp(0);
     if(state==5){session.Player.RestoreTimedAilments(new Olca.Core.Domain.PlayerTimedAilmentsDto{Paralysis=new Olca.Core.Domain.PlayerTimedAilmentDto{RemainingUnits=1000,DisplayFrames=20},Confusion=new Olca.Core.Domain.PlayerTimedAilmentDto(),Blindness=new Olca.Core.Domain.PlayerTimedAilmentDto()});NUnit.Framework.Assert.IsTrue(session.Player.IsParalyzed);}
     if(state==6){session.Player.TryTakeDamage(1,forceStagger:true);NUnit.Framework.Assert.IsTrue(session.Player.IsStaggered);}
     if(state==8)for(int n=0;n<Olca.Presentation.Exploration.TrialBombThrowMotion.InstantReleaseVisualFrames;n++)session.TickTraversal(0);if(state==9){session.TickTraversal(1);session.TickTraversal(0);}
     field.SetValue(view,lease);var result=typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("TrialBombSprite",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);NUnit.Framework.Assert.AreEqual(state==0||state==1||state==3||state==7,result!=null);if(state==7)NUnit.Framework.Assert.AreEqual("trial_bomb_throw_2",((UnityEngine.Sprite)result).name);
     lease.Dispose();NUnit.Framework.Assert.IsNull(typeof(Olca.Presentation.Exploration.OutsideExplorationView).GetMethod("TrialBombSprite",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null));field.SetValue(view,null);
    }
   }finally{field.SetValue(view,null);session.Dispose();UnityEngine.Object.DestroyImmediate(go);}
  }
  [NUnit.Framework.Test]public void 投擲連番は四つの256透過共有資産を借り破棄時も元画像を壊さない(){
   var seen=new System.Collections.Generic.HashSet<UnityEngine.Sprite>();using(var m=new Olca.Presentation.Exploration.TrialBombThrowMotion()){
    foreach(int age in new[]{0,2,4,6}){var p=m.Resolve(age,4,4);NUnit.Framework.Assert.IsNotNull(p);NUnit.Framework.Assert.IsTrue(seen.Add(p));NUnit.Framework.Assert.IsTrue(m.Contains(p));NUnit.Framework.Assert.AreEqual(256,p.rect.width);NUnit.Framework.Assert.AreEqual(256,p.rect.height);NUnit.Framework.Assert.AreEqual(100,p.pixelsPerUnit,.001f);NUnit.Framework.Assert.AreEqual(14,p.pivot.y,.01f);}
    m.Dispose();foreach(var p in seen){NUnit.Framework.Assert.IsTrue(p!=null);NUnit.Framework.Assert.IsFalse(m.Contains(p));}NUnit.Framework.Assert.IsNull(m.Resolve(0,4,4));
   }
  }
  [NUnit.Framework.Test]public void 投擲素材欠損時は部分採用や描画ごとの再読込をしない(){int loads=0;using(var m=new Olca.Presentation.Exploration.TrialBombThrowMotion(k=>{loads++;return new UnityEngine.Sprite[3];})){for(int n=0;n<20;n++)NUnit.Framework.Assert.IsNull(m.Resolve(0,4,4));NUnit.Framework.Assert.AreEqual(1,loads);}}
 }
}

namespace Olca.Tests {
 public class TrialBombVariantVisualTests {
  [NUnit.Framework.TestCase(false,false,0)] [NUnit.Framework.TestCase(true,true,4)] [NUnit.Framework.TestCase(false,true,8)]
  public void 空中とカエンの投擲は専用四姿勢を一度だけ借用する(bool grounded,bool fire,int first){
   using(var motion=new Olca.Presentation.Exploration.TrialBombThrowMotion()){
    var seen=new System.Collections.Generic.HashSet<UnityEngine.Sprite>();for(int n=0;n<4;n++){
     var sprite=motion.Resolve(n*2,4,4,grounded,fire);NUnit.Framework.Assert.IsNotNull(sprite);NUnit.Framework.Assert.AreEqual("trial_bomb_variant_"+(first+n),sprite.name);NUnit.Framework.Assert.IsTrue(seen.Add(sprite));NUnit.Framework.Assert.IsTrue(motion.Contains(sprite));NUnit.Framework.Assert.AreEqual(14,sprite.pivot.y,.01f);
    }motion.Dispose();foreach(var sprite in seen){NUnit.Framework.Assert.IsTrue(sprite!=null);NUnit.Framework.Assert.IsFalse(motion.Contains(sprite));}
   }
  }
  [NUnit.Framework.TestCase(-1,1)] [NUnit.Framework.TestCase(1,1)] [NUnit.Framework.TestCase(-1,5)] [NUnit.Framework.TestCase(1,5)] [NUnit.Framework.TestCase(-1,90)] [NUnit.Framework.TestCase(1,90)]
  public void 手先補間は固定発射位置から単調に終点へ収束し保存計画を変更しない(int facing,int duration){
   long origin=600000;long end=origin+facing*(duration==90?120000:6000);var launch=new Olca.Core.Logic.Integration.OutsideBombLaunchData{X=origin,Facing=facing};if(duration!=90)launch.Terrain=new Olca.Core.Logic.Integration.OutsideBombTerrainData{OriginHeight=60000,ImpactX=end,ImpactFrame=duration,ImpactHeight=30000};var flight=new Olca.Core.Logic.Integration.OutsideBombFlightData{Launch=launch,FiredAt=10};
   byte[] before=Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(flight);float previous=0;
   for(int age=0;age<=duration;age++){
    var point=Olca.Presentation.Exploration.TrialBombThrowMotion.FlightPosition(flight,10+age);
    float x=point.x;NUnit.Framework.Assert.GreaterOrEqual(x,(float)System.Math.Min(origin,end)/60000-.0001f);NUnit.Framework.Assert.LessOrEqual(x,(float)System.Math.Max(origin,end)/60000+.0001f);if(age>0)NUnit.Framework.Assert.GreaterOrEqual((x-previous)*facing,-.0001f);previous=x;
    NUnit.Framework.Assert.AreEqual(point,Olca.Presentation.Exploration.TrialBombThrowMotion.FlightPosition(flight,10+age));
   }
   var start=Olca.Presentation.Exploration.TrialBombThrowMotion.FlightPosition(flight,10);NUnit.Framework.Assert.AreEqual(start,Olca.Presentation.Exploration.TrialBombThrowMotion.FlightPosition(flight,9));
   var final=Olca.Presentation.Exploration.TrialBombThrowMotion.FlightPosition(flight,10+duration);NUnit.Framework.Assert.AreEqual(flight.Launch.ImpactX/60000f,final.x,.0001f);NUnit.Framework.Assert.AreEqual(flight.Height(10+duration)/60000f,final.y,.0001f);NUnit.Framework.CollectionAssert.AreEqual(before,Olca.Core.Logic.Dungeon.PortableCheckpointJson.Serialize(flight));
  }
 }
}
