using System;
using System.Linq;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
namespace Olca.Core.Logic.Dungeon
{
    public sealed class PortableFusionCard { public string Card,Family,Series;public int Level; }
    public sealed class PortableNormalFusion { public string A,B,Series;public CardRarity Regulation; }
    public sealed class PortableSpecialFusion { public string Id,A,B,Result; }
    public sealed class PortableExceedRule { public string Source,Result; }
    public sealed class PortableTuningRule { public string Material;public PortableTuningGrant Grant; }
    /// <summary>正規の登録表から変換する値カタログ。判定・計算は既存加工Systemへ委譲する。</summary>
    public sealed class PortableProcessingCatalog
    {
        public int Version=1;public string Id,Revision,Hash;
        public PortableCardDefinition[] Cards;public PortableFusionCard[] FusionCards;public PortableNormalFusion[] Normal;public PortableSpecialFusion[] Special;
        public PortableExceedRule[] Exceed;public PortableTuningRule[] Tuning;
        private static string Key(Card c)=>CollectionDisplay.DefinitionHash(c.CreateUntunedDefinitionCopy());
        public static PortableProcessingCatalog Capture(string id,string revision,IEnumerable<Card> definitions,ICardFactory factory,FusionDefinitionCatalog fusion,Func<Card,TuningGrantDefinition> tuning)
        {
            if(factory==null||fusion==null||definitions==null)throw new ArgumentException("正規加工定義が必要です");
            var cards=new Dictionary<string,Card>();void Add(Card c){if(c==null)throw new ArgumentException("加工参照先が欠損しています");cards[Key(c)]=c.CreateUntunedDefinitionCopy();}
            foreach(var c in definitions)Add(c);foreach(var e in fusion.PortableCards)Add(e.Card);
            var exceed=new List<PortableExceedRule>();
            // 上位定義として公開済みのものだけ。未定義の計算fallbackは新しい製品定義にしない。
            foreach(var c in cards.Values.ToArray())if(c.ExceedLevel<ExceedLevel.Lv3&&factory is IExceededCardDefinitionProvider provider&&provider.HasExceededDefinition(c,(ExceedLevel)((int)c.ExceedLevel+1))){var output=factory.CreateExceeded(c,(ExceedLevel)((int)c.ExceedLevel+1));Add(output);exceed.Add(new PortableExceedRule{Source=Key(c),Result=Key(output)});}
            var grants=cards.Values.Select(c=>(Card:c,Grant:tuning?.Invoke(c))).Where(p=>p.Grant!=null).Select(p=>new PortableTuningRule{Material=Key(p.Card),Grant=PortableTuningGrant.Capture(p.Grant.Id,p.Grant)}).ToArray();
            var result=new PortableProcessingCatalog{Id=id,Revision=revision,Cards=cards.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>PortableCardDefinition.Capture(p.Key,p.Value)).ToArray(),
                FusionCards=fusion.PortableCards.Select(e=>new PortableFusionCard{Card=Key(e.Card),Family=e.Family,Series=e.Series,Level=e.Level}).OrderBy(e=>e.Card,StringComparer.Ordinal).ToArray(),
                Normal=fusion.PortableNormal.Select(e=>new PortableNormalFusion{A=e.A,B=e.B,Series=e.Series,Regulation=e.Regulation}).OrderBy(e=>e.A,StringComparer.Ordinal).ThenBy(e=>e.B,StringComparer.Ordinal).ThenBy(e=>e.Regulation).ToArray(),
                Special=fusion.PortableSpecial.Select(e=>new PortableSpecialFusion{Id=e.Id,A=Key(e.A),B=Key(e.B),Result=Key(e.Result)}).OrderBy(e=>e.Id,StringComparer.Ordinal).ToArray(),Exceed=exceed.OrderBy(e=>e.Source,StringComparer.Ordinal).ToArray(),Tuning=grants.OrderBy(e=>e.Material,StringComparer.Ordinal).ToArray()};
            result.Hash=result.ComputeHash();result.Restore();return result;
        }
        private string ComputeHash()=>CheckpointValues.Hash(w=>{
            w.Write(Version);w.Write(Id);w.Write(Revision);
            foreach(var c in Cards){w.Write(c.Key);w.Write(c.Hash);}foreach(var c in FusionCards){w.Write(c.Card);w.Write(c.Family);w.Write(c.Series);w.Write(c.Level);}
            foreach(var r in Normal){w.Write(r.A);w.Write(r.B);w.Write(r.Series);w.Write((int)r.Regulation);}foreach(var r in Special){w.Write(r.Id);w.Write(r.A);w.Write(r.B);w.Write(r.Result);}
            foreach(var r in Exceed){w.Write(r.Source);w.Write(r.Result);}foreach(var r in Tuning){w.Write(r.Material);w.Write(r.Grant.Key);w.Write(r.Grant.Hash);}
        });
        public PortableProcessingRuntime Restore()
        {
            if(Version!=1||string.IsNullOrWhiteSpace(Id)||string.IsNullOrWhiteSpace(Revision)||Cards==null||FusionCards==null||Normal==null||Special==null||Exceed==null||Tuning==null)throw new ArgumentException("加工カタログの版または内容が欠損しています");
            if(Cards.Any(c=>c==null)||FusionCards.Any(c=>c==null)||Normal.Any(r=>r==null)||Special.Any(r=>r==null)||Exceed.Any(r=>r==null)||Tuning.Any(r=>r==null||r.Grant==null))throw new ArgumentException("加工定義が欠損しています");
            var cards=Cards.ToDictionary(c=>c.Key,c=>c.Restore());if(Cards.Any(c=>Key(cards[c.Key])!=c.Key))throw new ArgumentException("加工定義キーが元定義と一致しません");
            Card Resolve(string key)=>key!=null&&cards.TryGetValue(key,out var c)?c:throw new ArgumentException("加工参照先が欠損しています");
            var fusion=new FusionDefinitionCatalog();foreach(var c in FusionCards)fusion.RegisterCard(Resolve(c.Card),c.Family,c.Series,c.Level);
            foreach(var r in Normal){if(!FusionCards.Any(c=>c.Family==r.A)||!FusionCards.Any(c=>c.Family==r.B)||!FusionCards.Any(c=>c.Series==r.Series))throw new ArgumentException("通常融合先が欠損しています");fusion.RegisterNormal(r.A,r.B,r.Regulation,r.Series);}
            if(Special.Select(r=>r.Id).Distinct().Count()!=Special.Length)throw new ArgumentException("特殊融合IDが重複しています");foreach(var r in Special)fusion.RegisterSpecial(r.Id,Resolve(r.A),Resolve(r.B),Resolve(r.Result));
            foreach(var r in Exceed){var a=Resolve(r.Source);var b=Resolve(r.Result);if((int)b.ExceedLevel!=(int)a.ExceedLevel+1)throw new ArgumentException("超越段階が不正です");}
            foreach(var r in Tuning){Resolve(r.Material);r.Grant.Restore();}
            if(ComputeHash()!=Hash)throw new ArgumentException("加工カタログhashが一致しません");
            return new PortableProcessingRuntime(cards,Exceed,Tuning,fusion);
        }
    }
    public sealed class PortableProcessingRuntime : ICardFactory,IExceededCardDefinitionProvider,IFusionRecipeProvider
    {
        private readonly Dictionary<string,Card> cards;private readonly Dictionary<string,string> exceed;private readonly Dictionary<string,TuningGrantDefinition> tuning;
        public FusionSystem Fusion { get; }public ExceedSystem Exceed {get;}public TuningSystem Tuning {get;}
        internal PortableProcessingRuntime(Dictionary<string,Card> cards,PortableExceedRule[] upgrades,PortableTuningRule[] grants,FusionDefinitionCatalog fusion)
        {this.cards=cards;exceed=upgrades.ToDictionary(r=>r.Source,r=>r.Result);tuning=grants.ToDictionary(r=>r.Material,r=>r.Grant.Restore());recipes=fusion.CollectionRecipes;Fusion=new FusionSystem(this,this,fusion);Exceed=new ExceedSystem(this);Tuning=new TuningSystem(GetGrant);}
        private static string Key(Card c)=>CollectionDisplay.DefinitionHash(c.CreateUntunedDefinitionCopy());
        public TuningGrantDefinition GetGrant(Card c)=>c!=null&&tuning.TryGetValue(Key(c),out var g)?g:null;
        public Card CreateById(string id){var found=cards.Values.Where(c=>c.Id==id&&c.ExceedLevel==ExceedLevel.Normal).ToArray();if(found.Length>1)throw new ArgumentException("加工結果をIDだけでは解決できません");return found.SingleOrDefault()?.Clone();}
        public bool HasExceededDefinition(Card c,ExceedLevel level)=>c!=null&&level==(ExceedLevel)((int)c.ExceedLevel+1)&&exceed.ContainsKey(Key(c));
        public Card CreateExceeded(Card c,ExceedLevel level)=>HasExceededDefinition(c,level)?cards[exceed[Key(c)]].Clone():null;
        public string FindResultCardId(IReadOnlyList<string> ids)=>null;
        public bool HasRecipeContaining(IReadOnlyList<string> ids)=>FusionRecipes.Any(r=>Contains(r.Materials,ids));
        private IReadOnlyList<CollectionRecipe> FusionRecipes=>recipes;
        private IReadOnlyList<CollectionRecipe> recipes;
        private static bool Contains(IReadOnlyList<string> all,IReadOnlyList<string> requested){if(requested==null||requested.Count==0)return false;var left=all.ToList();return requested.All(id=>left.Remove(id));}
    }
}
