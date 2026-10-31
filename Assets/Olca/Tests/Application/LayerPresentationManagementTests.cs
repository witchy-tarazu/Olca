using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Olca.Unity;
using Olca.Presentation.Management;
namespace Olca.Tests.Application
{
    public sealed class LayerPresentationManagementTests
    {
        [Test] public async Task Layers_常設HUDと設定画面を独立表示し所有者終了で両方を解放する()
        {
            using(var resources=new ResourceManager())using(var dialogs=new DialogManager(()=>1))using(var views=new ViewManager(resources,dialogs)){
                var owner=new object();var hud=views.GetLayer("gameplay");Assert.AreSame(hud,views.GetLayer("gameplay"));
                views.Register(owner,"settings",ct=>Task.FromResult(new GameObject("Settings")),(x,c)=>x.SetActive(true),x=>x.SetActive(false));
                hud.Register(owner,"hud",ct=>Task.FromResult(new GameObject("HUD")),(x,c)=>x.SetActive(true),x=>x.SetActive(false));
                Assert.IsTrue((await hud.ShowAsync("hud")).Succeeded);Assert.IsTrue((await views.ShowAsync("settings")).Succeeded);Assert.AreEqual("hud",hud.CurrentKey);Assert.AreEqual("settings",views.CurrentKey);Assert.AreEqual(2,resources.OwnedInstanceCount);
                views.CloseCurrent();Assert.IsNull(views.CurrentKey);Assert.AreEqual("hud",hud.CurrentKey);views.UnregisterOwner(owner);Assert.IsNull(hud.CurrentKey);Assert.AreEqual(0,hud.RegisteredCount);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }
        }
        [Test] public async Task Layers_別層の取消と後着生成がHUDの寿命へ漏れない()
        {
            using(var resources=new ResourceManager())using(var dialogs=new DialogManager(()=>1))using(var views=new ViewManager(resources,dialogs)){
                var owner=new object();var hud=views.GetLayer("gameplay");hud.Register(owner,"hud",ct=>Task.FromResult(new GameObject("HUD")),(x,c)=>x.SetActive(true),x=>x.SetActive(false));await hud.ShowAsync("hud");
                var load=new TaskCompletionSource<GameObject>();views.Register(owner,"slow",ct=>load.Task,(x,c)=>x.SetActive(true),x=>x.SetActive(false));var request=views.ShowAsync("slow");views.CloseCurrent();var late=new GameObject("Late");load.SetResult(late);Assert.IsTrue((await request).Cancelled);Assert.IsTrue(late==null);Assert.AreEqual("hud",hud.CurrentKey);Assert.AreEqual(1,resources.OwnedInstanceCount);views.UnregisterOwner(owner);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }
        }
        [Test] public async Task Layers_親Disposeが層だけのOwnerと後着生成も破棄する()
        {
            using(var resources=new ResourceManager())using(var dialogs=new DialogManager(()=>1))using(var views=new ViewManager(resources,dialogs)){
                var owner=new object();var hud=views.GetLayer("gameplay");var loading=views.GetLayer("loading");
                hud.Register(owner,"hud",ct=>Task.FromResult(new GameObject("HUD")),(x,c)=>x.SetActive(true),x=>x.SetActive(false));await hud.ShowAsync("hud");
                var load=new TaskCompletionSource<GameObject>();loading.Register(owner,"late",ct=>load.Task,(x,c)=>x.SetActive(true),x=>x.SetActive(false));var pending=loading.ShowAsync("late");
                views.Dispose();Assert.IsNull(hud.CurrentKey);Assert.AreEqual(0,hud.RegisteredCount);Assert.AreEqual(0,loading.RegisteredCount);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.Throws<System.ObjectDisposedException>(()=>views.GetLayer("new"));
                var late=new GameObject("LateAfterDispose");load.SetResult(late);Assert.IsTrue((await pending).Cancelled);Assert.IsTrue(late==null);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }
        }
    }
}
