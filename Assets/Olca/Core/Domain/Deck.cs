using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Interfaces;

namespace Olca.Core.Domain
{
    /// <summary>
    /// デッキ
    /// </summary>
    public class Deck
    {
        private readonly List<Card> cards;
        private readonly List<Card> returnedDraws = new List<Card>();
        private readonly List<Card> drawnCards; // 既にドローしたカード

        /// <summary>デッキ内のカード総数</summary>
        public int TotalCount => cards.Count;

        /// <summary>まだドローしていないカードの枚数</summary>
        public int RemainingCount => cards.Count - drawnCards.Count;

        /// <summary>すべてドローしたか</summary>
        public bool IsEmpty => RemainingCount == 0;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="initialCards">初期カードリスト</param>
        public Deck(List<Card> initialCards = null)
        {
            cards = initialCards != null ? new List<Card>(initialCards) : new List<Card>();
            drawnCards = new List<Card>();
        }

        /// <summary>
        /// カードを追加
        /// </summary>
        public void AddCard(Card card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            cards.Add(card);
        }

        /// <summary>
        /// 複数のカードを追加
        /// </summary>
        public void AddCards(IEnumerable<Card> cardsToAdd)
        {
            if (cardsToAdd == null) throw new ArgumentNullException(nameof(cardsToAdd));
            cards.AddRange(cardsToAdd);
        }

        /// <summary>指定された攻略中の個体だけを一度に削除する。入力不正なら何も変更しない。</summary>
        public bool TryRemoveCards(IReadOnlyList<Card> targets)
        {
            // 対象がこのデッキの読取Viewでも、削除中に列挙元を変更しない。
            var selected = targets?.ToArray();
            if (selected == null || selected.Length == 0 || selected.Any(c => c == null || !cards.Contains(c)) ||
                selected.Distinct().Count() != selected.Length) return false;
            foreach (var card in selected) {
                cards.Remove(card); drawnCards.Remove(card); returnedDraws.Remove(card);
            }
            return true;
        }

        /// <summary>
        /// デッキ全体からランダムにカードを破棄する（ドロー済みカードも対象）。
        /// 要求枚数が総数を超える場合は、存在する枚数まで破棄する。
        /// </summary>
        public int DiscardRandomCards(int count, IRandomProvider random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (count < 0) throw new ArgumentException("破棄枚数は0以上である必要があります", nameof(count));

            int discardCount = Math.Min(count, cards.Count);
            for (int i = 0; i < discardCount; i++)
            {
                int index = random.Range(0, cards.Count);
                Card discardedCard = cards[index];
                cards.RemoveAt(index);
                drawnCards.Remove(discardedCard);
                returnedDraws.Remove(discardedCard);
            }

            return discardCount;
        }

        /// <summary>
        /// カードをランダムにドローする
        /// </summary>
        /// <param name="count">ドローする枚数</param>
        /// <param name="random">乱数プロバイダー</param>
        /// <returns>ドローしたカードのリスト</returns>
        public List<Card> DrawCards(int count, IRandomProvider random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (count < 0) throw new ArgumentException("ドロー枚数は0以上である必要があります", nameof(count));

            var result = new List<Card>();

            // まだドローしていないカードを取得
            var availableCards = cards.Where(c => !drawnCards.Contains(c)).ToList();

            // ドロー枚数を調整（残りカード数を超えない）
            int actualDrawCount = Math.Min(count, availableCards.Count);

            for (int i = 0; i < actualDrawCount; i++)
            {
                // ランダムにカードを選択
                // 取消で戻した追加分を先に引き直し、乱数の再抽選にしない。
                Card drawnCard;
                if (returnedDraws.Count > 0) { drawnCard = returnedDraws[0]; returnedDraws.RemoveAt(0); }
                else drawnCard = availableCards[random.Range(0, availableCards.Count)];

                result.Add(drawnCard);
                drawnCards.Add(drawnCard);
                availableCards.Remove(drawnCard);
            }

            return result;
        }

        /// <summary>未確定の追加ドローだけを元の抽選順へ返す。</summary>
        internal void ReturnPreviewDraws(IReadOnlyList<Card> returned)
        {
            if (returned.Distinct().Count() != returned.Count || returned.Any(c => !drawnCards.Contains(c)))
                throw new InvalidOperationException("追加ドローの復元対象が不正です");
            foreach (var card in returned) drawnCards.Remove(card);
            returnedDraws.InsertRange(0, returned);
        }

        /// <summary>
        /// デッキをシャッフル（ドロー履歴をリセット）
        /// </summary>
        public void Shuffle(IRandomProvider random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            drawnCards.Clear(); returnedDraws.Clear();

            // Fisher-Yatesシャッフル
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = random.Range(0, i + 1);
                var temp = cards[i];
                cards[i] = cards[j];
                cards[j] = temp;
            }
        }

        /// <summary>
        /// 検証済みチェックポイントの初回手札をドロー済みとして復元する
        /// </summary>
        internal void RestoreCheckpointOrder(IEnumerable<Card> order, IEnumerable<Card> firstHand)
        {
            var restored = order.ToList();
            var hand = firstHand.ToList();
            if (restored.Any(c => c == null) || restored.Distinct().Count() != restored.Count || hand.Any(c => !restored.Contains(c)))
                throw new ArgumentException("保存デッキの個体参照が不正です");
            cards.Clear(); cards.AddRange(restored);
            RestoreCheckpointDrawn(hand);
        }

        internal void RestoreCheckpointDrawn(IEnumerable<Card> firstHand)
        {
            var restored = firstHand.ToList();
            if (restored.Distinct().Count() != restored.Count || restored.Any(card => !cards.Contains(card)))
                throw new ArgumentException("初回手札とデッキ個体が一致しません");
            drawnCards.Clear(); returnedDraws.Clear();
            drawnCards.AddRange(restored);
        }

        /// <summary>ドロー履歴をリセット（再度すべてのカードをドロー可能にする）</summary>
        public void ResetDrawHistory()
        {
            drawnCards.Clear(); returnedDraws.Clear();
        }

        /// <summary>
        /// デッキ内のすべてのカードを取得（読み取り専用）
        /// </summary>
        public IReadOnlyList<Card> GetAllCards()
        {
            return cards.AsReadOnly();
        }

        /// <summary>
        /// まだドローしていないカードを取得（読み取り専用）
        /// </summary>
        public IReadOnlyList<Card> GetRemainingCards()
        {
            return cards.Where(c => !drawnCards.Contains(c)).ToList().AsReadOnly();
        }
    }
}

namespace Olca.Core.Domain
{
    /// <summary>
    /// 所持カードからの新規出撃だけに適用する共通構築制限。
    /// 非装備デッキの保存、攻略中の増減、チェックポイント復元では呼び出さない。
    /// </summary>
    public static class DepartureDeckRules
    {
        public static bool TryCreate(IReadOnlyList<Card> cards,
            IReadOnlyDictionary<(string Id, ExceedLevel Level), int> ownedCounts,
            out Deck deck, out string error)
        {
            deck = null; error = null;
            if (cards == null || ownedCounts == null) { error = "デッキと所持枚数が必要です"; return false; }
            if (cards.Count != 20) { error = "出撃デッキは20枚必要です"; return false; }
            if (cards.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) ||
                !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.ExceedLevel)))
            { error = "カード定義が不正です"; return false; }
            if (cards.Any(c => c.ExceedLevel == ExceedLevel.Lv3)) { error = "★★★は戦闘内専用で初期デッキに登録できません"; return false; }
            if (cards.GroupBy(c => c.Id).Any(g => g.Select(c => c.Rarity).Distinct().Count() != 1))
            { error = "同じカードIDのレギュレーションが一致しません"; return false; }
            foreach (var group in cards.GroupBy(c => (c.Id, c.ExceedLevel)))
            {
                int count = group.Count();
                if (!ownedCounts.TryGetValue(group.Key, out int owned) || owned < count)
                { error = "登録枚数に対して所持枚数が不足しています"; return false; }
                int limit = group.First().Rarity == CardRarity.Normal ? 4 : 1;
                if (count > limit) { error = "同一カードの構築上限を超えています"; return false; }
            }
            if (cards.Count(c => c.Rarity == CardRarity.Limited) > 5 || cards.Count(c => c.Rarity == CardRarity.Ultimate) > 1)
            { error = "レギュレーションの合計上限を超えています"; return false; }
            // 所持テンプレートを繰り返し指定しても、攻略中は一枚ずつ別個体にする。
            deck = new Deck(cards.Select(c => c.Clone()).ToList());
            return true;
        }
    }
}


