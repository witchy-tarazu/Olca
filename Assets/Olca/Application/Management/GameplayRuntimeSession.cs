using System;
using Olca.Core.Logic.Integration;
using Olca.Unity;

namespace Olca.Application.Management
{
    /// <summary>表示から独立した攻略の所有範囲。新たなUpdateや時計は作らず、既存時計を引き受ける。</summary>
    public sealed class GameplayRuntimeSession : IDisposable
    {
        private object driver;
        private object owner;
        private bool stopped, pumping, ownsLoop = true;
        public UnityLogicalLoop Loop { get; }
        public UnityInputProvider Input { get; private set; }
        public UnityRandomProvider Random { get; private set; }
        public UnityLogger Logger { get; private set; }
        public DungeonBattleManager Dungeon { get; private set; }
        public bool IsStopped => stopped;
        public GameplayRuntimeSession(object driver, UnityLogicalLoop loop)
        { this.driver = driver ?? throw new ArgumentNullException(nameof(driver)); Loop = loop ?? throw new ArgumentNullException(nameof(loop)); }
        internal void Claim(object candidate)
        { if (stopped) throw new ObjectDisposedException(nameof(GameplayRuntimeSession)); if (owner != null && !ReferenceEquals(owner,candidate)) throw new InvalidOperationException("攻略runtimeの所有者が重複しています"); owner=candidate; }
        public void Initialize(int? seed)
        {
            if (stopped) throw new ObjectDisposedException(nameof(GameplayRuntimeSession));
            if (Input != null) throw new InvalidOperationException("攻略入力は既に生成されています");
            Random = seed.HasValue ? new UnityRandomProvider(seed.Value) : new UnityRandomProvider();
            Logger = new UnityLogger(); Input = new UnityInputProvider();
        }
        public DungeonBattleManager CreateDungeon(Func<DungeonBattleManager> factory)
        {
            if (stopped || Input == null) throw new InvalidOperationException("攻略runtimeが開始できません");
            if (Dungeon != null) throw new InvalidOperationException("攻略Coreが重複しています");
            return Dungeon = (factory ?? throw new ArgumentNullException(nameof(factory)))() ?? throw new InvalidOperationException("攻略Coreがありません");
        }
        public void ReplaceDungeon(DungeonBattleManager dungeon)
        { if (stopped || Input == null) throw new InvalidOperationException("攻略runtimeは停止中です"); Dungeon=dungeon??throw new ArgumentNullException(nameof(dungeon)); }
        public void Pump(object caller,bool active,Action<long,int,LogicalInput> tick,Func<bool> canAdvance,Func<bool> advanceHeldTime)
        {
            if (stopped) return;
            if (Input==null || !ReferenceEquals(caller,driver) || pumping) throw new InvalidOperationException("攻略runtimeの更新所有者が不正です");
            pumping=true;
            try { Loop.PumpTimestamped(active,Input.CaptureTimestamped(),tick,canAdvance,advanceHeldTime); }
            finally { pumping=false; }
        }
        // 起動失敗は既存Controllerの再試行用時計へ所有を戻す。通常終了やView再生成では使わない。
        public void ReturnLoopForInitializationRetry() { ownsLoop=false; Dispose(); }
        public void Dispose()
        {
            if (stopped) return; stopped=true;
            if(Input!=null){Input.Dispose();Input.GamepadGameplayActive=null;Input.GamepadContext=null;Input.GamepadPauseAvailable=null;}
            if(ownsLoop) Loop.Dispose();
            Dungeon?.SuspendForCheckpoint(); Dungeon=null;Input=null;owner=null;driver=null;Random=null;Logger=null;
        }
    }
}

