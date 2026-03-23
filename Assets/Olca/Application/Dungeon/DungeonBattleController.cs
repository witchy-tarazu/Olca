using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Olca.Core.Logic.Integration;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Domain;
using Olca.Unity;
using Olca.Presentation.Field;
using Olca.Presentation.Battle;
using Olca.Presentation.CustomScreen;
using Olca.Presentation.Dungeon;
using Vector2Int = Olca.Core.Domain.Vector2Int;

namespace Olca.Application.Dungeon
{
    /// <summary>
    /// ダンジョンとバトルの統合コントローラー
    ///
    /// 遷移方針（統合フィールド）:
    /// - GameFieldView がフィールド（プレイヤー・敵・グリッド）を世界空間で常時管理
    /// - バトル開始: 接敵アニメーション → HUD退場 → グリッド出現 → バトルCardUI登場
    /// - バトル終了: CardUI退場 → グリッド消滅 → HUD復帰
    /// </summary>
    public class DungeonBattleController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameFieldView gameFieldView;
        [SerializeField] private BattleView battleView;
        [SerializeField] private CustomScreenView customScreenView;

        [Header("Settings")]
        [SerializeField] private int dungeonCellCount = 20;
        [SerializeField] private int playerMaxHp = 100;
        [SerializeField] private int playerAttackPower = 10;
        [SerializeField] private float dashDistanceCells = 3f;  // ダッシュで進むマス数
        [SerializeField] private float dashCooldown = 0.1f;   // ダッシュのクールタイム（秒）

        [Header("遷移設定")]
        [SerializeField] private float hudFadeDuration = 0.25f;
        [SerializeField] private ContactAnimator contactAnimator;

        private DungeonBattleManager manager;
        private UnityRandomProvider randomProvider;
        private UnityLogger logger;
        private UnityInputProvider inputProvider;

        private float dashStartPosition = -1f;    // ダッシュ開始位置（-1=非ダッシュ中）
        private float dashCooldownRemaining = 0f; // クールタイム残り時間

        private CancellationTokenSource transitionCts;

        private void Start()
        {
            randomProvider = new UnityRandomProvider();
            logger = new UnityLogger();
            inputProvider = new UnityInputProvider();

            var dungeonConfig = DungeonConfig.CreateTestDungeon(dungeonCellCount);
            var playerCharacter = new PlayerCharacter(
                maxHp: playerMaxHp,
                initialPosition: new Vector2Int(0, 0),
                attackPower: playerAttackPower);
            var initialDeck = CreateInitialDeck();

            manager = new DungeonBattleManager(
                dungeonConfig, playerCharacter, initialDeck, randomProvider, logger);

            // イベント購読
            manager.OnBattleStarted += OnBattleStarted;
            manager.OnGameOver += OnGameOver;
            manager.OnDungeonClearCompleted += OnDungeonCleared;

            // GameFieldView 初期化（フィールド・敵スプライトを世界空間に配置）
            gameFieldView?.Initialize(manager.Dungeon);

            // バトルCardUI は最初から非表示
            battleView?.gameObject.SetActive(false);

            logger.LogInfo("[DungeonBattleController] 初期化完了");
        }

        private void Update()
        {
            if (manager == null) return;
            inputProvider.Update();
            HandleInput();
            int deltaTimeMs = Mathf.RoundToInt(Time.deltaTime * 1000f);
            manager.Tick(deltaTimeMs, inputProvider);
            UpdateViews();
        }

