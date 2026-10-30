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

        private Font presentationFont;
        private Image illustration;
        private ScrollRect descriptionScroll;
        // 呼出元が所有する本絵を借用する。miniアイコンやゲーム定義は変更しない。
        public void ConfigurePresentation(Font font,Sprite artwork)
        {
            presentationFont=font;
            if(font==null)return;
            foreach(var text in GetComponentsInChildren<Text>(true)){text.font=font;text.supportRichText=false;}
            var root=(RectTransform)transform;root.sizeDelta=new Vector2(360,640);
            if(illustration==null){var obj=new GameObject("RewardCardIllustration",typeof(RectTransform),typeof(Image));obj.transform.SetParent(transform,false);illustration=obj.GetComponent<Image>();illustration.preserveAspect=true;illustration.raycastTarget=false;}
            illustration.sprite=artwork;illustration.gameObject.SetActive(artwork!=null);
            Place(illustration.rectTransform,104,256,256);
            Place(cardNameText?.rectTransform,18,312,68);if(cardNameText!=null){cardNameText.fontSize=32;cardNameText.resizeTextForBestFit=true;cardNameText.resizeTextMinSize=18;cardNameText.resizeTextMaxSize=32;}
            Place(cardCostText?.rectTransform,378,312,40);if(cardCostText!=null)cardCostText.fontSize=26;
            Place(cardRarityText?.rectTransform,426,312,30);if(cardRarityText!=null)cardRarityText.fontSize=22;
            if(cardDescText!=null){
                if(descriptionScroll==null){var obj=new GameObject("RewardDescriptionScroll",typeof(RectTransform),typeof(Image),typeof(RectMask2D),typeof(ScrollRect));obj.transform.SetParent(transform,false);var area=(RectTransform)obj.transform;Place(area,472,312,80);obj.GetComponent<Image>().color=Color.clear;descriptionScroll=obj.GetComponent<ScrollRect>();descriptionScroll.viewport=area;descriptionScroll.content=cardDescText.rectTransform;descriptionScroll.horizontal=false;descriptionScroll.movementType=ScrollRect.MovementType.Clamped;descriptionScroll.scrollSensitivity=32;cardDescText.transform.SetParent(area,false);
                    var barObject=new GameObject("DescriptionScrollbar",typeof(RectTransform),typeof(Image),typeof(Scrollbar));barObject.transform.SetParent(area,false);var barRect=(RectTransform)barObject.transform;barRect.anchorMin=barRect.anchorMax=new Vector2(1,.5f);barRect.pivot=new Vector2(1,.5f);barRect.sizeDelta=new Vector2(4,80);barRect.anchoredPosition=Vector2.zero;barObject.GetComponent<Image>().color=new Color(.06f,.2f,.25f,.18f);
                    var handleObject=new GameObject("Handle",typeof(RectTransform),typeof(Image));handleObject.transform.SetParent(barRect,false);var handle=(RectTransform)handleObject.transform;handle.anchorMin=Vector2.zero;handle.anchorMax=Vector2.one;handle.offsetMin=handle.offsetMax=Vector2.zero;handleObject.GetComponent<Image>().color=new Color(.05f,.25f,.3f,.65f);
                    var bar=barObject.GetComponent<Scrollbar>();bar.direction=Scrollbar.Direction.BottomToTop;bar.handleRect=handle;bar.targetGraphic=handleObject.GetComponent<Image>();bar.navigation=new Navigation{mode=Navigation.Mode.None};descriptionScroll.verticalScrollbar=bar;descriptionScroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;}
                var rect=cardDescText.rectTransform;rect.anchorMin=new Vector2(0,1);rect.anchorMax=Vector2.one;rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=Vector2.zero;rect.sizeDelta=new Vector2(-12,0);cardDescText.fontSize=22;cardDescText.lineSpacing=1.15f;cardDescText.alignment=TextAnchor.UpperLeft;cardDescText.verticalOverflow=VerticalWrapMode.Overflow;cardDescText.raycastTarget=false;
            }
            if(selectButton!=null){var rect=(RectTransform)selectButton.transform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(0,44);rect.sizeDelta=new Vector2(312,64);var label=selectButton.GetComponentInChildren<Text>(true);if(label!=null){label.fontSize=28;label.rectTransform.anchorMin=Vector2.zero;label.rectTransform.anchorMax=Vector2.one;label.rectTransform.offsetMin=new Vector2(12,4);label.rectTransform.offsetMax=new Vector2(-12,-4);}}
        }
        private static void Place(RectTransform rect,float top,float width,float height)
        {if(rect==null)return;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-top);rect.sizeDelta=new Vector2(width,height);}
        private void LateUpdate()
        {if(descriptionScroll!=null&&cardDescText!=null){float height=Mathf.Max(descriptionScroll.viewport.rect.height,cardDescText.preferredHeight+cardDescText.fontSize);if(!Mathf.Approximately(height,cardDescText.rectTransform.rect.height)){float position=descriptionScroll.verticalNormalizedPosition;cardDescText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);descriptionScroll.verticalNormalizedPosition=position;}}}

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
                cardNameText.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana(card.Name);
                var nameRect=cardNameText.rectTransform;
                nameRect.anchorMin=new Vector2(0,nameRect.anchorMin.y);nameRect.anchorMax=new Vector2(1,nameRect.anchorMax.y);
                nameRect.sizeDelta=new Vector2(presentationFont!=null?-48:-28,nameRect.sizeDelta.y);nameRect.anchoredPosition=new Vector2(0,nameRect.anchoredPosition.y);
                // 枠内の余白と元の文字サイズを上限にし、長い名前も既存の名前欄へ収める。
                cardNameText.resizeTextForBestFit=true;
                cardNameText.resizeTextMinSize=Mathf.Min(10,cardNameText.fontSize);
                cardNameText.resizeTextMaxSize=cardNameText.fontSize;
            }
            if (cardCostText != null) cardCostText.text = $"コスト: {card.Cost}";
            if (cardDescText != null) cardDescText.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana(card.Description);
            if(descriptionScroll!=null){Canvas.ForceUpdateCanvases();LateUpdate();descriptionScroll.StopMovement();descriptionScroll.verticalNormalizedPosition=1;}
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
