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
        private static void WithDialogue(System.Action<DialogueView,GameObject> action)
        {
            var root=new GameObject("DialogueReveal",typeof(RectTransform),typeof(Canvas));DialogueView view=null;
            try{root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;var panel=new GameObject("DialoguePanel",typeof(RectTransform));panel.transform.SetParent(root.transform,false);view=panel.AddComponent<DialogueView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),()=>{});action(view,panel);}
            finally{if(view!=null)typeof(DialogueView).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(view,null);Object.DestroyImmediate(root);}
        }
        [TestCase(DialogueTextSpeed.Slow,4)][TestCase(DialogueTextSpeed.Normal,2)][TestCase(DialogueTextSpeed.Fast,1)]
        public void 会話の選択の読みは全文で確定し原文の速度で出た文字を置換しない(DialogueTextSpeed speed,int interval)
        {
            WithDialogue((view,panel)=>{
                const string source="選択したカードを確定します。";var session=new DialogueSession(new DialogueScript("reveal",new[]{new DialogueLine(source)}),speed);view.Bind(session);var body=panel.GetComponentsInChildren<Text>().Single(x=>x.name=="DialogueText");var font=body.font;string previous="",full="センタクしたカードをカクテイします。";int count=System.Globalization.StringInfo.ParseCombiningCharacters(source).Length;
                for(int frame=0;frame<=count*interval;frame++){
                    if(frame>0)session.AdvanceFrame();view.Refresh();Assert.AreEqual(frame/interval,session.VisibleElements);Assert.IsTrue(body.text.StartsWith(previous),"表示済み本文を書き換えない");Assert.IsTrue(full.StartsWith(body.text));Assert.AreSame(font,body.font);
                    if(frame==interval)Assert.AreEqual("セン",body.text);if(frame==2*interval)Assert.AreEqual("センタク",body.text);previous=body.text;
                }
                Assert.AreEqual(full,body.text);Assert.AreEqual(source,session.VisibleText);Assert.IsTrue(session.TextComplete);Assert.AreEqual(0,session.Index);
            });
        }
        [TestCase("全ての選択を確定します。")][TestCase("一つ前のカードを選択します。")][TestCase("支払いを確認します。")][TestCase("あ\u3099😀選択したカード\nABC 123")]
        public void 会話の文脈読みと元のかな結合文字は安定した全文の接頭辞だけを表示する(string source)
        {
            WithDialogue((view,panel)=>{var session=new DialogueSession(new DialogueScript("context",new[]{new DialogueLine(source)}),DialogueTextSpeed.Fast);view.Bind(session);var body=panel.GetComponentsInChildren<Text>().Single(x=>x.name=="DialogueText");string full=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(source),previous="";
                for(int n=0;n<System.Globalization.StringInfo.ParseCombiningCharacters(source).Length;n++){session.AdvanceFrame();view.Refresh();Assert.IsTrue(body.text.StartsWith(previous));Assert.IsTrue(full.StartsWith(body.text));Assert.IsFalse(body.text.Length>0&&char.IsHighSurrogate(body.text[body.text.Length-1]));previous=body.text;}
                Assert.AreEqual(full,body.text);Assert.AreEqual(source,session.VisibleText);
            });
        }
        [Test] public void 会話の最初の決定は全文表示し次の決定で進みログとInstantは従来の全文を保つ()
        {
            WithDialogue((view,panel)=>{const string source="選択したカードを確定します。";var script=new DialogueScript("decide",new[]{new DialogueLine(source,"A"),new DialogueLine("全ての説明です。","B")});var session=new DialogueSession(script,DialogueTextSpeed.Slow);view.Bind(session);for(int n=0;n<4;n++)session.AdvanceFrame();view.Refresh();var body=panel.GetComponentsInChildren<Text>().Single(x=>x.name=="DialogueText");
                panel.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueNext").onClick.Invoke();view.Refresh();Assert.AreEqual(0,session.Index);Assert.AreEqual("センタクしたカードをカクテイします。",body.text);Assert.AreEqual("A\n"+source,session.Backlog.Single());
                panel.GetComponentsInChildren<Button>().Single(x=>x.name=="DialogueNext").onClick.Invoke();view.Refresh();Assert.AreEqual(1,session.Index);Assert.AreEqual("",body.text);
                session=new DialogueSession(script,DialogueTextSpeed.Instant);view.Bind(session);Assert.AreEqual("センタクしたカードをカクテイします。",body.text);Assert.AreEqual("A\n"+source,session.Backlog.Single());
            });
        }
        [Test] public void 会話の早送りと同文の再Bindも原文の文字数で進みポーズ復帰で本文を変えない()
        {
            WithDialogue((view,panel)=>{const string source="選択したカードを確定します。";var script=new DialogueScript("rebind",new[]{new DialogueLine(source)});var session=new DialogueSession(script,DialogueTextSpeed.Slow);view.Bind(session);session.ToggleFastForward();session.AdvanceFrame();view.Refresh();Assert.AreEqual(4,session.VisibleElements);var body=panel.GetComponentsInChildren<Text>().Single(x=>x.name=="DialogueText");Assert.AreEqual("センタクした",body.text);string before=body.text;view.SetPresentationPaused(true);view.SetPresentationPaused(false);Assert.AreEqual(before,body.text);
                session=new DialogueSession(script,DialogueTextSpeed.Slow);view.Bind(session);Assert.AreEqual("",body.text);for(int n=0;n<4;n++)session.AdvanceFrame();view.Refresh();Assert.AreEqual("セン",body.text);Assert.AreEqual(1,session.VisibleElements);
            });
        }
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
