using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.Effects
{
    /// <summary>
    /// カード選択・使用時のエフェクトView
    /// ハイライト、フラッシュ、スケールアニメーション
    /// </summary>
    public class CardEffectView : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Image targetImage;  // エフェクト対象のカード画像

        [Header("Highlight Effect Settings")]
        [SerializeField] private Color highlightColor = new Color(1f, 1f, 0.5f, 1f);  // ハイライト色（薄い黄色）
        [SerializeField] private float highlightIntensity = 1.3f;                      // ハイライト強度

        [Header("Flash Effect Settings")]
        [SerializeField] private Color flashColor = Color.white;   // フラッシュ色
        [SerializeField] private float flashDuration = 0.2f;       // フラッシュ時間（秒）

        [Header("Scale Animation Settings")]
        [SerializeField] private float selectedScale = 1.1f;       // 選択時のスケール
        [SerializeField] private float scaleDuration = 0.15f;      // スケールアニメーション時間（秒）

        [Header("Glow Effect Settings")]
        [SerializeField] private bool useGlowEffect = false;       // グローエフェクトを使用するか
        [SerializeField] private Image glowImage;                  // グロー用のImage（オプション）

        private Color originalColor;
        private Vector3 originalScale;
        private RectTransform rectTransform;
        private bool isHighlighted = false;
        private CancellationTokenSource effectCts;

        private void Awake()
        {
            InitializeIfNeeded();

            // グロー画像が未設定なら無効化
            if (glowImage != null)
            {
                glowImage.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 初期化（targetImageが設定された後に呼ぶ）
        /// </summary>
        public void InitializeIfNeeded()
        {
            if (targetImage != null && rectTransform == null)
            {
                rectTransform = targetImage.GetComponent<RectTransform>();
                originalColor = targetImage.color;
                originalScale = rectTransform.localScale;
            }
        }

        /// <summary>
        /// カード選択時のハイライト（オン）- 色を指定してオーバーライド可能
        /// </summary>
        public async UniTask ShowHighlightAsync(CancellationToken ct, Color? overrideColor = null)
        {
            Color targetColor = overrideColor.HasValue
                ? overrideColor.Value * highlightIntensity
                : highlightColor * highlightIntensity;
            await ShowHighlightWithColorAsync(ct, targetColor);
        }

        /// <summary>
        /// カード選択時のハイライト（オン）- 内部実装
        /// </summary>
        private async UniTask ShowHighlightWithColorAsync(CancellationToken ct, Color targetColor)
        {
            if (targetImage == null) return;
            // 既にハイライト中でも色変更要求があれば再アニメーション（カスタマイズモード中の色切替に対応）
            isHighlighted = true;

            effectCts?.Cancel();
            effectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // ハイライト色に変更 + スケール拡大
                await UniTask.WhenAll(
                    AnimateColorAsync(targetColor, scaleDuration, effectCts.Token),
                    AnimateScaleAsync(originalScale * selectedScale, scaleDuration, effectCts.Token)
                );

                // グローエフェクト表示
                if (useGlowEffect && glowImage != null)
                {
                    glowImage.gameObject.SetActive(true);
                }
            }
            catch (System.OperationCanceledException)
            {
                // キャンセル時は元に戻す
                ResetToOriginalState();
            }
        }

        /// <summary>
        /// カード選択解除時のハイライト（オフ）
        /// </summary>
        public async UniTask HideHighlightAsync(CancellationToken ct)
        {
            if (!isHighlighted || targetImage == null) return;
            isHighlighted = false;

            effectCts?.Cancel();
            effectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // 元の色・スケールに戻す
                await UniTask.WhenAll(
                    AnimateColorAsync(originalColor, scaleDuration, effectCts.Token),
                    AnimateScaleAsync(originalScale, scaleDuration, effectCts.Token)
                );

                // グローエフェクト非表示
                if (useGlowEffect && glowImage != null)
                {
                    glowImage.gameObject.SetActive(false);
                }
            }
            catch (System.OperationCanceledException)
            {
                ResetToOriginalState();
            }
        }

        /// <summary>
        /// カード確定時のフラッシュエフェクト
        /// </summary>
        public async UniTask PlayConfirmFlashAsync(CancellationToken ct)
        {
            if (targetImage == null) return;

            effectCts?.Cancel();
            effectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // フラッシュ: 白色に変更 → 元の色に戻る
                targetImage.color = flashColor;
                await Olca.Unity.GameplayPause.Delay(System.TimeSpan.FromSeconds(flashDuration / 2), cancellationToken: effectCts.Token);

                targetImage.color = originalColor;
                await Olca.Unity.GameplayPause.Delay(System.TimeSpan.FromSeconds(flashDuration / 2), cancellationToken: effectCts.Token);
            }
            catch (System.OperationCanceledException)
            {
                targetImage.color = originalColor;
            }
        }

        /// <summary>
        /// 色を補間アニメーション
        /// </summary>
        private async UniTask AnimateColorAsync(Color targetColor, float duration, CancellationToken ct)
        {
            if (targetImage == null) return;

            Color startColor = targetImage.color;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (ct.IsCancellationRequested) break;

                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / duration;
                targetImage.color = Color.Lerp(startColor, targetColor, t);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            targetImage.color = targetColor;
        }

        /// <summary>
        /// スケールを補間アニメーション
        /// </summary>
        private async UniTask AnimateScaleAsync(Vector3 targetScale, float duration, CancellationToken ct)
        {
            if (rectTransform == null) return;

            Vector3 startScale = rectTransform.localScale;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                if (ct.IsCancellationRequested) break;

                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / duration;
                rectTransform.localScale = Vector3.Lerp(startScale, targetScale, t);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            rectTransform.localScale = targetScale;
        }

        /// <summary>
        /// 元の状態にリセット
        /// </summary>
        private void ResetToOriginalState()
        {
            if (targetImage != null)
            {
                targetImage.color = originalColor;
            }

            if (rectTransform != null)
            {
                rectTransform.localScale = originalScale;
            }

            if (useGlowEffect && glowImage != null)
            {
                glowImage.gameObject.SetActive(false);
            }

            isHighlighted = false;
        }

        private void OnDestroy()
        {
            effectCts?.Cancel();
            effectCts?.Dispose();
        }

        /// <summary>
        /// Inspector上で対象Imageが変更された際に元の値を更新
        /// </summary>
        private void OnValidate()
        {
            if (targetImage != null)
            {
                originalColor = targetImage.color;

                if (rectTransform == null)
                {
                    rectTransform = targetImage.GetComponent<RectTransform>();
                }

                if (rectTransform != null)
                {
                    originalScale = rectTransform.localScale;
                }
            }
        }
    }
}
