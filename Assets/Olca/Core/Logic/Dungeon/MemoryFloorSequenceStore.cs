using System;
namespace Olca.Core.Logic.Dungeon
{
    /// <summary>同じ階層セッションを純C#で検証する保存先。ゲームの遷移判定はSessionが所有する。</summary>
    public sealed class MemoryFloorSequenceStore : IFloorSequenceStore
    {
        private readonly FloorSequenceDefinitions definitions;
        private Olca.Core.Domain.FloorSequenceCheckpointDto current;
        public string ReadError=>null;
        public MemoryFloorSequenceStore(FloorSequenceDefinitions definitions){this.definitions=definitions??throw new ArgumentNullException(nameof(definitions));}
        public Olca.Core.Domain.FloorSequenceCheckpointDto Read()=>current?.Copy();
        public bool TryWrite(Olca.Core.Domain.FloorSequenceCheckpointDto data,out string error)
        {
            if(!definitions.TryRestore(data,out var restored,out error))return false;var next=restored.Snapshot;
            if(current!=null&&(current.State.RunId!=next.State.RunId||next.State.Sequence<=current.State.Sequence)){
                if(current.State.RunId==next.State.RunId&&PortableCheckpointJson.Hash(PortableCheckpointJson.Encode(current))==PortableCheckpointJson.Hash(PortableCheckpointJson.Encode(next)))return true;
                error="階層保存の攻略IDまたは世代が競合しています";return false;
            }
            current=next;return true;
        }
    }
}
