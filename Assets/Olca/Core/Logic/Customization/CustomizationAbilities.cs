using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Olca.Core.Domain;
namespace Olca.Core.Logic.Customization
{
    /// <summary>既に規則が確定した耐性と常時フロートだけを定義する。製品値は外部登録。</summary>
    public sealed class CustomizationAbilityValue
    {
        public ElementResistances Elements { get; }
        public TimedStatusResistances Statuses { get; }
        public bool Floating { get; }
        public CustomizationAbilityValue(ElementResistances elements=null,TimedStatusResistances statuses=null,bool floating=false)
        { Elements=elements??ElementResistances.Neutral;Statuses=statuses??TimedStatusResistances.Neutral;Floating=floating; }
    }
    public sealed class CustomizationAbilitySourceDto
    {
        public string Kind,SourceId,ItemId;
        public int Fire,Water,Wind,Earth,Poison,Paralyze,Confusion,Blind;
        public bool Floating;
        public CustomizationAbilitySourceDto Copy()=>(CustomizationAbilitySourceDto)MemberwiseClone();
    }
    /// <summary>出撃時の解決入力を保存する。復帰時に現在の製品定義を再適用しない。</summary>
    public sealed class CustomizationAbilitiesDto
    {
        public int Version=1;
        public string DefinitionId;
        public CustomizationAbilitySourceDto[] Sources;
        public CustomizationAbilitiesDto Copy()=>new CustomizationAbilitiesDto{Version=Version,DefinitionId=DefinitionId,Sources=Sources?.Select(v=>v?.Copy()).ToArray()};
        public void Validate()
        {
            if(Version!=1||string.IsNullOrWhiteSpace(DefinitionId)||Sources==null||Sources.Any(v=>v==null||(v.Kind!="accessory"&&v.Kind!="part")||string.IsNullOrWhiteSpace(v.SourceId)||string.IsNullOrWhiteSpace(v.ItemId)||(v.Kind=="accessory"&&v.SourceId!=v.ItemId)||v.Poison<0||v.Paralyze<0||v.Confusion<0||v.Blind<0)||Sources.Select(v=>(v.Kind,v.SourceId)).Distinct().Count()!=Sources.Length)throw new ArgumentException("出撃能力の保存が不正です");
        }
    }
    public sealed class ResolvedCustomizationAbilities
    {
        private readonly CustomizationAbilitiesDto snapshot;
        public bool Floating=>snapshot.Sources.Any(v=>v.Floating);
        public ResolvedCustomizationAbilities(CustomizationAbilitiesDto value){if(value==null)throw new ArgumentNullException(nameof(value));value.Validate();snapshot=value.Copy();}
        public CustomizationAbilitiesDto Capture()=>snapshot.Copy();
        public ElementResistances CombineElements(ElementResistances baseline)
            =>ElementResistances.Combine(new[]{baseline}.Concat(snapshot.Sources.Select(v=>new ElementResistances(v.Fire,v.Water,v.Wind,v.Earth))).ToArray());
        public TimedStatusResistances CombineStatuses(TimedStatusResistances baseline)
            =>TimedStatusResistances.Combine(new[]{baseline}.Concat(snapshot.Sources.Select(v=>new TimedStatusResistances(v.Poison,v.Paralyze,v.Confusion,v.Blind))).ToArray());
    }
    public sealed class CustomizationAbilityCatalog
    {
        public string DefinitionId { get; }
        private readonly IReadOnlyDictionary<string,CustomizationAbilityValue> accessories,parts;
        public CustomizationAbilityCatalog(string definitionId,IReadOnlyDictionary<string,CustomizationAbilityValue> accessoryValues,IReadOnlyDictionary<string,CustomizationAbilityValue> partValues)
        {
            if(string.IsNullOrWhiteSpace(definitionId))throw new ArgumentException("能力定義IDが必要です");DefinitionId=definitionId;
            IReadOnlyDictionary<string,CustomizationAbilityValue> Copy(IReadOnlyDictionary<string,CustomizationAbilityValue> values){var copy=(values??new Dictionary<string,CustomizationAbilityValue>()).ToDictionary(v=>v.Key,v=>v.Value,StringComparer.Ordinal);if(copy.Any(v=>string.IsNullOrWhiteSpace(v.Key)||v.Value==null))throw new ArgumentException("能力の対応が不正です");return new ReadOnlyDictionary<string,CustomizationAbilityValue>(copy);}
            accessories=Copy(accessoryValues);parts=Copy(partValues);
        }
        public ResolvedCustomizationAbilities Resolve(CustomizationLoadoutSources loadout)
        {
            if(loadout==null)throw new ArgumentNullException(nameof(loadout));var values=new List<CustomizationAbilitySourceDto>();
            void Add(string kind,string source,string item,IReadOnlyDictionary<string,CustomizationAbilityValue> map){if(!map.TryGetValue(item,out var value))throw new ArgumentException("ソウビノウリョクのタイオウがミトウロクです");values.Add(new CustomizationAbilitySourceDto{Kind=kind,SourceId=source,ItemId=item,Fire=value.Elements.Fire,Water=value.Elements.Water,Wind=value.Elements.Wind,Earth=value.Elements.Earth,Poison=value.Statuses.Poison,Paralyze=value.Statuses.Paralyze,Confusion=value.Statuses.Confusion,Blind=value.Statuses.Blind,Floating=value.Floating});}
            foreach(var id in loadout.Accessories)Add("accessory",id,id,accessories);
            foreach(var part in loadout.Parts)Add("part",part.InstanceId,part.PartId,parts);
            return new ResolvedCustomizationAbilities(new CustomizationAbilitiesDto{DefinitionId=DefinitionId,Sources=values.ToArray()});
        }
    }
}
