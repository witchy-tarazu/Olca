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
            DungeonCheckpointSession checkpoints = null, RestoredCheckpoint restoration = null)
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
                rewardPool: rewardPool, checkpoints: checkpoints, restoration: restoration);

            // イベント購読
            dungeonSystem.OnBattleStart += OnDungeonBattleStart;
            dungeonSystem.OnDungeonFailed += OnDungeonFailed;
            dungeonSystem.OnDungeonCleared += OnDungeonCleared;
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
        public void Tick(int deltaTimeMs, IInputProvider input = null)
        {
            if (IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
            if (IsUserPaused || IsSuspended || dungeonSystem.IsCheckpointBlocked) return;
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
            out DungeonBattleManager manager, out string error, ExceedSystem exceed = null, TuningSystem tuning = null, FusionSystem fusion = null)
        {
            manager = null;
            if (!session.TryRead(out var restored, out error)) return false;
            manager = new DungeonBattleManager(restored.Map, restored.Player, restored.Deck, random, logger,
                exceed, tuning, fusion, checkpoints: session, restoration: restored);
            return true;
        }

        // ========== イベント ==========
        /// <summary>バトル開始時（カメラ遷移などに使用）</summary>
        public event Action<BattleSystem, ContactInfo> OnBattleStarted;

        /// <summary>バトル終了時（View遷移のトリガー）</summary>
        public event Action OnBattleEnded;

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
