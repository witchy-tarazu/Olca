using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Olca.Presentation.Effects
{
    /// <summary>
    /// ダメージ数値を表示するView
    /// Unity UI Textを使用して浮遊・フェードアウトアニメーションを実現
    /// </summary>
    public class DamageNumberView : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Text damageText;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Animation Settings")]
        [SerializeField] private float floatSpeed = 50f;           // 上昇速度（ピクセル/秒）
        [SerializeField] private float fadeDuration = 1f;          // フェードアウト時間（秒）
        [SerializeField] private float horizontalSpread = 30f;     // 横方向のランダムばらつき

        [Header("Color Settings")]
        [SerializeField] private Color normalColor = Color.white;      // 通常ダメージ色
        [SerializeField] private Color criticalColor = Color.yellow;   // クリティカル色
        [SerializeField] private Color healColor = Color.green;        // 回復色

        private RectTransform rectTransform;
        private CancellationTokenSource animationCts;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();

            // CanvasGroupが未設定なら自動で追加
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            // 初期状態は非表示
            canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// ダメージ数値を表示（通常ダメージ）
        /// </summary>
        public async UniTask ShowDamageAsync(int damage, Vector2 worldPosition, CancellationToken ct)
        {
            await ShowDamageAsync(damage, worldPosition, DamageType.Normal, ct);
        }

        /// <summary>
        /// ダメージ数値を表示（タイプ指定）
        /// </summary>
        public async UniTask ShowDamageAsync(int damage, Vector2 worldPosition, DamageType damageType, CancellationToken ct)
        {
            // キャンセル済みなら何もしない
            if (ct.IsCancellationRequested) return;

            // アクティブ化
            gameObject.SetActive(true);
            canvasGroup.alpha = 1f;

            // テキスト設定
            damageText.text = damage.ToString();

            // 色設定
            damageText.color = damageType switch
            {
                DamageType.Critical => criticalColor,
                DamageType.Heal => healColor,
                _ => normalColor
            };

            // フォントサイズ設定（クリティカルは大きめ）
            damageText.fontSize = damageType == DamageType.Critical ? 32 : 24;

            // 初期位置設定（anchoredPositionを使用）
            rectTransform.anchoredPosition = worldPosition;

            // アニメーション実行
            animationCts?.Cancel();
            animationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            try
            {
                await AnimateFloatAndFadeAsync(animationCts.Token);
            }
            catch (System.OperationCanceledException)
            {
                // キャンセルされた場合は無視
            }
            finally
            {
                // 非表示にしてプールに返す準備
                gameObject.SetActive(false);
                canvasGroup.alpha = 0f;
            }
        }

        /// <summary>
        /// 上昇 + フェードアウトアニメーション
        /// </summary>
        private async UniTask AnimateFloatAndFadeAsync(CancellationToken ct)
        {
            float elapsed = 0f;
            Vector2 startPosition = rectTransform.anchoredPosition;

            while (elapsed < fadeDuration)
            {
                if (ct.IsCancellationRequested) break;

                elapsed += Time.deltaTime;
                float t = elapsed / fadeDuration;

                // 上昇（線形）
                float yOffset = floatSpeed * elapsed;
                rectTransform.anchoredPosition = startPosition + new Vector2(0f, yOffset);

                // フェードアウト（線形）
                canvasGroup.alpha = 1f - t;

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // 最終状態
            canvasGroup.alpha = 0f;
        }

        private void OnDestroy()
        {
            animationCts?.Cancel();
            animationCts?.Dispose();
        }

        /// <summary>
        /// ダメージタイプ
        /// </summary>
        public enum DamageType
        {
            Normal,    // 通常ダメージ
            Critical,  // クリティカル
            Heal       // 回復
        }
    }
}
