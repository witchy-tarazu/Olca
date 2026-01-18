using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// カスタム画面システム
    /// カードの選択とデッキ操作を管理
    /// </summary>
    public class CustomScreenSystem
    {
        private readonly Deck deck;
        private readonly List<Card> currentHand; // 現在表示されているカード
        private readonly List<Card> selectedCards; // カスタム画面で今回選択中のカード（確定前）
        private readonly List<Card> availableCards; // バトル中に使用可能なカード（確定済み）
        private int maxHandSize;
        private int maxSelectionCount; // 最大選択枚数
        private int maxCost; // 最大コスト

        /// <summary>現在の手札</summary>
        public IReadOnlyList<Card> CurrentHand => currentHand.AsReadOnly();

        /// <summary>カスタム画面で選択中のカード</summary>
        public IReadOnlyList<Card> SelectedCards => selectedCards.AsReadOnly();

        /// <summary>バトル中に使用可能なカード（確定済みのカードのみ）</summary>
        public IReadOnlyList<Card> AvailableCards => availableCards.AsReadOnly();

        /// <summary>手札上限</summary>
        public int MaxHandSize => maxHandSize;

        /// <summary>最大選択枚数</summary>
        public int MaxSelectionCount => maxSelectionCount;

        /// <summary>最大コスト</summary>
        public int MaxCost => maxCost;

        /// <summary>残りコスト</summary>
        public int RemainingCost => maxCost - GetTotalUsedCost();

        /// <summary>カスタム画面が開いているか</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public CustomScreenSystem(Deck deck, int maxHandSize = 10, int maxSelectionCount = 5, int maxCost = 10)
        {
            this.deck = deck ?? throw new ArgumentNullException(nameof(deck));
            this.currentHand = new List<Card>();
            this.selectedCards = new List<Card>();
            this.availableCards = new List<Card>();
            this.maxHandSize = maxHandSize;
            this.maxSelectionCount = maxSelectionCount;
            this.maxCost = maxCost;
        }

        /// <summary>
        /// カスタム画面を開く（バトルシステムから未使用カードを受け取る）
        /// </summary>
        /// <param name="remainingCards">バトル中に未使用だったカード</param>
        public void Open(IEnumerable<Card> remainingCards, IRandomProvider random)
        {
            if (IsOpen) return;
            if (remainingCards == null) throw new ArgumentNullException(nameof(remainingCards));

            IsOpen = true;

            // バトルシステムから受け取った未使用カードを保持
            availableCards.Clear();
            availableCards.AddRange(remainingCards);

            // 仕様: 選択しなかったカードは次のターンも手札に残る
            // 手札が上限になるまでドロー（既存の手札枚数を考慮）
            int drawCount = maxHandSize - currentHand.Count;
            if (drawCount > 0)
            {
                var drawnCards = deck.DrawCards(drawCount, random);
                currentHand.AddRange(drawnCards);
            }

            OnScreenOpened?.Invoke(currentHand);
        }

        /// <summary>
        /// カードを選択
        /// </summary>
        public bool TrySelectCard(Card card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (!currentHand.Contains(card)) return false;

            // 選択枚数制限チェック
            if (selectedCards.Count >= maxSelectionCount)
            {
                OnSelectionFailed?.Invoke($"最大{maxSelectionCount}枚まで選択できます");
                return false;
            }

            // コスト制限チェック
            int currentCost = GetTotalUsedCost();
            int cardCost = CalculateCardCost(card);
            if (currentCost + cardCost > maxCost)
            {
                OnSelectionFailed?.Invoke($"コストが上限（{maxCost}）を超えます");
                return false;
            }

            // 選択成功
            selectedCards.Add(card);
            currentHand.Remove(card);

            OnCardSelected?.Invoke(card);
            return true;
        }


        /// <summary>
        /// カードの選択を解除
        /// </summary>
        public bool TryDeselectCard(Card card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            if (!selectedCards.Contains(card)) return false;

            selectedCards.Remove(card);
            currentHand.Add(card);

            OnCardDeselected?.Invoke(card);
            return true;
        }

        /// <summary>
        /// 確定ボタンが有効かどうか
        /// </summary>
        public bool CanConfirm()
        {
            // カスタム画面が開いていれば、0枚選択でも確定可能
            return IsOpen;
        }

        /// <summary>
        /// 選択を確定してカスタム画面を閉じる準備をする
        /// IsOpenはまだtrueのまま（アニメーション中はゲージを停止するため）
        /// </summary>
        public void Confirm()
        {
            if (!IsOpen) return;

            // 仕様:
            // - 今回1枚以上選択した場合: availableCardsをクリアし、selectedCardsの内容をavailableCardsに移動
            // - 今回0枚選択した場合: availableCardsをそのまま保持
            // - 選択しなかったカード（currentHand）は次回も保持される
            if (selectedCards.Count > 0)
            {
                // 前回の使用可能カードを破棄し、今回選択したカードを使用可能にする
                availableCards.Clear();
                availableCards.AddRange(selectedCards);
                selectedCards.Clear();
            }
            // else: 何も選択していない場合は、前回の使用可能カードをそのまま保持

            // 閉じるアニメーション開始イベントを発行（IsOpenはまだtrue）
            OnConfirmed?.Invoke(availableCards);
        }

        /// <summary>
        /// カスタム画面の閉じる処理を完了する
        /// アニメーション完了後に呼ばれ、IsOpenをfalseにする
        /// </summary>
        public void CompleteClose()
        {
            if (!IsOpen) return;
            IsOpen = false;
        }

        /// <summary>
        /// 何も選択せずにカスタム画面を閉じる
        /// </summary>
        public void Cancel()
        {
            if (!IsOpen) return;

            // 選択中のカードを手札に戻す
            currentHand.AddRange(selectedCards);
            selectedCards.Clear();

            IsOpen = false;
            OnScreenClosed?.Invoke(availableCards);
        }

        /// <summary>
        /// カスタム画面を完全に閉じる（アニメーション完了後に呼ばれる）
        /// </summary>
        public void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            OnScreenClosed?.Invoke(availableCards);
        }

        /// <summary>
        /// 使用可能カードをクリア（バトル終了時など）
        /// </summary>
        public void ClearAvailableCards()
        {
            availableCards.Clear();
        }

        /// <summary>
        /// 手札上限を変更
        /// </summary>
        public void SetMaxHandSize(int newMax)
        {
            if (newMax < 1) throw new ArgumentException("手札上限は1以上である必要があります", nameof(newMax));
            maxHandSize = newMax;
        }

        /// <summary>
        /// 最大選択枚数を変更
        /// </summary>
        public void SetMaxSelectionCount(int newMax)
        {
            if (newMax < 0) throw new ArgumentException("最大選択枚数は0以上である必要があります", nameof(newMax));
            maxSelectionCount = newMax;
        }

        /// <summary>
        /// 最大コストを変更
        /// </summary>
        public void SetMaxCost(int newMax)
        {
            if (newMax < 0) throw new ArgumentException("最大コストは0以上である必要があります", nameof(newMax));
            maxCost = newMax;
        }

        /// <summary>
        /// カードのコストを計算（同シリーズボーナス適用）
        /// </summary>
        private int CalculateCardCost(Card card)
        {
            int baseCost = card.Cost;

            // 同シリーズボーナス: すでに選択されているカードと同じシリーズなら-1
            if (selectedCards.Any(c => c.Series == card.Series))
            {
                baseCost = Math.Max(0, baseCost - 1);
            }

            return baseCost;
        }

        /// <summary>
        /// 現在選択中のカードの合計コストを取得
        /// </summary>
        public int GetTotalUsedCost()
        {
            // 選択順にコストを計算（シリーズボーナスを考慮）
            int totalCost = 0;
            var processedCards = new List<Card>();

            foreach (var card in selectedCards)
            {
                int cardCost = card.Cost;

                // すでに選択されているカードと同じシリーズなら-1
                if (processedCards.Any(c => c.Series == card.Series))
                {
                    cardCost = Math.Max(0, cardCost - 1);
                }

                totalCost += cardCost;
                processedCards.Add(card);
            }

            return totalCost;
        }

        /// <summary>
        /// カスタム画面を開いた時のイベント
        /// </summary>
        public event Action<List<Card>> OnScreenOpened;

        /// <summary>
        /// 確定ボタンが押された時のイベント（アニメーション開始）
        /// </summary>
        public event Action<List<Card>> OnConfirmed;

        /// <summary>
        /// カスタム画面を閉じた時のイベント（アニメーション完了後）
        /// </summary>
        public event Action<List<Card>> OnScreenClosed;

        /// <summary>
        /// カードを選択した時のイベント
        /// </summary>
        public event Action<Card> OnCardSelected;

        /// <summary>
        /// カードの選択を解除した時のイベント
        /// </summary>
        public event Action<Card> OnCardDeselected;

        /// <summary>
        /// 選択失敗時のイベント
        /// </summary>
        public event Action<string> OnSelectionFailed;
    }
}