        private void HandleInput()
        {
            switch (manager.CurrentPhase)
            {
                case GamePhase.Dungeon:
                    if (Input.GetKeyDown(KeyCode.Space)) manager.Jump();

                    // ダッシュ: 押した瞬間に発動し、指定マス数移動したら終了
                    bool dashPressed = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift);
                    if (dashPressed && dashCooldownRemaining <= 0f && dashStartPosition < 0f)
                    {
                        manager.StartDash();
                        dashStartPosition = manager.Dungeon.PlayerPosition;
                    }

                    if (dashStartPosition >= 0f)
                    {
                        float traveled = manager.Dungeon.PlayerPosition - dashStartPosition;
                        if (traveled >= dashDistanceCells)
                        {
                            manager.StopDash();
                            dashStartPosition = -1f;
                            dashCooldownRemaining = dashCooldown;
                        }
                    }

                    if (dashCooldownRemaining > 0f)
                        dashCooldownRemaining -= Time.deltaTime;
                    break;

                case GamePhase.Battle:
                    break;
            }
        }

        private void UpdateViews()
        {
            switch (manager.CurrentPhase)
            {
                case GamePhase.Dungeon:
                    gameFieldView?.UpdateView();
                    break;

                case GamePhase.Battle:
                    // フィールド（グリッド・敵）と CardUI の両方を更新
                    gameFieldView?.RefreshBattleField();
                    battleView?.Refresh();
                    break;
            }
        }

        // ==================== バトル開始遷移 ====================

        private void OnBattleStarted(BattleSystem battle, ContactInfo contactInfo)
        {
            TransitionToBattleAsync(battle, contactInfo).Forget();
        }

        /// <summary>
        /// ダンジョン → バトルへの遷移シーケンス
        ///
        /// 1. 接敵アニメーション（フィールドはそのまま、同一スプライトが継続）
        /// 2. バトルグリッドが接触位置に出現
        /// 3. ダンジョンHUD フェードアウト
        /// 4. バトルCardUI が登場
        /// 5. カスタム画面オープン
        /// </summary>
        private async UniTaskVoid TransitionToBattleAsync(BattleSystem battle, ContactInfo contactInfo)
        {
            transitionCts?.Cancel();
            transitionCts?.Dispose();
            transitionCts = new CancellationTokenSource();
            var ct = transitionCts.Token;

            // 接触した敵の View を取得（ダッシュ時の敵ノックバックアニメーション用）
            var enemyView = gameFieldView?.GetEnemyView(contactInfo.Enemy);

            try
            {
                // 1. 接敵アニメーション
                // ダッシュ: カメラをロック（突進・宙返りが画面上で鮮明に見えるように）
                // 通常: カメラ追従を維持（ノックバックをカメラが追いかける）
                if (contactAnimator != null && gameFieldView != null)
                {
                    if (contactInfo.IsDashContact)
                        gameFieldView.SetCameraFollowEnabled(false);

                    enemyView?.SetPositionAnimating(true);
                    await contactAnimator.PlayAsync(
                        contactInfo,
                        gameFieldView.PlayerTransform,
                        enemyView?.transform,
                        ct);
                }

                // 2. バトルグリッドを接触位置に出現 + 敵をグリッド座標に移動
                gameFieldView?.OnBattleStarted(battle, contactInfo.ContactPositionX);

                // 3. ダンジョンHUD フェードアウトと CardUI 初期化を並行実行
                var hudHideTask = gameFieldView?.HideHUDAsync(hudFadeDuration, ct)
                    ?? UniTask.CompletedTask;

                battleView?.gameObject.SetActive(true);
                battleView?.Initialize(battle);
                customScreenView?.Initialize(battle.CustomScreen);
                battle.CustomGauge.OnGaugeFull += () => OnGaugeFull(battle);

                await hudHideTask;

                // 4. ゲージ満タン → カスタム画面オープン
                battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
                battle.OpenCustomScreen(randomProvider);
                battleView?.OpenCustomScreen();

                logger.LogInfo("[Controller] バトル遷移完了");
            }
            catch (System.OperationCanceledException)
            {
                enemyView?.SetPositionAnimating(false);
            }
        }

        // ==================== バトル終了遷移 ====================

        /// <summary>
        /// バトル → ダンジョンへの復帰シーケンス
        ///
        /// 1. バトルCardUI 退場
        /// 2. バトルグリッド消滅
        /// 3. ダンジョンHUD フェードイン
        /// 4. カメラ追従再開
        /// </summary>
        private async UniTaskVoid TransitionToDungeonAsync()
        {
            transitionCts?.Cancel();
            transitionCts?.Dispose();
            transitionCts = new CancellationTokenSource();
            var ct = transitionCts.Token;

            try
            {
                // CardUI を非表示
                battleView?.gameObject.SetActive(false);

                // グリッド消滅 + HUD 復帰
                gameFieldView?.OnBattleEnded();
                await (gameFieldView?.ShowHUDAsync(hudFadeDuration, ct) ?? UniTask.CompletedTask);

                // カメラ追従を再開
                gameFieldView?.SetCameraFollowEnabled(true);

                logger.LogInfo("[Controller] ダンジョン復帰完了");
            }
            catch (System.OperationCanceledException) { }
        }

        // ==================== ゲージ満タン ====================

        private void OnGaugeFull(BattleSystem battle)
        {
            if (battle.CustomScreen.IsOpen) return;
            battle.OpenCustomScreen(randomProvider);
            battleView?.OpenCustomScreen();
        }

        // ==================== ゲームオーバー / クリア ====================

        private void OnGameOver()
        {
            logger.LogInfo("[Controller] ゲームオーバー");
        }

        private void OnDungeonCleared(IReadOnlyList<Card> obtainedCards)
        {
            logger.LogInfo($"[Controller] ダンジョンクリア！ 獲得カード: {obtainedCards.Count}枚");
        }

        // ==================== 初期デッキ ====================

        private Deck CreateInitialDeck()
        {
            var cards = new List<Card>
            {
                new Card("sword",  "ソード",  1, CardRarity.Normal, CardCategory.Attack,
                    ElementType.None, CardSeries.Sword, 30, "前方1マス攻撃"),
                new Card("heal",   "ヒール",  2, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None,  20, "HP回復"),
                new Card("shield", "シールド", 1, CardRarity.Normal, CardCategory.Defense,
                    ElementType.None, CardSeries.None,  15, "防御")
            };
            return new Deck(cards);
        }

        private void OnDestroy()
        {
            transitionCts?.Cancel();
            transitionCts?.Dispose();

            if (manager != null)
            {
                manager.OnBattleStarted -= OnBattleStarted;
                manager.OnGameOver -= OnGameOver;
                manager.OnDungeonClearCompleted -= OnDungeonCleared;
            }
        }
    }
}
