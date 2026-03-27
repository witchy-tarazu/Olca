using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Domain;
using Olca.Presentation.Effects;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.CustomScreen
{
    /// <summary>
    /// カスタマイズモードでのハイライト種別
    /// </summary>
    public enum CardHighlightType
    {
        /// <summary>ハイライトなし（通常状態）</summary>
        None,
        /// <summary>通常選択（デッキ選択用）</summary>
        Selected,
        /// <summary>超越素材として選択中</summary>
        ExceedMaterial,
        /// <summary>超越可能ペアが存在するカード（候補表示）</summary>
        ExceedCandidate,
        /// <summary>調律対象として選択中</summary>
        TuningTarget,
        /// <summary>調律素材として選択中</summary>
        TuningMaterial,
        /// <summary>融合素材として選択中</summary>
        FusionMaterial,
        /// <summary>現在の選択と組み合わせ不可（暗転表示）</summary>
        Incompatible,
    }

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
        /// カスタマイズモードのハイライト種別を指定して選択状態を更新
        /// </summary>
        public void UpdateHighlight(CardHighlightType type)
        {
            if (destroyCts.IsCancellationRequested) return;

            if (cardEffectView != null)
            {
                switch (type)
                {
                    case CardHighlightType.None:
                        cardEffectView.HideHighlightAsync(destroyCts.Token).Forget();
                        break;
                    case CardHighlightType.Selected:
                        // 通常選択は既存の黄色ハイライトを使用
                        cardEffectView.ShowHighlightAsync(destroyCts.Token).Forget();
                        break;
                    case CardHighlightType.ExceedMaterial:
                        // 超越素材: 金色
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(1f, 0.84f, 0f)).Forget();
                        break;
                    case CardHighlightType.ExceedCandidate:
                        // 超越候補: 淡い金色
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(0.8f, 0.7f, 0.2f)).Forget();
                        break;
                    case CardHighlightType.TuningTarget:
                        // 調律対象: 青色
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(0.3f, 0.6f, 1f)).Forget();
                        break;
                    case CardHighlightType.TuningMaterial:
                        // 調律素材: 水色
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(0f, 0.9f, 0.9f)).Forget();
                        break;
                    case CardHighlightType.FusionMaterial:
                        // 融合素材: シアン
                        cardEffectView.ShowHighlightAsync(destroyCts.Token, new Color(0f, 1f, 0.8f)).Forget();
                        break;
                    case CardHighlightType.Incompatible:
                        // 選択不可: ハイライト非表示（暗転はcardImageで表現）
                        cardEffectView.HideHighlightAsync(destroyCts.Token).Forget();
                        break;
                }
            }
            else
            {
                // エフェクト無効時はシンプルな色変更
                switch (type)
                {
                    case CardHighlightType.None:
                        cardImage.color = Color.white;
                        break;
                    case CardHighlightType.Selected:
                        cardImage.color = Color.yellow;
                        break;
                    case CardHighlightType.ExceedMaterial:
                        cardImage.color = new Color(1f, 0.84f, 0f);
                        break;
                    case CardHighlightType.ExceedCandidate:
                        cardImage.color = new Color(0.9f, 0.85f, 0.5f);
                        break;
                    case CardHighlightType.TuningTarget:
                        cardImage.color = new Color(0.3f, 0.6f, 1f);
                        break;
                    case CardHighlightType.TuningMaterial:
                        cardImage.color = new Color(0f, 0.9f, 0.9f);
                        break;
                    case CardHighlightType.FusionMaterial:
                        cardImage.color = new Color(0f, 1f, 0.8f);
                        break;
                    case CardHighlightType.Incompatible:
                        cardImage.color = new Color(0.35f, 0.35f, 0.35f, 0.7f);
                        break;
                }
            }

            // Incompatible の場合は cardImage を直接暗転（エフェクトあり・なし共通）
            if (type == CardHighlightType.Incompatible && cardEffectView != null)
            {
                if (cardImage != null)
                    cardImage.color = new Color(0.35f, 0.35f, 0.35f, 0.7f);
            }
            else if (type != CardHighlightType.Incompatible && cardEffectView != null)
            {
                // Incompatible 解除時に色をリセット（ハイライト自体の色はeffectViewが管理）
                if (cardImage != null && type == CardHighlightType.None)
                    cardImage.color = Color.white;
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
