using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// バトル画面全体のView
    /// BattleSystemの状態を表示する
    /// </summary>
    public class BattleView : MonoBehaviour
    {
        // ========== UI要素 ==========
        [Header("プレイヤー情報")]
        [SerializeField] private Text playerHpText;
        [SerializeField] private RectTransform customGaugeContainer;  // 縦置きゲージコンテナ
        [SerializeField] private GameObject customGaugeLabel;

        [Header("カスタム画面コンテナ")]
        [SerializeField] private RectTransform customScreenContainer;  // HP・ゲージ・カスタム画面を統合したコンテナ

        [Header("グリッド表示")]
        [SerializeField] private BattleFieldView battleFieldView;

        [Header("敵表示")]
        [SerializeField] private Transform enemyContainer;
        [SerializeField] private GameObject enemyViewPrefab;

        // ========== 内部状態 ==========
        private BattleSystem battleSystem;
        private List<EnemyView> enemyViews = new List<EnemyView>();
        private bool isCustomScreenOpen = false;

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

            // フィールド初期化
            battleFieldView.Initialize(battleSystem.Field, battleSystem.Player);

            // 敵View生成
            foreach (var enemy in battleSystem.Enemies)
            {
                var enemyObj = Instantiate(enemyViewPrefab, enemyContainer);
                var enemyView = enemyObj.GetComponent<EnemyView>();
                enemyView.Initialize(enemy);
                enemyViews.Add(enemyView);
            }

            // 初期状態設定：カスタム画面コンテナを画面外に配置
            // Phase2でスライドインアニメーション実装予定
            // 注意: HP・ゲージもCustomScreenContainerの子なので、別途配置が必要
            if (customScreenContainer != null)
            {
                var anchoredPos = customScreenContainer.anchoredPosition;
                anchoredPos.x = CUSTOM_SCREEN_HIDDEN_X;  // -960に設定（画面外）
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
            {
                playerHpText.text = $"{player.CurrentHp}";
            }

            // カスタムゲージ表示（カスタム画面が開いていない場合のみ）
            if (!isCustomScreenOpen && customGaugeContainer != null)
            {
                var slider = customGaugeContainer.GetComponentInChildren<Slider>();
                if (slider != null)
                {
                    slider.value = battleSystem.CustomGauge.CurrentGauge;
                }
            }

            // 敵View更新
            foreach (var enemyView in enemyViews)
            {
                enemyView.Refresh();
            }

            // グリッド更新
            battleFieldView.Refresh();
        }

        // ========== カスタム画面制御 ==========
        /// <summary>
        /// カスタム画面を開く
        /// Phase1: 座標を即座に切り替え（アニメーションなし）
        /// Phase2: UniTask/DOTweenでスライドインアニメーション実装予定
        /// </summary>
        public void OpenCustomScreen()
        {
            if (isCustomScreenOpen) return;
            isCustomScreenOpen = true;

            // CustomScreenContainerを画面内に移動（Phase1: 即座に移動）
            if (customScreenContainer != null)
            {
                var anchoredPos = customScreenContainer.anchoredPosition;
                anchoredPos.x = CUSTOM_SCREEN_VISIBLE_X;
                customScreenContainer.anchoredPosition = anchoredPos;
            }

            // 「準備中」ラベルをゲージの上に表示
            if (customGaugeLabel != null) customGaugeLabel.SetActive(true);
        }

        /// <summary>
        /// カスタム画面を閉じる
        /// Phase1: 座標を即座に切り替え（アニメーションなし）
        /// Phase2: UniTask/DOTweenでスライドアウトアニメーション実装予定
        /// </summary>
        public void CloseCustomScreen()
        {
            if (!isCustomScreenOpen) return;
            isCustomScreenOpen = false;

            // CustomScreenContainerを画面外に移動（Phase1: 即座に移動）
            if (customScreenContainer != null)
            {
                var anchoredPos = customScreenContainer.anchoredPosition;
                anchoredPos.x = CUSTOM_SCREEN_HIDDEN_X;
                customScreenContainer.anchoredPosition = anchoredPos;
            }

            // 「準備中」ラベルを非表示
            if (customGaugeLabel != null) customGaugeLabel.SetActive(false);
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
            if (battleSystem != null)
            {
                battleSystem.CustomGauge.OnGaugeChanged -= OnGaugeChanged;
                battleSystem.OnPlayerAttack -= OnPlayerAttack;
            }
        }
    }
}
