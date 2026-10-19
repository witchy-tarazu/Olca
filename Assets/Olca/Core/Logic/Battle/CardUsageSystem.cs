using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Core.Logic.CardEffects;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// カード使用システム
    /// 選択されたカードを順番に使用する
    /// </summary>
    public class CardUsageSystem
    {
        private readonly BattleSystem battleSystem;
        private readonly List<Card> queuedCards; // 使用待ちカードのキュー
        private readonly IGameLogger logger;
        private readonly ICardDatabase cardDatabase;
        private int currentCardIndex; // 次に使用する待機カードのインデックス
        private bool isUsingCard;
        private Card inFlightCard;

        /// <summary>動作中のカード。選び直す待機キューには含めない。</summary>
        public Card InFlightCard => inFlightCard;
        private bool interruptedByStagger;
        private int queueVersion;
        private CancellationTokenSource currentCardCts; // 現在実行中のカード効果のCancellationTokenSource

        /// <summary>使用待ちカードのリスト</summary>
        public IReadOnlyList<Card> QueuedCards => queuedCards.AsReadOnly();

        /// <summary>未使用範囲を使用順のまま複製して取得する</summary>
        public IReadOnlyList<Card> GetRemainingCards()
        {
            return queuedCards.GetRange(currentCardIndex, RemainingCount).AsReadOnly();
        }

        /// <summary>現在使用可能なカード（先頭のカード）</summary>
        public Card CurrentCard => currentCardIndex < queuedCards.Count ? queuedCards[currentCardIndex] : null;

        /// <summary>使用待ちカードの枚数</summary>
        public int RemainingCount => queuedCards.Count - currentCardIndex;

        /// <summary>すべてのカードを使用済みか</summary>
        public bool IsEmpty => currentCardIndex >= queuedCards.Count;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public CardUsageSystem(BattleSystem battleSystem, IGameLogger logger = null, ICardDatabase cardDatabase = null)
        {
            this.battleSystem = battleSystem ?? throw new ArgumentNullException(nameof(battleSystem));
            this.queuedCards = new List<Card>();
            this.logger = logger;
            this.cardDatabase = cardDatabase;
            this.currentCardIndex = 0;
        }

        /// <summary>
        /// カスタム画面で選択したカードをキューに追加
        /// </summary>
        public void EnqueueCards(List<Card> selectedCards)
        {
            if (selectedCards == null) throw new ArgumentNullException(nameof(selectedCards));

            // 既存のキューをクリアして新しいカードを追加
            queuedCards.Clear();
            queuedCards.AddRange(selectedCards);
            currentCardIndex = 0;

            logger?.LogInfo($"{selectedCards.Count}枚のカードをキューに追加");
            OnCardsQueued?.Invoke(selectedCards);
        }

        /// <summary>
        /// 現在のカード（先頭のカード）を使用
        /// </summary>
        public bool TryUseCurrentCard()
        {
            if (IsEmpty || isUsingCard || battleSystem.IsGameOver || battleSystem.IsPaused)
            {
                logger?.LogWarning("使用可能なカードがありません");
                return false;
            }

            var card = CurrentCard;

            int version = queueVersion;
            // カード効果を実行
            ExecuteCardEffect(card);
            if (version != queueVersion) return true; // 効果中に戦闘が終了した場合は新キューへ触れない。

            // 次のカードに進む
            currentCardIndex++;

            logger?.LogInfo($"カード使用: {card.Name} (残り{RemainingCount}枚)");
            OnCardUsed?.Invoke(card);

            return true;
        }

        /// <summary>
        /// カード効果を実行
        /// </summary>
        private void ExecuteCardEffect(Card card)
        {
            // 攻撃カードの使用時にだけ集中を消費し、今回の効果用コピーへ適用する。
            card = ApplyConcentrate(card);

            // BattleContextを作成
            var context = new BattleContext(battleSystem, card) { CardDatabase = cardDatabase };

            // カード効果を取得して実行
            var effect = CardEffectFactory.CreateEffect(card);
            if (effect != null)
            {
                effect.Execute(context);
                logger?.LogInfo($"カード効果実行: {card.Name}");
            }
            else
            {
                logger?.LogWarning($"カード効果が見つかりません: {card.Name}");
            }
        }

        private Card ApplyConcentrate(Card card)
        {
            if (card.Category != CardCategory.Attack || !battleSystem.Player.IsConcentrating)
                return card;

            return card.WithPowerMultiplier(battleSystem.Player.ConsumeConcentrateBuff());
        }

        /// <summary>
        /// カード効果を非同期で実行
        /// </summary>
        private async UniTask ExecuteCardEffectAsync(Card card, CancellationToken ct)
        {
            // 攻撃カードの使用時にだけ集中を消費し、今回の効果用コピーへ適用する。
            card = ApplyConcentrate(card);

            // BattleContextを作成
            var context = new BattleContext(battleSystem, card) { CardDatabase = cardDatabase };

            // カード効果を取得して実行
            var effect = CardEffectFactory.CreateEffect(card);
            if (effect != null)
            {
                if (effect is IAsyncCardEffect asyncEffect)
                {
                    await asyncEffect.ExecuteAsync(context, ct);
                }
                else
                {
                    effect.Execute(context);
                }
                logger?.LogInfo($"カード効果実行: {card.Name}");
            }
            else
            {
                logger?.LogWarning($"カード効果が見つかりません: {card.Name}");
            }
        }

        /// <summary>
        /// 現在のカード（先頭のカード）を非同期で使用
        /// </summary>
        public async UniTask<bool> TryUseCurrentCardAsync(CancellationToken ct = default)
        {
            if (IsEmpty || isUsingCard || battleSystem.IsGameOver || battleSystem.IsPaused)
            {
                logger?.LogWarning("使用可能なカードがありません");
                return false;
            }

            var card = CurrentCard;

            int version = queueVersion;
            currentCardIndex++; // 実行中の一枚を待機範囲から取り出す。
            inFlightCard = card;
            isUsingCard = true;
            interruptedByStagger = false;
            var useCts = new CancellationTokenSource();
            currentCardCts = useCts;

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                useCts.Token);

            try
            {
                // カード効果を実行
                await ExecuteCardEffectAsync(card, linkedCts.Token);

                return ConsumeAsyncCard(card, version);
            }
            catch (OperationCanceledException)
            {
                // 全体仕様第5節: 強靭度による中断は発動前でも消費する。
                // 外部トークンのキャンセルは意味が未定義のため、従来の未消費を維持する。
                logger?.LogWarning($"カード効果が中断されました: {card.Name}");
                if (interruptedByStagger && !ct.IsCancellationRequested)
                    return ConsumeAsyncCard(card, version);
                // 外部キャンセルは従来どおり未消費。明示Clear後は復元しない。
                if (version == queueVersion && !battleSystem.IsGameOver)
                {
                    queuedCards.Insert(currentCardIndex, card);
                    battleSystem.CustomScreen.RestoreUnusedCard(card);
                }
                return false;
            }
            finally
            {
                linkedCts.Dispose();
                useCts.Dispose();
                currentCardCts = null;
                isUsingCard = false;
                inFlightCard = null;
                interruptedByStagger = false;
            }
        }

        private bool ConsumeAsyncCard(Card card, int version)
        {
            // 待機キューの選び直しは実行中カードに影響しない。Clearだけが実行を無効化する。
            if (version != queueVersion) return false;
            logger?.LogInfo($"カード使用: {card.Name} (残り{RemainingCount}枚)");
            OnCardUsed?.Invoke(card);
            return true;
        }

        /// <summary>
        /// 現在実行中のカード効果を中断します（のけぞり時）
        /// </summary>
        public void InterruptCurrentCard()
        {
            if (!isUsingCard) return;
            interruptedByStagger = true;
            currentCardCts?.Cancel();
        }

        /// <summary>
        /// キューをクリア（ターン終了時など）
        /// </summary>
        public void Clear()
        {
            queueVersion++;
            queuedCards.Clear();
            currentCardIndex = 0;
            currentCardCts?.Cancel();
            OnQueueCleared?.Invoke();
        }

        /// <summary>
        /// 特定のカードをスキップ（使用せずに次へ）
        /// </summary>
        public bool SkipCurrentCard()
        {
            if (IsEmpty || isUsingCard || battleSystem.IsGameOver || battleSystem.IsPaused) return false;

            var skippedCard = CurrentCard;
            currentCardIndex++;

            logger?.LogInfo($"カードをスキップ: {skippedCard.Name}");
            OnCardSkipped?.Invoke(skippedCard);

            return true;
        }

        /// <summary>
        /// カードがキューに追加された時のイベント
        /// </summary>
        public event Action<List<Card>> OnCardsQueued;

        /// <summary>
        /// カードが使用された時のイベント
        /// </summary>
        public event Action<Card> OnCardUsed;

        /// <summary>
        /// カードがスキップされた時のイベント
        /// </summary>
        public event Action<Card> OnCardSkipped;

        /// <summary>
        /// キューがクリアされた時のイベント
        /// </summary>
        public event Action OnQueueCleared;
    }
}
