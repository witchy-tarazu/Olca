namespace Olca.Core.Logic.Battle
{
    // 短押し/タメと確定済み変身の性能を共通入力へ接続。拘束中に予約せずカードを優先する。
    public class BasicAttackInputState
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
        /// <summary>GUIとstandaloneの共通入力振分け。非同期完了の通知だけを呼出元へ委譲する。</summary>
        public void ProcessInput(BattleSystem battle, Olca.Core.Logic.Integration.LogicalInput input,
            int chargeFrames, int intervalFrames, int chargeMultiplier, System.Action useCard)
        {
            Tick(battle, input, chargeFrames, intervalFrames, chargeMultiplier);
            if (battle != null && !battle.IsPaused && input.CardUseButtonDown) useCard?.Invoke();
        }
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
