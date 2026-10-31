using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Olca.Unity
{
    /// <summary>共有Resourcesへの参照と、生成した表示実体の所有を分ける。</summary>
    public sealed class ResourceManager : IDisposable
    {
        public sealed class InstanceLease : IDisposable
        {
            private ResourceManager owner;
            public GameObject Instance { get; private set; }
            internal InstanceLease(ResourceManager owner, GameObject instance) { this.owner = owner; Instance = instance; }
            public void Dispose()
            {
                if (owner == null) return;
                var prior = owner; owner = null; prior.instances.Remove(this);
                DestroyOwned(Instance); Instance = null;
            }
        }
        private readonly HashSet<InstanceLease> instances = new HashSet<InstanceLease>();
        private readonly Dictionary<(Type, string), UnityEngine.Object> shared = new Dictionary<(Type, string), UnityEngine.Object>();
        private bool disposed;
        public int OwnedInstanceCount => instances.Count;
        public T GetShared<T>(string path) where T : UnityEngine.Object
        {
            if (disposed) throw new ObjectDisposedException(nameof(ResourceManager));
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(nameof(path));
            var key = (typeof(T), path);
            if (!shared.TryGetValue(key, out var value) || value == null) {
                value = Resources.Load<T>(path);
                if (value == null) throw new InvalidOperationException("リソースがありません: " + path);
                shared[key] = value;
            }
            return (T)value;
        }
        public async Task<InstanceLease> CreateAsync(Func<CancellationToken, Task<GameObject>> factory, CancellationToken cancellation)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ResourceManager));
            cancellation.ThrowIfCancellationRequested();
            var instance = await factory(cancellation);
            if (instance == null) throw new InvalidOperationException("画面の生成に失敗しました");
            if (disposed || cancellation.IsCancellationRequested) {
                DestroyOwned(instance); cancellation.ThrowIfCancellationRequested();
                throw new ObjectDisposedException(nameof(ResourceManager));
            }
            var lease = new InstanceLease(this, instance); instances.Add(lease); return lease;
        }
        public Task<InstanceLease> InstantiatePrefabAsync(string path, Transform parent, CancellationToken cancellation = default) =>
            CreateAsync(ct => { ct.ThrowIfCancellationRequested(); return Task.FromResult(UnityEngine.Object.Instantiate(GetShared<GameObject>(path), parent)); }, cancellation);
        private static void DestroyOwned(GameObject value)
        {
            if (value == null) return; value.SetActive(false);
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var value in new List<InstanceLease>(instances)) value.Dispose();
            // 他の既存Catalogも共有するimport素材を勝手にDestroy/Unloadしない。
            shared.Clear();
        }
    }
}
