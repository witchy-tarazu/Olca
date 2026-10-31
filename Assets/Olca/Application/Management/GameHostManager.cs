using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
using UnityEngine;
using Olca.Unity;

namespace Olca.Application.Management
{
    public sealed class HostOpenRequest
    {
        public string Path { get; }
        public Action<Scene> Prepare { get; }
        public Action<Scene> Start { get; }
        internal string Identity { get; }
        private Func<CancellationToken,Task<IHostObjectLease>> objectLoader;
        private Action<GameObject> prepareObject,startObject;
        public HostOpenRequest(string path,Action<Scene> prepare,Action<Scene> start) { Path=path;Identity="scene:"+path;Prepare=prepare??throw new ArgumentNullException(nameof(prepare));Start=start??throw new ArgumentNullException(nameof(start)); }
        private HostOpenRequest(string path,Func<CancellationToken,Task<IHostObjectLease>> load,Action<GameObject> prepare,Action<GameObject> start)
        { Path=path;Identity="prefab:"+path;objectLoader=load??throw new ArgumentNullException(nameof(load));prepareObject=prepare??throw new ArgumentNullException(nameof(prepare));startObject=start??throw new ArgumentNullException(nameof(start)); }
        public static HostOpenRequest Prefab(string path,Func<CancellationToken,Task<IHostObjectLease>> load,Action<GameObject> prepare,Action<GameObject> start)=>new HostOpenRequest(path,load,prepare,start);
        internal async Task<IGameHostLease> LoadAsync(IHostSceneLoader scenes,CancellationToken cancellation)
        { if(objectLoader!=null)return await objectLoader(cancellation);return await scenes.LoadSceneAsync(Path,cancellation); }
        internal void PrepareHost(IGameHostLease lease){if(objectLoader!=null)prepareObject(((IHostObjectLease)lease).Root);else Prepare(((IHostSceneLease)lease).Scene);}
        internal void StartHost(IGameHostLease lease){if(objectLoader!=null)startObject(((IHostObjectLease)lease).Root);else Start(((IHostSceneLease)lease).Scene);}
    }
    public sealed class HostOpenResult
    {
        public bool Succeeded { get; }
        public bool Cancelled { get; }
        public string Error { get; }
        internal HostOpenResult(bool success,bool cancelled=false,string error=null){Succeeded=success;Cancelled=cancelled;Error=error;}
    }
    /// <summary>攻略hostのロード/起動/終了。Scene互換とPrefabで同じ取消・解放契約を使う。</summary>
    public sealed class GameHostManager : IDisposable
    {
        private readonly IHostSceneLoader loader;
        private readonly Func<bool> hasSession;
        private IGameHostLease current;
        private string currentPath,currentIdentity,pendingIdentity;
        private CancellationTokenSource pendingCancellation;
        private Task<HostOpenResult> pending;
        private Task closing;
        private long version;
        private bool disposed,committing,releaseFailed;
        public string CurrentPath=>currentPath;
        public bool IsLoading=>pendingCancellation!=null;
        public GameHostManager(IHostSceneLoader loader,Func<bool> hasSession){this.loader=loader??throw new ArgumentNullException(nameof(loader));this.hasSession=hasSession??throw new ArgumentNullException(nameof(hasSession));}
        public Task<HostOpenResult> OpenAsync(HostOpenRequest request,CancellationToken cancellation=default)
        {
            if(disposed||cancellation.IsCancellationRequested)return Task.FromResult(new HostOpenResult(false,true));
            if(request==null||string.IsNullOrWhiteSpace(request.Path))return Task.FromResult(new HostOpenResult(false,error:"ホストが未指定です"));
            if(committing||closing!=null)return Task.FromResult(new HostOpenResult(false,error:"ホストの切替処理中です"));
            if(current!=null&&releaseFailed)return Task.FromResult(new HostOpenResult(false,error:"ホストの解放を再試行してください"));
            if(current!=null&&!current.IsLoaded)return ReopenReleasedAsync(request,cancellation);
            if(current!=null)return Task.FromResult(new HostOpenResult(currentIdentity==request.Identity,error:currentIdentity==request.Identity?null:"現在のホストを終了してください"));
            if(pendingCancellation!=null&&pendingCancellation.IsCancellationRequested)return Task.FromResult(new HostOpenResult(false,true,error:"取消ロードの解放待ちです"));
            if(pendingCancellation!=null){if(pendingIdentity==request.Identity)return Observe(pending,cancellation);return Task.FromResult(new HostOpenResult(false,error:"別ホストをロード中です"));}
            if(hasSession())return Task.FromResult(new HostOpenResult(false,error:"直起動した攻略が既に動作しています"));
            var completion=new TaskCompletionSource<HostOpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cts=CancellationTokenSource.CreateLinkedTokenSource(cancellation);long requestVersion=++version;
            pendingIdentity=request.Identity;pendingCancellation=cts;pending=completion.Task;
            RunOpen(request,requestVersion,cts,completion);return completion.Task;
        }
        private async Task<HostOpenResult> ReopenReleasedAsync(HostOpenRequest request,CancellationToken cancellation)
        {
            // BootstrapのSingle再入など、外部で終了したSceneも古い追跡を外してから再生成する。
            try{await CloseAsync();return await OpenAsync(request,cancellation);}
            catch(Exception error){return new HostOpenResult(false,error:error.Message);}
        }
        private static async Task<HostOpenResult> Observe(Task<HostOpenResult> task,CancellationToken cancellation)
        {
            if(!cancellation.CanBeCanceled)return await task;
            var stopped=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using(cancellation.Register(()=>stopped.TrySetResult(true))){if(await Task.WhenAny(task,stopped.Task)!=task)return new HostOpenResult(false,true);return await task;}
        }
        private async void RunOpen(HostOpenRequest request,long requestVersion,CancellationTokenSource cts,TaskCompletionSource<HostOpenResult> completion)
        {
            IGameHostLease lease=null;HostOpenResult result=new HostOpenResult(false,error:"ホスト起動未完了");
            try {
                lease=await request.LoadAsync(loader,cts.Token);cts.Token.ThrowIfCancellationRequested();
                if(disposed||requestVersion!=version)throw new OperationCanceledException();
                if(hasSession())throw new InvalidOperationException("ロード中に別の攻略が起動しました");
                committing=true;
                try {
                    request.PrepareHost(lease);cts.Token.ThrowIfCancellationRequested();
                    if(disposed||requestVersion!=version)throw new OperationCanceledException();
                    lease.Activate();request.StartHost(lease);
                    if(disposed||requestVersion!=version)throw new OperationCanceledException();
                    // 同期起動が完了した時点でcommit。以後の取消は起動済み保存を巻き戻さない。
                    current=lease;currentPath=request.Path;currentIdentity=request.Identity;lease=null;
                } finally { committing=false; }
                result=new HostOpenResult(true);
            } catch(OperationCanceledException){result=new HostOpenResult(false,true);}
            catch(Exception error){result=new HostOpenResult(false,error:error.Message);}
            finally {
                if(lease!=null){try{await lease.UnloadAsync();}catch(Exception error){current=lease;currentPath=request.Path;currentIdentity=request.Identity;releaseFailed=true;result=new HostOpenResult(false,error:(result.Error??"起動取消")+" / 解放失敗: "+error.Message);}}
                if(ReferenceEquals(pendingCancellation,cts)){pendingCancellation=null;pendingIdentity=null;pending=null;}
                cts.Dispose();
            }
            completion.TrySetResult(result);
        }
        public Task CloseAsync()
        {
            if(closing!=null)return closing;
            ++version;pendingCancellation?.Cancel();
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            closing=completion.Task;RunClose(pending,completion);return completion.Task;
        }
        private async void RunClose(Task<HostOpenResult> priorLoad,TaskCompletionSource<bool> completion)
        {
            try {
                if(priorLoad!=null)await priorLoad;
                var lease=current;
                if(lease!=null)await lease.UnloadAsync();
                current=null;currentPath=null;currentIdentity=null;releaseFailed=false;
                completion.TrySetResult(true);
            } catch(Exception error){releaseFailed=true;completion.TrySetException(error);}
            finally {closing=null;}
        }
        public void Dispose(){if(disposed)return;disposed=true;ObserveClose(CloseAsync());}
        private static async void ObserveClose(Task close){try{await close;}catch(Exception error){UnityEngine.Debug.LogException(error);}}
    }
}
