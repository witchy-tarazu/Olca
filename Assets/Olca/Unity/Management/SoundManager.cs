using System;
using UnityEngine;

namespace Olca.Unity
{
    /// <summary>既存ルーターをアプリ寿命で一つ所有する。画面変更では音を作り直さない。</summary>
    public sealed class SoundManager : IDisposable
    {
        private object sessionOwner;
        private bool disposed;
        public GameplayAudioRouter Router { get; }
        public SoundManager(Transform root) { Router = new GameplayAudioRouter(root); }
        public GameplayAudioRouter BeginSession(object owner, AudioVolumeSettings settings)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SoundManager));
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (sessionOwner != null && !ReferenceEquals(sessionOwner, owner)) throw new InvalidOperationException("音声の攻略所有者が重複しています");
            sessionOwner = owner; Router.Apply(settings); return Router;
        }
        public void EndSession(object owner)
        {
            if (disposed || !ReferenceEquals(sessionOwner, owner)) return;
            sessionOwner = null; Router.PlayBgm(null); Router.Effects.Stop(); Router.Voice.Stop();
        }
        public void Dispose() { if (disposed) return; disposed = true; sessionOwner = null; Router.Dispose(); }
    }
}
