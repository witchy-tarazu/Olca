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
        private readonly List<Card> selectedCards; // 選択されたカード
        private int maxHandSize;
        private int maxCost;

        /// <summary>現在の手札</summary>
        public IReadOnlyList<Card> CurrentHand => currentHand.AsReadOnly();

        /// <summary>選択中のカード</summary>
        public IReadOnlyList<Card> SelectedCards => selectedCards.AsReadOnly();

        /// <summary>手札上限</summary>
        public int MaxHandSize => maxHandSize;

        /// <summary>残りコスト</summary>
        public int RemainingCost => maxCost - GetTotalUsedCost();

        /// <summary>カスタム画面が開いているか</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public CustomScreenSystem(Deck deck, int maxHandSize = 5, int maxCost = 10)
        {
            this.deck = deck ?? throw new ArgumentNullException(nameof(deck));
            this.currentHand = new List<Card>();
            this.selectedCards = new List<Card>();
            this.maxHandSize = maxHandSize;
            this.maxCost = maxCost;
        }

        /// <summary>
        /// カスタム画面を開く
        /// </summary>
        public void Open(IRandomProvider random)
        {
            if (IsOpen) return;

            IsOpen = true;

            // 手札が上限になるまでドロー
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

            // 実際のコストを計算（系統ボーナスを考慮）
            int actualCost = CalculateCardCost(card);

            // コスト制限チェック
            if (RemainingCost < actualCost)
            {
                OnSelectionFailed?.Invoke("コストが足りません");
                return false;
            }

            // 同名カード制限チェック
            if (selectedCards.Any(c => c.Name == card.Name))
            {
                OnSelectionFailed?.Invoke("同名カードは1ターンに1枚しか選択できません");
                return false;
            }

            // 選択成功
            selectedCards.Add(card);
            currentHand.Remove(card);

            OnCardSelected?.Invoke(card);
            return true;
        }

        /// <summary>
        /// カードの実際のコストを計算（系統ボーナスを考慮）
        /// </summary>
        private int CalculateCardCost(Card card)
        {
            int baseCost = card.Cost;

            // 系統ボーナス: 前回選択したカードと同じ系統なら-1
            if (selectedCards.Count > 0)
            {
                var lastCard = selectedCards[selectedCards.Count - 1];
                if (lastCard.Series != null &&
                    card.Series != null &&
                    lastCard.Series.Equals(card.Series) &&
                    !card.Series.Equals(CardSeries.None))
                {
                    baseCost = Math.Max(0, baseCost - 1);
                }
            }

            return baseCost;
        }

        /// <summary>
        /// 選択済みカードの合計コストを取得（系統ボーナス込み）
        /// </summary>
        private int GetTotalUsedCost()
        {
            int total = 0;
            for (int i = 0; i < selectedCards.Count; i++)
            {
                var card = selectedCards[i];
                int cost = card.Cost;

                // 系統ボーナスの適用
                if (i > 0)
                {
                    var prevCard = selectedCards[i - 1];
                    if (prevCard.Series != null &&
                        card.Series != null &&
                        prevCard.Series.Equals(card.Series) &&
                        !card.Series.Equals(CardSeries.None))
                    {
                        cost = Math.Max(0, cost - 1);
                    }
                }

                total += cost;
            }
            return total;
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
        /// 選択を確定してカスタム画面を閉じる
        /// </summary>
        public void Confirm()
        {
            if (!IsOpen) return;

            // 未選択のカードを破棄
            currentHand.Clear();

            IsOpen = false;
            OnScreenClosed?.Invoke(selectedCards);
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
            OnScreenClosed?.Invoke(selectedCards);
        }

        /// <summary>
        /// 選択済みカードをクリア（ターン開始時など）
        /// </summary>
        public void ClearSelectedCards()
        {
            selectedCards.Clear();
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
        /// 最大コストを変更
        /// </summary>
        public void SetMaxCost(int newMax)
        {
            if (newMax < 0) throw new ArgumentException("最大コストは0以上である必要があります", nameof(newMax));
            maxCost = newMax;
        }

        /// <summary>
        /// カスタム画面を開いた時のイベント
        /// </summary>
        public event Action<List<Card>> OnScreenOpened;

        /// <summary>
        /// カスタム画面を閉じた時のイベント
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
