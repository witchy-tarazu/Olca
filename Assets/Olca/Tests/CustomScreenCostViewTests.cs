using System.Collections.Generic;
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
            var t2 = t1.Clone();
            var model = new CustomScreenSystem(new Deck(new List<Card> { a, t1, t2 }), tuningSystem: new TuningSystem());
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
            var model = new CustomScreenSystem(new Deck(new List<Card> { a, t }), tuningSystem: new TuningSystem());
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
    }
}
