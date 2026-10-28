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
        [Tooltip("探索通常速度。旧10000から20%減速。ダッシュ速度/ジャンプ期間/戦闘移動時間は独立。") ]
        [SerializeField, Min(1)] private int explorationNormalSpeedMilliPerSecond = 8000;
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
        [SerializeField, Min(1)] private int basicAttackChargeFrames = 60;
        [SerializeField, Min(1)] private int basicAttackChargeMultiplier = 2;
        [SerializeField, Min(1)] private int basicAttackIntervalFrames = 12;
        private readonly Olca.Core.Logic.Battle.BasicAttackInputState basicAttackInput = new Olca.Core.Logic.Battle.BasicAttackInputState();
        private BasicAttackHud basicAttackHud;
        private bool queuedTouchJump, queuedTouchDash, queuedTouchCard;
        private bool ExplorationUiAvailable => manager?.Dungeon.ActiveVisit==null && manager?.CurrentPhase==GamePhase.Dungeon && !IsUserPaused && !GameplayPause.InputBlocked &&
            !clearReturnVisible && !collectionOpen && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked && manager.Dungeon.IsExplorationMoving;
        private void QueueExplorationFromUi(bool jump)
        {
            if(!ExplorationUiAvailable)return;
            logicalLoop.QueueTimestampedCommand(inputProvider.PointerClickTimestamp,()=>{
                if(!ExplorationUiAvailable)return;
                if(jump)queuedTouchJump=true;else queuedTouchDash=true;
            });
        }
        private bool TouchCardAvailable => manager?.CurrentPhase==GamePhase.Battle && battleFieldReady && !manager.CurrentBattle.IsPaused &&
            !IsUserPaused && !GameplayPause.InputBlocked && !clearReturnVisible && !collectionOpen && !manager.CurrentBattle.CardUsage.IsEmpty &&
            manager.CurrentBattle.CardUsage.CanUseCurrentCard && !manager.CurrentBattle.Player.IsActionPrevented && !manager.CurrentBattle.PlayerMovement.IsNormalMoving;
        private void QueueTouchCard()
        {
            if(!TouchCardAvailable)return;
            logicalLoop.QueueTimestampedCommand(inputProvider.PointerClickTimestamp,()=>{if(TouchCardAvailable)queuedTouchCard=true;});
        }
        private void RefreshTouchContext()
        {
            bool enabled=manager?.CurrentPhase==GamePhase.Battle && battleFieldReady && !manager.CurrentBattle.IsPaused &&
                !IsUserPaused && !GameplayPause.InputBlocked && !clearReturnVisible && !collectionOpen && !awaitingCheckpointChoice;
            if(inputProvider!=null&&inputProvider.SetTouchMovementEnabled(enabled)) { logicalLoop.ReleaseInput();inputProvider.QueueSnapshot(); }
        }
        private bool uiAttackHeld, uiAttackDown, uiAttackUp;
        private void ResetTouchInput(bool preserveCharge = false)
        {
            queuedTouchJump=queuedTouchDash=queuedTouchCard=false;
            if (preserveCharge) basicAttackInput.Suspend(); else basicAttackInput.CancelCharge();
            uiAttackHeld = uiAttackDown = uiAttackUp = false; basicAttackHud?.CancelAttackPointer();
            queuedBasicAttackTap=false;TouchUiCancelGuard.CancelAll();logicalLoop.ReleaseInput();inputProvider?.QueueSnapshot();
        }
        private void OnApplicationFocus(bool focused) { if(!focused){StopGamepadVibration();ResetTouchInput();} }
        private bool applicationInactive;
        private void OnApplicationPause(bool paused)
        {
            applicationInactive = paused;
            if(paused)StopGamepadVibration();
            ResetTouchInput();
            // 復帰通知は入力を取り直すだけ。再開は必ずメニューから明示する。
            if (paused) RequestAutomaticPause();
        }
        private bool queuedBasicAttackTap;
        private bool BasicAttackUiAvailable => manager?.CurrentPhase == GamePhase.Battle && !IsUserPaused && !GameplayPause.InputBlocked &&
            !clearReturnVisible && !collectionOpen && manager.CurrentBattle.CanPlayerBasicAttack && (basicAttackInput.CooldownFrames == 0 || manager.CurrentBattle.CanCancelPlayerRecovery);
        private void QueueBasicAttackFromUi()
        {
            if (!BasicAttackUiAvailable || basicAttackInput.IsCharging || uiAttackHeld) return;
            logicalLoop.QueueTimestampedCommand(inputProvider.PointerClickTimestamp, () => queuedBasicAttackTap = true);
        }
        private void QueueBasicAttackHoldFromUi(bool held)
        {
            if (held && !BasicAttackUiAvailable) return;
            logicalLoop.QueueTimestampedCommand(inputProvider.PointerClickTimestamp, () => {
                if (held) { uiAttackDown |= !uiAttackHeld; uiAttackHeld = true; }
                else { uiAttackUp |= uiAttackHeld; uiAttackHeld = false; }
            });
        }
        private void CancelBasicAttackFromUi()
        {
            logicalLoop.QueueTimestampedCommand(inputProvider.PointerClickTimestamp, () => {
                uiAttackHeld = uiAttackDown = uiAttackUp = false; basicAttackInput.CancelCharge();
            });
        }
        private void RefreshBasicAttackHud()
        {
            if (basicAttackHud == null) return;
            bool visible = manager?.Dungeon.ActiveVisit==null && (manager?.CurrentPhase == GamePhase.Battle || manager?.CurrentPhase == GamePhase.Dungeon) && !clearReturnVisible && !collectionOpen &&
                (keyboardSettingsView == null || !keyboardSettingsView.gameObject.activeSelf) && (gamepadSettingsView==null||!gamepadSettingsView.gameObject.activeSelf);
            basicAttackHud.gameObject.SetActive(visible); if (!visible) return;
            if(manager.CurrentPhase==GamePhase.Dungeon) { basicAttackHud.RenderExploration(ExplorationUiAvailable,GameplayKeyboardProfile); return; }
            var battle = manager.CurrentBattle;
            string state = IsUserPaused ? "ポーズ中" : battle.IsHitStopped ? "ヒットストップ" : battle.CustomScreen.IsOpen || battle.IsPaused ? "カスタム中" :
                battle.Player.IsStaggered ? "のけぞり中" : battle.Player.IsActionPrevented ? "状態異常中" : battle.PlayerMovement.IsNormalMoving ? "移動中" :
                battle.CardUsage.InFlightCard != null ? "カード使用中" : basicAttackInput.CooldownFrames > 0 ? "再使用待ち" :
                GameplayPause.InputBlocked ? "キーを離してください" : basicAttackInput.IsResumePending ? "再押下でタメ再開" : basicAttackInput.IsCharging ? $"タメ {basicAttackInput.ChargeFrames}/{battle.ResolveBasicChargeFrames(basicAttackChargeFrames)}" : "短押し / 長押しでタメ";
            basicAttackHud.Render(GameplayKeyboardProfile.Bindings[("keyboard", "battle", "AttackPrimary")], state, BasicAttackUiAvailable, TouchCardAvailable,GameplayKeyboardProfile);
        }
        private bool battleFieldReady;
        private GameObject pauseCanvas;
        private GameObject pauseOverlay;
        private GameplayKeyboardSettingsView keyboardSettingsView,gamepadSettingsView;
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

        private sealed class DungeonChoice
        {
            public FloorSequenceDefinitions Floors;
            public CheckpointDefinitions Single;
            public string Id => Floors?.RouteId ?? Single.DungeonId;
            public string Key => Floors?.RouteHash ?? Single.MapHash;
            public string Label => Map().DungeonName + (Floors == null ? "" : " / " + Id);
            public DungeonConfig Map() => Floors?.CreateInitialMap(0) ?? Single.CreateInitialMap();
        }
                private bool exchangeOpen;
        private string exchangeSelectedId, exchangeRequestId, exchangeRound;
        private void OpenTokenExchange()
        {
            if (!CanSelectDungeon || styleSelectionOpen || (dungeonSelectionOpen && !initialHub)) return;
            exchangeOpen = true; exchangeSelectedId = exchangeRequestId = exchangeRound = null;
            EnsureTokenExchange(); ShowTokenExchange();
        }
        private void EnsureTokenExchange()
        {
            var factory=CreateCardFactory();var settings=ReturnSettings();
            TokenExchangeProduct Product(string id, bool random) {
                var card=factory.CreateById(id);long overflow=settings.Tokens(card.Rarity,card.ExceedLevel);
                return new TokenExchangeProduct("card-"+id,card,checked(overflow*3),overflow,random?ExchangeLimit.PerRefresh:ExchangeLimit.Unlimited,random?1:0);
            }
            clearReturnSession.EnsureExchange(new[]{Product(SimpleCardFactory.Sword,false)},
                new[]{Product(SimpleCardFactory.Bomb,true),Product(SimpleCardFactory.Heal,true),Product(SimpleCardFactory.Shield,true)},2,randomProvider);
        }
        private void ShowTokenExchange()
        {
            var profile=clearReturnSession.Snapshot;
            clearReturnView.RenderTokenExchange(profile, exchangeSelectedId,
                !CanSelectDungeon, clearReturnSession.Error,
                id => { if(!exchangeOpen || !CanSelectDungeon || profile.ExchangeStock==null || !profile.ExchangeStock.Offers.Any(o=>o.Id==id))return;
                    exchangeSelectedId=id;exchangeRound=profile.ExchangeStock.RoundId;exchangeRequestId=System.Guid.NewGuid().ToString("N");ShowTokenExchange(); },
                () => { if(!exchangeOpen || !CanSelectDungeon || exchangeSelectedId==null)return;
                    if(clearReturnSession.PurchaseExchange(exchangeRequestId,exchangeRound,exchangeSelectedId)) exchangeSelectedId=exchangeRequestId=exchangeRound=null;
                    ShowTokenExchange(); },
                () => { if(!exchangeOpen || !CanSelectDungeon)return;exchangeSelectedId=exchangeRequestId=exchangeRound=null;ShowTokenExchange(); },
                () => { if(!exchangeOpen || !CanSelectDungeon)return;exchangeOpen=false;if(initialHub)ShowDungeonSelection();else ShowClearReturn(); },
                () => { if(!exchangeOpen)return;
                    if(clearReturnSession.Retry()) {exchangeSelectedId=exchangeRequestId=exchangeRound=null;EnsureTokenExchange();}ShowTokenExchange(); });
        }

        private DepartureStyleDefinition[] departureStyles = { DepartureStyleDefinition.Basic };
        private bool styleSelectionOpen;
        private int styleDraft;
        /// <summary>製品定義の登録口。基本以外の種類・数値はここで生成しない。</summary>
        public void ConfigureDepartureStyles(IReadOnlyList<DepartureStyleDefinition> styles)
        {
            if (manager != null) throw new System.InvalidOperationException("起動後のスタイル登録はできません");
            if (styles == null || styles.Count == 0 || styles.Any(s => s == null) || styles.Select(s => s.Id).Distinct().Count() != styles.Count ||
                !styles.Any(s => s.Id == DepartureStyleDefinition.BasicId)) throw new System.ArgumentException("基本を含む重複しないスタイル定義が必要です");
            departureStyles = styles.ToArray();
        }
        private DepartureStyleDefinition SelectedStyle => departureStyles.SingleOrDefault(s => s.Id == (clearReturnSession.Snapshot.SelectedStyleId ?? DepartureStyleDefinition.BasicId));
        private void OpenStyleSelection()
        {
            if (exchangeOpen || !CanSelectDungeon || dungeonSelectionOpen && !initialHub) return;
            styleDraft = System.Array.FindIndex(departureStyles, s => s.Id == (clearReturnSession.Snapshot.SelectedStyleId ?? DepartureStyleDefinition.BasicId));
            styleSelectionOpen = true; ShowStyleSelection();
        }
        private void ShowStyleSelection()
        {
            clearReturnView.RenderStyleSelection(departureStyles.Select(s => s.Name + " / " + (s.Attack?.Element.ToString() ?? "None") +
                (s.RequiredClear == null ? "" : " / 解放: " + s.RequiredClear + "クリア")).ToArray(),
                departureStyles.Select(s => s.IsUnlocked(clearReturnSession.Snapshot)).ToArray(), styleDraft,
                i => { if (styleSelectionOpen && CanSelectDungeon && i >= 0 && i < departureStyles.Length && departureStyles[i].IsUnlocked(clearReturnSession.Snapshot)) { styleDraft = i; ShowStyleSelection(); } },
                () => { if (!styleSelectionOpen || !CanSelectDungeon || styleDraft < 0 || styleDraft >= departureStyles.Length) return;
                    if (!clearReturnSession.SelectDepartureStyle(departureStyles[styleDraft])) { ShowStyleSelection(); return; } CloseStyleSelection(); },
                () => { if (styleSelectionOpen && CanSelectDungeon) CloseStyleSelection(); },
                !CanSelectDungeon, clearReturnSession.Error, RetryStyleSelection);
        }
        private void CloseStyleSelection() { styleSelectionOpen = false; if (initialHub) ShowDungeonSelection(); else ShowClearReturn(); }
        private void RetryStyleSelection()
        {
            if (!styleSelectionOpen) return;
            if (clearReturnSession.Retry()) CloseStyleSelection(); else ShowStyleSelection();
        }
        private List<DungeonChoice> dungeonChoices;
        private bool dungeonSelectionEnabled, initialHub, dungeonSelectionOpen, debugUnlockDungeons;
        private int selectedDungeon, selectionBeforeOpen;
        /// <summary>登録順に解放する既存定義。デバッグ全開放は保存したクリア履歴を書き換えない。</summary>
        public void ConfigureDungeonSelection(IReadOnlyList<FloorSequenceDefinitions> choices, bool debugUnlockAll = false)
        {
            if (manager != null) throw new System.InvalidOperationException("起動後はダンジョン一覧を変更できません");
            if (choices == null || choices.Count == 0 || choices.Any(c => c == null) || choices.Select(c => c.RouteId).Distinct().Count() != choices.Count)
                throw new System.ArgumentException("重複しない既存ダンジョン定義が必要です");
            dungeonChoices = choices.Select(c => new DungeonChoice { Floors = c }).ToList();
            dungeonSelectionEnabled = true; debugUnlockDungeons = debugUnlockAll; floorDefinitions = choices[0];
        }
        private bool DungeonUnlocked(int index) => index >= 0 && index < dungeonChoices.Count &&
            (debugUnlockDungeons || index == 0 || clearReturnSession.Snapshot.ClearedDungeons.Contains(dungeonChoices[index].Id) || clearReturnSession.Snapshot.ClearedDungeons.Contains(dungeonChoices[index - 1].Id));
        private bool CanSelectDungeon => !CustomizationAcquisitionOpen && !CustomizationEditorOpen && !CustomizationUnlockOpen && !progressDialogOpen && clearReturnVisible && !collectionOpen && !ownedDepartureEditing && !savedDeckMenuOpen &&
            !departureWriting && !progressStartupBlocked && clearReturnSession.Ready && !clearReturnSession.HasPendingSave &&
            (initialHub || clearReturnSession.Snapshot.Flow?.Stage == ClearReturnStage.Returned) &&
            clearReturnSession.Snapshot.NextDeparture?.ParentRunId != (clearReturnSession.Snapshot.Flow?.RunId ?? NextDepartureData.InitialParent);
        private void OpenDungeonSelection()
        {
            if (exchangeOpen || !CanSelectDungeon || styleSelectionOpen || !dungeonSelectionEnabled) return;
            selectionBeforeOpen = selectedDungeon; dungeonSelectionOpen = true; ShowDungeonSelection();
        }
        private void ShowDungeonSelection()
        {
            gameplayAudio?.PlayBgm(PlaceholderBgm.Exploration(dungeonBgm));
            clearReturnVisible = true; dungeonSelectionOpen = true;
            clearReturnView.RenderDungeonSelection(dungeonChoices.Select(c => c.Label).ToArray(),
                Enumerable.Range(0, dungeonChoices.Count).Select(DungeonUnlocked).ToArray(), selectedDungeon,
                i => { if (!exchangeOpen && !styleSelectionOpen && dungeonSelectionOpen && CanSelectDungeon && DungeonUnlocked(i)) { selectedDungeon = i; ShowDungeonSelection(); } },
                () => { if (exchangeOpen || styleSelectionOpen || !dungeonSelectionOpen || !CanSelectDungeon || !DungeonUnlocked(selectedDungeon)) return; dungeonSelectionOpen = false;
                    if (initialHub) StartNextDeparture(); else ShowClearReturn(); },
                () => { if (exchangeOpen || styleSelectionOpen || !dungeonSelectionOpen || !CanSelectDungeon) return; selectedDungeon = selectionBeforeOpen; dungeonSelectionOpen = false;
                    if (initialHub) { dungeonSelectionOpen = true; ShowDungeonSelection(); } else ShowClearReturn(); },
                !CanSelectDungeon, clearReturnError ?? clearReturnSession.Error, RetryClearReturn,
                OpenStyleSelection, SelectedStyle?.Name ?? "未登録スタイル", OpenTokenExchange,
                dungeonChoices.Select(c => clearReturnSession.Snapshot.ClearedDungeons.Contains(c.Id)).ToArray(),
                Enumerable.Range(0,dungeonChoices.Count).Select(i => i==0 ? "最初から解放" : "一つ前のダンジョンをクリア").ToArray(),
                dungeonChoices.Select((c,i) => !DungeonUnlocked(i) ? null : ClearReturnView.FormatDungeonInformation(new DungeonDeparturePreview(
                    c.Floors==null ? new[]{c.Map()} : Enumerable.Range(0,c.Floors.FloorCount).Select(f=>c.Floors.CreateInitialMap(f))))).ToArray());
        }

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

        private GamepadVibrationAdapter gamepadVibration;
        private bool gamepadVibrationEnabled;
        private GamepadVibrationSettingsStore vibrationSettings;
        private string vibrationSettingsError;
        public bool GamepadVibrationEnabled=>gamepadVibrationEnabled;
        public bool TrySetGamepadVibrationEnabled(bool enabled,out string error)
        {
            if(!enabled)StopGamepadVibration();
            if(vibrationSettings==null){error="シンドウセッテイがミショキカです";return false;}
            if(!vibrationSettings.TrySave(enabled,out error)){vibrationSettingsError=error;return false;}
            vibrationSettingsError=null;gamepadVibrationEnabled=enabled;if(gamepadVibration!=null)gamepadVibration.Enabled=enabled;return true;
        }
        public bool TryPlayGamepadVibration(GamepadVibrationRequest request)=>isActiveAndEnabled&&(gamepadVibration?.TryPlay(request)??false);
        public void StopGamepadVibration()=>gamepadVibration?.Stop();
        private Olca.Presentation.Dungeon.CustomizationLoadoutView customizationEditorView;
        private Olca.Core.Logic.Customization.CustomizationEditDraft customizationDraft;
        private Button customizationEditorButton, customizationUnlockButton;
        private Olca.Presentation.Dungeon.CustomizationUnlockView customizationUnlockView;
        private bool customizationUnlockSavePending, customizationUnlockWasOpen;
        private bool customizationSavePending, customizationEditorInterrupted;
        private void OnDisable()
        {
            gameplayAudio?.SetPaused(true);
            combatFeedbackAudio?.Bind(null);
            CancelExplorationResume();
            if (pauseOverlay != null) pauseOverlay.SetActive(IsUserPaused);
            StopGamepadVibration();
            if(!CustomizationAcquisitionOpen&&!CustomizationEditorOpen&&!CustomizationUnlockOpen)return;
            customizationAcquisitionView?.Close();
            customizationUnlockWasOpen=false;customizationUnlockView?.Close();
            customizationEditorView.Close(); customizationDraft=null; customizationEditorInterrupted=true;
        }
        private void OnEnable()
        {
            gameplayAudio?.SetPaused(IsUserPaused);
            if(manager!=null&&!manager.IsSuspended)BindBattleFeedback(manager.CurrentBattle);
            if(!customizationEditorInterrupted||clearReturnSession?.Ready!=true)return;
            customizationEditorInterrupted=false;
            if(initialHub)ShowDungeonSelection();else ShowClearReturn();
        }
        private bool CustomizationEditorOpen => customizationEditorView != null && customizationEditorView.gameObject.activeSelf;
        private bool CustomizationUnlockOpen => customizationUnlockView != null && customizationUnlockView.gameObject.activeSelf;
        private void OpenCustomizationUnlocks()
        {
            if(!IsProgressHub||clearReturnSession.HasPendingSave||customizationCatalog==null)return;
            if(!TryGetCustomizationLayout(out _,out _)){clearReturnError="ホゾンとテイギがイッチしません";if(initialHub)ShowDungeonSelection();else ShowClearReturn();return;}
            customizationUnlockWasOpen=true;customizationUnlockSavePending=false;clearReturnView.gameObject.SetActive(false);
            customizationUnlockView.Open(customizationCatalog,clearReturnSession.CustomizationSnapshot);RefreshProgressActions();
        }
        private void CloseCustomizationUnlocks()
        {
            if(customizationUnlockSavePending)return;
            customizationUnlockWasOpen=false;customizationUnlockView.Close();if(initialHub)ShowDungeonSelection();else ShowClearReturn();
        }
        private void ApplyCustomizationUnlockFromUi(string id)
        {
            if(!CustomizationUnlockOpen||customizationUnlockSavePending)return;
            if(!TryApplyCustomizationUnlock(id,out _)){customizationUnlockSavePending=clearReturnSession.HasPendingSave;customizationUnlockView.SaveFailed(customizationUnlockSavePending);return;}
            customizationUnlockView.Applied(clearReturnSession.CustomizationSnapshot);
        }
        private void RetryCustomizationUnlockSave()
        {
            if(!CustomizationUnlockOpen||!customizationUnlockSavePending)return;
            if(!clearReturnSession.Retry()){customizationUnlockView.SaveFailed(true);return;}
            customizationUnlockSavePending=false;customizationUnlockView.Applied(clearReturnSession.CustomizationSnapshot);
        }
        public bool TryGetCustomizationSources(out Olca.Core.Logic.Customization.CustomizationLoadoutSources sources,out string error)
        {
            sources=null;error=null;
            if(clearReturnSession?.Ready!=true||customizationCatalog==null){error="ソウビテイギがミトウロクです";return false;}
            try{sources=new Olca.Core.Logic.Customization.CustomizationLoadoutSources(customizationCatalog,clearReturnSession.CustomizationSnapshot);return true;}
            catch(System.ArgumentException){error="ホゾンとテイギがイッチしません";return false;}
        }
        private void OpenCustomizationEditor()
        {
            if (!IsProgressHub || clearReturnSession.HasPendingSave || customizationCatalog == null) return;
            try {
                customizationDraft = new Olca.Core.Logic.Customization.CustomizationEditDraft(customizationCatalog, clearReturnSession.CustomizationSnapshot, clearReturnSession.Snapshot.Revision);
            } catch (System.ArgumentException) { clearReturnError="ホゾンとテイギがイッチしません";if(initialHub)ShowDungeonSelection();else ShowClearReturn();return; }
            customizationSavePending = false;
            clearReturnView.gameObject.SetActive(false); customizationEditorView.Open(customizationDraft); RefreshProgressActions();
        }
        private void CloseCustomizationEditor()
        {
            if (customizationSavePending) return;
            customizationEditorView?.Close(); customizationDraft = null;
            if (initialHub) ShowDungeonSelection(); else ShowClearReturn();
        }
        private void SaveCustomizationEditor()
        {
            if (this == null || !CustomizationEditorOpen || customizationDraft == null || customizationSavePending) return;
            if (clearReturnSession.Snapshot.Revision != customizationDraft.Revision) { customizationEditorView.SaveFailed(false); return; }
            if (!clearReturnSession.TrySetCustomizationLoadout(customizationCatalog, customizationDraft.Accessories, customizationDraft.Placements)) {
                customizationSavePending = clearReturnSession.HasPendingSave; customizationEditorView.SaveFailed(customizationSavePending); return;
            }
            CloseCustomizationEditor();
        }
        private void RetryCustomizationEditorSave()
        {
            if (!CustomizationEditorOpen || !customizationSavePending) return;
            if (!clearReturnSession.Retry()) { customizationEditorView.SaveFailed(true); return; }
            customizationSavePending = false; CloseCustomizationEditor();
        }

        private Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider customizationAcquisitionProvider;
        private Olca.Presentation.Dungeon.CustomizationAcquisitionView customizationAcquisitionView;
        private Button customizationAcquisitionButton;
        private string customizationAcquisitionContext;
        private bool customizationAcquisitionPending;
        private bool CustomizationAcquisitionOpen=>customizationAcquisitionView!=null&&customizationAcquisitionView.gameObject.activeSelf;
        public void ConfigureCustomizationAcquisitions(Olca.Core.Logic.Customization.ICustomizationAcquisitionProvider provider)
        {
            if(manager!=null)throw new System.InvalidOperationException("取得候補は起動前に登録してください");
            customizationAcquisitionProvider=provider??throw new System.ArgumentNullException(nameof(provider));
        }
        private void OpenCustomizationAcquisitions()
        {
            if(!IsProgressHub||clearReturnSession.HasPendingSave||customizationCatalog==null||customizationAcquisitionProvider==null)return;
            try{
                var flow=clearReturnSession.Snapshot.Flow;string run=flow?.Outcome==RunReturnOutcome.Cleared?flow.RunId:null;
                // クリア報酬は攻略IDを入手元IDに固定する。拠点イベントIDは入手元の明示入力。
                string fixedId=run==null?null:"clear:"+run;
                var saved=fixedId==null?null:clearReturnSession.FindCustomizationAcquisition(fixedId);
                var definition=saved==null?customizationAcquisitionProvider.Resolve(new Olca.Core.Logic.Customization.CustomizationAcquisitionContext(run,flow?.DungeonKey)):null;
                var requested=definition?.Snapshot;
                if(saved==null&&(requested==null||fixedId!=null&&requested.ContextId!=fixedId||requested.Candidates.Any(v=>v.ClearRunId!=run)))throw new System.ArgumentException("入手元と候補が一致しません");
                customizationAcquisitionContext=saved?.ContextId??requested.ContextId;
                bool prepared=saved!=null||clearReturnSession.TryPrepareCustomizationAcquisition(customizationCatalog,definition);
                customizationAcquisitionPending=clearReturnSession.HasPendingSave;
                if(!prepared&&!customizationAcquisitionPending)throw new System.ArgumentException(clearReturnSession.Error);
                clearReturnView.gameObject.SetActive(false);
                customizationAcquisitionView.Open(clearReturnSession.FindCustomizationAcquisition(customizationAcquisitionContext),clearReturnSession.CustomizationSnapshot,customizationAcquisitionPending);RefreshProgressActions();
            }catch(System.ArgumentException){clearReturnError="ソウビのコウホをカクニンできません。ホゾンをホジしています";if(initialHub)ShowDungeonSelection();else ShowClearReturn();}
        }
        private void ClaimCustomizationAcquisition(string requestId)
        {
            if(!CustomizationAcquisitionOpen||customizationAcquisitionPending)return;
            if(!clearReturnSession.TryClaimCustomizationAcquisition(customizationCatalog,customizationAcquisitionContext,requestId)){
                customizationAcquisitionPending=clearReturnSession.HasPendingSave;customizationAcquisitionView.Failed(customizationAcquisitionPending);return;
            }
            customizationAcquisitionView.Saved(clearReturnSession.FindCustomizationAcquisition(customizationAcquisitionContext),clearReturnSession.CustomizationSnapshot);
        }
        private void RetryCustomizationAcquisition()
        {
            if(!CustomizationAcquisitionOpen||!customizationAcquisitionPending)return;
            if(!clearReturnSession.Retry()){customizationAcquisitionView.Failed(true);return;}
            customizationAcquisitionPending=false;customizationAcquisitionView.Saved(clearReturnSession.FindCustomizationAcquisition(customizationAcquisitionContext),clearReturnSession.CustomizationSnapshot);
        }
        private void CloseCustomizationAcquisitions()
        {
            if(customizationAcquisitionPending)return;
            customizationAcquisitionView.Close();if(initialHub)ShowDungeonSelection();else ShowClearReturn();
        }
        private IEvaluationRewardProvider evaluationRewardProvider;
        public void ConfigureEvaluationRewards(IEvaluationRewardProvider provider)
        {
            if(manager!=null)throw new System.InvalidOperationException("評価報酬定義は起動前に登録してください");
            evaluationRewardProvider=provider??throw new System.ArgumentNullException(nameof(provider));
        }
        private Olca.Core.Logic.Customization.CustomizationAbilityCatalog customizationAbilityCatalog;
        public void ConfigureCustomizationAbilities(Olca.Core.Logic.Customization.CustomizationAbilityCatalog definitions)
        {
            if(manager!=null||clearReturnSession!=null)throw new System.InvalidOperationException("Controllerキドウマエにノウリョクをトウロクしてください");
            customizationAbilityCatalog=definitions??throw new System.ArgumentNullException(nameof(definitions));
        }
        private void ApplyDepartureCustomization(PlayerCharacter player)
        {
            if(customizationAbilityCatalog==null)return;
            if(!TryGetCustomizationSources(out var sources,out var error))throw new System.ArgumentException(error);
            player.SetDepartureAbilities(customizationAbilityCatalog.Resolve(sources));
        }
        private Olca.Core.Logic.Customization.CustomizationCatalog customizationCatalog;
        public void ConfigureCustomizationCatalog(Olca.Core.Logic.Customization.CustomizationCatalog catalog)
        {
            if (manager != null || clearReturnSession != null) throw new System.InvalidOperationException("起動後は装備定義を変更できません");
            customizationCatalog = catalog ?? throw new System.ArgumentNullException(nameof(catalog));
        }
        public Olca.Core.Logic.Customization.CustomizationProgress CustomizationState => clearReturnSession?.Ready == true ? clearReturnSession.CustomizationSnapshot : null;
        public bool TryGrantCustomization(Olca.Core.Logic.Customization.CustomizationGrant grant, out string error)
        {
            error = null;
            if (clearReturnSession == null || customizationCatalog == null) { error = "装備定義と進行保存の初期化が必要です"; return false; }
            bool result = clearReturnSession.TryGrantCustomization(customizationCatalog, grant); if (!result) error = clearReturnSession.Error; return result;
        }
        public bool TryApplyCustomizationUnlock(string itemId, out string error)
        {
            error = null;
            if (clearReturnSession == null || customizationCatalog == null) { error = "装備定義と進行保存の初期化が必要です"; return false; }
            bool result = clearReturnSession.TryApplyCustomizationUnlock(customizationCatalog, itemId); if (!result) error = clearReturnSession.Error; return result;
        }
        public bool TryGetCustomizationLayout(out Olca.Core.Logic.Customization.PartsLayout layout, out string error)
        {
            layout = null; error = null;
            if (clearReturnSession == null || customizationCatalog == null) { error = "装備定義と進行保存の初期化が必要です"; return false; }
            return clearReturnSession.TryGetCustomizationLayout(customizationCatalog, out layout, out error);
        }
        public bool TrySetCustomizationLoadout(IEnumerable<string> accessories, IEnumerable<Olca.Core.Logic.Customization.PartPlacement> placements, out string error)
        {
            error = null;
            if (clearReturnSession == null || customizationCatalog == null) { error = "装備定義と進行保存の初期化が必要です"; return false; }
            bool result = clearReturnSession.TrySetCustomizationLoadout(customizationCatalog, accessories, placements); if (!result) error = clearReturnSession.Error; return result;
        }

        private IEnemyAIProvider enemyAIProvider;
        /// <summary>起動前に登録し、通常出撃/再戦/階層/保存復帰の全Managerへ同じ定義元を渡す。</summary>
        public void ConfigureEnemyAIProvider(IEnemyAIProvider provider)
        {
            if(manager!=null)throw new System.InvalidOperationException("起動後は敵定義元を変更できません");
            enemyAIProvider=provider;
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

        private DialogueSession dialogue;private DialogueView dialogueView;private System.Action<DialogueEndReason> dialogueFinished;private bool finishingDialogue;
        private readonly UnityLogicalLoop dialogueLoop=new UnityLogicalLoop(observeEditor:true);
        private DialogueTextSettingsStore dialogueSettings;private DialogueTextSpeed dialogueSpeed=DialogueTextSpeed.Normal;private string dialogueSettingsError;
        public DialogueSession CurrentDialogue=>dialogue;private bool DialogueOpen=>dialogue!=null;
        public bool TryStartDialogue(DialogueScript script,System.Action<DialogueEndReason> finished=null)
        {
            if(script==null||DialogueOpen||finishingDialogue||manager==null||IsUserPaused||clearReturnVisible||awaitingCheckpointChoice||manager.IsSuspended||manager.Dungeon.IsCheckpointBlocked||manager.Dungeon.ActiveVisit!=null||
                (manager.CurrentPhase!=GamePhase.Dungeon&&manager.CurrentPhase!=GamePhase.Battle)||manager.CurrentPhase==GamePhase.Battle&&!battleFieldReady)return false;
            logicalLoop.Observe(false);logicalLoop.ReleaseInput();ResetTouchInput();dialogue=new DialogueSession(script,dialogueSpeed);dialogueFinished=finished;dialogueLoop.StartNewTimeline();GameplayPause.SetPaused(true);inputProvider.QueueSnapshot();dialogueView.Bind(dialogue);return true;
        }
        private void FinishDialogue(bool interrupt=false)
        {
            if(dialogue==null)return;var current=dialogue;if(interrupt)current.Interrupt();if(current.IsOpen)return;
            dialogue=null;if(dialogueView!=null)dialogueView.Bind(null);var callback=dialogueFinished;dialogueFinished=null;logicalLoop.Observe(false);ResetTouchInput();GameplayPause.SetPaused(IsUserPaused);inputProvider?.QueueSnapshot();
            finishingDialogue=true;try{callback?.Invoke(current.EndReason);}finally{finishingDialogue=false;}
        }
        private void UpdateDialogue()
        {
            bool active=DialogueOpen&&!IsUserPaused&&!dialogue.BacklogOpen&&!dialogue.SkipConfirmationOpen;
            dialogueLoop.PumpTimestamped(active,null,(frame,ms,input)=>dialogue?.AdvanceFrame(),()=>DialogueOpen&&dialogue.IsOpen&&!IsUserPaused&&!dialogue.BacklogOpen&&!dialogue.SkipConfirmationOpen);
            if(!DialogueOpen)return;if(!dialogue.IsOpen){FinishDialogue();return;}
            if(dialogueView!=null){dialogueView.gameObject.SetActive(!IsUserPaused);if(!IsUserPaused){dialogueView.Refresh();dialogueView.transform.SetAsLastSibling();}}
        }
        public bool IsUserPaused => manager != null && manager.IsUserPaused;
        [SerializeField, HideInInspector] private bool validationStorageRequired;
        public void RequireValidationStorageGuard()
        {
            if (inputProvider != null) throw new System.InvalidOperationException("起動後は検証ガードを変更できません");
            if (ValidationStorageGuard.FromEditorProject() == null) throw new System.IO.IOException("検証専用の保存先設定が必須です");
            validationStorageRequired = true;
        }
        private string gameplaySettingsDirectoryOverride;
        private KeyboardBindingFileStore keyboardSettings;
        private GamepadBindingFileStore gamepadSettings;
        public Olca.Core.Domain.InputBindingProfile GameplayGamepadProfile=>inputProvider?.GamepadProfile;
        public string GameplayGamepadSettingsError {get;private set;}
        public bool TryApplyGameplayGamepadProfile(Olca.Core.Domain.InputBindingProfile profile,out string error)
        {
            error=null;if(!IsUserPaused||inputProvider==null){error="ユーザーポーズ中のみ適用できます";return false;}
            if(!UnityInputProvider.ValidateGamepadProfile(profile,out error))return false;
            if(gamepadSettings==null||!gamepadSettings.TrySave(profile,out error)){GameplayGamepadSettingsError=error;return false;}
            if(!inputProvider.TryApplyGamepadProfile(profile,out error))return false;
            GameplayGamepadSettingsError=null;ResetTouchInput();return true;
        }
        private TransformationPresentationFileStore transformationSettings;
        private bool shortTransformationPresentation;
        private string transformationSettingsError;
        private AudioVolumeFileStore audioSettingsStore;
        private AudioVolumeSettings audioVolumes=new AudioVolumeSettings();
        private GameplayAudioRouter gameplayAudio;
        private CombatFeedbackAudio combatFeedbackAudio;
        private GameplayAudioSettingsView audioSettingsView;
        public AudioVolumeSettings GameplayAudioVolumes=>audioVolumes;
        public GameplayAudioRouter GameplayAudio=>gameplayAudio;
        public string GameplayAudioSettingsError { get; private set; }
        [SerializeField] private AudioClip dungeonBgm,battleBgm,uiEffect;
        private bool AudioSettingsOpen=>audioSettingsView!=null&&audioSettingsView.gameObject.activeSelf;
        public bool TryApplyGameplayAudioVolumes(AudioVolumeSettings value,out string error)
        {
            error=null;if(!IsUserPaused){error="ポーズ中のみ音量設定を適用できます";return false;}
            if(audioSettingsStore==null||!audioSettingsStore.TrySave(value,out error)){GameplayAudioSettingsError=error;return false;}
            audioVolumes=value;gameplayAudio.Apply(value);GameplayAudioSettingsError=null;return true;
        }
        private void OpenAudioSettings(){if(!IsUserPaused||progressDialogOpen||audioSettingsView==null)return;keyboardSettingsView?.DiscardAndClose();if(gamepadSettingsView!=null)gamepadSettingsView.DiscardAndClose();pauseOverlay.SetActive(false);audioSettingsView.Open(audioVolumes,GameplayAudioSettingsError);}
        private void CloseAudioSettings(){audioSettingsView.DiscardAndClose();pauseOverlay.SetActive(IsUserPaused);if(IsUserPaused)pauseOverlay.transform.SetAsLastSibling();}

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
            ResetTouchInput();
            return true;
        }

        /// <summary>Editor／Development Build専用。MCP等の開発入口から呼び、通常設定へ保存しない。</summary>
        public bool TryExecuteDebugGameSpeedCommand(string command, out string error)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int percent;
            switch(command) {
                case "game-speed 0.5": percent=50;break;
                case "game-speed 1": case "game-speed reset":percent=100;break;
                case "game-speed 2":percent=200;break;
                default:error="game-speed 0.5 | 1 | 2 | reset のみ使用できます";return false;
            }
            // 変更要求時点までを旧速度で確定。停止中の壁時計時間を回収しない。
            logicalLoop.Observe(manager!=null && !DialogueOpen && manager.Dungeon.ActiveVisit==null && !clearReturnVisible &&
                !IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked && Time.timeScale>0);
            logicalLoop.Clock.TrySetDebugSpeedPercent(percent);
            error=null;return true;
