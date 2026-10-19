using Olca.Core.Logic.CardEffects;
using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Integration
{
    /// <summary>
    /// ダンジョンとバトルの統合管理クラス
    /// シームレスな遷移を実現
    /// </summary>
    public class DungeonBattleManager
    {
        private readonly DungeonSystem dungeonSystem;
        private readonly PlayerCharacter player;
        private readonly IRandomProvider randomProvider;
        private readonly IGameLogger logger;

        private BattleSystem currentBattle;
        private GamePhase currentPhase;
        public bool IsUserPaused { get; private set; }

        /// <summary>ユーザー操作の停止だけを変更し、カスタム画面や遷移の停止は解除しない。</summary>
        public void SetUserPaused(bool paused)
        {
            if (IsUserPaused == paused) return;
            IsUserPaused = paused;
            if (paused) currentBattle?.Pause(BattlePauseReason.User);
            else currentBattle?.Resume(BattlePauseReason.User);
        }

        /// <summary>現在のゲームフェーズ</summary>
        public GamePhase CurrentPhase => currentPhase;

        /// <summary>ダンジョンシステム</summary>
        public DungeonSystem Dungeon => dungeonSystem;

        /// <summary>現在のバトルシステム（バトル中のみ）</summary>
        public BattleSystem CurrentBattle => currentBattle;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public DungeonBattleManager(
            DungeonConfig dungeonConfig,
            PlayerCharacter player,
            Deck initialDeck,
            IRandomProvider randomProvider,
            IGameLogger logger,
            ExceedSystem exceedSystem = null,
            TuningSystem tuningSystem = null,
            FusionSystem fusionSystem = null,
            ICardPool rewardPool = null,
            DungeonCheckpointSession checkpoints = null, RestoredCheckpoint restoration = null, ICardDatabase cardDatabase = null, bool hasNextFloor = false)
        {
            this.player = player;
            this.randomProvider = randomProvider;
            this.logger = logger;

            // ダンジョンシステム初期化
            dungeonSystem = new DungeonSystem(
                dungeonConfig,
                player,
                initialDeck,
                randomProvider,
                logger,
                exceedSystem: exceedSystem,
                tuningSystem: tuningSystem,
                fusionSystem: fusionSystem,
                rewardPool: rewardPool, checkpoints: checkpoints, restoration: restoration, cardDatabase: cardDatabase, hasNextFloor: hasNextFloor);

            // イベント購読
            dungeonSystem.OnBattleStart += OnDungeonBattleStart;
            dungeonSystem.OnDungeonFailed += OnDungeonFailed;
            dungeonSystem.OnDungeonCleared += OnDungeonCleared;
            dungeonSystem.OnFloorCompleted += () => {
                currentPhase = GamePhase.DungeonTransition;
                OnFloorCompleted?.Invoke();
            };
            dungeonSystem.OnCardRewardOffered += OnCardRewardOfferedHandler;
            dungeonSystem.OnReturnToDungeon += OnReturnedToDungeon;

            currentPhase = GamePhase.Dungeon;
            logger?.LogInfo("[DungeonBattleManager] 初期化完了 - ダンジョンフェーズ開始");
        }

        /// <summary>
        /// 毎フレーム更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        /// <param name="input">入力プロバイダー（オプショナル）</param>
        private bool logicalFrameTick;
        public void TickFrame(int compatibleMilliseconds, IInputProvider input = null)
        {
            logicalFrameTick = true;
            try { Tick(compatibleMilliseconds, input); }
            finally { logicalFrameTick = false; }
        }

        public void Tick(int deltaTimeMs, IInputProvider input = null)
        {
            if (IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
            if (IsUserPaused || IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
            switch (currentPhase)
            {
                case GamePhase.Dungeon:
                    if (logicalFrameTick) dungeonSystem.TickFrame(); else dungeonSystem.Tick(deltaTimeMs);
                    break;

                case GamePhase.Battle:
                    if (logicalFrameTick) currentBattle?.TickFrame(deltaTimeMs, input);
                    else currentBattle?.Tick(deltaTimeMs, input);
                    break;

                case GamePhase.CardReward:
                    // カード選択画面中は更新なし
                    break;

                case GamePhase.GameOver:
                    // ゲームオーバー時は更新なし
                    break;
            }
        }

        /// <summary>
        /// ダンジョンからバトル開始時
        /// DungeonSystem がバトルシステムを生成済みなので、そちらを参照する
        /// </summary>
        private void OnDungeonBattleStart(ContactInfo contactInfo)
        {
            logger?.LogInfo("[DungeonBattleManager] バトル開始");
            currentPhase = GamePhase.BattleTransition;

            // DungeonSystem が生成したバトルシステムを使用する
            // （DungeonBattleManager が独自に生成すると二重バトルになる）
            currentBattle = dungeonSystem.CurrentBattle;
            if (IsUserPaused) currentBattle?.Pause(BattlePauseReason.User);

            if (currentBattle == null)
            {
                logger?.LogWarning("[DungeonBattleManager] DungeonSystem.CurrentBattle が null - バトル開始失敗");
                currentPhase = GamePhase.Dungeon;
                return;
            }

            // バトル終了イベント購読
            currentBattle.OnBattleEnd += OnBattleEnd;

            // バトルフェーズに移行
            currentPhase = GamePhase.Battle;
            OnBattleStarted?.Invoke(currentBattle, contactInfo);
        }

        /// <summary>
        /// バトル終了時
        /// DungeonSystem.OnBattleEndFromBattleSystem が同じイベントを先に処理して
        /// OnBattleCompleted を呼ぶため、ここでは二重呼び出しを避ける。
        /// フェーズ遷移は DungeonSystem が発火するイベント経由で完結する：
        ///   勝利+カードドロップ → OnCardRewardOfferedHandler → CardReward
        ///   勝利+ドロップなし   → OnReturnedToDungeon → Dungeon
        ///   敗北               → OnDungeonFailed → GameOver
        /// このハンドラでは currentPhase を変更しない（上書き禁止）。
        /// </summary>
        private void OnBattleEnd(BattleResult result)
        {
            logger?.LogInfo($"[DungeonBattleManager] バトル終了: {result}");

            // バトルシステムの購読解除・参照を破棄
            if (currentBattle != null)
            {
                currentBattle.OnBattleEnd -= OnBattleEnd;
                currentBattle = null;
            }

            // バトル終了イベント（View遷移のトリガー）
            OnBattleEnded?.Invoke();
        }

        /// <summary>
        /// カード報酬選択画面表示のハンドラ
        /// </summary>
        private void OnCardRewardOfferedHandler(List<Card> choices)
        {
            logger?.LogInfo("[DungeonBattleManager] カード報酬選択画面 - CardRewardフェーズへ移行");
            currentPhase = GamePhase.CardReward;
            OnCardRewardPresented?.Invoke(choices);
        }

        /// <summary>
        /// ダンジョン復帰のハンドラ
        /// </summary>
        private void OnReturnedToDungeon()
        {
            currentPhase = GamePhase.Dungeon;
        }

        /// <summary>
        /// カード報酬を選択してダンジョンに戻る
        /// </summary>
        /// <param name="selectedCard">選択したカード（null でスキップ）</param>
        public void SelectCardReward(Card selectedCard)
        {
            if (IsUserPaused || IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
            if (currentPhase != GamePhase.CardReward)
            {
                logger?.LogWarning("[DungeonBattleManager] SelectCardReward は CardRewardフェーズ以外では無効");
                return;
            }
            dungeonSystem.OnCardSelectionCompleted(selectedCard);
            // OnReturnedToDungeon が発火してフェーズが Dungeon に戻る
        }

        /// <summary>
        /// バトル→ダンジョン遷移アニメーション完了後にプレイヤー移動を再開する
        /// DungeonBattleController が遷移・報酬処理をすべて終えた後に呼び出すこと
        /// </summary>
        public void ResumeAfterBattle()
        {
            if (IsUserPaused || IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
            dungeonSystem.ResumePlayerMovement();
        }

        /// <summary>
        /// ダンジョン失敗時
        /// </summary>
        private void OnDungeonFailed()
        {
            logger?.LogInfo("[DungeonBattleManager] ダンジョン失敗 - ゲームオーバー");
            currentPhase = GamePhase.GameOver;
            OnGameOver?.Invoke();
        }

        /// <summary>
        /// ダンジョンクリア時
        /// </summary>
        private void OnDungeonCleared()
        {
            logger?.LogInfo("[DungeonBattleManager] ダンジョンクリア！");
            currentPhase = GamePhase.DungeonCleared;
            OnDungeonClearCompleted?.Invoke(dungeonSystem.GetObtainedCards());
        }

        /// <summary>クリア時の獲得履歴から持ち帰り候補を検証する。帰還の確定処理ではない。</summary>
        public bool TryValidateCarryHomeSelection(IReadOnlyList<Card> selected,
            out IReadOnlyList<Card> validated, out string error)
        {
            validated = null; error = null;
            if (currentPhase != GamePhase.DungeonCleared || IsUserPaused || IsSuspended || dungeonSystem.IsCheckpointBlocked)
            { error = "クリア確定後に選択してください"; return false; }
            return CarryHomeSelectionRules.TryValidate(dungeonSystem.GetObtainedCards(), selected, out validated, out error);
        }

        /// <summary>
        /// プレイヤーアクション：ジャンプ
        /// </summary>
        public void Jump()
        {
            if (IsUserPaused || IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
            if (currentPhase == GamePhase.Dungeon)
            {
                dungeonSystem.Jump();
            }
        }

        /// <summary>
        /// プレイヤーアクション：ダッシュ開始
        /// </summary>
        public void StartDash()
        {
            if (IsUserPaused || IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
            if (currentPhase == GamePhase.Dungeon)
            {
                dungeonSystem.StartDash();
            }
        }

        /// <summary>
        /// プレイヤーアクション：ダッシュ停止
        /// </summary>
        public void StopDash()
        {
            if (IsUserPaused || IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
            if (currentPhase == GamePhase.Dungeon)
            {
                dungeonSystem.StopDash();
            }
        }

        public bool IsSuspended { get; private set; }
        public bool SuspendForCheckpoint()
        {
            if (dungeonSystem.IsCheckpointBlocked || currentPhase == GamePhase.GameOver || currentPhase == GamePhase.DungeonCleared) return false;
            IsSuspended = true; dungeonSystem.SuspendForCheckpoint(); return true;
        }
        public void ActivateCheckpoint() { if (!IsSuspended) dungeonSystem.ActivateCheckpoint(); }
        public bool RetryCheckpoint() => !IsSuspended && dungeonSystem.RetryCheckpoint();
        public static bool TryRestoreCheckpoint(DungeonCheckpointSession session, IRandomProvider random, IGameLogger logger,
            out DungeonBattleManager manager, out string error, ExceedSystem exceed = null, TuningSystem tuning = null, FusionSystem fusion = null, ICardDatabase cardDatabase = null)
        {
            manager = null;
            if (!session.TryRead(out var restored, out error)) return false;
            manager = new DungeonBattleManager(restored.Map, restored.Player, restored.Deck, random, logger,
                exceed, tuning, fusion, checkpoints: session, restoration: restored, cardDatabase: cardDatabase);
            return true;
        }

        // ========== イベント ==========
        /// <summary>バトル開始時（カメラ遷移などに使用）</summary>
        public event Action<BattleSystem, ContactInfo> OnBattleStarted;

        /// <summary>バトル終了時（View遷移のトリガー）</summary>
        public event Action OnBattleEnded;
        public event Action OnFloorCompleted;

        /// <summary>カード報酬選択画面表示時（選択肢リスト付き）</summary>
        public event Action<IReadOnlyList<Card>> OnCardRewardPresented;

        /// <summary>ゲームオーバー時</summary>
        public event Action OnGameOver;

        /// <summary>ダンジョンクリア時</summary>
        public event Action<IReadOnlyList<Card>> OnDungeonClearCompleted;
    }

    /// <summary>
    /// ゲームフェーズ
    /// </summary>
    public enum GamePhase
    {
        /// <summary>ダンジョン探索中</summary>
        Dungeon,

        /// <summary>ダンジョンからバトルへの遷移中</summary>
        BattleTransition,

        /// <summary>バトル中</summary>
        Battle,

        /// <summary>バトルからダンジョンへの遷移中</summary>
        DungeonTransition,

        /// <summary>カード報酬選択中</summary>
        CardReward,

        /// <summary>ダンジョンクリア</summary>
        DungeonCleared,

        /// <summary>ゲームオーバー</summary>
        GameOver
    }
}

namespace Olca.Core.Logic.Integration
{
    /// <summary>描画と独立した60 Hz論理時計。負荷上限で処理しきれないフレームは保持する。</summary>
    public sealed class LogicalFrameClock
    {
        public const int FramesPerSecond = 60;
        public const long WallTicksPerSecond = 10000000;
        private long credit;
        private bool advancing;
        public long Frame { get; private set; }
        public long PendingFrames => credit / WallTicksPerSecond;
        public long InputFrame => Frame + PendingFrames + 1;
        public double Interpolation => (double)(credit % WallTicksPerSecond) / WallTicksPerSecond;
        public void Accumulate(long elapsedWallTicks)
        {
            if (elapsedWallTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedWallTicks));
            credit = checked(credit + elapsedWallTicks * FramesPerSecond);
        }
        public int Drain(Action<long, int> tick, int budget = 240, Func<bool> canAdvance = null)
        {
            if (tick == null) throw new ArgumentNullException(nameof(tick));
            if (budget < 1) throw new ArgumentOutOfRangeException(nameof(budget));
            if (advancing) return 0;
            advancing = true;
            int count = 0;
            try
            {
                while (PendingFrames > 0 && count < budget && (canAdvance == null || canAdvance()))
                {
                    credit -= WallTicksPerSecond;
                    long previous = Frame++;
                    // 既存ms APIへの互換境界。16ms固定にせず、60フレームで必ず1000ms進める。
                    int milliseconds = (int)(Frame * 1000 / FramesPerSecond - previous * 1000 / FramesPerSecond);
                    tick(Frame, milliseconds);
                    count++;
                }
            }
            finally { advancing = false; }
            return count;
        }
        public static int DurationFrames(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            // SerializeFieldの0.2fなどの表現誤差だけを除く（1e-5フレーム未満）。
            return seconds == 0 ? 0 : Math.Max(1, checked((int)Math.Ceiling(seconds * FramesPerSecond - 1e-5)));
        }
    }

    /// <summary>1論理フレームの入力。押下イベントはそのフレームだけ有効。</summary>
    public sealed class LogicalInput : IInputProvider
    {
        public bool HasInputSample { get; set; }
        public bool AnyKey { get; set; }
        public float Horizontal { get; set; }
        public float HorizontalDown { get; set; }
        public float Vertical { get; set; }
        public bool JumpButtonDown { get; set; }
        public bool DashButton { get; set; }
        public bool DashButtonDown { get; set; }
        public bool CardUseButtonDown { get; set; }
        public bool BasicAttackButton { get; set; }
        public float BasicAttackButtonHoldTime { get; set; }
        public bool CustomScreenButtonDown { get; set; }
        public bool MenuConfirmButtonDown { get; set; }
        public bool MenuDecideButtonDown { get; set; }
        public bool MenuCancelButtonDown { get; set; }
        public LogicalInput Copy() => (LogicalInput)MemberwiseClone();
        public void ClearEdges() { HorizontalDown = 0; JumpButtonDown = DashButtonDown = CardUseButtonDown = CustomScreenButtonDown = MenuConfirmButtonDown = MenuDecideButtonDown = MenuCancelButtonDown = false; }
    }

    /// <summary>入力を論理フレームに割り当て、描画停止後の追いつき中にも一度だけ消費する。</summary>
    public sealed class LogicalInputQueue
    {
        private readonly Queue<KeyValuePair<long, LogicalInput>> pending = new Queue<KeyValuePair<long, LogicalInput>>();
        private LogicalInput held = new LogicalInput();
        private long lastQueued, heldFrames;
        public void Enqueue(long frame, LogicalInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            // 同一フレームのイベントはそこで併合し、翌フレームへの遅延を追加しない。
            long target = Math.Max(frame, lastQueued);
            pending.Enqueue(new KeyValuePair<long, LogicalInput>(target, input.Copy())); lastQueued = target;
        }
        public LogicalInput Take(long frame, bool advanceHeldTime = true)
        {
            held.ClearEdges();
            while (pending.Count > 0 && pending.Peek().Key <= frame)
            {
                var next = pending.Dequeue().Value;
                // 同じ論理フレーム内で再押下されても、解放前の連続保持時間を引き継がない。
                if (!next.BasicAttackButton) heldFrames = 0;
                next.JumpButtonDown |= held.JumpButtonDown;
                next.DashButtonDown |= held.DashButtonDown;
                next.CardUseButtonDown |= held.CardUseButtonDown;
                next.CustomScreenButtonDown |= held.CustomScreenButtonDown;
                next.MenuConfirmButtonDown |= held.MenuConfirmButtonDown;
                next.MenuDecideButtonDown |= held.MenuDecideButtonDown;
                next.MenuCancelButtonDown |= held.MenuCancelButtonDown;
                if (next.HorizontalDown == 0) next.HorizontalDown = held.HorizontalDown;
                held = next;
            }
            heldFrames = held.BasicAttackButton ? heldFrames + (advanceHeldTime ? 1 : 0) : 0;
            held.BasicAttackButtonHoldTime = heldFrames / (float)LogicalFrameClock.FramesPerSecond;
            return held.Copy();
        }
        public void Release()
        {
            pending.Clear(); held = new LogicalInput(); heldFrames = 0; lastQueued = 0;
        }
    }
}
