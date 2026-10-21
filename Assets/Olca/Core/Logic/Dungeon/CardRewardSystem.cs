using System.Linq;
using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Dungeon
{
    /// <summary>
    /// カード報酬管理システム
    /// バトル後のカードドロップと選択を管理
    /// </summary>
    public class CardRewardSystem
    {
        private readonly Deck dungeonDeck;  // 攻略中のデッキ
        private readonly IRandomProvider random;
        private readonly ICardPool cardPool;  // カードプール（nullの場合はフォールバック生成）
        private readonly List<Card> obtainedCards;  // 入手したカード（クリア報酬用）

        private static readonly string[] FallbackCardIds = { "sword", "heal", "shield", "fire", "ice" };
        private const int REWARD_CHOICE_COUNT = 3;  // 選択肢の枚数
        private const float NORMAL_ENEMY_DROP_RATE = 0.5f;  // 雑魚戦の基本ドロップ率（50%）

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="initialDeck">初期デッキ（コピーして管理）</param>
        /// <param name="random">乱数プロバイダー</param>
        /// <param name="cardPool">カードプール（省略時はダミーカードを生成）</param>
        public CardRewardSystem(Deck initialDeck, IRandomProvider random, ICardPool cardPool = null)
        {
            // 初期デッキをコピー（元のデッキは変更しない）
            dungeonDeck = new Deck(new List<Card>(initialDeck.GetAllCards()));
            this.random = random;
            this.cardPool = cardPool;
            obtainedCards = new List<Card>();
        }

        /// <summary>
        /// カードドロップ判定
        /// </summary>
        public bool ShouldDropCard(Enemy defeatedEnemy)
        {
            if (defeatedEnemy == null) return false;

            // ボスは100%ドロップ
            if (defeatedEnemy.EnemyId == "boss")
            {
                return true;
            }

            // 雑魚戦は戦闘ごとに1回、基本50%の確率でドロップ
            float roll = random.Range(0f, 1f);
            return roll < NORMAL_ENEMY_DROP_RATE;
        }

        /// <summary>
        /// カード選択肢を生成
        /// </summary>
        public List<Card> GenerateCardChoices(Enemy defeatedEnemy)
        {
            if (cardPool != null)
            {
                if (TryGenerateConfiguredChoices(defeatedEnemy, out var configured)) return configured;
                throw new InvalidOperationException("報酬プールの有効候補が不足しています。通常は異なる3候補、ボスは1候補以上が必要です。");
            }
            bool boss = defeatedEnemy.EnemyId == "boss";
            var definitions = new List<Card>();
            foreach (string id in FallbackCardIds)
                definitions.Add(CreateFallbackCard(id, boss ? CardRarity.Limited : CardRarity.Normal));
            // 通常シーンの未設定プールも同じ重み付き抽選へ通す。旧値と乱数呼出し順は維持。
            TryDrawChoices(definitions, null, boss ? 1 : REWARD_CHOICE_COUNT, out var choices);
            return choices;
        }

        /// <summary>
        /// 設定済み候補から重み付き・重複なしで抽選する。
        /// 重み未指定は1。候補不足・不正重み・合計上限超過は乱数を消費せず失敗する。
        /// </summary>
        public bool TryGenerateConfiguredChoices(Enemy defeatedEnemy, out List<Card> choices)
        {
            choices = new List<Card>();
            if (cardPool == null || defeatedEnemy == null) return false;
            bool boss = defeatedEnemy.EnemyId == "boss";
            return TryDrawChoices(cardPool.GetCandidates(boss ? CardRarity.Limited : CardRarity.Normal),
                cardPool as IWeightedCardPool, boss ? 1 : REWARD_CHOICE_COUNT, out choices,
                REWARD_CHOICE_COUNT);
        }

        private bool TryDrawChoices(IReadOnlyList<Card> definitions, IWeightedCardPool weightedPool,
            int minimum, out List<Card> choices, int maximum = 0)
        {
            choices = new List<Card>();
            var candidates = new List<Card>();
            var weights = new List<int>();
            var seen = new Dictionary<(string, ExceedLevel), int>();
            long total = 0;
            if (definitions != null)
            {
                foreach (var card in definitions)
                {
                    if (card == null || string.IsNullOrEmpty(card.Id)) continue;
                    int weight = weightedPool?.GetWeight(card) ?? 1;
                    if (weight < 0) return false;
                    var key = (card.Id, card.ExceedLevel);
                    if (seen.TryGetValue(key, out int previous))
                    {
                        // 重複登録で確率を増やさない。競合する重みは設定誤りとして拒否。
                        if (previous != weight) return false;
                        continue;
                    }
                    seen.Add(key, weight);
                    if (weight == 0) continue;
                    total += weight;
                    if (total > int.MaxValue) return false;
                    candidates.Add(card); weights.Add(weight);
                }
            }
            if (candidates.Count < minimum) return false;
            int count = maximum == 0 ? minimum : Math.Min(maximum, candidates.Count);
            for (int i = 0; i < count; i++)
            {
                int roll = random.Range(0, (int)total);
                int index = 0;
                while (roll >= weights[index]) { roll -= weights[index]; index++; }
                choices.Add(candidates[index].Clone());
                total -= weights[index];
                candidates.RemoveAt(index); weights.RemoveAt(index);
            }
            return true;
        }

        /// <summary>実抽選と同じ既存フォールバック定義。図鑑用の別カードを作らない。</summary>
        public static IReadOnlyList<Card> FallbackDefinitions => new[]{CardRarity.Normal,CardRarity.Limited}
            .SelectMany(r=>FallbackCardIds.Select(id=>CreateFallbackCard(id,r))).ToArray();

        public static Card CreateFallbackCard(string cardId, CardRarity targetRarity)
        {
            return new Card(
                id: cardId,
                name: $"{cardId}_card",
                cost: 1,
                rarity: targetRarity,
                category: CardCategory.Attack,
                element: ElementType.None,
                series: CardSeries.Sword,
                power: 30,
                description: "Fallback card"
            );
        }

        /// <summary>
        /// カードを選択してデッキに追加
        /// </summary>
        internal void RestoreObtained(IEnumerable<Card> history)
        {
            obtainedCards.Clear(); obtainedCards.AddRange(history);
        }

        public void SelectCard(Card selectedCard)
        {
            if (selectedCard == null)
            {
                throw new ArgumentNullException(nameof(selectedCard));
            }

            dungeonDeck.AddCard(selectedCard);
            obtainedCards.Add(selectedCard);

            OnCardAdded?.Invoke(selectedCard);
        }

        /// <summary>
        /// 攻略中のデッキを取得
        /// </summary>
        public Deck GetCurrentDeck() => dungeonDeck;

        /// <summary>
        /// 入手したカードのリスト（クリア報酬用）
        /// </summary>
        public IReadOnlyList<Card> ObtainedCards => obtainedCards.AsReadOnly();

        /// <summary>
        /// カード追加イベント
        /// </summary>
        public event Action<Card> OnCardAdded;
    }
    /// <summary>
    /// 戦闘開始時の通貨対象と外部定義額を固定する。受領・保存・勝敗時の扱いは所有しない。
    /// </summary>
    public sealed class BattleCurrencyRewardSnapshot
    {
        private readonly List<KeyValuePair<Enemy, int>> initialTargets = new List<KeyValuePair<Enemy, int>>();

        public BattleCurrencyRewardSnapshot(IEnumerable<Enemy> initialParticipants, Func<Enemy, int> amountForEnemy)
        {
            if (initialParticipants == null) throw new ArgumentNullException(nameof(initialParticipants));
            if (amountForEnemy == null) throw new ArgumentNullException(nameof(amountForEnemy));
            var seen = new HashSet<Enemy>();
            foreach (var enemy in initialParticipants)
            {
                if (enemy == null || !seen.Add(enemy)) throw new ArgumentException("初期参加者がnullまたは重複しています", nameof(initialParticipants));
                // 召喚物には通貨定義を要求せず、初期配置でも集計対象から除外する。
                if (enemy.IsSummoned) continue;
                int amount = amountForEnemy(enemy);
                if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amountForEnemy), "通貨額は非負です");
                initialTargets.Add(new KeyValuePair<Enemy, int>(enemy, amount));
            }
        }

        /// <summary>確定済みの死亡だけを合計する。0なら通貨報酬の提示対象はない。</summary>
        public long GetDefeatedEnemyTotal()
        {
            long total = 0;
            foreach (var target in initialTargets)
                if (target.Key.IsDead) total += target.Value;
            return total;
        }
    }

}

