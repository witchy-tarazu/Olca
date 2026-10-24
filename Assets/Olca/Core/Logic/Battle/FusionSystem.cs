using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 融合システム
    /// 複数のカードをレシピに基づいて合成し、新しいカードを生成する
    /// </summary>
    public class FusionSystem
    {
        private readonly IFusionRecipeProvider recipeProvider;
        private readonly ICardFactory cardFactory;
        private readonly FusionDefinitionCatalog definitions;

        /// <param name="recipeProvider">融合レシピ提供者</param>
        /// <param name="cardFactory">カード生成ファクトリ</param>
        public FusionSystem(IFusionRecipeProvider recipeProvider, ICardFactory cardFactory, FusionDefinitionCatalog definitions = null)
        {
            this.recipeProvider = recipeProvider ?? throw new ArgumentNullException(nameof(recipeProvider));
            this.cardFactory = cardFactory ?? throw new ArgumentNullException(nameof(cardFactory));
            this.definitions = definitions ?? (recipeProvider as IFusionDefinitionCatalogProvider)?.FusionDefinitions;
        }

        /// <summary>通常選択・直接実行で共通の素材役割の排他条件。</summary>
        public static string MaterialRoleError(IReadOnlyList<Card> cards)
        {
            if(cards==null||cards.Count<2||cards.Any(c=>c==null))return null;
            int count=cards.Count(TuningSystem.IsMaterial);
            if(count>0&&count!=cards.Count)return "調律素材とそれ以外の融合はできません";
            return null;
        }

        /// <summary>
        /// 融合可能かどうかを検証する
        /// </summary>
        public FusionValidationResult Validate(IReadOnlyList<Card> materials)
        {
            if (materials == null || materials.Count != 2)
                return FusionValidationResult.Fail("素材は2枚固定です（2枚以上の選択はできません）");
            if (materials.Any(c => c == null))
                return FusionValidationResult.Fail("カードにnullが含まれています");

            var materialError=MaterialRoleError(materials);
            if(materialError!=null)return FusionValidationResult.Fail(materialError);
            if(ReferenceEquals(materials[0],materials[1]))return FusionValidationResult.Fail("別々のカード2枚が必要です");
            if(definitions!=null)return definitions.Validate(materials);
            if(CollectionDisplay.DefinitionHash(materials[0].CreateUntunedDefinitionCopy())==CollectionDisplay.DefinitionHash(materials[1].CreateUntunedDefinitionCopy()))return FusionValidationResult.Fail("同じカード2枚は超越で扱います");
            var ids = materials.Select(c => c.Id).ToList();
            string resultId = recipeProvider.FindResultCardId(ids);
            if (resultId == null)
                return FusionValidationResult.Fail("この組み合わせの融合レシピが存在しません");

            return FusionValidationResult.Success(resultId);
        }

        /// <summary>
        /// 部分的な素材リストに対して互換性のあるレシピが存在するか判定する。
        /// カスタマイズモードでの選択可能カード絞り込みに使用する。
        /// </summary>
        public bool HasCompatibleRecipe(IReadOnlyList<string> partialIds)
            => recipeProvider.HasRecipeContaining(partialIds);

        /// <summary>
        /// 融合を実行し、生成カードを返す
        /// </summary>
        /// <param name="materials">融合素材カードリスト（2枚以上）</param>
        /// <returns>融合結果</returns>
        public FusionResult Execute(IReadOnlyList<Card> materials)
        {
            var validation = Validate(materials);
            if (!validation.IsValid)
                return FusionResult.Fail(validation.ErrorMessage);

            var resultCard = validation.CreateResult() ?? cardFactory.CreateById(validation.ResultCardId);
            if (resultCard == null)
                return FusionResult.Fail("融合後カードの生成に失敗しました");

            return FusionResult.Success(resultCard);
        }
    }

    /// <summary>
    /// 融合検証結果
    /// </summary>
    public readonly struct FusionValidationResult
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }
        public string ResultCardId { get; }
        private readonly Card resultDefinition;
        internal Card CreateResult()=>resultDefinition?.Clone();

        private FusionValidationResult(bool isValid, string resultCardId, string errorMessage, Card resultDefinition=null)
        {
            this.resultDefinition=resultDefinition;
            IsValid = isValid;
            ResultCardId = resultCardId;
            ErrorMessage = errorMessage;
        }

        public static FusionValidationResult Success(string resultCardId) =>
            new FusionValidationResult(true, resultCardId, null);

        public static FusionValidationResult Success(Card card)=>new FusionValidationResult(true,card.Id,null,card.CreateUntunedDefinitionCopy());

        public static FusionValidationResult Fail(string message) =>
            new FusionValidationResult(false, null, message);
    }

    /// <summary>
    /// 融合実行結果
    /// </summary>
    public readonly struct FusionResult
    {
        public bool IsSuccess { get; }
        public string ErrorMessage { get; }
        public Card ResultCard { get; }

        private FusionResult(bool isSuccess, Card resultCard, string errorMessage)
        {
            IsSuccess = isSuccess;
            ResultCard = resultCard;
            ErrorMessage = errorMessage;
        }

        public static FusionResult Success(Card card) =>
            new FusionResult(true, card, null);

        public static FusionResult Fail(string message) =>
            new FusionResult(false, null, message);
    }
}

