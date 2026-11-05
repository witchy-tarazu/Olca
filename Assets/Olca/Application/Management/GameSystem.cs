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
            public GameplayRuntimeSession Runtime { get; private set; }
            public void AdoptRuntime(GameplayRuntimeSession runtime)
            { if(system==null)throw new ObjectDisposedException(nameof(Session));if(runtime==null)throw new ArgumentNullException(nameof(runtime));if(Runtime!=null&&!ReferenceEquals(Runtime,runtime))throw new InvalidOperationException("攻略runtimeが重複しています");runtime.Claim(this);Runtime=runtime; }
            internal Session(GameSystem system, object owner, SaveManager.Context storage) { this.system = system; Owner = owner; Storage = storage; }
            public void Bind(DungeonBattleManager dungeon, IInputProvider input) { if (system == null) throw new ObjectDisposedException(nameof(Session)); Dungeon = dungeon; Input = input; }
            public void Dispose()
            {
                var prior = system; if (prior == null) return;
                system = null; Runtime?.Dispose(); Runtime=null; Dungeon = null; Input = null;
                var owner = Owner; Owner = null; prior.EndSession(this, owner);
            }
        }
        public SaveManager Saves { get; }
        public ResourceManager Resources { get; }
        public SoundManager Sounds { get; }
        public ViewManager Views { get; }
        public DialogManager Dialogs { get; }
        public GameHostManager Hosts { get; }
        public CameraRuntimeManager Cameras { get; }
        public UIRuntimeManager UI { get; }
        public Session CurrentSession { get; private set; }
        public OutsideExplorationSession OutsideExploration { get; private set; } = new OutsideExplorationSession();
        public OutsideExplorationRuntime OutsideRuntime {get;private set;}
        public string OutsideProgressGeneration {get;private set;}
        private bool disposed;
        public GameSystem(SaveManager saves, ResourceManager resources, SoundManager sounds, ViewManager views, DialogManager dialogs, UIRuntimeManager ui = null, CameraRuntimeManager cameras = null)
        { OutsideRuntime=new OutsideExplorationRuntime(OutsideExploration); Cameras = cameras; UI = ui; Saves = saves; Resources = resources; Sounds = sounds; Views = views; Dialogs = dialogs; Hosts = new GameHostManager(resources,()=>CurrentSession!=null); Resources.RetainGeneratedSprites(); }
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
        /// <summary>新規ゲームの保存成功後だけ呼ぶ。通常のhost/View再入では外界を保持する。</summary>
        public bool ResetOutsideForNewProgress(string generation)
        {
            if(disposed)throw new ObjectDisposedException(nameof(GameSystem));
            if(!Guid.TryParseExact(generation,"N",out _))throw new ArgumentException("保存済みの新規進行世代が必要です",nameof(generation));
            if(OutsideProgressGeneration==generation)return false;
            var next=new OutsideExplorationSession();
            var runtime=new OutsideExplorationRuntime(next);
            OutsideRuntime.Dispose();OutsideExploration.Dispose();
            OutsideExploration=next;OutsideRuntime=runtime;OutsideProgressGeneration=generation;
            return true;
        }
        private void EndSession(Session session, object owner)
        {
            if (!ReferenceEquals(CurrentSession, session)) return;
            Views.UnregisterOwner(owner); UI?.ReleaseContent(owner); Sounds.EndSession(owner); CurrentSession = null;
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            Hosts.Dispose(); CurrentSession?.Dispose(); OutsideRuntime.Dispose(); OutsideExploration.Dispose();
            // 再生中の音が参照する素材より先に、音声所有物を解放する。
            Dialogs.Dispose(); Views.Dispose(); UI?.Dispose(); Cameras?.Dispose(); Sounds.Dispose(); Resources.Dispose();
        }
    }
}