#else
            error="開発ビルド専用コマンドです";return false;
#endif
        }
        public int DebugGameSpeedPercent => logicalLoop.Clock.DebugSpeedPercent;

        private DungeonBattleManager automaticPauseOwner;
        private void OnGamepadDisconnected()
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            RequestAutomaticPause();
#endif
        }
        private void RequestAutomaticPause()
        {
            if (this == null || manager == null) return;
            // 勝敗演出中はメニューを開かず、同じ攻略の演出完了後へ要求を引き継ぐ。
            if (manager.IsBattleEndPresentationPending) { automaticPauseOwner = manager; return; }
            SetUserPaused(true);
        }

        private readonly ExplorationResumeCountdown explorationResume = new ExplorationResumeCountdown();
        private readonly UnityLogicalLoop explorationResumeLoop = new UnityLogicalLoop(observeEditor: true);
        private DungeonBattleManager explorationResumeOwner;
        private Text explorationResumeLabel;
        public bool IsExplorationResumeCounting => explorationResume.IsRunning;
        public int ExplorationResumeRemainingFrames => explorationResume.RemainingFrames;
        private bool CanCountExplorationResume => manager != null && ReferenceEquals(manager, explorationResumeOwner) &&
            manager.CurrentPhase == GamePhase.Dungeon && manager.IsUserPaused && !manager.IsSuspended &&
            !DialogueOpen && manager.Dungeon.ActiveVisit == null && !clearReturnVisible && !awaitingCheckpointChoice &&
            !manager.Dungeon.IsCheckpointBlocked && !applicationInactive;
        private void CancelExplorationResume()
        {
            explorationResume.Cancel(); explorationResumeOwner = null; explorationResumeLoop.Observe(false);
            if (explorationResumeLabel != null) explorationResumeLabel.gameObject.SetActive(false);
        }
        private void UpdateExplorationResume()
        {
            if (!explorationResume.IsRunning) { explorationResumeLoop.Observe(false); return; }
            if (!CanCountExplorationResume)
            {
                CancelExplorationResume();
                if (pauseOverlay != null) pauseOverlay.SetActive(IsUserPaused);
                return;
            }
            explorationResumeLoop.PumpTimestamped(true, null, (frame, ms, input) => AdvanceExplorationResumeFrame(),
                () => explorationResume.IsRunning && CanCountExplorationResume);
            if (explorationResumeLabel != null && explorationResume.IsRunning)
                explorationResumeLabel.text = explorationResume.DisplayCount.ToString();
        }
        private void AdvanceExplorationResumeFrame()
        {
            if (!CanCountExplorationResume) { CancelExplorationResume(); return; }
            if (explorationResume.AdvanceFrame()) { CancelExplorationResume(); ApplyUserPauseImmediate(false); }
        }

        public void SetUserPaused(bool paused)
        {
            bool cancelledCountdown = paused && explorationResume.IsRunning;
            if (paused) CancelExplorationResume();
            else if (explorationResume.IsRunning) return;
            if (!paused && manager != null && manager.IsUserPaused && manager.CurrentPhase == GamePhase.Dungeon &&
                !DialogueOpen && manager.Dungeon.ActiveVisit == null && !clearReturnVisible && !manager.IsSuspended &&
                !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked)
            {
                explorationResumeOwner = manager;
                if (CanCountExplorationResume)
                {
                    explorationResume.Begin(); explorationResumeLoop.StartNewTimeline();
                    if (keyboardSettingsView != null) keyboardSettingsView.DiscardAndClose();
                    if (audioSettingsView != null) audioSettingsView.DiscardAndClose();
                    if (gamepadSettingsView != null) gamepadSettingsView.DiscardAndClose();
                    logicalLoop.Observe(false); logicalLoop.ReleaseInput(); ResetTouchInput(preserveCharge: true);
                    systemMenu?.Suspend(); systemMenuRoot = null;
                    if (pauseOverlay != null) pauseOverlay.SetActive(false);
                    if (explorationResumeLabel != null) { explorationResumeLabel.text = "3"; explorationResumeLabel.gameObject.SetActive(true); }
                }
                else explorationResumeOwner = null;
                return;
            }
            ApplyUserPauseImmediate(paused);
            if (cancelledCountdown && IsUserPaused && pauseOverlay != null) pauseOverlay.SetActive(true);
        }

        private void ApplyUserPauseImmediate(bool paused)
        {
            if(paused)StopGamepadVibration();
            if (manager == null || manager.IsUserPaused == paused || (paused&&manager.IsBattleEndPresentationPending)) return;
            systemMenu?.Suspend();systemMenuRoot=null;if(paused)pauseFocus="ResumeButton";
            logicalLoop.Observe(!DialogueOpen && !manager.IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked && Time.timeScale > 0);
            ResetTouchInput(preserveCharge: true);
            manager.SetUserPaused(paused);
            if(dialogue!=null)dialogue.IsPaused=paused;
            dialogueLoop.Observe(false);
            gameplayAudio?.SetPaused(paused||!isActiveAndEnabled);
            GameplayPause.SetPaused(paused||DialogueOpen);
            if (paused) { timeScaleBeforePause = Time.timeScale; Time.timeScale = 0f; }
            else { Time.timeScale = timeScaleBeforePause; if (keyboardSettingsView != null) keyboardSettingsView.DiscardAndClose();if(audioSettingsView!=null)audioSettingsView.DiscardAndClose();if(gamepadSettingsView!=null)gamepadSettingsView.DiscardAndClose(); }
            // Unityでは破棄済み参照もCLR上は非nullになるため、生存判定を通す。
            if (pauseOverlay != null) { pauseOverlay.SetActive(paused); if (paused) pauseOverlay.transform.SetAsLastSibling(); }
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
                UiLook.Button(button);button.onClick.AddListener(()=>gameplayAudio?.PlayEffect(UiFeedbackSounds.ForButton(name,uiEffect))); return button;
            }
            var countdownObject = new GameObject("ExplorationResumeCountdown", typeof(RectTransform), typeof(Text));
            countdownObject.transform.SetParent(pauseCanvas.transform, false);
            explorationResumeLabel = countdownObject.GetComponent<Text>();
            explorationResumeLabel.font = font; explorationResumeLabel.fontSize = 96;
            explorationResumeLabel.alignment = TextAnchor.MiddleCenter; explorationResumeLabel.color = Color.white;
            explorationResumeLabel.raycastTarget = false;
            explorationResumeLabel.rectTransform.anchorMin = explorationResumeLabel.rectTransform.anchorMax = new Vector2(.5f, .5f);
            explorationResumeLabel.rectTransform.sizeDelta = new Vector2(240, 160);
            countdownObject.SetActive(false);
            currencyOverlay = new GameObject("CurrencyRewardPanel", typeof(RectTransform), typeof(Image));
            currencyOverlay.transform.SetParent(pauseCanvas.transform, false);
            var currencyRect = (RectTransform)currencyOverlay.transform; currencyRect.anchorMin = Vector2.zero; currencyRect.anchorMax = Vector2.one; currencyRect.offsetMin = currencyRect.offsetMax = Vector2.zero;
            currencyOverlay.GetComponent<Image>().color = UiLook.Surface;
            Text CurrencyText(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size) {
                var obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
                var text = obj.GetComponent<Text>(); text.font = font; text.fontSize = 30; UiLook.Label(text); text.color = UiLook.Ink; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
                var r = text.rectTransform; r.anchorMin = r.anchorMax = anchor; r.anchoredPosition = position; r.sizeDelta = size; return text;
            }
            currencyRewardText = CurrencyText("CurrencyRewardText", currencyOverlay.transform, new Vector2(.5f,.5f), new Vector2(0,100), new Vector2(1000,180));
            receiveCurrencyButton = MakeButton("ReceiveCurrencyButton", "ウケトル", currencyOverlay.transform, new Vector2(.5f,.5f), new Vector2(-240,-80));
            receiveCurrencyButton.onClick.AddListener(() => { if (!GameplayPause.InputBlocked) currencyChoice?.TrySetResult(true); });
            MakeButton("SkipCurrencyButton", "スキップ", currencyOverlay.transform, new Vector2(.5f,.5f), new Vector2(240,-80)).onClick.AddListener(() => { if (!GameplayPause.InputBlocked) currencyChoice?.TrySetResult(false); });
            currencyBalanceText = CurrencyText("CurrencyBalance", pauseCanvas.transform, Vector2.one, new Vector2(-160,-125), new Vector2(300,45));
            currencyBalanceText.color=Color.white; // 背景上のHUDだけは明るい文字で可読性を保つ。
            MakeButton("PauseCurrencyReward","ポーズ",currencyOverlay.transform,Vector2.one,new Vector2(-120,-60)).onClick.AddListener(()=>SetUserPaused(true));
            currencyOverlay.SetActive(false);
            itemRewardOverlay=new GameObject("ItemRewardPanel",typeof(RectTransform),typeof(Image));itemRewardOverlay.transform.SetParent(pauseCanvas.transform,false);
            var itemRect=(RectTransform)itemRewardOverlay.transform;itemRect.anchorMin=Vector2.zero;itemRect.anchorMax=Vector2.one;itemRect.offsetMin=itemRect.offsetMax=Vector2.zero;itemRewardOverlay.GetComponent<Image>().color=UiLook.Surface;
            itemRewardText=CurrencyText("ItemRewardText",itemRewardOverlay.transform,new Vector2(.5f,.5f),new Vector2(0,100),new Vector2(1200,220));
            MakeButton("ReceiveItemReward","ウケトル",itemRewardOverlay.transform,new Vector2(.5f,.5f),new Vector2(-240,-100)).onClick.AddListener(()=>{if(!GameplayPause.InputBlocked)itemRewardChoice?.TrySetResult(true);});
            MakeButton("SkipItemReward","スキップ",itemRewardOverlay.transform,new Vector2(.5f,.5f),new Vector2(240,-100)).onClick.AddListener(()=>{if(!GameplayPause.InputBlocked)itemRewardChoice?.TrySetResult(false);});
            MakeButton("PauseItemReward","ポーズ",itemRewardOverlay.transform,Vector2.one,new Vector2(-120,-60)).onClick.AddListener(()=>SetUserPaused(true));
            itemRewardOverlay.SetActive(false);

            MakeButton("PauseButton", "ポーズ", pauseCanvas.transform, Vector2.one, new Vector2(-120, -60)).onClick.AddListener(() => SetUserPaused(true));
            pauseOverlay = new GameObject("PauseOverlay", typeof(RectTransform), typeof(Image)); pauseOverlay.transform.SetParent(pauseCanvas.transform, false);
            var overlayRect = (RectTransform)pauseOverlay.transform; overlayRect.anchorMin = Vector2.zero; overlayRect.anchorMax = Vector2.one; overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
            pauseOverlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            MakeButton("ResumeButton", "再開", pauseOverlay.transform, new Vector2(0.5f, 0.5f), Vector2.zero).onClick.AddListener(() => SetUserPaused(false));
            MakeButton("LoadCheckpointButton", "続きから", pauseOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -100)).onClick.AddListener(ContinueCheckpointFromUi);
            MakeButton("KeyboardSettingsButton", "キー設定", pauseOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -200))
                .onClick.AddListener(() => {if(gamepadSettingsView!=null)gamepadSettingsView.DiscardAndClose();keyboardSettingsView.Open(GameplayKeyboardProfile);});
            var padSettingsButton=MakeButton("GamepadSettingsButton","パッド設定",pauseOverlay.transform,new Vector2(.5f,.5f),new Vector2(420,-100));
            ((RectTransform)padSettingsButton.transform).sizeDelta=new Vector2(420,70);
            padSettingsButton.onClick.AddListener(()=>{keyboardSettingsView.DiscardAndClose();gamepadSettingsView.Open(GameplayGamepadProfile);});
            var vibrationToggle=MakeButton("VibrationSetting","",pauseOverlay.transform,new Vector2(.5f,.5f),new Vector2(-420,-200));
            ((RectTransform)vibrationToggle.transform).sizeDelta=new Vector2(420,70);
            void RefreshVibrationSetting(){vibrationToggle.GetComponentInChildren<Text>().text=vibrationSettingsError!=null?"シンドウ: ホゾンエラー（サイシコウ）":"シンドウ: "+(GamepadVibrationEnabled?"ON":"OFF");}
            vibrationToggle.onClick.AddListener(()=>{TrySetGamepadVibrationEnabled(!GamepadVibrationEnabled,out _);RefreshVibrationSetting();});RefreshVibrationSetting();
            var animationSetting=MakeButton("TransformationAnimationSetting","",pauseOverlay.transform,new Vector2(.5f,.5f),new Vector2(420,-200));
            ((RectTransform)animationSetting.transform).sizeDelta=new Vector2(420,70);
            void RefreshAnimationSetting(){animationSetting.GetComponentInChildren<Text>().text=transformationSettingsError!=null?"演出設定 保存失敗（再試行）":"変身演出："+(shortTransformationPresentation?"短縮":"通常");}
            animationSetting.onClick.AddListener(()=>{
                if(transformationSettings.TrySave(!shortTransformationPresentation,out transformationSettingsError)){
                    shortTransformationPresentation=!shortTransformationPresentation;
                    if(manager?.CurrentBattle!=null)manager.CurrentBattle.Transformations.ShortPresentation=shortTransformationPresentation;
                }
                RefreshAnimationSetting();
            });RefreshAnimationSetting();
            MakeButton("OwnedCardsButton", "所持カード", pauseOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -300)).onClick.AddListener(OpenOwnedCards);
            MakeButton("RetreatButton", "撤退して\n帰還", pauseOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -400)).onClick.AddListener(RequestRetreat);
            retreatConfirmation = new GameObject("RetreatConfirmation", typeof(RectTransform), typeof(Image)); retreatConfirmation.transform.SetParent(pauseCanvas.transform, false);
            var rr = (RectTransform)retreatConfirmation.transform; rr.anchorMin = Vector2.zero; rr.anchorMax = Vector2.one; rr.offsetMin = rr.offsetMax = Vector2.zero;
            retreatConfirmation.GetComponent<Image>().color = UiLook.Surface;
            var warning = new GameObject("RetreatWarning", typeof(RectTransform), typeof(Text)); warning.transform.SetParent(retreatConfirmation.transform, false);
            var wt = warning.GetComponent<Text>(); wt.font = font; wt.fontSize = 30; UiLook.Label(wt); wt.alignment = TextAnchor.MiddleCenter; wt.raycastTarget = false;
            wt.text = "撤退すると攻略中に獲得したカード・通貨をすべて失います。\n経験値は獲得しません。既存の所持・成長は保持します。";
            wt.rectTransform.sizeDelta = new Vector2(1600,200); wt.rectTransform.anchoredPosition = new Vector2(0,150);
            MakeButton("ConfirmRetreat", "撤退して帰還", retreatConfirmation.transform, new Vector2(.5f,.5f), new Vector2(-280,-100)).onClick.AddListener(ConfirmRetreat);
            MakeButton("CancelRetreat", "攻略へ戻る", retreatConfirmation.transform, new Vector2(.5f,.5f), new Vector2(280,-100)).onClick.AddListener(() => { retreatConfirmation.SetActive(false); pauseOverlay.SetActive(true); });
            retreatConfirmation.SetActive(false);
            var settingsPanel = new GameObject("KeyboardSettingsPanel", typeof(RectTransform));
            settingsPanel.transform.SetParent(pauseCanvas.transform, false);
            keyboardSettingsView = settingsPanel.AddComponent<GameplayKeyboardSettingsView>();
            keyboardSettingsView.Initialize(font, candidate => TryApplyGameplayKeyboardProfile(candidate, out var error) ? null : error);
            var padPanel=new GameObject("GamepadSettingsPanel",typeof(RectTransform));padPanel.transform.SetParent(pauseCanvas.transform,false);gamepadSettingsView=padPanel.AddComponent<GameplayKeyboardSettingsView>();
            gamepadSettingsView.Initialize(font,candidate=>TryApplyGameplayGamepadProfile(candidate,out var error)?null:error,true);
            var audioPanel=new GameObject("AudioSettingsPanel",typeof(RectTransform));audioPanel.transform.SetParent(pauseCanvas.transform,false);
            audioSettingsView=audioPanel.AddComponent<GameplayAudioSettingsView>();audioSettingsView.Initialize(font,candidate=>TryApplyGameplayAudioVolumes(candidate,out var error)?null:error,CloseAudioSettings);
            var audioButton=MakeButton("AudioSettingsButton","音量設定",pauseOverlay.transform,new Vector2(.5f,.5f),new Vector2(420,-300));
            ((RectTransform)audioButton.transform).sizeDelta=new Vector2(420,70);audioButton.onClick.AddListener(OpenAudioSettings);
            pauseOverlay.SetActive(false);
            checkpointOverlay = new GameObject("CheckpointOverlay", typeof(RectTransform), typeof(Image));
            checkpointOverlay.transform.SetParent(pauseCanvas.transform, false);
            var cr = (RectTransform)checkpointOverlay.transform;
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
            checkpointOverlay.GetComponent<Image>().color = UiLook.Surface;
            var message = new GameObject("CheckpointMessage", typeof(RectTransform), typeof(Text));
            message.transform.SetParent(checkpointOverlay.transform, false);
            checkpointMessage = message.GetComponent<Text>(); checkpointMessage.font = font; UiLook.Label(checkpointMessage);
            checkpointMessage.fontSize = 30; checkpointMessage.alignment = TextAnchor.MiddleCenter;
            checkpointMessage.horizontalOverflow = HorizontalWrapMode.Wrap;
            checkpointMessage.rectTransform.sizeDelta = new Vector2(1360, 560);
            checkpointMessage.rectTransform.anchoredPosition = new Vector2(0, 90);
            continueCheckpointButton = MakeButton("ContinueCheckpointButton", "続きから", checkpointOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -270));
            continueCheckpointButton.onClick.AddListener(ContinueCheckpointFromUi);
            retryCheckpointButton = MakeButton("RetryCheckpointButton", "保存を再試行", checkpointOverlay.transform, new Vector2(0.5f, 0.5f), new Vector2(0, -270));
            ((RectTransform)retryCheckpointButton.transform).sizeDelta = new Vector2(320, 70);
            retryCheckpointButton.onClick.AddListener(() => { RetryMemoryCheckpoint(); ShowCheckpointStatus(); });
            CreateProgressActions(font,MakeButton);
            var dialoguePanel=new GameObject("DialoguePanel",typeof(RectTransform));dialoguePanel.transform.SetParent(pauseCanvas.transform,false);dialogueView=dialoguePanel.AddComponent<DialogueView>();dialogueView.Initialize(font,()=>SetUserPaused(true));
            var textSpeedButton=MakeButton("DialogueTextSpeedButton","",pauseOverlay.transform,new Vector2(.5f,.5f),new Vector2(420,-400));((RectTransform)textSpeedButton.transform).sizeDelta=new Vector2(420,70);
            void RefreshTextSpeed(){textSpeedButton.GetComponentInChildren<Text>().text=dialogueSettingsError??"文字表示："+new[]{"ゆっくり","標準","速い","即時"}[(int)dialogueSpeed];}
            textSpeedButton.onClick.AddListener(()=>{var candidate=(DialogueTextSpeed)(((int)dialogueSpeed+1)%4);if(dialogueSettings.TrySave(candidate,out dialogueSettingsError)){dialogueSpeed=candidate;dialogue?.SetSpeed(candidate);}RefreshTextSpeed();});RefreshTextSpeed();
            ShowCheckpointStatus();
            var returnPanel = new GameObject("ClearReturnPanel", typeof(RectTransform)); returnPanel.transform.SetParent(pauseCanvas.transform, false);
            clearReturnView = returnPanel.AddComponent<ClearReturnView>();
            clearReturnView.Initialize(font, ToggleCarryCard, ConfirmCarryCards, ConfirmClearReturn, RetryClearReturn, StartNextDeparture, CloseOwnedCards, OpenOwnedDeparture, OpenSavedDeckMenu, dungeonSelectionEnabled ? (System.Action)OpenDungeonSelection : null, OpenStyleSelection, OpenTokenExchange);
            var attackPanel = new GameObject("BasicAttackHud", typeof(RectTransform)); attackPanel.transform.SetParent(pauseCanvas.transform, false);
            basicAttackHud = attackPanel.AddComponent<BasicAttackHud>(); basicAttackHud.Initialize(font, QueueBasicAttackFromUi, QueueBasicAttackHoldFromUi, CancelBasicAttackFromUi);
            basicAttackHud.InitializeExploration(font,()=>QueueExplorationFromUi(true),()=>QueueExplorationFromUi(false),QueueTouchCard);
            basicAttackHud.gameObject.SetActive(false);
            var collectionPanel=new GameObject("CardCollectionPanel",typeof(RectTransform));collectionPanel.transform.SetParent(pauseCanvas.transform,false);
            cardCollectionView=collectionPanel.AddComponent<ClearReturnView>();cardCollectionView.Initialize(font,null,null,null,null);
            cardCollectionButton=MakeButton("CardCollectionButton","カード図鑑",pauseCanvas.transform,Vector2.one,new Vector2(-310,-85));
            ((RectTransform)cardCollectionButton.transform).sizeDelta=new Vector2(300,60);
            cardCollectionButton.onClick.AddListener(()=>OpenCardCollection());
            cardCollectionButton.gameObject.SetActive(false);
            var customizationPanel = new GameObject("CustomizationEditor", typeof(RectTransform)); customizationPanel.transform.SetParent(pauseCanvas.transform, false);
            customizationEditorView = customizationPanel.AddComponent<Olca.Presentation.Dungeon.CustomizationLoadoutView>();
            customizationEditorView.Initialize(font, SaveCustomizationEditor, CloseCustomizationEditor, RetryCustomizationEditorSave);
            customizationEditorButton = MakeButton("OpenCustomizationEditor", "ソウビ・パーツ", pauseCanvas.transform, Vector2.one,new Vector2(-310,-160));
            ((RectTransform)customizationEditorButton.transform).sizeDelta = new Vector2(300,60);
            customizationEditorButton.onClick.AddListener(OpenCustomizationEditor); customizationEditorButton.gameObject.SetActive(false);
            var unlockPanel=new GameObject("CustomizationUnlocks",typeof(RectTransform));unlockPanel.transform.SetParent(pauseCanvas.transform,false);
            customizationUnlockView=unlockPanel.AddComponent<Olca.Presentation.Dungeon.CustomizationUnlockView>();customizationUnlockView.Initialize(font,ApplyCustomizationUnlockFromUi,CloseCustomizationUnlocks,RetryCustomizationUnlockSave);
            var acquisitionPanel=new GameObject("CustomizationAcquisitions",typeof(RectTransform));acquisitionPanel.transform.SetParent(pauseCanvas.transform,false);
            customizationAcquisitionView=acquisitionPanel.AddComponent<Olca.Presentation.Dungeon.CustomizationAcquisitionView>();customizationAcquisitionView.Initialize(font,ClaimCustomizationAcquisition,CloseCustomizationAcquisitions,RetryCustomizationAcquisition);
            customizationAcquisitionButton=MakeButton("OpenCustomizationAcquisitions","ソウビのウケトリ",pauseCanvas.transform,Vector2.one,new Vector2(-310,-310));
            ((RectTransform)customizationAcquisitionButton.transform).sizeDelta=new Vector2(300,60);customizationAcquisitionButton.onClick.AddListener(OpenCustomizationAcquisitions);customizationAcquisitionButton.gameObject.SetActive(false);
            customizationUnlockButton=MakeButton("OpenCustomizationUnlocks","マスカイホウ",pauseCanvas.transform,Vector2.one,new Vector2(-310,-235));
            ((RectTransform)customizationUnlockButton.transform).sizeDelta=new Vector2(300,60);customizationUnlockButton.onClick.AddListener(OpenCustomizationUnlocks);customizationUnlockButton.gameObject.SetActive(false);
            clearReturnView.ConfigureMenuNavigation(()=>new[]{cardCollectionButton,manualSaveButton,newGameButton,customizationEditorButton,customizationUnlockButton},owned=>ResolveOwnedDefinition(owned,out var resolved)?resolved:null);
        }
        private Button manualSaveButton,newGameButton,progressConfirmButton,progressCancelButton;
        private GameObject progressActionOverlay;
        private Text progressActionMessage;
        private bool progressDialogOpen,newGameConfirmation,newGameSavePending,manualSavePending;
        private long confirmedProgressRevision;
        private bool IsProgressHub => !CustomizationAcquisitionOpen && !CustomizationEditorOpen && !CustomizationUnlockOpen && !DialogueOpen && !AudioSettingsOpen && clearReturnVisible && !collectionOpen && !ownedDepartureEditing && !savedDeckMenuOpen &&
            !exchangeOpen && !styleSelectionOpen && !cardCollectionOpen && !departureWriting && !progressStartupBlocked &&
            clearReturnSession!=null&&clearReturnSession.Ready&&(initialHub||clearReturnSession.Snapshot.Flow?.Stage==ClearReturnStage.Returned)&&
            clearReturnSession.Snapshot.NextDeparture?.ParentRunId!=(clearReturnSession.Snapshot.Flow?.RunId??NextDepartureData.InitialParent);
        private bool CanStartNewGame => !progressStartupBlocked&&clearReturnSession!=null&&clearReturnSession.Ready&&
            (IsProgressHub||awaitingCheckpointChoice)&&!clearReturnSession.HasPendingSave;
        private void CreateProgressActions(Font font,System.Func<string,string,Transform,Vector2,Vector2,Button> MakeButton)
        {
            manualSaveButton=MakeButton("ManualSaveButton","保存（スロット1）",pauseCanvas.transform,new Vector2(0,1),new Vector2(260,-85));
            newGameButton=MakeButton("NewGameButton","はじめから",pauseCanvas.transform,new Vector2(0,1),new Vector2(580,-85));
            foreach(var button in new[]{manualSaveButton,newGameButton})((RectTransform)button.transform).sizeDelta=new Vector2(280,60);
            manualSaveButton.onClick.AddListener(SaveProgressFromUi);newGameButton.onClick.AddListener(RequestNewGame);
            progressActionOverlay=new GameObject("ProgressActionOverlay",typeof(RectTransform),typeof(Image));progressActionOverlay.transform.SetParent(pauseCanvas.transform,false);
            var rect=(RectTransform)progressActionOverlay.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            progressActionOverlay.GetComponent<Image>().color=UiLook.Surface;
            var obj=new GameObject("ProgressActionMessage",typeof(RectTransform),typeof(Text));obj.transform.SetParent(progressActionOverlay.transform,false);
            progressActionMessage=obj.GetComponent<Text>();progressActionMessage.font=font;progressActionMessage.fontSize=30;UiLook.Label(progressActionMessage);progressActionMessage.alignment=TextAnchor.MiddleCenter;
            progressActionMessage.rectTransform.sizeDelta=new Vector2(1500,430);progressActionMessage.rectTransform.anchoredPosition=new Vector2(0,120);
            progressConfirmButton=MakeButton("ConfirmNewGame","進行を初期化して開始",progressActionOverlay.transform,new Vector2(.5f,.5f),new Vector2(-300,-180));
            ((RectTransform)progressConfirmButton.transform).sizeDelta=new Vector2(500,80);progressConfirmButton.onClick.AddListener(ConfirmNewGame);
            progressCancelButton=MakeButton("CancelProgressAction","取消して戻る",progressActionOverlay.transform,new Vector2(.5f,.5f),new Vector2(300,-180));
            ((RectTransform)progressCancelButton.transform).sizeDelta=new Vector2(500,80);progressCancelButton.onClick.AddListener(()=>{
                if(newGameSavePending||manualSavePending)return;progressDialogOpen=false;progressActionOverlay.SetActive(false);
            });progressActionOverlay.SetActive(false);
        }
        private void RefreshProgressActions()
        {
            if(manualSaveButton==null)return;
            bool allowProgressActions=!CustomizationAcquisitionOpen&&!CustomizationEditorOpen&&!CustomizationUnlockOpen&&!progressDialogOpen&&!cardCollectionOpen&&!(clearReturnView?.HasMenuModal??false);
            manualSaveButton.gameObject.SetActive(allowProgressActions&&IsProgressHub);
            manualSaveButton.interactable=IsProgressHub&&!clearReturnSession.HasPendingSave;
            newGameButton.gameObject.SetActive(allowProgressActions&&(IsProgressHub||awaitingCheckpointChoice));newGameButton.interactable=CanStartNewGame;
            manualSaveButton.transform.SetAsLastSibling();newGameButton.transform.SetAsLastSibling();
            if(customizationEditorButton!=null){
                customizationEditorButton.gameObject.SetActive(allowProgressActions&&IsProgressHub);
                customizationEditorButton.interactable=customizationCatalog!=null&&!clearReturnSession.HasPendingSave;
                customizationEditorButton.GetComponentInChildren<Text>().text=customizationCatalog==null?"ソウビ・パーツ（ミトウロク）":"ソウビ・パーツ";
            }
            if(customizationUnlockButton!=null){customizationUnlockButton.gameObject.SetActive(allowProgressActions&&IsProgressHub);customizationUnlockButton.interactable=customizationCatalog!=null&&!clearReturnSession.HasPendingSave;}
            if(customizationAcquisitionButton!=null){customizationAcquisitionButton.gameObject.SetActive(allowProgressActions&&IsProgressHub&&customizationAcquisitionProvider!=null);customizationAcquisitionButton.interactable=customizationCatalog!=null&&!clearReturnSession.HasPendingSave;}
            if(progressDialogOpen)progressActionOverlay.transform.SetAsLastSibling();
        }
        private void ShowProgressAction(string message,bool confirm,bool canClose=true)
        {
            progressDialogOpen=true;progressActionMessage.text=message;progressActionOverlay.SetActive(true);progressActionOverlay.transform.SetAsLastSibling();
            progressConfirmButton.gameObject.SetActive(confirm);progressCancelButton.interactable=canClose;
            progressCancelButton.GetComponentInChildren<Text>().text=newGameConfirmation?"取消して戻る":"閉じる";
            progressCancelButton.navigation=confirm?new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=progressConfirmButton,selectOnRight=progressConfirmButton}:new Navigation{mode=Navigation.Mode.None};
            progressConfirmButton.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=progressCancelButton,selectOnRight=progressCancelButton};
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(canClose?progressCancelButton.gameObject:progressConfirmButton.gameObject);RefreshProgressActions();
        }
        private void SaveProgressFromUi()
        {
            if(progressDialogOpen||!IsProgressHub||clearReturnSession.HasPendingSave)return;
            newGameConfirmation=false;
            bool saved=clearReturnSession.ManualSave();
            manualSavePending=!saved&&clearReturnSession.HasPendingSave;
            ShowProgressAction(saved?"スロット1に保存しました。\n拠点の所持・成長・編成・図鑑を保存しました。\nダンジョンの途中状態は保存していません。":"保存できませんでした。スロット1の元データを保持しています。\n"+clearReturnSession.Error+"\n同じ内容で再試行してください。",manualSavePending,!manualSavePending);
            if(!saved)progressConfirmButton.GetComponentInChildren<Text>().text="同じ保存を再試行";
        }
        private void RequestNewGame()
        {
            if(progressDialogOpen||!CanStartNewGame)return;
            newGameConfirmation=true;confirmedProgressRevision=clearReturnSession.Snapshot.Revision;
            progressConfirmButton.GetComponentInChildren<Text>().text="進行を初期化して開始";
            ShowProgressAction("スロット1を「はじめから」にしますか？\n所持カード・保存デッキ・成長・通貨・図鑑・解放履歴・攻略進行を初期化し、\nスターター20枚で拠点から開始します。\n操作設定・演出設定・音量設定は保持します。\n実行前の進行文書と旧攻略データは復旧用に保全します。",true);
        }
        private void ConfirmNewGame()
        {
            if(!progressDialogOpen)return;
            if(manualSavePending){if(clearReturnSession.Retry()){manualSavePending=false;ShowProgressAction("スロット1に保存しました。",false);}else ShowProgressAction("保存を再試行できませんでした。\n"+clearReturnSession.Error,true,false);return;}
            if(!newGameConfirmation)return;
            bool saved=newGameSavePending?clearReturnSession.Retry():CanStartNewGame&&clearReturnSession.StartNewGame(confirmedProgressRevision,CreateCardFactory().CreateStarterDeck().GetAllCards());
            if(!saved){newGameSavePending=clearReturnSession.HasPendingSave;ShowProgressAction("新規開始の保存が完了していません。元の進行と同じ再試行計画を保持しています。\n"+clearReturnSession.Error,true,!newGameSavePending);progressConfirmButton.GetComponentInChildren<Text>().text="同じ新規開始を再試行";return;}
            newGameSavePending=false;newGameConfirmation=false;progressDialogOpen=false;progressActionOverlay.SetActive(false);
            manager.SuspendForCheckpoint();combatFeedbackAudio?.Bind(null);battleSession?.Detach();transitionCts?.Cancel();battleView?.gameObject.SetActive(false);gameFieldView?.OnBattleEnded();
            awaitingCheckpointChoice=false;checkpointOverlay.SetActive(false);pauseOverlay.SetActive(false);initialHub=true;clearReturnVisible=true;
            dungeonSelectionOpen=false;selectedDungeon=selectionBeforeOpen=0;floorSwapPending=false;clearReturnError=null;nonClearReturnRequest=null;
            logicalLoop.StartNewTimeline();ResetTouchInput();ShowDungeonSelection();
        }
        private ClearReturnView cardCollectionView;
        private UnityEngine.UI.Button cardCollectionButton;
        private bool cardCollectionOpen, collectionWasPaused;
        private void OpenCardCollection()
        {
            if(progressDialogOpen||cardCollectionOpen||manager==null||!clearReturnSession.Ready)return;
            cardCollectionOpen=true;collectionWasPaused=IsUserPaused;SetUserPaused(true);pauseOverlay.SetActive(false);
            clearReturnSession.ConfigureCollectionDefinitions(CurrentCollectionDefinitions());
            clearReturnSession.EnsureCollection();ShowCardCollection();
        }
        private IReadOnlyList<Card> CurrentCollectionDefinitions() => CreateCardFactory().CollectionCards
            .Concat(memoryCheckpoints?.Definitions.CollectionDefinitions??System.Array.Empty<Card>())
            .Concat(configuredDepartureDeck?.GetAllCards()??System.Array.Empty<Card>()).GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First()).ToArray();
        private void ShowCardCollection()
        {
            var p=clearReturnSession.Snapshot;var cards=CurrentCollectionDefinitions();
            var rows=CollectionDisplay.CardRows(p,cards);
            var catalog=cards.Where(c=>c.ExceedLevel!=ExceedLevel.Lv3).GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First()).ToArray();
            var details=catalog.Select(c=>CollectionDisplay.IsKnown(p,c,catalog)?c:null).Concat(Enumerable.Repeat<Card>(null,rows.Length-catalog.Length)).ToArray();
            cardCollectionView.RenderCollection(rows,CollectionDisplay.RecipeRows(p,new SimpleFusionRecipeProvider(CreateCardFactory()).CollectionRecipes,cards),
                clearReturnSession.HasPendingSave,clearReturnSession.Error,CloseCardCollection,()=>{clearReturnSession.Retry();ShowCardCollection();},
                card=>customScreenView!=null?customScreenView.GetCardIcon(card.Id):null,cardDetails:details);
            cardCollectionView.transform.SetAsLastSibling();
        }
        private void CloseCardCollection()
        {
            if(clearReturnSession.HasPendingSave)return;
            cardCollectionView.gameObject.SetActive(false);cardCollectionOpen=false;SetUserPaused(collectionWasPaused);
            pauseOverlay.SetActive(collectionWasPaused);
        }
        private void RecordFusionDiscovery(IReadOnlyList<Card> materials,Card output)
        {
            var recipe=new SimpleFusionRecipeProvider(CreateCardFactory()).CollectionRecipes.FirstOrDefault(r=>r.Matches(materials,output));
            clearReturnSession.ConfigureCollectionDefinitions(CurrentCollectionDefinitions());
            if(recipe!=null&&!clearReturnSession.RecordFusion(recipe.Id))OpenCardCollection();
        }
        private ClearReturnFileStore clearReturnStore;
        private ClearReturnSession clearReturnSession;
        private ClearReturnView clearReturnView;
        private bool clearReturnVisible, progressStartupBlocked, collectionOpen, departureWriting;
        private CheckpointFileIo nextDepartureIo;
        private CheckpointDefinitions nextDepartureDefinitions;
        private string ProgressCheckpointRoot => clearReturnSession?.Snapshot?.CheckpointGeneration==null?CheckpointDirectory:
            System.IO.Path.Combine(CheckpointDirectory,"profiles",clearReturnSession.Snapshot.CheckpointGeneration);
        public string ActiveCheckpointDirectory => clearReturnSession?.Snapshot?.NextDeparture == null ? ProgressCheckpointRoot :
            System.IO.Path.Combine(ProgressCheckpointRoot, "runs", clearReturnSession.Snapshot.NextDeparture.Start.RunId);
        private void OpenOwnedCards()
        {
            if (!IsUserPaused || HasIncompleteReturn) return;
            collectionOpen = true; pauseOverlay.SetActive(false); clearReturnView.Render(clearReturnSession.Snapshot, clearReturnSession.Snapshot?.Flow, false, null, true);
        }
        private void CloseOwnedCards() { collectionOpen = false; clearReturnView.gameObject.SetActive(false); pauseOverlay.SetActive(IsUserPaused); }

        private bool ownedDepartureEditing;
        private bool savedDeckMenuOpen, savedDeckDirty;
        private string editingSavedDeckId, editingSavedDeckName;
        private SavedDepartureDeck[] SavedDecks => clearReturnSession.Snapshot.SavedDecks??new SavedDepartureDeck[0];
        private void OpenSavedDeckMenu(){if(!CanEditOwnedDeparture)return;clearReturnSession.ConfigureCollectionDefinitions(CurrentCollectionDefinitions());savedDeckMenuOpen=true;ShowSavedDeckMenu();}
        private void ShowSavedDeckMenu(string deleting=null)
        {
            ownedDepartureEditing=false;
            clearReturnView.RenderSavedDeckMenu(clearReturnSession.Snapshot, clearReturnSession.Error??ownedDepartureError,
                !clearReturnSession.Ready||clearReturnSession.HasPendingSave,()=>EditSavedDeck(null),EditSavedDeck,CloneSavedDeck,EquipSavedDeck,
                id=>ShowSavedDeckMenu(id),()=>{savedDeckMenuOpen=false;ShowClearReturn();},RetryClearReturn,
                deleting,()=>{clearReturnSession.DeleteDeck(deleting);ShowSavedDeckMenu();},ReloadSavedDeckMenu);
        }
        private void ReloadSavedDeckMenu()
        {
            if(!CanEditOwnedDeparture)return;
            ownedDepartureError=null;clearReturnSession.Reload();ShowSavedDeckMenu();
        }
        private void EditSavedDeck(string id)
        {
            if(!CanEditOwnedDeparture)return;
            OpenOwnedDeparture();savedDeckMenuOpen=true;
            var saved=SavedDecks.SingleOrDefault(d=>d.Id==id);
            editingSavedDeckId=saved?.Id??System.Guid.NewGuid().ToString("N");editingSavedDeckName=saved?.Name??("デッキ "+(SavedDecks.Length+1));
            ownedDepartureDraft.Clear();
            ownedDepartureKeys.Clear();
            if(saved!=null)foreach(var slot in saved.Cards){
                var owned=OwnedDefinition.Resolve(clearReturnSession.Snapshot,slot);Card card=null;
                if(owned!=null)ResolveOwnedDefinition(owned,out card);
                var restored=card?.Clone()??new Card(slot.Id,"未解決: "+slot.Id,0,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,0,"",exceedLevel:slot.Level);
                ownedDepartureDraft.Add(restored);ownedDepartureKeys[restored]=owned!=null?OwnedDefinition.Key(owned):slot.DefinitionKey;
            }
            savedDeckDirty=false;ownedDepartureError=null;ShowOwnedDeparture();
        }
        private void SaveEditingDeck()
        {
            if(!CanEditOwnedDeparture||editingSavedDeckId==null)return;
            if(clearReturnSession.SaveDeck(editingSavedDeckId,editingSavedDeckName,ownedDepartureDraft.Select(c=>new SavedDeckCard{Id=c.Id,Level=c.ExceedLevel,DefinitionKey=DraftDefinitionKey(c)}).ToArray())){
                var saved=SavedDecks.Single(d=>d.Id==editingSavedDeckId);
                for(int n=0;n<ownedDepartureDraft.Count;n++)ownedDepartureKeys[ownedDepartureDraft[n]]=saved.Cards[n].DefinitionKey;
                savedDeckDirty=false;ownedDepartureError=null;
            }else ownedDepartureError=clearReturnSession.Error;
            ShowOwnedDeparture();
        }
        private bool TryResolveSavedDeck(SavedDepartureDeck saved,out Deck deck,out string error)
        {
            deck=null;if(!SavedDepartureDeck.TryValidateEquipped(clearReturnSession.Snapshot,saved,out error))return false;
            var cards=new List<Card>();
            foreach(var slot in saved.Cards){var owned=OwnedDefinition.Resolve(clearReturnSession.Snapshot,slot);
                if(!ResolveOwnedDefinition(owned,out var card)){error="保存デッキの定義が未特定です。別定義へ置換せず保存を保持しています";return false;}cards.Add(card);}
            return OwnedDefinition.TryCreate(clearReturnSession.Snapshot,cards,saved.Cards,out deck,out error);
        }

        private void EquipSavedDeck(string id)
        {
            if(!CanEditOwnedDeparture)return;
            if(TryResolveSavedDeck(SavedDecks.SingleOrDefault(d=>d.Id==id),out _,out ownedDepartureError))clearReturnSession.EquipDeck(id);
            ShowSavedDeckMenu();
        }
        private void CloneSavedDeck(string id)
        {
            if(!CanEditOwnedDeparture)return;var saved=SavedDecks.SingleOrDefault(d=>d.Id==id);if(saved==null)return;
            clearReturnSession.SaveDeck(System.Guid.NewGuid().ToString("N"),saved.Name+" の複製",saved.Cards);ShowSavedDeckMenu();
        }
        private readonly List<Card> ownedDepartureDraft = new List<Card>();
        private readonly Dictionary<Card,string> ownedDepartureKeys = new Dictionary<Card,string>();
        private string DraftDefinitionKey(Card card) => ownedDepartureKeys.TryGetValue(card,out var key)?key:CollectionDisplay.DefinitionHash(card);
        private bool ResolveOwnedDefinition(OwnedCardCount owned,out Card card)
        {
            card=null;if(owned==null)return false;
            var matches=CurrentCollectionDefinitions().Where(c=>OwnedDefinition.CardMatches(owned,c)).GroupBy(CollectionDisplay.DefinitionHash).Select(g=>g.First()).ToArray();
            if(matches.Length!=1)return false;card=matches[0].Clone();return true;
        }
        private Card[] ownedDepartureOptions;
        private string ownedDepartureError;
        private bool CanEditOwnedDeparture => !exchangeOpen && !styleSelectionOpen && clearReturnVisible && !collectionOpen && !departureWriting && !progressStartupBlocked &&
            clearReturnSession.Ready && !clearReturnSession.HasPendingSave && clearReturnSession.Snapshot.Flow?.Stage == ClearReturnStage.Returned &&
            clearReturnSession.Snapshot.NextDeparture?.ParentRunId != clearReturnSession.Snapshot.Flow.RunId;
        private void OpenOwnedDeparture()
        {
            if (!CanEditOwnedDeparture) return;
            clearReturnSession.ConfigureCollectionDefinitions(CurrentCollectionDefinitions());
            editingSavedDeckId=null;savedDeckDirty=false;
            ownedDepartureOptions = clearReturnSession.Snapshot.Owned.Select(o=>ResolveOwnedDefinition(o,out var card)?card:null).ToArray();
            ownedDepartureDraft.Clear();ownedDepartureKeys.Clear();ownedDepartureEditing = true; ownedDepartureError = null; ShowOwnedDeparture();
        }
        private bool TryOwnedDepartureDeck(out Deck deck, out string error)
        {
            deck=null;
            if(ownedDepartureDraft.Any(c=>!ownedDepartureOptions.Any(o=>o!=null&&CollectionDisplay.DefinitionHash(o)==CollectionDisplay.DefinitionHash(c)))){error="カード定義が未解決です";return false;}
            return OwnedDefinition.TryCreate(clearReturnSession.Snapshot,ownedDepartureDraft,ownedDepartureDraft.Select(c=>new SavedDeckCard{Id=c.Id,Level=c.ExceedLevel,DefinitionKey=DraftDefinitionKey(c)}).ToArray(),out deck,out error);
        }

        private void ShowOwnedDeparture(bool confirmDiscard = false)
        {
            TryOwnedDepartureDeck(out _, out var validation);
            clearReturnView.RenderOwnedDeparture(clearReturnSession.Snapshot, ownedDepartureOptions, ownedDepartureDraft,
                ownedDepartureError ?? validation, validation == null, ChangeOwnedDeparture, ConfirmOwnedDeparture,
                CloseOwnedDeparture, () => ShowOwnedDeparture(), confirmDiscard, editingSavedDeckId==null?null:SaveEditingDeck,
                editingSavedDeckName, name=>{editingSavedDeckName=name;savedDeckDirty=true;}, clearReturnSession.HasPendingSave, RetryClearReturn,ownedDepartureDraft.Select(DraftDefinitionKey).ToArray());
        }
        private void ChangeOwnedDeparture(int index, int delta)
        {
            if (!ownedDepartureEditing || !CanEditOwnedDeparture || index < 0 || index >= ownedDepartureOptions.Length) return;
            var card = ownedDepartureOptions[index]; if (delta>0&&card == null) return;
            if (delta > 0) {
                int count = ownedDepartureDraft.Count(c => DraftDefinitionKey(c)==OwnedDefinition.Key(clearReturnSession.Snapshot.Owned[index]));
                if (editingSavedDeckId==null && (count >= clearReturnSession.Snapshot.Owned[index].Count || ownedDepartureDraft.Count >= 20)) return;
                var copy=card.Clone();ownedDepartureDraft.Add(copy);ownedDepartureKeys[copy]=OwnedDefinition.Key(clearReturnSession.Snapshot.Owned[index]);
            } else {
                int found = ownedDepartureDraft.FindLastIndex(c => DraftDefinitionKey(c)==OwnedDefinition.Key(clearReturnSession.Snapshot.Owned[index]));
                if (found >= 0) {ownedDepartureKeys.Remove(ownedDepartureDraft[found]);ownedDepartureDraft.RemoveAt(found);}
            }
            savedDeckDirty=true;ownedDepartureError = null; ShowOwnedDeparture();
        }
        private void CloseOwnedDeparture(bool discard)
        {
            if (!ownedDepartureEditing || !CanEditOwnedDeparture) return;
            if (!discard && (editingSavedDeckId!=null?savedDeckDirty:ownedDepartureDraft.Count>0)) { ShowOwnedDeparture(true); return; }
            ownedDepartureDraft.Clear(); ownedDepartureEditing = false; editingSavedDeckId=null;
            if(savedDeckMenuOpen)ShowSavedDeckMenu();else ShowClearReturn();
        }
        private void ConfirmOwnedDeparture()
        {
            if (!ownedDepartureEditing || !CanEditOwnedDeparture || editingSavedDeckId!=null) return;
            if (!TryOwnedDepartureDeck(out var deck, out ownedDepartureError)) { ShowOwnedDeparture(); return; }
            ownedDepartureEditing = false; StartNextDepartureWithDeck(deck);
        }

        private bool endingRun;
        private RunReturnOutcome? nonClearReturnRequest;
        private GameObject retreatConfirmation;
        private bool TryReadCurrentSavedRun(out RestoredCheckpoint source, out string key, out string error)
        {
            source = null; key = null; error = null;
            if (floorDefinitions != null) {
                var data = floorStore.Read(); if (data == null) { error = floorStore.ReadError; return false; }
                if (!floorDefinitions.TryRestore(data, out var restored, out error)) return false;
                source = restored.State; key = floorDefinitions.RouteId; return true;
            }
            var dto = memoryCheckpoints?.Store.Read();
            if (dto == null) { error = "攻略保存を読み込めません"; return false; }
            key = dto.DungeonId; return CheckpointCodec.TryRestore(dto, memoryCheckpoints.Definitions, out source, out error);
        }
        private void RequestRetreat()
        {
            if (!IsUserPaused || clearReturnVisible || endingRun || manager.IsSuspended || manager.Dungeon.IsCheckpointBlocked ||
                manager.CurrentPhase == GamePhase.GameOver || manager.CurrentPhase == GamePhase.DungeonCleared) return;
            pauseOverlay.SetActive(false); retreatConfirmation.SetActive(true);
        }
        private void ConfirmRetreat()
        {
            if (!IsUserPaused || retreatConfirmation == null || !retreatConfirmation.activeSelf || clearReturnVisible) return;
            EndRunWithoutRewards(RunReturnOutcome.Retreated);
        }
        private void EndRunWithoutRewards(RunReturnOutcome outcome)
        {
            StopGamepadVibration();
            if (endingRun || clearReturnSession == null || !clearReturnSession.Ready) return;
            endingRun = true; nonClearReturnRequest = outcome;
            try {
                if (!TryReadCurrentSavedRun(out var source, out var key, out var error)) { clearReturnError = error; ShowClearReturn(); return; }
                if (outcome == RunReturnOutcome.Retreated && !manager.IsSuspended && !manager.SuspendForCheckpoint()) return;
                if (IsUserPaused) SetUserPaused(false);
                transitionCts?.Cancel(); combatFeedbackAudio?.Bind(null);battleSession?.Detach(); cardSelectionView?.Hide();
                battleView?.EndSessionPresentation();
                battleView?.gameObject.SetActive(false);
                gameFieldView?.OnBattleEnded(); battleFieldReady = false;
                manager.Dungeon.DiscardRunCurrency();
                currencyChoice?.TrySetCanceled(); currencyOverlay?.SetActive(false);
                itemRewardChoice?.TrySetCanceled();itemRewardOverlay?.SetActive(false);
                pendingCardChoices = null; floorSwapPending = false; basicAttackInput.Reset(); queuedBasicAttackTap = false;
                clearReturnError = null;
                if (!clearReturnSession.EndWithoutRewards(source, key, ReturnSettings(), outcome) && !clearReturnSession.HasPendingSave) clearReturnError = clearReturnSession.Error;
                ShowClearReturn();
            } finally { endingRun = false; }
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
            gameplayAudio?.PlayBgm(PlaceholderBgm.Exploration(dungeonBgm));
            if (exchangeOpen) { ShowTokenExchange(); return; }
            if (styleSelectionOpen) { ShowStyleSelection(); return; }
            if (initialHub) { ShowDungeonSelection(); return; }
            clearReturnVisible = true; transitionCts?.Cancel(); retreatConfirmation?.SetActive(false);
            checkpointOverlay?.SetActive(false); pauseOverlay?.SetActive(false); keyboardSettingsView?.DiscardAndClose();
            clearReturnView.Render(clearReturnSession.Snapshot, clearReturnSession.DisplayFlow,
                !clearReturnSession.Ready || clearReturnSession.HasPendingSave || progressStartupBlocked || clearReturnError != null,
                clearReturnError ?? clearReturnSession.Error, dungeonName: (dungeonSelectionEnabled ? dungeonChoices[selectedDungeon].Label : "") + " / " + (SelectedStyle?.Name ?? "未登録スタイル"));
        }
        private void ResumeClearReturnIfNeeded()
        {
            if (!clearReturnSession.Ready) { ShowClearReturn(); return; }
            if (initialHub) { clearReturnVisible = true; ShowDungeonSelection(); return; }
            if(clearReturnSession.Snapshot.Flow?.Stage==ClearReturnStage.Returned&&clearReturnSession.Snapshot.NextDeparture==null){ShowClearReturn();return;}
            if (clearReturnSession.Snapshot.NextDeparture != null && !awaitingCheckpointChoice && !HasIncompleteReturn) {
                if (!ActivateNextDeparture(out var startError)) { clearReturnError = startError; ShowClearReturn(); }
                return;
            }
            if (HasIncompleteReturn) { ShowClearReturn(); return; }
            if (awaitingCheckpointChoice && TryReadCurrentSavedRun(out var ended, out var endedKey, out var endedError)) {
                var flow = clearReturnSession.Snapshot.Flow;
                if (flow != null && flow.Outcome != RunReturnOutcome.Cleared && flow.RunId == ended.Snapshot.RunId) { ShowClearReturn(); return; }
                if (clearReturnSession.Snapshot.Receipts.Any(r => r.RunId == ended.Snapshot.RunId && r.Outcome != RunReturnOutcome.Cleared)) {
                    clearReturnError = "終了済みの攻略は再開できません。既存保存は保持しています。"; ShowClearReturn(); return;
                }
                if (ended.Snapshot.Boundary == CheckpointBoundary.Terminal && ended.Snapshot.Player.Hp == 0) {
                    clearReturnSession.EndWithoutRewards(ended, endedKey, ReturnSettings(), RunReturnOutcome.Defeated); ShowClearReturn(); return;
                }
            }
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
            if(savedDeckMenuOpen){clearReturnSession.Retry();if(!clearReturnSession.HasPendingSave){savedDeckDirty=false;editingSavedDeckId=null;ownedDepartureError=null;}ShowSavedDeckMenu();return;}
            if (!clearReturnSession.Retry()) { if (initialHub) ShowDungeonSelection(); else ShowClearReturn(); return; }
            if (initialHub && clearReturnSession.Snapshot.NextDeparture != null) { if (!ActivateNextDeparture(out clearReturnError)) ShowDungeonSelection(); return; }
            if (initialHub) { ShowDungeonSelection(); return; }
            if (!progressStartupBlocked && clearReturnSession.Snapshot.NextDeparture?.ParentRunId == clearReturnSession.Snapshot.Flow?.RunId &&
                clearReturnSession.Snapshot.Flow?.Stage == ClearReturnStage.Returned) {
                if (!ActivateNextDeparture(out clearReturnError)) ShowClearReturn();
                return;
            }
            if (clearReturnSession.Snapshot.Flow?.Outcome != RunReturnOutcome.Cleared && clearReturnSession.Snapshot.Flow != null && !clearReturnSession.HasPendingSave) {
                clearReturnError = null; ShowClearReturn(); return;
            }
            if (nonClearReturnRequest.HasValue) { EndRunWithoutRewards(nonClearReturnRequest.Value); return; }
            if (progressStartupBlocked && clearReturnSession.Ready) clearReturnError = "進行データを読み込めました。ゲームを再起動して再開してください。";
            else if (clearReturnError != null && clearReturnSession.Ready && !clearReturnSession.HasPendingSave) { BeginClearReturn(); return; }
            ShowClearReturn();
        }

        private void StartNextDeparture() => StartNextDepartureWithDeck(null);
        private void StartNextDepartureWithDeck(Deck ownedDeck)
        {
            if (progressDialogOpen || exchangeOpen || styleSelectionOpen || dungeonSelectionOpen || departureWriting || collectionOpen || !clearReturnVisible || progressStartupBlocked || !clearReturnSession.Ready ||
                (!initialHub && clearReturnSession.Snapshot.Flow?.Stage != ClearReturnStage.Returned)) return;
            departureWriting = true;
            try {
                if (clearReturnSession.HasPendingSave) { ShowClearReturn(); return; }
                if (clearReturnSession.Snapshot.NextDeparture?.ParentRunId != (clearReturnSession.Snapshot.Flow?.RunId ?? NextDepartureData.InitialParent)) {
                    var choice = dungeonChoices[selectedDungeon];
                    if (dungeonSelectionEnabled && !DungeonUnlocked(selectedDungeon)) { clearReturnError = "未解放のダンジョンです"; ShowClearReturn(); return; }
                    var style = SelectedStyle;
                    if (style == null || !style.IsUnlocked(clearReturnSession.Snapshot)) { clearReturnError = "未登録または未解放のスタイルです。保存を保持しています。"; ShowClearReturn(); return; }
                    var map = choice.Map();
                    if (choice.Floors == null) map.ValidateFinalFloorForDeparture();
                    Deck deck=ownedDeck;
                    if(deck==null&&clearReturnSession.Snapshot.EquippedDeckId!=null&&!TryResolveSavedDeck(SavedDecks.SingleOrDefault(d=>d.Id==clearReturnSession.Snapshot.EquippedDeckId),out deck,out clearReturnError)){ShowClearReturn();return;}
                    deck=deck??CreateInitialDeck();
                    // 同じ固定カタログで捕捉し、編成枚数だけで定義hashを変更しない。
                    var frozen = choice.Floors?.CreateDepartureSession() ?? new DungeonCheckpointSession(choice.Single, new MemoryCheckpointStore(choice.Single));
                    var level = clearReturnSession.Snapshot.Level; var settings = ReturnSettings();
                    var player = new PlayerCharacter(settings.MaxHp(level), attackPower: settings.Attack(level),
                        staggerDurationFrames: battleStaggerFrames, poiseRecoveryDelayFrames: playerPoiseRecoveryFrames);
                    player.SetAttackGrowth(AttackMilestoneGrowth.ForLevel(level));
                    player.SetDepartureStyle(style);
                    ApplyDepartureCustomization(player);
                    new DungeonBattleManager(map, player, deck, randomProvider, logger, checkpoints: frozen, enemyAIProvider: enemyAIProvider,evaluationRewards:evaluationRewardProvider);
                    var start = frozen.Store.Read();
                    if (!ValidateDeparture(start, choice, out clearReturnError) || !clearReturnSession.PrepareNextDeparture(start, choice.Key)) {
                        ShowClearReturn(); return;
                    }
                }
                if (!ActivateNextDeparture(out clearReturnError)) ShowClearReturn();
            } catch (System.ArgumentException e) { clearReturnError = e.Message; ShowClearReturn(); }
              catch (System.OverflowException e) { clearReturnError = e.Message; ShowClearReturn(); }
            finally { departureWriting = false; }
        }
        private bool ValidateDeparture(CheckpointDto start, DungeonChoice choice, out string error)
        {
            if (choice.Floors != null) return choice.Floors.TryRestore(new FloorSequenceCheckpointDto {
                RouteId = choice.Floors.RouteId, Revision = choice.Floors.Revision, RouteHash = choice.Floors.RouteHash,
                FloorIndex = 0, State = start }, out _, out error);
            return CheckpointCodec.TryRestore(start, choice.Single, out _, out error);
        }
        private bool ActivateNextDeparture(out string error)
        {
            error = null; var plan = clearReturnSession.Snapshot.NextDeparture;
            var choice = plan == null ? null : dungeonChoices.Concat(knownLegacyDungeons).FirstOrDefault(c => c.Key == plan.DefinitionKey && ValidateDeparture(plan.Start,c,out _));
            if (choice == null || !ValidateDeparture(plan.Start, choice, out error)) {
                error = error ?? "次回出撃の定義が一致しません。既存保存は変更していません。"; return false;
            }
            DungeonBattleManager next; DungeonCheckpointSession session; FloorSequenceSession route = null; FloorSequenceFileStore routeStore = null;
            if (choice.Floors != null) {
                routeStore = new FloorSequenceFileStore(choice.Floors, ActiveCheckpointDirectory, nextDepartureIo);
                if (!routeStore.HasExistingData && !routeStore.TryWrite(new FloorSequenceCheckpointDto {
                    RouteId = choice.Floors.RouteId, Revision = choice.Floors.Revision, RouteHash = choice.Floors.RouteHash,
                    FloorIndex = 0, State = plan.Start }, out error)) return false;
                route = new FloorSequenceSession(choice.Floors, routeStore);
                if (!route.TryLoad(out error) || route.Current.State.Snapshot.RunId != plan.Start.RunId) { error = error ?? "攻略IDが一致しません"; return false; }
                if (route.Current.State.Snapshot.Boundary == CheckpointBoundary.Terminal) { error = "終了済み攻略を上書きしません"; return false; }
                next = CreateFloorManager(route, out session, choice.Floors);
            } else {
                var defs = choice.Single;
                var store = new FileCheckpointStore(defs, ActiveCheckpointDirectory, nextDepartureIo);
                if (!store.HasExistingData && !store.TryWrite(plan.Start, out error)) return false;
                var saved = store.Read(); if (saved == null || saved.RunId != plan.Start.RunId) { error = store.ReadError ?? "攻略IDが一致しません"; return false; }
                session = new DungeonCheckpointSession(defs, store); var f = CreateCardFactory();
                if (!DungeonBattleManager.TryRestoreCheckpoint(session, randomProvider, logger, out next, out error,
                    new ExceedSystem(f), new TuningSystem(f.GetTuningDefinition), new FusionSystem(new SimpleFusionRecipeProvider(CreateCardFactory()), f), f, enemyAIProvider: enemyAIProvider,evaluationRewards:evaluationRewardProvider)) return false;
            }
            // 新攻略の保存と復元が成功してから、旧帰還画面・モデルを交換する。
            if (manager.CurrentPhase != GamePhase.DungeonCleared && manager.CurrentPhase != GamePhase.GameOver) manager.SuspendForCheckpoint();
            floorDefinitions = choice.Floors; nextDepartureDefinitions = choice.Single; selectedDungeon = System.Math.Max(0,dungeonChoices.IndexOf(choice));
            initialHub = false; dungeonSelectionOpen = false; floorSession = route; floorStore = routeStore; CurrentFloorIndex = route?.Current.FloorIndex ?? 0; floorSwapPending = false;
            clearReturnVisible = false; clearReturnError = null; nonClearReturnRequest = null; clearReturnView.gameObject.SetActive(false); checkpointOverlay.SetActive(false);
            ReplaceManager(next, session); return true;
        }

        private CancellationTokenSource transitionCts;
        [SerializeField, Min(0)] private int currencyPerEnemy = 10;
        private GameObject currencyOverlay;
        private GameObject itemRewardOverlay;
        private Text itemRewardText;
        private UniTaskCompletionSource<bool> itemRewardChoice;
        private Text currencyRewardText, currencyBalanceText;
        private Button receiveCurrencyButton;
        private MenuPadScope resourceRewardMenu;
        private MenuPadScope systemMenu;private Transform systemMenuRoot;private string pauseFocus="ResumeButton";
        private void RefreshSystemMenu()
        {
            GameObject root=null;string preferred=null;System.Action cancel=null;
            Button Find(GameObject panel,string name)=>panel.transform.Find(name)?.GetComponent<Button>();
            if(progressDialogOpen&&progressActionOverlay.activeInHierarchy){root=progressActionOverlay;preferred=progressCancelButton.IsInteractable()?"CancelProgressAction":"ConfirmNewGame";cancel=()=>{if(progressCancelButton.IsInteractable())progressCancelButton.onClick.Invoke();};}
            else if(retreatConfirmation!=null&&retreatConfirmation.activeInHierarchy){root=retreatConfirmation;preferred="CancelRetreat";cancel=()=>Find(root,"CancelRetreat").onClick.Invoke();}
            else if(checkpointOverlay!=null&&checkpointOverlay.activeInHierarchy){root=checkpointOverlay;preferred=manager.Dungeon.IsCheckpointBlocked?"RetryCheckpointButton":"ContinueCheckpointButton";}
            else if(pauseOverlay!=null&&pauseOverlay.activeInHierarchy&&IsUserPaused){root=pauseOverlay;preferred=pauseFocus;cancel=()=>SetUserPaused(false);}
            var next=root!=null?root.transform:null;
            if(next!=systemMenuRoot){if(pauseOverlay!=null&&systemMenuRoot==pauseOverlay.transform&&systemMenu?.FocusName!=null)pauseFocus=systemMenu.FocusName;systemMenu?.Suspend();systemMenuRoot=next;if(next!=null){if(systemMenu==null)systemMenu=new MenuPadScope(()=>MenuPadScope.IsTopmost(systemMenuRoot),()=>systemMenuRoot==checkpointOverlay?.transform?new[]{newGameButton}:System.Array.Empty<Button>());systemMenu.Bind(next,preferred,cancel);}}
            systemMenu?.Tick();
        }
        private Transform resourceRewardRoot;
        private void BindResourceReward(GameObject root,string preferred,System.Action skip)
        {
            resourceRewardRoot=root.transform;
            if(resourceRewardMenu==null)resourceRewardMenu=new MenuPadScope(()=>!GameplayPause.InputBlocked&&MenuPadScope.IsTopmost(resourceRewardRoot));
            resourceRewardMenu.Bind(root.transform,preferred,skip);
        }
        private UniTaskCompletionSource<bool> currencyChoice;
        private long? pendingBattleElapsedFrames;
        private Olca.Core.Logic.Battle.BattleEvaluationResult pendingBattleEvaluation;
        private IReadOnlyList<Card> pendingCardChoices; // カード報酬フロー用

        // 未設定だった旧既定マップも、同じ配置で検証してから再開する。
        private DungeonConfig CreateDefaultDungeon(bool legacy = false)
        {
            var map=CreateDefaultDungeonWithoutTerrain(legacy);
            if(legacy)return map;
            var first=map.MapCells.FirstOrDefault(c=>c.Type==CellType.Enemy);
            if(first==null)return map;
            int ice=first.Index+6;
            if(ice+1>=map.TotalCellCount-1||Enumerable.Range(ice,2).Any(i=>map.MapCells[i].Type!=CellType.Empty||map.Visits.Any(v=>v.Cell==i)))return map;
            // 回復イベントの後に、属性/被害の新定義が不要な既存氷だけを登録する。
            return new DungeonConfig(map.DungeonId,map.DungeonName,map.TotalCellCount,map.MapCells,map.InitialDeckSize,
                map.Visits,map.StrongEnemyCells,map.ItemRewards,map.ItemRewardChance,
                new[]{new ExplorationTerrainCell(ice,PanelType.Ice),new ExplorationTerrainCell(ice+1,PanelType.Ice)});
        }
        private DungeonConfig CreateDefaultDungeonWithoutTerrain(bool legacy = false)
        {
            var map = CreateDefaultDungeonWithoutItemRewards(legacy);
            if (legacy) return map;
            var first = map.MapCells.FirstOrDefault(c => c.Type == CellType.Enemy && c.Enemy.EnemyId != "boss");
            if (first == null) return map;
            // 既存敵を強敵枠へ登録する。HP/AIは変更せず、回復量は既存Healを共用する。
            var heal = CreateCardFactory().CreateById(SimpleCardFactory.Heal);
            return new DungeonConfig(map.DungeonId, map.DungeonName, map.TotalCellCount, map.MapCells,
                map.InitialDeckSize, map.Visits, new[] { first.Index },
                new[] { new ItemRewardEntry(new FieldItem(heal.Id, heal.Name, heal.Power), 1) }, 100);
        }
        private DungeonConfig CreateDefaultDungeonWithoutItemRewards(bool legacy = false)
        {
            var map=CreateDefaultDungeonVisits(legacy,false);if(legacy||map.Visits.Count==0)return map;
            var first=map.MapCells.FirstOrDefault(c=>c.Type==CellType.Enemy);if(first==null)return map;
            int cell=first.Index+1;if(cell>=map.TotalCellCount-1||map.MapCells[cell].Type!=CellType.Empty)return map;
            var heal=CreateCardFactory().CreateById(SimpleCardFactory.Heal);var cells=new List<MapCell>(map.MapCells);
            cells[cell]=new MapCell(cell,new FieldItem(heal.Id,heal.Name,heal.Power));
            return new DungeonConfig(map.DungeonId,map.DungeonName,map.TotalCellCount,cells,map.InitialDeckSize,map.Visits);
        }
        private DungeonConfig CreateDefaultDungeonVisits(bool legacy, bool withoutRemoval)
        {
            var map=CreateDefaultDungeonWithoutVisits(legacy);if(legacy)return map;
            var first=map.MapCells.FirstOrDefault(c=>c.Type==CellType.Enemy);if(first==null)return map;
            int shop=first.Index+2, recovery=first.Index+4;
            if(recovery>=map.TotalCellCount-1||map.MapCells[shop].Type!=CellType.Empty||map.MapCells[recovery].Type!=CellType.Empty)return map;
            var factory=CreateCardFactory();
            var products=new List<DungeonVisitOption>{
                new DungeonVisitOption("sword",10,factory.CreateById(SimpleCardFactory.Sword)),
                new DungeonVisitOption("bomb",10,factory.CreateById(SimpleCardFactory.Bomb)),
                new DungeonVisitOption("heal",10,factory.CreateById(SimpleCardFactory.Heal))};
            if(!withoutRemoval)products.Add(new DungeonVisitOption("remove-one",10,removeCount:1));
            var visits=new[]{new DungeonVisitDefinition(shop,true,products,withoutRemoval?2:3),
                new DungeonVisitDefinition(recovery,false,new[]{new DungeonVisitOption("recover",0,heal:factory.CreateById(SimpleCardFactory.Heal).Power)},1)};
            return new DungeonConfig(map.DungeonId,map.DungeonName,map.TotalCellCount,map.MapCells,map.InitialDeckSize,visits);
        }
        private DungeonConfig CreateDefaultDungeonWithoutVisits(bool legacy = false)
            => DungeonConfig.CreateTestDungeon(dungeonCellCount, enemySpacing, firstEnemyCell,
                enemyPoise: legacy ? null : new EnemyPoiseDefinition(normalEnemyPoise, battleStaggerFrames, enemyPoiseRecoveryFrames),
                bossPoise: legacy ? null : new EnemyPoiseDefinition(bossEnemyPoise, battleStaggerFrames, enemyPoiseRecoveryFrames));
        private CheckpointDefinitions CreateSingleDefinitions(DungeonConfig map,Deck deck)
        {
            var defs=DungeonCheckpointSession.CreateDefault(map,deck).Definitions;
            var hashes=new HashSet<string>(defs.CollectionDefinitions.Select(CollectionDisplay.DefinitionHash));
            foreach(var card in CreateCardFactory().CollectionCards){var hash=CollectionDisplay.DefinitionHash(card);if(hashes.Add(hash))defs.RegisterCard("product/catalog/v1/"+hash,card);}
            return defs;
        }
        private readonly List<DungeonChoice> knownLegacyDungeons=new List<DungeonChoice>();
        private void RegisterLegacyDeparture(Deck initialDeck)
        {
            var plan=clearReturnSession.Snapshot.NextDeparture;if(plan==null||configuredDungeon!=null||floorDefinitions!=null)return;
            foreach(var map in new[]{CreateDefaultDungeonWithoutTerrain(),CreateDefaultDungeonWithoutItemRewards(),CreateDefaultDungeonVisits(false,false),CreateDefaultDungeonVisits(false,true),CreateDefaultDungeonWithoutVisits(),CreateDefaultDungeonWithoutVisits(true)})
                foreach(var deck in new[]{initialDeck,CreateDepartureDeck(true)}) {
                    var defs=CreateSingleDefinitions(map,deck);
                    if(plan.DefinitionKey==defs.MapHash&&!knownLegacyDungeons.Any(c=>c.Key==defs.MapHash)&&CheckpointCodec.TryRestore(plan.Start,defs,out _,out _))
                        knownLegacyDungeons.Add(new DungeonChoice{Single=defs});
                }
        }

        private void Start()
        {
            // 検証時はPlay再読込で設定先が失われたら、保存処理を生成する前に停止する。
            ValidationStorageGuard.RequireEditorStartup(gameplaySettingsDirectoryOverride, CheckpointDirectory, ProgressDirectory, validationStorageRequired);
            if (commonHitInvincibilityMs <= 0 || (long)commonHitInvincibilityMs * 60 <= (long)battleStaggerFrames * 1000)
                throw new System.ArgumentOutOfRangeException(nameof(commonHitInvincibilityMs), "被弾無敵は共通のけぞり時間より長く設定してください");
            ReturnSettings().Validate();
            clearReturnStore = clearReturnStore ?? new ClearReturnFileStore(ProgressDirectory);
            clearReturnSession = new ClearReturnSession(clearReturnStore, new SimpleFusionRecipeProvider(CreateCardFactory()).CollectionRecipes, CreateCardFactory().CollectionCards, isCustomizationRunActive: () => manager != null && !clearReturnVisible);
            progressStartupBlocked = !clearReturnSession.Ready;
            int persistentLevel = clearReturnSession.Snapshot?.Level ?? 1;
            randomProvider = simulationSeed.HasValue ? new UnityRandomProvider(simulationSeed.Value) : new UnityRandomProvider();
            logger = new UnityLogger();
            inputProvider = new UnityInputProvider();
            gamepadVibration=new GamepadVibrationAdapter();
            vibrationSettings=new GamepadVibrationSettingsStore(gameplaySettingsDirectoryOverride??System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","settings"));
            vibrationSettings.TryRead(out gamepadVibrationEnabled,out vibrationSettingsError);gamepadVibration.Enabled=gamepadVibrationEnabled;
            inputProvider.GamepadGameplayActive=()=>manager!=null&&!clearReturnVisible&&!DialogueOpen&&!IsUserPaused&&!awaitingCheckpointChoice&&!manager.Dungeon.IsCheckpointBlocked&&manager.Dungeon.ActiveVisit==null&&
                (manager.CurrentPhase==GamePhase.Dungeon||manager.CurrentPhase==GamePhase.Battle&&battleFieldReady&&!manager.CurrentBattle.IsPaused);
            inputProvider.GamepadContext=()=>manager?.CurrentPhase==GamePhase.Dungeon?"exploration":"battle";
            inputProvider.GamepadPauseAvailable=()=>manager!=null&&(!IsUserPaused||IsExplorationResumeCounting)&&!clearReturnVisible&&!DialogueOpen&&!awaitingCheckpointChoice&&!manager.Dungeon.IsCheckpointBlocked&&manager.Dungeon.ActiveVisit==null&&(manager.CurrentPhase==GamePhase.Dungeon||manager.CurrentPhase==GamePhase.Battle&&!manager.CurrentBattle.CustomScreen.IsOpen);
            gamepadSettings=new GamepadBindingFileStore(gameplaySettingsDirectoryOverride??System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","settings"));
            inputProvider.TryRestoreGamepadProfile(UnityInputProvider.CreateDefaultGamepadProfile(),out _);
            if(gamepadSettings.TryRead(out var padProfile,out var padError))inputProvider.TryRestoreGamepadProfile(padProfile,out padError);GameplayGamepadSettingsError=padError;
            keyboardSettings = new KeyboardBindingFileStore(gameplaySettingsDirectoryOverride ?? System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","settings"));
            transformationSettings=new TransformationPresentationFileStore(gameplaySettingsDirectoryOverride??System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","settings"));
            transformationSettings.TryRead(out shortTransformationPresentation,out transformationSettingsError);
            dialogueSettings=new DialogueTextSettingsStore(gameplaySettingsDirectoryOverride??System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","settings"));dialogueSettings.TryRead(out dialogueSpeed,out dialogueSettingsError);
            audioSettingsStore=new AudioVolumeFileStore(gameplaySettingsDirectoryOverride??System.IO.Path.Combine(UnityEngine.Application.persistentDataPath,"Olca","settings"));
            audioSettingsStore.TryRead(out audioVolumes,out var audioError);GameplayAudioSettingsError=audioError;
            gameplayAudio=new GameplayAudioRouter(transform);combatFeedbackAudio=new CombatFeedbackAudio(transform,gameplayAudio);gameplayAudio.Apply(audioVolumes);gameplayAudio.PlayBgm(PlaceholderBgm.Exploration(dungeonBgm));
            if (keyboardSettings.TryRead(out var profile, out var settingsError)) inputProvider.TryRestoreKeyboardProfile(profile, out settingsError);
            GameplayKeyboardSettingsError = settingsError;
            if (settingsError != null) logger.LogWarning(settingsError);
            inputProvider.OnGamepadDisconnected += OnGamepadDisconnected;
            inputProvider.StartEventCapture();
            customScreenView?.ConfigureLogicalConfirmation(logicalLoop.QueueTimestampedCommand, () => inputProvider.PointerClickTimestamp);

            dungeonSelectionEnabled |= configuredDungeon == null && floorDefinitions == null;
            var savedPlan = clearReturnSession.Snapshot?.NextDeparture;
            if (dungeonChoices != null && savedPlan != null) {
                int found = dungeonChoices.FindIndex(c => c.Key == savedPlan.DefinitionKey);
                if (found >= 0) { selectedDungeon = found; floorDefinitions = dungeonChoices[found].Floors; }
            }
            var dungeonConfig = floorDefinitions?.CreateInitialMap(0) ?? configuredDungeon ?? CreateDefaultDungeon();
            var playerCharacter = new PlayerCharacter(
                maxHp: ReturnSettings().MaxHp(persistentLevel),
                initialPosition: new Vector2Int(0, 0),
                attackPower: ReturnSettings().Attack(persistentLevel),
                staggerDurationFrames: battleStaggerFrames,
                poiseRecoveryDelayFrames: playerPoiseRecoveryFrames);

            playerCharacter.SetAttackGrowth(AttackMilestoneGrowth.ForLevel(persistentLevel));
            var cardFactory = CreateCardFactory();
            var recipeProvider = new SimpleFusionRecipeProvider(CreateCardFactory());
            var exceedSystem = new ExceedSystem(cardFactory);
            var tuningSystem = new TuningSystem(cardFactory.GetTuningDefinition);
            var fusionSystem = new FusionSystem(recipeProvider, cardFactory);

            var initialDeck = CreateInitialDeck();
            RegisterLegacyDeparture(initialDeck);
            if (floorDefinitions != null)
            {
                floorStore = new FloorSequenceFileStore(floorDefinitions, ActiveCheckpointDirectory);
                floorSession = new FloorSequenceSession(floorDefinitions, floorStore);
                memoryCheckpoints = floorDefinitions.CreateCheckpointSession(0, floorSession);
                awaitingCheckpointChoice = floorStore.HasExistingData;
            }
            else
            {
                var definitions = CreateSingleDefinitions(dungeonConfig, initialDeck);
                nextDepartureDefinitions = definitions; // 旧攻略の再開定義と、新規出撃の現在定義を分ける。
                var store = new FileCheckpointStore(definitions, ActiveCheckpointDirectory);
                if (store.HasExistingData && store.Read() == null)
                {
                    // マップ/カードそれぞれの既知の旧定義だけを完全検証する。保存は書き換えない。
                    var maps = configuredDungeon == null
                        ? new[] { dungeonConfig, CreateDefaultDungeonWithoutTerrain(), CreateDefaultDungeonWithoutItemRewards(), CreateDefaultDungeonVisits(false,false), CreateDefaultDungeonVisits(false,true), CreateDefaultDungeonWithoutVisits(), CreateDefaultDungeonWithoutVisits(true) } : new[] { dungeonConfig };
                    var decks = new[] { initialDeck, CreateDepartureDeck(legacyPoise: true) };
                    bool matched = false;
                    foreach (var map in maps)
                    {
                        foreach (var deck in decks)
                        {
                            var candidate = CreateSingleDefinitions(map, deck);
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
            // 通常の新規開始だけに配布。空の旧進行/既存攻略/外部fixtureへ遡及しない。
            if (configuredDungeon == null && floorDefinitions == null && configuredDepartureDeck == null &&
                !awaitingCheckpointChoice && clearReturnSession.Ready && clearReturnStore is INewClearReturnStore fresh && fresh.IsNewProfile)
            {
                initialDeck = cardFactory.CreateStarterDeck();
                clearReturnSession.InitializeStarter(initialDeck.GetAllCards());
            }
            if (dungeonChoices == null) dungeonChoices = new List<DungeonChoice> { new DungeonChoice { Floors = floorDefinitions, Single = nextDepartureDefinitions } };
            initialHub = dungeonSelectionEnabled && clearReturnSession.Ready && clearReturnSession.Snapshot.Flow == null &&
                savedPlan == null && !awaitingCheckpointChoice;
            bool waitingReturn=clearReturnSession.Snapshot?.Flow?.Stage==ClearReturnStage.Returned&&clearReturnSession.Snapshot.NextDeparture==null;
            // 既存攻略は保存済み能力を復元する。現在の製品登録を遡及適用しない。
            if(!awaitingCheckpointChoice&&savedPlan==null&&!waitingReturn&&!HasIncompleteReturn&&!progressStartupBlocked){
                try{ApplyDepartureCustomization(playerCharacter);}catch(System.ArgumentException e){progressStartupBlocked=true;clearReturnError=e.Message;}
            }
            manager = new DungeonBattleManager(
                dungeonConfig, playerCharacter, initialDeck, randomProvider, logger,
                exceedSystem: exceedSystem,
                tuningSystem: tuningSystem,
                fusionSystem: fusionSystem, checkpoints: waitingReturn || initialHub || awaitingCheckpointChoice || progressStartupBlocked || HasIncompleteReturn || clearReturnSession.Snapshot.NextDeparture != null
                    ? (dungeonConfig.Visits.Count==0 && dungeonConfig.ItemRewards.Count==0?null:DungeonCheckpointSession.CreateDefault(dungeonConfig,initialDeck)) : memoryCheckpoints, cardDatabase: cardFactory,
                hasNextFloor: !waitingReturn && !awaitingCheckpointChoice && !progressStartupBlocked && !HasIncompleteReturn && clearReturnSession.Snapshot.NextDeparture == null && floorDefinitions != null && floorDefinitions.FloorCount > 1, enemyAIProvider: enemyAIProvider,
                evaluationRewards: waitingReturn||initialHub||awaitingCheckpointChoice||progressStartupBlocked||HasIncompleteReturn||clearReturnSession.Snapshot.NextDeparture!=null?null:evaluationRewardProvider);

            manager.RequireBattleEndPresentation();
            manager.Dungeon.ConfigureNormalMovementSpeed(explorationNormalSpeedMilliPerSecond);
            manager.Dungeon.ConfigureCurrencyReward(currencyPerEnemy);

            // イベント購読
            BindManagerEvents(manager);

            // GameFieldView 初期化（フィールド・敵スプライトを世界空間に配置）
            gameFieldView?.Initialize(manager.Dungeon);

            // バトルCardUI は最初から非表示
            battleView?.gameObject.SetActive(false);

            // バトルセッション共通操作をBattleSessionControllerに委譲
            if (battleView != null && customScreenView != null)
                battleSession = new BattleSessionController(battleView, customScreenView, randomProvider, logger,
                    (frames, action) => logicalLoop.AfterFrames(frames, action), battleView.CloseDurationFrames, transformationPresentation:true);

            CreatePauseControls();
            ResumeClearReturnIfNeeded();
            logicalLoop.Observe(false);
            if (applicationInactive) RequestAutomaticPause();
            logger.LogInfo("[DungeonBattleController] 初期化完了");
        }

        // EventSystemとViewのUpdateで受けたクリックを、同じ描画フレームの論理処理へ渡す。
                private int? renderedVisitCell;
        private int selectedVisit = -1;
        private bool selectingVisitRemoval;
        private readonly HashSet<Card> visitRemovalTargets = new HashSet<Card>();
        private void ResetVisitSelection() { selectedVisit=-1;selectingVisitRemoval=false;visitRemovalTargets.Clear(); }
        private void RefreshDungeonVisit()
        {
            if(manager.Dungeon.ActiveVisit==null){if(renderedVisitCell.HasValue&&!clearReturnVisible)clearReturnView.gameObject.SetActive(false);renderedVisitCell=null;ResetVisitSelection();return;}
            if(IsUserPaused){clearReturnView.gameObject.SetActive(false);return;}
            if(renderedVisitCell!=manager.Dungeon.ActiveVisit.Cell||!clearReturnView.gameObject.activeSelf){ResetVisitSelection();logicalLoop.ReleaseInput();ShowDungeonVisit();}
        }
        private void ShowDungeonVisit()
        {
            var d=manager.Dungeon;var definition=d.ActiveVisit;var state=d.ActiveVisitState;if(definition==null)return;
            renderedVisitCell=definition.Cell;
            if(selectingVisitRemoval && selectedVisit>=0 && selectedVisit<state.Options.Length) {
                var option=definition.Options[state.Options[selectedVisit]];var cards=d.GetCurrentDeck().GetAllCards().ToArray();
                visitRemovalTargets.RemoveWhere(c=>!cards.Contains(c));int needed=d.VisitRemovalCount(selectedVisit);
                var labels=cards.Select((c,i)=>$"カード {i+1}: {c.Name}{new string('★',(int)c.ExceedLevel)} / コスト {c.GetFinalCost()} / 威力 {c.GetFinalPower()}"+(d.GetObtainedCards().Contains(c)?" ［攻略中取得・持帰り履歴は保持］":" ［持込・永続所持は保持］")).ToArray();
                clearReturnView.RenderVisitRemoval("攻略中カード削除 / 所持 "+d.Currency+" 通貨",labels,cards.Select(c=>visitRemovalTargets.Contains(c)).ToArray(),needed,option.Price,d.IsCheckpointBlocked,
                    needed>0&&visitRemovalTargets.Count==needed&&d.CanChooseVisit(selectedVisit),
                    i=>{if(IsUserPaused||d.IsCheckpointBlocked||!ReferenceEquals(d,manager.Dungeon)||i<0||i>=cards.Length)return;var card=cards[i];if(!visitRemovalTargets.Remove(card)&&visitRemovalTargets.Count<needed)visitRemovalTargets.Add(card);ShowDungeonVisit();},
                    ()=>{if(IsUserPaused||!ReferenceEquals(d,manager.Dungeon))return;d.ChooseVisit(selectedVisit,cards.Where(c=>visitRemovalTargets.Contains(c)).ToArray());ResetVisitSelection();ShowDungeonVisit();},
                    ()=>{ResetVisitSelection();ShowDungeonVisit();},()=>SetUserPaused(true));return;
            }
            var enabled=state.Purchased.Select((p,i)=>!p&&(definition.IsShop||!state.Purchased.Any(x=>x))&&(definition.Options[state.Options[i]].RemoveCount==0||d.GetCurrentDeck().TotalCount>0)).ToArray();
            var names=state.Options.Select((n,i)=>(state.Purchased[i]?"［済］ ":"")+definition.Options[n].Label+" / "+definition.Options[n].Price+" 通貨").ToArray();
            bool can=selectedVisit>=0&&selectedVisit<enabled.Length&&enabled[selectedVisit]&&d.CanChooseVisit(selectedVisit);
            clearReturnView.RenderDungeonVisit((definition.IsShop?"ダンジョンショップ":"イベント")+" / 所持 "+d.Currency+" 通貨",names,enabled,selectedVisit,d.IsCheckpointBlocked,can,
                i=>{if(IsUserPaused||d.IsCheckpointBlocked||!ReferenceEquals(d,manager.Dungeon))return;ResetVisitSelection();selectedVisit=i;ShowDungeonVisit();},
                ()=>{if(IsUserPaused||!ReferenceEquals(d,manager.Dungeon))return;
                    if(selectedVisit>=0&&selectedVisit<state.Options.Length&&definition.Options[state.Options[selectedVisit]].RemoveCount>0&&d.CanChooseVisit(selectedVisit))selectingVisitRemoval=true;
                    else{d.ChooseVisit(selectedVisit);ResetVisitSelection();}ShowDungeonVisit();},
                ()=>{ResetVisitSelection();ShowDungeonVisit();},
                ()=>{if(IsUserPaused||!ReferenceEquals(d,manager.Dungeon))return;d.ExitVisit();RefreshDungeonVisit();},()=>SetUserPaused(true),d.CanExitVisit);
        }

        private void LateUpdate()
        {
            gamepadVibration?.Tick();
            if (manager == null) return;
            if(inputProvider.ConsumeGamepadPause())SetUserPaused(true);
            RefreshTouchContext();
            UpdateDialogue();
            UpdateExplorationResume();
            bool active = !DialogueOpen && manager.Dungeon.ActiveVisit==null && !clearReturnVisible && !IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked && Time.timeScale > 0;
            logicalLoop.PumpTimestamped(active, inputProvider.CaptureTimestamped(), TickLogicalFrame, () => !DialogueOpen && !clearReturnVisible && !IsUserPaused && !awaitingCheckpointChoice && !manager.Dungeon.IsCheckpointBlocked,
                () => manager.Dungeon.ActiveVisit==null && (manager.CurrentPhase == GamePhase.Dungeon || (manager.CurrentBattle != null && !manager.CurrentBattle.IsPaused)));
            ApplyPendingFloor();
            RefreshDungeonVisit();
            if (manager.Dungeon.IsCheckpointBlocked) ShowCheckpointStatus();
            UpdateViews();
            RefreshBasicAttackHud();
            RefreshProgressActions();
            if(AudioSettingsOpen)audioSettingsView.transform.SetAsLastSibling();
            if(DialogueOpen&&!IsUserPaused)dialogueView.transform.SetAsLastSibling();
            resourceRewardMenu?.Tick();
            RefreshSystemMenu();
        }

        private void TickLogicalFrame(long frame, int deltaTimeMs, LogicalInput input)
        {
            if(DialogueOpen)return;
            if (GameplayPause.InputBlocked) { basicAttackInput.Suspend(); uiAttackHeld = uiAttackDown = uiAttackUp = false; input = new LogicalInput(); queuedBasicAttackTap = false;queuedTouchJump=queuedTouchDash=queuedTouchCard=false; }
            if(queuedTouchJump||queuedTouchDash||queuedTouchCard) {
                input=input.Copy();
                if(manager.CurrentPhase==GamePhase.Dungeon) {input.JumpButtonDown|=queuedTouchJump;input.DashButtonDown|=queuedTouchDash;}
                if(manager.CurrentPhase==GamePhase.Battle)input.CardUseButtonDown|=queuedTouchCard;
                queuedTouchJump=queuedTouchDash=queuedTouchCard=false;
            }
            if (uiAttackHeld || uiAttackDown || uiAttackUp) {
                input = input.Copy();
                input.BasicAttackButtonDown |= uiAttackDown;
                input.BasicAttackButton |= uiAttackHeld;
                input.BasicAttackButtonUp = !input.BasicAttackButton && (input.BasicAttackButtonUp || uiAttackUp);
                uiAttackDown = uiAttackUp = false;
            }
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
            if(manager.Dungeon.ActiveVisit!=null)return;
            switch (manager.CurrentPhase)
            {
                case GamePhase.Dungeon:
                    if(manager.Dungeon.IsTerrainRecovering){
                        if(dashStartPosition>=0f){dashStartPosition=-1f;dashCooldownRemaining=LogicalFrameClock.DurationFrames(dashCooldown);}
                        return;
                    }
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

                    if (dashCooldownRemaining > 0f && !manager.Dungeon.IsDashJumping && !manager.Dungeon.IsObstacleStopped)
                        dashCooldownRemaining--;
                    break;

                case GamePhase.Battle:
                    // AltキーでBattleSessionControllerに委譲してカスタム画面を開く
                    if (input.CustomScreenButtonDown)
                        battleSession?.TryOpenCustomScreen();

                    // 入力優先とカード振分けはstandaloneと同じCore。完了通知/例外処理だけセッションへ委譲。
                    basicAttackInput.ProcessInput(manager.CurrentBattle, input, basicAttackChargeFrames, basicAttackIntervalFrames,
                        basicAttackChargeMultiplier, () => battleSession?.UseCardFromInput());
                    break;

                case GamePhase.CardReward:
                    // カード選択中はダンジョン操作を無効化
                    break;
            }
        }

        private void UpdateViews()
        {
            if(customizationUnlockWasOpen&&!CustomizationUnlockOpen){customizationUnlockWasOpen=false;if(initialHub)ShowDungeonSelection();else ShowClearReturn();}
            if(customizationDraft!=null&&(!CustomizationEditorOpen||customizationEditorView.Draft==null)) {
                customizationEditorView?.Close();customizationDraft=null;
                if(initialHub)ShowDungeonSelection();else ShowClearReturn();
            }
            if(CustomizationEditorOpen)customizationEditorView.transform.SetAsLastSibling();
            if(CustomizationUnlockOpen)customizationUnlockView.transform.SetAsLastSibling();
            if(CustomizationAcquisitionOpen)customizationAcquisitionView.transform.SetAsLastSibling();
            if(cardCollectionButton!=null){cardCollectionButton.gameObject.SetActive(!CustomizationAcquisitionOpen && !CustomizationEditorOpen && !CustomizationUnlockOpen && !DialogueOpen && !AudioSettingsOpen && !(keyboardSettingsView!=null&&keyboardSettingsView.gameObject.activeInHierarchy) && !(gamepadSettingsView!=null&&gamepadSettingsView.gameObject.activeInHierarchy) && !progressDialogOpen && !cardCollectionOpen && !(clearReturnView?.HasMenuModal??false) && (IsUserPaused||clearReturnVisible));
                if(cardCollectionButton.gameObject.activeSelf)cardCollectionButton.transform.SetAsLastSibling();}
            if (currencyBalanceText != null) currencyBalanceText.text = $"通貨: {manager?.Dungeon.Currency ?? 0}";
            dungeonHudView?.SetDeckCountVisible(manager.CurrentPhase!=GamePhase.CardReward);
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

        private void BindBattleFeedback(BattleSystem battle) => combatFeedbackAudio?.Bind(isActiveAndEnabled ? battle : null);

        private void OnBattleStarted(BattleSystem battle, ContactInfo contactInfo)
        {
            BindBattleFeedback(battle);
            pendingBattleEvaluation=null;pendingBattleElapsedFrames=null;
            gameplayAudio?.PlayBgm(PlaceholderBgm.Battle(battleBgm,dungeonBgm));
            ResetTouchInput();
            battle.ConfigureDefaultHitInvincibility(commonHitInvincibilityMs);
            battle.ConfigureBasicAttackTiming(CheckpointBattleFactory.ProductBasicTiming);
            battleFieldReady = false;
            // 接敵フレームでモデルを停止する。描画遷移中にAIや状態異常を先行させない。
            battle.CustomGauge.SetGauge(CustomGaugeSystem.MAX_GAUGE);
            if (!battle.CustomScreen.IsOpen) battle.CustomScreen.Commands.Execute(new CustomScreenCommand(CustomCommandKind.Open), randomProvider);
            battle.CustomScreen.OnFusionExecuted += (materials,output)=>{if(ReferenceEquals(manager?.CurrentBattle,battle)&&!manager.IsSuspended)RecordFusionDiscovery(materials,output);};
            battle.Transformations.ShortPresentation=shortTransformationPresentation;
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

        private CancellationTokenSource outcomePresentationCts;
        private DungeonBattleManager outcomePresentationOwner;
        private void CancelOutcomePresentation()
        {
            automaticPauseOwner = null;
            outcomePresentationCts?.Cancel();
            BattleVisualClock.SetPaused(this,false);
        }
        private void OnBattleEndPresentationRequested(BattleResult result)
        {
            combatFeedbackAudio?.Bind(null);
            StopGamepadVibration();
            if(outcomePresentationOwner!=null)return;
            PlayBattleOutcomeAsync(result).Forget();
        }
        private async UniTask PlayBattleOutcomeAsync(BattleResult result)
        {
            var owner=manager;
            if(owner==null||!owner.IsBattleEndPresentationPending)return;
            var cts=CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            outcomePresentationCts=cts;outcomePresentationOwner=owner;
            try
            {
                if(IsUserPaused)SetUserPaused(false);
                transitionCts?.Cancel();combatFeedbackAudio?.Bind(null);battleSession?.Detach();
                if(gameFieldView!=null)await gameFieldView.HideHUDAsync(0,cts.Token);
                BattleVisualClock.SetPaused(this,true);
                var terminal=gameFieldView!=null?gameFieldView.PlayBattleOutcomeAsync(result,cts.Token):UniTask.CompletedTask;
                if(battleView!=null)await battleView.CloseCustomScreenAsync();
                await terminal;
                cts.Token.ThrowIfCancellationRequested();
                if(!ReferenceEquals(manager,owner))return;
                if(result==BattleResult.Defeat){gameFieldView?.OnBattleEnded();battleView?.gameObject.SetActive(false);battleFieldReady=false;}
                owner.CompleteBattleEndPresentation();
            }
            catch(System.OperationCanceledException){}
            finally
            {
                if(ReferenceEquals(outcomePresentationCts,cts)){outcomePresentationCts=null;outcomePresentationOwner=null;BattleVisualClock.SetPaused(this,false);}
                cts.Dispose();
            }
        }

        private void OnBattleEnded()
        {
            combatFeedbackAudio?.Bind(null);
            StopGamepadVibration();
            if (ReferenceEquals(automaticPauseOwner, manager))
            {
                automaticPauseOwner = null;
                SetUserPaused(true);
            }
            pendingBattleEvaluation = manager?.LastBattleEvaluation;
            pendingBattleElapsedFrames = pendingBattleEvaluation?.LogicalFrames ?? pendingBattleElapsedFrames;
            gameplayAudio?.PlayBgm(PlaceholderBgm.Exploration(dungeonBgm));
            ResetTouchInput();
            if(manager.LastBattleResult==BattleResult.Defeat)return;
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
            pendingBattleEvaluation = null;
            pendingBattleElapsedFrames = manager?.CurrentBattle?.LogicalFrame;
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
            combatFeedbackAudio?.Bind(null);battleSession?.Detach();

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
                        Card selectedCard = await cardSelectionView.ShowAsync(pendingCardChoices, deckCount, ct, pendingBattleElapsedFrames, pendingBattleEvaluation);
                        await GameplayPause.WaitAsync(ct);
                        pendingCardChoices = null;
                        logicalLoop.Schedule(() => { if (ReferenceEquals(manager, owner)) { owner.SelectCardReward(selectedCard); ScheduleExplorationResume(); } });
                        while (manager.CurrentPhase == GamePhase.CardReward && manager.Dungeon.PendingItemReward==null && manager.Dungeon.PendingCurrencyReward == 0) await GameplayPause.NextFrame(ct);
                    }
                    else
                    {
                        // CardSelectionView が未アサインの場合はスキップしてダンジョン復帰
                        logger.LogWarning("[Controller] CardSelectionView が未設定のためカード報酬をスキップします");
                        await GameplayPause.WaitAsync(ct);
                        pendingCardChoices = null;
                        logicalLoop.Schedule(() => { if (ReferenceEquals(manager, owner)) { owner.SelectCardReward(null); ScheduleExplorationResume(); } });
                        while (manager.CurrentPhase == GamePhase.CardReward && manager.Dungeon.PendingItemReward==null && manager.Dungeon.PendingCurrencyReward == 0) await GameplayPause.NextFrame(ct);
                    }
                }

                if(manager.Dungeon.PendingItemReward!=null)
                {
                    var item=manager.Dungeon.PendingItemReward;
                    itemRewardChoice=new UniTaskCompletionSource<bool>();var choice=itemRewardChoice;
                    itemRewardText.text=$"アイテムホウシュウ: {item.Name}\nHPカイフク {item.Heal} / ゲンザイHP {manager.Dungeon.Player.CurrentHp}/{manager.Dungeon.Player.MaxHp}\n満タンでも受け取り時に消費します";
                    if(pendingBattleElapsedFrames.HasValue)itemRewardText.text+="\n"+CardSelectionView.FormatBattleSummary(pendingBattleElapsedFrames,pendingBattleEvaluation);
                    itemRewardOverlay.SetActive(true);itemRewardOverlay.transform.SetAsLastSibling();BindResourceReward(itemRewardOverlay,"SkipItemReward",()=>{if(!GameplayPause.InputBlocked)itemRewardChoice?.TrySetResult(false);});bool receive;
                    try{using(ct.Register(()=>choice.TrySetCanceled(ct)))receive=await choice.Task;}
                    finally{resourceRewardMenu?.Suspend();if(itemRewardOverlay!=null)itemRewardOverlay.SetActive(false);itemRewardChoice=null;}
                    await GameplayPause.WaitAsync(ct);
                    logicalLoop.Schedule(()=>{if(ReferenceEquals(manager,owner)){owner.SelectItemReward(receive);ScheduleExplorationResume();}});
                    while(manager.CurrentPhase==GamePhase.CardReward&&manager.Dungeon.PendingCurrencyReward==0)await GameplayPause.NextFrame(ct);
                }

                if (manager.Dungeon.PendingCurrencyReward > 0)
                {
                    currencyChoice = new UniTaskCompletionSource<bool>();
                    var choice = currencyChoice;
                    currencyRewardText.text = $"ツウカホウシュウ: {manager.Dungeon.PendingCurrencyReward}\nショジ: {manager.Dungeon.Currency}（攻略中のみ）";
                    if(pendingBattleElapsedFrames.HasValue)currencyRewardText.text+="\n"+CardSelectionView.FormatBattleSummary(pendingBattleElapsedFrames,pendingBattleEvaluation);
                    receiveCurrencyButton.interactable = manager.Dungeon.PendingCurrencyReward <= long.MaxValue - manager.Dungeon.Currency;
                    currencyOverlay.SetActive(true); currencyOverlay.transform.SetAsLastSibling();
                    BindResourceReward(currencyOverlay,"SkipCurrencyButton",()=>{if(!GameplayPause.InputBlocked)currencyChoice?.TrySetResult(false);});
                    bool receive;
                    try { using (ct.Register(() => choice.TrySetCanceled(ct))) receive = await choice.Task; }
                    finally { resourceRewardMenu?.Suspend();if (currencyOverlay != null) currencyOverlay.SetActive(false); currencyChoice = null; }
                    await GameplayPause.WaitAsync(ct);
                    logicalLoop.Schedule(() => { if (ReferenceEquals(manager, owner)) { owner.SelectCurrencyReward(receive); ScheduleExplorationResume(); } });
                    while (manager.CurrentPhase == GamePhase.CardReward) await GameplayPause.NextFrame(ct);
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
                        gameFieldView.PlayerTransform, targetPos, ct, gameFieldView.CellSize, gameFieldView.SetContactPose);
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
            EndRunWithoutRewards(RunReturnOutcome.Defeated);
        }

        private void OnDungeonCleared(IReadOnlyList<Card> obtainedCards)
        {
            StopGamepadVibration();
            // 最終報酬の選択後はDungeonフェーズを経由しないため、ここで最新値を反映する。
            RefreshDungeonHud();
            logger.LogInfo($"[Controller] ダンジョンクリア！ 獲得カード: {obtainedCards.Count}枚");
            BeginClearReturn();
        }

        // ==================== 初期デッキ ====================

        private IReadOnlyDictionary<string, PlayerAttackTiming> configuredCardTimings;
        /// <summary>出撃デッキ生成前の製品時間データ。保存済み定義を起動後に書き換えない。</summary>
        public void ConfigureCardAttackData(IReadOnlyDictionary<string, PlayerAttackTiming> timings)
        {
            if (manager != null || configuredDepartureDeck != null) throw new System.InvalidOperationException("時間定義は編成・攻略開始前に設定してください");
            if (timings == null) throw new System.ArgumentNullException(nameof(timings));
            // 全件を検証してから公開し、外部辞書の変更を引き継がない。
            _ = new SimpleCardFactory(new CardPoiseSettings(swordPoiseDamage, bombPoiseDamage, fireSwordPoiseDamage), cardTimings: timings);
            configuredCardTimings = timings.ToDictionary(entry => entry.Key, entry => entry.Value);
        }
        private SimpleCardFactory CreateCardFactory()
            => new SimpleCardFactory(new CardPoiseSettings(swordPoiseDamage, bombPoiseDamage, fireSwordPoiseDamage), cardTimings: configuredCardTimings);

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
            if (clearReturnVisible) { error = "帰還処理を先に完了してください"; return false; }
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

        private DungeonBattleManager CreateFloorManager(FloorSequenceSession route, out DungeonCheckpointSession session, FloorSequenceDefinitions definitions = null)
        {
            definitions = definitions ?? floorDefinitions;
            var state = route.Current.State;
            session = definitions.CreateCheckpointSession(route.Current.FloorIndex, route);
            var factory = CreateCardFactory();
            return new DungeonBattleManager(state.Map, state.Player, state.Deck, randomProvider, logger,
                new ExceedSystem(factory), new TuningSystem(factory.GetTuningDefinition), new FusionSystem(new SimpleFusionRecipeProvider(CreateCardFactory()), factory),
                checkpoints: session, restoration: state, cardDatabase: factory,
                hasNextFloor: route.Current.FloorIndex + 1 < definitions.FloorCount, enemyAIProvider: enemyAIProvider,evaluationRewards:evaluationRewardProvider);
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
                    out restored, out error, new ExceedSystem(factory), new TuningSystem(factory.GetTuningDefinition),
                    new FusionSystem(new SimpleFusionRecipeProvider(CreateCardFactory()), factory), cardDatabase: factory, enemyAIProvider: enemyAIProvider,evaluationRewards:evaluationRewardProvider)) return false;
            }
            if (clearReturnSession?.Snapshot?.Receipts.Any(r => r.RunId == candidate.RunId && r.Outcome != RunReturnOutcome.Cleared) == true)
            { restored.SuspendForCheckpoint(); error = "敗北・撤退が確定した攻略は再開できません"; return false; }
            if (!manager.SuspendForCheckpoint()) { error = "現在の攻略は再開できません"; return false; }
            if (loadedRoute != null) { floorSession = loadedRoute; CurrentFloorIndex = loadedRoute.Current.FloorIndex; }
            floorSwapPending = false;
            ReplaceManager(restored, candidate);
            return true;
        }

        private void ReplaceManager(DungeonBattleManager restored, DungeonCheckpointSession candidate)
        {
            CancelExplorationResume();
            FinishDialogue(interrupt:true);
            CancelOutcomePresentation();
            if (IsUserPaused) ApplyUserPauseImmediate(false);
            transitionCts?.Cancel(); combatFeedbackAudio?.Bind(null);battleSession?.Detach();
            UnbindManagerEvents(manager);
            pendingCardChoices = null; cardSelectionView?.Hide();
            currencyChoice?.TrySetCanceled(); currencyOverlay?.SetActive(false);
                itemRewardChoice?.TrySetCanceled();itemRewardOverlay?.SetActive(false);
            gameFieldView?.OnBattleEnded(); battleFieldReady = false;
            battleView?.gameObject.SetActive(false);
            logicalLoop.StartNewTimeline(); basicAttackInput.Reset(); queuedBasicAttackTap = false;
            manager = restored;
            manager.RequireBattleEndPresentation();
            manager.Dungeon.ConfigureNormalMovementSpeed(explorationNormalSpeedMilliPerSecond);
            manager.Dungeon.ConfigureCurrencyReward(currencyPerEnemy);
            memoryCheckpoints = candidate; awaitingCheckpointChoice = false;
            dashStartPosition = -1f; dashCooldownRemaining = 0;
            ShowCheckpointStatus();
            BindManagerEvents(manager);
            gameFieldView?.SetPlayerPositionUpdateEnabled(true);
            gameFieldView?.Initialize(manager.Dungeon);
            manager.ActivateCheckpoint();
            if (manager.CurrentPhase == GamePhase.Dungeon) { gameplayAudio?.PlayBgm(PlaceholderBgm.Exploration(dungeonBgm)); gameFieldView?.ShowHUDAsync(0).Forget(); }
            RefreshDungeonHud();
            if (applicationInactive) RequestAutomaticPause();
        }

        private void BindManagerEvents(DungeonBattleManager target)
        {
            if(target==null)return;
            target.OnBattleEndPresentationRequested+=OnBattleEndPresentationRequested;
            target.OnBattleStarted+=OnBattleStarted;
            target.OnBattleEnded+=OnBattleEnded;
            target.OnCardRewardPresented+=OnCardRewardPresented;
            target.OnGameOver+=OnGameOver;
            target.OnDungeonClearCompleted+=OnDungeonCleared;
            target.OnFloorCompleted+=OnFloorCompleted;
        }

        private void UnbindManagerEvents(DungeonBattleManager target)
        {
            if(target==null)return;
            target.OnBattleEndPresentationRequested-=OnBattleEndPresentationRequested;
            target.OnBattleStarted-=OnBattleStarted;
            target.OnBattleEnded-=OnBattleEnded;
            target.OnCardRewardPresented-=OnCardRewardPresented;
            target.OnGameOver-=OnGameOver;
            target.OnDungeonClearCompleted-=OnDungeonCleared;
            target.OnFloorCompleted-=OnFloorCompleted;
        }

        private void OnDestroy()
        {
            CancelExplorationResume(); explorationResumeLoop.Dispose();
            gamepadVibration?.Dispose();
            CancelOutcomePresentation();
            systemMenu?.Dispose();
            resourceRewardMenu?.Dispose();
            FinishDialogue(interrupt:true);dialogueLoop.Dispose();
            combatFeedbackAudio?.Dispose();gameplayAudio?.Dispose();
            if (inputProvider != null) inputProvider.OnGamepadDisconnected -= OnGamepadDisconnected;
            inputProvider?.Dispose();
            logicalLoop.Dispose();
            if (IsUserPaused) ApplyUserPauseImmediate(false);
            if (pauseCanvas != null) Destroy(pauseCanvas);
            transitionCts?.Cancel();
            transitionCts?.Dispose();
            combatFeedbackAudio?.Bind(null);battleSession?.Detach();

            if (manager != null)
            {
                UnbindManagerEvents(manager);
            }
        }
    }
}
