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
        [TestCase(false,false)][TestCase(false,true)][TestCase(true,false)][TestCase(true,true)]
        public void 次の発言と別会話は同じ本文でも先頭から表示し慣性を持ち越さない(bool rebind,bool sameText)
        {
            var root=new GameObject("DialogueScrollReset",typeof(RectTransform),typeof(Canvas));DialogueView view=null;
            try{
                root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;var panel=new GameObject("DialoguePanel",typeof(RectTransform));panel.transform.SetParent(root.transform,false);view=panel.AddComponent<DialogueView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),()=>{});
                string first=string.Concat(Enumerable.Repeat("選択したカードを確定します。",300)),second=sameText?first:string.Concat(Enumerable.Repeat("次の説明を最初から読みます。",300));var session=new DialogueSession(new DialogueScript("scroll-reset",new[]{new DialogueLine(first,"A"),new DialogueLine(second,"B")}),DialogueTextSpeed.Instant);view.Bind(session);Canvas.ForceUpdateCanvases();var scroll=panel.GetComponentsInChildren<ScrollRect>(true).Single();Assert.Greater(scroll.content.rect.height,scroll.viewport.rect.height);scroll.verticalNormalizedPosition=.2f;scroll.velocity=new Vector2(0,100);Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(.2f).Within(.001f));
                if(rebind){session=new DialogueSession(new DialogueScript("other-dialogue",new[]{new DialogueLine(second,"C")}),DialogueTextSpeed.Instant);view.Bind(session);}else{panel.GetComponentsInChildren<Button>(true).Single(b=>b.name=="DialogueNext").onClick.Invoke();Assert.AreEqual(1,session.Index);view.Refresh();}
                Canvas.ForceUpdateCanvases();Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(1).Within(.001f));Assert.AreEqual(Vector2.zero,scroll.velocity);long version=session.Version;int index=session.Index;view.Refresh();Assert.AreEqual(version,session.Version);Assert.AreEqual(index,session.Index);
            }finally{if(view!=null)typeof(DialogueView).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);Object.DestroyImmediate(root);}
        }
        [Test] public void 同じ発言の表示更新とポーズ復帰は読んでいた位置を保つ()
        {
            var root=new GameObject("DialogueScrollPreserve",typeof(RectTransform),typeof(Canvas));DialogueView view=null;
            try{
                root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;var panel=new GameObject("DialoguePanel",typeof(RectTransform));panel.transform.SetParent(root.transform,false);view=panel.AddComponent<DialogueView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),()=>{});var session=new DialogueSession(new DialogueScript("scroll-preserve",new[]{new DialogueLine(string.Concat(Enumerable.Repeat("選択したカードを確定します。",300)),"A")}),DialogueTextSpeed.Instant);view.Bind(session);Canvas.ForceUpdateCanvases();var scroll=panel.GetComponentInChildren<ScrollRect>();scroll.verticalNormalizedPosition=.2f;session.ToggleFastForward();view.Refresh();Canvas.ForceUpdateCanvases();Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(.2f).Within(.001f));view.SetPresentationPaused(true);view.SetPresentationPaused(false);Canvas.ForceUpdateCanvases();Assert.That(scroll.verticalNormalizedPosition,Is.EqualTo(.2f).Within(.001f));
            }finally{if(view!=null)typeof(DialogueView).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);Object.DestroyImmediate(root);}
        }
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