namespace Olca.Core.Logic.Battle
{
    public interface IFusionDefinitionCatalogProvider { FusionDefinitionCatalog FusionDefinitions { get; } }

    /// <summary>保存形式を変更せず、元定義ごとの系統・シリーズ・レベルを明示登録する融合表。</summary>
    public sealed class FusionDefinitionCatalog
    {
        private sealed class Entry
        {
            public Card Card; public string Family,Series; public int Level; public CardRarity Regulation;
        }
        private readonly Dictionary<string,Entry> cards=new Dictionary<string,Entry>();
        private readonly Dictionary<string,string> normal=new Dictionary<string,string>();
        private readonly List<(string A,string B,CardRarity Regulation,string Series)> normalDefinitions=new List<(string,string,CardRarity,string)>();
        private readonly List<(string Id,Card A,Card B,Card Result)> specialDefinitions=new List<(string,Card,Card,Card)>();
        internal IEnumerable<(Card Card,string Family,string Series,int Level)> PortableCards=>cards.Values.Select(e=>(e.Card.Clone(),e.Family,e.Series,e.Level));
        internal IEnumerable<(string A,string B,CardRarity Regulation,string Series)> PortableNormal=>normalDefinitions;
        internal IEnumerable<(string Id,Card A,Card B,Card Result)> PortableSpecial=>specialDefinitions.Select(e=>(e.Id,e.A.Clone(),e.B.Clone(),e.Result.Clone()));
        private readonly Dictionary<string,(string id,Card result)> special=new Dictionary<string,(string,Card)>();
        private static string Hash(Card card)=>CollectionDisplay.DefinitionHash(card.CreateUntunedDefinitionCopy());
        private static string Pair(string a,string b)=>string.CompareOrdinal(a,b)<0?a.Length+":"+a+b.Length+":"+b:b.Length+":"+b+a.Length+":"+a;
        private static string Rule(string a,string b,CardRarity regulation)=>Pair(a,b)+"/"+(int)regulation;
        public void RegisterCard(Card card,string family,string series,int level)
        {
            if(card==null||string.IsNullOrWhiteSpace(family)||string.IsNullOrWhiteSpace(series)||level<0||level>3||level!=(int)card.ExceedLevel||!Enum.IsDefined(typeof(CardRarity),card.Rarity))throw new ArgumentException("融合カード分類が不正です");
            string key=Hash(card);if(cards.TryGetValue(key,out var old)) { if(old.Family!=family||old.Series!=series||old.Level!=level)throw new ArgumentException("同一定義の分類が競合しています");return; }
            if(cards.Values.Any(c=>c.Series==series&&(c.Regulation!=card.Rarity||c.Level==level)))throw new ArgumentException("シリーズのレギュレーションまたはレベル定義が競合しています");
            cards.Add(key,new Entry{Card=card.CreateUntunedDefinitionCopy(),Family=family,Series=series,Level=level,Regulation=card.Rarity});
        }
        public void RegisterNormal(string familyA,string familyB,CardRarity regulation,string resultSeries)
        {
            if(string.IsNullOrWhiteSpace(familyA)||string.IsNullOrWhiteSpace(familyB)||string.IsNullOrWhiteSpace(resultSeries)||!Enum.IsDefined(typeof(CardRarity),regulation))throw new ArgumentException("通常融合表が不正です");
            string key=Rule(familyA,familyB,regulation);if(normal.ContainsKey(key))throw new ArgumentException("通常融合表が重複しています");normal.Add(key,resultSeries);normalDefinitions.Add((familyA,familyB,regulation,resultSeries));
        }
        public void RegisterSpecial(string recipeId,Card a,Card b,Card result)
        {
            if(string.IsNullOrWhiteSpace(recipeId)||a==null||b==null||result==null||!cards.ContainsKey(Hash(a))||!cards.ContainsKey(Hash(b))||!cards.ContainsKey(Hash(result)))throw new ArgumentException("特殊融合の定義が不足しています");
            string key=Pair(Hash(a),Hash(b));if(special.ContainsKey(key))throw new ArgumentException("特殊融合が重複しています");special.Add(key,(recipeId,result.CreateUntunedDefinitionCopy()));specialDefinitions.Add((recipeId,a.CreateUntunedDefinitionCopy(),b.CreateUntunedDefinitionCopy(),result.CreateUntunedDefinitionCopy()));
        }
        public FusionValidationResult Validate(IReadOnlyList<Card> materials)
        {
            if(materials==null||materials.Count!=2||materials.Any(c=>c==null))return FusionValidationResult.Fail("素材は2枚固定です");
            var role=FusionSystem.MaterialRoleError(materials);if(role!=null)return FusionValidationResult.Fail(role);
            if(!cards.TryGetValue(Hash(materials[0]),out var a)||!cards.TryGetValue(Hash(materials[1]),out var b))return FusionValidationResult.Fail("素材の系統・シリーズ定義が未登録です");
            if(a.Series==b.Series)return FusionValidationResult.Fail("同じシリーズ同士は融合できません");
            if(special.TryGetValue(Pair(Hash(materials[0]),Hash(materials[1])),out var recipe))return FusionValidationResult.Success(recipe.result);
            var regulation=(CardRarity)Math.Max((int)a.Regulation,(int)b.Regulation);int level=Math.Max(a.Level,b.Level);
            if(!normal.TryGetValue(Rule(a.Family,b.Family,regulation),out var series))return FusionValidationResult.Fail("この系統とレギュレーションの通常融合レシピが未登録です");
            var result=cards.Values.SingleOrDefault(c=>c.Series==series&&c.Level==level&&c.Regulation==regulation);
            return result==null?FusionValidationResult.Fail("通常融合先のシリーズ・レベル定義が未登録です"):FusionValidationResult.Success(result.Card);
        }
        public IReadOnlyList<CollectionRecipe> CollectionRecipes
        {
            get
            {
                var result=new List<CollectionRecipe>();var entries=cards.Values.ToArray();
                for(int i=0;i<entries.Length;i++)for(int j=i+1;j<entries.Length;j++) {
                    var a=entries[i].Card;var b=entries[j].Card;var found=Validate(new[]{a,b});if(!found.IsValid)continue;var output=found.CreateResult();
                    string pair=Pair(Hash(a),Hash(b));string id=special.TryGetValue(pair,out var recipe)?recipe.id:"normal/v1/"+pair+"/"+Hash(output);
                    result.Add(new CollectionRecipe(id,new[]{a.Id,b.Id},output.Id,output.ExceedLevel,new[]{a,b},output));
                }
                return result;
            }
        }
    }
}
