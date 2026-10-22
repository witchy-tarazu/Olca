using System;
using System.IO;
using System.Linq;
using Olca.Core.Domain;
namespace Olca.Core.Logic.Dungeon
{
    /// <summary>全階層の正規カタログと既存の保存封筒。現在階層だけへ情報を縮退させない。</summary>
    public sealed class PortableFloorRoute
    {
        public int Version=1;
        public string RouteId,Revision,RouteHash;
        public byte[] CheckpointEnvelope;
        public PortableFloorDefinition[] Floors;
        public static PortableFloorRoute Capture(FloorSequenceDefinitions definitions,byte[] checkpoint)
        {
            var result=new PortableFloorRoute{RouteId=definitions.RouteId,Revision=definitions.Revision,RouteHash=definitions.RouteHash,
                CheckpointEnvelope=(byte[])checkpoint.Clone(),Floors=Enumerable.Range(0,definitions.FloorCount).Select(i=>PortableFloorDefinition.Capture(definitions.Floor(i))).ToArray()};
            result.Restore(out _);return result;
        }
        public CheckpointDefinitions Restore(out CheckpointDto state)
        {
            if(Version!=1||Floors==null||Floors.Length==0||Floors.Any(f=>f==null)||CheckpointEnvelope==null)throw new ArgumentException("階層輸送の版または内容が不正です");
            var definitions=new FloorSequenceDefinitions(RouteId,Revision,Floors.Select(f=>f.Restore()).ToArray());
            if(definitions.RouteHash!=RouteHash)throw new ArgumentException("階層列の製品定義hashが一致しません");
            var envelope=PortableCheckpointJson.DecodeFloor(CheckpointEnvelope);
            if(!definitions.TryRestore(envelope,out var restored,out var error))throw new InvalidDataException(error);
            state=restored.State.Snapshot;
            return definitions.Floor(envelope.FloorIndex);
        }
    }
    public sealed class PortableFloorDefinition
    {
        public string Revision,MapHash,CatalogHash;
        public PortableMapDefinition Map;
        public PortableCardDefinition[] Cards;public PortableTuningGrant[] Grants;
        internal static PortableFloorDefinition Capture(CheckpointDefinitions definitions)
        {
            return new PortableFloorDefinition{Revision=definitions.Revision,MapHash=definitions.MapHash,CatalogHash=definitions.CardCatalogHash,
                Map=PortableMapDefinition.Capture(definitions.CreateInitialMap()),Grants=definitions.PortableGrants.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>PortableTuningGrant.Capture(p.Key,p.Value)).ToArray(),Cards=definitions.PortableCards.OrderBy(c=>c.Key,StringComparer.Ordinal).Select(c=>PortableCardDefinition.Capture(c.Key,c.Value)).ToArray()};
        }
        internal CheckpointDefinitions Restore()
        {
            if(Map==null||Cards==null||Cards.Any(c=>c==null))throw new ArgumentException("階層定義が欠損しています");
            var definitions=new CheckpointDefinitions(Revision,Map.Restore());
            foreach(var card in Cards)definitions.RegisterCard(card.Key,card.Restore());
            if(Grants!=null)foreach(var grant in Grants)definitions.RegisterGrant(grant.Key,grant.Restore());
            if(definitions.MapHash!=MapHash||definitions.CardCatalogHash!=CatalogHash)throw new ArgumentException("階層定義hashが一致しません");
            return definitions;
        }
    }
}
