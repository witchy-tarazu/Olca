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
