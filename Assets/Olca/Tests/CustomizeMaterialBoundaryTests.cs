using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Olca.Presentation.CustomScreen;
using Olca.Presentation.CustomScreen.CustomizeMode;
using Olca.Application.Battle;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Tests.Mocks;

namespace Olca.Tests
{
    public class CustomizeMaterialBoundaryTests
    {
        private SimpleCardFactory factory;
        private SimpleFusionRecipeProvider recipes;

        [SetUp]
        public void SetUp()
        {
            factory = new SimpleCardFactory();
            recipes = new SimpleFusionRecipeProvider();
        }

        // DungeonBattleControllerと同じ3サービス・実ファクトリ・実レシピを使用する。
        private CustomScreenSystem Open(params Card[] cards)
        {
            var model = new CustomScreenSystem(new Deck(cards.ToList()),
                exceedSystem: new ExceedSystem(factory), tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier),
                fusionSystem: new FusionSystem(recipes, factory));
            model.Open(new Card[0], new MockRandomProvider(0));
            return model;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 全サービス構成_三枚目の超越調律候補を拒否し二枚融合の表示と実行を維持する(bool tuning)
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var bomb = factory.CreateById(SimpleCardFactory.Bomb);
            var third = tuning
                ? new Card("tuning", "調律", 1, CardRarity.Normal, CardCategory.Tuning, ElementType.None, CardSeries.None, 1, "検証")
                : factory.CreateById(SimpleCardFactory.Sword);
            var model = Open(a, bomb, third);
            Assert.IsFalse(model.IsCompatibleWithCurrentSelection(new[] { a, bomb }, third));
            WithView(model, (view, mode, button, selected) =>
            {
                view.OnCardClicked(a);
                Assert.IsFalse(button.interactable);
                view.OnCardClicked(bomb);
                Assert.IsTrue(button.interactable);
                view.OnCardClicked(third);
                CollectionAssert.AreEqual(new[] { a, bomb }, selected);
                Assert.IsTrue(button.interactable, "元の有効な二枚融合は実行可能");
                Assert.AreEqual("Fusion", typeof(CustomScreenView).GetMethod("DetectCustomizeOperation", Flags).Invoke(view, null).ToString());
                typeof(CustomScreenView).GetMethod("OnCustomizeExecuteRequested", Flags).Invoke(view, null);
                Assert.AreEqual(2, model.CurrentHand.Count);
                Assert.IsTrue(model.CurrentHand.Contains(third));
                Assert.IsTrue(model.CurrentHand.Any(c => c.Id == SimpleCardFactory.FireSword));
            });
        }

        [Test]
        public void 三枚融合は候補でも直接実行でも拒否して手札を維持する()
        {
            var a=factory.CreateById(SimpleCardFactory.Sword);var b=factory.CreateById(SimpleCardFactory.Bomb);var c=factory.CreateById(SimpleCardFactory.Heal);var model=Open(a,b,c);
            Assert.IsFalse(model.IsCompatibleWithCurrentSelection(new[]{a,b},c));Assert.IsFalse(model.ExecuteFusion(new[]{a,b,c}).IsSuccess);
            CollectionAssert.AreEquivalent(new[]{a,b,c},model.CurrentHand);
        }

