using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;

namespace Olca.Presentation.CustomScreen
{
    /// <summary>
    /// 手札1枚のカードView
    /// </summary>
    public class HandCardView : MonoBehaviour
    {
        [SerializeField] private Text costText;
        [SerializeField] private Image cardImage;
        [SerializeField] private Button cardButton;

        public Card Card { get; private set; }
        private CustomScreenView parentView;

        public void Initialize(Card card, CustomScreenView parent)
        {
            this.Card = card;
            this.parentView = parent;

            // カード情報表示（コストのみ）
            costText.text = $"{card.Cost}";

            // クリックイベント
            cardButton.onClick.AddListener(() => parentView.OnCardClicked(card));
        }

        public void UpdateSelection(bool isSelected)
        {
            // 選択状態のハイライト
            cardImage.color = isSelected ? Color.yellow : Color.white;
        }

        private void OnDestroy()
        {
            if (cardButton != null)
            {
                cardButton.onClick.RemoveAllListeners();
            }
        }
    }
}