namespace Olca.Core.Domain
{
    /// <summary>攻略中の獲得履歴に対する選択検証。永続所持への付与や換算は行わない。</summary>
    public static class CarryHomeSelectionRules
    {
        public static bool TryValidate(IReadOnlyList<Card> obtained, IReadOnlyList<Card> selected,
            out IReadOnlyList<Card> validated, out string error)
        {
            validated = null; error = null;
            if (obtained == null || selected == null) { error = "獲得履歴と選択が必要です"; return false; }
            if (selected.Count > 3) { error = "持ち帰りは最大3枚です"; return false; }
            var result = selected.ToArray();
            if (result.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) ||
                !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.ExceedLevel)))
            { error = "カード定義が不正です"; return false; }
            // 同名の持込カードや別攻略・復帰前の個体を混ぜず、現在の履歴の個体だけを受け取る。
            if (result.Any(c => !obtained.Any(candidate => ReferenceEquals(candidate, c))))
            { error = "攻略中に獲得したカードだけを選択できます"; return false; }
            if (result.Any(c => c.ExceedLevel == ExceedLevel.Lv3))
            { error = "★★★カードは持ち帰れません"; return false; }
            // 出撃デッキの既存識別と同じく、レベル違いは別カードとして扱う。
            if (result.GroupBy(c => (c.Id, c.ExceedLevel)).Any(g => g.Count() > 1))
            { error = "同じカードは1枚までです"; return false; }
            validated = Array.AsReadOnly(result);
            return true;
        }
    }
}

namespace Olca.Core.Domain
{
    public enum ClearReturnStage { Choosing, Awarded, Returned }
    public enum RunReturnOutcome { Cleared, Defeated, Retreated }

    // クリア導線の暫定調整値。攻略途中には成長を反映しない。
    public sealed class ClearReturnSettings
    {
        public int FirstClearExperience = 100, ExperiencePerLevel = 100, MaximumLevel = 99;
        public int BaseMaxHp = 100, BaseAttack = 10, HpPerLevel = 5, AttackPerLevel = 1;
        public int StandardTokens = 1, LimitedTokens = 2, UltimateTokens = 3;
        public ClearReturnSettings Copy() => (ClearReturnSettings)MemberwiseClone();
        public void Validate()
        {
            if (FirstClearExperience <= 0 || ExperiencePerLevel <= 0 || MaximumLevel < 1 || BaseMaxHp <= 0 ||
                BaseAttack < 0 || HpPerLevel < 0 || AttackPerLevel < 0 || StandardTokens <= 0 || LimitedTokens <= 0 || UltimateTokens <= 0)
                throw new ArgumentException("クリア報酬・成長の設定が不正です");
        }
        public int MaxHp(int level) => checked(BaseMaxHp + (level - 1) * HpPerLevel);
        public int Attack(int level) => checked(BaseAttack + (level - 1) * AttackPerLevel);
        public int Tokens(CardRarity rarity, ExceedLevel level)
            => checked((rarity == CardRarity.Normal ? StandardTokens : rarity == CardRarity.Limited ? LimitedTokens : UltimateTokens) * ((int)level + 1));
    }
    public sealed class ReturnCardData
    {
        public string InstanceId, Id, Name, ValueHash;
        public CardRarity Rarity;
        public ExceedLevel Level;
        public int OverflowTokens;
        public ReturnCardData Copy() => (ReturnCardData)MemberwiseClone();
    }
    public sealed class OwnedCardCount
    {
        public string Id, Name, DefinitionHash;
        public CardRarity Rarity;
        public ExceedLevel Level;
        public int Count;
        public OwnedCardCount Copy() => (OwnedCardCount)MemberwiseClone();
    }
    public sealed class ClearReceipt
    {
        public string RunId, Fingerprint;
        public RunReturnOutcome Outcome;
        public long Experience, Tokens;
        public ClearReceipt Copy() => (ClearReceipt)MemberwiseClone();
    }
    public sealed class ClearReturnData
    {
        public string RunId, DungeonKey, Fingerprint;
        public ClearReturnStage Stage;
        public RunReturnOutcome Outcome;
        public ReturnCardData[] Candidates = new ReturnCardData[0];
        public int[] Selected = new int[0];
        public long ExperienceAward, TokenAward;
        public int NewLevel, ReturnedMaxHp, ReturnedAttack;
        public ClearReturnSettings Settings;
        public ClearReturnData Copy()
        {
            var copy = (ClearReturnData)MemberwiseClone();
            copy.Candidates = Candidates?.Select(c => c?.Copy()).ToArray(); copy.Selected = Selected?.ToArray(); copy.Settings = Settings?.Copy(); return copy;
        }
    }
    // 進行文書を先に確定し、別攻略ディレクトリへ同じ出撃スナップショットを再試行する。
    public sealed class NextDepartureData
    {
        public const string InitialParent = "initial-departure-v1";
        public string ParentRunId, DefinitionKey;
        public CheckpointDto Start;
        public NextDepartureData Copy() => new NextDepartureData { ParentRunId = ParentRunId, DefinitionKey = DefinitionKey, Start = Start?.Copy() };
    }
    public sealed class SavedDeckCard
    {
        public string Id, DefinitionKey;
        public ExceedLevel Level;
        public SavedDeckCard Copy() => (SavedDeckCard)MemberwiseClone();
    }
    public sealed class SavedDepartureDeck
    {
        public string Id, Name;
        public SavedDeckCard[] Cards = new SavedDeckCard[0];
        public SavedDepartureDeck Copy() => new SavedDepartureDeck { Id=Id, Name=Name, Cards=Cards?.Select(c=>c?.Copy()).ToArray() };
        public static bool TryValidateEquipped(ClearReturnProfile profile, SavedDepartureDeck saved, out string error)
        {
            error=null;
            if(saved?.Cards==null||saved.Cards.Any(c=>c==null)){error="デッキがありません";return false;}
            var cards=new List<Card>();var used=new Dictionary<string,int>();
            foreach(var slot in saved.Cards){
                var owned=OwnedDefinition.Resolve(profile,slot);
                if(owned==null){error="未所持または定義が未特定のカードを装備できません";return false;}
                string key=OwnedDefinition.Key(owned);used.TryGetValue(key,out int count);used[key]=count+1;
                if(used[key]>owned.Count){error="登録定義の所持枚数が不足しています";return false;}
                cards.Add(new Card(owned.Id,owned.Name,0,owned.Rarity,CardCategory.Attack,ElementType.None,CardSeries.None,0,"",exceedLevel:owned.Level));
            }
            return DepartureDeckRules.TryCreate(cards,OwnedDefinition.AggregateCounts(profile),out _,out error);
        }
    }
    /// <summary>個体ではなく所持定義の識別。旧行はメタデータ付きの安定キーで保持する。</summary>
    public static class OwnedDefinition
    {
        public static string Key(OwnedCardCount owned) => owned.DefinitionHash??("legacy/"+owned.Id.Length+":"+owned.Id+"/"+(int)owned.Level+"/"+(int)owned.Rarity+"/"+owned.Name);
        public static OwnedCardCount Resolve(ClearReturnProfile profile,SavedDeckCard slot)
        {
            if(profile==null||slot==null)return null;
            var matches=profile.Owned.Where(o=>o.Id==slot.Id&&o.Level==slot.Level&&(slot.DefinitionKey==null||Key(o)==slot.DefinitionKey)).ToArray();
            return matches.Length==1?matches[0]:null;
        }
        public static Dictionary<(string Id,ExceedLevel Level),int> AggregateCounts(ClearReturnProfile p) => p.Owned.GroupBy(o=>(o.Id,o.Level)).ToDictionary(g=>g.Key,g=>g.Sum(o=>o.Count));
        public static bool CardMatches(OwnedCardCount owned,Card card) => owned!=null&&card!=null&&owned.Id==card.Id&&owned.Level==card.ExceedLevel&&owned.Name==card.Name&&owned.Rarity==card.Rarity&&
            (owned.DefinitionHash==null||owned.DefinitionHash==CollectionDisplay.DefinitionHash(card));
        public static bool TryCreate(ClearReturnProfile profile,IReadOnlyList<Card> cards,IReadOnlyList<SavedDeckCard> slots,out Deck deck,out string error)
        {
            deck=null;
            if(cards==null||slots==null||cards.Count!=slots.Count){error="定義付きカード参照が必要です";return false;}
            if(!SavedDepartureDeck.TryValidateEquipped(profile,new SavedDepartureDeck{Cards=slots.ToArray()},out error))return false;
            for(int n=0;n<cards.Count;n++)if(!CardMatches(Resolve(profile,slots[n]),cards[n])){error="所持と出撃カードの定義が一致しません";return false;}
            return DepartureDeckRules.TryCreate(cards,AggregateCounts(profile),out deck,out error);
        }
        public static string DisplayName(OwnedCardCount card)
        {
            string suffix=new string('★',(int)card.Level);
            return card.Name+(suffix.Length>0&&!card.Name.EndsWith(suffix)?suffix:"")+"　"+card.Rarity;
        }
    }

