using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Olca.Unity;
using Olca.Presentation.Battle;
using Olca.Presentation.CustomScreen;
using Olca.Presentation.Management;

namespace Olca.Application.Management
{
    /// <summary>Battle/Customの表示実体だけをhostに貸し、終了時は購読解除してからViewを返す。</summary>
    public sealed class BattlePresentationBinding : IDisposable
    {
        private readonly object owner;
        private readonly ViewManager layer;
        private bool disposed;
        public BattleView Battle { get; private set; }
        public CustomScreenView Custom { get; private set; }

        public BattlePresentationBinding(object owner, ResourceManager resources, ViewManager views, Transform parent)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            layer = views.GetLayer("battle");
            layer.Register(owner, "battle-custom", ct => {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(UnityEngine.Object.Instantiate(resources.GetShared<GameObject>("OlcaSystem/BattlePresentation"), parent, false));
            }, (panel, context) => {
                Battle = panel.GetComponent<BattleView>();
                Custom = panel.GetComponentInChildren<CustomScreenView>(true);
                if (Battle == null || Custom == null) throw new InvalidOperationException("Battle/CustomのViewがありません");
                // 戦闘開始/終了のactive切替とCustomのpauseは既存Controller/Systemに残す。
                panel.SetActive(false);
            }, panel => { if (panel != null) panel.SetActive(false); });
            try {
                using (var cancellation = new CancellationTokenSource()) {
                    var showing = layer.ShowAsync("battle-custom", cancellation.Token);
                    if (!showing.IsCompleted) {
                        cancellation.Cancel();
                        throw new InvalidOperationException("起動Battle Viewは同期ロード可能なPrefabが必要です");
                    }
                    var result = showing.GetAwaiter().GetResult();
                    if (!result.Succeeded) throw new InvalidOperationException(result.Error ?? "Battle Viewを開始できません");
                }
            } catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (disposed) return;
            // Scene終了で先に破棄された実体は各ViewのOnDestroyが解除済み。
            if (Battle != null) Battle.ReleaseSessionPresentation();
            if (Custom != null) Custom.ReleaseSessionPresentation();
            layer.UnregisterOwner(owner);
            Battle = null; Custom = null; disposed = true;
        }
    }
}

namespace Olca.Application.Management
{
    /// <summary>hostの必要時表示とPopup資源を束縛する。ゲーム状態と保存処理を所有しない。</summary>
    public sealed class HostPanelBindings : IDisposable
    {
        private sealed class Slot
        {
            public ViewManager Layer;
            public Func<GameObject> Factory;
            public GameObject Instance;
        }
        private readonly System.Collections.Generic.Dictionary<string,Slot> slots=new System.Collections.Generic.Dictionary<string,Slot>();
        private readonly System.Collections.Generic.Dictionary<string,ResourceManager.InstanceLease> popups=new System.Collections.Generic.Dictionary<string,ResourceManager.InstanceLease>();
        private readonly ResourceManager resources;
        private readonly ViewManager views;
        private readonly object owner;
        private bool disposed;
        public bool IsAlive=>!disposed;
        public HostPanelBindings(object owner,Transform parent,ResourceManager resources,ViewManager views){this.owner=owner??throw new ArgumentNullException(nameof(owner));this.resources=resources;this.views=views;parent.gameObject.AddComponent<HostPanelLifetime>().Bind(this);}
        public void Register(string key,string layer,Func<GameObject> factory,Action<GameObject,ManagedScreenContext> show,Action<GameObject> hide)
        {
            if(disposed)throw new ObjectDisposedException(nameof(HostPanelBindings));
            var slot=new Slot{Factory=factory,Layer=views?.GetLayer(layer)};slots.Add(key,slot);
            if(slot.Layer!=null)slot.Layer.Register(owner,key,ct=>{
                ct.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException();
                return Task.FromResult(slot.Instance=factory());
            },show,hide);
        }
        /// <summary>既存の同期Render前に生成とcontextを確定する。表示しない先行生成は行わない。</summary>
        public GameObject Show(string key)
        {
            if(disposed)throw new ObjectDisposedException(nameof(HostPanelBindings));
            var slot=slots[key];
            if(slot.Layer==null){if(slot.Instance==null)slot.Instance=slot.Factory();return slot.Instance;}
            using(var cancellation=new CancellationTokenSource()){
                var task=slot.Layer.ShowAsync(key,cancellation.Token);
                if(!task.IsCompleted){cancellation.Cancel();throw new InvalidOperationException("同期表示factoryの完了を待てません: "+key);}
                var result=task.GetAwaiter().GetResult();if(!result.Succeeded)throw new InvalidOperationException(result.Error??"表示が取り消されました: "+key);
            }
            return slot.Instance;
        }
        public GameObject Popup(string key,Func<GameObject> factory)
        {
            if(disposed)throw new ObjectDisposedException(nameof(HostPanelBindings));
            if(popups.TryGetValue(key,out var prior)&&prior.Instance!=null)return prior.Instance;
            prior?.Dispose();
            if(resources==null){if(slots.TryGetValue(key,out var cached)&&cached.Instance!=null)return cached.Instance;var instance=factory();slots[key]=new Slot{Instance=instance};return instance;}
            // factoryはUnityの同期組立のみ。非同期ロードをブロックして待たない。
            var task=resources.CreateAsync(ct=>Task.FromResult(factory()),CancellationToken.None);
            if(!task.IsCompleted)throw new InvalidOperationException("同期Popup factoryが完了していません");
            var lease=task.GetAwaiter().GetResult();popups[key]=lease;return lease.Instance;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            // Popupの親context/callbackを先に返し、その後に借用部品を破棄する。
            foreach(var slot in slots.Values)slot.Layer?.UnregisterOwner(owner);
            foreach(var lease in popups.Values)lease.Dispose();popups.Clear();
            if(views==null)foreach(var slot in slots.Values)if(slot.Instance!=null){slot.Instance.SetActive(false);if(UnityEngine.Application.isPlaying)UnityEngine.Object.Destroy(slot.Instance);else UnityEngine.Object.DestroyImmediate(slot.Instance);}
            foreach(var slot in slots.Values){slot.Factory=null;slot.Instance=null;slot.Layer=null;}slots.Clear();
        }
    }
}

