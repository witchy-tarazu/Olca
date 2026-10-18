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
        public void 共通カメラは画面比率にかかわらず横八マスでスケールを変えない(float aspect)
        {
            var go = new GameObject("EightCellCamera");
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
                Assert.AreEqual(8f, camera.orthographicSize * 2f * camera.aspect, 0.0001f);
                Assert.AreEqual(Vector3.one, camera.transform.localScale);
                camera.aspect = 2f;
                refresh.Invoke(view, null);
                Assert.AreEqual(2f, camera.orthographicSize, 0.0001f);
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
    }
}
