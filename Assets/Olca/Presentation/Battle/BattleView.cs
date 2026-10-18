using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// バトル画面全体のView
    /// BattleSystemの状態を表示する
    /// </summary>
    public class BattleView : MonoBehaviour, IBattleView
    {
        // ========== UI要素（カードUI・HP・ゲージのみ。フィールドはGameFieldViewが担当）==========
        [Header("プレイヤー情報")]
        [SerializeField] private Text playerHpText;
        [SerializeField] private RectTransform customGaugeContainer;  // 縦置きゲージコンテナ
        [SerializeField] private GameObject customGaugeLabel;

        [Header("カスタム画面コンテナ")]
        [SerializeField] private RectTransform customScreenContainer;  // HP・ゲージ・カスタム画面を統合したコンテナ

        [Header("アニメーション設定")]
        [SerializeField] private float animationDuration = 0.4f;  // アニメーション時間
        [SerializeField] private float overshootAmount = 50f;     // オーバーラン量

        // ========== 内部状態 ==========
        private BattleSystem battleSystem;
        private bool isCustomScreenOpen = false;
        private Image customScreenBackdrop;
        private CancellationTokenSource animationCts;

        private Slider gaugeSlider;
        private Image gaugeFill;
        private Image gaugeHighlight;
        private Color normalGaugeColor;
        private Vector3 normalGaugeScale;
        private bool gaugeResetting;
        private float gaugePulseTime;

        // ========== 定数 ==========
        private const float CUSTOM_SCREEN_HIDDEN_X = -960f;  // カスタム画面非表示時のX座標（画面外）
        private const float CUSTOM_SCREEN_VISIBLE_X = 0f;    // カスタム画面表示時のX座標（画面内）

        // ========== 初期化 ==========
        public void Initialize(BattleSystem battleSystem)
        {
            DetachBattleEvents();
            ResetGaugeVisual();
            gaugeResetting = false;
            isCustomScreenOpen = false;
            this.battleSystem = battleSystem;
            EnsureGaugeVisual();
            EnsureCustomScreenBackdrop();
            customScreenBackdrop.gameObject.SetActive(false);

            // イベント購読
            battleSystem.CustomGauge.OnGaugeChanged += OnGaugeChanged;
            battleSystem.OnPlayerAttack += OnPlayerAttack;
            battleSystem.OnBattleEnd += OnBattleEnded;

            // 初期状態設定：カスタム画面コンテナを画面外に配置
            if (customScreenContainer != null)
            {
                var anchoredPos = customScreenContainer.anchoredPosition;
                anchoredPos.x = CUSTOM_SCREEN_HIDDEN_X;
                customScreenContainer.anchoredPosition = anchoredPos;
            }

            // CustomGaugeLabelのみ初期状態で非表示
            if (customGaugeLabel != null) customGaugeLabel.SetActive(false);

            Refresh();
        }

        // カスタム画面の表示だけを暗くし、フィールドや入力処理は変更しない。
        private void EnsureCustomScreenBackdrop()
        {
            if (customScreenBackdrop != null) return;
            var backdrop = new GameObject("CustomScreenBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)backdrop.transform;
            rect.SetParent(transform, false);
            rect.SetAsFirstSibling();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            customScreenBackdrop = backdrop.GetComponent<Image>();
            customScreenBackdrop.color = new Color(0f, 0f, 0f, 0.7f);
            customScreenBackdrop.raycastTarget = false;
        }

        // フィルの子として生成し、既存のゲージ配置や入力対象を変えない。
        private void EnsureGaugeVisual()
        {
            if (gaugeSlider != null || customGaugeContainer == null) return;
            gaugeSlider = customGaugeContainer.GetComponentInChildren<Slider>(true);
            if (gaugeSlider == null || gaugeSlider.fillRect == null) return;
            gaugeFill = gaugeSlider.fillRect.GetComponent<Image>();
            if (gaugeFill == null) return;
            normalGaugeColor = gaugeFill.color;
            normalGaugeScale = gaugeSlider.transform.localScale;
            var highlight = new GameObject("FullGaugeHighlight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            highlight.transform.SetParent(gaugeSlider.fillRect, false);
            gaugeHighlight = highlight.GetComponent<Image>();
            gaugeHighlight.color = new Color(1f, 1f, 1f, 0.65f);
            gaugeHighlight.raycastTarget = false;
            gaugeHighlight.gameObject.SetActive(false);
        }

        private void Update()
        {
            UpdateGaugeVisual(Time.unscaledDeltaTime);
        }

        private void UpdateGaugeVisual(float deltaTime)
        {
            if (gaugeFill == null) return;
            bool full = isActiveAndEnabled && battleSystem != null
                && !battleSystem.IsGameOver && battleSystem.CustomGauge.IsFull && !gaugeResetting;
            if (!full)
            {
                ResetGaugeVisual();
                return;
            }

            gaugePulseTime = Mathf.Repeat(gaugePulseTime + deltaTime, 0.5f);
            float phase = gaugePulseTime / 0.5f;
            float pulse = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
            gaugeFill.color = new Color32(0xF3, 0x9C, 0x12, 0xFF);
            gaugeSlider.transform.localScale = normalGaugeScale * (1f + 0.05f * pulse);
            gaugeHighlight.gameObject.SetActive(true);
            var rect = gaugeHighlight.rectTransform;
            float y = phase * 0.84f;
            rect.anchorMin = new Vector2(0f, y);
            rect.anchorMax = new Vector2(1f, y + 0.16f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void ResetGaugeVisual()
        {
            gaugePulseTime = 0f;
            if (gaugeFill != null) gaugeFill.color = normalGaugeColor;
            if (gaugeSlider != null) gaugeSlider.transform.localScale = normalGaugeScale;
            if (gaugeHighlight != null) gaugeHighlight.gameObject.SetActive(false);
        }

        private void OnBattleEnded(BattleResult result) => ResetGaugeVisual();

        private void OnDisable() => ResetGaugeVisual();

        private void DetachBattleEvents()
        {
            if (battleSystem == null) return;
            battleSystem.CustomGauge.OnGaugeChanged -= OnGaugeChanged;
            battleSystem.OnPlayerAttack -= OnPlayerAttack;
            battleSystem.OnBattleEnd -= OnBattleEnded;
        }

        // ========== 更新 ==========
        public void Refresh()
        {
            if (battleSystem == null) return;

            // プレイヤーHP表示（数値のみ）
            var player = battleSystem.Player;
            if (playerHpText != null)
                playerHpText.text = $"{player.CurrentHp}";

            // カスタムゲージ表示（カスタム画面が開いていない場合のみ）
            if (!isCustomScreenOpen && customGaugeContainer != null)
            {
                var slider = customGaugeContainer.GetComponentInChildren<Slider>();
                if (slider != null)
                    slider.value = battleSystem.CustomGauge.NormalizedGauge;
            }
            UpdateGaugeVisual(0f);
            // フィールド・敵の更新は GameFieldView.RefreshBattleField() が担当
        }

        // ========== カスタム画面制御（UniTaskベース） ==========
        /// <summary>
        /// カスタム画面を開く（UniTaskベースのアニメーション）
        /// Phase2: オーバーランを含むスライドインアニメーション
        /// </summary>
        public void OpenCustomScreen()
        {
            if (isCustomScreenOpen) return;
            isCustomScreenOpen = true;
            EnsureCustomScreenBackdrop();
            customScreenBackdrop.gameObject.SetActive(true);

            // 前回のアニメーションをキャンセル
            animationCts?.Cancel();
            animationCts = new CancellationTokenSource();

            // アニメーション実行（非同期）
            AnimateCustomScreenOpenAsync(animationCts.Token).Forget();
        }

        /// <summary>
        /// カスタム画面を閉じる（UniTaskベースのアニメーション）
        /// Phase2: オーバーランを含むスライドアウトアニメーション
        /// アニメーション完了まで待つ
        /// </summary>
        public async UniTask CloseCustomScreenAsync()
        {
            if (!isCustomScreenOpen) return;
            isCustomScreenOpen = false;
            if (customScreenBackdrop != null) customScreenBackdrop.gameObject.SetActive(false);

            // 前回のアニメーションをキャンセル
            animationCts?.Cancel();
            var closingCts = new CancellationTokenSource();
            animationCts = closingCts;

            // アニメーション実行（完了まで待つ）
            gaugeResetting = true;
            ResetGaugeVisual();
            try
            {
                await AnimateCustomScreenCloseAsync(closingCts.Token);
            }
            catch
            {
                if (ReferenceEquals(animationCts, closingCts))
                {
                    isCustomScreenOpen = true;
                    if (customScreenBackdrop != null) customScreenBackdrop.gameObject.SetActive(true);
                }
                throw;
            }
            finally
            {
                if (ReferenceEquals(animationCts, closingCts)) gaugeResetting = false;
            }
        }

        // ========== IBattleView 明示的実装 ==========
        /// <summary>
        /// IBattleView.CloseCustomScreenAsync の実装。
        /// UniTask版をTask に変換して返す。
        /// </summary>
        async System.Threading.Tasks.Task IBattleView.CloseCustomScreenAsync()
            => await CloseCustomScreenAsync();

        // ========== アニメーション実装（UniTask） ==========
        /// <summary>
        /// カスタム画面を開くアニメーション
        /// 画面外 → オーバーラン → 定位置
        /// </summary>
        private async UniTask AnimateCustomScreenOpenAsync(CancellationToken ct)
        {
            if (customScreenContainer == null) return;

            // 「準備中」ラベルを表示
            if (customGaugeLabel != null) customGaugeLabel.SetActive(true);

            // Phase 1: 画面外 → オーバーラン (0.25秒、EaseOutQuad)
            float phase1Duration = animationDuration * 0.625f; // 0.25秒
            float elapsed = 0f;

            while (elapsed < phase1Duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / phase1Duration;
                float easedT = EaseOutQuad(t);

                float currentX = Mathf.Lerp(CUSTOM_SCREEN_HIDDEN_X, CUSTOM_SCREEN_VISIBLE_X + overshootAmount, easedT);
                customScreenContainer.anchoredPosition = new Vector2(currentX, customScreenContainer.anchoredPosition.y);

                await UniTask.Yield(ct);
            }

            // Phase 2: オーバーラン → 定位置 (0.15秒、EaseInQuad)
            float phase2Duration = animationDuration * 0.375f; // 0.15秒
            elapsed = 0f;

            while (elapsed < phase2Duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / phase2Duration;
                float easedT = EaseInQuad(t);

                float currentX = Mathf.Lerp(CUSTOM_SCREEN_VISIBLE_X + overshootAmount, CUSTOM_SCREEN_VISIBLE_X, easedT);
                customScreenContainer.anchoredPosition = new Vector2(currentX, customScreenContainer.anchoredPosition.y);

                await UniTask.Yield(ct);
            }

            // 最終位置を保証
            customScreenContainer.anchoredPosition = new Vector2(CUSTOM_SCREEN_VISIBLE_X, customScreenContainer.anchoredPosition.y);
        }

        /// <summary>
        /// カスタム画面を閉じるアニメーション
        /// 定位置 → オーバーラン → 画面外
        /// </summary>
        private async UniTask AnimateCustomScreenCloseAsync(CancellationToken ct)
        {
            if (customScreenContainer == null) return;

            // Phase 1: 定位置 → オーバーラン (0.15秒、EaseOutQuad)
            float phase1Duration = animationDuration * 0.375f; // 0.15秒
            float elapsed = 0f;

            while (elapsed < phase1Duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / phase1Duration;
                float easedT = EaseOutQuad(t);

                float currentX = Mathf.Lerp(CUSTOM_SCREEN_VISIBLE_X, CUSTOM_SCREEN_VISIBLE_X + overshootAmount, easedT);
                customScreenContainer.anchoredPosition = new Vector2(currentX, customScreenContainer.anchoredPosition.y);

                await UniTask.Yield(ct);
            }

            // Phase 2: オーバーラン → 画面外 (0.25秒、EaseInQuad)
            float phase2Duration = animationDuration * 0.625f; // 0.25秒
            elapsed = 0f;

            while (elapsed < phase2Duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / phase2Duration;
                float easedT = EaseInQuad(t);

                float currentX = Mathf.Lerp(CUSTOM_SCREEN_VISIBLE_X + overshootAmount, CUSTOM_SCREEN_HIDDEN_X, easedT);
                customScreenContainer.anchoredPosition = new Vector2(currentX, customScreenContainer.anchoredPosition.y);

                await UniTask.Yield(ct);
            }

            // 最終位置を保証
            customScreenContainer.anchoredPosition = new Vector2(CUSTOM_SCREEN_HIDDEN_X, customScreenContainer.anchoredPosition.y);

            // 「準備中」ラベルを非表示
            if (customGaugeLabel != null) customGaugeLabel.SetActive(false);

            // カスタムゲージを0にリセット（急速アニメーション）
            await AnimateGaugeResetAsync(ct);
        }

        /// <summary>
        /// カスタムゲージを最大から0へ急速にアニメーション
        /// </summary>
        private async UniTask AnimateGaugeResetAsync(CancellationToken ct)
        {
            if (customGaugeContainer == null) return;

            var slider = customGaugeContainer.GetComponentInChildren<Slider>();
            if (slider == null) return;

            float resetDuration = 0.3f; // 0.3秒で急速にリセット
            float elapsed = 0f;
            float startValue = slider.value;

            while (elapsed < resetDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / resetDuration;
                float easedT = EaseInQuad(t);

                slider.value = Mathf.Lerp(startValue, 0f, easedT);

                await UniTask.Yield(ct);
            }

            // 最終値を保証
            slider.value = 0f;
        }

        // ========== イージング関数 ==========
        /// <summary>
        /// EaseOutQuad: 速く始まり、ゆっくり終わる
        /// </summary>
        private float EaseOutQuad(float t)
        {
            return 1f - (1f - t) * (1f - t);
        }

        /// <summary>
        /// EaseInQuad: ゆっくり始まり、速く終わる
        /// </summary>
        private float EaseInQuad(float t)
        {
            return t * t;
        }

        // ========== イベントハンドラー ==========
        private void OnGaugeChanged(float newValue)
        {
            UpdateGaugeVisual(0f);
            if (!isCustomScreenOpen && customGaugeContainer != null)
            {
                var slider = customGaugeContainer.GetComponentInChildren<Slider>();
                if (slider != null)
                {
                    slider.value = newValue;
                }
            }
        }

        private void OnPlayerAttack(Enemy target, int damage)
        {
            Debug.Log($"プレイヤーが{target.Name}に{damage}ダメージ！");
            // ダメージ数値表示（後で実装）
        }

        private void OnDestroy()
        {
            // アニメーション用CancellationTokenをキャンセル
            animationCts?.Cancel();
            animationCts?.Dispose();

            DetachBattleEvents();
            ResetGaugeVisual();
        }
    }
}
