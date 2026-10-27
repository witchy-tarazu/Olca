using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;
using Olca.Presentation.Field;

namespace Olca.Tests
{
    public class DialogueLayoutRefactorTests
    {
        [TestCase(false)] [TestCase(true)]
        public void 会話とバックログは漢字だけカナへ変換した最終本文でスクロール高を測る(bool backlog)
        {
            var root = new GameObject("DialogueLayoutTest", typeof(RectTransform), typeof(Canvas));
            DialogueView view = null;
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var panel = new GameObject("DialoguePanel", typeof(RectTransform));
                panel.transform.SetParent(root.transform, false);
                view = panel.AddComponent<DialogueView>();
                var font = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");
                view.Initialize(font, () => { });
                var source = string.Concat(Enumerable.Repeat("選択したカードを確定します。", 300));
                var session = new DialogueSession(new DialogueScript("fixture-layout", new[] { new DialogueLine(source, "テスト") }), DialogueTextSpeed.Instant);
                if (backlog) Assert.IsTrue(session.ToggleBacklog());
                view.Bind(session);
                Canvas.ForceUpdateCanvases();
                var body = panel.GetComponentsInChildren<Text>().Single(v => v.name == "DialogueText");
                Assert.AreSame(font, body.font);
                StringAssert.DoesNotContain("選択", body.text);
                StringAssert.Contains("したカードを", body.text);
                Assert.Greater(body.preferredHeight, 420);
                Assert.GreaterOrEqual(body.rectTransform.rect.height, body.preferredHeight + 19);
                long version = session.Version;
                var height = body.rectTransform.rect.height;
                view.Refresh();
                Canvas.ForceUpdateCanvases();
                Assert.AreEqual(version, session.Version);
                Assert.AreEqual(height, body.rectTransform.rect.height);
                Assert.GreaterOrEqual(body.rectTransform.rect.height, body.preferredHeight + 19);
            }
            finally
            {
                if (view != null) typeof(DialogueView).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(view, null);
                Object.DestroyImmediate(root);
            }
        }
    }
}