namespace Olca.Core.Domain
{
    /// <summary>既存アイテム効果の抽選重み。製品の新しい種類は作らない。</summary>
    public sealed class ItemRewardEntry
    {
        public FieldItem Item { get; }
        public int Weight { get; }
        public ItemRewardEntry(FieldItem item,int weight=1){if(item==null||weight<0)throw new System.ArgumentException("アイテム/重みが不正です");Item=item;Weight=weight;}
        public static void Validate(System.Collections.Generic.IReadOnlyList<ItemRewardEntry> pool)
        {
            if(pool==null)throw new System.ArgumentNullException(nameof(pool));
            var seen=new System.Collections.Generic.HashSet<string>();long total=0;
            foreach(var entry in pool){if(entry==null||!seen.Add(entry.Item.Id))throw new System.ArgumentException("アイテム候補の欠落/重複です");total+=entry.Weight;if(total>int.MaxValue)throw new System.ArgumentException("重み合計が上限を超えています");}
        }
        public static int? Pick(System.Collections.Generic.IReadOnlyList<ItemRewardEntry> pool,int chance,Olca.Core.Interfaces.IRandomProvider random)
        {
            if(random==null)throw new System.ArgumentNullException(nameof(random));
            Validate(pool);if(chance<0||chance>100)throw new System.ArgumentOutOfRangeException(nameof(chance));
            int total=0;foreach(var entry in pool)total+=entry.Weight;if(total==0||chance==0)return null;
            if(chance<100&&random.Range(0,100)>=chance)return null;
            int roll=random.Range(0,total);for(int i=0;i<pool.Count;i++){if(roll<pool[i].Weight)return i;roll-=pool[i].Weight;}return null;
        }
    }
}
