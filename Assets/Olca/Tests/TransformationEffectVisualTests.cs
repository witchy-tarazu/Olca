using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Presentation.Battle;
using Olca.Presentation.Effects;
using Olca.Tests.Mocks;
namespace Olca.Tests
{
    public class TransformationEffectVisualTests
    {
        private const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private GameObject host; private BattleView view; private BattleSystem battle;
        private void Refresh()=>typeof(BattleView).GetMethod("UpdateTransformationVisual",Hidden).Invoke(view,null);
        private Image Effect=>host.GetComponentsInChildren<Image>(true).Single(x=>x.name=="TransformationEffect");
        private GameObject Frame=>host.GetComponentsInChildren<Image>(true).Single(x=>x.name=="TransformationPresentation").gameObject;
        [SetUp] public void Setup()
        {
            host=new GameObject("TransformationTest",typeof(RectTransform));view=host.AddComponent<BattleView>();
            var player=new PlayerCharacter(500);var enemy=new Enemy("fixture","テスト",500,ElementType.None,new Olca.Core.Domain.Vector2Int(5,0));
            var cards=Enumerable.Range(0,20).Select(i=>new Card("fixture-"+i,"テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,1,"")).ToList();
            battle=new BattleSystem(player,new List<Enemy>{enemy},new Deck(cards));
            battle.Transformations.Configure(new[]{new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire)});battle.Transformations.EnablePresentation();
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);battle.OpenCustomScreen(new MockRandomProvider());
            typeof(BattleView).GetField("battleSystem",Hidden).SetValue(view,battle);
        }
        [TearDown] public void Cleanup(){Object.DestroyImmediate(host);}
        [TestCase(true,"activation")] [TestCase(false,"release")]
        public void 共通素材は真透過で文字中央を空け包装と一致して共有される(bool activating,string name)
        {
            var bytes=Resources.Load<TextAsset>("OlcaTransformationEffects/"+name);Assert.IsNotNull(bytes);
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Art/Effects/Transformation",name+".png")),bytes.bytes);
            var texture=new Texture2D(2,2);try{Assert.IsTrue(texture.LoadImage(bytes.bytes));Assert.AreEqual(512,texture.width);Assert.AreEqual(160,texture.height);
                CollectionAssert.AreEqual(new byte[]{0,255},texture.GetPixels32().Select(p=>p.a).Distinct().OrderBy(x=>x).ToArray());Assert.IsTrue(texture.GetPixels(154,0,204,160).All(p=>p.a==0));
            }finally{Object.DestroyImmediate(texture);}
            var sprite=TransformationEffectSprites.Get(activating);Assert.IsNotNull(sprite);Assert.AreSame(sprite,TransformationEffectSprites.Get(activating));Assert.IsFalse(sprite.texture.isReadable);
        }
        [TestCase(false,60)] [TestCase(true,12)]
        public void 通常短縮とも既存時計へ追従し休止表示更新で時間を進めず終了で消える(bool shortened,int frames)
        {
            var state=battle.Transformations;state.ShortPresentation=shortened;Assert.IsTrue(state.TryPreview("fixture"));battle.CustomScreen.Confirm();Refresh();Assert.IsFalse(Frame.activeSelf);
            battle.CustomScreen.Close();Refresh();var image=Effect;Assert.IsTrue(Frame.activeSelf);Assert.AreSame(TransformationEffectSprites.Get(true),image.sprite);Assert.IsFalse(image.raycastTarget);Assert.IsTrue(image.preserveAspect);
            var caption=host.GetComponentsInChildren<Text>(true).Single(x=>x.name=="TransformationCaption");Assert.Less(image.transform.GetSiblingIndex(),caption.transform.GetSiblingIndex());
            battle.TickFrame(16);Refresh();Assert.AreEqual(frames-1,state.ActivationFramesRemaining);var color=image.color;var logical=battle.LogicalFrame;
            battle.Pause(BattlePauseReason.User);for(int n=0;n<100;n++){battle.TickFrame(16);Refresh();}Assert.AreEqual(frames-1,state.ActivationFramesRemaining);Assert.AreEqual(color,image.color);Assert.AreEqual(logical,battle.LogicalFrame);
            battle.Resume(BattlePauseReason.User);for(int n=1;n<frames;n++){battle.TickFrame(16);Refresh();}Assert.IsFalse(Frame.activeSelf);Assert.AreEqual(0,state.ActivationFramesRemaining);Assert.AreSame(image,Effect);
        }
        [Test] public void 取消は素材を表示せず弱点解除は追加停止なしで解除素材へ移り終了破棄で消える()
        {
            var state=battle.Transformations;state.TryPreview("fixture");Assert.IsTrue(state.TryCancelPending());Refresh();Assert.IsFalse(Frame.activeSelf);
            state.TryPreview("fixture");battle.CustomScreen.Confirm();battle.CustomScreen.Close();for(int i=0;i<61;i++)battle.TickFrame(16);
            battle.Player.TakeDamage(1,attackElement:ElementType.Fire);Refresh();Assert.AreEqual(12,state.ReleaseFramesRemaining);Assert.IsTrue(Frame.activeSelf);Assert.AreSame(TransformationEffectSprites.Get(false),Effect.sprite);
            long logical=battle.LogicalFrame;for(int i=0;i<12;i++){battle.TickFrame(16);Refresh();}Assert.Greater(battle.LogicalFrame,logical);Assert.IsFalse(Frame.activeSelf);
            view.EndSessionPresentation();Assert.IsFalse(Frame.activeSelf);Assert.IsFalse(host.GetComponentsInChildren<Text>(true).Single(x=>x.name=="TransformationHud").transform.parent.gameObject.activeSelf);
        }
    }
}
