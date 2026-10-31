using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

namespace Olca.Unity
{
    public interface IHostSceneLease
    {
        Scene Scene { get; }
        bool IsLoaded { get; }
        void Activate();
        Task UnloadAsync();
    }
    public interface IHostSceneLoader
    {
        // nativeロード開始後は取消でも結果leaseを回収する。呼出側は有効化前にtokenを再確認し解放する。
        Task<IHostSceneLease> LoadSceneAsync(string path, CancellationToken cancellation);
    }
    /// <summary>移行中の互換Sceneをhandle単位で所有する。ロード完了まで取消結果も回収する。</summary>
    public sealed class SceneResourceLoader : IHostSceneLoader, IDisposable
    {
        private sealed class Lease : IHostSceneLease
        {
            private SceneResourceLoader owner;
            private readonly List<(GameObject Root, bool Active)> roots;
            private Scene previous;
            private Task unloading;
            public Scene Scene { get; }
            public bool IsLoaded=>Scene.IsValid()&&Scene.isLoaded;
            public Lease(SceneResourceLoader owner, Scene scene, List<(GameObject, bool)> roots) { this.owner=owner;Scene=scene;this.roots=roots; }
            public void Activate()
            {
                if(owner==null||unloading!=null||!Scene.IsValid()||!Scene.isLoaded)throw new InvalidOperationException("解放済みホストです");
                previous=SceneManager.GetActiveScene();SceneManager.SetActiveScene(Scene);
                foreach(var item in roots)if(item.Root!=null)item.Root.SetActive(item.Active);
            }
            public Task UnloadAsync() => unloading==null||unloading.IsFaulted ? unloading=UnloadCore() : unloading;
            private async Task UnloadCore()
            {
                var prior=owner;if(prior==null)return;
                foreach(var item in roots)if(item.Root!=null)item.Root.SetActive(false);
                if(Application.isPlaying&&Scene.IsValid()&&Scene.isLoaded){
                    if(SceneManager.GetActiveScene()==Scene&&previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                    var operation=SceneManager.UnloadSceneAsync(Scene);
                    if(operation!=null)while(!operation.isDone)await UniTask.Yield();
                    if(Scene.isLoaded)throw new InvalidOperationException("Sceneの解放を完了できません。再試行してください");
                }
                // 失敗時にはowner/追跡集合を維持し、同じhandleを再試行する。
                owner=null;prior.leases.Remove(this);
            }
        }
        private readonly HashSet<Lease> leases=new HashSet<Lease>();
        private bool disposed,loading;
        public int OwnedSceneCount=>leases.Count;
        public async Task<IHostSceneLease> LoadSceneAsync(string path,CancellationToken cancellation)
        {
            if(disposed)throw new ObjectDisposedException(nameof(SceneResourceLoader));
            cancellation.ThrowIfCancellationRequested();
            if(loading)throw new InvalidOperationException("Sceneロードの完了回収中です");
            if(!Application.isPlaying||string.IsNullOrWhiteSpace(path)||!Application.CanStreamedLevelBeLoaded(path))throw new InvalidOperationException("ホストSceneをロードできません: "+path);
            if(SceneManager.GetSceneByPath(path).isLoaded)throw new InvalidOperationException("既にロードされたSceneは取得しません: "+path);
            loading=true;
            try {
            Scene captured=default;var roots=new List<(GameObject,bool)>();
            void Loaded(Scene scene,LoadSceneMode mode) {
                if(scene.path!=path||captured.IsValid())return;
                captured=scene;
                // Awake/OnEnableは通知前に走る。保存/入力開始を含むController.Startだけを起動許可まで止める。
                foreach(var root in scene.GetRootGameObjects()){roots.Add((root,root.activeSelf));root.SetActive(false);}
            }
            SceneManager.sceneLoaded+=Loaded;
            try {
                var operation=SceneManager.LoadSceneAsync(path,LoadSceneMode.Additive);
                if(operation==null)throw new InvalidOperationException("Sceneロードを開始できません");
                // Unityのロードは取消不能。完了回収後に非activeのまま解放する。
                while(!operation.isDone)await UniTask.Yield();
            } finally { SceneManager.sceneLoaded-=Loaded; }
            if(!captured.IsValid()||!captured.isLoaded)throw new InvalidOperationException("ロードしたSceneを捕捉できません");
            var lease=new Lease(this,captured,roots);leases.Add(lease);
            // 開始後は必ず非active leaseを返す。coordinatorが取消/世代を確認し、解放失敗も追跡する。
            return lease;
            } finally {loading=false;}
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            foreach(var lease in new List<Lease>(leases))Release(lease);
        }
        private static async void Release(Lease lease) { try{await lease.UnloadAsync();}catch(Exception error){Debug.LogException(error);} }
    }
}
