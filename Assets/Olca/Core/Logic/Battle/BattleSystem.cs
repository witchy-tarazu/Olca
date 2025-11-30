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
            float customGaugeSpeed = 1.0f,
            IGameLogger logger = null)
        {
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            this.battleField = new BattleField();
            this.customGauge = new CustomGaugeSystem(customGaugeSpeed);
            this.logger = logger;
            this.state = BattleState.InProgress;

            // プレイヤー死亡時の処理
            player.OnDeath += OnPlayerDeath;

            // 敵死亡時の処理
            foreach (var enemy in enemies)
            {
                enemy.OnDeath += () => CheckVictoryCondition();
            }
        }

        /// <summary>
        /// バトルを更新（毎フレーム呼び出す）
        /// </summary>
        /// <param name="deltaTime">前フレームからの経過時間</param>
        public void Tick(float deltaTime)
        {
            if (isPaused || IsGameOver) return;

            // カスタムゲージを更新
            customGauge.Update(deltaTime);

            // TODO: 敵のAI更新など
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
        /// カードを使用
        /// </summary>
        public void UseCard(Card card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));

            logger?.LogInfo($"カード使用: {card.Name}");
            OnCardUsed?.Invoke(card);

            // TODO: カード効果の実行（フェーズ1後半で実装）
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
        /// カード使用時のイベント
        /// </summary>
        public event Action<Card> OnCardUsed;

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
