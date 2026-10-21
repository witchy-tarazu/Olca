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
        private readonly Action<int, Action> scheduleLogical;
        private readonly int closeDurationFrames;

        public BattleSessionController(
            IBattleView battleView,
            ICustomScreenView customScreenView,
            IRandomProvider randomProvider,
            IGameLogger logger, Action<int, Action> scheduleLogical = null, int closeDurationFrames = 0)
        {
            this.battleView = battleView;
            this.customScreenView = customScreenView;
            this.randomProvider = randomProvider;
            this.logger = logger;
            this.scheduleLogical = scheduleLogical;
            this.closeDurationFrames = closeDurationFrames;
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
            customScreenView.BindTransformations(battle.Transformations);
            currentBattle.CustomGauge.OnGaugeFull += OnGaugeFull;
            if (scheduleLogical != null) currentBattle.CustomScreen.OnConfirmed += OnLogicalConfirmed;
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
                currentBattle.CustomScreen.OnConfirmed -= OnLogicalConfirmed;
                currentBattle.CancelBasicAction();
                currentBattle.CardUsage.Clear();
                // バトル終了時にカスタム画面が開きっぱなしの場合は強制クローズ
                // （確定アニメーション中にバトルが終了した際、Close()が呼ばれないケースへの対処）
                if (currentBattle.CustomScreen.IsOpen)
                    currentBattle.CustomScreen.Close();
                currentBattle = null;
            }
            customScreenView.OnCustomScreenConfirmed -= OnCustomScreenConfirmed;
            customScreenView.BindTransformations(null);
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

        /// <summary>カード入力を含む通常ゲーム経路の一論理フレーム。</summary>
        public void ProcessLogicalFrame(Action action)
        {
            var battle = currentBattle;
            if (battle == null) action(); else battle.RunLogicalFrame(action);
        }

        /// <summary>Unityの入力イベント用。非同期処理の例外はTryUseCardAsync内で処理する。</summary>
        public void UseCardFromInput() => TryUseCardAsync().Forget();

        /// <summary>現在のセッションでカードを一枚使用する。受付拒否・中断・失敗はfalse。</summary>
        public async UniTask<bool> TryUseCardAsync()
        {
            var battle = currentBattle;
            int version = sessionVersion;
            if (battle == null || battle.IsGameOver || battle.IsPaused || battle.CustomScreen.IsOpen
                || battle.CardUsage.IsEmpty || !battle.CardUsage.CanUseCurrentCard) return false;
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

        private void OnLogicalConfirmed(System.Collections.Generic.List<Olca.Core.Domain.Card> cards) => OnCustomScreenConfirmed();

        private void OnCustomScreenConfirmed()
        {
            // 意図: アニメーションを待ってから CustomScreen.Close() を呼ぶことで
            // バトルの Resume() が正しいタイミングで呼ばれる
            if (currentBattle == null || isClosing) return;
            isClosing = true;
            if (scheduleLogical == null) CloseCustomScreenAsync().Forget();
            else
            {
                var closing = currentBattle; int version = sessionVersion;
                // 表示の終了通知ではなく既存演出時間を換算した論理フレームで再開する。
                scheduleLogical(closeDurationFrames, () => {
                    if (version != sessionVersion || !ReferenceEquals(currentBattle, closing)) return;
                    closing.CustomScreen.Close(); isClosing = false;
                });
                CloseVisualOnlyAsync().Forget();
            }
        }

        private async UniTask CloseVisualOnlyAsync()
        {
            try { await battleView.CloseCustomScreenAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger?.LogError($"[BattleSession] 表示の終了に失敗: {ex}"); }
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

namespace Olca.Application.Battle
{
    // 短押し/タメと確定済み変身の性能を共通入力へ接続。拘束中に予約せずカードを優先する。
    public sealed class BasicAttackInputState
    {
        private Olca.Core.Logic.Battle.BattleSystem current;
        private bool armed, resumePending;
        private string form;
        public int ChargeFrames { get; private set; }
        public bool IsCharging => armed;
        public bool IsResumePending => resumePending;
        public int CooldownFrames { get; private set; }
        public void CancelCharge() { armed = resumePending = false; ChargeFrames = 0; }
        public void Suspend() { if (armed) { armed = false; resumePending = true; } }
        public void Reset() { current?.CancelBasicAction(); current = null; form = null; CancelCharge(); CooldownFrames = 0; }
        public void Tick(Olca.Core.Logic.Battle.BattleSystem battle, Olca.Core.Logic.Integration.LogicalInput input,
            int chargeFrames, int intervalFrames, int chargeMultiplier = 2)
        {
            if (chargeFrames < 1 || intervalFrames < 1 || chargeMultiplier < 1) throw new System.ArgumentOutOfRangeException();
            if (!ReferenceEquals(current, battle)) { Reset(); current = battle; form = battle?.Transformations.ActiveId; }
            if (battle == null) return;
            chargeFrames = battle.ResolveBasicChargeFrames(chargeFrames);
            intervalFrames = battle.ResolveBasicIntervalFrames(intervalFrames);
            if (form != battle.Transformations.ActiveId) { CancelCharge(); battle.CancelBasicStartupForFormChange(); form = battle.Transformations.ActiveId; }
            if (!battle.IsPaused && CooldownFrames > 0) CooldownFrames--;
            if (input.CardUseButtonDown) { CancelCharge(); return; }
            if (battle.IsGameOver || battle.Player.IsDead || battle.Player.IsActionPrevented || (battle.CardUsage.InFlightCard != null && !battle.CanCancelPlayerRecovery))
            { CancelCharge(); return; }
            // カスタム/ポーズは量だけ保持。復帰時の自動解放で発射せず、新しい押下で再開する。
            if (battle.IsPaused || input.CustomScreenButtonDown) { Suspend(); return; }
            if (resumePending && !input.BasicAttackButtonDown) return;
            if (input.BasicAttackButtonDown && !armed)
            {
                bool cancelRecovery = battle.CanCancelPlayerRecovery;
                if ((CooldownFrames > 0 && !cancelRecovery) || !battle.TryPreparePlayerAttack()) { CancelCharge(); return; }
                if (cancelRecovery) CooldownFrames = 0;
                armed = true; resumePending = false;
            }
            if (armed && input.BasicAttackButton) ChargeFrames = System.Math.Min(chargeFrames, ChargeFrames + 1);
            if (input.BasicAttackButtonUp && !input.BasicAttackButton)
            {
                bool fire = armed && CooldownFrames == 0 && battle.CanPlayerBasicAttack;
                bool charged = ChargeFrames >= chargeFrames;
                CancelCharge();
                if (fire) { CooldownFrames = intervalFrames; battle.TryStartPlayerBasicAttack(charged, chargeMultiplier); }
            }
            else if (!input.BasicAttackButton) CancelCharge();
        }
    }
}
