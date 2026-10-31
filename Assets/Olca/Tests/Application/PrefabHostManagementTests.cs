using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Olca.Application.Dungeon;
using Olca.Application.Management;
using Olca.Presentation.Field;
using Olca.Unity;
namespace Olca.Tests.Application
{
    public sealed class PrefabHostManagementTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Test] public async Task HostPrefab_設定だけを供給し非activeのまま解放できる()
        {
            using(var resources=new ResourceManager()){
                var prefab=resources.GetShared<GameObject>(BootstrapEntry.GameplayHostPrefabPath);
                Assert.IsFalse(prefab.activeSelf);Assert.AreEqual(1,prefab.GetComponentsInChildren<Transform>(true).Length);
                Assert.AreEqual(0,prefab.GetComponentsInChildren<Camera>(true).Length);Assert.AreEqual(0,prefab.GetComponentsInChildren<AudioListener>(true).Length);Assert.AreEqual(0,prefab.GetComponentsInChildren<Canvas>(true).Length);
                var lease=await GameplayPrefabHostLease.LoadAsync(resources,BootstrapEntry.GameplayHostPrefabPath,SceneManager.GetActiveScene(),CancellationToken.None);
                var controller=lease.Root.GetComponent<DungeonBattleController>();Assert.NotNull(controller);Assert.IsFalse(lease.Root.activeSelf);Assert.AreEqual(1,resources.OwnedInstanceCount);
                foreach(var name in new[]{"gameFieldView","battleView","customScreenView","dungeonHudView","cardSelectionView"})Assert.IsNull(typeof(DungeonBattleController).GetField(name,Private).GetValue(controller),name);
                await lease.UnloadAsync();await lease.UnloadAsync();Assert.IsFalse(lease.IsLoaded);Assert.AreEqual(0,resources.OwnedInstanceCount);
            }
        }
        [Test] public void HostPrefab_欠損と事前取消は実体を作らない()
        {
            using(var resources=new ResourceManager())using(var cancel=new CancellationTokenSource()){
                cancel.Cancel();Assert.CatchAsync<OperationCanceledException>(async()=>await GameplayPrefabHostLease.LoadAsync(resources,BootstrapEntry.GameplayHostPrefabPath,SceneManager.GetActiveScene(),cancel.Token));
                Assert.ThrowsAsync<InvalidOperationException>(async()=>await GameplayPrefabHostLease.LoadAsync(resources,"OlcaSystem/absent-host",SceneManager.GetActiveScene(),CancellationToken.None));Assert.AreEqual(0,resources.OwnedInstanceCount);
            }
        }
        [Test] public void RewardPrefab_画面を単独供給し共有入力や論理状態を持たず返却する()
        {
            var parent=new GameObject("RewardHost");var resources=new ResourceManager();var dialogs=new Olca.Presentation.Management.DialogManager(()=>Time.frameCount);var views=new Olca.Presentation.Management.ViewManager(resources,dialogs);
            try{
                var binding=new CardRewardPresentationBinding(new object(),resources,views,parent.transform);var view=binding.View;Assert.NotNull(view);Assert.AreEqual(1,resources.OwnedInstanceCount);
                Assert.AreEqual(0,parent.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true).Length);Assert.AreEqual(0,parent.GetComponentsInChildren<DungeonBattleController>(true).Length);
                var container=(GameObject)view.GetType().GetField("containerRoot",Private).GetValue(view);Assert.IsFalse(container.activeSelf,"報酬phaseまで表示しない");
                var items=(Array)view.GetType().GetField("cardItems",Private).GetValue(view);Assert.Greater(items.Length,0);Assert.IsTrue(items.Cast<Component>().All(x=>x!=null&&x.transform.IsChildOf(parent.transform)));
                binding.Dispose();binding.Dispose();Assert.IsNull(binding.View);Assert.AreEqual(0,resources.OwnedInstanceCount);Assert.IsTrue(view==null);
            }finally{views.Dispose();dialogs.Dispose();resources.Dispose();UnityEngine.Object.DestroyImmediate(parent);}
        }
        private sealed class ObjectLease:IHostObjectLease
        {
            public GameObject Root{get;private set;}=new GameObject("ObjectHost");public bool IsLoaded=>Root!=null;public int Releases;
            public ObjectLease(){Root.SetActive(false);}public void Activate()=>Root.SetActive(true);
            public Task UnloadAsync(){Releases++;UnityEngine.Object.DestroyImmediate(Root);Root=null;return Task.CompletedTask;}
        }
        private sealed class NoSceneLoader:IHostSceneLoader
        {public int Loads;public Task<IHostSceneLease> LoadSceneAsync(string path,CancellationToken ct){Loads++;throw new Exception("Scene load must not run");}}
        [Test] public async Task Prefab要求_同時要求を共有してScene同名とは混同しない()
        {
            var sceneLoader=new NoSceneLoader();using(var hosts=new GameHostManager(sceneLoader,()=>false)){
                var pending=new TaskCompletionSource<IHostObjectLease>();int loads=0;var order=new List<string>();
                var request=HostOpenRequest.Prefab("same",ct=>{loads++;return pending.Task;},root=>{Assert.IsFalse(root.activeSelf);order.Add("prepare");},root=>{Assert.IsTrue(root.activeSelf);order.Add("start");});
                var a=hosts.OpenAsync(request);var b=hosts.OpenAsync(request);var lease=new ObjectLease();pending.SetResult(lease);Assert.IsTrue((await a).Succeeded);Assert.IsTrue((await b).Succeeded);Assert.AreEqual(1,loads);CollectionAssert.AreEqual(new[]{"prepare","start"},order);
                Assert.IsFalse((await hosts.OpenAsync(new HostOpenRequest("same",_=>{},_=>{}))).Succeeded);Assert.AreEqual(0,sceneLoader.Loads);
                await hosts.CloseAsync();Assert.AreEqual(1,lease.Releases);Assert.IsNull(hosts.CurrentPath);
            }
        }
        [Test] public async Task Prefab要求_取消後の後着実体も有効化せず解放する()
        {
            using(var hosts=new GameHostManager(new NoSceneLoader(),()=>false))using(var cancellation=new CancellationTokenSource()){
                var pending=new TaskCompletionSource<IHostObjectLease>();var request=HostOpenRequest.Prefab("cancel",ct=>pending.Task,_=>Assert.Fail("取消後prepare"),_=>Assert.Fail("取消後start"));
                var open=hosts.OpenAsync(request,cancellation.Token);cancellation.Cancel();var lease=new ObjectLease();pending.SetResult(lease);Assert.IsTrue((await open).Cancelled);Assert.IsFalse(lease.IsLoaded);Assert.AreEqual(1,lease.Releases);
            }
        }
    }
}
