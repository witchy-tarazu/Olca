using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Olca.Unity
{
    /// <summary>実行時生成Sprite/Textureだけを所有する。import素材と表示/GameStateは所有しない。</summary>
    public static class GeneratedSpriteAssets
    {
        public sealed class Builder
        {
            internal readonly List<UnityEngine.Object> Objects = new List<UnityEngine.Object>();
            public Texture2D CreateTexture(int width,int height)
            {var value=new Texture2D(width,height,TextureFormat.RGBA32,false);Objects.Add(value);return value;}
            public Sprite CreateSprite(Texture2D texture,Rect rect,Vector2 pivot,float pixelsPerUnit=100)
            {
                // 借用したimport Textureを所有物として登録できない。
                if(texture==null||!Objects.Contains(texture))throw new ArgumentException("この生成処理が所有するTextureだけを使用してください");
                var value=Sprite.Create(texture,rect,pivot,pixelsPerUnit,0,SpriteMeshType.FullRect);Objects.Add(value);return value;
            }
        }
        internal sealed class Entry { internal object Value; internal List<UnityEngine.Object> Objects; }
        public sealed class Cache
        {
            internal readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
            internal readonly HashSet<Lease> borrowers=new HashSet<Lease>();
            internal int roots;internal bool retired,disposed;
            public int Generation {get;}=++nextGeneration;
            internal T Get<T>(string key,Func<Builder,T> factory) where T:class
            {
                if(disposed)throw new ObjectDisposedException(nameof(Cache));
                if(entries.TryGetValue(key,out var prior)){
                    if(prior.Objects.All(x=>x!=null))return (T)prior.Value;
                    ReleaseObjects(prior.Objects);entries.Remove(key);
                }
                var builder=new Builder();
                try{
                    var value=factory(builder);
                    if(value==null){ReleaseObjects(builder.Objects);return null;}
                    entries.Add(key,new Entry{Value=value,Objects=builder.Objects});return value;
                }catch{ReleaseObjects(builder.Objects);throw;}
            }
            internal void ReleaseIfUnused()
            {
                PruneBorrowers();
                if(roots!=0||borrowers.Count!=0)return;
                Dispose(false);
            }
            internal void PruneBorrowers()
            {
                foreach(var lease in borrowers.ToArray())if(lease.HasNativeOwner&&lease.NativeOwner==null)lease.Release(false);
            }
            internal void Dispose(bool immediate)
            {
                if(disposed)return;disposed=true;
                foreach(var entry in entries.Values)ReleaseObjects(entry.Objects,immediate);
                entries.Clear();live.Remove(this);if(ReferenceEquals(current,this))current=null;
            }
        }
        /// <summary>利用中のView/Visualが保持する借用。非表示だけでは返却しない。</summary>
        public sealed class Lease : IDisposable
        {
            private Cache cache;
            internal UnityEngine.Object NativeOwner;
            internal bool HasNativeOwner;
            internal Lease(Cache cache,UnityEngine.Object owner){this.cache=cache;NativeOwner=owner;HasNativeOwner=!ReferenceEquals(owner,null);cache.borrowers.Add(this);}
            public int Generation=>cache?.Generation??0;
            public void BindNativeOwner(UnityEngine.Object owner)
            {
                if(cache==null)throw new ObjectDisposedException(nameof(Lease));
                if(owner==null)throw new ArgumentNullException(nameof(owner));
                if(HasNativeOwner&&!ReferenceEquals(NativeOwner,owner))throw new InvalidOperationException("借用所有者は付け替えできません");
                NativeOwner=owner;HasNativeOwner=true;QueuePrune();
            }
            public T Get<T>(string key,Func<Builder,T> factory) where T:class
            {if(cache==null)throw new ObjectDisposedException(nameof(Lease));return cache.Get(key,factory);}
            internal void Release(bool check)
            {
                var prior=cache;if(prior==null)return;cache=null;prior.borrowers.Remove(this);
                if(check)prior.ReleaseIfUnused();
            }
            public void Dispose(){Release(true);QueuePrune();}
        }
        public sealed class RootLease : IDisposable
        {
            private Cache cache;
            internal RootLease(Cache cache){this.cache=cache;cache.roots++;}
            public int Generation=>cache?.Generation??0;
            public void Dispose()
            {
                var prior=cache;if(prior==null)return;cache=null;prior.roots--;
                if(prior.roots==0){prior.retired=true;if(ReferenceEquals(current,prior))current=null;}
                prior.ReleaseIfUnused();QueuePrune();
            }
        }
        private static readonly HashSet<Cache> live=new HashSet<Cache>();
        private static readonly HashSet<UnityEngine.Object> pending=new HashSet<UnityEngine.Object>();
        private static Cache current;private static int nextGeneration;private static bool pruneQueued,shuttingDown;
        static GeneratedSpriteAssets()
        {
            Application.quitting+=Shutdown;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload+=Shutdown;
            UnityEditor.EditorApplication.quitting+=Shutdown;
#endif
        }
        private static Cache Current()
        {
            if(shuttingDown){shuttingDown=false;pruneQueued=false;}
            if(current==null||current.disposed||current.retired){current=new Cache();live.Add(current);}return current;
        }
        public static RootLease AcquireRoot(){Prune();return new RootLease(Current());}
        /// <summary>Rootのない検証/旧経路も、この明示scopeをusing/Destroyで必ず返す。Rootは生成しない。</summary>
        public static Lease Acquire(UnityEngine.Object owner=null){Prune();var lease=new Lease(Current(),owner);if(lease.HasNativeOwner)QueuePrune();return lease;}
        public static T Get<T>(string key,Func<Builder,T> factory,Lease lease=null) where T:class
        {
            if(lease!=null)return lease.Get(key,factory);
            Prune();
            if(current==null||current.disposed)throw new InvalidOperationException("生成素材の利用にはRootまたは明示借用scopeが必要です");
            return current.Get(key,factory);
        }
        public static void Prune()
        {
            foreach(var cache in live.ToArray())cache.ReleaseIfUnused();
            pending.RemoveWhere(x=>x==null);
        }
        public static object Inspect()
        {
            Prune();return new{generations=live.Count,roots=live.Sum(x=>x.roots),borrowers=live.Sum(x=>x.borrowers.Count),entries=live.Sum(x=>x.entries.Count),pendingNativeDestroy=pending.Count,
                caches=live.Select(x=>new{generation=x.Generation,x.roots,borrowers=x.borrowers.Count,x.retired,entries=x.entries.Select(e=>new{key=e.Key,sprites=e.Value.Objects.OfType<Sprite>().Where(v=>v!=null).Select(v=>v.GetInstanceID()).ToArray(),textures=e.Value.Objects.OfType<Texture2D>().Where(v=>v!=null).Select(v=>v.GetInstanceID()).ToArray()}).ToArray()}).ToArray()};
        }
        private static void ReleaseObjects(IEnumerable<UnityEngine.Object> values,bool immediate=false)
        {
            // Textureだけ先に失効させず、所有Spriteを先に破棄する。importはBuilderで登録できない。
            foreach(var value in values.OrderBy(x=>x is Sprite?0:1)){
                if(value==null)continue;
                if(immediate||!Application.isPlaying){UnityEngine.Object.DestroyImmediate(value);pending.Remove(value);}
                else if(pending.Add(value))UnityEngine.Object.Destroy(value);
            }
        }
        private static async void QueuePrune()
        {
            if(!Application.isPlaying||pruneQueued||shuttingDown)return;
            pruneQueued=true;
            try{await Awaitable.NextFrameAsync(Application.exitCancellationToken);if(!shuttingDown)Prune();}
            catch(OperationCanceledException) { }
            finally{pruneQueued=false;if(!shuttingDown&&(pending.Count!=0||live.Any(cache=>cache.roots==0&&cache.borrowers.Any(lease=>lease.HasNativeOwner))))QueuePrune();}
        }
        private static void Shutdown()
        {
            // Domain/アプリ終了は全managed所有者の終端。通常Root終了はこの強制経路を使わない。
            shuttingDown=true;foreach(var cache in live.ToArray())cache.Dispose(true);
            ReleaseObjects(pending.ToArray(),true);pending.Clear();
        }
    }
}
