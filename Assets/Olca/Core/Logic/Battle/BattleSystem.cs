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

        /// <summary>パネル効果システム</summary>
        public PanelEffectSystem PanelEffects => panelEffects;

        /// <summary>遅延効果システム</summary>
        public DelayedEffectSystem DelayedEffects => delayedEffects;

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
            IEnumerable<Vector2Int> initialPermanentHoles = null)
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
            this.cardUsage = new CardUsageSystem(this, logger);
            this.playerMovement = new PlayerMovementSystem(player, battleField, logger);
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
                RegisterEnemy(enemy, new List<IEnemyAIAction> { new BasicAttackAction(attackDamage: 10, cooldown: 2.0f) });

            // カスタム画面イベントの購読
            customScreen.OnScreenOpened += OnTransformationPhaseOpened;
            customScreen.OnConfirmed += OnCustomScreenConfirmed;
            customScreen.OnScreenClosed += OnCustomScreenClosed;

            // プレイヤー移動時にパネル効果を適用
            playerMovement.OnMoved += OnPlayerMoved;
        }

        private void OnPlayerMoved(Vector2Int newPos, Vector2Int oldPos)
        {
            panelEffects.ApplyOnEnterEffect(player, newPos);
        }

        private void OnPlayerPositionChanged(Vector2Int position)
        {
            var previous = previousPlayerPosition;
            previousPlayerPosition = position;
            if (!IsGameOver && !player.IsDead && previous != position)
                panelEffects.ApplyOnLeaveEffect(player.TerrainState, previous);
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
                    panelEffects.ApplyOnLeaveEffect(enemy.TerrainState, from);
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
        public void TickFrame(int compatibleMilliseconds, IInputProvider input = null)
        {
            logicalFrameTick = true;
            try { Tick(compatibleMilliseconds, input); }
            finally { logicalFrameTick = false; }
        }

        public void Tick(int deltaTimeMs, IInputProvider input = null)
        {
            if (IsPaused || IsGameOver) return;

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

                // 敵の状態異常ティック処理（毒ダメージ等）
                foreach (var enemy in tickParticipants)
                {
                    if (enemy.IsDead) continue;
                    int poisonDamage = logicalFrameTick ? enemy.TickStatusFrame() : enemy.TickStatus(deltaTimeMs);
                    // 毒だけでは倒さない。通常攻撃のダメージ処理にはこの下限を持ち込まない。
                    int appliedPoisonDamage = Math.Min(poisonDamage, Math.Max(0, enemy.CurrentHp - 1));
                    if (appliedPoisonDamage > 0)
                    {
                        enemy.TakeDamage(appliedPoisonDamage, ElementType.None);
                    }
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
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.IsDead) return;

            if (IsGameOver || IsPaused) return;
            BeginResolution();
            try
            {
                int damage = player.AttackPower;
                target.TakeDamage(damage, ElementType.None);

                logger?.LogInfo($"プレイヤーが{target.Name}に{damage}ダメージを与えた");
                OnPlayerAttack?.Invoke(target, damage);
            }
            finally { EndResolution(); }
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

            // 未使用カードを取得してカスタム画面に渡す
            var remainingCards = cardUsage.GetRemainingCards();
            customScreen.Open(remainingCards, random);
            Pause();
        }

        // 開き直しが実際に成立した時だけ、確定済み変身の残回数を進める。
        private void OnTransformationPhaseOpened(List<Card> hand)
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
            player.EndTransformationDefense();
            Transformations.Dispose();
            player.OnDamageProcessingStarted -= BeginResolution;
            player.OnDamageProcessingCompleted -= EndResolution;
            player.OnDeath -= OnPlayerDeath;
            player.OnStaggered -= OnPlayerStaggered;
            playerMovement.OnMoved -= OnPlayerMoved;
            customScreen.OnScreenOpened -= OnTransformationPhaseOpened;
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
