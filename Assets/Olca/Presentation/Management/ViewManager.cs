using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Olca.Unity;

namespace Olca.Presentation.Management
{
    public sealed class ScreenOpenResult
    {
        public bool Succeeded { get; }
        public bool Cancelled { get; }
        public string Error { get; }
        internal ScreenOpenResult(bool success, bool cancelled = false, string error = null) { Succeeded = success; Cancelled = cancelled; Error = error; }
    }
    /// <summary>Viewに渡す寿命境界。保存・素材管理・ゲーム進行への参照は渡さない。</summary>
    public sealed class ManagedScreenContext
    {
        private readonly ViewManager manager;
        internal readonly string Key;
        internal readonly long Epoch;
        internal ManagedScreenContext(ViewManager manager, string key, long epoch) { this.manager = manager; Key = key; Epoch = epoch; }
        public bool IsCurrent => manager.IsCurrent(this);
        public bool DialogBlocksInput => manager.Dialogs.Blocks(this);
        public Task<DialogResult> ShowDialog(GameObject panel, Button confirm, Button cancel, Action<DialogResult> complete) => manager.Dialogs.Show(this, panel, confirm, cancel, complete);
        public void NotifyHidden() => manager.NotifyHidden(this);
    }
    /// <summary>画面単位の生成・切替を所有する。Dialogは別Managerへ委譲する。</summary>
    public sealed class ViewManager : IDisposable
    {
        private sealed class Entry
        {
            public object Owner;
            public string Key;
            public bool Valid = true;
            public Func<CancellationToken, Task<GameObject>> Factory;
            public Action<GameObject, ManagedScreenContext> Show;
            public Action<GameObject> Hide;
            public ResourceManager.InstanceLease Lease;
            public ManagedScreenContext Context;
        }
        private readonly ResourceManager resources;
        internal DialogManager Dialogs { get; }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private Entry current, pending;
        private CancellationTokenSource pendingCancellation;
        private TaskCompletionSource<ScreenOpenResult> pendingResult;
        private long version;
        private bool disposed, committing;
        public string CurrentKey => current?.Key;
        public int RegisteredCount => entries.Count;
        public ViewManager(ResourceManager resources, DialogManager dialogs) { this.resources = resources; Dialogs = dialogs; }
        public void Register(object owner, string key, Func<CancellationToken, Task<GameObject>> factory, Action<GameObject, ManagedScreenContext> show, Action<GameObject> hide)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ViewManager));
            if (owner == null || string.IsNullOrWhiteSpace(key) || factory == null || show == null || hide == null || entries.ContainsKey(key)) throw new ArgumentException("画面登録が重複または不足しています");
            entries.Add(key, new Entry { Owner = owner, Key = key, Factory = factory, Show = show, Hide = hide });
        }
        public Task<ScreenOpenResult> ShowAsync(string key, CancellationToken cancellation = default)
        {
            if (disposed || cancellation.IsCancellationRequested) return Task.FromResult(new ScreenOpenResult(false, true));
            if (committing) return Task.FromResult(new ScreenOpenResult(false, error: "画面の切替処理中です"));
            if (!entries.TryGetValue(key, out var entry)) return Task.FromResult(new ScreenOpenResult(false, error: "画面が登録されていません: " + key));
            if (ReferenceEquals(current, entry) && entry.Lease?.Instance != null && entry.Lease.Instance.activeInHierarchy) {
                // 表示中の画面へ戻る要求も、別画面の後着ロードより優先する。
                ++version; CancelPending();
                return Task.FromResult(new ScreenOpenResult(true));
            }
            if (ReferenceEquals(pending, entry) && pendingCancellation != null && !pendingCancellation.IsCancellationRequested) return WaitDuplicate(pendingResult.Task, cancellation);
            CancelPending();
            long request = ++version;
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var result = new TaskCompletionSource<ScreenOpenResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = entry; pendingCancellation = cts; pendingResult = result;
            RunOpen(entry, request, cts, result);
            return result.Task;
        }
        private static async Task<ScreenOpenResult> WaitDuplicate(Task<ScreenOpenResult> task, CancellationToken cancellation)
        {
            if (!cancellation.CanBeCanceled) return await task;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellation.Register(() => cancelled.TrySetResult(true))) {
                if (await Task.WhenAny(task, cancelled.Task) != task) return new ScreenOpenResult(false, true);
                return await task;
            }
        }
        private async void RunOpen(Entry entry, long request, CancellationTokenSource cts, TaskCompletionSource<ScreenOpenResult> result)
        {
            ResourceManager.InstanceLease created = null;
            ScreenOpenResult outcome;
            try {
                if (entry.Lease == null || entry.Lease.Instance == null) created = await resources.CreateAsync(entry.Factory, cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                if (disposed || !entry.Valid || request != version) throw new OperationCanceledException();
                if (created != null) { entry.Lease = created; created = null; }
                var prior = current;
                var context = new ManagedScreenContext(this, entry.Key, request);
                current = entry; entry.Context = context; committing = true;
                try {
                    entry.Show(entry.Lease.Instance, context);
                    if (!ReferenceEquals(current, entry) || !entry.Valid) throw new OperationCanceledException();
                    if (prior != null) { Dialogs.CancelOwner(prior.Context); prior.Hide(prior.Lease.Instance); prior.Context = null; }
                } catch {
                    Dialogs.CancelOwner(context); entry.Context = null; current = prior;
                    if (entry.Lease != null) { entry.Hide?.Invoke(entry.Lease.Instance); entry.Lease.Dispose(); entry.Lease = null; }
                    if (prior?.Lease?.Instance != null && !prior.Lease.Instance.activeSelf) prior.Show(prior.Lease.Instance, prior.Context);
                    throw;
                } finally { committing = false; }
                outcome = new ScreenOpenResult(true);
            } catch (OperationCanceledException) { outcome = new ScreenOpenResult(false, true); }
            catch (Exception error) { outcome = new ScreenOpenResult(false, error: error.Message); }
            finally {
                created?.Dispose();
                if (ReferenceEquals(pendingCancellation, cts)) { pending = null; pendingCancellation = null; pendingResult = null; }
                cts.Dispose();
            }
            result.TrySetResult(outcome);
        }
        internal bool IsCurrent(ManagedScreenContext context) => !disposed && current != null && current.Valid && ReferenceEquals(current.Context, context);
        internal void NotifyHidden(ManagedScreenContext context)
        {
            if (!IsCurrent(context)) return;
            Dialogs.CancelOwner(context); current.Context = null; current = null;
        }
        private void CancelPending() { if (pendingCancellation != null && !pendingCancellation.IsCancellationRequested) pendingCancellation.Cancel(); }
        public void CloseCurrent()
        {
            ++version; CancelPending();
            var prior = current; current = null;
            if (prior == null) return;
            Dialogs.CancelOwner(prior.Context); prior.Context = null;
            prior.Hide(prior.Lease.Instance);
        }
        public void UnregisterOwner(object owner)
        {
            foreach (var entry in entries.Values.Where(e => ReferenceEquals(e.Owner, owner)).ToArray()) {
                entry.Valid = false;
                if (ReferenceEquals(pending, entry)) { ++version; CancelPending(); }
                if (ReferenceEquals(current, entry)) CloseCurrent();
                Dialogs.CancelOwner(entry.Context); entry.Context = null;
                entry.Lease?.Dispose(); entry.Lease = null;
                entries.Remove(entry.Key); entry.Factory = null; entry.Show = null; entry.Hide = null; entry.Owner = null;
            }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            CloseCurrent();
            foreach (var owner in entries.Values.Select(e => e.Owner).Distinct().ToArray()) UnregisterOwner(owner);
            disposed = true;
        }
    }
}