namespace Olca.Application.Management
{
    /// <summary>Sessionのcontent破棄からも生成leaseを返す。ゲーム状態には触れない。</summary>
    public sealed class HostPanelLifetime:MonoBehaviour
    {
        private HostPanelBindings binding;
        public void Bind(HostPanelBindings value){binding=value;}
        private void OnDestroy(){var value=binding;binding=null;value?.Dispose();}
    }
}


namespace Olca.Application.Management
{
    /// <summary>進行Dialogの型付き操作口。保存計画と原子的な適用は既存Sessionだけが所有する。</summary>
    public sealed class ProgressActionOperations : Olca.Presentation.Management.IProgressActionOperations
    {
        private readonly Olca.Core.Domain.ClearReturnSession session;
        private readonly System.Func<bool> canStart,alive;
        private Olca.Presentation.Management.ProgressPendingAction pendingAction;
        private readonly System.Func<System.Collections.Generic.IReadOnlyList<Olca.Core.Domain.Card>> starter;
        public ProgressActionOperations(Olca.Core.Domain.ClearReturnSession session,System.Func<bool> canStart,System.Func<System.Collections.Generic.IReadOnlyList<Olca.Core.Domain.Card>> starter,System.Func<bool> alive)
        {this.alive=alive;this.session=session??throw new System.ArgumentNullException(nameof(session));this.canStart=canStart;this.starter=starter;}
        public long Revision=>session.Snapshot.Revision;
        public bool Pending=>session.HasPendingSave;
        public string Error=>session.Error;
        public Olca.Presentation.Management.ProgressPendingAction PendingAction=>session.HasPendingSave?pendingAction:Olca.Presentation.Management.ProgressPendingAction.None;
        public bool CanStartNewGame=>(alive?.Invoke()??false)&&(canStart?.Invoke()??false);
        public bool ManualSave(){if(!(alive?.Invoke()??false)||session.HasPendingSave)return false;bool saved=session.ManualSave();pendingAction=session.HasPendingSave?Olca.Presentation.Management.ProgressPendingAction.ManualSave:Olca.Presentation.Management.ProgressPendingAction.None;return saved;}
        public bool Retry(){if(!(alive?.Invoke()??false)||PendingAction==Olca.Presentation.Management.ProgressPendingAction.None)return false;bool saved=session.Retry();if(saved)pendingAction=Olca.Presentation.Management.ProgressPendingAction.None;return saved;}
        public bool StartNewGame(long confirmedRevision){if(!CanStartNewGame||session.HasPendingSave)return false;bool saved=session.StartNewGame(confirmedRevision,starter());pendingAction=session.HasPendingSave?Olca.Presentation.Management.ProgressPendingAction.NewGame:Olca.Presentation.Management.ProgressPendingAction.None;return saved;}
    }
}
