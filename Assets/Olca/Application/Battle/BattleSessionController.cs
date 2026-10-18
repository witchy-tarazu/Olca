using System;
using Cysharp.Threading.Tasks;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;

namespace Olca.Application.Battle
{
    /// <summary>
    /// 1バトルセッション中の共通操作を管理するコントローラー。
    ///
    /// 責務:
    /// - ゲージ満タン時のログ通知
    /// - Altキー等によるカスタム画面の手動オープン（TryOpenCustomScreen）
    /// - カスタム画面確定後のクローズアニメーション → バトル再開
    /// - Attach / Detach によるイベント購読ライフサイクル管理
    ///
    /// BattleController / DungeonBattleController 両方からコンポジションで使用する。
    /// </summary>
    public class BattleSessionController
    {
        private readonly IBattleView battleView;
        private readonly ICustomScreenView customScreenView;
        private readonly IRandomProvider randomProvider;
        private readonly IGameLogger logger;

        private BattleSystem currentBattle;
        private int sessionVersion;
        private bool isClosing;

        public BattleSessionController(
            IBattleView battleView,
            ICustomScreenView customScreenView,
            IRandomProvider randomProvider,
            IGameLogger logger)
        {
            this.battleView = battleView;
            this.customScreenView = customScreenView;
            this.randomProvider = randomProvider;
            this.logger = logger;
        }

        /// <summary>
        /// バトルセッション開始。
        /// BattleSystem を受け取り、イベント購読を開始する。
        /// 前回の購読が残っている場合は先に解除する（バトル間の再利用を安全に行う）。
        /// </summary>
        public void Attach(BattleSystem battle)
        {
            Detach();
            currentBattle = battle;
            currentBattle.CustomGauge.OnGaugeFull += OnGaugeFull;
            customScreenView.OnCustomScreenConfirmed += OnCustomScreenConfirmed;
        }

        /// <summary>
        /// バトルセッション終了。全購読を解除する。
        /// バトル終了遷移の前に呼ぶこと。
        /// </summary>
        public void Detach()
        {
            sessionVersion++;
            isClosing = false;
            if (currentBattle != null)
            {
                currentBattle.CustomGauge.OnGaugeFull -= OnGaugeFull;
                currentBattle.CardUsage.Clear();
                // バトル終了時にカスタム画面が開きっぱなしの場合は強制クローズ
                // （確定アニメーション中にバトルが終了した際、Close()が呼ばれないケースへの対処）
                if (currentBattle.CustomScreen.IsOpen)
                    currentBattle.CustomScreen.Close();
                currentBattle = null;
            }
            customScreenView.OnCustomScreenConfirmed -= OnCustomScreenConfirmed;
        }

        /// <summary>
        /// カスタム画面を開こうとする。
        /// ゲージが満タンかつ画面が閉じている場合のみ開く。
        /// Altキー入力など、外部からのトリガーに応じて呼ぶ。
        /// </summary>
        public void TryOpenCustomScreen()
        {
            if (currentBattle == null || currentBattle.IsGameOver) return;
            if (!currentBattle.CustomGauge.IsFull) return;
            if (currentBattle.CustomScreen.IsOpen) return;

            currentBattle.OpenCustomScreen(randomProvider);
            battleView.OpenCustomScreen();
        }

        /// <summary>Unityの入力イベント用。非同期処理の例外はTryUseCardAsync内で処理する。</summary>
        public void UseCardFromInput() => TryUseCardAsync().Forget();

        /// <summary>現在のセッションでカードを一枚使用する。受付拒否・中断・失敗はfalse。</summary>
        public async UniTask<bool> TryUseCardAsync()
        {
            var battle = currentBattle;
            int version = sessionVersion;
            if (battle == null || battle.IsGameOver || battle.IsPaused || battle.CustomScreen.IsOpen
                || battle.CardUsage.IsEmpty || battle.CardUsage.InFlightCard != null) return false;
            try
            {
                bool consumed = await battle.CardUsage.TryUseCurrentCardAsync();
                return consumed && version == sessionVersion && ReferenceEquals(currentBattle, battle);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                logger?.LogError($"[BattleSession] カード使用に失敗: {ex}");
                return false;
            }
        }

        // ==================== 内部ハンドラー ====================

        private void OnGaugeFull()
        {
            // 自動では開かない。ユーザーのAltキー入力まで待つ。
            logger.LogInfo("[BattleSession] カスタムゲージ満タン！Altキーで画面を開けます");
        }

        private void OnCustomScreenConfirmed()
        {
            // 意図: アニメーションを待ってから CustomScreen.Close() を呼ぶことで
            // バトルの Resume() が正しいタイミングで呼ばれる
            if (currentBattle == null || isClosing) return;
            isClosing = true;
            CloseCustomScreenAsync().Forget();
        }

        private async UniTask CloseCustomScreenAsync()
        {
            var closingBattle = currentBattle;
            int version = sessionVersion;
            try
            {
                await battleView.CloseCustomScreenAsync();
                if (version == sessionVersion && ReferenceEquals(currentBattle, closingBattle))
                    closingBattle?.CustomScreen.Close();
            }
            catch (OperationCanceledException)
            {
                if (version == sessionVersion) closingBattle?.CustomScreen.CancelConfirmation();
            }
            catch (Exception ex)
            {
                if (version == sessionVersion) closingBattle?.CustomScreen.CancelConfirmation();
                logger?.LogError($"[BattleSession] カスタム画面の終了に失敗: {ex}");
            }
            finally
            {
                if (version == sessionVersion) isClosing = false;
            }
        }
    }
}
