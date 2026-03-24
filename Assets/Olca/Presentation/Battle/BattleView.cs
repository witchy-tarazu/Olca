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
        private CancellationTokenSource animationCts;

        // ========== 定数 ==========
        private const float CUSTOM_SCREEN_HIDDEN_X = -960f;  // カスタム画面非表示時のX座標（画面外）
        private const float CUSTOM_SCREEN_VISIBLE_X = 0f;    // カスタム画面表示時のX座標（画面内）

        // ========== 初期化 ==========
        public void Initialize(BattleSystem battleSystem)
        {
            this.battleSystem = battleSystem;

            // イベント購読
            battleSystem.CustomGauge.OnGaugeChanged += OnGaugeChanged;
            battleSystem.OnPlayerAttack += OnPlayerAttack;

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

            // 前回のアニメーションをキャンセル
            animationCts?.Cancel();
            animationCts = new CancellationTokenSource();

            // アニメーション実行（完了まで待つ）
            await AnimateCustomScreenCloseAsync(animationCts.Token);
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

            if (battleSystem != null)
            {
                battleSystem.CustomGauge.OnGaugeChanged -= OnGaugeChanged;
                battleSystem.OnPlayerAttack -= OnPlayerAttack;
            }
        }
    }
}
