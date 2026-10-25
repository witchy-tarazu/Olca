using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Presentation.CustomScreen;
using Olca.Presentation.Field;
using Olca.Presentation.Dungeon;
using Olca.Tests.Mocks;
using UnityEngine;
using UnityEngine.UI;

namespace Olca.Tests
{
    public class CustomScreenCostViewTests
    {
        [Test]
        public void 出撃前情報は全階層の実定義を読むだけで敵や地形を変えず独立した一覧を返す()
        {
            var cells=Enumerable.Range(0,8).Select(i=>new MapCell(i)).ToList();
            var fire=new Enemy("fire","テスト",100,ElementType.Fire,new Olca.Core.Domain.Vector2Int(3,0));
            cells[3]=new MapCell(3,fire);cells[4]=new MapCell(4,new Enemy("summon","テスト",10,ElementType.Earth,new Olca.Core.Domain.Vector2Int(4,0),true));
            var terrain=new[]{new ExplorationTerrainCell(0,PanelType.Grass),new ExplorationTerrainCell(1,PanelType.Grass),new ExplorationTerrainCell(2,PanelType.Grass),new ExplorationTerrainCell(5,PanelType.Ice)};
            var a=new DungeonConfig("a","テスト",8,cells,explorationTerrain:terrain);
            var bCells=Enumerable.Range(0,4).Select(i=>new MapCell(i)).ToList();bCells[3]=new MapCell(3,new Enemy("water","テスト",100,ElementType.Water,new Olca.Core.Domain.Vector2Int(3,0)));
            var b=new DungeonConfig("b","テスト",4,bCells,explorationTerrain:new[]{new ExplorationTerrainCell(0,PanelType.Magma),new ExplorationTerrainCell(1,PanelType.Magma),new ExplorationTerrainCell(2,PanelType.Ice),new ExplorationTerrainCell(3,PanelType.Ice)});
            var info=new DungeonDeparturePreview(new[]{a,b});
            CollectionAssert.AreEqual(new[]{ElementType.Fire,ElementType.Water},info.EnemyElements);
            CollectionAssert.AreEqual(new[]{PanelType.Normal,PanelType.Ice,PanelType.Grass,PanelType.Magma},info.TerrainTypes);
            Assert.AreEqual(100,fire.CurrentHp);CollectionAssert.AreEqual(terrain,a.ExplorationTerrain);
            a.MapCells[3]=new MapCell(3);CollectionAssert.AreEqual(new[]{ElementType.Fire,ElementType.Water},info.EnemyElements);
            Assert.Throws<System.NotSupportedException>(()=>((IList<ElementType>)info.EnemyElements).Add(ElementType.Wind));
            var text=ClearReturnView.FormatDungeonInformation(info);StringAssert.Contains("ヒ / ミズ",text);StringAssert.Contains("ノーマル / コオリ / クサムラ / マグマ",text);Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text,"[一-鿿]"));
        }
        [Test]
        public void 敵や地形の情報がない場合に属性や推奨レベルを捏造しない()
        {
            var info=new DungeonDeparturePreview(new[]{new DungeonConfig("empty","テスト",0,new List<MapCell>())});
            Assert.IsEmpty(info.EnemyElements);Assert.IsEmpty(info.TerrainTypes);
            Assert.AreEqual("テキゾクセイ: ジョウホウナシ　チケイ: ジョウホウナシ",ClearReturnView.FormatDungeonInformation(info));
            Assert.Throws<System.ArgumentNullException>(()=>new DungeonDeparturePreview(null));
            Assert.Throws<System.ArgumentException>(()=>new DungeonDeparturePreview(new DungeonConfig[0]));
            Assert.Throws<System.ArgumentException>(()=>new DungeonDeparturePreview(new DungeonConfig[]{null}));
        }
        [Test]
        public void 未解放名と詳細はページを変えても露出せず解放条件とクリア履歴を保持する()
        {
            var root=new GameObject("DungeonSelectionPreview",typeof(RectTransform));ClearReturnView view=null;
            try {
                view=root.AddComponent<ClearReturnView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),null,null,null,null);
                var names=Enumerable.Range(0,7).Select(i=>"secret-name-"+i).ToArray();var unlocked=new[]{true,false,false,false,false,false,false};var cleared=new[]{true,false,false,false,false,false,false};
                var conditions=Enumerable.Repeat("ヒトツマエノダンジョンヲクリア",7).ToArray();var descriptions=Enumerable.Range(0,7).Select(i=>"secret-info-"+i).ToArray();
                view.RenderDungeonSelection(names,unlocked,0,_=>{},()=>{},()=>{},false,null,()=>{},cleared:cleared,unlockConditions:conditions,descriptions:descriptions);
                var first=root.GetComponentsInChildren<Button>().Single(b=>b.name=="DungeonChoice_0");StringAssert.Contains("クリアズミ",first.GetComponentInChildren<Text>().text);
                var locked=root.GetComponentsInChildren<Button>().Single(b=>b.name=="DungeonChoice_1");Assert.IsFalse(locked.interactable);StringAssert.Contains("？？？",locked.GetComponentInChildren<Text>().text);StringAssert.DoesNotContain(names[1],locked.GetComponentInChildren<Text>().text);
                root.GetComponentsInChildren<Button>().Single(b=>b.name=="NextDungeonPage").onClick.Invoke();
                var all=string.Join("\n",root.GetComponentsInChildren<Text>().Select(t=>t.text));StringAssert.DoesNotContain(names[5],all);StringAssert.DoesNotContain(names[6],all);StringAssert.Contains(conditions[6],all);
                view.RenderDungeonSelection(names,unlocked,6,_=>{},()=>{},()=>{},false,null,()=>{},cleared:cleared,unlockConditions:conditions,descriptions:descriptions);
                all=string.Join("\n",root.GetComponentsInChildren<Text>().Select(t=>t.text));StringAssert.DoesNotContain(descriptions[6],all);
            }finally{if(view!=null)typeof(ClearReturnView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Object.DestroyImmediate(root);}
        }

        [Test]
        public void 非アクティブな親でも手札初期化と選択がAwakeに依存せず再表示で寿命を作り直さない()
        {
            var root=new GameObject("InactiveCustom",typeof(RectTransform));root.SetActive(false);
            try {
                var parent=root.AddComponent<CustomScreenView>();
                var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandCardViewPrefab.prefab");
                var obj=Object.Instantiate(prefab,root.transform);var hand=obj.GetComponent<HandCardView>();
                var card=Create("sword",CardSeries.Sword);
                hand.Initialize(card,parent);
                var field=typeof(HandCardView).GetField("destroyCts",BindingFlags.Instance|BindingFlags.NonPublic);
                var lifetime=field.GetValue(hand);Assert.IsNotNull(lifetime);
                Assert.DoesNotThrow(()=>hand.UpdateSelection(false));
                Assert.DoesNotThrow(()=>hand.UpdateHighlight(CardHighlightType.None));
                root.SetActive(true);Assert.AreSame(lifetime,field.GetValue(hand));
                root.SetActive(false);hand.Initialize(card,parent);root.SetActive(true);
                Assert.AreSame(lifetime,field.GetValue(hand));Assert.AreSame(card,hand.Card);
            } finally {Object.DestroyImmediate(root);}
        }

        [TestCase("close")] [TestCase("disable")] [TestCase("destroy")] [TestCase("reinitialize")]
        public async System.Threading.Tasks.Task 手札射出中に閉じる無効化破棄しても後続カードを生成しない(string ending)
        {
            Olca.Unity.GameplayPause.SetPaused(false);
            var root=new GameObject("CustomLifetime",typeof(RectTransform));
            try {
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var confirm=new GameObject("Confirm",typeof(RectTransform),typeof(Image),typeof(Button));confirm.transform.SetParent(root.transform,false);
                var container=new GameObject("Hand",typeof(RectTransform));container.transform.SetParent(root.transform,false);
                typeof(CustomScreenView).GetField("confirmButton",flags).SetValue(view,confirm.GetComponent<Button>());
                typeof(CustomScreenView).GetField("handCardContainer",flags).SetValue(view,container.transform);
                typeof(CustomScreenView).GetField("handCardPrefab",flags).SetValue(view,UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandCardViewPrefab.prefab"));
                typeof(CustomScreenView).GetField("cardShootInterval",flags).SetValue(view,.15f);
                var model=new CustomScreenSystem(new Deck(new List<Card>{Create("a",CardSeries.Sword),Create("b",CardSeries.Sword),Create("c",CardSeries.Sword)}));
                view.Initialize(model);model.Open(new Card[0],new MockRandomProvider(0));
                var cards=(List<HandCardView>)typeof(CustomScreenView).GetField("handCardViews",flags).GetValue(view);
                Assert.AreEqual(1,cards.Count,"最初のカードの射出中に終了させる");
                if(ending=="close")model.Close();
                else if(ending=="disable"){
                    // EditModeでは通常のMonoBehaviour通知をUnityが実行しないため明示する。
                    typeof(CustomScreenView).GetMethod("OnDisable",flags).Invoke(view,null);root.SetActive(false);
                }
                else if(ending=="reinitialize")view.Initialize(new CustomScreenSystem(new Deck(new List<Card>())));
                else {typeof(CustomScreenView).GetMethod("OnDestroy",flags).Invoke(view,null);Object.DestroyImmediate(root);root=null;}
                await System.Threading.Tasks.Task.Delay(600);
                Assert.AreEqual(1,cards.Count,"旧タスクが画面終了後もカードを追加してはならない");
                if(root!=null)Assert.IsFalse((bool)typeof(CustomScreenView).GetField("handAnimating",flags).GetValue(view));
            } finally {if(root!=null){typeof(CustomScreenView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root.GetComponent<CustomScreenView>(),null);Object.DestroyImmediate(root);}Olca.Unity.BattleVisualClock.Refresh();}
        }

        [Test]
        public void 左上の本絵はマウスと共通カーソル移動で切替わり未登録時はミニを流用しない()
        {
            Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            var root=new GameObject("IllustrationBinding",typeof(RectTransform));var texture=new Texture2D(2,1);
            var a=Sprite.Create(texture,new Rect(0,0,1,1),Vector2.one*.5f);var b=Sprite.Create(texture,new Rect(1,0,1,1),Vector2.one*.5f);
            try {
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var model=new CustomScreenSystem(new Deck(new List<Card>{Create("sword",CardSeries.Sword),Create("heal",CardSeries.Sword)}));model.Open(new Card[0],new MockRandomProvider(0));
                var hand=model.CurrentHand.ToList();typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,model);typeof(CustomScreenView).GetField("inputHand",flags).SetValue(view,hand);
                var imageObject=new GameObject("FocusedCardIllustration",typeof(RectTransform),typeof(Image));imageObject.transform.SetParent(root.transform,false);var image=imageObject.GetComponent<Image>();image.rectTransform.sizeDelta=new Vector2(280,280);
                typeof(CustomScreenView).GetField("selectedCardImage",flags).SetValue(view,image);
                var cursor=new CustomScreenCursor();typeof(CustomScreenView).GetField("cursor",flags).SetValue(view,cursor);
                cursor.OnCursorMoved+=index=>typeof(CustomScreenView).GetMethod("OnCursorMoved",flags).Invoke(view,new object[]{index});cursor.Initialize(2);
                view.ConfigureCardVisuals(new[]{new CardVisualEntry{CardId=hand[0].Id,Illustration=a},new CardVisualEntry{CardId=hand[1].Id,Illustration=b}});
                Assert.AreSame(a,image.sprite);Assert.AreEqual(Color.white,image.color);Assert.IsTrue(image.isActiveAndEnabled);Assert.IsTrue(image.preserveAspect);
                cursor.MoveRight();Assert.AreSame(b,image.sprite);
                var handViews=(List<HandCardView>)typeof(CustomScreenView).GetField("handCardViews",flags).GetValue(view);
                for(int i=0;i<2;i++){var obj=new GameObject("Hand"+i);obj.transform.SetParent(root.transform,false);var h=obj.AddComponent<HandCardView>();typeof(HandCardView).GetField("parentView",flags).SetValue(h,view);typeof(HandCardView).GetField("<Card>k__BackingField",flags).SetValue(h,hand[i]);handViews.Add(h);}
                handViews[0].OnPointerEnter(null);Assert.AreSame(a,image.sprite);Assert.AreEqual(0,cursor.CurrentIndex);
                cursor.MoveToConfirmButton();Assert.IsNull(image.sprite);cursor.MoveToCard(1);Assert.AreSame(b,image.sprite);
                view.ConfigureCardVisuals(null);Assert.IsNull(image.sprite);Assert.AreEqual(new Vector2(280,280),image.rectTransform.sizeDelta);
            }finally{Object.DestroyImmediate(root);Object.DestroyImmediate(a);Object.DestroyImmediate(b);Object.DestroyImmediate(texture);}
        }
        [Test]
        public void 操作した機器だけでガイドが切替わり離し入力や微小スティック揺れでは奪い返さない()
        {
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            Olca.Unity.InputGuideActivity.EnsureInitialized();
            try {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.A));UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.KeyboardMouse,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.West));UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.Gamepad,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.Gamepad,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState{delta=Vector2.right,position=new Vector2(10,20)});UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.KeyboardMouse,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState{leftStick=new Vector2(.01f,0)});UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.KeyboardMouse,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState{leftStick=new Vector2(.6f,0)});UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.Gamepad,Olca.Unity.InputGuideActivity.Current);
            } finally {UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);typeof(Olca.Unity.InputGuideActivity).GetMethod("Reset",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);}
        }
        [TestCase(false)] [TestCase(true)]
        public void ガイドは最後の機器だけを表示し既存の再割当とカナ表記を維持する(bool battle)
        {
            var profile=Olca.Unity.UnityInputProvider.CreateDefaultKeyboardProfile();var phase=battle?Olca.Core.Logic.Integration.GamePhase.Battle:Olca.Core.Logic.Integration.GamePhase.Dungeon;
            var keyboard=GameFieldView.FormatGameplayInstructionsForDevice(profile,phase,Olca.Unity.InputGuideDevice.KeyboardMouse);
            var pad=GameFieldView.FormatGameplayInstructionsForDevice(profile,phase,Olca.Unity.InputGuideDevice.Gamepad);
            StringAssert.Contains("Escape",keyboard);StringAssert.DoesNotContain("West",keyboard);StringAssert.DoesNotContain("South",keyboard);StringAssert.DoesNotContain("Escape",pad);
            StringAssert.Contains(battle?"West":"South",pad);
            var edit=profile.BeginEdit();Assert.IsTrue(edit.TryAssign(("keyboard",battle?"battle":"exploration",battle?"AttackPrimary":"JumpPrimary"),"J",out _));
            StringAssert.Contains("J",GameFieldView.FormatGameplayInstructionsForDevice(edit.Preview(),phase,Olca.Unity.InputGuideDevice.KeyboardMouse));
            var rendered=CustomDisplay.Katakana(pad);Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(rendered,"[一-鿿]"));
        }

        [Test]
        public async System.Threading.Tasks.Task カスタム停止中もHUD退場遷移を完了してカスタム画面の登場を妨げない()
        {
            var root=new GameObject("HudTransition");var owner=new object();
            using(var cancel=new System.Threading.CancellationTokenSource()){
                try {
                    Olca.Unity.GameplayPause.SetPaused(false);
                    var group=root.AddComponent<CanvasGroup>();var field=root.AddComponent<GameFieldView>();
                    typeof(GameFieldView).GetField("dungeonHudCanvasGroup",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(field,group);
                    Olca.Unity.BattleVisualClock.SetPaused(owner,true);
                    var task=field.HideHUDAsync(.001f,cancel.Token).AsTask();
                    Assert.AreSame(task,await System.Threading.Tasks.Task.WhenAny(task,System.Threading.Tasks.Task.Delay(3000)));
                    await task;Assert.AreEqual(0f,group.alpha);Assert.IsTrue(Olca.Unity.BattleVisualClock.IsPaused);
                } finally {cancel.Cancel();Olca.Unity.BattleVisualClock.SetPaused(owner,false);Object.DestroyImmediate(root);}
            }
        }

        [Test]
        public void 描画前のカナ変換は文字データだけでなく同一フレームの文字メッシュにも反映する()
        {
            var root=new GameObject("KanaCanvas",typeof(RectTransform),typeof(Canvas));
            try {
                root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
                var child=new GameObject("KanaText",typeof(RectTransform),typeof(Text));child.transform.SetParent(root.transform,false);
                var label=child.GetComponent<Text>();label.font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");label.fontSize=28;
                label.rectTransform.sizeDelta=new Vector2(400,100);label.horizontalOverflow=HorizontalWrapMode.Overflow;
                label.text="カナ";Canvas.ForceUpdateCanvases();UiLook.Label(label);
                var kana=label.GetComponent<UiKanaLabel>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(UiKanaLabel).GetMethod("OnDisable",flags).Invoke(kana,null);
                typeof(UiKanaLabel).GetMethod("OnEnable",flags).Invoke(kana,null);
                label.text="通貨";Canvas.ForceUpdateCanvases();
                Assert.AreEqual("ツウカ",label.text);Assert.AreEqual(3,label.cachedTextGenerator.characterCountVisible);
            } finally {foreach(var kana in root.GetComponentsInChildren<UiKanaLabel>(true))typeof(UiKanaLabel).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(kana,null);Object.DestroyImmediate(root);}
        }

        [Test]
        public void 変身の登録名称とアイコンは一覧とHUDへ連携し未登録素材を作らない()
        {
            var root=new GameObject("TransformationMetadata",typeof(RectTransform));
            var texture=new Texture2D(1,1);var sprite=Sprite.Create(texture,new Rect(0,0,1,1),Vector2.one*.5f);
            var player=new PlayerCharacter(100,new Olca.Core.Domain.Vector2Int(0,0));
            var battle=new BattleSystem(player,new List<Enemy>{new Enemy("fixtureEnemy","fixtureEnemy",50,ElementType.None,new Olca.Core.Domain.Vector2Int(4,0))},new Deck(new List<Card>{Create("sword",CardSeries.Sword)}));
            try {
                var container=new GameObject("Custom",typeof(RectTransform));container.transform.SetParent(root.transform,false);
                var view=container.AddComponent<CustomScreenView>();
                var confirm=new GameObject("Confirm",typeof(RectTransform),typeof(Image),typeof(Button));confirm.transform.SetParent(container.transform,false);
                var text=new GameObject("Label",typeof(RectTransform),typeof(Text));text.transform.SetParent(confirm.transform,false);
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(CustomScreenView).GetField("confirmButton",flags).SetValue(view,confirm.GetComponent<Button>());
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,battle.CustomScreen);
                var entry=new TransformationVisualEntry{Id="fixture",DisplayNameKana="テストヘンシン",Icon=sprite};
                view.ConfigureTransformationVisuals(new[]{entry});entry.DisplayNameKana="カキカエ";
                Assert.AreEqual("テストヘンシン",view.GetTransformationName("fixture"));Assert.IsNull(view.GetTransformationIcon("unregistered"));
                Assert.Throws<System.ArgumentException>(()=>view.ConfigureTransformationVisuals(new[]{new TransformationVisualEntry{Id="fixture",DisplayNameKana="漢字"}}));
                battle.Transformations.Configure(new[]{new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire)});
                battle.SetFirstStrike();battle.CustomScreen.Open(new Card[0],new MockRandomProvider(0));view.BindTransformations(battle.Transformations);
                var choice=container.transform.Find("TransformationChoice0");Assert.IsNotNull(choice);
                StringAssert.Contains("テストヘンシン",choice.GetComponentInChildren<Text>().text);Assert.AreSame(sprite,choice.Find("TransformationIcon").GetComponent<Image>().sprite);
                Assert.IsFalse(choice.Find("TransformationIcon").GetComponent<Image>().raycastTarget);
                Assert.IsTrue(battle.Transformations.TryPreview("fixture"));battle.CustomScreen.Confirm();
                var hud=root.AddComponent<Olca.Presentation.Battle.BattleView>();
                typeof(Olca.Presentation.Battle.BattleView).GetField("customScreenContainer",flags).SetValue(hud,container.GetComponent<RectTransform>());
                typeof(Olca.Presentation.Battle.BattleView).GetField("battleSystem",flags).SetValue(hud,battle);
                typeof(Olca.Presentation.Battle.BattleView).GetMethod("UpdateTransformationVisual",flags).Invoke(hud,null);
                var status=root.GetComponentsInChildren<Text>(true).Single(t=>t.name=="TransformationHud");StringAssert.Contains("テストヘンシン",status.text);
                Assert.AreSame(sprite,root.GetComponentsInChildren<Image>(true).Single(i=>i.name=="TransformationHudIcon").sprite);
                view.ConfigureTransformationVisuals(null);
                typeof(Olca.Presentation.Battle.BattleView).GetMethod("UpdateTransformationVisual",flags).Invoke(hud,null);
                Assert.IsFalse(root.GetComponentsInChildren<Image>(true).Single(i=>i.name=="TransformationHudIcon").gameObject.activeSelf);
            } finally {battle.Transformations.Dispose();Object.DestroyImmediate(root);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }

        [Test]
        public void 閉じた画面の内部クリーンアップは末尾解除を拒否せず仮選択を戻せる()
        {
            var card=Create("sword",CardSeries.Sword);var model=new CustomScreenSystem(new Deck(new List<Card>{card}));
            model.Open(new Card[0],new MockRandomProvider(0));Assert.IsTrue(model.TrySelectCard(card));model.CompleteClose();
            Assert.IsFalse(model.IsOpen);Assert.IsTrue(model.TryDeselectCard(card));Assert.IsEmpty(model.SelectedCards);
            Assert.Contains(card,model.CurrentHand.ToList());
        }

        [Test]
        public void 選択済みスロットは対応アイコンを表示しクリックした同名カード一枚だけ解除して順序とフォーカスを揃える()
        {
            Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);
            Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            var root=new GameObject("SelectedSlots",typeof(RectTransform));
            try {
                var model=new CustomScreenSystem(new Deck(new List<Card>{Create("sword",CardSeries.Sword),Create("sword",CardSeries.Sword),Create("heal",CardSeries.Sword)}));
                model.Open(new Card[0],new MockRandomProvider(0));var hand=model.CurrentHand.ToList();
                foreach(var card in hand)Assert.IsTrue(model.TrySelectCard(card));
                Assert.IsFalse(model.TryDeselectCard(hand[1]),"既存の取消は末尾だけを解除する契約を保つ");
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,model);
                typeof(CustomScreenView).GetField("inputHand",flags).SetValue(view,hand);
                var cursor=new CustomScreenCursor();cursor.Initialize(hand.Count);typeof(CustomScreenView).GetField("cursor",flags).SetValue(view,cursor);
                var cost=new GameObject("Cost",typeof(RectTransform),typeof(Text));cost.transform.SetParent(root.transform,false);
                typeof(CustomScreenView).GetField("costDisplay",flags).SetValue(view,cost.GetComponent<Text>());
                var indicator=new GameObject("SelectionIndicator",typeof(RectTransform));indicator.transform.SetParent(root.transform,false);
                var slots=new List<Image>();for(int i=0;i<3;i++){var slot=new GameObject("Slot"+i,typeof(RectTransform),typeof(Image));slot.transform.SetParent(indicator.transform,false);((RectTransform)slot.transform).anchoredPosition=new Vector2(50*i,0);slots.Add(slot.GetComponent<Image>());}
                typeof(CustomScreenView).GetMethod("UpdateSelectedCardDisplay",flags).Invoke(view,null);
                for(int i=0;i<3;i++)Assert.AreSame(view.GetCardIcon(hand[i].Id),slots[i].sprite);
                slots[1].GetComponent<Button>().onClick.Invoke();
                CollectionAssert.AreEqual(new[]{hand[0],hand[2]},model.SelectedCards);
                Assert.AreSame(view.GetCardIcon(hand[2].Id),slots[1].sprite);Assert.IsNull(slots[2].sprite);Assert.IsFalse(slots[2].raycastTarget);
                Assert.AreEqual(1,cursor.CurrentIndex);StringAssert.Contains(model.GetTotalUsedCost().ToString(),cost.GetComponent<Text>().text);
                slots[0].GetComponent<Button>().onClick.Invoke();CollectionAssert.AreEqual(new[]{hand[2]},model.SelectedCards);
            } finally {Object.DestroyImmediate(root);}
        }
        [Test]
        public void フォーカス枠は背景より前で手札アイコンより後ろに描きポインター入力を遮らない()
        {
            var root=new GameObject("FocusOrder",typeof(RectTransform));
            try {var background=new GameObject("BgCustom",typeof(RectTransform),typeof(Image));background.transform.SetParent(root.transform,false);
                var cards=new GameObject("Cards",typeof(RectTransform));cards.transform.SetParent(root.transform,false);
                var focus=new GameObject("Focus",typeof(RectTransform),typeof(Image));focus.transform.SetParent(root.transform,false);
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(CustomScreenView).GetField("handCardContainer",flags).SetValue(view,cards.transform);
                typeof(CustomScreenView).GetField("cursorIndicator",flags).SetValue(view,focus);
                for(int i=0;i<3;i++)typeof(CustomScreenView).GetMethod("UpdateCursorIndicator",flags).Invoke(view,new object[]{-1});
                Assert.Less(background.transform.GetSiblingIndex(),focus.transform.GetSiblingIndex());Assert.Less(focus.transform.GetSiblingIndex(),cards.transform.GetSiblingIndex());Assert.IsFalse(focus.GetComponent<Image>().raycastTarget);
            } finally {Object.DestroyImmediate(root);}
        }
        [Test]
        public void カスタム停止は戦闘Animatorの元速度を復元しUIアニメと入力を止めない()
        {
            var world=new GameObject("BattleCharacter",typeof(Animator));var ui=new GameObject("Ui",typeof(Canvas),typeof(Animator));var a=new object();var b=new object();
            try {world.GetComponent<Animator>().speed=.55f;ui.GetComponent<Animator>().speed=.8f;
                bool inputBlocked=Olca.Unity.GameplayPause.InputBlocked;
                Olca.Unity.BattleVisualClock.SetPaused(a,true);Olca.Unity.BattleVisualClock.SetPaused(b,true);
                Assert.AreEqual(0f,world.GetComponent<Animator>().speed);Assert.AreEqual(.8f,ui.GetComponent<Animator>().speed);Assert.AreEqual(0f,Olca.Unity.BattleVisualClock.DeltaTime);Assert.AreEqual(inputBlocked,Olca.Unity.GameplayPause.InputBlocked);
                Olca.Unity.BattleVisualClock.SetPaused(a,false);Assert.AreEqual(0f,world.GetComponent<Animator>().speed);
                Olca.Unity.BattleVisualClock.SetPaused(b,false);Assert.AreEqual(.55f,world.GetComponent<Animator>().speed);
            } finally {Olca.Unity.BattleVisualClock.SetPaused(a,false);Olca.Unity.BattleVisualClock.SetPaused(b,false);Object.DestroyImmediate(world);Object.DestroyImmediate(ui);}
        }
        [TestCase("選択したカードを確定します","センタクシタカードヲカクテイシマス")]
        [TestCase("前方1マス炎属性攻撃","ゼンポウ1マスホノオゾクセイコウゲキ")]
        [TestCase("使用済み / 手札上限","シヨウズミ / テフダジョウゲン")]
        [TestCase("十字左 / 獲得: 3枚 / 通貨: 0","ジュウジヒダリ / カクトク: 3マイ / ツウカ: 0")]
        [TestCase("続きから","ツヅキカラ")]
        [TestCase("変身演出：通常 / 短縮","ヘンシンエンシュツ：ツウジョウ / タンシュク")]
        [TestCase("音量設定","オンリョウセッテイ")]
        [TestCase("文字表示：標準 / ゆっくり / 速い / 即時","モジヒョウジ：ヒョウジュン / ユックリ / ハヤイ / ソクジ")]
        [TestCase("演出設定 保存失敗（再試行）","エンシュツセッテイ ホゾンシッパイ（サイシコウ）")]
        public void 指定フォントを交換せず既存カスタム文言をカナで表示する(string source,string expected)
            =>Assert.AreEqual(expected,CustomDisplay.Katakana(source));

        [Test]
        public void タッチ短押しはクリックを残し長押しは一度だけ詳細を開いて解放クリックを拒否する()
        {
            WithTouchHold((hold,screen,group,opened,valid)=>{
                var tap=Touch(screen,1);hold.OnPointerDown(tap);hold.OnPointerUp(tap);
                Assert.IsTrue(tap.eligibleForClick);Assert.AreEqual(0,opened());
                var press=Touch(screen,2);hold.OnPointerDown(press);AdvanceHold(hold);AdvanceHold(hold);
                Assert.AreEqual(1,opened());Assert.IsFalse(press.eligibleForClick);hold.OnPointerUp(press);Assert.AreEqual(1,opened());
                var mouse=new UnityEngine.InputSystem.UI.ExtendedPointerEventData(null){pointerId=-1,eligibleForClick=true};
                hold.OnPointerDown(mouse);AdvanceHold(hold);hold.OnPointerUp(mouse);Assert.IsTrue(mouse.eligibleForClick);Assert.AreEqual(1,opened());
            });
        }
        [Test]
        public void 長押しの離脱ドラッグ取消無効化とOS取消は詳細もクリックも発生させない()
        {
            WithTouchHold((hold,screen,group,opened,valid)=>{
                for(int i=0;i<8;i++){
                    var press=Touch(screen,10+i);hold.OnPointerDown(press);
                    if(i==0)hold.OnPointerExit(press);
                    else if(i==1){press.position=new Vector2(100,100);hold.OnPointerMove(press);}
                    else if(i==2)hold.OnCancel(null);
                    else if(i==3){valid(false);}
                    else if(i==4)hold.enabled=false;
                    else if(i==5)hold.GetComponent<Button>().enabled=false;
                    else if(i==6)hold.GetComponent<Button>().interactable=false;
                    else {var guard=hold.GetComponent<Olca.Unity.TouchUiCancelGuard>();guard.OnPointerDown(press);Olca.Unity.TouchUiCancelGuard.CancelTouch(screen.deviceId,press.touchId);}
                    AdvanceHold(hold);hold.OnPointerUp(press);Assert.IsFalse(press.eligibleForClick,"case "+i);Assert.AreEqual(0,opened());hold.enabled=true;hold.GetComponent<Button>().enabled=true;hold.GetComponent<Button>().interactable=true;valid(true);
                }
                valid(false);var invalid=Touch(screen,20);hold.OnPointerDown(invalid);Assert.IsFalse(invalid.eligibleForClick);AdvanceHold(hold);Assert.AreEqual(0,opened());
            });
        }
        [Test]
        public void 複数指は両方の長押しとクリックを抑え全解放後の新規押下だけを受け付ける()
        {
            WithTouchHold((hold,screen,group,opened,valid)=>{
                var other=new GameObject("SecondTouchDetail",typeof(RectTransform),typeof(Image),typeof(Button));
                try{var second=other.AddComponent<CustomDetailHold>();second.Configure(other.GetComponent<Button>(),()=>true,()=>Assert.Fail("second finger opened"),group);
                    var a=Touch(screen,1);var b=Touch(screen,2);hold.OnPointerDown(a);second.OnPointerDown(b);AdvanceHold(hold);AdvanceHold(second);
                    Assert.IsFalse(a.eligibleForClick);Assert.IsFalse(b.eligibleForClick);Assert.AreEqual(0,opened());
                    hold.OnPointerUp(a);second.OnPointerUp(b);var fresh=Touch(screen,3);hold.OnPointerDown(fresh);AdvanceHold(hold);Assert.AreEqual(1,opened());hold.OnPointerUp(fresh);
                }finally{Object.DestroyImmediate(other);}
            });
        }
        private static UnityEngine.InputSystem.UI.ExtendedPointerEventData Touch(UnityEngine.InputSystem.Touchscreen screen,int id)=>
            new UnityEngine.InputSystem.UI.ExtendedPointerEventData(null){device=screen,touchId=id,pointerId=id,eligibleForClick=true};
        private static void AdvanceHold(CustomDetailHold hold)=>typeof(CustomDetailHold).GetMethod("Advance",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hold,new object[]{double.MaxValue});
        private static void WithTouchHold(System.Action<CustomDetailHold,UnityEngine.InputSystem.Touchscreen,HashSet<CustomDetailHold>,System.Func<int>,System.Action<bool>> action)
        {
            var screen=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Touchscreen>();
            var root=new GameObject("CustomTouchDetails",typeof(RectTransform),typeof(Image),typeof(Button));
            try{int count=0;bool valid=true;var group=new HashSet<CustomDetailHold>();var hold=root.AddComponent<CustomDetailHold>();
                hold.Configure(root.GetComponent<Button>(),()=>valid,()=>count++,group);action(hold,screen,group,()=>count,v=>valid=v);
            }finally{Object.DestroyImmediate(root);UnityEngine.InputSystem.InputSystem.RemoveDevice(screen);}
        }

        [Test]
        public void 共通UI素材は指定フォントとウェイトサイズ自動縮小とマテリアルを置き換えない()
        {
            var root=new GameObject("AssignedTypeface",typeof(RectTransform),typeof(Text));
            try{var text=root.GetComponent<Text>();var font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");Assert.IsNotNull(font);
                text.font=font;text.fontSize=31;text.fontStyle=FontStyle.Bold;text.resizeTextForBestFit=true;text.resizeTextMinSize=16;text.resizeTextMaxSize=35;var material=text.material;
                UiLook.Label(text);Assert.AreSame(font,text.font);Assert.AreEqual(FontStyle.Bold,text.fontStyle);Assert.AreEqual(31,text.fontSize);Assert.IsTrue(text.resizeTextForBestFit);Assert.AreEqual(16,text.resizeTextMinSize);Assert.AreEqual(35,text.resizeTextMaxSize);Assert.AreSame(material,text.material);
                text.font=null;UiLook.Label(text);Assert.AreSame(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),text.font);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 手札コストの元フォント参照を共通UIの都合で別書体へ差し替えない()
        {
            var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandCardViewPrefab.prefab");
            var cost=prefab.GetComponentInChildren<Text>(true);Assert.IsNotNull(cost);Assert.AreEqual("Assets/Font/JikiTourai.ttf",UnityEditor.AssetDatabase.GetAssetPath(cost.font));
        }

        [Test]
        public void 共通ボタン素材は専用カードアイコンを上書きしない()
        {
            var root=new GameObject("DedicatedCardButton",typeof(RectTransform),typeof(Image),typeof(Button));
            var texture=new Texture2D(2,2);var icon=Sprite.Create(texture,new Rect(0,0,2,2),Vector2.one*.5f);icon.name="dedicated-card-mini";
            try {var image=root.GetComponent<Image>();image.sprite=icon;var button=root.GetComponent<Button>();button.targetGraphic=image;
                Olca.Presentation.Dungeon.UiLook.Button(button);Assert.AreSame(icon,image.sprite);Assert.AreNotEqual(Selectable.Transition.SpriteSwap,button.transition);
            } finally {Object.DestroyImmediate(root);Object.DestroyImmediate(icon);Object.DestroyImmediate(texture);}
        }
        [Test]
        public void 表示生成前の決定は先頭手札を選び再選択と後続カーソルを演出完了で変えない()
        {
            var root=new GameObject("EarlyCustomInput",typeof(RectTransform));
            try {
                var model=new CustomScreenSystem(new Deck(new List<Card>{Create("a",CardSeries.Sword),Create("b",CardSeries.Sword),Create("c",CardSeries.Sword)}));
                model.Open(new Card[0],new MockRandomProvider(0));var order=new List<Card>(model.CurrentHand);
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,model);
                typeof(CustomScreenView).GetField("costDisplay",flags).SetValue(view,root.AddComponent<Text>());
                var cursor=new CustomScreenCursor();typeof(CustomScreenView).GetField("cursor",flags).SetValue(view,cursor);
                view.ConfigureLogicalConfirmation((time,action)=>action(),()=>0);
                typeof(CustomScreenView).GetMethod("InitializeInputHand",flags).Invoke(view,new object[]{order});
                typeof(CustomScreenView).GetField("handAnimating",flags).SetValue(view,true);
                var decide=new Olca.Core.Logic.Integration.LogicalInput{MenuDecideButtonDown=true};
                view.HandleLogicalConfirmation(decide);Assert.IsTrue(model.IsOpen);Assert.IsFalse(model.IsConfirming);Assert.AreSame(order[0],model.SelectedCards[0]);
                view.HandleLogicalConfirmation(decide);Assert.IsEmpty(model.SelectedCards,"同じ位置の再決定は同じカードを取り消す");
                cursor.MoveToCard(1);view.HandleLogicalConfirmation(decide);Assert.AreSame(order[1],model.SelectedCards[0]);
                typeof(CustomScreenView).GetMethod("ShowCursorAfterAnimation",flags).Invoke(view,null);Assert.AreEqual(1,cursor.CurrentIndex);
                view.HandleLogicalConfirmation(new Olca.Core.Logic.Integration.LogicalInput{MenuConfirmButtonDown=true});Assert.IsTrue(model.IsConfirming);Assert.AreSame(order[1],model.AvailableCards[0]);
            } finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void 手札演出中の通過ホバーはキー選択位置を確定へ移さず空手札は明示確定できる()
        {
            var root=new GameObject("CustomHover",typeof(RectTransform));
            try {
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var cursor=new CustomScreenCursor();cursor.Initialize(1);typeof(CustomScreenView).GetField("cursor",flags).SetValue(view,cursor);
                var cardObject=new GameObject("MovingCard");cardObject.transform.SetParent(root.transform);var moving=cardObject.AddComponent<HandCardView>();
                ((List<HandCardView>)typeof(CustomScreenView).GetField("handCardViews",flags).GetValue(view)).Add(moving);
                typeof(CustomScreenView).GetField("handAnimating",flags).SetValue(view,true);view.OnCardPointerExited(moving);Assert.AreEqual(0,cursor.CurrentIndex);
                var empty=new CustomScreenSystem(new Deck(new List<Card>()));empty.Open(new Card[0],new MockRandomProvider(0));
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,empty);
                typeof(CustomScreenView).GetMethod("InitializeInputHand",flags).Invoke(view,new object[]{new List<Card>()});Assert.IsTrue(cursor.IsOnConfirmButton);
                view.ConfigureLogicalConfirmation((time,action)=>action(),()=>0);view.HandleLogicalConfirmation(new Olca.Core.Logic.Integration.LogicalInput{MenuDecideButtonDown=true});Assert.IsTrue(empty.IsConfirming);
            } finally { Object.DestroyImmediate(root); }
        }

        [TestCase(0, false)]
        [TestCase(2, true)]
        public void キー移動後の静止ポインタークリックは同じ個体へカーソルと次の決定を揃える(int target, bool selected)
        {
            var root = new GameObject("PointerKeyboardSelection", typeof(RectTransform));
            try {
                // 同じIDでも異なるカード個体。選択によるCurrentHandの詰め直しに依存しない。
                var cards = new List<Card> { Create("same", CardSeries.Sword), Create("same", CardSeries.Sword), Create("same", CardSeries.Sword) };
                var model = new CustomScreenSystem(new Deck(cards));
                model.Open(new Card[0], new MockRandomProvider(0));
                var order = new List<Card>(model.CurrentHand);
                var view = root.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                typeof(CustomScreenView).GetField("costDisplay", flags).SetValue(view, root.AddComponent<Text>());
                var cursor = new CustomScreenCursor();
                typeof(CustomScreenView).GetField("cursor", flags).SetValue(view, cursor);
                typeof(CustomScreenView).GetMethod("InitializeInputHand", flags).Invoke(view, new object[] { order });
                view.ConfigureLogicalConfirmation((time, action) => action(), () => 0);
                if (selected) view.OnCardClicked(order[target]);
                cursor.MoveToCard((target + 1) % order.Count);

                // 再入場のPointerEnterがないクリックでも操作位置を更新する。
                view.OnCardClicked(order[target]);
                Assert.AreEqual(target, cursor.CurrentIndex);
                Assert.AreEqual(!selected, model.SelectedCards.Contains(order[target]));
                view.HandleLogicalConfirmation(new Olca.Core.Logic.Integration.LogicalInput { MenuDecideButtonDown = true });
                Assert.AreEqual(selected, model.SelectedCards.Contains(order[target]));
                foreach (var card in order)
                    if (!ReferenceEquals(card, order[target])) Assert.IsFalse(model.SelectedCards.Contains(card));
            } finally { Object.DestroyImmediate(root); }
        }

        [TestCase(5, true, 5, 2)]
        [TestCase(8, false, 6, 2)]
        [TestCase(9, false, 9, 3)]
        public void カードクリック_選択可否と表示合計と確定内容が一致する(int limit, bool consecutive, int total, int count)
        {
            var cards = new List<Card> {
                Create("a", CardSeries.Sword),
                Create("b", consecutive ? CardSeries.Sword : CardSeries.Bomb),
                Create("c", CardSeries.Sword)
            };
            var model = new CustomScreenSystem(new Deck(cards), maxCost: limit);
            model.Open(new Card[0], new MockRandomProvider(0));
            var go = new GameObject("CostDisplayTest", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<Text>();
                var view = go.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("costDisplay", flags).SetValue(view, text);
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                view.OnCardClicked(cards[0]);
                view.OnCardClicked(cards[1]);
                if (!consecutive) view.OnCardClicked(cards[2]);
                Assert.AreEqual(total, model.GetTotalUsedCost());
                Assert.AreEqual(limit - total, model.RemainingCost);
                Assert.AreEqual($"コスト: {total} / {limit}", text.text);
                Assert.AreEqual(Color.white, text.color);
                Assert.AreEqual(count, model.SelectedCards.Count);
                var expected = new List<Card>(model.SelectedCards);
                model.Confirm();
                model.Close();
                CollectionAssert.AreEqual(expected, model.AvailableCards);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void 中間解除は表示不変で末尾解除と再選択だけ反映する()
        {
            var cards = new List<Card> { Create("a", CardSeries.Sword), Create("b", CardSeries.Bomb), Create("c", CardSeries.Sword) };
            var model = new CustomScreenSystem(new Deck(cards));
            model.Open(new Card[0], new MockRandomProvider(0));
            var go = new GameObject("CostDisplayTest", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<Text>();
                var view = go.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("costDisplay", flags).SetValue(view, text);
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                foreach (var card in cards) view.OnCardClicked(card);
                Assert.AreEqual("コスト: 9 / 10", text.text);
                view.OnCardClicked(cards[1]);
                Assert.AreEqual("コスト: 9 / 10", text.text);
                CollectionAssert.AreEqual(cards, model.SelectedCards);
                view.OnCardClicked(cards[2]);
                Assert.AreEqual("コスト: 6 / 10", text.text);
                view.OnCardClicked(cards[2]);
                Assert.AreEqual("コスト: 9 / 10", text.text);
                CollectionAssert.AreEqual(cards, model.SelectedCards);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void 中間解除拒否_上限内表示と確定ボタンを維持する()
        {
            var a = Create("a", CardSeries.Sword);
            var b = new Card("b", "B", 0, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Bomb, 1, "テスト");
            var c = Create("c", CardSeries.Bomb);
            var model = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: 5);
            model.Open(new Card[0], new MockRandomProvider(0));
            var go = new GameObject("CostDisplayTest", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<Text>();
                var button = go.AddComponent<Button>();
                var view = go.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("costDisplay", flags).SetValue(view, text);
                typeof(CustomScreenView).GetField("confirmButton", flags).SetValue(view, button);
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                foreach (var card in new[] { a, b, c }) view.OnCardClicked(card);
                Assert.IsTrue(button.interactable);
                view.OnCardClicked(b);
                Assert.AreEqual("コスト: 5 / 5", text.text);
                Assert.AreEqual(Color.white, text.color);
                Assert.IsTrue(button.interactable);
                view.OnCardClicked(c);
                Assert.AreEqual("コスト: 3 / 5", text.text);
                Assert.IsTrue(button.interactable);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static Card Create(string id, CardSeries series) =>
            new Card(id, id, 3, CardRarity.Normal, CardCategory.Attack, ElementType.None, series, 1, "テスト");

        [TestCase(16f / 9f)]
        [TestCase(4f / 3f)]
        [TestCase(691f / 327f)]
        [TestCase(9f / 16f)]
        public void 共通カメラは画面比率にかかわらず横七マスでスケールを変えない(float aspect)
        {
            var go = new GameObject("SevenCellCamera");
            var viewObject = new GameObject("FieldView");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.orthographic = true;
                camera.aspect = aspect;
                camera.transform.localScale = new Vector3(1f, 1f, 1f);
                var view = viewObject.AddComponent<GameFieldView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GameFieldView).GetField("fieldCamera", flags).SetValue(view, camera);
                var refresh = typeof(GameFieldView).GetMethod("UpdateCameraFraming", flags);
                refresh.Invoke(view, null);
                Assert.AreEqual(7f, camera.orthographicSize * 2f * camera.aspect, 0.0001f);
                Assert.AreEqual(0.25f, camera.WorldToViewportPoint(new Vector3(0f, -0.25f, 0f)).y, 0.0001f);
                Assert.AreEqual(Vector3.one, camera.transform.localScale);
                camera.aspect = 2f;
                refresh.Invoke(view, null);
                Assert.AreEqual(1.75f, camera.orthographicSize, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
                Object.DestroyImmediate(go);
            }
        }

        [TestCase("敗北して帰還しました","ハイボクシテキカンシマシタ")]
        [TestCase("総経験値 0 / 永続所持カード / 装備 / トークン交換所 / カード図鑑","ソウケイケンチ 0 / エイゾクショジカード / ソウビ / トークンコウカンジョ / カードズカン")]
        [TestCase("基本 / 無属性 / 再出撃 / 前へ / 次へ","キホン / ムゾクセイ / サイシュツゲキ / マエヘ / ツギヘ")]
        public void 帰還画面の既存用語をフォント変更せずカナで表示する(string source,string expected)
            =>Assert.AreEqual(expected,CustomDisplay.Katakana(source));

        [TestCase(0L,"ゲキハタイム: 00:00.00")]
        [TestCase(1L,"ゲキハタイム: 00:00.01")]
        [TestCase(59L,"ゲキハタイム: 00:00.98")]
        [TestCase(60L,"ゲキハタイム: 00:01.00")]
        [TestCase(3599L,"ゲキハタイム: 00:59.98")]
        [TestCase(3600L,"ゲキハタイム: 01:00.00")]
        public void 報酬の撃破時間は整数論理フレームから表示する(long frames,string expected)
            =>Assert.AreEqual(expected,CardSelectionView.FormatBattleTime(frames));
        [Test]
        public void 未計測の撃破時間は捏造せず負の時刻を拒否する()
        {
            Assert.AreEqual("",CardSelectionView.FormatBattleTime(null));
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>CardSelectionView.FormatBattleTime(-1));
            StringAssert.StartsWith("ゲキハタイム: ",CardSelectionView.FormatBattleTime(long.MaxValue));
        }

        [TestCase(false)] [TestCase(true)]
        public async System.Threading.Tasks.Task 報酬の受取と明示スキップは最初の入力だけで確定し再表示でもボタンを増やさない(bool skipFirst)
        {
            var root = new GameObject("RewardSkip", typeof(RectTransform));
            var headingObject=new GameObject("TitleText",typeof(RectTransform),typeof(Text));headingObject.transform.SetParent(root.transform,false);
            var heading=headingObject.GetComponent<Text>();heading.text="カードを1枚獲得";
            heading.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var originalFont=heading.font;
            try {
                var view = root.AddComponent<CardSelectionView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CardSelectionView).GetField("containerRoot", flags).SetValue(view, root);
                var countObject=new GameObject("DeckCount",typeof(RectTransform),typeof(Text));countObject.transform.SetParent(root.transform,false);
                var countText=countObject.GetComponent<Text>();typeof(CardSelectionView).GetField("deckCountText",flags).SetValue(view,countText);
                typeof(CardSelectionView).GetField("cardItems", flags).SetValue(view, new CardSelectionItemView[0]);
                var card = Create("reward", CardSeries.Sword);
                for (int repeat=0; repeat<2; repeat++) {
                    var task = view.ShowAsync(new[] { card }, 6, battleLogicalFrames:repeat==0?(long?)61:null).AsTask();
                    var time=root.transform.Find("BattleTimeText").GetComponent<Text>();
                    Assert.AreEqual(repeat==0,time.gameObject.activeSelf);
                    Assert.AreEqual(repeat==0?"ゲキハタイム: 00:01.01":"",time.text);
                    Assert.AreSame(countText.font,time.font);Assert.IsFalse(time.raycastTarget);
                    Assert.AreEqual(1,root.GetComponentsInChildren<Text>(true).Count(t=>t.name=="BattleTimeText"));
                    var button = root.GetComponentInChildren<Button>(true);
                    Assert.AreEqual("SkipCardReward", button.name);
                    Assert.AreEqual("カードヲ1マイカクトク",heading.text);Assert.AreSame(originalFont,heading.font);Assert.AreEqual(Color.white,countText.color);
                    Assert.AreEqual("ウケトラズニススム",button.GetComponentInChildren<Text>(true).text);
                    Assert.AreEqual(1, root.GetComponentsInChildren<Button>(true).Length);
                    // ポーズ中は明示スキップでも報酬を確定しない。
                    Olca.Unity.GameplayPause.SetPaused(true); button.onClick.Invoke(); Assert.IsFalse(task.IsCompleted);
                    Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                    Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame();
                    if (skipFirst) button.onClick.Invoke();
                    typeof(CardSelectionView).GetMethod("OnItemClicked", flags).Invoke(view, new object[] { card });
                    button.onClick.Invoke();
                    Assert.AreSame(skipFirst ? null : card, await task); Assert.IsFalse(root.activeSelf);
                }
            } finally {
                Object.DestroyImmediate(root); Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }

        [TestCase(691, 327, 3)]
        [TestCase(1280, 720, 3)]
        [TestCase(640, 480, 1)]
        public void 報酬カードは画面内へ等比配置される(int width, int height, int count)
        {
            var root = new GameObject("RewardLayout", typeof(RectTransform));
            try
            {
                var parent = (RectTransform)root.transform;
                parent.sizeDelta = new Vector2(width, height);
                var view = root.AddComponent<CardSelectionView>();
                var items = new CardSelectionItemView[count];
                for (int i = 0; i < count; i++)
                {
                    var child = new GameObject("Card", typeof(RectTransform));
                    child.transform.SetParent(root.transform, false);
                    ((RectTransform)child.transform).sizeDelta = new Vector2(280, 380);
                    items[i] = child.AddComponent<CardSelectionItemView>();
                }
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CardSelectionView).GetField("cardItems", flags).SetValue(view, items);
                typeof(CardSelectionView).GetMethod("UpdateChoiceLayout", flags).Invoke(view, null);
                foreach (var item in items)
                {
                    var rect = (RectTransform)item.transform;
                    Assert.AreEqual(rect.localScale.x, rect.localScale.y);
                    Assert.LessOrEqual(Mathf.Abs(rect.anchoredPosition.x) + rect.rect.width * rect.localScale.x / 2f, width / 2f - 16f + 0.01f);
                    Assert.LessOrEqual(rect.rect.height * rect.localScale.y, height - 64f + 0.01f);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void 加工Undoボタンは二重入力と確定競合で余分に戻さない(bool confirmFirst)
        {
            var a = Create("a", CardSeries.Sword);
            var t1 = new Card("t1", "調律", 1, CardRarity.Normal, CardCategory.Tuning, ElementType.None, CardSeries.None, 0, "", modifier: new CardModifier { BonusDamage = 1 });
            t1 = t1.WithTuningDefinition(new TuningGrantDefinition("undo", new[] { new TuningEffectDefinition("bonus", 1,
                new Dictionary<int, CardModifier> { { 1, new CardModifier { BonusDamage = 1 } } }) }));
            var t2 = t1.Clone();
            var model = new CustomScreenSystem(new Deck(new List<Card> { a, t1, t2 }), tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier));
            model.Open(new Card[0], new MockRandomProvider(0));
            var first = model.ExecuteTuning(a, t1).ResultCard;
            model.ExecuteTuning(first, t2);
            var root = new GameObject("UndoButtonTest", typeof(RectTransform));
            try
            {
                var confirmObject = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button));
                confirmObject.transform.SetParent(root.transform, false);
                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelObject.transform.SetParent(confirmObject.transform, false);
                var view = root.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("confirmButton", flags).SetValue(view, confirmObject.GetComponent<Button>());
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                typeof(CustomScreenView).GetMethod("EnsureUndoButton", flags).Invoke(view, null);
                var button = (Button)typeof(CustomScreenView).GetField("undoCustomizationButton", flags).GetValue(view);
                Assert.AreEqual("加工を戻す", button.GetComponentInChildren<Text>().text);
                Assert.AreEqual(Navigation.Mode.None, button.navigation.mode);
                if (confirmFirst) model.Confirm();
                typeof(CustomScreenView).GetMethod("UpdateUndoButton", flags).Invoke(view, null);
                button.onClick.Invoke();
                button.onClick.Invoke();
                Assert.AreEqual(confirmFirst ? 2 : 1, model.CustomizationUndoCount);
                Assert.IsFalse(button.interactable);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("empty")]
        [TestCase("closed")]
        [TestCase("confirming")]
        public void 加工Undoボタンは操作できない状態で無効(string state)
        {
            var a = Create("a", CardSeries.Sword);
            var t = new Card("t", "調律", 1, CardRarity.Normal, CardCategory.Tuning, ElementType.None, CardSeries.None, 0, "");
            var model = new CustomScreenSystem(new Deck(new List<Card> { a, t }), tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier));
            model.Open(new Card[0], new MockRandomProvider(0));
            if (state != "empty") model.ExecuteTuning(a, t);
            if (state == "closed") { model.Close(); model.Open(new Card[0], new MockRandomProvider(0)); }
            if (state == "confirming") model.Confirm();
            var go = new GameObject("UndoState", typeof(RectTransform), typeof(Button));
            try
            {
                var view = go.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                typeof(CustomScreenView).GetField("undoCustomizationButton", flags).SetValue(view, go.GetComponent<Button>());
                typeof(CustomScreenView).GetMethod("UpdateUndoButton", flags).Invoke(view, null);
                Assert.IsFalse(go.GetComponent<Button>().interactable);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void 加工Undoボタンは閉鎖と確定中に非表示となり再開時に戻る()
        {
            var model = new CustomScreenSystem(new Deck(new List<Card>()));
            var root = new GameObject("UndoVisibility", typeof(RectTransform));
            var buttonObject = new GameObject("UndoButton", typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(root.transform);
            try
            {
                var view = root.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                typeof(CustomScreenView).GetField("undoCustomizationButton", flags).SetValue(view, buttonObject.GetComponent<Button>());
                var refresh = typeof(CustomScreenView).GetMethod("UpdateUndoButton", flags);
                refresh.Invoke(view, null);
                Assert.IsFalse(buttonObject.activeSelf);
                model.Open(new Card[0], new MockRandomProvider(0));
                refresh.Invoke(view, null);
                Assert.IsTrue(buttonObject.activeSelf);
                Assert.IsFalse(buttonObject.GetComponent<Button>().interactable);
                model.Confirm();
                refresh.Invoke(view, null);
                Assert.IsFalse(buttonObject.activeSelf);
                model.Close();
                refresh.Invoke(view, null);
                Assert.IsFalse(buttonObject.activeSelf);
                model.Open(new Card[0], new MockRandomProvider(0));
                refresh.Invoke(view, null);
                Assert.IsTrue(buttonObject.activeSelf);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void 共通床は二対一の切り出しを等比描画し戦闘前後で接地面を保つ()
        {
            var go = new GameObject("SharedFloor");
            var texture = new Texture2D(64, 64);
            var source = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f);
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var grid = go.AddComponent<WorldBattleGridView>();
                typeof(WorldBattleGridView).GetField("panelSprite", flags).SetValue(grid, source);
                typeof(WorldBattleGridView).GetField("panelScale", flags).SetValue(grid, 1f);
                typeof(WorldBattleGridView).GetMethod("Awake", flags).Invoke(grid, null);
                var floor = go.GetComponentsInChildren<SpriteRenderer>();
                foreach (var renderer in floor)
                {
                    Assert.AreEqual(64f, renderer.sprite.rect.width);
                    Assert.AreEqual(32f, renderer.sprite.rect.height);
                    Assert.AreEqual(renderer.transform.localScale.x, renderer.transform.localScale.y);
                    Assert.AreEqual(1.6f, renderer.bounds.size.x, 0.0001f);
                    Assert.AreEqual(0.8f, renderer.bounds.size.y, 0.0001f);
                    if (renderer.enabled) Assert.AreEqual(-0.3f, renderer.bounds.max.y, 0.0001f);
                }
                grid.Show(new BattleField(), 0f);
                var occupied = new System.Collections.Generic.HashSet<int>();
                foreach (var renderer in floor)
                {
                    if (!renderer.enabled) continue;
                    Assert.AreEqual(-0.3f, renderer.bounds.max.y, 0.0001f);
                    Assert.IsTrue(occupied.Add(Mathf.RoundToInt(renderer.transform.position.x / grid.CellSize)), "床の二重表示");
                }
                grid.Hide();
                Assert.IsTrue(go.activeSelf);
                Assert.IsTrue(System.Array.Exists(floor, renderer => renderer.enabled));
                Assert.AreEqual(3 * 1.6f, grid.SnapWorldXToCellCenter(5.5f), 0.0001f);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(source); Object.DestroyImmediate(texture); }
        }

        [TestCase(1f)]
        [TestCase(1.6f)]
        [TestCase(1.92f)]
        [TestCase(2.56f)]
        public void 表示単位を変えても論理マス距離と共通カメラの七マス幅を保つ(float units)
        {
            var gridObject = new GameObject("GridUnits");
            var viewObject = new GameObject("FieldUnits");
            var cameraObject = new GameObject("CameraUnits");
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var grid = gridObject.AddComponent<WorldBattleGridView>();
                typeof(WorldBattleGridView).GetField("panelSize", flags).SetValue(grid, units);
                typeof(WorldBattleGridView).GetField("originX", flags).SetValue(grid, 12.5f);
                Assert.AreEqual(units, grid.GridIndexToWorldX(4) - grid.GridIndexToWorldX(3), 0.0001f);
                Assert.AreEqual(3.75f, grid.WorldToLogical(grid.LogicalToWorld(3.75f)), 0.0001f);
                Assert.AreEqual(12.5f + 2.5f * units, grid.GridCenterWorldX, 0.0001f);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true; camera.aspect = 16f / 9f;
                var view = viewObject.AddComponent<GameFieldView>();
                typeof(GameFieldView).GetField("fieldCamera", flags).SetValue(view, camera);
                typeof(GameFieldView).GetField("battleGridView", flags).SetValue(view, grid);
                typeof(GameFieldView).GetMethod("UpdateCameraFraming", flags).Invoke(view, null);
                Assert.AreEqual(7f * units, camera.orthographicSize * 2f * camera.aspect, 0.0001f);
                Assert.AreEqual(7.5f * units, view.LogicalToWorld(7.5f), 0.0001f);
                Assert.AreEqual(Vector3.one, view.transform.localScale);
            }
            finally { Object.DestroyImmediate(gridObject); Object.DestroyImmediate(viewObject); Object.DestroyImmediate(cameraObject); }
        }
        [Test]
        public void 足元表示差分は親座標を変えず再適用でも累積しない()
        {
            var go = new GameObject("FootlineView");
            var root = new GameObject("LogicalPlayer");
            var spriteObject = new GameObject("PlayerSprite");
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var grid = go.AddComponent<WorldBattleGridView>();
                var view = go.AddComponent<GameFieldView>();
                root.transform.position = new Vector3(12f, 3f, 0f);
                spriteObject.transform.SetParent(root.transform, false);
                spriteObject.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                var sprite = spriteObject.AddComponent<SpriteRenderer>();
                typeof(GameFieldView).GetField("playerTransform", flags).SetValue(view, root.transform);
                typeof(GameFieldView).GetField("playerSprite", flags).SetValue(view, sprite);
                typeof(GameFieldView).GetField("battleGridView", flags).SetValue(view, grid);
                var apply = typeof(GameFieldView).GetMethod("ApplyCharacterDisplayOffset", flags);
                apply.Invoke(view, null);
                apply.Invoke(view, null);
                Assert.AreEqual(-0.36f, view.CharacterDisplayOffsetY, 0.0001f);
                Assert.AreEqual(-0.66f, sprite.transform.localPosition.y, 0.0001f);
                Assert.AreEqual(new Vector3(12f, 3f, 0f), root.transform.position);
                Assert.AreEqual(Vector3.one, sprite.transform.localScale);
                Assert.AreEqual(1.6f, view.LogicalToWorld(1f), 0.0001f);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(root); }
        }
        [Test]
        public void 前景は床より下を覆い等比タイルで控えめに速く流れる()
        {
            var go = new GameObject("ForegroundTest");
            var cameraObject = new GameObject("ForegroundCamera");
            var texture = new Texture2D(64, 16);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 64, 16), new Vector2(0.5f, 0.5f), 64f);
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true; camera.aspect = 16f / 9f; camera.orthographicSize = 3.15f;
                camera.transform.position = new Vector3(0f, 1.275f, -10f);
                var grid = go.AddComponent<WorldBattleGridView>();
                var layer = go.AddComponent<ScrollingBackgroundView>();
                typeof(ScrollingBackgroundView).GetField("targetCamera", flags).SetValue(layer, camera);
                typeof(ScrollingBackgroundView).GetField("foregroundGrid", flags).SetValue(layer, grid);
                var refresh = typeof(ScrollingBackgroundView).GetMethod("UpdateForeground", flags);
                refresh.Invoke(layer, null);
                Assert.IsNull(typeof(ScrollingBackgroundView).GetField("foregroundRoot", flags).GetValue(layer), "素材なしで仮前景を出さない");
                typeof(ScrollingBackgroundView).GetField("foregroundSprite", flags).SetValue(layer, sprite);
                refresh.Invoke(layer, null);
                var root = (Transform)typeof(ScrollingBackgroundView).GetField("foregroundRoot", flags).GetValue(layer);
                var fill = root.Find("ForegroundOpaqueBase").GetComponent<SpriteRenderer>();
                Assert.AreEqual(-1.1f, fill.bounds.max.y, 0.0001f);
                Assert.AreEqual(-1.875f, fill.bounds.min.y, 0.0001f);
                Assert.GreaterOrEqual(fill.bounds.size.x, 11.2f);
                var rock = root.Find("ForegroundRock_0").GetComponent<SpriteRenderer>();
                Assert.AreEqual(rock.transform.localScale.x, rock.transform.localScale.y);
                Assert.AreEqual(-1.1f, rock.bounds.max.y, 0.0001f);
                typeof(ScrollingBackgroundView).GetField("foregroundOpaqueTop", flags).SetValue(layer, 0.25f);
                refresh.Invoke(layer, null);
                Assert.AreEqual(-1.1f, rock.bounds.min.y + rock.bounds.size.y * 0.25f, 0.0001f, "透過余白ではなく不透明部分の最高点を床下端に合わせる");
                float before = rock.transform.position.x;
                camera.transform.position += new Vector3(0.1f, 0f, 0f);
                refresh.Invoke(layer, null);
                Assert.AreEqual(-0.012f, rock.transform.position.x - before, 0.0001f);
                camera.transform.position -= new Vector3(0.1f, 0f, 0f);
                refresh.Invoke(layer, null);
                Assert.AreEqual(before, rock.transform.position.x, 0.0001f);
                // EditModeでは通常MonoBehaviourのライフサイクルが自動実行されない。
                typeof(ScrollingBackgroundView).GetMethod("OnDisable", flags).Invoke(layer, null);
                Assert.IsFalse(root.gameObject.activeSelf);
            }
            finally
            {
                var layer = go.GetComponent<ScrollingBackgroundView>();
                if (layer != null) typeof(ScrollingBackgroundView).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(layer, null);
                Object.DestroyImmediate(go); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }
    }
}
