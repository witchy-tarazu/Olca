using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Integration;
using Olca.Unity;
namespace Olca.Application.Management
{
    /// <summary>保存から復帰候補を読む。現在Coreの停止、採用、時計、View、保存確定は呼出側の責務。</summary>
    public sealed class DungeonCheckpointFlow
    {
        public sealed class Prepared
        {
            public DungeonBattleManager Core {get;internal set;}
            public DungeonCheckpointSession Checkpoint {get;internal set;}
            public FloorSequenceSession Route {get;internal set;}
        }
        private readonly DungeonCoreFactory factory;
        public DungeonCheckpointFlow(DungeonCoreFactory factory){this.factory=factory??throw new ArgumentNullException(nameof(factory));}
        public bool TryPrepare(DungeonCheckpointSession memory,FloorSequenceDefinitions floors,FloorSequenceFileStore floorStore,Func<IReadOnlyList<ClearReceipt>> receipts,out Prepared result,out string error)
        {
            result=null;DungeonBattleManager core;DungeonCheckpointSession checkpoint;FloorSequenceSession route=null;
            if(floors!=null){
                route=new FloorSequenceSession(floors,floorStore);
                if(!route.TryLoad(out error))return false;
                if(route.Current.Snapshot.State.Boundary==CheckpointBoundary.Terminal){error="終了済みの攻略は再開できません";return false;}
                core=factory.CreateFloor(route,floors,out checkpoint);
            } else {
                checkpoint=memory.Store is FileCheckpointStore file?new DungeonCheckpointSession(memory.Definitions,new FileCheckpointStore(memory.Definitions,file.DirectoryPath)):memory;
                if(!factory.TryRestore(checkpoint,out core,out error))return false;
            }
            if(receipts?.Invoke()?.Any(r=>r.RunId==checkpoint.RunId&&r.Outcome!=RunReturnOutcome.Cleared)==true){core.SuspendForCheckpoint();error="敗北・撤退が確定した攻略は再開できません";return false;}
            result=new Prepared{Core=core,Checkpoint=checkpoint,Route=route};error=null;return true;
        }
    }
}
