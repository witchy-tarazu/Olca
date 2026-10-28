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
            UiLook.Face(GetComponent<Image>()); UiLook.Button(selectButton);
            UiLook.Label(cardNameText);UiLook.Label(cardCostText);UiLook.Label(cardDescText);UiLook.Label(cardRarityText);
            if (cardNameText != null)
            {
                cardNameText.text = card.Name;
                var nameRect=cardNameText.rectTransform;
                nameRect.anchorMin=new Vector2(0,nameRect.anchorMin.y);nameRect.anchorMax=new Vector2(1,nameRect.anchorMax.y);
                nameRect.sizeDelta=new Vector2(-28,nameRect.sizeDelta.y);nameRect.anchoredPosition=new Vector2(0,nameRect.anchoredPosition.y);
                // 枠内の余白と元の文字サイズを上限にし、長い名前も既存の名前欄へ収める。
                cardNameText.resizeTextForBestFit=true;
                cardNameText.resizeTextMinSize=Mathf.Min(10,cardNameText.fontSize);
                cardNameText.resizeTextMaxSize=cardNameText.fontSize;
            }
            if (cardCostText != null) cardCostText.text = $"コスト: {card.Cost}";
            if (cardDescText != null) cardDescText.text = card.Description;
            if (cardRarityText != null) cardRarityText.text = Olca.Presentation.CustomScreen.CustomDisplay.Regulation(card.Rarity);
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
