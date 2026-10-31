using System;
using System.Threading;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Olca.Unity;
namespace Olca.Tests.Application
{
    public sealed class UIRuntimeManagementTests
    {
        // EditModeではOnEnableが自動実行されない。既存管理テストと同じ登録/解除経路を明示する。
        private static void Lifecycle(EventSystem value,string method) { if(value!=null)typeof(EventSystem).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null); }
        [Test] public void Prefabs_既存の投影不要UI設定と入力参照を保持する()
        {
            var ui=Resources.Load<GameObject>("OlcaSystem/UIRoot");Assert.NotNull(ui);
            var canvas=ui.GetComponent<Canvas>();Assert.AreEqual(RenderMode.ScreenSpaceOverlay,canvas.renderMode);Assert.AreEqual(32000,canvas.sortingOrder);
            var scaler=ui.GetComponent<CanvasScaler>();Assert.AreEqual(new Vector2(1920,1080),scaler.referenceResolution);Assert.AreEqual(.5f,scaler.matchWidthOrHeight);
            Assert.NotNull(ui.GetComponent<GraphicRaycaster>());
            var events=Resources.Load<GameObject>("OlcaSystem/EventSystem");Assert.NotNull(events.GetComponent<EventSystem>());
            var module=events.GetComponent<InputSystemUIInputModule>();Assert.NotNull(module.actionsAsset);Assert.NotNull(module.point.action);Assert.NotNull(module.leftClick.action);Assert.NotNull(module.submit.action);Assert.NotNull(module.cancel.action);
            var screen=Resources.Load<GameObject>("OlcaSystem/KeyboardSettings").GetComponent<RectTransform>();Assert.AreEqual(Vector2.zero,screen.anchorMin);Assert.AreEqual(Vector2.one,screen.anchorMax);
        }
        [Test] public void Content_再取得と所有拒否と解放後再生成を分離する()
        {
            var root=new GameObject("UIRuntimeTest");var resources=new ResourceManager();var ui=new UIRuntimeManager(resources,root.transform);var owner=new object();EventSystem ownedEvents=null;
            try {
                Assert.IsNull(EventSystem.current,"前テストのEventSystem残留を見逃さない");
                var first=ui.AcquireContent(owner);var firstId=first.GetInstanceID();Assert.AreSame(first,ui.AcquireContent(owner));Assert.Throws<InvalidOperationException>(()=>ui.AcquireContent(new object()));
                Assert.AreEqual(2,resources.OwnedInstanceCount);ownedEvents=root.GetComponentInChildren<EventSystem>();Lifecycle(ownedEvents,"OnEnable");EventSystem.current=ownedEvents;var eventId=EventSystem.current.GetInstanceID();var canvasId=first.GetComponentInParent<Canvas>().GetInstanceID();
                var button=new GameObject("Selected",typeof(RectTransform),typeof(Button));button.transform.SetParent(first.transform,false);EventSystem.current.SetSelectedGameObject(button);
                ui.ReleaseContent(new object());Assert.AreSame(button,EventSystem.current.currentSelectedGameObject);
                ui.ReleaseContent(owner);Assert.IsNull(EventSystem.current.currentSelectedGameObject);Assert.IsTrue(first==null);
                var second=ui.AcquireContent(new object());Assert.AreNotEqual(firstId,second.GetInstanceID());Assert.AreEqual(eventId,EventSystem.current.GetInstanceID());Assert.AreEqual(canvasId,second.GetComponentInParent<Canvas>().GetInstanceID());
                Lifecycle(ownedEvents,"OnDisable");ui.Dispose();Assert.AreEqual(0,resources.OwnedInstanceCount);
            } finally {Lifecycle(ownedEvents,"OnDisable");ui.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void Acquire_外部EventSystemを二重所有せず失敗する()
        {
            var root=new GameObject("ExternalEvents",typeof(EventSystem));var resources=new ResourceManager();var ui=new UIRuntimeManager(resources,root.transform);
            try {Lifecycle(root.GetComponent<EventSystem>(),"OnEnable");EventSystem.current=root.GetComponent<EventSystem>();Assert.Throws<InvalidOperationException>(()=>ui.AcquireContent(new object()));Assert.AreEqual(0,resources.OwnedInstanceCount);ui.Dispose();Assert.IsTrue(root!=null);Assert.AreSame(root.GetComponent<EventSystem>(),EventSystem.current);}
            finally {Lifecycle(root.GetComponent<EventSystem>(),"OnDisable");ui.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        [Test] public void Resource_欠損と事前取消は生成物を残さない()
        {
            using(var resources=new ResourceManager()){
                Assert.Throws<InvalidOperationException>(()=>resources.InstantiatePrefab("OlcaSystem/Missing",null));
                var cancel=new CancellationTokenSource();cancel.Cancel();Assert.CatchAsync<OperationCanceledException>(async()=>await resources.InstantiatePrefabAsync("OlcaSystem/UIRoot",null,cancel.Token));Assert.AreEqual(0,resources.OwnedInstanceCount);cancel.Dispose();
            }
        }
    }
}
