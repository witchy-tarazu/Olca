using System;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Integration;
using Olca.Presentation.Management;
using Olca.Unity;

namespace Olca.Application.Management
{
    /// <summary>起動・Manager寿命・攻略との接続順を調整する。ゲーム計算と論理時計は持たない。</summary>
    public sealed class GameSystem : IDisposable
    {
        public sealed class Session : IDisposable
        {
            private GameSystem system;
            internal object Owner { get; private set; }
            public SaveManager.Context Storage { get; }
            public DungeonBattleManager Dungeon { get; private set; }
            public IInputProvider Input { get; private set; }
            internal Session(GameSystem system, object owner, SaveManager.Context storage) { this.system = system; Owner = owner; Storage = storage; }
            public void Bind(DungeonBattleManager dungeon, IInputProvider input) { if (system == null) throw new ObjectDisposedException(nameof(Session)); Dungeon = dungeon; Input = input; }
            public void Dispose()
            {
                var prior = system; if (prior == null) return;
                system = null; Dungeon = null; Input = null;
                var owner = Owner; Owner = null; prior.EndSession(this, owner);
            }
        }
        public SaveManager Saves { get; }
        public ResourceManager Resources { get; }
        public SoundManager Sounds { get; }
        public ViewManager Views { get; }
        public DialogManager Dialogs { get; }
        public Session CurrentSession { get; private set; }
        private bool disposed;
        public GameSystem(SaveManager saves, ResourceManager resources, SoundManager sounds, ViewManager views, DialogManager dialogs)
        { Saves = saves; Resources = resources; Sounds = sounds; Views = views; Dialogs = dialogs; }
        public Session BeginSession(object owner, string settings, string checkpoints, string progress)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameSystem));
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (CurrentSession != null) {
                if (ReferenceEquals(CurrentSession.Owner, owner)) return CurrentSession;
                throw new InvalidOperationException("攻略の所有者が重複しています");
            }
            return CurrentSession = new Session(this, owner, Saves.OpenContext(settings, checkpoints, progress));
        }
        private void EndSession(Session session, object owner)
        {
            if (!ReferenceEquals(CurrentSession, session)) return;
            Views.UnregisterOwner(owner); Sounds.EndSession(owner); CurrentSession = null;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            CurrentSession?.Dispose();
            // 再生中の音が参照する素材より先に、音声所有物を解放する。
            Dialogs.Dispose(); Views.Dispose(); Sounds.Dispose(); Resources.Dispose();
        }
    }
}
