using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Presentation.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests
{
    public class CombatStatusIconsTests
    {
        private BattleSystem Battle(out PlayerCharacter player,out Enemy enemy)
        {
            player=new PlayerCharacter(500,new Olca.Core.Domain.Vector2Int(0,0));enemy=new Enemy("boss","テスト",500,ElementType.None,new Olca.Core.Domain.Vector2Int(5,0));
            var card=new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,1,"");
            return new BattleSystem(player,new List<Enemy>{enemy},new Deck(new List<Card>{card}),logger:new MockLogger());
        }
        private void States(BattleSystem battle,PlayerCharacter p)
        {battle.RunLogicalFrame(()=>{p.ApplyPoison("poison",new StatusEffect(StatusEffectType.Poison,2000,0),30);p.ApplyParalysis(120);p.ApplyConfusion(120);p.ApplyBlindness(120);});}
        [Test] public void 状態読取は元の全長と確定残量だけを返し時計を進めない()
        {
            var b=Battle(out var p,out _);States(b,p);for(int i=0;i<30;i++)b.TickFrame(16);
            var snapshot=p.InspectStatuses();Assert.AreEqual(4,snapshot.Count);Assert.IsTrue(snapshot.All(v=>v.DurationFrames==120&&v.RemainingFrames==90));
            long frame=b.LogicalFrame;for(int i=0;i<5;i++)Assert.IsTrue(CombatStatusIcons.ForPlayer(p).All(v=>v.RemainingFraction==.75f));Assert.AreEqual(frame,b.LogicalFrame);
        }
        [Test] public void 短い再付与も長い再付与も重複せず新期間を表示基準にする()
        {
            var b=Battle(out var p,out _);b.RunLogicalFrame(()=>p.ApplyConfusion(120));for(int i=0;i<30;i++)b.TickFrame(16);
            b.RunLogicalFrame(()=>p.ApplyConfusion(20));var status=p.InspectStatuses().Single();Assert.AreEqual(20,status.DurationFrames);Assert.AreEqual(20,status.RemainingFrames);Assert.AreEqual(1,CombatStatusIcons.ForPlayer(p).Single().RemainingFraction);
            b.RunLogicalFrame(()=>p.ApplyConfusion(200));Assert.AreEqual(200,p.InspectStatuses().Single().DurationFrames);Assert.AreEqual(1,CombatStatusIcons.ForPlayer(p).Count);
        }
        [Test] public void 耐性後の全長を使い無効な付与予約を表示へ混ぜない()
        {
            var b=Battle(out var p,out _);p.SetBattleStatusResistances(new TimedStatusResistances(confusion:50,blind:100));p.ApplyConfusion(120);p.ApplyBlindness(120);
            Assert.IsEmpty(CombatStatusIcons.ForPlayer(p));b.TickFrame(16);var item=p.InspectStatuses().Single();Assert.AreEqual(StatusEffectType.Confusion,item.Type);Assert.AreEqual(60,item.DurationFrames);
        }
        [TestCase(false)] [TestCase(true)] public void 時間制防御は時間割合だけを表示し耐久値や性能を変更しない(bool aura)
        {
            var b=Battle(out var p,out _);if(aura)p.SetAura(50,120);else p.SetBarrier(50,120);
            for(int i=0;i<30;i++)b.TickFrame(16);var item=CombatStatusIcons.ForPlayer(p).Single();Assert.AreEqual(aura?CombatIconId.Aura:CombatIconId.Barrier,item.Icon);Assert.AreEqual(.75f,item.RemainingFraction);Assert.AreEqual(50,aura?p.AuraThreshold:p.BarrierHp);
            p.SetBarrier(50);Assert.IsNull(CombatStatusIcons.ForPlayer(p).Single().RemainingFraction);
        }
        [Test] public void 異なる毒種は共有絵を使い個別の安定識別を持ち解除で消える()
        {
            var b=Battle(out var p,out _);b.RunLogicalFrame(()=>{p.ApplyPoison("poison",new StatusEffect(StatusEffectType.Poison,2000,0),30);p.ApplyPoison("venom",new StatusEffect(StatusEffectType.Poison,2000,0,poisonKind:"venom",poisonIntervalFrames:30),30);});
            var values=CombatStatusIcons.ForPlayer(p);Assert.AreEqual(2,values.Count);Assert.AreEqual(2,values.Select(v=>v.Id).Distinct().Count());Assert.IsTrue(values.All(v=>v.Icon==CombatIconId.Poison));
            b.RunLogicalFrame(()=>p.ClearPoisons());Assert.IsEmpty(CombatStatusIcons.ForPlayer(p));
        }
        [Test] public void 毒保存の形式と残時計を変えず復帰時残量を表示基準にする()
        {
            var b=Battle(out var p,out _);b.RunLogicalFrame(()=>p.ApplyPoison("poison",new StatusEffect(StatusEffectType.Poison,2000,0),30));for(int i=0;i<30;i++)b.TickFrame(16);
            var data=p.CapturePoisons();var before=PortableCheckpointJson.Serialize(data);var restored=new PlayerCharacter(500);restored.RestorePoisons(data);
            Assert.AreEqual(90,restored.InspectStatuses().Single().DurationFrames);Assert.AreEqual(1,CombatStatusIcons.ForPlayer(restored).Single().RemainingFraction);CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(restored.CapturePoisons()));
        }
        [TestCase("user")] [TestCase("custom")] [TestCase("dark")] [TestCase("hit")]
        public void 各停止中は状態と防御の表示割合を進めない(string mode)
        {
            var b=Battle(out var p,out var e);b.RunLogicalFrame(()=>p.ApplyConfusion(120));p.SetBarrier(50,120);
            if(mode=="user")b.Pause(BattlePauseReason.User);else if(mode=="custom")b.Pause(BattlePauseReason.CustomScreen);else if(mode=="dark")Assert.IsTrue(b.TryStartDarkeningEffect(DarkeningSide.Opponent,0,10,()=>{}));else b.RunLogicalFrame(()=>e.TakeDamage(1,ElementType.None,hitStopFrames:10));
            var before=CombatStatusIcons.ForPlayer(p).Select(v=>v.RemainingFraction).ToArray();for(int i=0;i<5;i++)b.TickFrame(16);CollectionAssert.AreEqual(before,CombatStatusIcons.ForPlayer(p).Select(v=>v.RemainingFraction).ToArray());
        }
        [Test] public void 敵も全長を保持し死亡時は残状態や補助のアイコンを返さない()
        {
            var b=Battle(out var p,out var e);b.RunLogicalFrame(()=>e.ApplyStatus(new StatusEffect(StatusEffectType.Blind,2000)));for(int i=0;i<30;i++)b.TickFrame(16);
            Assert.AreEqual(120,e.InspectStatuses().Single().DurationFrames);Assert.AreEqual(.75f,CombatStatusIcons.ForEnemy(e).Single().RemainingFraction);e.TakeDamage(99999,ElementType.None);Assert.IsEmpty(CombatStatusIcons.ForEnemy(e));p.TakeDamage(99999);Assert.IsEmpty(CombatStatusIcons.ForPlayer(p));
        }
        [Test] public void 無時間バフは既存フラグで表示しバリアとオーラを重複させない()
        {
            Battle(out var p,out _);p.Guard.Set(true,false);p.TerrainState.IsFloating=true;p.SetConcentrateBuff(2);p.SetBarrier(10);var values=CombatStatusIcons.ForPlayer(p);Assert.AreEqual(4,values.Count);Assert.IsTrue(values.All(v=>v.RemainingFraction==null));
            p.SetAura(20);values=CombatStatusIcons.ForPlayer(p);Assert.IsTrue(values.Any(v=>v.Icon==CombatIconId.Aura));Assert.IsFalse(values.Any(v=>v.Icon==CombatIconId.Barrier));
        }
        [TestCase(CombatIconId.Poison)] [TestCase(CombatIconId.Paralyze)] [TestCase(CombatIconId.Confusion)] [TestCase(CombatIconId.Blind)]
        [TestCase(CombatIconId.Guard)] [TestCase(CombatIconId.Barrier)] [TestCase(CombatIconId.Aura)] [TestCase(CombatIconId.Float)] [TestCase(CombatIconId.Concentrate)]
        public void 専用素材は同じ共有参照を返し元PNGと実行用包装は一致して真透過である(CombatIconId id)
        {
            Assert.IsTrue(CombatStatusIconCatalog.TryGet(id,out var color,out var gray));Assert.IsTrue(CombatStatusIconCatalog.TryGet(id,out var again,out var muted));Assert.AreSame(color,again);Assert.AreSame(gray,muted);Assert.AreNotSame(color,gray);
            Assert.AreEqual(64,color.texture.width);Assert.AreEqual(64,color.texture.height);Assert.AreEqual(FilterMode.Point,color.texture.filterMode);Assert.IsFalse(color.texture.isReadable);Assert.That(color.texture.format,Is.EqualTo(TextureFormat.ARGB32).Or.EqualTo(TextureFormat.RGBA32));
            var key=id.ToString().ToLowerInvariant();var png=File.ReadAllBytes("Assets/Art/UI/CombatStatus/"+key+".png");CollectionAssert.AreEqual(png,Resources.Load<TextAsset>("OlcaCombatStatus/"+key).bytes);
            var image=new Texture2D(2,2);try{Assert.IsTrue(image.LoadImage(png));var pixels=image.GetPixels32();Assert.IsTrue(pixels.Any(v=>v.a==0));Assert.IsTrue(pixels.Any(v=>v.a==255));Assert.IsTrue(pixels.All(v=>v.a==0||v.a==255));}finally{UnityEngine.Object.DestroyImmediate(image);}
        }
        [Test] public void 未対応の種は別の素材へ置き換えない(){Assert.IsNull(CombatStatusIconCatalog.ForStatus(StatusEffectType.None));Assert.IsFalse(CombatStatusIconCatalog.TryGet((CombatIconId)999,out _,out _));}
        [Test] public void 描画更新は重複生成せず割合と消去を反映して入力や数値を追加しない()
        {
            var root=new GameObject("IconStrip",typeof(RectTransform));try{var strip=root.AddComponent<CombatStatusIconStrip>();strip.Configure(5);var values=new[]{new CombatIconEntry("one",CombatIconId.Poison,.5f),new CombatIconEntry("two",CombatIconId.Guard)};
                strip.Show(values);strip.Show(values);Assert.AreEqual(2,strip.VisibleCount);Assert.AreEqual(2,root.transform.childCount);Assert.AreEqual(.5f,root.transform.GetChild(0).Find("Remaining").GetComponent<Image>().fillAmount);var face=root.transform.GetChild(0).GetComponent<Image>();Assert.AreSame(Resources.Load<Sprite>("OlcaUI/description"),face.sprite);Assert.AreEqual(Image.Type.Sliced,face.type);Assert.AreSame(face.sprite,root.transform.GetChild(1).GetComponent<Image>().sprite);Assert.AreEqual(new Vector2(40,40),((RectTransform)root.transform.GetChild(0).Find("Remaining")).rect.size);Assert.AreEqual(new Vector2(102,48),((RectTransform)root.transform).rect.size);Assert.IsTrue(root.GetComponentsInChildren<Image>().All(v=>!v.raycastTarget));Assert.IsEmpty(root.GetComponentsInChildren<Text>());
                Assert.Throws<ArgumentException>(()=>strip.Show(new[]{values[0],values[0]}));Assert.AreEqual(2,strip.VisibleCount);strip.Show(new[]{values[1]});Assert.AreEqual(1,strip.VisibleCount);strip.Clear();Assert.AreEqual(0,strip.VisibleCount);Assert.IsEmpty(root.GetComponentsInChildren<Image>());strip.Show(values);Assert.AreEqual(2,root.transform.childCount);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void プレイヤー状態ドックは開閉と比率変更でも一定寸法でHPへ追従し非表示に残らない()
        {
            var root=new GameObject("StatusDockViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(1920,1080);
            try{
                var custom=new GameObject("CustomScreenContainer",typeof(RectTransform));custom.transform.SetParent(root.transform,false);var panel=(RectTransform)custom.transform;panel.anchorMin=panel.anchorMax=new Vector2(0,.5f);panel.pivot=new Vector2(0,.5f);panel.sizeDelta=new Vector2(960,1080);
                var bg=new GameObject("BgCustom",typeof(RectTransform));bg.transform.SetParent(custom.transform,false);var backdrop=(RectTransform)bg.transform;backdrop.anchoredPosition=new Vector2(580,0);backdrop.anchorMin=backdrop.anchorMax=new Vector2(0,.5f);backdrop.sizeDelta=new Vector2(1280,1000);
                var hp=new GameObject("PlayerHpText",typeof(RectTransform),typeof(Text));hp.transform.SetParent(custom.transform,false);var text=hp.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var hpRect=(RectTransform)hp.transform;hpRect.anchorMin=hpRect.anchorMax=new Vector2(1,.5f);hpRect.pivot=new Vector2(1,.5f);hpRect.anchoredPosition=new Vector2(225,440);hpRect.sizeDelta=new Vector2(200,100);
                var view=root.AddComponent<BattleView>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var type=typeof(BattleView);type.GetField("playerHpText",flags).SetValue(view,text);type.GetField("customScreenContainer",flags).SetValue(view,panel);
                var gauge=new GameObject("CustomGaugeContainer",typeof(RectTransform));gauge.transform.SetParent(root.transform,false);var gaugeRect=(RectTransform)gauge.transform;gaugeRect.anchorMin=gaugeRect.anchorMax=new Vector2(0,.5f);gaugeRect.pivot=new Vector2(0,.5f);gaugeRect.sizeDelta=new Vector2(190,750);type.GetField("customGaugeContainer",flags).SetValue(view,gaugeRect);
                var battle=Battle(out var player,out _);player.Guard.Set(true,false);view.Initialize(battle);var refresh=type.GetMethod("RefreshStatusIcons",flags);refresh.Invoke(view,null);var strip=(CombatStatusIconStrip)type.GetField("statusIcons",flags).GetValue(view);var entries=Enumerable.Range(0,8).Select(i=>new CombatIconEntry("fixture/"+i,(CombatIconId)i,.5f)).ToArray();var set=type.GetMethod("SetCustomSlide",flags);
                foreach(var setup in new[]{new Vector2(1920,1),new Vector2(1440,1),new Vector2(1920,2)})foreach(float slide in new[]{-900f,0f}){
                    viewport.sizeDelta=new Vector2(setup.x,1080);root.transform.localScale=new Vector3(setup.y,setup.y,1);strip.Show(entries);set.Invoke(view,new object[]{slide});Assert.AreSame(root.transform,strip.transform.parent);Assert.AreEqual(Vector3.one,strip.transform.localScale);
                    var points=new Vector3[4];((RectTransform)strip.transform.GetChild(0)).GetWorldCorners(points);Assert.AreEqual(48*setup.y,Vector3.Distance(points[0],points[3]),.02f);
                    ((RectTransform)strip.transform).GetWorldCorners(points);var local=points.Select(v=>root.transform.InverseTransformPoint(v)).ToArray();Assert.GreaterOrEqual(local.Min(v=>v.x),viewport.rect.xMin);Assert.LessOrEqual(local.Max(v=>v.x),viewport.rect.xMax);Assert.GreaterOrEqual(local.Min(v=>v.y),viewport.rect.yMin);Assert.LessOrEqual(local.Max(v=>v.y),viewport.rect.yMax);
                    if(slide==0){backdrop.GetWorldCorners(points);Assert.GreaterOrEqual(local.Min(v=>v.x),points.Max(v=>root.transform.InverseTransformPoint(v).x)+10);}
                    else{hpRect.GetWorldCorners(points);Assert.Less(local.Max(v=>v.y),points.Min(v=>root.transform.InverseTransformPoint(v).y));gaugeRect.GetWorldCorners(points);Assert.GreaterOrEqual(local.Min(v=>v.x),points.Max(v=>root.transform.InverseTransformPoint(v).x)+10);}
                }
                view.SetCustomInspectionHidden(true);refresh.Invoke(view,null);Assert.AreEqual(0,strip.VisibleCount);view.SetCustomInspectionHidden(false);refresh.Invoke(view,null);Assert.AreEqual(1,strip.VisibleCount);
                text.enabled=false;refresh.Invoke(view,null);Assert.AreEqual(0,strip.VisibleCount);text.enabled=true;refresh.Invoke(view,null);Assert.AreEqual(1,strip.VisibleCount);Assert.AreEqual(8,strip.transform.childCount);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void 通常敵は素材を作らずボス二体のアイコンと隣ゲージを重ねない()
        {
            var roots=new List<GameObject>();var views=new List<EnemyView>();
            try{
                if(Camera.main==null){var camera=new GameObject("IconCamera",typeof(Camera));camera.tag="MainCamera";camera.transform.position=new Vector3(0,0,-10);roots.Add(camera);}
                EnemyView Make(string id,bool poise=false){var model=new Enemy(id,"テスト",100,ElementType.None,new Olca.Core.Domain.Vector2Int(3,0),poiseDefinition:poise?new EnemyPoiseDefinition(100,30):null);model.Guard.Set(true,false);var obj=new GameObject(id);roots.Add(obj);var view=obj.AddComponent<EnemyView>();views.Add(view);view.Initialize(model);view.ShowHp();return view;}
                var normal=Make("grunt");Assert.IsEmpty(normal.GetComponentsInChildren<CombatStatusIconStrip>(true));var a=Make("boss");var b=Make("boss",true);a.Refresh();b.Refresh();
                var rect=a.GetComponentsInChildren<Image>().First().rectTransform;var corners=new Vector3[4];rect.GetWorldCorners(corners);float left=corners.Min(v=>Camera.main.WorldToViewportPoint(v).x),right=corners.Max(v=>Camera.main.WorldToViewportPoint(v).x);
                var ownGauge=a.transform.Find("EnemyHpTrack").GetComponent<LineRenderer>();float ownLeft=Camera.main.WorldToViewportPoint(ownGauge.GetPosition(0)-Camera.main.transform.right*ownGauge.startWidth*.5f).x;Assert.Greater(ownLeft-right,.005f);
                float nextHp=Camera.main.WorldToViewportPoint(b.transform.Find("EnemyHpTrack").GetComponent<LineRenderer>().GetPosition(0)).x;Assert.IsTrue(nextHp<left-.003f||nextHp>right+.003f);
                var poiseIcons=b.GetComponentsInChildren<Image>().First().rectTransform;poiseIcons.GetWorldCorners(corners);var poiseBar=b.transform.Find("EnemyPoiseTrack").GetComponent<LineRenderer>();float gap=Camera.main.WorldToViewportPoint(poiseBar.GetPosition(0)-Camera.main.transform.right*poiseBar.startWidth*.5f).x-corners.Max(v=>Camera.main.WorldToViewportPoint(v).x);Assert.Greater(gap,.005f);
                a.enabled=false;a.Refresh();Assert.AreEqual(0,a.GetComponentInChildren<CombatStatusIconStrip>(true).VisibleCount);
                a.enabled=true;a.Refresh();Assert.AreEqual(1,a.GetComponentInChildren<CombatStatusIconStrip>(true).VisibleCount);
                a.HideHp();Assert.AreEqual(0,a.GetComponentInChildren<CombatStatusIconStrip>(true).VisibleCount);
            }finally{foreach(var view in views)if(view!=null)view.HideHp();foreach(var root in roots)if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
