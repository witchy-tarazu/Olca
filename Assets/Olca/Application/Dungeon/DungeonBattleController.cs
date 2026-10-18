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
using Olca.Application.Battle;
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
    ///
    /// バトル中の共通操作（カスタム画面開閉・ゲージ管理）は BattleSessionController に委譲する。
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

        [Header("敵配置設定")]
        [Tooltip("敵と敵の間隔（セル数）")]
        [SerializeField] private int enemySpacing = 30;
        [Tooltip("最初の敵が出現するセルインデックス")]
        [SerializeField] private int firstEnemyCell = 10;

        [Header("遷移設定")]
        [SerializeField] private float hudFadeDuration = 0.25f;
        [SerializeField] private ContactAnimator contactAnimator;
        [Tooltip("バトル終了後、ダンジョン移動を再開するまでの待機時間（ミリ秒）")]
        [SerializeField] private int returnToDungeonDelayMs = 1000;

        [Header("Dungeon UI")]
        [SerializeField] private DungeonHudView dungeonHudView;
        [SerializeField] private CardSelectionView cardSelectionView;

        private DungeonBattleManager manager;
        private UnityRandomProvider randomProvider;
        private UnityLogger logger;
        private UnityInputProvider inputProvider;
        private BattleSessionController battleSession;

        private float dashStartPosition = -1f;    // ダッシュ開始位置（-1=非ダッシュ中）
        private float dashCooldownRemaining = 0f; // クールタイム残り時間

        private CancellationTokenSource transitionCts;
        private IReadOnlyList<Card> pendingCardChoices; // カード報酬フロー用

        private void Start()
        {
            randomProvider = new UnityRandomProvider();
            logger = new UnityLogger();
            inputProvider = new UnityInputProvider();

            var dungeonConfig = DungeonConfig.CreateTestDungeon(dungeonCellCount, enemySpacing, firstEnemyCell);
            var playerCharacter = new PlayerCharacter(
                maxHp: playerMaxHp,
                initialPosition: new Vector2Int(0, 0),
                attackPower: playerAttackPower);

            var cardFactory = new SimpleCardFactory();
            var recipeProvider = new SimpleFusionRecipeProvider();
            var exceedSystem = new ExceedSystem(cardFactory);
            var tuningSystem = new TuningSystem();
            var fusionSystem = new FusionSystem(recipeProvider, cardFactory);

            manager = new DungeonBattleManager(
                dungeonConfig, playerCharacter, CreateInitialDeck(), randomProvider, logger,
                exceedSystem: exceedSystem,
                tuningSystem: tuningSystem,
                fusionSystem: fusionSystem);

            // イベント購読
            manager.OnBattleStarted += OnBattleStarted;
            manager.OnBattleEnded += OnBattleEnded;
            manager.OnCardRewardPresented += OnCardRewardPresented;
            manager.OnGameOver += OnGameOver;
            manager.OnDungeonClearCompleted += OnDungeonCleared;

            // GameFieldView 初期化（フィールド・敵スプライトを世界空間に配置）
            gameFieldView?.Initialize(manager.Dungeon);

            // バトルCardUI は最初から非表示
            battleView?.gameObject.SetActive(false);

            // バトルセッション共通操作をBattleSessionControllerに委譲
            if (battleView != null && customScreenView != null)
                battleSession = new BattleSessionController(battleView, customScreenView, randomProvider, logger);

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
                    // AltキーでBattleSessionControllerに委譲してカスタム画面を開く
                    if (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt))
                        battleSession?.TryOpenCustomScreen();

                    // Spaceキーでキューの次のカードを使用
                    var battle = manager.CurrentBattle;
                    if (Input.GetKeyDown(KeyCode.Space)
                        && battle != null
                        && !battle.CustomScreen.IsOpen
                        && !battle.CardUsage.IsEmpty)
                    {
                        battle.UseCurrentCard();
                    }
                    break;

                case GamePhase.CardReward:
                    // カード選択中はダンジョン操作を無効化
                    break;
            }
        }

        private void UpdateViews()
        {
            switch (manager.CurrentPhase)
            {
                case GamePhase.Dungeon:
                    gameFieldView?.UpdateView();
                    RefreshDungeonHud();
                    break;

                case GamePhase.Battle:
                    gameFieldView?.RefreshBattleField();
                    battleView?.Refresh();
                    break;

                case GamePhase.CardReward:
                    break;
            }
        }

        private void RefreshDungeonHud()
        {
            if (dungeonHudView != null && manager?.Dungeon != null)
            {
                var data = new DungeonHudData(
                    currentHp: manager.Dungeon.Player.CurrentHp,
                    maxHp: manager.Dungeon.Player.MaxHp,
                    playerPosition: manager.Dungeon.PlayerPosition,
                    dungeonLength: dungeonCellCount,
                    isDashing: manager.Dungeon.IsDashing,
                    isDashCooldown: dashCooldownRemaining > 0f,
                    deckCount: manager.Dungeon.GetCurrentDeck().TotalCount,
                    obtainedCardCount: manager.Dungeon.GetObtainedCards().Count
                );
                dungeonHudView.Refresh(data);
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
        /// 1. 接敵アニメーション
        /// 2. バトルグリッドが接触位置に出現
        /// 3. ダンジョンHUD フェードアウト
        /// 4. バトルCardUI が登場
        /// 5. BattleSessionController.Attach → カスタム画面オープン
        /// </summary>
        private async UniTaskVoid TransitionToBattleAsync(BattleSystem battle, ContactInfo contactInfo)
        {
            transitionCts?.Cancel();
            transitionCts?.Dispose();
            transitionCts = new CancellationTokenSource();
            var ct = transitionCts.Token;

            var enemyView = gameFieldView?.GetEnemyView(contactInfo.Enemy);

            try
            {
                // 1. 接敵アニメーション
                if (contactAnimator != null && gameFieldView != null)
                {
                    // ダッシュ接触時もアニメーション中はカメラ追従を維持する。
                    // 即停止するとダッシュ中の追従遅延分だけカメラが後方に残り、
                    // バトル開始時にグリッド中央へ一気に動いてカメラジャンプが起きる。
                    // cameraFollowEnabled = false は OnBattleStarted 内で設定される。

                    enemyView?.SetPositionAnimating(true);
                    await contactAnimator.PlayAsync(
                        contactInfo,
                        gameFieldView.PlayerTransform,
                        enemyView?.transform,
                        ct);
                }

                // 初撃などで戦闘終了した古い開始遷移は、UIやセッションを再開しない。
                ct.ThrowIfCancellationRequested();
                if (battle.IsGameOver || !ReferenceEquals(manager.CurrentBattle, battle)) return;

                // 2. バトルグリッドを接触位置に出現
                gameFieldView?.OnBattleStarted(battle, contactInfo.ContactPositionX);

                // 3. ダンジョンHUD フェードアウトと CardUI 初期化を並行実行
                var hudHideTask = gameFieldView?.HideHUDAsync(hudFadeDuration, ct)
                    ?? UniTask.CompletedTask;

                battleView?.gameObject.SetActive(true);
                battleView?.Initialize(battle);
                customScreenView?.Initialize(battle.CustomScreen);

                // バトルセッション開始（購読・ゲージ管理をBattleSessionControllerに委譲）
                battleSession?.Attach(battle);

                await hudHideTask;

                ct.ThrowIfCancellationRequested();
                if (battle.IsGameOver || !ReferenceEquals(manager.CurrentBattle, battle)) return;

                // 4. ゲージ満タン → カスタム画面オープン
                battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
                battleSession?.TryOpenCustomScreen();

                logger.LogInfo("[Controller] バトル遷移完了");
            }
            catch (System.OperationCanceledException)
            {
                enemyView?.SetPositionAnimating(false);
            }
        }

        // ==================== バトル終了遷移 ====================

        private void OnBattleEnded()
        {
            HandleBattleEndAsync().Forget();
        }

        private void OnCardRewardPresented(IReadOnlyList<Card> choices)
        {
            pendingCardChoices = choices;
        }

        /// <summary>
        /// バトル終了後の処理シーケンス
        /// 1. BattleSessionController.Detach
        /// 2. バトルUI退場 → グリッド消滅 → HUD復帰
        /// 3. カード報酬がある場合はカード選択画面を表示
        /// 4. インターバル後にダンジョン移動を再開
        /// </summary>
        private async UniTaskVoid HandleBattleEndAsync()
        {
            // バトルセッション終了（購読解除）
            battleSession?.Detach();

            transitionCts?.Cancel();
            transitionCts?.Dispose();
            transitionCts = new CancellationTokenSource();
            var ct = transitionCts.Token;

            try
            {
                await TransitionToDungeonAsync(ct);

                // SelectCardReward を呼ぶと同期的に GamePhase.Dungeon に切り替わり、
                // 次フレームの UpdatePlayerPosition でプレイヤーがダンジョン座標にワープして
                // カメラちらつきが起きるため、先に位置更新を無効化しておく
                gameFieldView?.SetPlayerPositionUpdateEnabled(false);

                if (manager.CurrentPhase == GamePhase.CardReward && pendingCardChoices != null)
                {
                    if (cardSelectionView != null)
                    {
                        int deckCount = manager.Dungeon.GetCurrentDeck().TotalCount;
                        Card selectedCard = await cardSelectionView.ShowAsync(pendingCardChoices, deckCount, ct);
                        pendingCardChoices = null;
                        manager.SelectCardReward(selectedCard);
                    }
                    else
                    {
                        // CardSelectionView が未アサインの場合はスキップしてダンジョン復帰
                        logger.LogWarning("[Controller] CardSelectionView が未設定のためカード報酬をスキップします");
                        pendingCardChoices = null;
                        manager.SelectCardReward(null);
                    }
                }

                // 定位置（グリッド列1）へ復帰ジャンプ（ダッシュ宙返りと同じ放物線）
                if (contactAnimator != null
                    && gameFieldView?.PlayerTransform != null
                    && gameFieldView?.BattleGrid != null)
                {
                    float targetX = gameFieldView.BattleGrid.GridIndexToWorldX(1);
                    var targetPos = new UnityEngine.Vector3(
                        targetX, gameFieldView.PlayerTransform.position.y, 0f);
                    await contactAnimator.PlayReturnJumpAsync(
                        gameFieldView.PlayerTransform, targetPos, ct);
                    gameFieldView.SetPlayerPositionUpdateEnabled(true);
                }

                // ジャンプ完了後にカメラ追従を再開（バトル中にカメラはすでに列1+オフセット位置にあるため、スナップ不要）
                gameFieldView?.SetCameraFollowEnabled(true);

                // インターバル後に移動を再開（遷移アニメーション完了を保証）
                await UniTask.Delay(returnToDungeonDelayMs, cancellationToken: ct);
                manager.ResumeAfterBattle();
            }
            catch (System.OperationCanceledException) { }
        }

        /// <summary>
        /// バトル → ダンジョンへの復帰シーケンス
        /// カメラ追従の再開は SnapCameraToPlayerAndResumeFollow() に委譲する
        /// </summary>
        private async UniTask TransitionToDungeonAsync(CancellationToken ct)
        {
            battleView?.gameObject.SetActive(false);
            gameFieldView?.OnBattleEnded();
            await (gameFieldView?.ShowHUDAsync(hudFadeDuration, ct) ?? UniTask.CompletedTask);
            // SetCameraFollowEnabled(true) はここでは呼ばない
            // → カード選択後に SnapCameraToPlayerAndResumeFollow() でスナップしてから再開
            logger.LogInfo("[Controller] ダンジョン復帰完了");
        }

        // ==================== ゲームオーバー / クリア ====================

        private void OnGameOver()
        {
            logger.LogInfo("[Controller] ゲームオーバー");
        }

        private void OnDungeonCleared(IReadOnlyList<Card> obtainedCards)
        {
            // 最終報酬の選択後はDungeonフェーズを経由しないため、ここで最新値を反映する。
            RefreshDungeonHud();
            logger.LogInfo($"[Controller] ダンジョンクリア！ 獲得カード: {obtainedCards.Count}枚");
        }

        // ==================== 初期デッキ ====================

        private Deck CreateInitialDeck()
        {
            var f = new SimpleCardFactory();
            return new Deck(new List<Card>
            {
                // 超越テスト用: 同名カード2枚
                f.CreateById(SimpleCardFactory.Sword),
                f.CreateById(SimpleCardFactory.Sword),
                // 融合テスト用: ソード＋ボム → ファイアソード
                f.CreateById(SimpleCardFactory.Bomb),
                // 調律テスト用: 調律カード
                f.CreateById(SimpleCardFactory.Tuning),
                // 通常カード
                f.CreateById(SimpleCardFactory.Heal),
                f.CreateById(SimpleCardFactory.Shield),
            });
        }

        private void OnDestroy()
        {
            transitionCts?.Cancel();
            transitionCts?.Dispose();
            battleSession?.Detach();

            if (manager != null)
            {
                manager.OnBattleStarted -= OnBattleStarted;
                manager.OnBattleEnded -= OnBattleEnded;
                manager.OnCardRewardPresented -= OnCardRewardPresented;
                manager.OnGameOver -= OnGameOver;
                manager.OnDungeonClearCompleted -= OnDungeonCleared;
            }
        }
    }
}
