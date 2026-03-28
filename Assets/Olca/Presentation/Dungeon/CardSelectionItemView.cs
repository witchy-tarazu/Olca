using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;

namespace Olca.Presentation.Dungeon
{
    /// <summary>
    /// カード選択肢1枚分のUI
    /// CardSelectionView の子オブジェクトにアタッチして使用する
    /// </summary>
    public class CardSelectionItemView : MonoBehaviour
    {
        [SerializeField] private Text cardNameText;
        [SerializeField] private Text cardCostText;
        [SerializeField] private Text cardDescText;
        [SerializeField] private Text cardRarityText;
        [SerializeField] private Button selectButton;

        private Card card;
        private CardSelectionView parentView;

        private void Awake()
        {
            parentView = GetComponentInParent<CardSelectionView>();
            selectButton?.onClick.AddListener(OnClicked);
        }

        /// <summary>
        /// カード情報をUIに反映する
        /// </summary>
        public void Setup(Card card)
        {
            this.card = card;
            if (cardNameText != null) cardNameText.text = card.Name;
            if (cardCostText != null) cardCostText.text = $"コスト: {card.Cost}";
            if (cardDescText != null) cardDescText.text = card.Description;
            if (cardRarityText != null) cardRarityText.text = card.Rarity.ToString();
        }

        private void OnClicked()
        {
            parentView?.OnItemClicked(card);
        }

        private void OnDestroy()
        {
            selectButton?.onClick.RemoveListener(OnClicked);
        }
    }
}
