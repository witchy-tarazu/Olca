using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using Olca.Unity;
using Olca.Application.Management;

namespace Olca.Tests.Application
{
    public class GameHostManagerTests
    {
        private sealed class Lease : IHostSceneLease
        {
            public Scene Scene=>default;
            public bool IsLoaded { get; set; }=true;
            public int Activations,Releases;
            public Action Activating;
            public Task Release=Task.CompletedTask;
            public Func<Task> ReleaseFactory;
            public void Activate(){Activations++;Activating?.Invoke();}
            public Task UnloadAsync(){Releases++;return ReleaseFactory?.Invoke()??Release;}
        }
        private sealed class Loader : IHostSceneLoader
        {
            public int Loads;
            public Func<Task<IHostSceneLease>> Factory;
            public Task<IHostSceneLease> LoadSceneAsync(string path,CancellationToken cancellation){Loads++;return Factory();}
        }
        private Loader loader;
        private GameHostManager hosts;
        private bool session;
        [SetUp] public void Setup(){session=false;loader=new Loader();hosts=new GameHostManager(loader,()=>session);}
        [TearDown] public void Cleanup(){hosts.Dispose();}
        private HostOpenRequest Request(Action<Scene> prepare=null,Action<Scene> start=null)=>new HostOpenRequest("host",prepare??(_=>{}),start??(_=>{}));
        [Test] public async Task 同時起動を一回のロードへまとめ注入から有効化起動の順を保つ()
        {
            var loading=new TaskCompletionSource<IHostSceneLease>();loader.Factory=()=>loading.Task;
            var order=new List<string>();var lease=new Lease{Activating=()=>order.Add("activate")};
            var request=Request(_=>order.Add("prepare"),_=>order.Add("start"));
            var first=hosts.OpenAsync(request);var second=hosts.OpenAsync(request);Assert.AreEqual(1,loader.Loads);
            loading.SetResult(lease);Assert.IsTrue((await first).Succeeded);Assert.IsTrue((await second).Succeeded);
            CollectionAssert.AreEqual(new[]{"prepare","activate","start"},order);
            Assert.IsTrue((await hosts.OpenAsync(request)).Succeeded);Assert.AreEqual(1,loader.Loads);
            await hosts.CloseAsync();Assert.AreEqual(1,lease.Releases);Assert.IsNull(hosts.CurrentPath);
        }
        [Test] public async Task 取消ロードの後着も回収解放してから終了を完了する()
        {
            var loading=new TaskCompletionSource<IHostSceneLease>();loader.Factory=()=>loading.Task;var lease=new Lease();int starts=0;
            using(var cts=new CancellationTokenSource()){
                var open=hosts.OpenAsync(Request(start:_=>starts++),cts.Token);cts.Cancel();Assert.IsTrue((await hosts.OpenAsync(Request())).Cancelled);Assert.AreEqual(1,loader.Loads);var close=hosts.CloseAsync();Assert.IsFalse(close.IsCompleted);
                loading.SetResult(lease);Assert.IsTrue((await open).Cancelled);await close;
                Assert.AreEqual(0,starts);Assert.AreEqual(0,lease.Activations);Assert.AreEqual(1,lease.Releases);
            }
        }
        [Test] public async Task 準備失敗は有効化せず解放して再試行できる()
        {
            var lease=new Lease();loader.Factory=()=>Task.FromResult<IHostSceneLease>(lease);
            var failed=await hosts.OpenAsync(Request(_=>throw new InvalidOperationException("fixture prepare")));
            Assert.IsFalse(failed.Succeeded);StringAssert.Contains("fixture",failed.Error);Assert.AreEqual(0,lease.Activations);Assert.AreEqual(1,lease.Releases);
            var next=new Lease();loader.Factory=()=>Task.FromResult<IHostSceneLease>(next);Assert.IsTrue((await hosts.OpenAsync(Request())).Succeeded);
        }
        [Test] public async Task 起動失敗も有効化したホストを解放して所有を残さない()
        {
            var lease=new Lease();loader.Factory=()=>Task.FromResult<IHostSceneLease>(lease);
            var result=await hosts.OpenAsync(Request(start:_=>throw new InvalidOperationException("fixture start")));
            Assert.IsFalse(result.Succeeded);Assert.AreEqual(1,lease.Activations);Assert.AreEqual(1,lease.Releases);Assert.IsNull(hosts.CurrentPath);
        }
        [Test] public async Task 既存直起動とロード中の外部起動を重複させない()
        {
            session=true;Assert.IsFalse((await hosts.OpenAsync(Request())).Succeeded);Assert.AreEqual(0,loader.Loads);
            session=false;var loading=new TaskCompletionSource<IHostSceneLease>();loader.Factory=()=>loading.Task;
            var open=hosts.OpenAsync(Request());session=true;var lease=new Lease();loading.SetResult(lease);
            Assert.IsFalse((await open).Succeeded);Assert.AreEqual(0,lease.Activations);Assert.AreEqual(1,lease.Releases);
        }
        [Test] public async Task 解放中の再起動を拒否し解放完了後は再生成できる()
        {
            var ending=new TaskCompletionSource<bool>();var lease=new Lease{Release=ending.Task};loader.Factory=()=>Task.FromResult<IHostSceneLease>(lease);
            await hosts.OpenAsync(Request());var close=hosts.CloseAsync();Assert.AreSame(close,hosts.CloseAsync());
            Assert.IsFalse((await hosts.OpenAsync(Request())).Succeeded);ending.SetResult(true);await close;
            loader.Factory=()=>Task.FromResult<IHostSceneLease>(new Lease());Assert.IsTrue((await hosts.OpenAsync(Request())).Succeeded);Assert.AreEqual(2,loader.Loads);
        }
        [Test] public async Task 管理破棄後の後着を起動せず解放する()
        {
            var loading=new TaskCompletionSource<IHostSceneLease>();loader.Factory=()=>loading.Task;var open=hosts.OpenAsync(Request());hosts.Dispose();
            var lease=new Lease();loading.SetResult(lease);Assert.IsTrue((await open).Cancelled);await hosts.CloseAsync();Assert.AreEqual(0,lease.Activations);Assert.AreEqual(1,lease.Releases);
        }
        [Test] public async Task 解放失敗は所有を維持し再試行成功まで新起動を拒否する()
        {
            var lease=new Lease();lease.ReleaseFactory=()=>lease.Releases==1?Task.FromException(new InvalidOperationException("fixture unload")):Task.CompletedTask;
            loader.Factory=()=>Task.FromResult<IHostSceneLease>(lease);await hosts.OpenAsync(Request());
            Assert.ThrowsAsync<InvalidOperationException>(async()=>await hosts.CloseAsync());Assert.AreEqual("host",hosts.CurrentPath);
            Assert.IsFalse((await hosts.OpenAsync(Request())).Succeeded);Assert.AreEqual(1,loader.Loads);
            await hosts.CloseAsync();Assert.AreEqual(2,lease.Releases);Assert.IsNull(hosts.CurrentPath);
        }
        [Test] public async Task 起動失敗後の解放失敗も再試行できる()
        {
            var lease=new Lease();lease.ReleaseFactory=()=>lease.Releases==1?Task.FromException(new InvalidOperationException("fixture unload")):Task.CompletedTask;
            loader.Factory=()=>Task.FromResult<IHostSceneLease>(lease);
            var result=await hosts.OpenAsync(Request(start:_=>throw new InvalidOperationException("fixture startup")));
            Assert.IsFalse(result.Succeeded);StringAssert.Contains("解放失敗",result.Error);Assert.AreEqual("host",hosts.CurrentPath);
            Assert.IsFalse((await hosts.OpenAsync(Request())).Succeeded);await hosts.CloseAsync();Assert.AreEqual(2,lease.Releases);Assert.IsNull(hosts.CurrentPath);
        }
        [Test] public async Task 外部で終了したホストを同path再要求すると追跡解除して再生成する()
        {
            var old=new Lease();loader.Factory=()=>Task.FromResult<IHostSceneLease>(old);await hosts.OpenAsync(Request());old.IsLoaded=false;
            var next=new Lease();loader.Factory=()=>Task.FromResult<IHostSceneLease>(next);Assert.IsTrue((await hosts.OpenAsync(Request())).Succeeded);
            Assert.AreEqual(1,old.Releases);Assert.AreEqual(2,loader.Loads);Assert.AreEqual(1,next.Activations);Assert.AreEqual("host",hosts.CurrentPath);
        }
        [Test] public void ビルド入口登録は重複せず他Sceneの順序と有効状態を保つ()
        {
            var original=new[]{new UnityEditor.EditorBuildSettingsScene("other-a",false),new UnityEditor.EditorBuildSettingsScene(BootstrapEntry.CompatibilityScenePath,false),new UnityEditor.EditorBuildSettingsScene("other-b",true)};
            var first=BootstrapBuildSetup.Plan(original);var second=BootstrapBuildSetup.Plan(first);
            Assert.AreEqual(BootstrapEntry.BootstrapScenePath,first[0].path);Assert.IsTrue(first[0].enabled);
            CollectionAssert.AreEqual(Array.ConvertAll(first,x=>x.path),Array.ConvertAll(second,x=>x.path));
            Assert.AreEqual("other-a",first[1].path);Assert.IsFalse(first[1].enabled);Assert.IsTrue(first[2].enabled);Assert.AreEqual("other-b",first[3].path);Assert.IsFalse(original[1].enabled);
        }
    }
}
