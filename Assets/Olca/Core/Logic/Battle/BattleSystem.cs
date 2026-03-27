using System;
using System.Collections.Generic;
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
            FusionSystem fusionSystem = null)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            this.battleField = new BattleField();
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
            player.OnDeath += OnPlayerDeath;

            // プレイヤーのけぞり時の処理（カード効果の中断）
            player.OnStaggered += OnPlayerStaggered;

            // 敵死亡時の処理と敵AI登録
            foreach (var enemy in enemies)
            {
                enemy.OnDeath += () =>
                {
                    CheckVictoryCondition();
                    enemyAI.UnregisterEnemy(enemy);
                };

                // 敵をAI管理に登録（基本攻撃のみ）
                var actions = new List<IEnemyAIAction>
                {
                    new BasicAttackAction(attackDamage: 10, cooldown: 2.0f)
                };
                enemyAI.RegisterEnemy(enemy, actions);
            }

            // カスタム画面イベントの購読
            customScreen.OnConfirmed += OnCustomScreenConfirmed;
            customScreen.OnScreenClosed += OnCustomScreenClosed;

            // プレイヤー移動時にパネル効果を適用
            playerMovement.OnMoved += (newPos, oldPos) =>
            {
                panelEffects.ApplyOnLeaveEffect(player, oldPos);
                panelEffects.ApplyOnEnterEffect(player, newPos);
            };
        }

        /// <summary>
        /// バトルを更新（毎フレーム呼び出す）- 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">前フレームからの経過時間（ミリ秒）</param>
        /// <param name="input">入力プロバイダー（オプショナル）</param>
        public void Tick(int deltaTimeMs, IInputProvider input = null)
        {
            if (isPaused || IsGameOver) return;

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

            int damage = player.AttackPower;
            target.TakeDamage(damage, ElementType.None);

            logger?.LogInfo($"プレイヤーが{target.Name}に{damage}ダメージを与えた");
            OnPlayerAttack?.Invoke(target, damage);
        }

        /// <summary>
        /// カスタム画面を開く
        /// </summary>
        public void OpenCustomScreen(IRandomProvider random)
        {
            if (!customGauge.IsFull)
            {
                logger?.LogWarning("カスタムゲージが満タンではありません");
                return;
            }

            // 未使用カードを取得してカスタム画面に渡す
            var remainingCards = cardUsage.QueuedCards;
            customScreen.Open(remainingCards, random);
            Pause();
        }

        /// <summary>
        /// カスタム画面で確定ボタンが押された時の処理（アニメーション開始）
        /// </summary>
        private void OnCustomScreenConfirmed(List<Card> selectedCards)
        {
            // 選択されたカードをキューに追加
            if (selectedCards.Count > 0)
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
            state = BattleState.Defeat;
            logger?.LogInfo("プレイヤー死亡 - 敗北");
            OnBattleEnd?.Invoke(BattleResult.Defeat);
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
            bool allEnemiesDead = true;
            foreach (var enemy in enemies)
            {
                if (!enemy.IsDead)
                {
                    allEnemiesDead = false;
                    break;
                }
            }

            if (allEnemiesDead)
            {
                state = BattleState.Victory;
                logger?.LogInfo("全ての敵を撃破 - 勝利");
                OnBattleEnd?.Invoke(BattleResult.Victory);
            }
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