        [Test]
        public void 加工素材は使用予定から除外し素材の解除順は使用順と分離する()
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var b = factory.CreateById(SimpleCardFactory.Bomb);
            var model = Open(a, b);
            Assert.IsTrue(model.TrySelectCard(a));
            WithView(model, (view, mode, button, selected) =>
            {
                view.OnCardClicked(a);
                Assert.AreEqual(0, selected.Count, "使用予定カードは素材にできない");
                CollectionAssert.AreEqual(new[] { a }, model.SelectedCards);
                Assert.IsFalse(button.interactable);
                Assert.IsTrue(model.TryDeselectCard(a));
                view.OnCardClicked(a);
                view.OnCardClicked(b);
                Assert.IsTrue(button.interactable);
                view.OnCardClicked(a);
                CollectionAssert.AreEqual(new[] { b }, selected, "順不同レシピの素材解除は自由");
                Assert.IsFalse(button.interactable);
                Assert.AreEqual(0, model.SelectedCards.Count);
            });
        }

        [Test]
        public void 加工プレビューは本絵と専用miniを分け借用し素材とUndoを変更しない()
        {
            Assert.AreEqual("ソザイのケイトウがヒトつです",CustomDisplay.Katakana("素材の系統が一つです"));
            var a=factory.CreateById("sword");var b=factory.CreateById("bomb");var model=Open(a,b);var before=model.CurrentHand.ToArray();var texture=new Texture2D(16,16);var art=Sprite.Create(texture,new Rect(0,0,16,16),new Vector2(.5f,.5f));var mini=Sprite.Create(texture,new Rect(0,0,8,8),new Vector2(.5f,.5f));
            try{WithView(model,(view,mode,button,selected)=>{
                var imageObject=new GameObject("SelectedCardImage",typeof(RectTransform),typeof(Image));imageObject.transform.SetParent(view.transform,false);var image=imageObject.GetComponent<Image>();typeof(CustomScreenView).GetField("selectedCardImage",Flags).SetValue(view,image);
                var textObject=new GameObject("Description",typeof(RectTransform),typeof(Text));textObject.transform.SetParent(view.transform,false);var text=textObject.GetComponent<Text>();text.font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");typeof(CustomScreenView).GetField("selectedCardDescription",Flags).SetValue(view,text);
                view.ConfigureCardVisuals(new[]{new CardVisualEntry{CardId="fire_sword",Illustration=art,CustomIcon=mini},new CardVisualEntry{CardId="sword",CustomIcon=mini},new CardVisualEntry{CardId="bomb",CustomIcon=mini}});selected.Add(a);selected.Add(b);var update=typeof(CustomScreenView).GetMethod("UpdateCustomizationPreview",Flags);update.Invoke(view,new object[]{-1});
                Assert.AreSame(art,image.sprite);Assert.AreNotSame(mini,image.sprite);Assert.AreEqual(Color.white,image.color);Assert.AreEqual(2,view.GetComponentsInChildren<Image>().Count(i=>i.name=="MaterialIcon"&&i.sprite==mini));StringAssert.Contains("ソード",text.text);StringAssert.Contains("ボム",text.text);StringAssert.Contains("ミジッコウ",text.text);CollectionAssert.AreEqual(before,model.CurrentHand);Assert.AreEqual(0,model.CustomizationUndoCount);Assert.AreEqual(0,model.SelectedCards.Count);
                mode.Exit();typeof(CustomScreenView).GetMethod("UpdateProcessingControls",Flags).Invoke(view,null);Assert.IsFalse(view.GetComponentsInChildren<Image>().Any(i=>i.name=="MaterialIcon"));Assert.IsNull(image.sprite);mode.Enter();
                selected.Clear();update.Invoke(view,new object[]{-1});Assert.IsNull(image.sprite);Assert.AreEqual(0,image.color.a);var frames=view.GetComponentsInChildren<Image>().Where(i=>i.name.StartsWith("ProcessingMaterial_")).ToArray();Assert.AreEqual(2,frames.Length);Assert.IsTrue(frames.All(i=>!i.raycastTarget));
                var emptyIcons=view.GetComponentsInChildren<Image>().Where(i=>i.name=="MaterialIcon").ToArray();Assert.AreEqual(2,emptyIcons.Length);Assert.IsTrue(emptyIcons.All(i=>!i.enabled&&i.sprite==null&&!i.raycastTarget),"空き2枠は非入力で、前回のminiを描画しない");Assert.IsFalse(button.interactable);CollectionAssert.AreEqual(before,model.CurrentHand);Assert.AreEqual(0,model.CustomizationUndoCount);
            });Assert.IsTrue(art!=null);Assert.IsTrue(mini!=null);}finally{UnityEngine.Object.DestroyImmediate(art);UnityEngine.Object.DestroyImmediate(mini);UnityEngine.Object.DestroyImmediate(texture);}
        }
        [Test]
        public void 加工操作はJiki表示で通常確定を一時退避し取消後に同じ操作位置へ戻す()
        {
            var model=Open(factory.CreateById("sword"),factory.CreateById("bomb"));WithView(model,(view,mode,button,selected)=>{
                var normal=new GameObject("Confirm",typeof(RectTransform),typeof(Button));normal.transform.SetParent(view.transform,false);var rect=(RectTransform)normal.transform;rect.anchoredPosition=new Vector2(20,-400);typeof(CustomScreenView).GetField("confirmButton",Flags).SetValue(view,normal.GetComponent<Button>());var normalLabel=new GameObject("Label",typeof(RectTransform),typeof(Text));normalLabel.transform.SetParent(normal.transform,false);normalLabel.GetComponent<Text>().font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var guideObject=new GameObject("Guide",typeof(RectTransform),typeof(Text));guideObject.transform.SetParent(mode.transform,false);var guide=guideObject.GetComponent<Text>();typeof(CustomizeModeView).GetField("guideText",Flags).SetValue(mode,guide);var font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");mode.ConfigurePresentation(font);mode.ApplyLayout(1);Assert.AreSame(font,guide.font);Assert.AreEqual(Color.white,guide.color);
                var visibility=typeof(CustomScreenView).GetMethod("UpdateProcessingControls",Flags);visibility.Invoke(view,null);Assert.IsFalse(normal.activeSelf);typeof(CustomScreenView).GetMethod("ShowDetailsValue",Flags).Invoke(view,new object[]{"テスト"});var close=view.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseCustomPadDetails");Assert.IsTrue(close.gameObject.activeInHierarchy);close.onClick.Invoke();Assert.IsTrue(mode.IsActive);Assert.IsFalse(normal.activeSelf);mode.Exit();visibility.Invoke(view,null);Assert.IsTrue(normal.activeSelf);Assert.AreEqual(new Vector2(20,-400),rect.anchoredPosition);Assert.IsFalse(model.IsConfirming);Assert.AreEqual(0,model.CustomizationUndoCount);
            });
        }

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static void WithView(CustomScreenSystem model,
            System.Action<CustomScreenView, CustomizeModeView, Button, List<Card>> check)
        {
            var root = new GameObject("CustomizeBoundaryTest", typeof(RectTransform));
            var modeObject = new GameObject("Mode", typeof(RectTransform));
            modeObject.transform.SetParent(root.transform);
            try
            {
                var view = root.AddComponent<CustomScreenView>();
                var mode = modeObject.AddComponent<CustomizeModeView>();
                var button = modeObject.AddComponent<Button>();
                typeof(CustomizeModeView).GetField("executeButton", Flags).SetValue(mode, button);
                typeof(CustomScreenView).GetField("customScreen", Flags).SetValue(view, model);
                typeof(CustomScreenView).GetField("customizeModeView", Flags).SetValue(view, mode);
                mode.Enter();
                var selected = (List<Card>)typeof(CustomScreenView).GetField("customizeSelectedCards", Flags).GetValue(view);
                check(view, mode, button, selected);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [TestCase(true)] [TestCase(false)]
        public void 加工終了を同じフレームの論理確定取消へ重ねず使用予定を維持する(bool cancel)
        {
            Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            var a=factory.CreateById("sword");var b=factory.CreateById("bomb");var heal=factory.CreateById("heal");var model=Open(a,b,heal);Assert.IsTrue(model.TrySelectCard(heal));
            WithView(model,(view,mode,button,selected)=>{
                view.ConfigureLogicalConfirmation((stamp,action)=>action(),()=>System.Diagnostics.Stopwatch.GetTimestamp());
                if(cancel)typeof(CustomScreenView).GetMethod("OnCustomizeModeCancelled",Flags).Invoke(view,null);
                else {view.OnCardClicked(a);view.OnCardClicked(b);model.OnFused+=card=>mode.Exit();typeof(CustomScreenView).GetMethod("OnCustomizeExecuteRequested",Flags).Invoke(view,null);Assert.AreEqual(1,model.CustomizationUndoCount);}
                view.HandleLogicalConfirmation(new Olca.Core.Logic.Integration.LogicalInput{MenuCancelButtonDown=cancel,MenuConfirmButtonDown=!cancel});
                Assert.IsFalse(mode.IsActive);Assert.IsFalse(model.IsConfirming);CollectionAssert.AreEqual(new[]{heal},model.SelectedCards);
            });
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void 超越_同参照素材の実在枚数を確認する(int copies, bool allowed)
        {
            var card = factory.CreateById(SimpleCardFactory.Sword);
            var other = factory.CreateById(SimpleCardFactory.Heal);
            var cards = Enumerable.Repeat(card, copies).Concat(new[] { other }).ToArray();
            var model = Open(cards);
            model.TrySelectCard(other);
            var hand = model.CurrentHand.ToArray();
            var selection = model.SelectedCards.ToArray();
            int events = 0;
            model.OnExceeded += _ => events++;
            var result = model.ExecuteExceed(card, card);
            Assert.AreEqual(allowed, result.IsSuccess);
            Assert.AreEqual(allowed ? 1 : 0, events);
            CollectionAssert.AreEqual(selection, model.SelectedCards);
            if (allowed)
            {
                Assert.AreEqual(1, model.CurrentHand.Count);
                Assert.AreEqual(ExceedLevel.Lv1, model.CurrentHand[0].ExceedLevel);
            }
            else
            {
                Assert.AreEqual("指定カードが手札にありません", result.ErrorMessage);
                CollectionAssert.AreEqual(hand, model.CurrentHand);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 全サービス構成_異段階の同IDを融合候補として許可しない(bool reverse)
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var b = factory.CreateExceeded(a, ExceedLevel.Lv1);
            var bomb = factory.CreateById(SimpleCardFactory.Bomb);
            var model = Open(a, b, bomb);
            var first = reverse ? b : a;
            var second = reverse ? a : b;
            Assert.IsFalse(model.CanExceed(first, second));
            Assert.IsFalse(model.IsCompatibleWithCurrentSelection(new[] { first }, second));
            Assert.AreEqual(!reverse,model.IsCompatibleWithCurrentSelection(new[] { first }, bomb),"特殊レシピは元定義のソードだけを登録");
            Assert.IsFalse(model.ExecuteExceed(first, second).IsSuccess);
            Assert.IsFalse(model.ExecuteFusion(new[] { first, second }).IsSuccess);
            CollectionAssert.AreEqual(new[] { a, b, bomb }, model.CurrentHand);
            Assert.AreEqual(0, model.SelectedCards.Count);
        }

        [TestCase(ExceedLevel.Normal)]
        [TestCase(ExceedLevel.Lv1)]
        [TestCase(ExceedLevel.Lv2)]
        public void 全サービス構成_同段階超越を維持する(ExceedLevel level)
        {
            var original = factory.CreateById(SimpleCardFactory.Sword);
            var a = level == ExceedLevel.Normal ? original : factory.CreateExceeded(original, level);
            var b = level == ExceedLevel.Normal ? factory.CreateById(SimpleCardFactory.Sword) : factory.CreateExceeded(original, level);
            var model = Open(a, b);
            Assert.IsTrue(model.IsCompatibleWithCurrentSelection(new[] { a }, b));
            var result = model.ExecuteExceed(a, b);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual((ExceedLevel)((int)level + 1), result.ResultCard.ExceedLevel);
            Assert.AreEqual(1, model.CurrentHand.Count);
        }

        [Test]
        public void 全サービス構成_上限段階2枚を融合候補にしない()
        {
            var card = factory.CreateExceeded(factory.CreateById(SimpleCardFactory.Sword), ExceedLevel.Lv3);
            var other = factory.CreateExceeded(factory.CreateById(SimpleCardFactory.Sword), ExceedLevel.Lv3);
            var model = Open(card, other);
            Assert.IsFalse(model.IsCompatibleWithCurrentSelection(new[] { card }, other));
            Assert.IsFalse(model.ExecuteExceed(card, other).IsSuccess);
            Assert.AreEqual(2, model.CurrentHand.Count);
        }

        [Test]
        public void 融合候補_部分一致でも必要枚数を超えない()
        {
            Assert.IsTrue(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Sword }));
            Assert.IsTrue(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Bomb }));
            Assert.IsTrue(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Bomb, SimpleCardFactory.Sword }));
            Assert.IsFalse(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Sword, SimpleCardFactory.Sword }));
            Assert.IsFalse(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Bomb, SimpleCardFactory.Bomb }));
            Assert.IsFalse(recipes.HasRecipeContaining(new[] { SimpleCardFactory.Sword, SimpleCardFactory.Bomb, SimpleCardFactory.Sword }));
            Assert.IsFalse(recipes.HasRecipeContaining(null));
            Assert.IsFalse(recipes.HasRecipeContaining(new string[0]));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 全サービス構成_正規融合は素材順によらず成功する(bool reverse)
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var b = factory.CreateById(SimpleCardFactory.Bomb);
            var model = Open(a, b);
            var materials = reverse ? new[] { b, a } : new[] { a, b };
            Assert.IsTrue(model.IsCompatibleWithCurrentSelection(new[] { materials[0] }, materials[1]));
            var result = model.ExecuteFusion(materials);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(SimpleCardFactory.FireSword, result.ResultCard.Id);
            CollectionAssert.AreEqual(new[] { result.ResultCard }, model.CurrentHand);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void 融合_余分な同ID素材を消費せず拒否する(bool reverse)
        {
            var a = factory.CreateById(SimpleCardFactory.Sword);
            var b = factory.CreateById(SimpleCardFactory.Sword);
            var bomb = factory.CreateById(SimpleCardFactory.Bomb);
            var model = Open(a, b, bomb);
            var materials = reverse ? new[] { bomb, a, b } : new[] { a, b, bomb };
            Assert.IsNull(recipes.FindResultCardId(materials.Select(c => c.Id).ToArray()));
            Assert.IsFalse(model.ExecuteFusion(materials).IsSuccess);
            CollectionAssert.AreEqual(new[] { a, b, bomb }, model.CurrentHand);
        }
    }
    public class ProductCustomizationDefinitionTests
    {
        private sealed class Store : Olca.Core.Logic.Dungeon.ICheckpointStore
        {
            private readonly Olca.Core.Logic.Dungeon.MemoryCheckpointStore inner;
            public bool Fail;
            public Store(Olca.Core.Logic.Dungeon.CheckpointDefinitions definitions) { inner=new Olca.Core.Logic.Dungeon.MemoryCheckpointStore(definitions); }
            public Olca.Core.Domain.CheckpointDto Read()=>inner.Read();
            public bool TryWrite(Olca.Core.Domain.CheckpointDto dto,out string error) { if(Fail){error="保存失敗fixture";return false;}return inner.TryWrite(dto,out error); }
        }

        [TestCase(false)][TestCase(true)]
        public void 加工後の保存失敗と復帰は加工前の戦闘開始構成を保つ(bool tune)
        {
            var f=new SimpleCardFactory();var map=Olca.Core.Domain.DungeonConfig.CreateTestDungeon(20,30,3);
            var deck=new Deck(new List<Card>{f.CreateById("sword"),f.CreateById("sword"),f.CreateById("tuning")});
            var defs=Olca.Core.Logic.Dungeon.DungeonCheckpointSession.CreateDefault(map,deck).Definitions;
            var store=new Store(defs);var session=new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defs,store);
            var random=new MockRandomProvider(0);var logger=new MockLogger();
            var game=new Olca.Core.Logic.Integration.DungeonBattleManager(map,new PlayerCharacter(100),deck,random,logger,
                new ExceedSystem(f),new TuningSystem(f.GetTuningDefinition),checkpoints:session,cardDatabase:f);
            for(int n=0;n<20&&game.CurrentBattle==null;n++)game.Tick(50);
            Assert.IsNotNull(game.CurrentBattle);game.CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            game.CurrentBattle.OpenCustomScreen(random);var cs=game.CurrentBattle.CustomScreen;
            var a=cs.CurrentHand.First(c=>c.Id=="sword");var b=cs.CurrentHand.First(c=>tune?c.Id=="tuning":c.Id=="sword"&&!object.ReferenceEquals(c,a));
            Assert.IsTrue(cs.ExecuteCustomization(new[]{a,b}).CanExecute);var sequence=store.Read().Sequence;
            store.Fail=true;foreach(var e in game.CurrentBattle.Enemies.ToArray())e.TakeDamage(e.MaxHp,ElementType.None);
            game.SelectCardReward(null);game.SelectCurrencyReward(false);
            Assert.IsTrue(game.Dungeon.IsCheckpointBlocked);Assert.AreEqual(sequence,store.Read().Sequence);
            Assert.IsTrue(Olca.Core.Logic.Integration.DungeonBattleManager.TryRestoreCheckpoint(
                new Olca.Core.Logic.Dungeon.DungeonCheckpointSession(defs,store),random,logger,out var reopened,out var error,
                new ExceedSystem(f),new TuningSystem(f.GetTuningDefinition),cardDatabase:f),error);
            reopened.ActivateCheckpoint();var restored=reopened.CurrentBattle.CustomScreen;
            if(!restored.IsOpen){reopened.CurrentBattle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);reopened.CurrentBattle.OpenCustomScreen(random);}
            Assert.AreEqual(3,restored.CurrentHand.Count);Assert.AreEqual(2,restored.CurrentHand.Count(c=>c.Id=="sword"));
            Assert.IsTrue(restored.CurrentHand.All(c=>c.TuningCount==0&&c.ExceedLevel==ExceedLevel.Normal));
            store.Fail=false;Assert.IsTrue(game.RetryCheckpoint());Assert.AreEqual(3,game.Dungeon.GetCurrentDeck().TotalCount);
        }

        [Test]
        public void 調律は同名個体を区別して消費し取消で元の構成と値へ戻る()
        {
            var f = new SimpleCardFactory(); var a = f.CreateById("sword"); var b = f.CreateById("sword");
            var t = f.CreateById("tuning"); var deck = new Deck(new List<Card>{a,b,t});
            var cs = new CustomScreenSystem(deck, tuningSystem: new TuningSystem(f.GetTuningDefinition));
            cs.Open(new Card[0], new MockRandomProvider(0));
            var preview = cs.PreviewCustomization(new[]{t,a}); Assert.IsTrue(preview.CanExecute);
            Assert.AreEqual(40, preview.ResultCard.GetFinalPower()); Assert.AreEqual(3,cs.CurrentHand.Count);
            var result = cs.ExecuteTuning(a,t).ResultCard;
            Assert.IsNotNull(result); Assert.AreEqual(2,cs.CurrentHand.Count); Assert.Contains(b,cs.CurrentHand.ToList());
            Assert.IsFalse(cs.ExecuteTuning(a,t).IsSuccess); Assert.AreEqual(30,b.GetFinalPower());
            Assert.IsTrue(cs.TryUndoCustomization()); Assert.AreEqual(2,cs.CurrentHand.Count(c=>c.Id=="sword"));
            Assert.AreEqual(1,cs.CurrentHand.Count(c=>c.Id=="tuning"));
            Assert.AreEqual(3,cs.CurrentHand.Distinct().Count()); Assert.AreEqual(30,a.GetFinalPower());
            Assert.AreEqual(0,a.TuningCount); Assert.IsFalse(cs.TryUndoCustomization());
        }

        [Test]
        public void 調律は総回数に応じて加算し上位生成では標準状態へ戻る()
        {
            var f = new SimpleCardFactory(); var tuning = new TuningSystem(f.GetTuningDefinition);
            var a = f.CreateById("sword"); var material = f.CreateById("tuning");
            var first=tuning.Execute(a,material).ResultCard; var second=tuning.Execute(first,material).ResultCard;
            Assert.AreEqual(50,second.GetFinalPower()); Assert.AreEqual(2,second.TuningCount);
            Assert.AreEqual(1,second.GetFinalCost()); Assert.AreEqual(a.Element,second.Element);
            var upper=f.CreateExceeded(second,ExceedLevel.Lv1); Assert.AreEqual(45,upper.GetFinalPower());
            Assert.AreEqual(0,upper.TuningCount); Assert.AreEqual(30,a.GetFinalPower());
            Assert.IsNull(f.GetTuningDefinition(f.CreateById("bomb")));
        }

        [TestCase(ExceedLevel.Normal,ExceedLevel.Lv1,45)]
        [TestCase(ExceedLevel.Lv1,ExceedLevel.Lv2,60)]
        [TestCase(ExceedLevel.Lv2,ExceedLevel.Lv3,90)]
        public void ソード上位定義は元の加工値によらず固定値と固有名を参照する(ExceedLevel before,ExceedLevel after,int power)
        {
            var f=new SimpleCardFactory();var normal=f.CreateById("sword");
            var a=before==ExceedLevel.Normal?normal:f.CreateExceeded(normal,before);
            a.Modifier.BonusDamage=999; Assert.IsTrue(f.HasExceededDefinition(a,after));
            var result=new ExceedSystem(f).Execute(a,a.Clone()).ResultCard;
            Assert.AreEqual(power,result.GetFinalPower()); Assert.AreNotEqual(a.Name,result.Name);
            Assert.AreEqual(a.Series,result.Series); Assert.AreEqual(after,result.ExceedLevel);
            Assert.AreEqual(0,result.Modifier.BonusDamage); Assert.AreEqual(0,result.TuningCount);
            Assert.IsFalse(f.HasExceededDefinition(f.CreateById("bomb"),ExceedLevel.Lv1));
            Assert.IsFalse(f.HasExceededDefinition(normal,ExceedLevel.Lv3));
        }

        [Test]
        public void 超越確定の素材消費と取消は元の二個体を復元する()
        {
            var f=new SimpleCardFactory();var a=f.CreateById("sword");var b=f.CreateById("sword");
            var deck=new Deck(new List<Card>{a,b});var cs=new CustomScreenSystem(deck,exceedSystem:new ExceedSystem(f));
            cs.Open(new Card[0],new MockRandomProvider(0));var preview=cs.PreviewCustomization(new[]{a,b});
            Assert.IsTrue(preview.CanExecute);Assert.AreEqual(2,cs.CurrentHand.Count);
            var result=cs.ExecuteExceed(a,b);Assert.IsTrue(result.IsSuccess);Assert.AreEqual(1,cs.CurrentHand.Count);
            Assert.IsFalse(cs.ExecuteExceed(a,b).IsSuccess);Assert.IsTrue(cs.TryUndoCustomization());
            Assert.AreEqual(2,cs.CurrentHand.Count(c=>c.Id=="sword"&&c.Power==30&&c.ExceedLevel==ExceedLevel.Normal));
            Assert.AreEqual(2,cs.CurrentHand.Distinct().Count());
            Assert.AreEqual(2,deck.GetAllCards().Count);Assert.AreEqual(30,a.Power);
        }
    }

}
