using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;
using Olca.Presentation.Effects;
using Cysharp.Threading.Tasks;
using System.Threading;

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

        [Header("Effects")]
        [SerializeField] private CardEffectView cardEffectView;

        public Card Card { get; private set; }
        private CustomScreenView parentView;
        private CancellationTokenSource destroyCts;

        private void Awake()
        {
            destroyCts = new CancellationTokenSource();

            // CardEffectViewが未設定なら自動で追加
            if (cardEffectView == null && cardImage != null)
            {
                cardEffectView = cardImage.gameObject.AddComponent<CardEffectView>();
                // 対象Imageを設定（リフレクションを使用）
                var targetImageField = typeof(CardEffectView).GetField("targetImage",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (targetImageField != null)
                {
                    targetImageField.SetValue(cardEffectView, cardImage);
                    // targetImageを設定した後、明示的に初期化を呼ぶ
                    cardEffectView.InitializeIfNeeded();
                }
            }
        }

        public void Initialize(Card card, CustomScreenView parent)
        {
            this.Card = card;
            this.parentView = parent;

            // カード情報表示（コストのみ）
            costText.text = $"{card.Cost}";

            // クリックイベント
            cardButton.onClick.AddListener(() => parentView.OnCardClicked(card));
        }

        /// <summary>
        /// 選択状態を更新（エフェクト付き）
        /// </summary>
        public void UpdateSelection(bool isSelected)
        {
            if (destroyCts.IsCancellationRequested) return;

            // エフェクトが有効な場合はアニメーション実行
            if (cardEffectView != null)
            {
                if (isSelected)
                {
                    cardEffectView.ShowHighlightAsync(destroyCts.Token).Forget();
                }
                else
                {
                    cardEffectView.HideHighlightAsync(destroyCts.Token).Forget();
                }
            }
            else
            {
                // エフェクト無効時はシンプルな色変更
                cardImage.color = isSelected ? Color.yellow : Color.white;
            }
        }

        /// <summary>
        /// カード確定時のエフェクトを再生
        /// </summary>
        public async UniTask PlayConfirmEffectAsync()
        {
            if (cardEffectView != null && !destroyCts.IsCancellationRequested)
            {
                await cardEffectView.PlayConfirmFlashAsync(destroyCts.Token);
            }
        }

        private void OnDestroy()
        {
            if (cardButton != null)
            {
                cardButton.onClick.RemoveAllListeners();
            }

            destroyCts?.Cancel();
            destroyCts?.Dispose();
        }
    }
}
