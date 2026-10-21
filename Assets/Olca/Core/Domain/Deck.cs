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
        public string Id, Name;
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
        public string Id;
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
            var cards=new List<Card>();
            foreach(var slot in saved.Cards){
                var owned=profile.Owned.SingleOrDefault(o=>o.Id==slot.Id&&o.Level==slot.Level);
                if(owned==null){error="未所持カードを装備できません";return false;}
                cards.Add(new Card(owned.Id,owned.Name,0,owned.Rarity,CardCategory.Attack,ElementType.None,CardSeries.None,0,"",exceedLevel:owned.Level));
            }
            return DepartureDeckRules.TryCreate(cards,profile.Owned.ToDictionary(o=>(o.Id,o.Level),o=>o.Count),out _,out error);
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
                Price=price, OverflowTokens=overflowTokens, Limit=limit, Maximum=maximum };
            ExchangeOfferData.Validate(data);
        }
        public ExchangeOfferData Capture() => data.Copy();
    }
    public sealed class ExchangeOfferData
    {
        public string Id, CardId, Name;
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
        public string SelectedStyleId; // 旧省略/nullは基本スタイル
        public ClearReturnProfile Copy() => new ClearReturnProfile {
            Version = Version, Revision = Revision, Experience = Experience, Tokens = Tokens, Level = Level, ReturnedHp = ReturnedHp, CommitId = CommitId,
            Owned = Owned?.Select(c => c?.Copy()).ToArray(), ClearedDungeons = ClearedDungeons?.ToArray(),
            Receipts = Receipts?.Select(r => r?.Copy()).ToArray(), Flow = Flow?.Copy(), NextDeparture = NextDeparture?.Copy(), EquippedDeckId=EquippedDeckId, SelectedStyleId=SelectedStyleId, ExchangeStock=ExchangeStock?.Copy(),
            ExchangeTotals=ExchangeTotals?.Select(t=>t?.Copy()).ToArray(), ExchangeReceipts=ExchangeReceipts?.Select(t=>t?.Copy()).ToArray(),
            SavedDecks=(SavedDecks??new SavedDepartureDeck[0]).Select(d=>d?.Copy()).ToArray() };
        public static void Validate(ClearReturnProfile p)
        {
            if (p == null || p.Version != 1 || p.Revision < 0 || p.Experience < 0 || p.Tokens < 0 || p.Level < 1 || p.ReturnedHp < 0 ||
                (p.Revision > 0 && string.IsNullOrWhiteSpace(p.CommitId)) || p.Owned == null || p.ClearedDungeons == null || p.Receipts == null)
                throw new ArgumentException("進行データの版・基本値が不正です");
            if (p.SelectedStyleId != null && string.IsNullOrWhiteSpace(p.SelectedStyleId)) throw new ArgumentException("選択スタイルIDが不正です");
            if (p.Owned.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) || c.Count < 1 || c.Count > 4 ||
                !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.Level)) ||
                p.Owned.GroupBy(c => (c.Id, c.Level)).Any(g => g.Count() > 1) ||
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
    public interface IClearReturnStore
    {
        ClearReturnProfile Read(out string error);
        bool TryWrite(long expectedRevision, ClearReturnProfile next, out string error);
    }
    // 保存成功までは現状態を変更しない。失敗後も同じ計画を再試行し、受領済みRunIdを再計上しない。
    public sealed class ClearReturnSession
    {
        private readonly IClearReturnStore store;
        private ClearReturnProfile current, pending;
        private bool writing;
        public bool Ready { get; private set; }
        public string Error { get; private set; }
        public bool HasPendingSave => pending != null;
        public ClearReturnProfile Snapshot => current?.Copy();
        public ClearReturnData DisplayFlow => (pending?.Flow != null && pending.Flow.RunId != current?.Flow?.RunId
            ? pending.Flow : current?.Flow ?? pending?.Flow)?.Copy();
        public ClearReturnSession(IClearReturnStore store)
        {
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
                var owned = next.Owned.ToList(); long tokens = 0;
                foreach (int index in f.Selected) {
                    var card = f.Candidates[index]; var entry = owned.SingleOrDefault(c => c.Id == card.Id && c.Level == card.Level);
                    if (entry == null) owned.Add(new OwnedCardCount { Id = card.Id, Name = card.Name, Rarity = card.Rarity, Level = card.Level, Count = 1 });
                    else if (entry.Count < 4) entry.Count++;
                    else tokens = checked(tokens + card.OverflowTokens);
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
                var owned=next.Owned.ToList();var card=owned.SingleOrDefault(c=>c.Id==item.CardId&&c.Level==item.Level);
                long overflow=0;
                if(card==null)owned.Add(new OwnedCardCount{Id=item.CardId,Name=item.Name,Rarity=item.Rarity,Level=item.Level,Count=1});
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
                foreach(var group in saved.Cards.GroupBy(c=>(c.Id,c.Level)))
                    if(!current.Owned.Any(o=>o.Id==group.Key.Id&&o.Level==group.Key.Level)&&group.Count()>(prior?.Cards.Count(c=>c.Id==group.Key.Id&&c.Level==group.Key.Level)??0))
                        return Fail("未所持カードは新しく登録できません");
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
        public PlayerCharacter ReturnedPlayer => current?.Flow?.Stage == ClearReturnStage.Returned
            ? new PlayerCharacter(current.Flow.ReturnedMaxHp, attackPower: current.Flow.ReturnedAttack) : null;
        private bool Stage(ClearReturnProfile next)
        {
            try { next.Revision = checked(current.Revision + 1); next.CommitId = Guid.NewGuid().ToString("N"); ClearReturnProfile.Validate(next); }
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
