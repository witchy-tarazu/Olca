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
                int index = random.Range(0, availableCards.Count);
                Card drawnCard = availableCards[index];

                result.Add(drawnCard);
                drawnCards.Add(drawnCard);
                availableCards.RemoveAt(index);
            }

            return result;
        }

        /// <summary>
        /// デッキをシャッフル（ドロー履歴をリセット）
        /// </summary>
        public void Shuffle(IRandomProvider random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            drawnCards.Clear();

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
            drawnCards.Clear();
            drawnCards.AddRange(restored);
        }

        /// <summary>ドロー履歴をリセット（再度すべてのカードをドロー可能にする）</summary>
        public void ResetDrawHistory()
        {
            drawnCards.Clear();
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
        public long Experience, Tokens;
        public ClearReceipt Copy() => (ClearReceipt)MemberwiseClone();
    }
    public sealed class ClearReturnData
    {
        public string RunId, DungeonKey, Fingerprint;
        public ClearReturnStage Stage;
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
        public ClearReturnProfile Copy() => new ClearReturnProfile {
            Version = Version, Revision = Revision, Experience = Experience, Tokens = Tokens, Level = Level, ReturnedHp = ReturnedHp, CommitId = CommitId,
            Owned = Owned?.Select(c => c?.Copy()).ToArray(), ClearedDungeons = ClearedDungeons?.ToArray(),
            Receipts = Receipts?.Select(r => r?.Copy()).ToArray(), Flow = Flow?.Copy() };
        public static void Validate(ClearReturnProfile p)
        {
            if (p == null || p.Version != 1 || p.Revision < 0 || p.Experience < 0 || p.Tokens < 0 || p.Level < 1 || p.ReturnedHp < 0 ||
                (p.Revision > 0 && string.IsNullOrWhiteSpace(p.CommitId)) || p.Owned == null || p.ClearedDungeons == null || p.Receipts == null)
                throw new ArgumentException("進行データの版・基本値が不正です");
            if (p.Owned.Any(c => c == null || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) || c.Count < 1 || c.Count > 4 ||
                !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.Level)) ||
                p.Owned.GroupBy(c => (c.Id, c.Level)).Any(g => g.Count() > 1) ||
                p.ClearedDungeons.Any(string.IsNullOrWhiteSpace) || p.ClearedDungeons.Distinct().Count() != p.ClearedDungeons.Length ||
                p.Receipts.Any(r => r == null || string.IsNullOrWhiteSpace(r.RunId) || string.IsNullOrWhiteSpace(r.Fingerprint) || r.Experience < 0 || r.Tokens < 0) ||
                p.Receipts.Select(r => r.RunId).Distinct().Count() != p.Receipts.Length)
                throw new ArgumentException("所持カードまたは受領履歴が不正です");
            var f = p.Flow; if (f == null) return;
            if (string.IsNullOrWhiteSpace(f.RunId) || string.IsNullOrWhiteSpace(f.DungeonKey) || string.IsNullOrWhiteSpace(f.Fingerprint) ||
                !Enum.IsDefined(typeof(ClearReturnStage), f.Stage) || f.Candidates == null || f.Selected == null || f.Settings == null || f.ExperienceAward < 0 || f.TokenAward < 0)
                throw new ArgumentException("帰還段階が不正です");
            f.Settings.Validate();
            if (f.Candidates.Any(c => c == null || string.IsNullOrWhiteSpace(c.InstanceId) || string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Name) ||
                string.IsNullOrWhiteSpace(c.ValueHash) || !Enum.IsDefined(typeof(CardRarity), c.Rarity) || !Enum.IsDefined(typeof(ExceedLevel), c.Level) || c.OverflowTokens <= 0) ||
                f.Candidates.Select(c => c.InstanceId).Distinct().Count() != f.Candidates.Length)
                throw new ArgumentException("持ち帰り候補が不正です");
            ValidateSelection(f);
            var receipt = p.Receipts.SingleOrDefault(r => r.RunId == f.RunId);
            if (f.Stage == ClearReturnStage.Choosing ? receipt != null : receipt == null || receipt.Fingerprint != f.Fingerprint ||
                receipt.Experience != f.ExperienceAward || receipt.Tokens != f.TokenAward || f.NewLevel < 1 || f.ReturnedMaxHp <= 0 || f.ReturnedAttack < 0)
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
            var next = current.Copy(); next.Flow.Stage = ClearReturnStage.Returned; next.ReturnedHp = next.Flow.ReturnedMaxHp; return Stage(next);
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
