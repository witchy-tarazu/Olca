using System;
using System.Linq;
using System.Collections.Generic;
using Olca.Core.Domain;
namespace Olca.Core.Logic.Dungeon
{
    public sealed class PortableTuningCondition
    {
        public CardCategory? Category;public string Series,DefinitionHash;public ElementType? Element;
        public static PortableTuningCondition Capture(TuningTargetCondition c)=>c==null?null:new PortableTuningCondition{Category=c.Category,Series=c.SeriesName,Element=c.Element,DefinitionHash=c.DefinitionHash};
        public TuningTargetCondition Restore()=>new TuningTargetCondition(Category,Series==null?null:new CardSeries(Series),Element,DefinitionHash);
    }
    public sealed class PortableTuningStage { public int Stage;public CardModifier Modifier; }
    public sealed class PortableTuningEffect
    {
        public string Id;public int MaxStage;public PortableTuningCondition Condition;public PortableTuningStage[] Stages;
        internal static PortableTuningEffect Capture(TuningEffectDefinition e)=>new PortableTuningEffect{Id=e.Id,MaxStage=e.MaxStage,Condition=PortableTuningCondition.Capture(e.TargetCondition),Stages=e.PortableStages.OrderBy(p=>p.Key).Select(p=>new PortableTuningStage{Stage=p.Key,Modifier=p.Value?.Clone()}).ToArray()};
        internal TuningEffectDefinition Restore(){if(string.IsNullOrWhiteSpace(Id)||MaxStage<1||Stages==null||Stages.Any(s=>s==null||s.Stage<1||s.Stage>MaxStage||s.Modifier==null)||Stages.Select(s=>s.Stage).Distinct().Count()!=Stages.Length)throw new ArgumentException("調律段階定義が不正です");return new TuningEffectDefinition(Id,MaxStage,Stages.ToDictionary(s=>s.Stage,s=>s.Modifier.Clone()),Condition?.Restore());}
    }
    /// <summary>欠落段階を補完せず、登録値と対象制限をそのまま運ぶ。</summary>
    public sealed class PortableTuningGrant
    {
        public string Key,Hash,Id;public int FixedCostAdjustment;public bool ZeroCost;public PortableTuningCondition Condition;public PortableTuningEffect[] Effects;
        public static PortableTuningGrant Capture(string key,TuningGrantDefinition g)
        {if(g==null)throw new ArgumentNullException(nameof(g));return new PortableTuningGrant{Key=key,Hash=CheckpointValues.GrantHash(g),Id=g.Id,FixedCostAdjustment=g.FixedCostAdjustment,ZeroCost=g.ZeroCost,Condition=PortableTuningCondition.Capture(g.TargetCondition),Effects=g.Effects.Select(PortableTuningEffect.Capture).ToArray()};}
        public TuningGrantDefinition Restore()
        {if(string.IsNullOrWhiteSpace(Key)||string.IsNullOrWhiteSpace(Id)||Effects==null||Effects.Any(e=>e==null))throw new ArgumentException("調律付与定義が欠損しています");var g=new TuningGrantDefinition(Id,Effects.Select(e=>e.Restore()),FixedCostAdjustment,ZeroCost,Condition?.Restore());if(CheckpointValues.GrantHash(g)!=Hash)throw new ArgumentException("調律付与定義hashが一致しません");return g;}
    }
}
