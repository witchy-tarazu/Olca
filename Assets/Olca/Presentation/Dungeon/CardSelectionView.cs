using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;

namespace Olca.Presentation.Dungeon
{
    /// <summary>
    /// バトル勝利後のカード選択画面View
    /// DungeonBattleController から ShowAsync() を呼び、選択完了まで待機する
    /// </summary>
    public class CardSelectionView : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject containerRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private CardSelectionItemView[] cardItems;
        [SerializeField] private Text deckCountText;

        [Header("アニメーション")]
        [SerializeField] private float fadeInDuration = 0.3f;

        private UniTaskCompletionSource<Card> selectionTcs;

        /// <summary>
        /// カード選択画面を表示し、選択が完了するまで待機する
        /// </summary>
        /// <param name="choices">選択肢のカードリスト</param>
        /// <param name="currentDeckCount">現在のデッキ枚数</param>
        /// <param name="ct">キャンセルトークン（キャンセル時は null を返す）</param>
        /// <returns>選択されたカード（スキップまたはキャンセル時は null）</returns>
        public async UniTask<Card> ShowAsync(
            IReadOnlyList<Card> choices,
            int currentDeckCount,
            CancellationToken ct = default)
        {
            selectionTcs = new UniTaskCompletionSource<Card>();

            // デッキ枚数表示
            if (deckCountText != null)
                deckCountText.text = $"デッキ: {currentDeckCount}枚";

            // カード選択肢をセット
            for (int i = 0; i < cardItems.Length; i++)
            {
                if (i < choices.Count)
                {
                    cardItems[i].Setup(choices[i]);
                    cardItems[i].gameObject.SetActive(true);
                }
                else
                {
                    cardItems[i].gameObject.SetActive(false);
                }
            }

            // 表示
            if (containerRoot != null)
                containerRoot.SetActive(true);

            // フェードイン
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
                await FadeAsync(canvasGroup, 0f, 1f, fadeInDuration, ct);
            }

            // キャンセル時は null を返す
            ct.Register(() => selectionTcs.TrySetResult(null));

            return await selectionTcs.Task;
        }

        /// <summary>
        /// カード選択画面を非表示にしてリセット
        /// </summary>
        public void Hide()
        {
            selectionTcs?.TrySetResult(null);
            if (containerRoot != null)
                containerRoot.SetActive(false);
        }

        /// <summary>
        /// カードアイテムがクリックされたときに CardSelectionItemView から呼ばれる
        /// </summary>
        internal void OnItemClicked(Card card)
        {
            selectionTcs?.TrySetResult(card);
            if (containerRoot != null)
                containerRoot.SetActive(false);
        }

        private async UniTask FadeAsync(
            CanvasGroup group,
            float from, float to, float duration, CancellationToken ct)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            group.alpha = to;
        }

        private void OnDestroy()
        {
            selectionTcs?.TrySetCanceled();
        }
    }

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
