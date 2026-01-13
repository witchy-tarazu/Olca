using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.Effects
{
    /// <summary>
    /// ヒットエフェクトを表示するView
    /// 敵が攻撃を受けた際のフラッシュ・振動エフェクト
    /// </summary>
    public class HitEffectView : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Image targetImage;  // エフェクト対象のImage（敵のスプライト等）

        [Header("Flash Effect Settings")]
        [SerializeField] private Color flashColor = Color.white;     // フラッシュ色
        [SerializeField] private float flashDuration = 0.1f;         // フラッシュ時間（秒）
        [SerializeField] private int flashCount = 2;                 // フラッシュ回数

        [Header("Shake Effect Settings")]
        [SerializeField] private float shakeAmount = 5f;             // 振動の大きさ（ピクセル）
        [SerializeField] private float shakeDuration = 0.2f;         // 振動時間（秒）
        [SerializeField] private int shakeIterations = 10;           // 振動の回数

        [Header("Scale Pulse Settings")]
        [SerializeField] private bool useScalePulse = true;          // スケールパルスを使用するか
        [SerializeField] private float scalePulseAmount = 1.2f;      // パルス時の拡大率
        [SerializeField] private float scalePulseDuration = 0.15f;   // パルス時間（秒）

        private Color originalColor;
        private Vector3 originalPosition;
        private Vector3 originalScale;
        private RectTransform rectTransform;
        private CancellationTokenSource effectCts;

        private void Awake()
        {
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
                originalPosition = rectTransform.localPosition;
                originalScale = rectTransform.localScale;
            }
        }

        /// <summary>
        /// ヒットエフェクトを再生（フラッシュ + 振動 + スケールパルス）
        /// </summary>
        public async UniTask PlayHitEffectAsync(CancellationToken ct)
        {
            // targetImageまたはrectTransformがnullの場合は何もしない
            if (targetImage == null || rectTransform == null)
            {
                return;
            }

            // 既存のエフェクトをキャンセル
            effectCts?.Cancel();
            effectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                // 並行実行: フラッシュ、振動、スケールパルス
                await UniTask.WhenAll(
                    FlashEffectAsync(effectCts.Token),
                    ShakeEffectAsync(effectCts.Token),
                    useScalePulse ? ScalePulseEffectAsync(effectCts.Token) : UniTask.CompletedTask
                );
            }
            catch (System.OperationCanceledException)
            {
                // キャンセルされた場合は元の状態に戻す
                ResetToOriginalState();
            }
        }

        /// <summary>
        /// フラッシュエフェクト（色を点滅）
        /// </summary>
        private async UniTask FlashEffectAsync(CancellationToken ct)
        {
            for (int i = 0; i < flashCount; i++)
            {
                if (ct.IsCancellationRequested) break;

                // フラッシュ色に変更
                targetImage.color = flashColor;
                await UniTask.Delay(System.TimeSpan.FromSeconds(flashDuration / 2), cancellationToken: ct);

                // 元の色に戻す
                targetImage.color = originalColor;
                await UniTask.Delay(System.TimeSpan.FromSeconds(flashDuration / 2), cancellationToken: ct);
            }

            // 最終的に元の色に戻す
            targetImage.color = originalColor;
        }

        /// <summary>
        /// 振動エフェクト（位置をランダムに振動）
        /// </summary>
        private async UniTask ShakeEffectAsync(CancellationToken ct)
        {
            float elapsed = 0f;
            float interval = shakeDuration / shakeIterations;

            while (elapsed < shakeDuration)
            {
                if (ct.IsCancellationRequested) break;

                // ランダムな方向に振動
                float offsetX = Random.Range(-shakeAmount, shakeAmount);
                float offsetY = Random.Range(-shakeAmount, shakeAmount);
                rectTransform.localPosition = originalPosition + new Vector3(offsetX, offsetY, 0f);

                elapsed += interval;
                await UniTask.Delay(System.TimeSpan.FromSeconds(interval), cancellationToken: ct);
            }

            // 元の位置に戻す
            rectTransform.localPosition = originalPosition;
        }

        /// <summary>
        /// スケールパルスエフェクト（一瞬拡大してから元に戻る）
        /// </summary>
        private async UniTask ScalePulseEffectAsync(CancellationToken ct)
        {
            float elapsed = 0f;
            float halfDuration = scalePulseDuration / 2f;

            // 拡大フェーズ
            while (elapsed < halfDuration)
            {
                if (ct.IsCancellationRequested) break;

                elapsed += Time.deltaTime;
                float t = elapsed / halfDuration;
                rectTransform.localScale = Vector3.Lerp(originalScale, originalScale * scalePulseAmount, t);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            elapsed = 0f;

            // 縮小フェーズ
            while (elapsed < halfDuration)
            {
                if (ct.IsCancellationRequested) break;

                elapsed += Time.deltaTime;
                float t = elapsed / halfDuration;
                rectTransform.localScale = Vector3.Lerp(originalScale * scalePulseAmount, originalScale, t);

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // 元のスケールに戻す
            rectTransform.localScale = originalScale;
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
                rectTransform.localPosition = originalPosition;
                rectTransform.localScale = originalScale;
            }
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
                    originalPosition = rectTransform.localPosition;
                    originalScale = rectTransform.localScale;
                }
            }
        }
    }
}
