using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
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
        private readonly UnityLogicalLoop logicalLoop = new UnityLogicalLoop(observeEditor: true);
        public long LogicalFrame => logicalLoop.Clock.Frame;
        public long PendingLogicalFrames => logicalLoop.Clock.PendingFrames;
        private BattleSessionController battleSession;

        private float dashStartPosition = -1f;    // ダッシュ開始位置（-1=非ダッシュ中）
        private int dashCooldownRemaining; // クールタイム残り論理フレーム

        private bool battleFieldReady;
        private GameObject pauseCanvas;
        private GameObject pauseOverlay;
        private float timeScaleBeforePause;
        [SerializeField, HideInInspector] private string checkpointDirectoryOverride;
        private bool awaitingCheckpointChoice;
        private DungeonConfig configuredDungeon;
        private int? simulationSeed;
        public void ConfigureSimulationSeed(int seed)
        {
            if (manager != null) throw new System.InvalidOperationException("起動後はseedを変更できません");
            simulationSeed = seed;
        }
        public void ConfigureDungeon(DungeonConfig config)
        {
            if (manager != null) throw new System.InvalidOperationException("起動後はマップを変更できません");
            configuredDungeon = config ?? throw new System.ArgumentNullException(nameof(config));
        }
        private GameObject checkpointOverlay;
        private Text checkpointMessage;
        private Button continueCheckpointButton, retryCheckpointButton;
        public string CheckpointDirectory => string.IsNullOrEmpty(checkpointDirectoryOverride)
            ? System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "Olca", "checkpoints") : checkpointDirectoryOverride;

        // 起動前に独立した検証先を注入できる。稼働中の保存先変更は禁止。
        public void ConfigureCheckpointDirectory(string directory)
        {
            if (manager != null) throw new System.InvalidOperationException("起動後は保存先を変更できません");
            checkpointDirectoryOverride = System.IO.Path.GetFullPath(directory);
        }
        private void ShowCheckpointStatus(string message = null)
        {
            bool blocked = manager != null && manager.Dungeon.IsCheckpointBlocked;
            checkpointOverlay.SetActive(blocked || awaitingCheckpointChoice);
            retryCheckpointButton.gameObject.SetActive(blocked);
            continueCheckpointButton.gameObject.SetActive(!blocked);
            checkpointMessage.text = message ?? (blocked
                ? "保存できないため進行を停止しています。\n保存済みデータは保持しています。\n原因を解消して同じ内容の保存を再試行してください。\n" + manager.Dungeon.CheckpointError
                : "保存されたチェックポイントがあります。\n続きから再開します。\n対応外の形式・データは上書きせず停止します。");
        }
        private void ContinueCheckpointFromUi()
        {
            if (!TryRestoreMemoryCheckpoint(out var error))
            {
                awaitingCheckpointChoice = true;
                ShowCheckpointStatus("再開できないため進行を停止しています。\n保存データと現在の状態は保持しています。\n" + error);
            }
        }

        public bool IsUserPaused => manager != null && manager.IsUserPaused;
        public void SetUserPaused(bool paused)
        {
            if (manager == null || manager.IsUserPaused == paused) return;
            logicalLoop.Observe(!manager.IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked && Time.timeScale > 0);
            logicalLoop.ReleaseInput();
            manager.SetUserPaused(paused);
            GameplayPause.SetPaused(paused);
            if (paused) { timeScaleBeforePause = Time.timeScale; Time.timeScale = 0f; }
            else Time.timeScale = timeScaleBeforePause;
            pauseOverlay?.SetActive(paused);
        }

        private void CreatePauseControls()
        {
            pauseCanvas = new GameObject("GameplayPauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = pauseCanvas.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32000;
            var scaler = pauseCanvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = 0.5f;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (customScreenView != null)
            {
                var existing = customScreenView.GetComponentInChildren<Text>(true);
                if (existing != null && existing.font != null) font = existing.font;
            }
            Button MakeButton(string name, string label, Transform parent, Vector2 anchor, Vector2 position)
            {
                var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
                var rect = (RectTransform)obj.transform; rect.anchorMin = rect.anchorMax = anchor; rect.sizeDelta = new Vector2(200, 70); rect.anchoredPosition = position;
                obj.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.32f, 1f);
                var button = obj.GetComponent<Button>(); button.navigation = new Navigation { mode = Navigation.Mode.None };
                var textObj = new GameObject("Label", typeof(RectTransform), typeof(Text)); textObj.transform.SetParent(obj.transform, false);
                var text = textObj.GetComponent<Text>(); text.font = font; text.text = label; text.fontSize = 30; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
                var tr = text.rectTransform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
                return button;
            }
            MakeButton("PauseButton", "ポーズ", pauseCanvas.transform, Vector2.one, new Vector2(-120, -60)).onClick.AddListener(() => SetUserPaused(true));
            pauseOverlay = new GameObject("PauseOverlay", typeof(RectTransform), typeof(Image)); pauseOverlay.transform.SetParent(pauseCanvas.transform, false);
            var overlayRect = (RectTransform)pauseOverlay.transform; overlayRect.anchorMin = Vector2.zero; overlayRect.anchorMax = Vector2.one; overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            pauseOverlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            MakeButton("ResumeButton", "再開", pauseOverlay.transform, new Vector2(0.5f, 0.5f), Vector2.zero).onClick.AddListener(() => SetUserPaused(false));
            MakeButton("LoadCheckpointButton", "続きから", pauseOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -100)).onClick.AddListener(ContinueCheckpointFromUi);
            pauseOverlay.SetActive(false);
            checkpointOverlay = new GameObject("CheckpointOverlay", typeof(RectTransform), typeof(Image));
            checkpointOverlay.transform.SetParent(pauseCanvas.transform, false);
            var cr = (RectTransform)checkpointOverlay.transform;
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
            checkpointOverlay.GetComponent<Image>().color = new Color(0.02f, 0.04f, 0.08f, 0.94f);
            var message = new GameObject("CheckpointMessage", typeof(RectTransform), typeof(Text));
            message.transform.SetParent(checkpointOverlay.transform, false);
            checkpointMessage = message.GetComponent<Text>(); checkpointMessage.font = font;
            checkpointMessage.fontSize = 30; checkpointMessage.alignment = TextAnchor.MiddleCenter;
            checkpointMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            checkpointMessage.rectTransform.sizeDelta = new Vector2(1360, 560);
            checkpointMessage.rectTransform.anchoredPosition = new Vector2(0, 90);
            continueCheckpointButton = MakeButton("ContinueCheckpointButton", "続きから", checkpointOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -270));
            continueCheckpointButton.onClick.AddListener(ContinueCheckpointFromUi);
            retryCheckpointButton = MakeButton("RetryCheckpointButton", "保存を再試行", checkpointOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -270));
            ((RectTransform)retryCheckpointButton.transform).sizeDelta = new Vector2(320, 70);
            retryCheckpointButton.onClick.AddListener(() => { RetryMemoryCheckpoint(); ShowCheckpointStatus(); });
            ShowCheckpointStatus();
        }
        private CancellationTokenSource transitionCts;
        private IReadOnlyList<Card> pendingCardChoices; // カード報酬フロー用

        private void Start()
        {
            randomProvider = simulationSeed.HasValue ? new UnityRandomProvider(simulationSeed.Value) : new UnityRandomProvider();
            logger = new UnityLogger();
            inputProvider = new UnityInputProvider();

            var dungeonConfig = configuredDungeon ?? DungeonConfig.CreateTestDungeon(dungeonCellCount, enemySpacing, firstEnemyCell);
            var playerCharacter = new PlayerCharacter(
                maxHp: playerMaxHp,
                initialPosition: new Vector2Int(0, 0),
                attackPower: playerAttackPower);

            var cardFactory = new SimpleCardFactory();
            var recipeProvider = new SimpleFusionRecipeProvider();
            var exceedSystem = new ExceedSystem(cardFactory);
            var tuningSystem = new TuningSystem();
            var fusionSystem = new FusionSystem(recipeProvider, cardFactory);

            var initialDeck = CreateInitialDeck();
            var definitions = DungeonCheckpointSession.CreateDefault(dungeonConfig, initialDeck).Definitions;
            var store = new FileCheckpointStore(definitions, CheckpointDirectory);
            memoryCheckpoints = new DungeonCheckpointSession(definitions, store);
            awaitingCheckpointChoice = store.HasExistingData;
            manager = new DungeonBattleManager(
                dungeonConfig, playerCharacter, initialDeck, randomProvider, logger,
                exceedSystem: exceedSystem,
                tuningSystem: tuningSystem,
                fusionSystem: fusionSystem, checkpoints: awaitingCheckpointChoice ? null : memoryCheckpoints);

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
                battleSession = new BattleSessionController(battleView, customScreenView, randomProvider, logger,
                    (frames, action) => logicalLoop.AfterFrames(frames, action), battleView.CloseDurationFrames);

            CreatePauseControls();
            logicalLoop.Observe(false);
            logger.LogInfo("[DungeonBattleController] 初期化完了");
        }

        private void Update()
        {
            if (manager == null) return;
            GameplayPause.UpdateInputBarrier();
            bool active = !IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked && Time.timeScale > 0;
            var sample = GameplayPause.InputBlocked ? new LogicalInput() : inputProvider.CaptureLogical();
            logicalLoop.Pump(active, sample, TickLogicalFrame, () => !IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked,
                () => manager.CurrentPhase == GamePhase.Dungeon || (manager.CurrentBattle != null && !manager.CurrentBattle.IsPaused));
            if (manager.Dungeon.IsCheckpointBlocked) ShowCheckpointStatus();
            UpdateViews();
        }

        private void TickLogicalFrame(long frame, int deltaTimeMs, LogicalInput input)
        {
            if (GameplayPause.InputBlocked) input = new LogicalInput();
            HandleInput(input, deltaTimeMs);
            manager.TickFrame(deltaTimeMs, input);
        }

        private void HandleInput(LogicalInput input, int deltaTimeMs)
        {
            switch (manager.CurrentPhase)
            {
                case GamePhase.Dungeon:
                    if (input.JumpButtonDown) manager.Jump();

                    // ダッシュ: 押した瞬間に発動し、指定マス数移動したら終了
                    bool dashPressed = input.DashButtonDown;
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
                            dashCooldownRemaining = LogicalFrameClock.DurationFrames(dashCooldown);
                        }
                    }

                    if (dashCooldownRemaining > 0f)
                        dashCooldownRemaining--;
                    break;

                case GamePhase.Battle:
                    // AltキーでBattleSessionControllerに委譲してカスタム画面を開く
                    if (input.CustomScreenButtonDown)
                        battleSession?.TryOpenCustomScreen();

                    // 既存Spaceバインドを保ち、実行中の再入制御はセッションに委譲する。
                    if (input.CardUseButtonDown)
                        battleSession?.UseCardFromInput();
                    break;

                case GamePhase.CardReward:
                    // カード選択中はダンジョン操作を無効化
                    break;
            }
        }

        private void UpdateViews()
        {
            gameFieldView?.RefreshDebugUI(manager.CurrentPhase);
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
            battleFieldReady = false;
            // 接敵フレームでモデルを停止する。描画遷移中にAIや状態異常を先行させない。
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            if (!battle.CustomScreen.IsOpen) battle.OpenCustomScreen(randomProvider);
            battleSession?.Attach(battle);
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
                        ct, gameFieldView.CellSize,
                        x => gameFieldView.BattleGrid != null ? gameFieldView.BattleGrid.SnapWorldXToCellCenter(x) : x);
                }

                // 初撃などで戦闘終了した古い開始遷移は、UIやセッションを再開しない。
                await GameplayPause.WaitAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (battle.IsGameOver || !ReferenceEquals(manager.CurrentBattle, battle)) return;

                // 2. バトルグリッドを接触位置に出現
                gameFieldView?.OnBattleStarted(battle, contactInfo.ContactPositionX);
                battleFieldReady = true;

                // 3. ダンジョンHUD フェードアウトと CardUI 初期化を並行実行
                var hudHideTask = gameFieldView?.HideHUDAsync(hudFadeDuration, ct)
                    ?? UniTask.CompletedTask;

                battleView?.gameObject.SetActive(true);
                battleView?.Initialize(battle);
                customScreenView?.Initialize(battle.CustomScreen);

                // バトルセッション開始（購読・ゲージ管理をBattleSessionControllerに委譲）
                // 論理セッションは接敵フレームでAttach済み。

                await hudHideTask;

                await GameplayPause.WaitAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (battle.IsGameOver || !ReferenceEquals(manager.CurrentBattle, battle)) return;

                // 4. ゲージ満タン → カスタム画面オープン
                if (battle.CustomScreen.IsOpen) battleView?.OpenCustomScreen();

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
            if (manager.CurrentPhase == GamePhase.Dungeon) ScheduleExplorationResume();
            HandleBattleEndAsync().Forget();
        }

        private void ScheduleExplorationResume()
        {
            var owner = manager;
            double seconds = hudFadeDuration + (contactAnimator != null ? contactAnimator.ReturnDurationSeconds : 0) + returnToDungeonDelayMs / 1000d;
            logicalLoop.AfterFrames(LogicalFrameClock.DurationFrames(seconds), () => {
                if (ReferenceEquals(manager, owner) && manager.CurrentPhase == GamePhase.Dungeon) manager.ResumeAfterBattle();
            });
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
            bool useBattleGrid = battleFieldReady;
            // バトルセッション終了（購読解除）
            battleSession?.Detach();

            transitionCts?.Cancel();
            transitionCts?.Dispose();
            transitionCts = new CancellationTokenSource();
            var ct = transitionCts.Token;

            try
            {
                await TransitionToDungeonAsync(ct);

                await GameplayPause.WaitAsync(ct);
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
                        await GameplayPause.WaitAsync(ct);
                        pendingCardChoices = null;
                        logicalLoop.Schedule(() => { manager.SelectCardReward(selectedCard); ScheduleExplorationResume(); });
                        while (manager.CurrentPhase == GamePhase.CardReward) await GameplayPause.NextFrame(ct);
                    }
                    else
                    {
                        // CardSelectionView が未アサインの場合はスキップしてダンジョン復帰
                        logger.LogWarning("[Controller] CardSelectionView が未設定のためカード報酬をスキップします");
                        await GameplayPause.WaitAsync(ct);
                        pendingCardChoices = null;
                        logicalLoop.Schedule(() => { manager.SelectCardReward(null); ScheduleExplorationResume(); });
                        while (manager.CurrentPhase == GamePhase.CardReward) await GameplayPause.NextFrame(ct);
                    }
                }

                // 定位置（グリッド列1）へ復帰ジャンプ（ダッシュ宙返りと同じ放物線）
                if (contactAnimator != null
                    && gameFieldView?.PlayerTransform != null)
                {
                    // 初撃全滅ではグリッドが未初期化。探索座標へ戻し画面外への移動を防ぐ。
                    float targetX = useBattleGrid && gameFieldView.BattleGrid != null
                        ? gameFieldView.BattleGrid.GridIndexToWorldX(1)
                        : gameFieldView.LogicalToWorld(manager.Dungeon.PlayerPosition);
                    float targetY = useBattleGrid ? gameFieldView.PlayerTransform.position.y : gameFieldView.LogicalToWorld(manager.Dungeon.PlayerHeight);
                    var targetPos = new UnityEngine.Vector3(targetX, targetY, 0f);
                    await contactAnimator.PlayReturnJumpAsync(
                        gameFieldView.PlayerTransform, targetPos, ct, gameFieldView.CellSize);
                    gameFieldView.SetPlayerPositionUpdateEnabled(true);
                }

                await GameplayPause.WaitAsync(ct);
                // ジャンプ完了後にカメラ追従を再開（バトル中にカメラはすでに列1+オフセット位置にあるため、スナップ不要）
                gameFieldView?.SetCameraFollowEnabled(true);

                // インターバル後に移動を再開（遷移アニメーション完了を保証）
                await GameplayPause.Delay(returnToDungeonDelayMs, cancellationToken: ct);
                await GameplayPause.WaitAsync(ct);
                // モデル再開はScheduleExplorationResumeの論理期限で処理済み。
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

        private DungeonCheckpointSession memoryCheckpoints;
        public bool RetryMemoryCheckpoint()
        {
            // 停止中の待機と同期保存の所要時間を、成功後の最初のUpdateへ持ち込まない。
            // 既に獲得済みの論理フレームは消さず、観測点だけを区切る。
            logicalLoop.Observe(manager != null && !IsUserPaused && !awaitingCheckpointChoice &&
                !manager.Dungeon.IsCheckpointBlocked && Time.timeScale > 0);
            try
            {
                bool saved = manager != null && manager.RetryCheckpoint();
                if (saved && manager.CurrentPhase == GamePhase.Dungeon) manager.ResumeAfterBattle();
                return saved;
            }
            finally
            {
                logicalLoop.Observe(false);
                logicalLoop.ReleaseInput();
            }
        }
        // 候補と保存形式を検証した後でのみ旧モデルを破棄する。
        public bool TryRestoreMemoryCheckpoint(out string error)
        {
            error = null;
            if (manager == null || memoryCheckpoints == null || manager.Dungeon.IsCheckpointBlocked)
            { error = "保存が未完了です"; return false; }
            var factory = new SimpleCardFactory();
            var candidate = memoryCheckpoints.Store is FileCheckpointStore file
                ? new DungeonCheckpointSession(memoryCheckpoints.Definitions, new FileCheckpointStore(memoryCheckpoints.Definitions, file.DirectoryPath))
                : memoryCheckpoints;
            if (!DungeonBattleManager.TryRestoreCheckpoint(candidate, randomProvider, logger,
                out var restored, out error, new ExceedSystem(factory), new TuningSystem(),
                new FusionSystem(new SimpleFusionRecipeProvider(), factory))) return false;
            if (!manager.SuspendForCheckpoint()) { error = "現在の攻略は再開できません"; return false; }
            if (IsUserPaused) SetUserPaused(false);
            transitionCts?.Cancel(); battleSession?.Detach();
            manager.OnBattleStarted -= OnBattleStarted; manager.OnBattleEnded -= OnBattleEnded;
            manager.OnCardRewardPresented -= OnCardRewardPresented; manager.OnGameOver -= OnGameOver;
            manager.OnDungeonClearCompleted -= OnDungeonCleared;
            pendingCardChoices = null; cardSelectionView?.Hide();
            gameFieldView?.OnBattleEnded(); battleFieldReady = false;
            battleView?.gameObject.SetActive(false);
            logicalLoop.StartNewTimeline();
            manager = restored;
            memoryCheckpoints = candidate; awaitingCheckpointChoice = false;
            dashStartPosition = -1f; dashCooldownRemaining = 0;
            ShowCheckpointStatus();
            manager.OnBattleStarted += OnBattleStarted; manager.OnBattleEnded += OnBattleEnded;
            manager.OnCardRewardPresented += OnCardRewardPresented; manager.OnGameOver += OnGameOver;
            manager.OnDungeonClearCompleted += OnDungeonCleared;
            gameFieldView?.Initialize(manager.Dungeon);
            manager.ActivateCheckpoint();
            if (manager.CurrentPhase == GamePhase.Dungeon) gameFieldView?.ShowHUDAsync(0).Forget();
            return true;
        }

        private void OnDestroy()
        {
            logicalLoop.Dispose();
            if (IsUserPaused) SetUserPaused(false);
            if (pauseCanvas != null) Destroy(pauseCanvas);
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
