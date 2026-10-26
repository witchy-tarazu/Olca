using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Customization
{
    /// <summary>保存に触れない編集下書き。失敗候補は採用せず、取消はこの実体を捨てるだけ。</summary>
    public sealed class CustomizationEditDraft
    {
        public CustomizationCatalog Catalog { get; }
        public PartsBoardDefinition Board { get; }
        public long Revision { get; }
        private readonly CustomizationProgress owned;
        private readonly List<string> accessories;
        private List<PartPlacement> placements;
        public IReadOnlyList<string> Accessories => accessories.ToArray();
        public IReadOnlyList<PartPlacement> Placements => placements.ToArray();
        public IReadOnlyList<string> OwnedAccessories => owned.OwnedAccessoryIds.ToArray();
        public IReadOnlyList<CustomizationItemCount> OwnedParts => owned.OwnedParts.Select(v=>v.Copy()).ToArray();
        public CustomizationEditDraft(CustomizationCatalog catalog, CustomizationProgress progress, long revision)
        {
            Catalog=catalog??throw new ArgumentNullException(nameof(catalog)); Revision=revision;
            CustomizationProgress.Validate(progress); owned=(progress??new CustomizationProgress()).Copy();
            if(owned.BoardId!=null&&owned.BoardId!=catalog.BoardId)throw new ArgumentException("保存と盤面定義が一致しません");
            Board=owned.BoardId==null?catalog.Board:new PartsBoardDefinition(catalog.Board.Width,catalog.Board.Height,owned.UnlockedCells.Select(v=>new Vector2Int(v.X,v.Y)));
            accessories=owned.EquippedAccessoryIds.ToList(); placements=owned.Placements.Select(v=>v.ToPlacement()).ToList();
            if(accessories.Count>catalog.AccessorySlots||accessories.Any(id=>!catalog.Accessories.Contains(id))||!Validate(placements,out _,out _))throw new ArgumentException("保存と装備定義が一致しません");
        }
        public bool TryToggleAccessory(string id)
        {
            if(accessories.Contains(id)){accessories.Remove(id);return true;}
            if(!owned.OwnedAccessoryIds.Contains(id)||!Catalog.Accessories.Contains(id)||accessories.Count>=Catalog.AccessorySlots)return false;
            accessories.Add(id);return true;
        }
        private bool Validate(IEnumerable<PartPlacement> values,out PartsLayout layout,out PartsLayoutError error)
            =>PartsLayout.TryCreate(Board,Catalog.Parts,owned.OwnedParts.ToDictionary(v=>v.Id,v=>v.Count),values,out layout,out error);
        public PartsLayout Layout { get {Validate(placements,out var layout,out _);return layout;} }
        public bool TryPlace(string instanceId,string partId,Vector2Int anchor,PartRotation rotation,out PartsLayoutError error)
        {
            var candidate=placements.Where(v=>v.InstanceId!=instanceId).ToList();
            candidate.Add(new PartPlacement(instanceId,partId,anchor,rotation));
            if(!Validate(candidate,out _,out error))return false;
            placements=candidate;return true;
        }
        public bool Remove(string instanceId)=>placements.RemoveAll(v=>v.InstanceId==instanceId)>0;
    }
}
