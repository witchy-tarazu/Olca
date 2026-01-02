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
        private int currentCardIndex; // 現在使用中のカードのインデックス
        private CancellationTokenSource currentCardCts; // 現在実行中のカード効果のCancellationTokenSource

        /// <summary>使用待ちカードのリスト</summary>
        public IReadOnlyList<Card> QueuedCards => queuedCards.AsReadOnly();

        /// <summary>現在使用可能なカード（先頭のカード）</summary>
        public Card CurrentCard => currentCardIndex < queuedCards.Count ? queuedCards[currentCardIndex] : null;

        /// <summary>使用待ちカードの枚数</summary>
        public int RemainingCount => queuedCards.Count - currentCardIndex;

        /// <summary>すべてのカードを使用済みか</summary>
        public bool IsEmpty => currentCardIndex >= queuedCards.Count;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public CardUsageSystem(BattleSystem battleSystem, IGameLogger logger = null)
        {
            this.battleSystem = battleSystem ?? throw new ArgumentNullException(nameof(battleSystem));
            this.queuedCards = new List<Card>();
            this.logger = logger;
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
            if (IsEmpty)
            {
                logger?.LogWarning("使用可能なカードがありません");
                return false;
            }

            var card = CurrentCard;

            // カード効果を実行
            ExecuteCardEffect(card);

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
            // BattleContextを作成
            var context = new BattleContext(battleSystem, card);

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

        /// <summary>
        /// カード効果を非同期で実行
        /// </summary>
        private async UniTask ExecuteCardEffectAsync(Card card, CancellationToken ct)
        {
            // BattleContextを作成
            var context = new BattleContext(battleSystem, card);

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
            if (IsEmpty)
            {
                logger?.LogWarning("使用可能なカードがありません");
                return false;
            }

            var card = CurrentCard;

            // 新しいCTSを作成
            currentCardCts?.Cancel();
            currentCardCts?.Dispose();
            currentCardCts = new CancellationTokenSource();

            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                currentCardCts.Token);

            try
            {
                // カード効果を実行
                await ExecuteCardEffectAsync(card, linkedCts.Token);

                // 次のカードに進む
                currentCardIndex++;

                logger?.LogInfo($"カード使用: {card.Name} (残り{RemainingCount}枚)");
                OnCardUsed?.Invoke(card);

                return true;
            }
            catch (OperationCanceledException)
            {
                // カード効果が中断された
                logger?.LogWarning($"カード効果が中断されました: {card.Name}");
                return false;
            }
            finally
            {
                linkedCts?.Dispose();
            }
        }

        /// <summary>
        /// 現在実行中のカード効果を中断します（のけぞり時）
        /// </summary>
        public void InterruptCurrentCard()
        {
            currentCardCts?.Cancel();
        }

        /// <summary>
        /// キューをクリア（ターン終了時など）
        /// </summary>
        public void Clear()
        {
            queuedCards.Clear();
            currentCardIndex = 0;
            OnQueueCleared?.Invoke();
        }

        /// <summary>
        /// 特定のカードをスキップ（使用せずに次へ）
        /// </summary>
        public bool SkipCurrentCard()
        {
            if (IsEmpty) return false;

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
