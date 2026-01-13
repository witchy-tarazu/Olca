using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;

namespace Olca.Presentation.CustomScreen
{
    /// <summary>
    /// カスタム画面（カード選択UI）
    /// </summary>
    public class CustomScreenView : MonoBehaviour
    {
        [SerializeField] private Transform handCardContainer;
        [SerializeField] private GameObject handCardPrefab;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Text costDisplay;
        [SerializeField] private Transform[] cardPositionMarkers; // カード位置マーカー（10個）

        // Phase2用: 壁マーカー（アニメーション衝突判定用）
        [SerializeField] private RectTransform rightWall;
        [SerializeField] private RectTransform topWall;
        [SerializeField] private RectTransform bottomWall;

        // 選択カード表示エリア
        [SerializeField] private Image selectedCardImage;
        [SerializeField] private Text selectedCardDescription;

        private CustomScreenSystem customScreen;
        private List<HandCardView> handCardViews = new List<HandCardView>();

        public void Initialize(CustomScreenSystem customScreen)
        {
            this.customScreen = customScreen;

            // 確定ボタン
            confirmButton.onClick.AddListener(OnConfirmClicked);

            // イベント購読
            customScreen.OnScreenOpened += OnScreenOpened;
            customScreen.OnScreenClosed += OnScreenClosed;
        }

        private void OnScreenOpened(List<Card> hand)
        {
            // 既存の手札Viewをクリア
            foreach (var view in handCardViews)
            {
                Destroy(view.gameObject);
            }
            handCardViews.Clear();

            // 新しい手札View生成
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                var cardObj = Instantiate(handCardPrefab, handCardContainer);
                var cardView = cardObj.GetComponent<HandCardView>();
                cardView.Initialize(card, this);
                handCardViews.Add(cardView);

                // カード位置を手動設定（Phase1: アニメーションなし）
                PlaceCard(cardView, i);
            }

            // 確定ボタンの初期状態を設定（0枚選択なので無効）
            UpdateConfirmButton();

            // コスト表示の初期状態を設定（0枚選択なのでコスト0）
            UpdateCostDisplay();

            // 選択カード表示エリアの初期状態（何も選択されていない）
            UpdateSelectedCardDisplay();
        }

        /// <summary>
        /// カード位置マーカーからRectTransform位置を取得
        /// </summary>
        private Vector2 GetCardPosition(int index)
        {
            if (cardPositionMarkers == null || index < 0 || index >= cardPositionMarkers.Length)
            {
                // フォールバック: マーカーが設定されていない場合は手動計算
                Debug.LogWarning($"CardPositionMarker[{index}] が設定されていません。デフォルト位置を使用します。");
                int row = index / 5;
                int col = index % 5;
                return new Vector2(col * 160f, -row * 160f); // 150px + 10px間隔
            }

            var markerRect = cardPositionMarkers[index] as RectTransform;
            return markerRect.anchoredPosition;
        }

        /// <summary>
        /// カードを配置（Phase1: 即座に配置、Phase2: アニメーション後に配置）
        /// </summary>
        private void PlaceCard(HandCardView cardView, int index)
        {
            var rectTransform = cardView.GetComponent<RectTransform>();
            rectTransform.anchoredPosition = GetCardPosition(index);
            rectTransform.rotation = Quaternion.identity;
        }

        public void OnCardClicked(Card card)
        {
            // カード選択トグル
            if (customScreen.SelectedCards.Any(c => c == card))
            {
                customScreen.TryDeselectCard(card);
            }
            else
            {
                customScreen.TrySelectCard(card);
            }

            // View更新（選択状態のハイライト）
            foreach (var view in handCardViews)
            {
                view.UpdateSelection(customScreen.SelectedCards.Any(c => c == view.Card));
            }

            // 確定ボタンの有効/無効を更新
            UpdateConfirmButton();

            // コスト表示を更新
            UpdateCostDisplay();

            // 選択カード表示エリアを更新
            UpdateSelectedCardDisplay();
        }

        /// <summary>
        /// 確定ボタンの有効/無効を更新
        /// </summary>
        private void UpdateConfirmButton()
        {
            if (confirmButton != null)
            {
                confirmButton.interactable = customScreen.CanConfirm();
            }
        }

        /// <summary>
        /// コスト表示を更新
        /// </summary>
        private void UpdateCostDisplay()
        {
            if (costDisplay != null)
            {
                int totalCost = customScreen.SelectedCards.Sum(c => c.Cost);
                int costLimit = customScreen.MaxCost;
                costDisplay.text = $"コスト: {totalCost} / {costLimit}";

                // コスト超過時は赤色表示
                if (totalCost > costLimit)
                {
                    costDisplay.color = new Color(1f, 0.39f, 0.39f); // R:255, G:100, B:100
                }
                else
                {
                    costDisplay.color = Color.white;
                }
            }
        }

        /// <summary>
        /// 選択カード表示エリアを更新
        /// </summary>
        private void UpdateSelectedCardDisplay()
        {
            if (customScreen.SelectedCards.Count > 0)
            {
                // 最後に選択されたカード（最新）を表示
                var lastSelectedCard = customScreen.SelectedCards[customScreen.SelectedCards.Count - 1];

                if (selectedCardDescription != null)
                {
                    // コスト、威力、説明を表示
                    selectedCardDescription.text =
                        $"コスト: {lastSelectedCard.Cost}\n" +
                        $"威力: {lastSelectedCard.Power}\n\n" +
                        $"{lastSelectedCard.Description}";
                }

                // 画像は現時点では設定なし（Phase1では白背景）
                if (selectedCardImage != null)
                {
                    selectedCardImage.color = Color.white;
                }
            }
            else
            {
                // 何も選択されていない場合
                if (selectedCardDescription != null)
                {
                    selectedCardDescription.text = "カードを選択してください";
                }

                if (selectedCardImage != null)
                {
                    selectedCardImage.color = Color.white;
                }
            }
        }

        private void OnConfirmClicked()
        {
            // 選択確定
            customScreen.Confirm();
        }

        private void OnScreenClosed(List<Card> selectedCards)
        {
            // BattleControllerに通知（BattleViewを再表示）
            var battleController = FindObjectOfType<Olca.Presentation.Battle.BattleController>();
            if (battleController != null)
            {
                battleController.OnCustomScreenClosed();
            }
        }

        private void OnDestroy()
        {
            if (customScreen != null)
            {
                customScreen.OnScreenOpened -= OnScreenOpened;
                customScreen.OnScreenClosed -= OnScreenClosed;
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
            }
        }
    }
}
