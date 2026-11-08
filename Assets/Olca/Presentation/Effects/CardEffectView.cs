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
        private int effectGeneration;

        private CancellationTokenSource BeginEffect(CancellationToken ct, out CancellationToken token, out int generation)
        {
            var previous = effectCts;
            var current = CancellationTokenSource.CreateLinkedTokenSource(ct);
            token = current.Token;
            generation = ++effectGeneration;
            effectCts = current;
            previous?.Cancel();
            previous?.Dispose();
            return current;
        }

        private bool OwnsEffect(CancellationTokenSource source, int generation)
            => generation == effectGeneration && ReferenceEquals(effectCts, source);

        private void EndEffect(CancellationTokenSource source)
        {
            if (ReferenceEquals(effectCts, source)) effectCts = null;
            source.Dispose();
        }

        private void Awake()
        {
            InitializeIfNeeded();

            // グロー画像が未設定なら無効化
            if (glowImage != null)
            {
                glowImage.gameObject.SetActive(false);
            }
        }

        internal void InitializeTarget(Image image)
        {
            targetImage = image;
            InitializeIfNeeded();
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
            var request = BeginEffect(ct, out var token, out var generation);
            if (OwnsEffect(request, generation)) isHighlighted = true;

            try
            {
                // ハイライト色に変更 + スケール拡大
                await UniTask.WhenAll(
                    AnimateColorAsync(targetColor, scaleDuration, token, request, generation),
                    AnimateScaleAsync(originalScale * selectedScale, scaleDuration, token, request, generation)
                );

                // グローエフェクト表示
                if (OwnsEffect(request, generation) && !token.IsCancellationRequested && useGlowEffect && glowImage != null)
                {
                    glowImage.gameObject.SetActive(true);
                }
            }
            catch (System.OperationCanceledException)
            {
                // キャンセル時は元に戻す
                if (OwnsEffect(request, generation)) ResetToOriginalState();
            }
            finally { EndEffect(request); }
        }

        /// <summary>
        /// カード選択解除時のハイライト（オフ）
        /// </summary>
        public async UniTask HideHighlightAsync(CancellationToken ct)
        {
            if (!isHighlighted || targetImage == null) return;
            var request = BeginEffect(ct, out var token, out var generation);
            if (OwnsEffect(request, generation)) isHighlighted = false;

            try
            {
                // 元の色・スケールに戻す
                await UniTask.WhenAll(
                    AnimateColorAsync(originalColor, scaleDuration, token, request, generation),
                    AnimateScaleAsync(originalScale, scaleDuration, token, request, generation)
                );

                // グローエフェクト非表示
                if (OwnsEffect(request, generation) && !token.IsCancellationRequested && useGlowEffect && glowImage != null)
                {
                    glowImage.gameObject.SetActive(false);
                }
            }
            catch (System.OperationCanceledException)
            {
                if (OwnsEffect(request, generation)) ResetToOriginalState();
            }
            finally { EndEffect(request); }
        }

        /// <summary>
        /// カード確定時のフラッシュエフェクト
        /// </summary>
        public async UniTask PlayConfirmFlashAsync(CancellationToken ct)
        {
            if (targetImage == null) return;

            var request = BeginEffect(ct, out var token, out var generation);

            try
            {
                // フラッシュ: 白色に変更 → 元の色に戻る
                token.ThrowIfCancellationRequested();
                if (!OwnsEffect(request, generation)) return;
                targetImage.color = flashColor;
                await Olca.Unity.GameplayPause.Delay(System.TimeSpan.FromSeconds(flashDuration / 2), cancellationToken: token);

                token.ThrowIfCancellationRequested();
                if (!OwnsEffect(request, generation) || targetImage == null) return;
                targetImage.color = originalColor;
                await Olca.Unity.GameplayPause.Delay(System.TimeSpan.FromSeconds(flashDuration / 2), cancellationToken: token);
            }
            catch (System.OperationCanceledException)
            {
                if (OwnsEffect(request, generation) && targetImage != null) targetImage.color = originalColor;
            }
            finally { EndEffect(request); }
        }

        /// <summary>
        /// 色を補間アニメーション
        /// </summary>
        private async UniTask AnimateColorAsync(Color targetColor, float duration, CancellationToken ct, CancellationTokenSource request, int generation)
        {
            if (targetImage == null) return;

            Color startColor = targetImage.color;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                if (!OwnsEffect(request, generation)) return;

                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / duration;
                targetImage.color = Color.Lerp(startColor, targetColor, t);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            ct.ThrowIfCancellationRequested();
            if (OwnsEffect(request, generation) && targetImage != null) targetImage.color = targetColor;
        }

        /// <summary>
        /// スケールを補間アニメーション
        /// </summary>
        private async UniTask AnimateScaleAsync(Vector3 targetScale, float duration, CancellationToken ct, CancellationTokenSource request, int generation)
        {
            if (rectTransform == null) return;

            Vector3 startScale = rectTransform.localScale;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                if (!OwnsEffect(request, generation)) return;

                elapsed += Olca.Unity.GameplayPause.DeltaTime;
                float t = elapsed / duration;
                rectTransform.localScale = Vector3.Lerp(startScale, targetScale, t);

                await Olca.Unity.GameplayPause.NextFrame(ct);
            }

            ct.ThrowIfCancellationRequested();
            if (OwnsEffect(request, generation) && rectTransform != null) rectTransform.localScale = targetScale;
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
            ++effectGeneration;
            var current = effectCts;
            effectCts = null;
            current?.Cancel();
            current?.Dispose();
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