    public enum ExchangeLimit { Unlimited, PerRefresh, Lifetime }
    /// <summary>交換所の既存カード商品。単価は超過変換量より必ず高くする。</summary>
    public sealed class TokenExchangeProduct
    {
        private readonly ExchangeOfferData data;
        public string Id => data.Id;
        public TokenExchangeProduct(string id, Card card, long price, long overflowTokens, ExchangeLimit limit = ExchangeLimit.Unlimited, int maximum = 0)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            data = new ExchangeOfferData { Id=id, CardId=card.Id, Name=card.Name, Rarity=card.Rarity, Level=card.ExceedLevel,
                Price=price, OverflowTokens=overflowTokens, Limit=limit, Maximum=maximum, CardDefinitionHash=CollectionDisplay.DefinitionHash(card) };
            ExchangeOfferData.Validate(data);
        }
        public ExchangeOfferData Capture() => data.Copy();
    }
    public sealed class ExchangeOfferData
    {
        public string Id, CardId, Name, CardDefinitionHash;
        public CardRarity Rarity;
        public ExceedLevel Level;
        public long Price, OverflowTokens;
        public ExchangeLimit Limit;
        public int Maximum, Purchased;
        public bool Random;
        public ExchangeOfferData Copy() => (ExchangeOfferData)MemberwiseClone();
        public static void Validate(ExchangeOfferData o)
        {
            if(o==null || string.IsNullOrWhiteSpace(o.Id) || string.IsNullOrWhiteSpace(o.CardId) || string.IsNullOrWhiteSpace(o.Name) ||
                !Enum.IsDefined(typeof(CardRarity),o.Rarity) || !Enum.IsDefined(typeof(ExceedLevel),o.Level) || !Enum.IsDefined(typeof(ExchangeLimit),o.Limit) ||
                o.OverflowTokens<=0 || o.Price<=o.OverflowTokens || o.Purchased<0 || (o.Limit==ExchangeLimit.Unlimited ? o.Maximum!=0 : o.Maximum<1) ||
                (o.Limit==ExchangeLimit.PerRefresh && o.Purchased>o.Maximum)) throw new ArgumentException("交換商品の値・価格・回数制限が不正です");
        }
    }
    public sealed class ExchangeStockData
    {
        public string RoundId;
        public ExchangeOfferData[] Offers = Array.Empty<ExchangeOfferData>();
        public ExchangeStockData Copy() => new ExchangeStockData { RoundId=RoundId, Offers=Offers?.Select(o=>o?.Copy()).ToArray() };
    }
    public sealed class ExchangePurchaseTotal
    {
        public string ProductId;
        public long Count;
        public ExchangePurchaseTotal Copy() => (ExchangePurchaseTotal)MemberwiseClone();
    }
    public sealed class ExchangeReceipt
    {
        public string RequestId, RoundId, ProductId;
        public long Paid, OverflowTokens;
        public ExchangeReceipt Copy() => (ExchangeReceipt)MemberwiseClone();
    }
    public sealed class ClearReturnProfile
    {
        public int Version = 1;
        public long Revision, Experience, Tokens;
        public int Level = 1, ReturnedHp;
        public string CommitId;
        public string CheckpointGeneration; // nullは旧保存の領域。新規開始だけで切替。
        public OwnedCardCount[] Owned = new OwnedCardCount[0];
        public string[] ClearedDungeons = new string[0];
        public ClearReceipt[] Receipts = new ClearReceipt[0];
        public ClearReturnData Flow;
        public NextDepartureData NextDeparture;
        public SavedDepartureDeck[] SavedDecks = new SavedDepartureDeck[0];
        public string EquippedDeckId;
        public ExchangeStockData ExchangeStock;
        public ExchangePurchaseTotal[] ExchangeTotals;
        public ExchangeReceipt[] ExchangeReceipts;
        public DiscoveredCard[] DiscoveredCards; // nullは旧保存
        public string[] RevealedRecipes;
        public string SelectedStyleId; // 旧省略/nullは基本スタイル
        public ClearReturnProfile Copy() => new ClearReturnProfile {
            Version = Version, Revision = Revision, Experience = Experience, Tokens = Tokens, Level = Level, ReturnedHp = ReturnedHp, CommitId = CommitId, CheckpointGeneration=CheckpointGeneration,
            Owned = Owned?.Select(c => c?.Copy()).ToArray(), ClearedDungeons = ClearedDungeons?.ToArray(),
            Receipts = Receipts?.Select(r => r?.Copy()).ToArray(), Flow = Flow?.Copy(), NextDeparture = NextDeparture?.Copy(), EquippedDeckId=EquippedDeckId, SelectedStyleId=SelectedStyleId, DiscoveredCards=DiscoveredCards?.Select(c=>c?.Copy()).ToArray(), RevealedRecipes=RevealedRecipes?.ToArray(), ExchangeStock=ExchangeStock?.Copy(),
            ExchangeTotals=ExchangeTotals?.Select(t=>t?.Copy()).ToArray(), ExchangeReceipts=ExchangeReceipts?.Select(t=>t?.Copy()).ToArray(),
            SavedDecks=(SavedDecks??new SavedDepartureDeck[0]).Select(d=>d?.Copy()).ToArray() };
        public static void Validate(ClearReturnProfile p)
        {
            if (p == null || p.Version != 1 || p.Revision < 0 || p.Experience < 0 || p.Tokens < 0 || p.Level < 1 || p.ReturnedHp < 0 ||
                (p.Revision > 0 && string.IsNullOrWhiteSpace(p.CommitId)) || p.Owned == null || p.ClearedDungeons == null || p.Receipts == null)
                throw new ArgumentException("進行データの版・基本値が不正です");
            if(p.CheckpointGeneration!=null&&!Guid.TryParseExact(p.CheckpointGeneration,"N",out _))throw new ArgumentException("攻略保存の世代が不正です");
            var discovered=p.DiscoveredCards??Array.Empty<DiscoveredCard>();
            if(discovered.Any(c=>c==null||string.IsNullOrWhiteSpace(c.Id)||string.IsNullOrWhiteSpace(c.Name)||c.Level==ExceedLevel.Lv3||
                !Enum.IsDefined(typeof(ExceedLevel),c.Level)||!Enum.IsDefined(typeof(CardRarity),c.Rarity)||
                (c.DefinitionHash!=null&&(c.DefinitionHash.Length!=64||!c.DefinitionHash.All(Uri.IsHexDigit))))||
                discovered.GroupBy(c=>(c.Id,c.Level,c.Rarity,c.Name,c.DefinitionHash)).Any(g=>g.Count()>1)||
                (p.RevealedRecipes!=null&&(p.RevealedRecipes.Any(string.IsNullOrWhiteSpace)||p.RevealedRecipes.Distinct().Count()!=p.RevealedRecipes.Length)))
                throw new ArgumentException("図鑑履歴が不正です");
            if (p.SelectedStyleId != null && string.IsNullOrWhiteSpace(p.SelectedStyleId)) throw new ArgumentException("選択スタイルIDが不正です");
            if (p.Owned.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) || c.Count < 1 || c.Count > 4 ||
                !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.Level) ||
                (c.DefinitionHash!=null&&(c.DefinitionHash.Length!=64||!c.DefinitionHash.All(Uri.IsHexDigit)))) ||
                p.Owned.GroupBy(OwnedDefinition.Key).Any(g => g.Count() > 1) ||
                p.ClearedDungeons.Any(string.IsNullOrWhiteSpace) || p.ClearedDungeons.Distinct().Count() != p.ClearedDungeons.Length ||
                p.Receipts.Any(r => r == null || string.IsNullOrWhiteSpace(r.RunId) || string.IsNullOrWhiteSpace(r.Fingerprint) || r.Experience < 0 || r.Tokens < 0 || !Enum.IsDefined(typeof(RunReturnOutcome), r.Outcome) ||
                    (r.Outcome != RunReturnOutcome.Cleared && (r.Experience != 0 || r.Tokens != 0))) ||
                p.Receipts.Select(r => r.RunId).Distinct().Count() != p.Receipts.Length)
                throw new ArgumentException("所持カードまたは受領履歴が不正です");
            var stock=p.ExchangeStock;
            if(stock!=null){if(string.IsNullOrWhiteSpace(stock.RoundId)||stock.Offers==null||stock.Offers.Any(o=>o==null)||stock.Offers.Select(o=>o.Id).Distinct().Count()!=stock.Offers.Length)
                throw new ArgumentException("交換在庫が不正です");foreach(var offer in stock.Offers)ExchangeOfferData.Validate(offer);}
            var totals=p.ExchangeTotals??Array.Empty<ExchangePurchaseTotal>();var purchases=p.ExchangeReceipts??Array.Empty<ExchangeReceipt>();
            if(totals.Any(t=>t==null||string.IsNullOrWhiteSpace(t.ProductId)||t.Count<1)||totals.Select(t=>t.ProductId).Distinct().Count()!=totals.Length||
                purchases.Any(t=>t==null||!Guid.TryParseExact(t.RequestId,"N",out _)||string.IsNullOrWhiteSpace(t.RoundId)||string.IsNullOrWhiteSpace(t.ProductId)||t.Paid<=t.OverflowTokens||t.OverflowTokens<0)||
                purchases.Select(t=>t.RequestId).Distinct().Count()!=purchases.Length)throw new ArgumentException("交換履歴が不正です");
            if(totals.Any(t=>t.Count!=purchases.LongCount(x=>x.ProductId==t.ProductId)) || purchases.Any(x=>!totals.Any(t=>t.ProductId==x.ProductId)) ||
                (stock!=null && stock.Offers.Any(o=>o.Purchased!=purchases.LongCount(x=>x.RoundId==stock.RoundId&&x.ProductId==o.Id))))
                throw new ArgumentException("交換の回数と領収書が一致しません");
            var decks=p.SavedDecks??new SavedDepartureDeck[0];
            if(decks.Any(d=>d==null||!Guid.TryParseExact(d.Id,"N",out _)||string.IsNullOrWhiteSpace(d.Name)||d.Cards==null||
                d.Cards.Any(c=>c==null||string.IsNullOrWhiteSpace(c.Id)||!Enum.IsDefined(typeof(ExceedLevel),c.Level)||c.Level==ExceedLevel.Lv3))||
                decks.Select(d=>d.Id).Distinct().Count()!=decks.Length)throw new ArgumentException("保存デッキの識別/参照が不正です");
            if(p.EquippedDeckId!=null){var equipped=decks.SingleOrDefault(d=>d.Id==p.EquippedDeckId);
                if(equipped==null||!SavedDepartureDeck.TryValidateEquipped(p,equipped,out _))throw new ArgumentException("装備デッキの構築条件が不正です");}
            var n = p.NextDeparture;
            if (n != null && (string.IsNullOrWhiteSpace(n.ParentRunId) || string.IsNullOrWhiteSpace(n.DefinitionKey) ||
                n.Start == null || !Guid.TryParseExact(n.Start.RunId, "N", out _) || n.Start.RunId == n.ParentRunId ||
                n.Start.Boundary != CheckpointBoundary.Departure || n.Start.Sequence != 1 || n.Start.PositionMilli != 0 ||
                n.Start.Player == null || n.Start.Player.Hp != n.Start.Player.MaxHp || n.Start.ObtainedOrder == null || n.Start.ObtainedOrder.Length != 0 ||
                !(p.Receipts.Any(r => r.RunId == n.ParentRunId) || (n.ParentRunId == NextDepartureData.InitialParent && (p.Flow == null || p.Flow?.RunId == n.Start.RunId))))) throw new ArgumentException("次回出撃計画が不正です");
            var f = p.Flow; if (f == null) return;
            if (string.IsNullOrWhiteSpace(f.RunId) || string.IsNullOrWhiteSpace(f.DungeonKey) || string.IsNullOrWhiteSpace(f.Fingerprint) ||
                !Enum.IsDefined(typeof(ClearReturnStage), f.Stage) || !Enum.IsDefined(typeof(RunReturnOutcome), f.Outcome) || f.Candidates == null || f.Selected == null || f.Settings == null || f.ExperienceAward < 0 || f.TokenAward < 0)
                throw new ArgumentException("帰還段階が不正です");
            f.Settings.Validate();
            if (f.Outcome != RunReturnOutcome.Cleared && (f.Stage != ClearReturnStage.Returned || f.Candidates.Length != 0 ||
                f.Selected.Length != 0 || f.ExperienceAward != 0 || f.TokenAward != 0)) throw new ArgumentException("失敗・撤退で報酬を付与できません");
            if (f.Candidates.Any(c => c == null || string.IsNullOrWhiteSpace(c.InstanceId) || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) ||
                string.IsNullOrWhiteSpace(c.ValueHash) || !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.Level) || c.OverflowTokens <= 0) ||
                f.Candidates.Select(c => c.InstanceId).Distinct().Count() != f.Candidates.Length)
                throw new ArgumentException("持ち帰り候補が不正です");
            ValidateSelection(f);
            var receipt = p.Receipts.SingleOrDefault(r => r.RunId == f.RunId);
            if (f.Stage == ClearReturnStage.Choosing ? receipt != null : receipt == null || receipt.Fingerprint != f.Fingerprint ||
                receipt.Outcome != f.Outcome || receipt.Experience != f.ExperienceAward || receipt.Tokens != f.TokenAward || f.NewLevel < 1 || f.ReturnedMaxHp <= 0 || f.ReturnedAttack < 0)
                throw new ArgumentException("帰還と受領履歴の対応が不正です");
            if (f.Stage != ClearReturnStage.Choosing && (f.NewLevel != p.Level || f.ReturnedMaxHp != f.Settings.MaxHp(p.Level) || f.ReturnedAttack != f.Settings.Attack(p.Level)))
                throw new ArgumentException("成長反映値が不正です");
            if (f.Stage == ClearReturnStage.Returned && p.ReturnedHp != f.ReturnedMaxHp) throw new ArgumentException("帰還HPが不正です");
        }
        internal static void ValidateSelection(ClearReturnData f)
        {
            if (f.Selected.Any(i => i < 0 || i >= f.Candidates.Length)) throw new ArgumentException("候補にないカードです");
            var cards = f.Candidates.Select(c => new Card(c.Id, c.Name, 0, c.Rarity, CardCategory.Attack, ElementType.None, CardSeries.None, 0, "", exceedLevel: c.Level)).ToArray();
            if (!CarryHomeSelectionRules.TryValidate(cards, f.Selected.Select(i => cards[i]).ToArray(), out _, out var error)) throw new ArgumentException(error);
        }
    }
    /// <summary>所持数とは独立した永続入手登録。未登録カードの詳細は保持/公開しない。</summary>
    public sealed class DiscoveredCard
    {
        public string Id, Name, DefinitionHash;
        public ExceedLevel Level;
        public CardRarity Rarity;
        public DiscoveredCard Copy() => (DiscoveredCard)MemberwiseClone();
    }
    public sealed class CollectionRecipe
    {
        public string Id { get; }
        public string ResultId { get; }
        public ExceedLevel ResultLevel { get; }
        private readonly string[] materials;
        private readonly Card[] materialCards;
        private readonly Card resultCard;
        public IReadOnlyList<string> Materials => Array.AsReadOnly(materials);
        public CollectionRecipe(string id, IEnumerable<string> materials, string resultId, ExceedLevel resultLevel=ExceedLevel.Normal, IEnumerable<Card> materialCards=null, Card resultCard=null)
        {
            this.materialCards=materialCards?.Select(c=>c.Clone()).ToArray();this.resultCard=resultCard?.Clone();
            Id=id;ResultId=resultId;ResultLevel=resultLevel;this.materials=materials?.ToArray();
            if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(resultId)||this.materials==null||this.materials.Length!=2||this.materials.Any(string.IsNullOrWhiteSpace)||
                resultLevel==ExceedLevel.Lv3||!Enum.IsDefined(typeof(ExceedLevel),resultLevel)||
                (resultCard!=null&&(resultCard.Id!=resultId||resultCard.ExceedLevel!=resultLevel)))throw new ArgumentException("図鑑レシピ定義が不正です");
        }
        public bool IsResultKnown(ClearReturnProfile profile,IReadOnlyList<Card> catalog=null) => resultCard==null?
            (profile?.DiscoveredCards??Array.Empty<DiscoveredCard>()).Any(c=>c.Id==ResultId&&c.Level==ResultLevel):CollectionDisplay.IsKnown(profile,resultCard,catalog);
        public bool IsRevealed(ClearReturnProfile profile,IReadOnlyList<Card> catalog=null) =>
            (profile?.RevealedRecipes??Array.Empty<string>()).Contains(Id)||IsResultKnown(profile,catalog);
        public string MaterialName(ClearReturnProfile profile,int index,IReadOnlyList<Card> catalog=null) => materialCards==null?CollectionDisplay.Name(profile,materials[index]):
            CollectionDisplay.IsKnown(profile,materialCards[index],catalog)?materialCards[index].Name:"？？？";
        public string ResultName(ClearReturnProfile profile,IReadOnlyList<Card> catalog=null) => !IsRevealed(profile,catalog)?"？？？":
            resultCard==null?CollectionDisplay.Name(profile,ResultId,ResultLevel):resultCard.Name;
        // 公開履歴はレシピIDに対する知識。結果定義を特定できない旧定義から性能を推測しない。
        public bool TryGetResult(ClearReturnProfile profile,out Card result,IReadOnlyList<Card> catalog=null)
        {
            result=IsRevealed(profile,catalog)?resultCard?.Clone():null;return result!=null;
        }
        public string ResultPerformance(ClearReturnProfile profile,IReadOnlyList<Card> catalog=null)
        {
            if(!TryGetResult(profile,out var card,catalog))return null;
            var description=card.Description??"";
            if(materialCards==null)description="素材定義未特定のため説明は非表示";
            else foreach(var material in materialCards.Where(c=>!CollectionDisplay.IsKnown(profile,c,catalog)))
                if(!string.IsNullOrEmpty(material.Name))description=description.Replace(material.Name,"？？？");
            return CollectionDisplay.PerformanceText(card,description);
        }
        public bool Matches(IReadOnlyList<Card> input,Card output) => input!=null&&output!=null&&output.Id==ResultId&&output.ExceedLevel==ResultLevel&&
            (resultCard==null||CollectionDisplay.DefinitionHash(output)==CollectionDisplay.DefinitionHash(resultCard))&&
            input.All(c=>c!=null)&&input.Select(c=>c.Id).OrderBy(x=>x).SequenceEqual(materials.OrderBy(x=>x));
    }
    public static class CollectionDisplay
    {
        public static string DefinitionHash(Card card) => Olca.Core.Logic.Dungeon.CheckpointValues.CardHash(card);
        public static bool MetadataMatches(DiscoveredCard known,Card card) => known.Id==card.Id&&known.Level==card.ExceedLevel&&known.Rarity==card.Rarity&&known.Name==card.Name;
        public static bool MetadataKnown(ClearReturnProfile profile,Card card) => (profile?.DiscoveredCards??Array.Empty<DiscoveredCard>()).Any(c=>MetadataMatches(c,card));
        public static string Name(ClearReturnProfile profile,string id,ExceedLevel level=ExceedLevel.Normal)
        {
            var names=(profile?.DiscoveredCards??Array.Empty<DiscoveredCard>()).Where(c=>c.Id==id&&c.Level==level).Select(c=>c.Name).Distinct().ToArray();
            return names.Length==1?names[0]:"？？？";
        }
        private static bool EntryKnown(DiscoveredCard entry,Card card,IReadOnlyList<Card> catalog)
        {
            if(!MetadataMatches(entry,card))return false;
            if(entry.DefinitionHash!=null)return entry.DefinitionHash==DefinitionHash(card);
            return (catalog??new[]{card}).Where(c=>c.Id==card.Id&&c.ExceedLevel==card.ExceedLevel&&c.Rarity==card.Rarity&&c.Name==card.Name)
                .Select(DefinitionHash).Distinct().Count()==1;
        }
        public static bool IsKnown(ClearReturnProfile profile,Card card,IReadOnlyList<Card> catalog=null)
            => (profile?.DiscoveredCards??Array.Empty<DiscoveredCard>()).Any(entry=>EntryKnown(entry,card,catalog));
        public static string CardText(ClearReturnProfile profile,Card card,IReadOnlyList<Card> catalog=null)
        {
            if(!IsKnown(profile,card,catalog))return "？？？　未入手";
            return card.Name+"　"+card.ExceedLevel+"　"+card.Rarity+"　コスト "+card.GetFinalCost()+"　威力 "+card.GetFinalPower()+"\n"+card.Description;
        }
        public static string[] CardRows(ClearReturnProfile profile,IReadOnlyList<Card> definitions)
        {
            var catalog=definitions.Where(c=>c.ExceedLevel!=ExceedLevel.Lv3).GroupBy(DefinitionHash).Select(g=>g.First()).ToArray();
            return catalog.Select(c=>CardText(profile,c,catalog)).Concat((profile.DiscoveredCards??Array.Empty<DiscoveredCard>())
                .Where(known=>!catalog.Any(c=>EntryKnown(known,c,catalog))).GroupBy(c=>(c.Id,c.Level,c.Rarity,c.Name))
                .Select(g=>g.Key.Name+"　登録済み（詳細定義未特定）")).ToArray();
        }
        public static string RecipeText(ClearReturnProfile profile,CollectionRecipe recipe,IReadOnlyList<Card> catalog=null)
            => !recipe.IsRevealed(profile,catalog)?"？？？　未公開レシピ":
                "公開済み："+string.Join(" ＋ ",Enumerable.Range(0,recipe.Materials.Count).Select(i=>recipe.MaterialName(profile,i,catalog)))+" → "+recipe.ResultName(profile,catalog);
        public static CollectionRecipeRow[] RecipeRows(ClearReturnProfile profile,IEnumerable<CollectionRecipe> recipes,IReadOnlyList<Card> catalog=null)
            => recipes.Select(r=>new CollectionRecipeRow(RecipeText(profile,r,catalog),r.TryGetResult(profile,out var card,catalog)?card:null,r.ResultPerformance(profile,catalog))).ToArray();
        public static string PerformanceText(Card card,string description=null) => card.Name+new string('★',(int)card.ExceedLevel)+
            "　コスト "+card.GetFinalCost()+"　威力 "+card.GetFinalPower()+"\n分類 "+card.Category+"　"+card.Rarity+"\n属性 "+card.Element+"　シリーズ "+card.Series?.Name+
            "\n"+(description??card.Description);
    }
    /// <summary>公開判定後の表示専用値。未公開行には結果定義自体を渡さない。</summary>
    public sealed class CollectionRecipeRow
    {
        public string Text { get; }
        private readonly Card result;
        public Card Result => result?.Clone();
        public string Performance { get; }
        internal CollectionRecipeRow(string text,Card result,string performance){Text=text;this.result=result?.Clone();Performance=performance;}
    }
    public interface IClearReturnStore
    {
        ClearReturnProfile Read(out string error);
        bool TryWrite(long expectedRevision, ClearReturnProfile next, out string error);
    }
    /// <summary>未作成の進行保存だけを明示する。空の旧保存とは区別する。</summary>
    public interface INewClearReturnStore { bool IsNewProfile { get; } }
    // 保存成功までは現状態を変更しない。失敗後も同じ計画を再試行し、受領済みRunIdを再計上しない。
    public sealed class ClearReturnSession
    {
        private readonly IClearReturnStore store;
        private ClearReturnProfile current, pending;
        private readonly CollectionRecipe[] collectionRecipes;
        private Card[] collectionDefinitions;
        private bool writing;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public bool HasPendingSave => pending != null;
        public ClearReturnProfile Snapshot => current?.Copy();
        public ClearReturnData DisplayFlow => (pending?.Flow != null && pending.Flow.RunId != current?.Flow?.RunId
            ? pending.Flow : current?.Flow ?? pending?.Flow)?.Copy();
        public ClearReturnSession(IClearReturnStore store, IReadOnlyList<CollectionRecipe> recipes=null, IReadOnlyList<Card> definitions=null)
        {
            ConfigureCollectionDefinitions(definitions);
            collectionRecipes=recipes?.ToArray()??Array.Empty<CollectionRecipe>();
            if(collectionRecipes.Any(r=>r==null)||collectionRecipes.Select(r=>r.Id).Distinct().Count()!=collectionRecipes.Length)throw new ArgumentException("重複しない図鑑レシピが必要です");
            this.store = store ?? throw new ArgumentNullException(nameof(store)); Reload();
        }
        public bool Reload()
        {
            if (writing || pending != null) return Fail("未保存の処理があります");
            var read = store.Read(out var error);
            if (read == null) { Ready = false; return Fail(error ?? "進行データを読み込めません"); }
            try { ClearReturnProfile.Validate(read); current = read.Copy(); Ready = true; Error = null; return true; }
            catch (ArgumentException e) { Ready = false; return Fail(e.Message); }
        }
        public bool InitializeStarter(IReadOnlyList<Card> cards)
        {
            if (!Available() || !(store is INewClearReturnStore fresh) || !fresh.IsNewProfile ||
                current.Revision != 0 || current.Level != 1 || current.Experience != 0 || current.Tokens != 0 ||
                current.Owned.Length != 0 || current.Receipts.Length != 0 || current.ClearedDungeons.Length != 0 ||
                current.Flow != null || current.NextDeparture != null || current.SavedDecks.Length != 0 ||
                current.EquippedDeckId != null || current.ExchangeStock != null || current.SelectedStyleId != null)
                return Fail("既存の進行データにはスターターを配布しません");
            var next=current.Copy();
            if(!PopulateStarter(next,cards,out var error))return Fail(error);
            return Stage(next);
        }
        private static bool PopulateStarter(ClearReturnProfile next,IReadOnlyList<Card> cards,out string error)
        {
            error=null;
            if(cards==null){error="スターター定義が必要です";return false;}
            var counts=cards.Where(c=>c!=null).GroupBy(c=>(c.Id,c.ExceedLevel)).ToDictionary(g=>g.Key,g=>g.Count());
            if(!DepartureDeckRules.TryCreate(cards,counts,out _,out error))return false;

            next.Owned=cards.GroupBy(c=>(c.Id,c.ExceedLevel)).Select(g=>new OwnedCardCount {
                Id=g.Key.Id,Level=g.Key.ExceedLevel,Name=g.First().Name,Rarity=g.First().Rarity,Count=g.Count(),DefinitionHash=CollectionDisplay.DefinitionHash(g.First()) }).ToArray();
            string id=Guid.NewGuid().ToString("N");
            next.SavedDecks=new[]{new SavedDepartureDeck {Id=id,Name="スターター",Cards=cards.Select(c=>new SavedDeckCard{Id=c.Id,Level=c.ExceedLevel,DefinitionKey=CollectionDisplay.DefinitionHash(c)}).ToArray()}};
            next.EquippedDeckId=id;
            foreach(var card in cards)Discover(next,card.Id,card.Name,card.Rarity,card.ExceedLevel,CollectionDisplay.DefinitionHash(card));

            return true;
        }
        /// <summary>許可地点は呼出側で判定し、進行1枠の確定済み内容だけを再保存する。</summary>
        public bool ManualSave() => Available() && Stage(current.Copy());
        public bool StartNewGame(long confirmedRevision,IReadOnlyList<Card> starter)
        {
            if(!Available()||current.Revision!=confirmedRevision)return Fail("確認後に進行が変わりました。確認し直してください");
            var next=new ClearReturnProfile { CheckpointGeneration=Guid.NewGuid().ToString("N") };
            if(!PopulateStarter(next,starter,out var error))return Fail(error);
            return Stage(next);
        }
        public void ConfigureCollectionDefinitions(IReadOnlyList<Card> definitions)
        {
            collectionDefinitions=definitions?.Where(c=>c!=null&&c.ExceedLevel!=ExceedLevel.Lv3).GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First().Clone()).ToArray();
        }
        private static bool SameDiscovery(ClearReturnProfile a,ClearReturnProfile b) =>
            (a.DiscoveredCards??Array.Empty<DiscoveredCard>()).Select(c=>(c.Id,c.Level,c.Rarity,c.Name,c.DefinitionHash)).SequenceEqual(
                (b.DiscoveredCards??Array.Empty<DiscoveredCard>()).Select(c=>(c.Id,c.Level,c.Rarity,c.Name,c.DefinitionHash)));
        private void NormalizeOwned(ClearReturnProfile next)
        {
            foreach(var owned in next.Owned.Where(o=>o.DefinitionHash==null)) {
                var matches=collectionDefinitions?.Where(c=>c.Id==owned.Id&&c.ExceedLevel==owned.Level&&c.Rarity==owned.Rarity&&c.Name==owned.Name)
                    .Select(CollectionDisplay.DefinitionHash).Distinct().ToArray();
                if(matches?.Length==1)owned.DefinitionHash=matches[0];
            }
            foreach(var deck in next.SavedDecks)foreach(var slot in deck.Cards) {
                var before=OwnedDefinition.Resolve(current,slot);if(before==null)continue;
                var matches=next.Owned.Where(o=>o.Id==before.Id&&o.Level==before.Level&&o.Rarity==before.Rarity&&o.Name==before.Name&&
                    (before.DefinitionHash==null||before.DefinitionHash==o.DefinitionHash)).ToArray();
                var after=matches.FirstOrDefault(o=>o.DefinitionHash==before.DefinitionHash)??(matches.Length==1?matches[0]:null);
                if(after!=null)slot.DefinitionKey=OwnedDefinition.Key(after);
            }
        }
        public bool EnsureOwnedDefinitions()
        {
            if(!Available())return Fail("所持定義の未保存処理を先に再試行してください");
            var next=current.Copy();NormalizeOwned(next);
            bool same=next.Owned.Select(OwnedDefinition.Key).SequenceEqual(current.Owned.Select(OwnedDefinition.Key))&&
                next.SavedDecks.SelectMany(d=>d.Cards).Select(c=>c.DefinitionKey).SequenceEqual(current.SavedDecks.SelectMany(d=>d.Cards).Select(c=>c.DefinitionKey));
            if(same){Error=null;return true;}return Stage(next);
        }
        private OwnedCardCount AcquisitionOwned(ClearReturnProfile next,string id,string name,CardRarity rarity,ExceedLevel level,string hash)
        {
            NormalizeOwned(next);
            var exact=next.Owned.SingleOrDefault(o=>o.Id==id&&o.Level==level&&hash!=null&&o.DefinitionHash==hash);
            if(exact!=null)return exact;
            // 定義一覧のない旧APIは従来のメタデータ行を維持し、性能を推測して付け替えない。
            var legacy=next.Owned.Where(o=>o.Id==id&&o.Level==level&&o.Name==name&&o.Rarity==rarity&&o.DefinitionHash==null).ToArray();
            return legacy.Length==1&&(hash==null||collectionDefinitions==null)?legacy[0]:null;
        }
        private static void Discover(ClearReturnProfile profile,string id,string name,CardRarity rarity,ExceedLevel level,string hash=null)
        {
            if(level==ExceedLevel.Lv3)return;
            var known=(profile.DiscoveredCards??Array.Empty<DiscoveredCard>()).Select(c=>c.Copy()).ToList();
            bool Same(DiscoveredCard c)=>c.Id==id&&c.Name==name&&c.Rarity==rarity&&c.Level==level;
            if(hash==null&&known.Any(Same))return;
            if(known.Any(c=>Same(c)&&c.DefinitionHash==hash))return;
            if(hash!=null)known.RemoveAll(c=>Same(c)&&c.DefinitionHash==null);
            known.Add(new DiscoveredCard{Id=id,Name=name,Rarity=rarity,Level=level,DefinitionHash=hash});profile.DiscoveredCards=known.ToArray();
        }
        private void UpdateCollection(ClearReturnProfile profile)
        {
            foreach(var card in (current?.Owned??Array.Empty<OwnedCardCount>()).Concat(profile.Owned))
                Discover(profile,card.Id,card.Name,card.Rarity,card.Level);
            profile.DiscoveredCards=profile.DiscoveredCards??Array.Empty<DiscoveredCard>();
            // 旧履歴の4項目から候補が一意な場合だけ、既存定義へ結び付ける。
            if(collectionDefinitions!=null)foreach(var known in profile.DiscoveredCards.Where(c=>c.DefinitionHash==null)) {
                var matches=collectionDefinitions.Where(c=>CollectionDisplay.MetadataMatches(known,c)).Select(CollectionDisplay.DefinitionHash).Distinct().ToArray();
                if(matches.Length==1)known.DefinitionHash=matches[0];
            }
            profile.RevealedRecipes=(profile.RevealedRecipes??Array.Empty<string>()).Concat(collectionRecipes.Where(r=>r.IsResultKnown(profile,collectionDefinitions)).Select(r=>r.Id)).Distinct().ToArray();
        }

        public bool EnsureCollection()
        {
            if(!Available())return Fail("図鑑の未保存処理を先に再試行してください");
            var next=current.Copy();UpdateCollection(next);
            if(SameDiscovery(current,next) &&
                (current.RevealedRecipes??Array.Empty<string>()).SequenceEqual(next.RevealedRecipes)){Error=null;return true;}
            return Stage(next);
        }
        public bool RecordFusion(string recipeId)
        {
            if(!Available())return Fail("図鑑の未保存処理を先に再試行してください");
            if(!collectionRecipes.Any(r=>r.Id==recipeId))return Fail("未登録の融合レシピです");
            var next=current.Copy();UpdateCollection(next);
            next.RevealedRecipes=next.RevealedRecipes.Concat(new[]{recipeId}).Distinct().ToArray();
            if(next.RevealedRecipes.SequenceEqual(current.RevealedRecipes??Array.Empty<string>())&&
                SameDiscovery(current,next)){Error=null;return true;}
            return Stage(next);
        }
        private bool Fail(string error) { Error = error; return false; }
        private bool Available() => Ready && !writing && pending == null;
        public bool Begin(Olca.Core.Logic.Dungeon.RestoredCheckpoint source, string dungeonKey, ClearReturnSettings settings)
        {
            if (!Available()) return Fail("未保存の処理を先に再試行してください");
            try {
                if (settings == null) throw new ArgumentNullException(nameof(settings));
                settings.Validate(); var dto = source?.Snapshot;
                if (dto == null || dto.Boundary != CheckpointBoundary.Terminal || dto.Player.Hp <= 0 || string.IsNullOrWhiteSpace(dungeonKey))
                    throw new ArgumentException("生存した最終クリア境界が必要です");
                var boss = source.Map.MapCells.LastOrDefault(c => c.Type == CellType.Boss)?.Enemy;
                if (boss != null ? !boss.IsDead : source.Enemies.Values.Any(e => !e.IsDead)) throw new ArgumentException("最終クリアが確定していません");
                var definitions = dto.Cards.ToDictionary(c => c.InstanceId);
                var candidates = dto.ObtainedOrder.Select(id => {
                    var card = source.Cards[id];
                    return new ReturnCardData { InstanceId = id, Id = card.Id, Name = card.Name, Rarity = card.Rarity, Level = card.ExceedLevel,
                        ValueHash = definitions[id].ValueHash, OverflowTokens = settings.Tokens(card.Rarity, card.ExceedLevel) };
                }).ToArray();
                string fingerprint = Olca.Core.Logic.Dungeon.CheckpointValues.Hash(w => {
                    w.Write(dto.RunId); w.Write(dto.Sequence); w.Write(dto.MapHash); w.Write(dungeonKey);
                    foreach (var c in candidates) { w.Write(c.InstanceId); w.Write(c.ValueHash); }
                });
                var receipt = current.Receipts.SingleOrDefault(r => r.RunId == dto.RunId);
                if (receipt != null) return receipt.Fingerprint == fingerprint || Fail("同じ攻略IDの内容が一致しません");
                if (current.Flow != null && current.Flow.Stage != ClearReturnStage.Returned)
                    return current.Flow.RunId == dto.RunId && current.Flow.Fingerprint == fingerprint || Fail("別の未完了帰還があります");
                var next = current.Copy();
                next.Flow = new ClearReturnData { RunId = dto.RunId, DungeonKey = dungeonKey, Fingerprint = fingerprint, Candidates = candidates, Settings = settings.Copy(),
                    ExperienceAward = current.ClearedDungeons.Contains(dungeonKey) ? settings.FirstClearExperience / 2 : settings.FirstClearExperience };
                return Stage(next);
            } catch (Exception e) when (e is ArgumentException || e is OverflowException) { return Fail(e.Message); }
        }
        /// <summary>終了済みRunIdを報酬なしで記録する。進行保存が確定するまで未確定。</summary>
        public bool EndWithoutRewards(Olca.Core.Logic.Dungeon.RestoredCheckpoint source, string dungeonKey,
            ClearReturnSettings settings, RunReturnOutcome outcome)
        {
            if (!Available()) return Fail("未保存の処理を先に再試行してください");
            try {
                if (settings == null) throw new ArgumentNullException(nameof(settings)); settings.Validate();
                var dto = source?.Snapshot;
                if (dto == null || string.IsNullOrWhiteSpace(dungeonKey) || !Enum.IsDefined(typeof(RunReturnOutcome), outcome) || outcome == RunReturnOutcome.Cleared ||
                    (outcome == RunReturnOutcome.Defeated ? dto.Boundary != CheckpointBoundary.Terminal || dto.Player.Hp != 0 : dto.Boundary == CheckpointBoundary.Terminal))
                    throw new ArgumentException("敗北境界または未終了攻略の撤退が必要です");
                string fingerprint = Olca.Core.Logic.Dungeon.CheckpointValues.Hash(w => {
                    w.Write("non-clear-return-v1"); w.Write(dto.RunId); w.Write(dto.Sequence); w.Write(dto.MapHash); w.Write(dungeonKey); w.Write((int)outcome);
                });
                var receipt = current.Receipts.SingleOrDefault(r => r.RunId == dto.RunId);
                if (receipt != null) return receipt.Outcome == outcome && receipt.Fingerprint == fingerprint || Fail("終了済み攻略の内容が一致しません");
                if (current.Flow != null && current.Flow.Stage != ClearReturnStage.Returned) return Fail("別の未完了帰還があります");
                var next = current.Copy();
                next.Flow = new ClearReturnData { RunId = dto.RunId, DungeonKey = dungeonKey, Fingerprint = fingerprint,
                    Outcome = outcome, Stage = ClearReturnStage.Returned, Settings = settings.Copy(), NewLevel = next.Level,
                    ReturnedMaxHp = settings.MaxHp(next.Level), ReturnedAttack = settings.Attack(next.Level) };
                next.ExchangeStock = null;
                next.ReturnedHp = next.Flow.ReturnedMaxHp;
                next.Receipts = next.Receipts.Concat(new[] { new ClearReceipt { RunId = dto.RunId, Fingerprint = fingerprint, Outcome = outcome } }).ToArray();
                return Stage(next);
            } catch (Exception e) when (e is ArgumentException || e is OverflowException) { return Fail(e.Message); }
        }

        public bool Toggle(int index)
        {
            if (!Available() || current.Flow?.Stage != ClearReturnStage.Choosing) return Fail("カードを選択できる状態ではありません");
            try {
                var next = current.Copy(); var selected = next.Flow.Selected.ToList();
                if (selected.Contains(index)) selected.Remove(index); else selected.Add(index);
                next.Flow.Selected = selected.ToArray(); ClearReturnProfile.ValidateSelection(next.Flow); return Stage(next);
            } catch (ArgumentException e) { return Fail(e.Message); }
        }
        public bool ConfirmSelection()
        {
            if (!Available() || current.Flow == null) return Fail("持ち帰り選択がありません");
            if (current.Flow.Stage != ClearReturnStage.Choosing) { Error = null; return true; }
            try {
                var next = current.Copy(); var f = next.Flow; ClearReturnProfile.ValidateSelection(f);
                NormalizeOwned(next);var owned = next.Owned.ToList(); long tokens = 0;
                foreach (int index in f.Selected) {
                    var card = f.Candidates[index]; var entry = AcquisitionOwned(next,card.Id,card.Name,card.Rarity,card.Level,card.ValueHash);
                    if(entry==null||entry.Count<4)Discover(next,card.Id,card.Name,card.Rarity,card.Level,card.ValueHash);
                    if (entry == null) owned.Add(new OwnedCardCount { Id = card.Id, Name = card.Name, Rarity = card.Rarity, Level = card.Level, Count = 1, DefinitionHash=card.ValueHash });
                    else if (entry.Count < 4) entry.Count++;
                    else tokens = checked(tokens + card.OverflowTokens);
                    next.Owned=owned.ToArray();
                }
                next.Owned = owned.ToArray(); next.Tokens = checked(next.Tokens + tokens); next.Experience = checked(next.Experience + f.ExperienceAward);
                next.Level = Math.Max(next.Level, (int)Math.Min(f.Settings.MaximumLevel, checked(1 + next.Experience / f.Settings.ExperiencePerLevel)));
                f.NewLevel = next.Level; f.ReturnedMaxHp = f.Settings.MaxHp(next.Level); f.ReturnedAttack = f.Settings.Attack(next.Level);
                f.TokenAward = tokens; f.Stage = ClearReturnStage.Awarded;
                next.ClearedDungeons = next.ClearedDungeons.Concat(new[] { f.DungeonKey }).Distinct().ToArray();
                next.Receipts = next.Receipts.Concat(new[] { new ClearReceipt { RunId = f.RunId, Fingerprint = f.Fingerprint, Experience = f.ExperienceAward, Tokens = tokens } }).ToArray();
                return Stage(next);
            } catch (Exception e) when (e is ArgumentException || e is OverflowException) { return Fail(e.Message); }
        }
        public bool ConfirmReturn()
        {
            if (!Available() || current.Flow == null || current.Flow.Stage == ClearReturnStage.Choosing) return Fail("持ち帰りを先に確定してください");
            if (current.Flow.Stage == ClearReturnStage.Returned) { Error = null; return true; }
            var next = current.Copy(); next.ExchangeStock = null; next.Flow.Stage = ClearReturnStage.Returned; next.ExchangeStock = null;
                next.ReturnedHp = next.Flow.ReturnedMaxHp; return Stage(next);
        }
        public bool PrepareNextDeparture(CheckpointDto start, string definitionKey)
        {
            if (!Available() || (current.Flow != null && current.Flow.Stage != ClearReturnStage.Returned)) return Fail("拠点で出撃してください");
            string parent = current.Flow?.RunId ?? NextDepartureData.InitialParent;
            // 同じ帰還画面の再操作は、既に確定した攻略ID/能力/デッキを使う。
            if (current.NextDeparture?.ParentRunId == parent) { Error = null; return true; }
            var next = current.Copy();
            next.NextDeparture = new NextDepartureData { ParentRunId = parent, DefinitionKey = definitionKey, Start = start?.Copy() };
            return Stage(next);
        }
                private bool ExchangeAvailable() => Available() && (current.Flow==null || current.Flow.Stage==ClearReturnStage.Returned) &&
            current.NextDeparture?.ParentRunId != (current.Flow?.RunId ?? NextDepartureData.InitialParent);
        public bool EnsureExchange(IReadOnlyList<TokenExchangeProduct> fixedProducts, IReadOnlyList<TokenExchangeProduct> randomProducts,
            int randomSlots, Olca.Core.Interfaces.IRandomProvider random)
        {
            if(!ExchangeAvailable()) return Fail("拠点・保存完了後に交換所を開いてください");
            string round=current.Flow?.RunId??NextDepartureData.InitialParent;
            if(current.ExchangeStock?.RoundId==round){Error=null;return true;}
            try {
                if(fixedProducts==null || randomProducts==null || random==null || randomSlots<0 || randomSlots>randomProducts.Count ||
                    fixedProducts.Concat(randomProducts).Any(p=>p==null) || fixedProducts.Concat(randomProducts).Select(p=>p.Id).Distinct().Count()!=fixedProducts.Count+randomProducts.Count)
                    throw new ArgumentException("重複のない交換商品と有効な抽選枠が必要です");
                var offers=fixedProducts.Select(p=>p.Capture()).ToList();var pool=randomProducts.ToList();
                for(int i=0;i<randomSlots;i++){int index=random.Range(0,pool.Count);if(index<0||index>=pool.Count)throw new ArgumentException("乱数の範囲が不正です");
                    var item=pool[index].Capture();item.Random=true;offers.Add(item);pool.RemoveAt(index);}
                var next=current.Copy();next.ExchangeStock=new ExchangeStockData{RoundId=round,Offers=offers.ToArray()};return Stage(next);
            }catch(ArgumentException e){return Fail(e.Message);}
        }
        public static bool CanExchange(ClearReturnProfile profile, ExchangeOfferData offer)
        {
            if(profile==null || offer==null || profile.Tokens<offer.Price)return false;
            long total=(profile.ExchangeTotals??Array.Empty<ExchangePurchaseTotal>()).SingleOrDefault(t=>t.ProductId==offer.Id)?.Count??0;
            return offer.Limit==ExchangeLimit.Unlimited || (offer.Limit==ExchangeLimit.PerRefresh?offer.Purchased:total)<offer.Maximum;
        }
        /// <summary>要求IDは一度の確認操作が所有する。再送は同じ領収書を返し支払/獲得を繰り返さない。</summary>
        public bool PurchaseExchange(string requestId, string roundId, string productId)
        {
            if(!ExchangeAvailable()) return Fail("拠点・保存完了後に交換してください");
            if(!Guid.TryParseExact(requestId,"N",out _))return Fail("交換要求IDが不正です");
            var receipt=(current.ExchangeReceipts??Array.Empty<ExchangeReceipt>()).SingleOrDefault(x=>x.RequestId==requestId);
            if(receipt!=null)return receipt.RoundId==roundId&&receipt.ProductId==productId || Fail("交換要求IDの内容が一致しません");
            var stock=current.ExchangeStock;var offer=stock?.Offers.SingleOrDefault(o=>o.Id==productId);
            if(stock==null || stock.RoundId!=roundId || !CanExchange(current,offer))return Fail("商品更新・売切れまたはトークン不足です");
            try {
                var next=current.Copy();var item=next.ExchangeStock.Offers.Single(o=>o.Id==productId);
                var card=AcquisitionOwned(next,item.CardId,item.Name,item.Rarity,item.Level,item.CardDefinitionHash);var owned=next.Owned.ToList();
                long overflow=0;
                if(card==null||card.Count<4)Discover(next,item.CardId,item.Name,item.Rarity,item.Level,item.CardDefinitionHash);
                if(card==null)owned.Add(new OwnedCardCount{Id=item.CardId,Name=item.Name,Rarity=item.Rarity,Level=item.Level,Count=1,DefinitionHash=item.CardDefinitionHash});
                else if(card.Name!=item.Name||card.Rarity!=item.Rarity) return Fail("既存所持と商品定義が一致しません");
                else if(card.Count<4)card.Count++;else overflow=item.OverflowTokens;
                next.Tokens=checked(next.Tokens-item.Price+overflow);next.Owned=owned.ToArray();item.Purchased=checked(item.Purchased+1);
                var totals=(next.ExchangeTotals??Array.Empty<ExchangePurchaseTotal>()).ToList();var total=totals.SingleOrDefault(t=>t.ProductId==productId);
                if(total==null)totals.Add(new ExchangePurchaseTotal{ProductId=productId,Count=1});else total.Count=checked(total.Count+1);
                next.ExchangeTotals=totals.ToArray();next.ExchangeReceipts=(next.ExchangeReceipts??Array.Empty<ExchangeReceipt>()).Concat(new[]{
                    new ExchangeReceipt{RequestId=requestId,RoundId=roundId,ProductId=productId,Paid=item.Price,OverflowTokens=overflow}}).ToArray();
                return Stage(next);
            }catch(OverflowException e){return Fail(e.Message);}
        }

        public bool SelectDepartureStyle(DepartureStyleDefinition style)
        {
            if (!Available() || (current.Flow != null && current.Flow.Stage != ClearReturnStage.Returned) ||
                current.NextDeparture?.ParentRunId == (current.Flow?.RunId ?? NextDepartureData.InitialParent)) return Fail("拠点でスタイルを選択してください");
            if (style == null || !style.IsUnlocked(current)) return Fail("未解放のスタイルです");
            string id = style.Id == DepartureStyleDefinition.BasicId ? null : style.Id;
            if (current.SelectedStyleId == id) { Error = null; return true; }
            var next = current.Copy(); next.SelectedStyleId = id; return Stage(next);
        }
        private bool DeckEditingAvailable() => Available() && current.Flow?.Stage==ClearReturnStage.Returned && current.NextDeparture?.ParentRunId!=current.Flow.RunId;
        public bool SaveDeck(string id,string name,IReadOnlyList<SavedDeckCard> cards)
        {
            if(!DeckEditingAvailable())return Fail("帰還後・保存完了後にデッキを編集してください");
            try{
                var next=current.Copy();var decks=next.SavedDecks.ToList();var prior=decks.SingleOrDefault(d=>d.Id==id);
                var saved=new SavedDepartureDeck{Id=id,Name=name?.Trim(),Cards=cards?.Select(c=>c?.Copy()).ToArray()};
                if(saved.Cards==null||saved.Cards.Any(c=>c==null))return Fail("カード参照が不正です");
                foreach(var group in saved.Cards.GroupBy(c=>(c.Id,c.Level,c.DefinitionKey)))
                    if(OwnedDefinition.Resolve(current,group.First())==null&&group.Count()>(prior?.Cards.Count(c=>c.Id==group.Key.Id&&c.Level==group.Key.Level&&c.DefinitionKey==group.Key.DefinitionKey)??0))
                        return Fail("未所持の別定義は新しく登録できません");
                if(id==next.EquippedDeckId&&!SavedDepartureDeck.TryValidateEquipped(next,saved,out var error))return Fail(error);
                if(prior!=null)decks.Remove(prior);decks.Add(saved);next.SavedDecks=decks.ToArray();return Stage(next);
            }catch(ArgumentException e){return Fail(e.Message);}
        }
        public bool EquipDeck(string id)
        {
            if(!DeckEditingAvailable())return Fail("攻略中はデッキを切り替えられません");
            var saved=(current.SavedDecks??new SavedDepartureDeck[0]).SingleOrDefault(d=>d.Id==id);
            if(!SavedDepartureDeck.TryValidateEquipped(current,saved,out var error))return Fail(error);
            if(current.EquippedDeckId==id){Error=null;return true;}
            var next=current.Copy();next.EquippedDeckId=id;return Stage(next);
        }
        public bool DeleteDeck(string id)
        {
            if(!DeckEditingAvailable())return Fail("攻略中はデッキを削除できません");
            if(current.EquippedDeckId==id)return Fail("装備中の削除と自動切替は未対応です。先に別のデッキを装備してください");
            var next=current.Copy();if(!next.SavedDecks.Any(d=>d.Id==id))return Fail("保存デッキがありません");
            next.SavedDecks=next.SavedDecks.Where(d=>d.Id!=id).ToArray();return Stage(next);
        }
        public PlayerCharacter ReturnedPlayer {
            get {
                if(current?.Flow?.Stage!=ClearReturnStage.Returned)return null;
                var player=new PlayerCharacter(current.Flow.ReturnedMaxHp,attackPower:current.Flow.ReturnedAttack);
                player.SetAttackGrowth(AttackMilestoneGrowth.ForLevel(current.Level));return player;
            }
        }
        private bool Stage(ClearReturnProfile next)
        {
            try { NormalizeOwned(next); UpdateCollection(next); next.Revision = checked(current.Revision + 1); next.CommitId = Guid.NewGuid().ToString("N"); ClearReturnProfile.Validate(next); }
            catch (Exception e) when (e is ArgumentException || e is OverflowException) { return Fail(e.Message); }
            pending = next.Copy(); return Retry();
        }
        public bool Retry()
        {
            if (writing) return Fail("保存中です");
            if (pending == null) return Reload();
            writing = true;
            try {
                if (!store.TryWrite(current.Revision, pending.Copy(), out var error)) return Fail(error);
                current = pending; pending = null; Error = null; return true;
            } finally { writing = false; }
        }
    }
}
