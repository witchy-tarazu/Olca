using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Interfaces;
using Vector2Int = Olca.Core.Domain.Vector2Int;

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
        private readonly Deck masterDeck;
        private readonly IRandomProvider randomProvider;
        private readonly IGameLogger logger;

        private BattleSystem currentBattle;
        private GamePhase currentPhase;

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
            IGameLogger logger)
        {
            this.player = player;
            this.masterDeck = initialDeck;
            this.randomProvider = randomProvider;
            this.logger = logger;

            // ダンジョンシステム初期化
            dungeonSystem = new DungeonSystem(
                dungeonConfig,
                player,
                initialDeck,
                randomProvider,
                logger);

            // イベント購読
            dungeonSystem.OnBattleStart += OnDungeonBattleStart;
            dungeonSystem.OnDungeonFailed += OnDungeonFailed;
            dungeonSystem.OnDungeonCleared += OnDungeonCleared;

            currentPhase = GamePhase.Dungeon;
            logger?.LogInfo("[DungeonBattleManager] 初期化完了 - ダンジョンフェーズ開始");
        }

        /// <summary>
        /// 毎フレーム更新 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        /// <param name="input">入力プロバイダー（オプショナル）</param>
        public void Tick(int deltaTimeMs, IInputProvider input = null)
        {
            switch (currentPhase)
            {
                case GamePhase.Dungeon:
                    dungeonSystem.Tick(deltaTimeMs);
                    break;

                case GamePhase.Battle:
                    currentBattle?.Tick(deltaTimeMs, input);
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
        /// </summary>
        private void OnDungeonBattleStart(ContactInfo contactInfo)
        {
            logger?.LogInfo("[DungeonBattleManager] バトル開始");
            currentPhase = GamePhase.BattleTransition;

            // バトルシステムを初期化
            var enemies = new List<Enemy> { contactInfo.Enemy };
            var currentDeck = dungeonSystem.GetCurrentDeck();

            currentBattle = new BattleSystem(
                player,
                enemies,
                currentDeck,
                customGaugeSpeed: 1.0f,
                randomProvider,
                logger);

            // ダッシュ接敵効果を適用
            if (contactInfo.IsDashContact && contactInfo.DashEffect.HasValue)
            {
                ApplyDashContactEffect(contactInfo.DashEffect.Value, contactInfo.Enemy);
            }

            // プレイヤー初期配置（ダンジョン位置に基づく）
            var initialPlayerPos = CalculateInitialPlayerPosition(dungeonSystem.PlayerPosition);
            player.MoveTo(initialPlayerPos);

            // 敵の初期配置
            var enemyPosition = CalculateEnemyPosition(contactInfo.Enemy, dungeonSystem.PlayerPosition);
            contactInfo.Enemy.MoveTo(enemyPosition);

            // バトル終了イベント購読
            currentBattle.OnBattleEnd += OnBattleEnd;

            // バトルフェーズに移行
            currentPhase = GamePhase.Battle;
            OnBattleStarted?.Invoke(currentBattle, contactInfo);
        }

        /// <summary>
        /// ダッシュ接敵効果を適用
        /// </summary>
        private void ApplyDashContactEffect(ActionSystem.DashContactEffect effect, Enemy enemy)
        {
            switch (effect)
            {
                case ActionSystem.DashContactEffect.InitialDamage:
                    // 初撃ダメージ（プレイヤー攻撃力の50%）
                    int damage = player.AttackPower / 2;
                    enemy.TakeDamage(damage, ElementType.None);
                    logger?.LogInfo($"[DungeonBattleManager] ダッシュ接敵: 初撃{damage}ダメージ");
                    break;

                case ActionSystem.DashContactEffect.Stun:
                    // 敵に3秒スタンを付与
                    const int STUN_DURATION_MS = 3000;
                    enemy.ApplyStun(STUN_DURATION_MS);
                    logger?.LogInfo($"[DungeonBattleManager] ダッシュ接敵: スタン（{STUN_DURATION_MS / 1000}秒）");
                    break;

                case ActionSystem.DashContactEffect.FirstStrike:
                    // カスタムゲージを満タンにして先制攻撃権を付与
                    currentBattle.SetFirstStrike();
                    logger?.LogInfo("[DungeonBattleManager] ダッシュ接敵: 先制攻撃 - カスタムゲージ満タン");
                    break;
            }
        }

        /// <summary>
        /// ダンジョン位置からバトル初期位置を計算
        /// ダンジョンの進行度に応じてプレイヤーの開始位置を決定
        /// </summary>
        private Vector2Int CalculateInitialPlayerPosition(float dungeonPosition)
        {
            // プレイヤーは常に左側（X=0）から開始
            return new Vector2Int(0, 0);
        }

        /// <summary>
        /// 敵のバトル位置を計算
        /// </summary>
        private Vector2Int CalculateEnemyPosition(Enemy enemy, float dungeonPosition)
        {
            // 敵は右側（X=5）に配置
            // 後方の敵ほど遠くに配置する仕様も可能
            return new Vector2Int(5, 0);
        }

        /// <summary>
        /// バトル終了時
        /// </summary>
        private void OnBattleEnd(BattleResult result)
        {
            logger?.LogInfo($"[DungeonBattleManager] バトル終了: {result}");
            currentPhase = GamePhase.DungeonTransition;

            bool victory = result == BattleResult.Victory;

            // ダンジョンシステムに結果を通知
            dungeonSystem.OnBattleCompleted(victory);

            if (victory)
            {
                // 勝利後、カードドロップ判定はDungeonSystemが行う
                // カード選択画面が表示される場合は OnCardRewardOffered イベントで通知される
                // 表示されない場合はすぐにダンジョンフェーズに戻る
                currentPhase = GamePhase.Dungeon;
            }
            else
            {
                // 敗北時はゲームオーバー（DungeonSystem.OnDungeonFailedで処理）
                currentPhase = GamePhase.GameOver;
            }

            // バトルシステムを破棄
            if (currentBattle != null)
            {
                currentBattle.OnBattleEnd -= OnBattleEnd;
                currentBattle = null;
            }
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

        /// <summary>
        /// プレイヤーアクション：ジャンプ
        /// </summary>
        public void Jump()
        {
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
            if (currentPhase == GamePhase.Dungeon)
            {
                dungeonSystem.StopDash();
            }
        }

        // ========== イベント ==========
        /// <summary>バトル開始時（カメラ遷移などに使用）</summary>
        public event Action<BattleSystem, ContactInfo> OnBattleStarted;

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
