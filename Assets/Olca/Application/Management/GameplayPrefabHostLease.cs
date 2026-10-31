using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Olca.Application.Dungeon;
using Olca.Unity;
namespace Olca.Application.Management
{
    /// <summary>攻略hostの寿命を表示Viewとは分ける。破棄完了まで次のhostへ所有を渡さない。</summary>
    public sealed class GameplayPrefabHostLease : IHostObjectLease
    {
        private ResourceManager.InstanceLease instance;
        private GameObject root;
        private Task unloading;
        private bool stopped;
        public GameObject Root=>root;
        public bool IsLoaded=>root!=null;
        private GameplayPrefabHostLease(ResourceManager.InstanceLease instance){this.instance=instance;root=instance.Instance;}
        public static async Task<IHostObjectLease> LoadAsync(ResourceManager resources,string path,Scene target,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if(!target.IsValid()||!target.isLoaded)throw new InvalidOperationException("hostの配置先Sceneがありません");
            var prefab=resources.GetShared<GameObject>(path);
            if(prefab.activeSelf||prefab.GetComponentsInChildren<DungeonBattleController>(true).Length!=1)throw new InvalidOperationException("hostは非activeかつController1個のPrefabが必要です");
            var lease=await resources.CreateAsync(ct=>{ct.ThrowIfCancellationRequested();return Task.FromResult(UnityEngine.Object.Instantiate(prefab));},cancellation);
            try{if(lease.Instance.scene!=target)SceneManager.MoveGameObjectToScene(lease.Instance,target);return new GameplayPrefabHostLease(lease);}
            catch{lease.Dispose();throw;}
        }
        public void Activate(){if(root==null||unloading!=null)throw new InvalidOperationException("解放済みhostです");root.SetActive(true);}
        public Task UnloadAsync()=>unloading==null||unloading.IsFaulted ? unloading=UnloadCore() : unloading;
        private async Task UnloadCore()
        {
            if(root==null){instance?.Dispose();instance=null;return;}
            if(!stopped){root.GetComponentInChildren<DungeonBattleController>(true).StopManagedHost();stopped=true;}
            root.SetActive(false);instance?.Dispose();
            // Editor更新からの要求では次UpdateがDestroy処理より早い。描画終了側まで回収する。
            if(UnityEngine.Application.isPlaying)await UniTask.NextFrame(PlayerLoopTiming.LastPostLateUpdate);
            if(root!=null)throw new InvalidOperationException("hostの破棄完了を確認できません。解放を再試行してください");
            instance=null;root=null;
        }
    }
}
