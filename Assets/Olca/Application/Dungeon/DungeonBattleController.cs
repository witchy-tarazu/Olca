using System.Linq;
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
        [Tooltip("正の強靭度削りを受けなくなってから蓄積をリセットする論理フレーム数。仮値180=3秒。新規出撃に適用する。")]
        [SerializeField, Min(1)] private int playerPoiseRecoveryFrames = 180;
        [SerializeField] private float dashDistanceCells = 3f;  // ダッシュで進むマス数
        [SerializeField] private float dashCooldown = 0.1f;   // ダッシュのクールタイム（秒）

        [Header("戦闘の暫定調整値")]
        [Tooltip("敵味方共通ののけぞり論理フレーム数。仮値18=0.3秒。新規出撃用。")]
        [SerializeField, Min(1)] private int battleStaggerFrames = 18;
        [Tooltip("既定AI攻撃・基本攻撃・マグマの被弾後無敵。仮値600ms。共通のけぞり時間より長く設定する。カードの個別設定は変更しない。")]
        [SerializeField, Min(1)] private int commonHitInvincibilityMs = 600;
        [SerializeField, Min(0)] private int swordPoiseDamage = 15;
        [SerializeField, Min(0)] private int bombPoiseDamage = 20;
        [SerializeField, Min(0)] private int fireSwordPoiseDamage = 30;
        [SerializeField, Min(1)] private int normalEnemyPoise = 30;
        [SerializeField, Min(1)] private int bossEnemyPoise = 60;
        [Tooltip("既定マップの敵の自然回復待ち。仮値180=3秒。外部マップ定義は上書きしない。")]
        [SerializeField, Min(1)] private int enemyPoiseRecoveryFrames = 180;

        [Header("クリア成長・超過変換の暫定値")]
        [SerializeField, Min(1)] private int clearExperience = 100;
        [SerializeField, Min(1)] private int experiencePerLevel = 100;
        [SerializeField, Min(1)] private int maximumPlayerLevel = 99;
        [SerializeField, Min(0)] private int hpGrowthPerLevel = 5;
        [SerializeField, Min(0)] private int attackGrowthPerLevel = 1;
        [SerializeField, Min(1)] private int standardOverflowTokens = 1;
        [SerializeField, Min(1)] private int limitedOverflowTokens = 2;
        [SerializeField, Min(1)] private int ultimateOverflowTokens = 3;

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

        [Header("基本攻撃の暫定調整（Planning/暫定実装一覧）")]
        [SerializeField, Min(1)] private int basicAttackShortPressFrames = 30;
        [SerializeField, Min(1)] private int basicAttackIntervalFrames = 12;
        private readonly BasicAttackInputState basicAttackInput = new BasicAttackInputState();
        private BasicAttackHud basicAttackHud;
        private bool queuedBasicAttackTap;
        private bool BasicAttackUiAvailable => manager?.CurrentPhase == GamePhase.Battle && !IsUserPaused && !GameplayPause.InputBlocked &&
            !clearReturnVisible && !collectionOpen && manager.CurrentBattle.CanPlayerBasicAttack && basicAttackInput.CooldownFrames == 0;
        private void QueueBasicAttackFromUi()
        {
            if (!BasicAttackUiAvailable) return;
            logicalLoop.QueueTimestampedCommand(inputProvider.PointerClickTimestamp, () => queuedBasicAttackTap = true);
        }
        private void RefreshBasicAttackHud()
        {
            if (basicAttackHud == null) return;
            bool visible = manager?.CurrentPhase == GamePhase.Battle && !clearReturnVisible && !collectionOpen &&
                (keyboardSettingsView == null || !keyboardSettingsView.gameObject.activeSelf);
            basicAttackHud.gameObject.SetActive(visible); if (!visible) return;
            var battle = manager.CurrentBattle;
            string state = IsUserPaused ? "ポーズ中" : battle.CustomScreen.IsOpen || battle.IsPaused ? "カスタム中" :
                battle.Player.IsStaggered ? "のけぞり中" : battle.Player.IsActionPrevented ? "状態異常中" : battle.PlayerMovement.IsNormalMoving ? "移動中" :
                battle.CardUsage.InFlightCard != null ? "カード使用中" : basicAttackInput.CooldownFrames > 0 ? "再使用待ち" :
                GameplayPause.InputBlocked ? "キーを離してください" : "使用できます";
            basicAttackHud.Render(GameplayKeyboardProfile.Bindings[("keyboard", "battle", "AttackPrimary")], state, BasicAttackUiAvailable);
        }
        private bool battleFieldReady;
        private GameObject pauseCanvas;
        private GameObject pauseOverlay;
        private GameplayKeyboardSettingsView keyboardSettingsView;
        private float timeScaleBeforePause;
        [SerializeField, HideInInspector] private string checkpointDirectoryOverride;
        private bool awaitingCheckpointChoice;
        private DungeonConfig configuredDungeon;
        private Deck configuredDepartureDeck;
        private FloorSequenceDefinitions floorDefinitions;
        private FloorSequenceSession floorSession;
        private FloorSequenceFileStore floorStore;
        private bool floorSwapPending;
        public int CurrentFloorIndex { get; private set; }
        public int FloorCount => floorDefinitions?.FloorCount ?? 1;

        /// <summary>外部で確定した階層列だけを使用する。既定シーンは従来の単階層経路。</summary>
        public void ConfigureFloorSequence(FloorSequenceDefinitions definitions)
        {
            if (manager != null) throw new System.InvalidOperationException("起動後は階層列を変更できません");
            floorDefinitions = definitions ?? throw new System.ArgumentNullException(nameof(definitions));
        }


        /// <summary>新規出撃前の編成入力。失敗時は既存設定・保存ファイル・所持枚数を変更しない。</summary>
        public bool TryConfigureDepartureDeck(IReadOnlyList<Card> cards,
            IReadOnlyDictionary<(string Id, ExceedLevel Level), int> ownedCounts, out string error)
        {
            if (manager != null) { error = "攻略開始後はデッキを切り替えられません"; return false; }
            if (!DepartureDeckRules.TryCreate(cards, ownedCounts, out var candidate, out error)) return false;
            configuredDepartureDeck = candidate;
            return true;
        }

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
        private string gameplaySettingsDirectoryOverride;
        private KeyboardBindingFileStore keyboardSettings;
        public string GameplayKeyboardSettingsError { get; private set; }
        public void ConfigureGameplaySettingsDirectory(string directory)
        {
            if (inputProvider != null) throw new System.InvalidOperationException("起動後は設定保存先を変更できません");
            gameplaySettingsDirectoryOverride = System.IO.Path.GetFullPath(directory);
        }
        public Olca.Core.Domain.InputBindingProfile GameplayKeyboardProfile => inputProvider?.KeyboardProfile;
        public bool TryApplyGameplayKeyboardProfile(Olca.Core.Domain.InputBindingProfile profile, out string error)
        {
            error = null;
            if (!IsUserPaused || inputProvider == null) { error = "ユーザーポーズ中のみ適用できます"; return false; }
            if (!UnityInputProvider.ValidateKeyboardProfile(profile, out error)) return false;
            if (keyboardSettings == null || !keyboardSettings.TrySave(profile, out error)) { GameplayKeyboardSettingsError = error; return false; }
            if (!inputProvider.TryApplyKeyboardProfile(profile, out error)) return false;
            GameplayKeyboardSettingsError = null;
            gameFieldView?.RefreshGameplayInstructions(inputProvider.KeyboardProfile, manager.CurrentPhase);
            // 論理時計は継続し、旧割当の入力だけを捨てる。再開時は既存の全キー解放待ちを通る。
            logicalLoop.ReleaseInput();
            inputProvider.QueueSnapshot();
            return true;
        }

        public void SetUserPaused(bool paused)
        {
            if (manager == null || manager.IsUserPaused == paused) return;
            logicalLoop.Observe(!manager.IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked && Time.timeScale > 0);
            logicalLoop.ReleaseInput();
            manager.SetUserPaused(paused);
            GameplayPause.SetPaused(paused);
            if (paused) { timeScaleBeforePause = Time.timeScale; Time.timeScale = 0f; }
            else { Time.timeScale = timeScaleBeforePause; if (keyboardSettingsView != null) keyboardSettingsView.DiscardAndClose(); }
            // Unityでは破棄済み参照もCLR上は非nullになるため、生存判定を通す。
            if (pauseOverlay != null) pauseOverlay.SetActive(paused);
            inputProvider?.QueueSnapshot();
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
            MakeButton("KeyboardSettingsButton", "キー設定", pauseOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -200))
                .onClick.AddListener(() => keyboardSettingsView.Open(GameplayKeyboardProfile));
            MakeButton("OwnedCardsButton", "所持カード", pauseOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -300)).onClick.AddListener(OpenOwnedCards);
            var settingsPanel = new GameObject("KeyboardSettingsPanel", typeof(RectTransform));
            settingsPanel.transform.SetParent(pauseCanvas.transform, false);
            keyboardSettingsView = settingsPanel.AddComponent<GameplayKeyboardSettingsView>();
            keyboardSettingsView.Initialize(font, candidate => TryApplyGameplayKeyboardProfile(candidate, out var error) ? null : error);
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
            var returnPanel = new GameObject("ClearReturnPanel", typeof(RectTransform)); returnPanel.transform.SetParent(pauseCanvas.transform, false);
            clearReturnView = returnPanel.AddComponent<ClearReturnView>();
            clearReturnView.Initialize(font, ToggleCarryCard, ConfirmCarryCards, ConfirmClearReturn, RetryClearReturn, StartNextDeparture, CloseOwnedCards, OpenOwnedDeparture);
            var attackPanel = new GameObject("BasicAttackHud", typeof(RectTransform)); attackPanel.transform.SetParent(pauseCanvas.transform, false);
            basicAttackHud = attackPanel.AddComponent<BasicAttackHud>(); basicAttackHud.Initialize(font, QueueBasicAttackFromUi, () => basicAttackShortPressFrames);
            basicAttackHud.gameObject.SetActive(false);
        }
        private ClearReturnFileStore clearReturnStore;
        private ClearReturnSession clearReturnSession;
        private ClearReturnView clearReturnView;
        private bool clearReturnVisible, progressStartupBlocked, collectionOpen, departureWriting;
        private CheckpointFileIo nextDepartureIo;
        private CheckpointDefinitions nextDepartureDefinitions;
        public string ActiveCheckpointDirectory => clearReturnSession?.Snapshot?.NextDeparture == null ? CheckpointDirectory :
            System.IO.Path.Combine(CheckpointDirectory, "runs", clearReturnSession.Snapshot.NextDeparture.Start.RunId);
        private void OpenOwnedCards()
        {
            if (!IsUserPaused || HasIncompleteReturn) return;
            collectionOpen = true; pauseOverlay.SetActive(false); clearReturnView.Render(clearReturnSession.Snapshot, clearReturnSession.Snapshot?.Flow, false, null, true);
        }
        private void CloseOwnedCards() { collectionOpen = false; clearReturnView.gameObject.SetActive(false); pauseOverlay.SetActive(IsUserPaused); }

        private bool ownedDepartureEditing;
        private readonly List<Card> ownedDepartureDraft = new List<Card>();
        private Card[] ownedDepartureOptions;
        private string ownedDepartureError;
        private bool CanEditOwnedDeparture => clearReturnVisible && !collectionOpen && !departureWriting && !progressStartupBlocked &&
            clearReturnSession.Ready && !clearReturnSession.HasPendingSave && clearReturnSession.Snapshot.Flow?.Stage == ClearReturnStage.Returned &&
            clearReturnSession.Snapshot.NextDeparture?.ParentRunId != clearReturnSession.Snapshot.Flow.RunId;
        private void OpenOwnedDeparture()
        {
            if (!CanEditOwnedDeparture) return;
            ownedDepartureOptions = clearReturnSession.Snapshot.Owned.Select(o => {
                Card card; bool resolved = floorDefinitions != null ? floorDefinitions.TryResolveOwnedCard(o.Id, o.Level, o.Rarity, o.Name, out card)
                    : nextDepartureDefinitions.TryResolveOwnedCard(o.Id, o.Level, o.Rarity, o.Name, out card);
                return resolved ? card : null;
            }).ToArray();
            ownedDepartureDraft.Clear(); ownedDepartureEditing = true; ownedDepartureError = null; ShowOwnedDeparture();
        }
        private bool TryOwnedDepartureDeck(out Deck deck, out string error)
            => DepartureDeckRules.TryCreate(ownedDepartureDraft, clearReturnSession.Snapshot.Owned.ToDictionary(o => (o.Id, o.Level), o => o.Count), out deck, out error);
        private void ShowOwnedDeparture(bool confirmDiscard = false)
        {
            TryOwnedDepartureDeck(out _, out var validation);
            clearReturnView.RenderOwnedDeparture(clearReturnSession.Snapshot, ownedDepartureOptions, ownedDepartureDraft,
                ownedDepartureError ?? validation, validation == null, ChangeOwnedDeparture, ConfirmOwnedDeparture,
                CloseOwnedDeparture, () => ShowOwnedDeparture(), confirmDiscard);
        }
        private void ChangeOwnedDeparture(int index, int delta)
        {
            if (!ownedDepartureEditing || !CanEditOwnedDeparture || index < 0 || index >= ownedDepartureOptions.Length) return;
            var card = ownedDepartureOptions[index]; if (card == null) return;
            if (delta > 0) {
                int count = ownedDepartureDraft.Count(c => c.Id == card.Id && c.ExceedLevel == card.ExceedLevel);
                if (count >= clearReturnSession.Snapshot.Owned[index].Count || ownedDepartureDraft.Count >= 20) return;
                ownedDepartureDraft.Add(card.Clone());
            } else {
                int found = ownedDepartureDraft.FindLastIndex(c => c.Id == card.Id && c.ExceedLevel == card.ExceedLevel);
                if (found >= 0) ownedDepartureDraft.RemoveAt(found);
            }
            ownedDepartureError = null; ShowOwnedDeparture();
        }
        private void CloseOwnedDeparture(bool discard)
        {
            if (!ownedDepartureEditing || !CanEditOwnedDeparture) return;
            if (!discard && ownedDepartureDraft.Count > 0) { ShowOwnedDeparture(true); return; }
            ownedDepartureDraft.Clear(); ownedDepartureEditing = false; ShowClearReturn();
        }
        private void ConfirmOwnedDeparture()
        {
            if (!ownedDepartureEditing || !CanEditOwnedDeparture) return;
            if (!TryOwnedDepartureDeck(out var deck, out ownedDepartureError)) { ShowOwnedDeparture(); return; }
            ownedDepartureEditing = false; StartNextDepartureWithDeck(deck);
        }

        private string clearReturnError, progressDirectoryOverride;
        public ClearReturnProfile PersistentProgress => clearReturnSession?.Snapshot;
        public PlayerCharacter ReturnedPlayer => clearReturnSession?.ReturnedPlayer;
        public string ProgressDirectory => progressDirectoryOverride ?? (string.IsNullOrEmpty(checkpointDirectoryOverride)
            ? System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "Olca", "progress") : System.IO.Path.Combine(CheckpointDirectory, "progress"));
        public void ConfigureProgressDirectory(string directory)
        {
            if (clearReturnSession != null) throw new System.InvalidOperationException("起動後は進行保存先を変更できません");
            progressDirectoryOverride = System.IO.Path.GetFullPath(directory);
        }
        private ClearReturnSettings ReturnSettings() => new ClearReturnSettings {
            FirstClearExperience = clearExperience, ExperiencePerLevel = experiencePerLevel, MaximumLevel = maximumPlayerLevel,
            BaseMaxHp = playerMaxHp, BaseAttack = playerAttackPower, HpPerLevel = hpGrowthPerLevel, AttackPerLevel = attackGrowthPerLevel,
            StandardTokens = standardOverflowTokens, LimitedTokens = limitedOverflowTokens, UltimateTokens = ultimateOverflowTokens };
        private bool HasIncompleteReturn => clearReturnSession?.Snapshot?.Flow != null && clearReturnSession.Snapshot.Flow.Stage != ClearReturnStage.Returned;
        private bool TryReadCompletedRun(out RestoredCheckpoint restored, out string dungeonKey, out string error)
        {
            restored = null; error = null; dungeonKey = null;
            if (floorDefinitions != null) {
                var data = floorStore.Read();
                if (data == null) { error = floorStore.ReadError; return false; }
                if (data.FloorIndex != floorDefinitions.FloorCount - 1 || data.State?.Boundary != CheckpointBoundary.Terminal || data.State.Player == null || data.State.Player.Hp <= 0) return false;
                if (!floorDefinitions.TryRestore(data, out var route, out error)) return false;
                restored = route.State; dungeonKey = floorDefinitions.RouteId; return true;
            }
            var dto = memoryCheckpoints?.Store.Read();
            if (dto?.Boundary != CheckpointBoundary.Terminal || dto.Player == null || dto.Player.Hp <= 0) return false;
            if (!CheckpointCodec.TryRestore(dto, memoryCheckpoints.Definitions, out restored, out error)) return false;
            dungeonKey = dto.DungeonId; return true;
        }
        private void ShowClearReturn()
        {
            clearReturnVisible = true; transitionCts?.Cancel();
            checkpointOverlay?.SetActive(false); pauseOverlay?.SetActive(false); keyboardSettingsView?.DiscardAndClose();
            clearReturnView.Render(clearReturnSession.Snapshot, clearReturnSession.DisplayFlow,
                !clearReturnSession.Ready || clearReturnSession.HasPendingSave || progressStartupBlocked || clearReturnError != null,
                clearReturnError ?? clearReturnSession.Error);
        }
        private void ResumeClearReturnIfNeeded()
        {
            if (!clearReturnSession.Ready) { ShowClearReturn(); return; }
            if (clearReturnSession.Snapshot.NextDeparture != null && !awaitingCheckpointChoice && !HasIncompleteReturn) {
                if (!ActivateNextDeparture(out var startError)) { clearReturnError = startError; ShowClearReturn(); }
                return;
            }
            if (HasIncompleteReturn) { ShowClearReturn(); return; }
            if (awaitingCheckpointChoice && TryReadCompletedRun(out var source, out var key, out var error)) {
                clearReturnSession.Begin(source, key, ReturnSettings()); ShowClearReturn();
            }
        }
        private void BeginClearReturn()
        {
            if (!TryReadCompletedRun(out var source, out var key, out var error)) {
                clearReturnError = error ?? "クリア済みの保存を確認できません。既存データは変更していません。";
            } else {
                clearReturnError = null; clearReturnSession.Begin(source, key, ReturnSettings());
            }
            ShowClearReturn();
        }
        private void ToggleCarryCard(int index) { clearReturnSession.Toggle(index); ShowClearReturn(); }
        private void ConfirmCarryCards() { clearReturnSession.ConfirmSelection(); ShowClearReturn(); }
        private void ConfirmClearReturn() { clearReturnSession.ConfirmReturn(); ShowClearReturn(); }
        private void RetryClearReturn()
        {
            if (!clearReturnSession.Retry()) { ShowClearReturn(); return; }
            if (!progressStartupBlocked && clearReturnSession.Snapshot.NextDeparture?.ParentRunId == clearReturnSession.Snapshot.Flow?.RunId &&
                clearReturnSession.Snapshot.Flow?.Stage == ClearReturnStage.Returned) {
                if (!ActivateNextDeparture(out clearReturnError)) ShowClearReturn();
                return;
            }
            if (progressStartupBlocked && clearReturnSession.Ready) clearReturnError = "進行データを読み込めました。ゲームを再起動して再開してください。";
            else if (clearReturnError != null && clearReturnSession.Ready && !clearReturnSession.HasPendingSave) { BeginClearReturn(); return; }
            ShowClearReturn();
        }

        private string DepartureDefinitionKey(CheckpointDto start) => floorDefinitions?.RouteHash ?? nextDepartureDefinitions.MapHash;
        private void StartNextDeparture() => StartNextDepartureWithDeck(null);
        private void StartNextDepartureWithDeck(Deck ownedDeck)
        {
            if (departureWriting || collectionOpen || !clearReturnVisible || progressStartupBlocked || !clearReturnSession.Ready ||
                clearReturnSession.Snapshot.Flow?.Stage != ClearReturnStage.Returned) return;
            departureWriting = true;
            try {
                if (clearReturnSession.HasPendingSave) { ShowClearReturn(); return; }
                if (clearReturnSession.Snapshot.NextDeparture?.ParentRunId != clearReturnSession.Snapshot.Flow.RunId) {
                    var map = floorDefinitions?.CreateInitialMap(0) ?? nextDepartureDefinitions.CreateInitialMap();
                    if (floorDefinitions == null) map.ValidateFinalFloorForDeparture();
                    var deck = ownedDeck ?? CreateInitialDeck();
                    // 同じ固定カタログで捕捉し、編成枚数だけで定義hashを変更しない。
                    var frozen = floorDefinitions?.CreateDepartureSession() ?? new DungeonCheckpointSession(nextDepartureDefinitions, new MemoryCheckpointStore(nextDepartureDefinitions));
                    var level = clearReturnSession.Snapshot.Level; var settings = ReturnSettings();
                    var player = new PlayerCharacter(settings.MaxHp(level), attackPower: settings.Attack(level),
                        staggerDurationFrames: battleStaggerFrames, poiseRecoveryDelayFrames: playerPoiseRecoveryFrames);
                    new DungeonBattleManager(map, player, deck, randomProvider, logger, checkpoints: frozen);
                    var start = frozen.Store.Read();
                    if (!ValidateDeparture(start, out clearReturnError) || !clearReturnSession.PrepareNextDeparture(start, DepartureDefinitionKey(start))) {
                        ShowClearReturn(); return;
                    }
                }
                if (!ActivateNextDeparture(out clearReturnError)) ShowClearReturn();
            } catch (System.ArgumentException e) { clearReturnError = e.Message; ShowClearReturn(); }
              catch (System.OverflowException e) { clearReturnError = e.Message; ShowClearReturn(); }
            finally { departureWriting = false; }
        }
        private bool ValidateDeparture(CheckpointDto start, out string error)
        {
            if (floorDefinitions != null) return floorDefinitions.TryRestore(new FloorSequenceCheckpointDto {
                RouteId = floorDefinitions.RouteId, Revision = floorDefinitions.Revision, RouteHash = floorDefinitions.RouteHash,
                FloorIndex = 0, State = start }, out _, out error);
            return CheckpointCodec.TryRestore(start, nextDepartureDefinitions, out _, out error);
        }
        private bool ActivateNextDeparture(out string error)
        {
            error = null; var plan = clearReturnSession.Snapshot.NextDeparture;
            if (plan == null || plan.DefinitionKey != DepartureDefinitionKey(plan.Start) || !ValidateDeparture(plan.Start, out error)) {
                error = error ?? "次回出撃の定義が一致しません。既存保存は変更していません。"; return false;
            }
            DungeonBattleManager next; DungeonCheckpointSession session; FloorSequenceSession route = null; FloorSequenceFileStore routeStore = null;
            if (floorDefinitions != null) {
                routeStore = new FloorSequenceFileStore(floorDefinitions, ActiveCheckpointDirectory, nextDepartureIo);
                if (!routeStore.HasExistingData && !routeStore.TryWrite(new FloorSequenceCheckpointDto {
                    RouteId = floorDefinitions.RouteId, Revision = floorDefinitions.Revision, RouteHash = floorDefinitions.RouteHash,
                    FloorIndex = 0, State = plan.Start }, out error)) return false;
                route = new FloorSequenceSession(floorDefinitions, routeStore);
                if (!route.TryLoad(out error) || route.Current.State.Snapshot.RunId != plan.Start.RunId) { error = error ?? "攻略IDが一致しません"; return false; }
                if (route.Current.State.Snapshot.Boundary == CheckpointBoundary.Terminal) { error = "終了済み攻略を上書きしません"; return false; }
                next = CreateFloorManager(route, out session);
            } else {
                var defs = nextDepartureDefinitions;
                var store = new FileCheckpointStore(defs, ActiveCheckpointDirectory, nextDepartureIo);
                if (!store.HasExistingData && !store.TryWrite(plan.Start, out error)) return false;
                var saved = store.Read(); if (saved == null || saved.RunId != plan.Start.RunId) { error = store.ReadError ?? "攻略IDが一致しません"; return false; }
                session = new DungeonCheckpointSession(defs, store); var f = CreateCardFactory();
                if (!DungeonBattleManager.TryRestoreCheckpoint(session, randomProvider, logger, out next, out error,
                    new ExceedSystem(f), new TuningSystem(), new FusionSystem(new SimpleFusionRecipeProvider(), f), f)) return false;
            }
            // 新攻略の保存と復元が成功してから、旧帰還画面・モデルを交換する。
            if (manager.CurrentPhase != GamePhase.DungeonCleared && manager.CurrentPhase != GamePhase.GameOver) manager.SuspendForCheckpoint();
            floorSession = route; floorStore = routeStore; CurrentFloorIndex = route?.Current.FloorIndex ?? 0; floorSwapPending = false;
            clearReturnVisible = false; clearReturnError = null; clearReturnView.gameObject.SetActive(false); checkpointOverlay.SetActive(false);
            ReplaceManager(next, session); return true;
        }

        private CancellationTokenSource transitionCts;
        private IReadOnlyList<Card> pendingCardChoices; // カード報酬フロー用

        // 未設定だった旧既定マップも、同じ配置で検証してから再開する。
        private DungeonConfig CreateDefaultDungeon(bool legacy = false)
            => DungeonConfig.CreateTestDungeon(dungeonCellCount, enemySpacing, firstEnemyCell,
                enemyPoise: legacy ? null : new EnemyPoiseDefinition(normalEnemyPoise, battleStaggerFrames, enemyPoiseRecoveryFrames),
                bossPoise: legacy ? null : new EnemyPoiseDefinition(bossEnemyPoise, battleStaggerFrames, enemyPoiseRecoveryFrames));

        private void Start()
        {
            if (commonHitInvincibilityMs <= 0 || (long)commonHitInvincibilityMs * 60 <= (long)battleStaggerFrames * 1000)
                throw new System.ArgumentOutOfRangeException(nameof(commonHitInvincibilityMs), "被弾無敵は共通のけぞり時間より長く設定してください");
            ReturnSettings().Validate();
            clearReturnStore = clearReturnStore ?? new ClearReturnFileStore(ProgressDirectory);
            clearReturnSession = new ClearReturnSession(clearReturnStore);
            progressStartupBlocked = !clearReturnSession.Ready;
            int persistentLevel = clearReturnSession.Snapshot?.Level ?? 1;
            randomProvider = simulationSeed.HasValue ? new UnityRandomProvider(simulationSeed.Value) : new UnityRandomProvider();
            logger = new UnityLogger();
            inputProvider = new UnityInputProvider();
            keyboardSettings = new KeyboardBindingFileStore(gameplaySettingsDirectoryOverride ?? System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","settings"));
            if (keyboardSettings.TryRead(out var profile, out var settingsError)) inputProvider.TryRestoreKeyboardProfile(profile, out settingsError);
            GameplayKeyboardSettingsError = settingsError;
            if (settingsError != null) logger.LogWarning(settingsError);
            inputProvider.StartEventCapture();
            customScreenView?.ConfigureLogicalConfirmation(logicalLoop.QueueTimestampedCommand, () => inputProvider.PointerClickTimestamp);

            var dungeonConfig = floorDefinitions?.CreateInitialMap(0) ?? configuredDungeon ?? CreateDefaultDungeon();
            var playerCharacter = new PlayerCharacter(
                maxHp: ReturnSettings().MaxHp(persistentLevel),
                initialPosition: new Vector2Int(0, 0),
                attackPower: ReturnSettings().Attack(persistentLevel),
                staggerDurationFrames: battleStaggerFrames,
                poiseRecoveryDelayFrames: playerPoiseRecoveryFrames);

            var cardFactory = CreateCardFactory();
            var recipeProvider = new SimpleFusionRecipeProvider();
            var exceedSystem = new ExceedSystem(cardFactory);
            var tuningSystem = new TuningSystem();
            var fusionSystem = new FusionSystem(recipeProvider, cardFactory);

            var initialDeck = CreateInitialDeck();
            if (floorDefinitions != null)
            {
                floorStore = new FloorSequenceFileStore(floorDefinitions, ActiveCheckpointDirectory);
                floorSession = new FloorSequenceSession(floorDefinitions, floorStore);
                memoryCheckpoints = floorDefinitions.CreateCheckpointSession(0, floorSession);
                awaitingCheckpointChoice = floorStore.HasExistingData;
            }
            else
            {
                var definitions = DungeonCheckpointSession.CreateDefault(dungeonConfig, initialDeck).Definitions;
                nextDepartureDefinitions = definitions; // 旧攻略の再開定義と、新規出撃の現在定義を分ける。
                var store = new FileCheckpointStore(definitions, ActiveCheckpointDirectory);
                if (store.HasExistingData && store.Read() == null)
                {
                    // マップ/カードそれぞれの既知の旧定義だけを完全検証する。保存は書き換えない。
                    var maps = configuredDungeon == null
                        ? new[] { dungeonConfig, CreateDefaultDungeon(legacy: true) } : new[] { dungeonConfig };
                    var decks = new[] { initialDeck, CreateDepartureDeck(legacyPoise: true) };
                    bool matched = false;
                    foreach (var map in maps)
                    {
                        foreach (var deck in decks)
                        {
                            var candidate = DungeonCheckpointSession.CreateDefault(map, deck).Definitions;
                            var candidateStore = new FileCheckpointStore(candidate, ActiveCheckpointDirectory);
                            if (candidateStore.Read() == null) continue;
                            dungeonConfig = map; initialDeck = deck; definitions = candidate; store = candidateStore;
                            matched = true; break;
                        }
                        if (matched) break;
                    }
                }
                memoryCheckpoints = new DungeonCheckpointSession(definitions, store);
                awaitingCheckpointChoice = store.HasExistingData;
                // 旧保存には新規出撃条件を遡及適用しない。
                if (!awaitingCheckpointChoice) dungeonConfig.ValidateFinalFloorForDeparture();
            }
            manager = new DungeonBattleManager(
                dungeonConfig, playerCharacter, initialDeck, randomProvider, logger,
                exceedSystem: exceedSystem,
                tuningSystem: tuningSystem,
                fusionSystem: fusionSystem, checkpoints: awaitingCheckpointChoice || progressStartupBlocked || HasIncompleteReturn || clearReturnSession.Snapshot.NextDeparture != null ? null : memoryCheckpoints, cardDatabase: cardFactory,
                hasNextFloor: !awaitingCheckpointChoice && !progressStartupBlocked && !HasIncompleteReturn && clearReturnSession.Snapshot.NextDeparture == null && floorDefinitions != null && floorDefinitions.FloorCount > 1);

            // イベント購読
            manager.OnBattleStarted += OnBattleStarted;
            manager.OnBattleEnded += OnBattleEnded;
            manager.OnCardRewardPresented += OnCardRewardPresented;
            manager.OnGameOver += OnGameOver;
            manager.OnDungeonClearCompleted += OnDungeonCleared;
            manager.OnFloorCompleted += OnFloorCompleted;

            // GameFieldView 初期化（フィールド・敵スプライトを世界空間に配置）
            gameFieldView?.Initialize(manager.Dungeon);

            // バトルCardUI は最初から非表示
            battleView?.gameObject.SetActive(false);

            // バトルセッション共通操作をBattleSessionControllerに委譲
            if (battleView != null && customScreenView != null)
                battleSession = new BattleSessionController(battleView, customScreenView, randomProvider, logger,
                    (frames, action) => logicalLoop.AfterFrames(frames, action), battleView.CloseDurationFrames);

            CreatePauseControls();
            ResumeClearReturnIfNeeded();
            logicalLoop.Observe(false);
            logger.LogInfo("[DungeonBattleController] 初期化完了");
        }

        // EventSystemとViewのUpdateで受けたクリックを、同じ描画フレームの論理処理へ渡す。
        private void LateUpdate()
        {
            if (manager == null) return;
            bool active = !clearReturnVisible && !IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked && Time.timeScale > 0;
            logicalLoop.PumpTimestamped(active, inputProvider.CaptureTimestamped(), TickLogicalFrame, () => !clearReturnVisible && !IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked,
                () => manager.CurrentPhase == GamePhase.Dungeon || (manager.CurrentBattle != null && !manager.CurrentBattle.IsPaused));
            ApplyPendingFloor();
            if (manager.Dungeon.IsCheckpointBlocked) ShowCheckpointStatus();
            UpdateViews();
            RefreshBasicAttackHud();
        }

        private void TickLogicalFrame(long frame, int deltaTimeMs, LogicalInput input)
        {
            if (GameplayPause.InputBlocked) { input = new LogicalInput(); queuedBasicAttackTap = false; }
            if (queuedBasicAttackTap) { input = input.Copy(); input.BasicAttackButtonDown = input.BasicAttackButtonUp = true; input.BasicAttackButton = false; queuedBasicAttackTap = false; }
            void Process()
            {
                customScreenView?.HandleLogicalConfirmation(input);
                HandleInput(input, deltaTimeMs);
                manager.TickFrame(deltaTimeMs, input);
            }
            if (battleSession != null) battleSession.ProcessLogicalFrame(Process); else Process();
            ApplyPendingFloor();
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
                        // 空中などで開始を拒否された入力から距離・クールタイムを作らない。
                        if (manager.Dungeon.IsDashing) dashStartPosition = manager.Dungeon.PlayerPosition;
                    }

                    if (dashStartPosition >= 0f)
                    {
                        float traveled = manager.Dungeon.PlayerPosition - dashStartPosition;
                        if (traveled >= dashDistanceCells || (!manager.Dungeon.IsDashing && !manager.Dungeon.IsDashJumping))
                        {
                            manager.StopDash();
                            dashStartPosition = -1f;
                            dashCooldownRemaining = LogicalFrameClock.DurationFrames(dashCooldown);
                        }
                    }

                    if (dashCooldownRemaining > 0f && !manager.Dungeon.IsDashJumping)
                        dashCooldownRemaining--;
                    break;

                case GamePhase.Battle:
                    // AltキーでBattleSessionControllerに委譲してカスタム画面を開く
                    if (input.CustomScreenButtonDown)
                        battleSession?.TryOpenCustomScreen();

                    basicAttackInput.Tick(manager.CurrentBattle, input, basicAttackShortPressFrames, basicAttackIntervalFrames);
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
            gameFieldView?.RefreshGameplayInstructions(inputProvider.KeyboardProfile, manager.CurrentPhase);
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
                    dungeonLength: manager.Dungeon.Map.TotalCellCount,
                    isDashing: manager.Dungeon.IsDashing,
                    isDashCooldown: dashCooldownRemaining > 0f,
                    deckCount: manager.Dungeon.GetCurrentDeck().TotalCount,
                    obtainedCardCount: manager.Dungeon.GetObtainedCards().Count,
                    floorNumber: floorDefinitions != null ? CurrentFloorIndex + 1 : 0
                );
                dungeonHudView.Refresh(data);
            }
        }

        // ==================== バトル開始遷移 ====================

        private void OnBattleStarted(BattleSystem battle, ContactInfo contactInfo)
        {
            battle.ConfigureDefaultHitInvincibility(commonHitInvincibilityMs);
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
                        x => gameFieldView.BattleGrid != null ? gameFieldView.BattleGrid.SnapWorldXToCellCenter(x) : x,
                        gameFieldView.SetContactPose);
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
            var owner = manager;
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
                        logicalLoop.Schedule(() => { if (ReferenceEquals(manager, owner)) { owner.SelectCardReward(selectedCard); ScheduleExplorationResume(); } });
                        while (manager.CurrentPhase == GamePhase.CardReward) await GameplayPause.NextFrame(ct);
                    }
                    else
                    {
                        // CardSelectionView が未アサインの場合はスキップしてダンジョン復帰
                        logger.LogWarning("[Controller] CardSelectionView が未設定のためカード報酬をスキップします");
                        await GameplayPause.WaitAsync(ct);
                        pendingCardChoices = null;
                        logicalLoop.Schedule(() => { if (ReferenceEquals(manager, owner)) { owner.SelectCardReward(null); ScheduleExplorationResume(); } });
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
            BeginClearReturn();
        }

        // ==================== 初期デッキ ====================

        private SimpleCardFactory CreateCardFactory()
            => new SimpleCardFactory(new CardPoiseSettings(swordPoiseDamage, bombPoiseDamage, fireSwordPoiseDamage));

        private Deck CreateInitialDeck() => CreateDepartureDeck(legacyPoise: false);
        private Deck CreateDepartureDeck(bool legacyPoise)
        {
            if (configuredDepartureDeck != null)
            {
                var cards = new List<Card>();
                foreach (var card in configuredDepartureDeck.GetAllCards()) cards.Add(card.Clone());
                return new Deck(cards);
            }
            // 編成入力のない既存シーンは加工・戦闘確認用のfixtureを使用する。
            var f = legacyPoise ? new SimpleCardFactory(legacyPoise: true) : CreateCardFactory();
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
                ApplyPendingFloor();
                if (saved && manager.CurrentPhase == GamePhase.Dungeon) manager.ResumeAfterBattle();
                return saved;
            }
            finally
            {
                logicalLoop.Observe(false);
                logicalLoop.ReleaseInput();
                inputProvider?.QueueSnapshot();
            }
        }
        // 候補と保存形式を検証した後でのみ旧モデルを破棄する。
        public bool TryRestoreMemoryCheckpoint(out string error)
        {
            if (clearReturnVisible) { error = "クリア後の帰還処理を先に完了してください"; return false; }
            // 呼出し前の進行時間は保持し、検証・初期化に費やす同期時間だけを除外する。
            logicalLoop.Observe(manager != null && !IsUserPaused && !awaitingCheckpointChoice &&
                !manager.Dungeon.IsCheckpointBlocked && Time.timeScale > 0);
            try { return RestoreMemoryCheckpoint(out error); }
            finally
            {
                // 成功・拒否・例外のどの経路でも、完了後から再び経過時間を計測する。
                logicalLoop.Observe(false);
                logicalLoop.ReleaseInput();
                inputProvider?.QueueSnapshot();
            }
        }
        private void OnFloorCompleted() { floorSwapPending = true; }

        private DungeonBattleManager CreateFloorManager(FloorSequenceSession route, out DungeonCheckpointSession session)
        {
            var state = route.Current.State;
            session = floorDefinitions.CreateCheckpointSession(route.Current.FloorIndex, route);
            var factory = CreateCardFactory();
            return new DungeonBattleManager(state.Map, state.Player, state.Deck, randomProvider, logger,
                new ExceedSystem(factory), new TuningSystem(), new FusionSystem(new SimpleFusionRecipeProvider(), factory),
                checkpoints: session, restoration: state, cardDatabase: factory,
                hasNextFloor: route.Current.FloorIndex + 1 < floorDefinitions.FloorCount);
        }

        private void ApplyPendingFloor()
        {
            // 古い戦闘イベントの配信が終わってから一度だけ交換する。
            if (!floorSwapPending || floorSession == null || floorSession.Current.FloorIndex != CurrentFloorIndex + 1) return;
            var next = CreateFloorManager(floorSession, out var session);
            if (!manager.SuspendForCheckpoint()) return;
            floorSwapPending = false;
            CurrentFloorIndex = floorSession.Current.FloorIndex;
            ReplaceManager(next, session);
        }

        private bool RestoreMemoryCheckpoint(out string error)
        {
            error = null;
            if (manager == null || memoryCheckpoints == null || manager.Dungeon.IsCheckpointBlocked)
            { error = "保存が未完了です"; return false; }
            DungeonBattleManager restored;
            DungeonCheckpointSession candidate;
            FloorSequenceSession loadedRoute = null;
            if (floorDefinitions != null)
            {
                loadedRoute = new FloorSequenceSession(floorDefinitions, floorStore);
                if (!loadedRoute.TryLoad(out error)) return false;
                if (loadedRoute.Current.Snapshot.State.Boundary == CheckpointBoundary.Terminal)
                { error = "終了済みの攻略は再開できません"; return false; }
                restored = CreateFloorManager(loadedRoute, out candidate);
            }
            else
            {
                var factory = CreateCardFactory();
                candidate = memoryCheckpoints.Store is FileCheckpointStore file
                    ? new DungeonCheckpointSession(memoryCheckpoints.Definitions, new FileCheckpointStore(memoryCheckpoints.Definitions, file.DirectoryPath))
                    : memoryCheckpoints;
                if (!DungeonBattleManager.TryRestoreCheckpoint(candidate, randomProvider, logger,
                    out restored, out error, new ExceedSystem(factory), new TuningSystem(),
                    new FusionSystem(new SimpleFusionRecipeProvider(), factory), cardDatabase: factory)) return false;
            }
            if (!manager.SuspendForCheckpoint()) { error = "現在の攻略は再開できません"; return false; }
            if (loadedRoute != null) { floorSession = loadedRoute; CurrentFloorIndex = loadedRoute.Current.FloorIndex; }
            floorSwapPending = false;
            ReplaceManager(restored, candidate);
            return true;
        }

        private void ReplaceManager(DungeonBattleManager restored, DungeonCheckpointSession candidate)
        {
            if (IsUserPaused) SetUserPaused(false);
            transitionCts?.Cancel(); battleSession?.Detach();
            manager.OnBattleStarted -= OnBattleStarted; manager.OnBattleEnded -= OnBattleEnded;
            manager.OnCardRewardPresented -= OnCardRewardPresented; manager.OnGameOver -= OnGameOver;
            manager.OnDungeonClearCompleted -= OnDungeonCleared;
            manager.OnFloorCompleted -= OnFloorCompleted;
            pendingCardChoices = null; cardSelectionView?.Hide();
            gameFieldView?.OnBattleEnded(); battleFieldReady = false;
            battleView?.gameObject.SetActive(false);
            logicalLoop.StartNewTimeline(); basicAttackInput.Reset(); queuedBasicAttackTap = false;
            manager = restored;
            memoryCheckpoints = candidate; awaitingCheckpointChoice = false;
            dashStartPosition = -1f; dashCooldownRemaining = 0;
            ShowCheckpointStatus();
            manager.OnBattleStarted += OnBattleStarted; manager.OnBattleEnded += OnBattleEnded;
            manager.OnCardRewardPresented += OnCardRewardPresented; manager.OnGameOver += OnGameOver;
            manager.OnDungeonClearCompleted += OnDungeonCleared;
            manager.OnFloorCompleted += OnFloorCompleted;
            gameFieldView?.SetPlayerPositionUpdateEnabled(true);
            gameFieldView?.Initialize(manager.Dungeon);
            manager.ActivateCheckpoint();
            if (manager.CurrentPhase == GamePhase.Dungeon) gameFieldView?.ShowHUDAsync(0).Forget();
            RefreshDungeonHud();
        }

        private void OnDestroy()
        {
            inputProvider?.Dispose();
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
            manager.OnFloorCompleted -= OnFloorCompleted;
            }
        }
    }
}
