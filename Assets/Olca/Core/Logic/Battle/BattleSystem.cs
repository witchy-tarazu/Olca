using Olca.Core.Logic.CardEffects;
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// バトルシステムのメインロジック
    /// </summary>
    public class BattleSystem
    {
        private readonly PlayerCharacter player;
        private readonly List<Enemy> enemies;
        private readonly Dictionary<Enemy, Action> enemyDeathHandlers = new Dictionary<Enemy, Action>();
        private readonly Dictionary<Enemy, Action<Vector2Int>> enemyPositionHandlers = new Dictionary<Enemy, Action<Vector2Int>>();
        private Vector2Int previousPlayerPosition;
        private Vector2Int previousPlayerLogicalPosition;
        private readonly BattleField battleField;
        private readonly CustomGaugeSystem customGauge;
        private readonly CustomScreenSystem customScreen;
        private readonly CardUsageSystem cardUsage;
        private readonly PlayerMovementSystem playerMovement;
        private readonly PanelEffectSystem panelEffects;
        private readonly EnemyAISystem enemyAI;
        private readonly DelayedEffectSystem delayedEffects;
        private readonly IRandomProvider randomProvider;
        private readonly IGameLogger logger;

        private BattlePauseReason pauseReasons;
        private BattleState state;
        private long actionTimeUnits;
        private long actionFrame;
        internal long ActionFrame => actionFrame;
        /// <summary>表示が参照する戦闘の論理時刻。停止中は進まない。</summary>
        public long LogicalFrame => actionFrame;
        private int resolutionDepth;
        private bool resolutionRequested;

        /// <summary>プレイヤーキャラクター</summary>
        public PlayerCharacter Player => player;

        /// <summary>敵リスト</summary>
        public IReadOnlyList<Enemy> Enemies => enemies.AsReadOnly();

        /// <summary>バトルフィールド</summary>
        public BattleField Field => battleField;

        /// <summary>カスタムゲージ</summary>
        public CustomGaugeSystem CustomGauge => customGauge;

        /// <summary>カスタム画面システム</summary>
        public CustomScreenSystem CustomScreen => customScreen;
        public TransformationSelectionState Transformations { get; }

        /// <summary>カード使用システム</summary>
        public CardUsageSystem CardUsage => cardUsage;

        /// <summary>プレイヤー移動システム</summary>
        public PlayerMovementSystem PlayerMovement => playerMovement;

        /// <summary>既定の敵AI・プレイヤー基本攻撃・マグマが発生させる共通無敵。0は旧設定。</summary>
        public int DefaultHitInvincibilityMs { get; private set; }
        public void ConfigureDefaultHitInvincibility(int durationMs)
        {
            if (durationMs < 0) throw new ArgumentOutOfRangeException(nameof(durationMs));
            if (actionTimeUnits != 0 || IsGameOver) throw new InvalidOperationException("共通被弾無敵は戦闘更新前に設定してください");
            DefaultHitInvincibilityMs = durationMs;
            panelEffects.ConfigureHitInvincibility(durationMs);
        }


        /// <summary>パネル効果システム</summary>
        public PanelEffectSystem PanelEffects => panelEffects;

        /// <summary>遅延効果システム</summary>
        public DelayedEffectSystem DelayedEffects => delayedEffects;

        private object playerActionOwner;
        private Action cancelPlayerAction;
        private bool basicAction;
        private long recoveryStarted;
        private int? recoveryCancelFrames;
        public PlayerAttackPhase PlayerActionPhase { get; private set; }
        public bool HasPlayerAction => playerActionOwner != null;
        public bool CanCancelPlayerRecovery => PlayerActionPhase == PlayerAttackPhase.Recovery &&
            recoveryCancelFrames.HasValue && actionTimeUnits - recoveryStarted >= recoveryCancelFrames.Value * 50L;
        public int RecoveryElapsedFrames => PlayerActionPhase == PlayerAttackPhase.Recovery ? (int)((actionTimeUnits - recoveryStarted) / 50) : 0;
        private PlayerAttackTiming basicTiming = new PlayerAttackTiming();
        private readonly Dictionary<string, PlayerAttackTiming> cardTimings = new Dictionary<string, PlayerAttackTiming>();
        public void ConfigureBasicAttackTiming(PlayerAttackTiming timing)
        { if (HasPlayerAction) throw new InvalidOperationException("動作中は時間定義を変更できません"); basicTiming = timing ?? throw new ArgumentNullException(nameof(timing)); }
        public void ConfigureCardAttackTiming(string id, PlayerAttackTiming timing)
        { if (HasPlayerAction) throw new InvalidOperationException("動作中は時間定義を変更できません"); if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("カードIDが必要です"); cardTimings[id] = timing ?? throw new ArgumentNullException(nameof(timing)); }
        internal PlayerAttackTiming CardTiming(Card card) => cardTimings.TryGetValue(card.Id, out var timing) ? timing : null;
        internal object BeginPlayerAction(bool basic, Action cancel)
        {
            if (HasPlayerAction) throw new InvalidOperationException("行動は同時に開始できません");
            playerActionOwner = new object(); basicAction = basic; cancelPlayerAction = cancel;
            PlayerActionPhase = PlayerAttackPhase.Startup; return playerActionOwner;
        }
        internal void SetPlayerActionPhase(object owner, PlayerAttackPhase phase, int? cancelAfter = null)
        {
            if (!ReferenceEquals(owner, playerActionOwner)) return;
            PlayerActionPhase = phase;
            if (phase == PlayerAttackPhase.Recovery) { recoveryStarted = actionTimeUnits; recoveryCancelFrames = cancelAfter; }
        }
        internal void EndPlayerAction(object owner)
        {
            if (!ReferenceEquals(owner, playerActionOwner)) return;
            playerActionOwner = null; cancelPlayerAction = null; PlayerActionPhase = PlayerAttackPhase.None; recoveryCancelFrames = null;
        }
        public bool TryPreparePlayerAttack()
        {
            if (!CanPlayerBasicAttack) return false;
            if (HasPlayerAction) cancelPlayerAction?.Invoke();
            return !HasPlayerAction;
        }
        public void CancelBasicAction() => InterruptBasicAction();
        private void InterruptBasicAction() { if (basicAction && HasPlayerAction) cancelPlayerAction?.Invoke(); }
        public void CancelBasicStartupForFormChange()
        { if (PlayerActionPhase == PlayerAttackPhase.Startup) InterruptBasicAction(); }
        public bool TryStartPlayerBasicAttack(bool charged, int fallbackChargeMultiplier = 2)
        {
            if (fallbackChargeMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(fallbackChargeMultiplier));
            if (!TryPreparePlayerAttack()) return false;
            var attack = Transformations.ActiveAttack;
            long percent = attack != null ? (charged ? attack.ChargedPowerPercent : attack.NormalPowerPercent) : charged ? 100L * fallbackChargeMultiplier : 100;
            var timing = attack?.Timing ?? basicTiming;
            ResolveBasicActionAsync(percent, timing, Transformations.ActiveId).Forget();
            return true;
        }
        private async UniTask ResolveBasicActionAsync(long percent, PlayerAttackTiming timing, string form)
        {
            var cts = new CancellationTokenSource(); var owner = BeginPlayerAction(true, () => cts.Cancel());
            try {
                await WaitForActionFramesAsync(timing.StartupFrames, cts.Token);
                if (IsGameOver || player.IsActionPrevented || Transformations.ActiveId != form) return;
                SetPlayerActionPhase(owner, PlayerAttackPhase.Executing);
                foreach (var enemy in enemies)
                    if (!enemy.IsDead && enemy.GridPosition.X == player.GridPosition.X + 1 && enemy.GridPosition.Y == player.GridPosition.Y) { ApplyPlayerBasicAttack(enemy, percent); break; }
                if (IsGameOver) return;
                SetPlayerActionPhase(owner, PlayerAttackPhase.Recovery, timing.CancelAfterFrames);
                await WaitForActionFramesAsync(timing.RecoveryFrames, cts.Token);
            } catch (OperationCanceledException) { }
            finally { EndPlayerAction(owner); cts.Dispose(); }
        }

        /// <summary>バトル状態</summary>
        public BattleState State => state;

        /// <summary>一時停止中か</summary>
        public bool IsPaused => pauseReasons != BattlePauseReason.None;

        /// <summary>バトル終了したか</summary>
        public bool IsGameOver => state == BattleState.Victory || state == BattleState.Defeat;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public BattleSystem(
            PlayerCharacter player,
            List<Enemy> enemies,
            Deck deck,
            float customGaugeSpeed = 1.0f,
            IRandomProvider randomProvider = null,
            IGameLogger logger = null,
            ExceedSystem exceedSystem = null,
            TuningSystem tuningSystem = null,
            FusionSystem fusionSystem = null,
            IEnumerable<Vector2Int> initialPermanentHoles = null, ICardDatabase cardDatabase = null)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            this.battleField = new BattleField(initialPermanentHoles);
            this.customGauge = new CustomGaugeSystem(customGaugeSpeed);
            this.customScreen = new CustomScreenSystem(deck,
                exceedSystem: exceedSystem,
                tuningSystem: tuningSystem,
                fusionSystem: fusionSystem);
            Transformations = new TransformationSelectionState(player, customScreen);
            customScreen.OnOpening += OnTransformationPhaseOpening;
            this.cardUsage = new CardUsageSystem(this, logger, cardDatabase);
            this.playerMovement = new PlayerMovementSystem(player, battleField, logger, () => !IsPaused && !IsGameOver && !HasPlayerAction && cardUsage.InFlightCard == null && !player.IsActionPrevented);
            this.panelEffects = new PanelEffectSystem(battleField, logger);
            this.enemyAI = new EnemyAISystem(logger);
            this.delayedEffects = new DelayedEffectSystem(this);
            this.randomProvider = randomProvider;
            this.logger = logger;
            this.state = BattleState.InProgress;

            battleField.SetOccupancyQuery(position =>
                (!player.IsDead && player.GridPosition == position) ||
                this.enemies.Exists(enemy => !enemy.IsDead && enemy.GridPosition == position));
            previousPlayerPosition = player.GridPosition;
            previousPlayerLogicalPosition = player.GetLogicalPosition();
            player.OnPositionChanged += OnPlayerPositionChanged;

            // プレイヤー死亡時の処理
            player.IsOnGrass = () => battleField.GetPanelType(player.GridPosition) == PanelType.Grass;
            player.OnDamageProcessingStarted += BeginResolution;
            player.OnDamageProcessingCompleted += EndResolution;
            player.OnDeath += OnPlayerDeath;

            // プレイヤーのけぞり時の処理（カード効果の中断）
            player.OnStaggered += OnPlayerStaggered;

            // 初期参加者のAIは既存設定を維持する。
            foreach (var enemy in this.enemies)
                RegisterEnemy(enemy, new List<IEnemyAIAction> { new BasicAttackAction(attackDamage: 10, cooldown: 2.0f,
                    onHit: target => { if (!target.IsDead) target.SetInvincible(DefaultHitInvincibilityMs); }) });

            // カスタム画面イベントの購読
            customScreen.OnConfirmed += OnCustomScreenConfirmed;
            customScreen.OnScreenClosed += OnCustomScreenClosed;

        }

        private void OnPlayerPositionChanged(Vector2Int position)
        {
            var previous = previousPlayerPosition;
            previousPlayerPosition = position;
            var logicalPosition = player.GetLogicalPosition();
            bool enteredLogicalPosition = previousPlayerLogicalPosition != logicalPosition;
            previousPlayerLogicalPosition = logicalPosition;
            if (!IsGameOver && !player.IsDead && (previous != position || enteredLogicalPosition))
            {
                BeginResolution();
                try
                {
                    // 一時移動先からの離脱・ヒビ破壊は従来の占有通知を維持する。
                    if (previous != position) panelEffects.ApplyOnLeaveEffect(player.TerrainState, previous);
                    // 一時ヒット座標だけの変更を新しい接地へ転用しない。
                    if (enteredLogicalPosition)
                    {
                        if (enemies.Exists(enemy => !enemy.IsDead && enemy.GridPosition == logicalPosition))
                            panelEffects.ApplyOnEnemyEnterEffect(enemies, player, logicalPosition);
                        else panelEffects.ApplyOnEnterEffect(player, logicalPosition);
                    }
                }
                finally { EndResolution(); }
            }
        }

        private void RegisterEnemy(Enemy enemy, List<IEnemyAIAction> actions)
        {
            enemy.IsOnGrass = () => battleField.GetPanelType(enemy.GridPosition) == PanelType.Grass;
            enemyAI.RegisterEnemy(enemy, actions);
            enemy.OnDamageProcessingStarted += BeginResolution;
            enemy.OnDamageProcessingCompleted += EndResolution;
            var previous = enemy.GridPosition;
            Action<Vector2Int> moved = position =>
            {
                var from = previous;
                previous = position;
                if (!IsGameOver && !enemy.IsDead && from != position)
                {
                    BeginResolution();
                    try
                    {
                        panelEffects.ApplyOnLeaveEffect(enemy.TerrainState, from);
                        panelEffects.ApplyOnEnemyEnterEffect(enemies, player, position);
                    }
                    finally { EndResolution(); }
                }
            };
            enemyPositionHandlers.Add(enemy, moved);
            enemy.OnPositionChanged += moved;
            Action handler = null;
            handler = () =>
            {
                // HP0は占有から既に除外されている。再通知で破壊を繰り返さない。
                enemy.OnPositionChanged -= moved;
                enemy.OnDeath -= handler;
                panelEffects.ApplyOnLeaveEffect(enemy.TerrainState, enemy.GridPosition);
                enemyAI.UnregisterEnemy(enemy);
                CheckVictoryCondition();
            };
            enemyDeathHandlers.Add(enemy, handler);
            enemy.OnDeath += handler;
        }

        /// <summary>呼び出し側が定義した召喚物とAIを、配置順の末尾に登録する。</summary>
        public void AddSummon(Enemy summon, List<IEnemyAIAction> actions)
        {
            if (summon == null) throw new ArgumentNullException(nameof(summon));
            if (IsGameOver) throw new InvalidOperationException("終了した戦闘には召喚できません");
            if (!summon.IsSummoned || summon.IsDead || !battleField.IsInBounds(summon.GridPosition) || enemies.Contains(summon))
                throw new ArgumentException("生存中で未登録の召喚物をフィールド内に指定してください", nameof(summon));
            // AI登録の検証に失敗した場合は参加者リストへ追加しない。
            RegisterEnemy(summon, actions);
            enemies.Add(summon);
            if (statusFrameParticipants != null)
            { statusFrameParticipants.Add(summon); summon.BeginStatusFrame(); summon.BeginHpFrame(); }
            OnSummonAdded?.Invoke(summon);
        }

        /// <summary>表示層へ戦闘中の追加を通知する。</summary>
        public event Action<Enemy> OnSummonAdded;

        /// <summary>
        /// バトルを更新（毎フレーム呼び出す）- 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">前フレームからの経過時間（ミリ秒）</param>
        /// <param name="input">入力プロバイダー（オプショナル）</param>
        private bool logicalFrameTick;
        private List<Enemy> statusFrameParticipants;
        /// <summary>入力による付与も含め、状態異常の有効化・解除境界を一論理フレームにまとめる。</summary>
        public void RunLogicalFrame(Action action)
        {
            if (statusFrameParticipants != null || IsGameOver) { action(); return; }
            var participants = new List<Enemy>(enemies);
            statusFrameParticipants = participants;
            BeginResolution();
            player.BeginHpFrame();
            foreach (var enemy in participants) { enemy.BeginStatusFrame(); enemy.BeginHpFrame(); }
            try
            {
                if (player.BeginParalysisFrame(!IsPaused)) { cardUsage.InterruptCurrentCard(); InterruptBasicAction(); playerMovement.InterruptNormalMove(); }
                action();
            }
            finally
            {
                var committed = participants.ToArray();
                statusFrameParticipants = null;
                // 全員のHPを確定してから通知し、勝敗と保存境界はその後に処理する。
                player.CommitHpFrame();
                player.EndParalysisFrame(!IsPaused);
                foreach (var enemy in committed) enemy.CommitHpFrame();
                foreach (var enemy in committed)
                {
                    if (player.IsDead) enemy.CancelStatusFrame();
                    else enemy.EndStatusFrame();
                }
                try
                {
                    player.PublishHpFrame();
                    foreach (var enemy in committed) enemy.PublishHpFrame();
                }
                finally { EndResolution(); }
            }
        }
        public void TickFrame(int compatibleMilliseconds, IInputProvider input = null)
        {
            RunLogicalFrame(() => {
                logicalFrameTick = true;
                try { Tick(compatibleMilliseconds, input); }
                finally { logicalFrameTick = false; }
            });
        }

        public void Tick(int deltaTimeMs, IInputProvider input = null)
        {
            if (IsPaused || IsGameOver) return;
            if (statusFrameParticipants == null && (player.HasParalysisState || player.HasPoisonState)) { RunLogicalFrame(() => Tick(deltaTimeMs, input)); return; }

            // 追加登録・通知は即時だが、Tick途中の召喚へ過去の経過時間を渡さない。
            // 状態異常とAIは同じ開始時参加者だけを更新する。
            var tickParticipants = enemies.ToArray();
            BeginResolution();
            try
            {
                actionTimeUnits += logicalFrameTick ? 50 : deltaTimeMs * 3L;
                if (logicalFrameTick) actionFrame++;
                AdvanceActionWaits();
                if (IsGameOver || IsPaused) return;
                // カスタムゲージを更新
                if (logicalFrameTick) customGauge.UpdateFrame(); else customGauge.Update(deltaTimeMs);

                // 入力遮断中も自動滑り・移動待機時間は進める。能動入力だけを分離する。
                playerMovement.Tick(deltaTimeMs, input, enemies, logicalFrameTick);
                panelEffects.Tick(deltaTimeMs, player);

                // 遅延効果を更新（投擲物の着弾など）
                delayedEffects.Update(deltaTimeMs, logicalFrameTick);

                // プレイヤーの無敵時間ティック処理
                if (logicalFrameTick) player.TickInvincibleFrame(); else player.TickInvincible(deltaTimeMs);
                player.TickStagger(deltaTimeMs, logicalFrameTick);
                player.TickParalysis(deltaTimeMs, logicalFrameTick);
                player.TickPoisons(deltaTimeMs, logicalFrameTick);
                player.TickProtection(deltaTimeMs, logicalFrameTick);
                player.TickPoiseRecovery(deltaTimeMs, logicalFrameTick);

                // 敵の状態異常ティック処理（毒ダメージ等）
                foreach (var enemy in tickParticipants)
                {
                    if (enemy.IsDead) continue;
                    enemy.TickProtection(deltaTimeMs, logicalFrameTick);
                    enemy.TickInvincible(deltaTimeMs, logicalFrameTick);
                    enemy.TickPoiseRecovery(deltaTimeMs, logicalFrameTick);
                    enemy.TickStatusesAndApply(deltaTimeMs, logicalFrameTick);
                }

                // 敵のAI更新
                enemyAI.Tick(deltaTimeMs, player, battleField, randomProvider, tickParticipants, logicalFrameTick);
            }
            finally { EndResolution(); }
        }

        /// <summary>
        /// 一時停止（カスタム画面を開く時）
        /// </summary>
        public void Pause(BattlePauseReason reason = BattlePauseReason.CustomScreen)
        {
            pauseReasons |= reason;
            logger?.LogInfo("バトル一時停止");
        }

        /// <summary>
        /// 再開（カスタム画面を閉じた時）
        /// </summary>
        public void Resume(BattlePauseReason reason = BattlePauseReason.CustomScreen)
        {
            pauseReasons &= ~reason;
            logger?.LogInfo("バトル再開");
        }

        /// <summary>
        /// 先制攻撃を設定する（ダッシュ接敵の先制攻撃権）
        /// カスタムゲージを満タンにして、プレイヤーがすぐに行動できる状態にする
        /// </summary>
        public void SetFirstStrike()
        {
            customGauge.SetGaugeMs(CustomGaugeSystem.MAX_GAUGE_MS);
            logger?.LogInfo("[BattleSystem] 先制攻撃: カスタムゲージ満タン");
        }

        /// <summary>
        /// プレイヤーの基本攻撃
        /// </summary>
        public void PlayerBasicAttack(Enemy target)
        { if (!HasPlayerAction) ApplyPlayerBasicAttack(target, Transformations.ActiveAttack?.NormalPowerPercent ?? 100); }
        private void ApplyPlayerBasicAttack(Enemy target, long powerPercent)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.IsDead) return;

            if (IsGameOver || IsPaused || cardUsage.InFlightCard != null || playerMovement.IsNormalMoving || player.IsActionPrevented) return;
            BeginResolution();
            try
            {
                int damage = (int)Math.Min(int.MaxValue, (decimal)player.AttackPower * powerPercent / 100);
                target.TakeDamage(damage, Transformations.ActiveAttack?.Element ?? ElementType.None, hitInvincibilityMs: DefaultHitInvincibilityMs);

                logger?.LogInfo($"プレイヤーが{target.Name}に{damage}ダメージを与えた");
                OnPlayerAttack?.Invoke(target, damage);
            }
            finally { EndResolution(); }
        }

        // 暫定通常攻撃: 既存の右向きグリッドで前方1マスの先配置の生存敵1体。
        public bool CanPlayerBasicAttack => !IsGameOver && !IsPaused && !player.IsDead &&
            (cardUsage.InFlightCard == null || CanCancelPlayerRecovery) && (!HasPlayerAction || CanCancelPlayerRecovery) &&
            !playerMovement.IsNormalMoving && !player.IsActionPrevented;
        public bool TryPlayerBasicAttackForward(int multiplier = 1)
        {
            if (multiplier < 1) throw new ArgumentOutOfRangeException(nameof(multiplier));
            long percent = (long)(Transformations.ActiveAttack?.NormalPowerPercent ?? 100) * multiplier;
            return TryPlayerBasicAttackForwardWithPercent(percent);
        }
        public bool TryPlayerBasicAttackForward(bool charged, int fallbackChargeMultiplier = 2)
        {
            if (fallbackChargeMultiplier < 1) throw new ArgumentOutOfRangeException(nameof(fallbackChargeMultiplier));
            var attack = Transformations.ActiveAttack;
            long percent = attack != null ? (charged ? attack.ChargedPowerPercent : attack.NormalPowerPercent)
                : charged ? 100L * fallbackChargeMultiplier : 100;
            return TryPlayerBasicAttackForwardWithPercent(percent);
        }
        private bool TryPlayerBasicAttackForwardWithPercent(long powerPercent)
        {
            if (HasPlayerAction || !CanPlayerBasicAttack) return false;
            foreach (var enemy in enemies)
                if (!enemy.IsDead && enemy.GridPosition.X == player.GridPosition.X + 1 && enemy.GridPosition.Y == player.GridPosition.Y) {
                    ApplyPlayerBasicAttack(enemy, powerPercent); return true;
                }
            return false;
        }

        private sealed class ActionWait
        {
            public long Deadline;
            public readonly UniTaskCompletionSource Completion = new UniTaskCompletionSource();
            public CancellationTokenRegistration Registration;
        }
        private readonly List<ActionWait> actionWaits = new List<ActionWait>();

        // Unity PlayerLoopのWaitUntilを使わず、期限に達した論理更新内で継続する。
        internal UniTask WaitForActionAsync(float seconds, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (IsGameOver) return UniTask.FromCanceled(new CancellationToken(true));
            if (seconds <= 0) return UniTask.CompletedTask;
            // 1フレーム=50単位。float秒の表現誤差のみを除き、入力境界で一度だけ変換する。
            // 旧ms APIと交互に呼ばれても同じ時計で期限を判定する。
            var wait = new ActionWait {
                Deadline = actionTimeUnits + Math.Max(1, (long)Math.Ceiling(seconds * 3000d - 0.0005d))
            };
            actionWaits.Add(wait);
            if (ct.CanBeCanceled) wait.Registration = ct.Register(() => wait.Completion.TrySetCanceled(ct));
            return wait.Completion.Task;
        }
        internal UniTask WaitForActionFramesAsync(int frames, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (IsGameOver) return UniTask.FromCanceled(new CancellationToken(true));
            if (frames <= 0) return UniTask.CompletedTask;
            var wait = new ActionWait { Deadline = actionTimeUnits + frames * 50L };
            actionWaits.Add(wait);
            if (ct.CanBeCanceled) wait.Registration = ct.Register(() => wait.Completion.TrySetCanceled(ct));
            return wait.Completion.Task;
        }
        private void AdvanceActionWaits()
        {
            // 継続内で追加された次の待機には、このフレームの時間を二重適用しない。
            foreach (var wait in actionWaits.ToArray())
            {
                if (wait.Completion.Task.Status != UniTaskStatus.Pending || wait.Deadline <= actionTimeUnits || IsGameOver)
                {
                    actionWaits.Remove(wait); wait.Registration.Dispose();
                    if (IsGameOver) wait.Completion.TrySetCanceled(); else wait.Completion.TrySetResult();
                }
            }
        }

        /// <summary>
        /// カスタム画面を開く
        /// </summary>
        public void OpenCustomScreen(IRandomProvider random)
        {
            if (IsGameOver || !customGauge.IsFull)
            {
                logger?.LogWarning("カスタムゲージが満タンではありません");
                return;
            }

            if (customScreen.IsOpen) return;
            // 未使用カードを取得してカスタム画面に渡す
            var remainingCards = cardUsage.GetRemainingCards();
            customScreen.Open(remainingCards, random);
            Pause();
        }

        private void OnTransformationPhaseOpening()
        {
            if (!IsGameOver) player.AdvanceTransformationPhase();
        }

        /// <summary>
        /// カスタム画面で確定ボタンが押された時の処理（アニメーション開始）
        /// </summary>
        private void OnCustomScreenConfirmed(List<Card> selectedCards)
        {
            // 実行中カードはCardUsageが保護し、ここでは待機分だけを置き換える。
            if (!IsGameOver)
            {
                cardUsage.EnqueueCards(selectedCards);
            }

            // ゲージをリセット
            customGauge.Reset();
        }

        /// <summary>
        /// カスタム画面を閉じた時の処理（アニメーション完了後）
        /// </summary>
        private void OnCustomScreenClosed(List<Card> selectedCards)
        {
            // 未確定で閉じる場合もゲージを消費する。通常の確定時リセットを重複通知しない。
            if (customGauge.CurrentGaugeMs != 0) customGauge.Reset();
            // バトル再開
            Resume();
        }

        /// <summary>
        /// 現在のカードを使用
        /// </summary>
        public bool UseCurrentCard()
        {
            return cardUsage.TryUseCurrentCard();
        }

        /// <summary>
        /// プレイヤー死亡時の処理
        /// </summary>
        private void OnPlayerDeath()
        {
            panelEffects.ApplyOnLeaveEffect(player.TerrainState, player.GridPosition);
            RequestResolution();
        }

        /// <summary>
        /// プレイヤーのけぞり時の処理（現在実行中のカード効果を中断）
        /// </summary>
        private void OnPlayerStaggered()
        {
            cardUsage.InterruptCurrentCard();
            InterruptBasicAction();
            playerMovement.InterruptNormalMove();
            logger?.LogInfo("プレイヤーがのけぞり - カード効果を中断");
        }

        /// <summary>
        /// 勝利条件チェック（全敵撃破）
        /// </summary>
        private void CheckVictoryCondition()
        {
            RequestResolution();
        }

        // 時間窓は作らず、既存Tickまたは同期ダメージ/反撃の呼出しが戻るまで判定を保留する。
        private void BeginResolution() => resolutionDepth++;

        private void EndResolution()
        {
            resolutionDepth--;
            ResolveBattleEnd();
        }

        private void RequestResolution()
        {
            if (IsGameOver) return;
            resolutionRequested = true;
            ResolveBattleEnd();
        }

        private void ResolveBattleEnd()
        {
            if (resolutionDepth != 0 || !resolutionRequested || IsGameOver) return;
            resolutionRequested = false;

            // 全体仕様第7節: 同じ処理単位で両者HP0なら敗北が優先。
            if (player.IsDead)
            {
                state = BattleState.Defeat;
            }
            else
            {
                if (!enemies.Exists(enemy => !enemy.IsSummoned) || enemies.Exists(enemy => !enemy.IsSummoned && !enemy.IsDead)) return;
                state = BattleState.Victory;
            }

            // 戦闘限定バフは勝敗通知より前に解除し、同じプレイヤーを使う次戦へ残さない。
            player.SetConcentrateBuff(1f);
            player.SetBarrier(0);
            player.TerrainState.Clear();
            foreach (var enemy in enemies)
            {
                enemy.TerrainState.Clear();
                enemy.ClearBattleParalysis();
            }
            cardUsage.Clear(); // 終了した戦闘の遅延完了が次のキューやUIを更新しない。
            ReleaseBattleParticipants();
            var result = state == BattleState.Defeat ? BattleResult.Defeat : BattleResult.Victory;
            logger?.LogInfo($"バトル終了: {result}");
            OnBattleEnd?.Invoke(result);
        }

        internal void AbandonForCheckpoint()
        {
            if (IsGameOver) return;
            state = BattleState.Defeat;
            cardUsage.Clear();
            ReleaseBattleParticipants();
            OnBattleEnd = null;
        }

        private void ReleaseBattleParticipants()
        {
            foreach (var entry in enemyDeathHandlers)
            {
                entry.Key.CancelStatusFrame();
                entry.Key.Guard.Clear();
                entry.Key.ClearProtection();
                entry.Key.ClearInvincibility();
                entry.Key.ClearPoiseRecovery();
                entry.Key.IsOnGrass = null;
                entry.Key.OnDamageProcessingStarted -= BeginResolution;
                entry.Key.OnDamageProcessingCompleted -= EndResolution;
                entry.Key.OnDeath -= entry.Value;
                enemyAI.UnregisterEnemy(entry.Key);
            }
            enemyDeathHandlers.Clear();
            foreach (var entry in enemyPositionHandlers)
                entry.Key.OnPositionChanged -= entry.Value;
            enemyPositionHandlers.Clear();
            player.OnPositionChanged -= OnPlayerPositionChanged;
            battleField.SetOccupancyQuery(null);
            battleField.SetProtectedPosition(null);
            player.IsOnGrass = null;
            player.ClearInvincibility();
            player.ClearStagger();
            player.ResetParalysis();
            InterruptBasicAction();
            player.ClearPoiseRecovery();
            player.SetAura(0);
            player.EndTransformationDefense();
            customScreen.OnOpening -= OnTransformationPhaseOpening;
            Transformations.Dispose();
            player.OnDamageProcessingStarted -= BeginResolution;
            player.OnDamageProcessingCompleted -= EndResolution;
            player.OnDeath -= OnPlayerDeath;
            player.OnStaggered -= OnPlayerStaggered;
            playerMovement.InterruptNormalMove();
            customScreen.OnConfirmed -= OnCustomScreenConfirmed;
            customScreen.OnScreenClosed -= OnCustomScreenClosed;
            enemies.RemoveAll(enemy => enemy.IsSummoned);
            battleField.ClearObstacles();
            delayedEffects.Clear();
            OnSummonAdded = null;
        }

        /// <summary>
        /// 生きている敵を取得
        /// </summary>
        public List<Enemy> GetAliveEnemies()
        {
            var aliveEnemies = new List<Enemy>();
            foreach (var enemy in enemies)
            {
                if (!enemy.IsDead)
                {
                    aliveEnemies.Add(enemy);
                }
            }
            return aliveEnemies;
        }

        /// <summary>
        /// プレイヤー攻撃時のイベント
        /// </summary>
        public event Action<Enemy, int> OnPlayerAttack;

        /// <summary>
        /// バトル終了時のイベント
        /// </summary>
        public event Action<BattleResult> OnBattleEnd;
    }

    /// <summary>
    /// バトルの状態
    /// </summary>
    public enum BattleState
    {
        /// <summary>進行中</summary>
        InProgress,

        /// <summary>勝利</summary>
        Victory,

        /// <summary>敗北</summary>
        Defeat
    }

    /// <summary>
    /// バトル結果
    /// </summary>
    public enum BattleResult
    {
        /// <summary>勝利</summary>
        Victory,

        /// <summary>敗北</summary>
        Defeat
    }
    [System.Flags]
    public enum BattlePauseReason { None = 0, CustomScreen = 1, User = 2 }
}
