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

        // カスタマイズシステム（省略可能）
        private readonly ExceedSystem exceedSystem;
        private readonly TuningSystem tuningSystem;
        private readonly FusionSystem fusionSystem;

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
        /// <param name="exceedSystem">超越システム（省略可能）</param>
        /// <param name="tuningSystem">調律システム（省略可能）</param>
        /// <param name="fusionSystem">融合システム（省略可能）</param>
        public CustomScreenSystem(Deck deck, int maxHandSize = 10, int maxSelectionCount = 5, int maxCost = 10,
            ExceedSystem exceedSystem = null, TuningSystem tuningSystem = null, FusionSystem fusionSystem = null)
        {
            this.deck = deck ?? throw new ArgumentNullException(nameof(deck));
            this.currentHand = new List<Card>();
            this.selectedCards = new List<Card>();
            this.availableCards = new List<Card>();
            this.maxHandSize = maxHandSize;
            this.maxSelectionCount = maxSelectionCount;
            this.maxCost = maxCost;
            this.exceedSystem = exceedSystem;
            this.tuningSystem = tuningSystem;
            this.fusionSystem = fusionSystem;
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
            return IsOpen && GetTotalUsedCost() <= maxCost;
        }

        /// <summary>
        /// 選択を確定してカスタム画面を閉じる準備をする
        /// IsOpenはまだtrueのまま（アニメーション中はゲージを停止するため）
        /// </summary>
        public void Confirm()
        {
            if (!CanConfirm()) return;

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
            var previous = selectedCards.Count > 0 ? selectedCards[selectedCards.Count - 1] : null;
            return CalculateCardCost(card, previous, selectedCards.Any(c => c.Series == CardSeries.None));
        }

        private static int CalculateCardCost(Card card, Card previous, bool hasNone)
        {
            // Noneの系統扱いは仕様未確定のため、既存の「選択済みにNoneがあれば割引」を維持する。
            bool discounted = card.Series == CardSeries.None
                ? hasNone
                : previous != null && previous.Series == card.Series;
            return discounted ? Math.Max(0, card.Cost - 1) : card.Cost;
        }

        /// <summary>
        /// 現在選択中のカードの合計コストを取得
        /// </summary>
        public int GetTotalUsedCost()
        {
            int totalCost = 0;
            Card previous = null;
            bool hasNone = false;
            foreach (var card in selectedCards)
            {
                totalCost += CalculateCardCost(card, previous, hasNone);
                previous = card;
                hasNone |= card.Series == CardSeries.None;
            }
            return totalCost;
        }

        // ========== カスタマイズ互換性チェック ==========

        /// <summary>
        /// カスタマイズモードで、現在の選択状態に対して候補カードが追加可能かを判定する。
        /// 超越・調律・融合のいずれかが成立する可能性があれば true を返す。
        /// </summary>
        /// <param name="currentSelection">既に選択済みのカード</param>
        /// <param name="candidate">追加候補のカード</param>
        public bool IsCompatibleWithCurrentSelection(IReadOnlyList<Card> currentSelection, Card candidate)
        {
            if (candidate == null) return false;
            if (currentSelection == null || currentSelection.Count == 0) return true;

            var first = currentSelection[0];

            // 超越の実行と候補表示で同じ検証を使用する。
            if (CanExceed(first, candidate))
                return true;

            // 調律: TuningカードとTuning以外のカードの組み合わせ
            if (tuningSystem != null)
            {
                bool firstIsTuning = first.Category == CardCategory.Tuning;
                bool candidateIsTuning = candidate.Category == CardCategory.Tuning;
                if (firstIsTuning != candidateIsTuning) return true;
            }

            // 融合: 現在の選択＋候補がいずれかのレシピに含まれる（部分一致）
            if (fusionSystem != null)
            {
                var ids = currentSelection.Select(c => c.Id).Append(candidate.Id).ToList();
                if (fusionSystem.HasCompatibleRecipe(ids)) return true;
            }

            return false;
        }

        // ========== カスタマイズ操作 ==========

        // 同じ参照が複数枠に入る場合も、要求された枚数だけ手札に存在することを確認する。
        private bool HasMaterialCopies(IEnumerable<Card> materials)
        {
            var remaining = new List<Card>(currentHand);
            foreach (var card in materials)
            {
                if (card == null || !remaining.Remove(card)) return false;
            }
            return true;
        }

        /// <summary>同名・同段階の素材で超越できるかを検証する</summary>
        public bool CanExceed(Card cardA, Card cardB)
        {
            return exceedSystem != null && exceedSystem.Validate(cardA, cardB).IsValid;
        }

        /// <summary>
        /// 超越を実行する（同名カード2枚 → 上位グレードカード1枚）
        /// </summary>
        /// <param name="cardA">超越素材カード1（手札にあること）</param>
        /// <param name="cardB">超越素材カード2（同名・手札にあること）</param>
        public ExceedResult ExecuteExceed(Card cardA, Card cardB)
        {
            if (exceedSystem == null)
                return ExceedResult.Fail("超越システムが設定されていません");
            if (!HasMaterialCopies(new[] { cardA, cardB }))
                return ExceedResult.Fail("指定カードが手札にありません");

            var result = exceedSystem.Execute(cardA, cardB);
            if (result.IsSuccess)
            {
                // 最初の素材カードの位置に結果カードを挿入
                int insertIdx = currentHand.IndexOf(cardA);
                currentHand.Remove(cardA);
                currentHand.Remove(cardB);
                if (insertIdx >= 0 && insertIdx <= currentHand.Count)
                    currentHand.Insert(insertIdx, result.ResultCard);
                else
                    currentHand.Add(result.ResultCard);
                OnExceeded?.Invoke(result.ResultCard);
            }
            return result;
        }

        /// <summary>
        /// 調律を実行する（調律カードを対象カードに適用して追加効果を付与）
        /// </summary>
        /// <param name="targetCard">調律対象カード（手札にあること）</param>
        /// <param name="tuningCard">調律素材カード（手札にあること・CategoryがTuning）</param>
        public TuningResult ExecuteTuning(Card targetCard, Card tuningCard)
        {
            if (tuningSystem == null)
                return TuningResult.Fail("調律システムが設定されていません");
            if (!currentHand.Contains(targetCard) || !currentHand.Contains(tuningCard))
                return TuningResult.Fail("指定カードが手札にありません");

            var result = tuningSystem.Execute(targetCard, tuningCard);
            if (result.IsSuccess)
            {
                currentHand.Remove(tuningCard);
                int idx = currentHand.IndexOf(targetCard);
                if (idx >= 0)
                    currentHand[idx] = result.ResultCard;
                OnTuned?.Invoke(result.ResultCard);
            }
            return result;
        }

        /// <summary>
        /// 融合を実行する（複数カード → 新カード）
        /// </summary>
        /// <param name="materials">融合素材カードリスト（すべて手札にあること）</param>
        public FusionResult ExecuteFusion(IReadOnlyList<Card> materials)
        {
            if (fusionSystem == null)
                return FusionResult.Fail("融合システムが設定されていません");
            if (materials == null || materials.Count < 2)
                return FusionResult.Fail("2枚以上の素材が必要です");
            if (!HasMaterialCopies(materials))
                return FusionResult.Fail("指定カードが手札にありません");

            var result = fusionSystem.Execute(materials);
            if (result.IsSuccess)
            {
                // 最初の素材カードの位置に結果カードを挿入
                int insertIdx = currentHand.IndexOf(materials[0]);
                foreach (var card in materials)
                    currentHand.Remove(card);
                if (insertIdx >= 0 && insertIdx <= currentHand.Count)
                    currentHand.Insert(insertIdx, result.ResultCard);
                else
                    currentHand.Add(result.ResultCard);
                OnFused?.Invoke(result.ResultCard);
            }
            return result;
        }

        /// <summary>
        /// 超越成功時のイベント
        /// </summary>
        public event Action<Card> OnExceeded;

        /// <summary>
        /// 調律成功時のイベント
        /// </summary>
        public event Action<Card> OnTuned;

        /// <summary>
        /// 融合成功時のイベント
        /// </summary>
        public event Action<Card> OnFused;

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
