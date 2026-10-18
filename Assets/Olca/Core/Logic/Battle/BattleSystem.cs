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

        private bool isPaused; // カスタム画面中はtrue
        private BattleState state;
        private long actionTimeMs;
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
        public bool IsPaused => isPaused;

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
            this.cardUsage = new CardUsageSystem(this, logger);
            this.playerMovement = new PlayerMovementSystem(player, battleField, logger);
            this.panelEffects = new PanelEffectSystem(battleField, logger);
            this.enemyAI = new EnemyAISystem(logger);
            this.delayedEffects = new DelayedEffectSystem(this);
            this.randomProvider = randomProvider;
            this.logger = logger;
            this.state = BattleState.InProgress;

            // プレイヤー死亡時の処理
            player.OnDamageProcessingStarted += BeginResolution;
            player.OnDamageProcessingCompleted += EndResolution;
            player.OnDeath += OnPlayerDeath;

            // プレイヤーのけぞり時の処理（カード効果の中断）
            player.OnStaggered += OnPlayerStaggered;

            // 初期参加者のAIは既存設定を維持する。
            foreach (var enemy in this.enemies)
                RegisterEnemy(enemy, new List<IEnemyAIAction> { new BasicAttackAction(attackDamage: 10, cooldown: 2.0f) });

            // カスタム画面イベントの購読
            customScreen.OnConfirmed += OnCustomScreenConfirmed;
            customScreen.OnScreenClosed += OnCustomScreenClosed;

            // プレイヤー移動時にパネル効果を適用
            playerMovement.OnMoved += OnPlayerMoved;
        }

        private void OnPlayerMoved(Vector2Int newPos, Vector2Int oldPos)
        {
            panelEffects.ApplyOnLeaveEffect(player, oldPos);
            panelEffects.ApplyOnEnterEffect(player, newPos);
        }

        private void RegisterEnemy(Enemy enemy, List<IEnemyAIAction> actions)
        {
            enemyAI.RegisterEnemy(enemy, actions);
            enemy.OnDamageProcessingStarted += BeginResolution;
            enemy.OnDamageProcessingCompleted += EndResolution;
            Action handler = () => { CheckVictoryCondition(); enemyAI.UnregisterEnemy(enemy); };
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
        public void Tick(int deltaTimeMs, IInputProvider input = null)
        {
            if (isPaused || IsGameOver) return;

            BeginResolution();
            try
            {
                actionTimeMs += deltaTimeMs;
                // カスタムゲージを更新
                customGauge.Update(deltaTimeMs);

                // プレイヤー移動とパネル効果を更新
                if (input != null)
                {
                    playerMovement.Tick(deltaTimeMs, input, enemies);
                    panelEffects.Tick(deltaTimeMs, player);
                }

                // 遅延効果を更新（投擲物の着弾など）
                delayedEffects.Update(deltaTimeMs);

                // プレイヤーの無敵時間ティック処理
                player.TickInvincible(deltaTimeMs);

                // 敵の状態異常ティック処理（毒ダメージ等）
                foreach (var enemy in enemies)
                {
                    if (enemy.IsDead) continue;
                    int poisonDamage = enemy.TickStatus(deltaTimeMs);
                    if (poisonDamage > 0)
                    {
                        enemy.TakeDamage(poisonDamage, ElementType.None);
                    }
                }

                // 敵のAI更新
                enemyAI.Tick(deltaTimeMs, player, battleField, randomProvider);
            }
            finally { EndResolution(); }
        }

        /// <summary>
        /// 一時停止（カスタム画面を開く時）
        /// </summary>
        public void Pause()
        {
            isPaused = true;
            logger?.LogInfo("バトル一時停止");
        }

        /// <summary>
        /// 再開（カスタム画面を閉じた時）
        /// </summary>
        public void Resume()
        {
            isPaused = false;
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

            if (IsGameOver) return;
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

        // Coreの時間は既存Tickのみで進める。Pause中の前隙/後隙は残り時間を維持する。
        internal async UniTask WaitForActionAsync(float seconds, CancellationToken ct)
        {
            long deadline = actionTimeMs + (long)Math.Ceiling(seconds * 1000d);
            await UniTask.WaitUntil(() => (!IsPaused && actionTimeMs >= deadline) || IsGameOver,
                cancellationToken: ct, cancelImmediately: true);
            ct.ThrowIfCancellationRequested();
            if (IsGameOver) throw new OperationCanceledException();
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

            player.TerrainState.Clear();
            foreach (var enemy in enemies) enemy.TerrainState.Clear();
            cardUsage.Clear(); // 終了した戦闘の遅延完了が次のキューやUIを更新しない。
            ReleaseBattleParticipants();
            var result = state == BattleState.Defeat ? BattleResult.Defeat : BattleResult.Victory;
            logger?.LogInfo($"バトル終了: {result}");
            OnBattleEnd?.Invoke(result);
        }

        private void ReleaseBattleParticipants()
        {
            foreach (var entry in enemyDeathHandlers)
            {
                entry.Key.OnDamageProcessingStarted -= BeginResolution;
                entry.Key.OnDamageProcessingCompleted -= EndResolution;
                entry.Key.OnDeath -= entry.Value;
                enemyAI.UnregisterEnemy(entry.Key);
            }
            enemyDeathHandlers.Clear();
            player.OnDamageProcessingStarted -= BeginResolution;
            player.OnDamageProcessingCompleted -= EndResolution;
            player.OnDeath -= OnPlayerDeath;
            player.OnStaggered -= OnPlayerStaggered;
            playerMovement.OnMoved -= OnPlayerMoved;
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
}