namespace Olca.Application.Management
{
    /// <summary>外界Sessionの単一時計。hostは借用して駆動し、View終了では時計/位置を破棄しない。</summary>
    public sealed class OutsideExplorationRuntime : System.IDisposable
    {
        private readonly Olca.Core.Logic.Integration.OutsideExplorationSession session;
        private Olca.Unity.UnityLogicalLoop loop;
        private object driver;private Olca.Unity.UnityInputProvider input;
        private bool disposed,active,armed,pumping,covered,resumeSelection;
        private readonly Olca.Core.Logic.Integration.OutsideSelectionRepeat selection=new Olca.Core.Logic.Integration.OutsideSelectionRepeat();
        public void SetCovered(object owner,bool value){if(!ReferenceEquals(driver,owner)||covered==value)return;if(covered&&!value)resumeSelection=true;covered=value;}
        public long Frame=>loop?.Clock.Frame??0;
        public OutsideExplorationRuntime(Olca.Core.Logic.Integration.OutsideExplorationSession session){this.session=session??throw new System.ArgumentNullException(nameof(session));}
        public void Attach(object owner,Olca.Unity.UnityInputProvider provider)
        {
            if(disposed)throw new System.ObjectDisposedException(nameof(OutsideExplorationRuntime));
            if(owner==null||provider==null)throw new System.ArgumentNullException(nameof(owner));
            if(driver!=null&&!ReferenceEquals(driver,owner))throw new System.InvalidOperationException("外界driverが重複しています");
            if(loop==null)loop=new Olca.Unity.UnityLogicalLoop(true,()=>active&&session.IsPresented&&!session.IsEnded&&session.Player?.IsDead!=true);driver=owner;input=provider;covered=resumeSelection=false;selection.Reset();Reset();
        }
        private void Reset(){active=false;armed=false;loop?.Observe(false);loop?.ReleaseInput();input?.CaptureTimestamped();input?.QueueSnapshot();}
        public void Detach(object owner){if(!ReferenceEquals(driver,owner))return;Reset();selection.Reset();covered=resumeSelection=false;driver=null;input=null;}
        public void Pump(object owner,bool canPlay)
        {
            if(disposed||!ReferenceEquals(driver,owner)||pumping)return;
            pumping=true;
            try{
                if(!canPlay||session.Player?.IsDead==true){if(active)Reset();loop.Observe(false);input.CaptureTimestamped();return;}
                if(!active){Reset();selection.Reset(resumeSelection);resumeSelection=false;active=true;}
                // 時刻順/floor/未来保留は既存loopだけが判断する。未採用イベントでneutralを解除しない。
                var samples=new System.Collections.Generic.List<Olca.Unity.TimestampedLogicalInput>();
                foreach(var entry in input.CaptureTimestamped()){
                    if(entry.Input==null){samples.Add(entry);continue;}var value=entry.Input.Copy();var filtered=value.Copy();if(filtered.HasSelectionSample)filtered.AnyKey=filtered.AnyKeyExceptSelection;
                    samples.Add(new Olca.Unity.TimestampedLogicalInput(entry.Timestamp,filtered,entry.Command));long time=entry.Timestamp;
                    samples.Add(new Olca.Unity.TimestampedLogicalInput(time,null,()=>selection.Queue(time,value.SelectionPreviousHeld,value.SelectionNextHeld,value.SelectionPreviousDown,value.SelectionNextDown,value.SelectionSnapshot,value.AnyKey,value.SelectionPreviousAt,value.SelectionNextAt)));
                }
                loop.PumpTimestamped(true,samples,(frame,ms,value)=>{
                    selection.AdvanceFrame(session.Cards,!Olca.Unity.GameplayPause.InputBlocked);
                    if(!armed&&value.HasInputSample&&!value.AnyKey&&System.Math.Abs(value.Horizontal)<.01f)armed=true;
                    bool accept=armed&&!Olca.Unity.GameplayPause.InputBlocked;
                    session.Core.TickFrame(accept?value.Horizontal:0,accept&&value.JumpButtonDown,accept&&value.JumpButton,accept&&value.DashButtonDown,accept&&value.DashButton);
                    session.AdvancePlayerFrame();
                },()=>session.IsPresented&&!session.IsEnded&&session.Player?.IsDead!=true);
            }finally{pumping=false;}
        }
        public void Dispose(){if(disposed)return;disposed=true;Reset();driver=null;input=null;loop?.Dispose();}
    }
}
