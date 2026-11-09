namespace Olca.Core.Logic.Battle
{
    // 短押し／タメと攻撃中の共通1枠予約。時間はBattleの論理frameだけを使う。
    public class BasicAttackInputState
    {
        private Olca.Core.Logic.Battle.BattleSystem current;
        private bool armed, resumePending;
        private string form;
        private readonly AttackInputReservation reservation = new AttackInputReservation();
        private long reservedAction, reservedEpoch, reservedQueue;
        private int pendingChargeFrames, pendingIntervalFrames, pendingMultiplier;
        private bool basicHeld, productInput;
        private long observedEpoch;
        private System.Action pendingUseCard;
        public AttackReservationKind PendingAttack => reservation.Kind;
        public bool IsChargeStartPending => basicHeld && !armed && current != null && !current.IsGameOver && !current.Player.IsDead;
        public int ChargeFrames { get; private set; }
        public bool IsCharging => armed;
        public bool IsResumePending => resumePending;
        public int CooldownFrames { get; private set; }
        private void ClearCharge() { armed = resumePending = false; ChargeFrames = 0; }
        private void CancelReservation() { reservation.Cancel(); pendingUseCard = null; }
        public void CancelCharge() { ClearCharge(); CancelReservation(); }
        // 旧Basicポインターだけの取消を、後押しされたカード予約へ波及させない。
        public void CancelBasicInput() { basicHeld = false; ClearCharge(); if (reservation.Kind == AttackReservationKind.Basic) CancelReservation(); }
        public void Suspend() { CancelReservation(); if (armed) { armed = false; resumePending = true; } }
        public void SuspendForUserPause() { ClearCharge(); CancelReservation(); }
        public void Reset() { basicHeld = productInput = false; if (current != null) current.AttackInputBoundary -= OnAttackInputBoundary; current?.CancelBasicAction(); current = null; form = null; CancelCharge(); CooldownFrames = 0; }
        /// <summary>GUIとstandaloneの共通入力振分け。非同期完了の通知だけを呼出元へ委譲する。</summary>
        public void ProcessInput(BattleSystem battle, Olca.Core.Logic.Integration.LogicalInput input,
            int chargeFrames, int intervalFrames, int chargeMultiplier, System.Action useCard)
        {
            if (input == null) throw new System.ArgumentNullException(nameof(input));
            if (chargeFrames < 1 || intervalFrames < 1 || chargeMultiplier < 1) throw new System.ArgumentOutOfRangeException();
            if (!ReferenceEquals(current, battle)) { Reset(); current = battle; form = battle?.Transformations.ActiveId; if (battle != null) battle.AttackInputBoundary += OnAttackInputBoundary; }
            if (battle == null) return;
            productInput = true;
            bool wasHeld = basicHeld;
            basicHeld = input.HasChargeHoldSample ? input.ChargeHoldButton || input.BasicAttackButton : input.BasicAttackButton;
            if (observedEpoch != battle.AttackInputEpoch)
            {
                // 状態取消で量は持越さない。Customだけは従来の量保持契約。
                if (!(battle.CustomScreen.IsOpen || battle.IsCustomPaused) && !battle.IsUserPaused) ClearCharge();
                observedEpoch = battle.AttackInputEpoch;
            }
            pendingChargeFrames = chargeFrames; pendingIntervalFrames = intervalFrames; pendingMultiplier = chargeMultiplier;
            if (form != battle.Transformations.ActiveId)
            {
                CancelCharge(); battle.CancelBasicStartupForFormChange(); form = battle.Transformations.ActiveId;
            }
            if (reservedEpoch != battle.AttackInputEpoch || reservedAction != battle.AttackActionSerial ||
                (reservation.Kind == AttackReservationKind.Card && reservedQueue != battle.CardUsage.SelectionGeneration)) CancelReservation();
            if (battle.IsGameOver || battle.Player.IsDead || battle.Player.IsActionPrevented)
            { CancelCharge(); Tick(battle, new Olca.Core.Logic.Integration.LogicalInput(), chargeFrames, intervalFrames, chargeMultiplier); return; }
            if (battle.IsUserPaused) { observedEpoch = battle.AttackInputEpoch; SuspendForUserPause(); return; }
            if ((battle.CustomScreen.IsOpen || battle.IsCustomPaused)) { observedEpoch = battle.AttackInputEpoch; Suspend(); return; }
            // 暗転／hitstopは戦闘時計と同じく停止。ユーザーPauseとは区別する。
            if (battle.IsPaused) return;
            reservation.Observe(battle.LogicalFrame);
            if (reservation.Kind == AttackReservationKind.None) pendingUseCard = null;
            bool card = input.CardUseButtonDown && (!input.BasicAttackButtonDown ||
                input.CardUsePressedAt == 0 || input.BasicAttackPressedAt == 0 || input.CardUsePressedAt >= input.BasicAttackPressedAt);
            if (card)
            {
                CancelCharge();
                if (battle.HasPlayerAction && !battle.CardUsage.CanUseCurrentCard)
                {
                    if (!battle.CardUsage.IsEmpty) { Reserve(battle, AttackReservationKind.Card); pendingUseCard = useCard; }
                    Tick(battle, new Olca.Core.Logic.Integration.LogicalInput(), chargeFrames, intervalFrames, chargeMultiplier);
                }
                else { Tick(battle, new Olca.Core.Logic.Integration.LogicalInput(), chargeFrames, intervalFrames, chargeMultiplier); useCard?.Invoke(); }
                return;
            }
            if (input.BasicAttackButtonDown)
            {
                // 同frameで先にカードを押した場合も、その時点のタメ解除は保持する。
                if (input.CardUseButtonDown) ClearCharge();
                CancelReservation();
                if (battle.HasPlayerAction && (basicHeld || !battle.CanCancelPlayerRecovery))
                {
                    Reserve(battle, AttackReservationKind.Basic);
                    Tick(battle, new Olca.Core.Logic.Integration.LogicalInput(), chargeFrames, intervalFrames, chargeMultiplier);
                    return;
                }
                var basicOnly = input.Copy(); basicOnly.CardUseButtonDown = false; basicOnly.CustomScreenButtonDown = false; basicOnly.BasicAttackButton = basicHeld;
                Tick(battle, basicOnly, chargeFrames, intervalFrames, chargeMultiplier); return;
            }
            if (reservation.Kind == AttackReservationKind.Card && battle.CardUsage.CanUseCurrentCard)
            {
                CancelReservation(); ClearCharge();
                Tick(battle, new Olca.Core.Logic.Integration.LogicalInput(), chargeFrames, intervalFrames, chargeMultiplier);
                useCard?.Invoke(); return;
            }
            if (reservation.Kind == AttackReservationKind.Basic && battle.CanPlayerBasicAttack &&
                (CooldownFrames <= 1 || battle.CanCancelPlayerRecovery) && !basicHeld)
            {
                bool held = false; CancelReservation();
                Tick(battle, new Olca.Core.Logic.Integration.LogicalInput {
                    BasicAttackButtonDown = true, BasicAttackButton = held, BasicAttackButtonUp = !held
                }, chargeFrames, intervalFrames, chargeMultiplier); return;
            }
            // 保持は予約slotではない。開始は攻撃全体の終了を待ち、後隙をcancelしない。
            var effective = input.Copy(); effective.CustomScreenButtonDown = false; effective.BasicAttackButton = basicHeld;
            effective.BasicAttackButtonUp |= wasHeld && !basicHeld;
            if (!armed && basicHeld && !battle.HasPlayerAction && CooldownFrames <= 1 && battle.CanPlayerBasicAttack)
                effective.BasicAttackButtonDown = true;
            if (!armed && !basicHeld) ClearCharge();
            Tick(battle, effective, chargeFrames, intervalFrames, chargeMultiplier);
        }
        private void Reserve(BattleSystem battle, AttackReservationKind kind)
        {
            reservation.Request(kind, battle.LogicalFrame);
            reservedAction = battle.AttackActionSerial; reservedEpoch = battle.AttackInputEpoch; reservedQueue = battle.CardUsage.SelectionGeneration;
        }

        // 入力受付後、同じ論理frameのwait解決で到達した最速の窓を取り逃がさない。
        private void OnAttackInputBoundary(bool afterWaits)
        {
            var battle = current;
            if (battle == null || !productInput) return;
            if (battle.IsUserPaused) { observedEpoch = battle.AttackInputEpoch; SuspendForUserPause(); return; }
            if ((battle.CustomScreen.IsOpen || battle.IsCustomPaused)) { observedEpoch = battle.AttackInputEpoch; Suspend(); return; }
            if (observedEpoch != battle.AttackInputEpoch)
            { ClearCharge(); observedEpoch = battle.AttackInputEpoch; }
            if (battle.Player.IsActionPrevented || battle.Player.IsDead || battle.IsGameOver) ClearCharge();
            if (reservation.Kind == AttackReservationKind.None) { TryStartHeldCharge(afterWaits); return; }
            if (reservedEpoch != battle.AttackInputEpoch || reservedAction != battle.AttackActionSerial ||
                (reservation.Kind == AttackReservationKind.Card && reservedQueue != battle.CardUsage.SelectionGeneration) ||
                form != battle.Transformations.ActiveId || battle.IsGameOver || battle.Player.IsDead || battle.Player.IsActionPrevented ||
                battle.IsUserPaused || (battle.CustomScreen.IsOpen || battle.IsCustomPaused)) { CancelReservation(); TryStartHeldCharge(afterWaits); return; }
            reservation.Observe(battle.LogicalFrame);
            if (reservation.Kind == AttackReservationKind.None) { pendingUseCard = null; TryStartHeldCharge(afterWaits); return; }
            if (!afterWaits || battle.IsPaused) return;
            if (reservation.Kind == AttackReservationKind.Card && battle.CardUsage.CanUseCurrentCard)
            {
                var use = pendingUseCard; CancelReservation(); ClearCharge(); use?.Invoke();
            }
            else if (reservation.Kind == AttackReservationKind.Basic && battle.CanPlayerBasicAttack &&
                (CooldownFrames == 0 || battle.CanCancelPlayerRecovery) && !basicHeld)
            {
                bool held = false; CancelReservation();
                TickImmediate(battle, new Olca.Core.Logic.Integration.LogicalInput { BasicAttackButtonDown = true,
                    BasicAttackButton = held, BasicAttackButtonUp = !held }, pendingChargeFrames, pendingIntervalFrames, pendingMultiplier, false);
            }
            else if (reservation.Kind == AttackReservationKind.Card && !battle.HasPlayerAction && battle.CardUsage.IsEmpty) CancelReservation();
            TryStartHeldCharge(afterWaits);
        }

        private void TryStartHeldCharge(bool afterWaits)
        {
            var battle = current;
            if (!afterWaits || !basicHeld || armed || battle == null || battle.IsPaused || battle.HasPlayerAction ||
                !battle.CanPlayerBasicAttack || CooldownFrames > 0) return;
            if (reservation.Kind == AttackReservationKind.Basic) CancelReservation();
            TickImmediate(battle, new Olca.Core.Logic.Integration.LogicalInput {
                BasicAttackButtonDown = true, BasicAttackButton = true
            }, pendingChargeFrames, pendingIntervalFrames, pendingMultiplier, false);
        }

        public void Tick(Olca.Core.Logic.Battle.BattleSystem battle, Olca.Core.Logic.Integration.LogicalInput input,
            int chargeFrames, int intervalFrames, int chargeMultiplier = 2)
        { TickImmediate(battle, input, chargeFrames, intervalFrames, chargeMultiplier, true); }
        private void TickImmediate(Olca.Core.Logic.Battle.BattleSystem battle, Olca.Core.Logic.Integration.LogicalInput input,
            int chargeFrames, int intervalFrames, int chargeMultiplier, bool advanceCooldown)
        {
            if (chargeFrames < 1 || intervalFrames < 1 || chargeMultiplier < 1) throw new System.ArgumentOutOfRangeException();
            if (!ReferenceEquals(current, battle)) { Reset(); current = battle; form = battle?.Transformations.ActiveId; if (battle != null) battle.AttackInputBoundary += OnAttackInputBoundary; }
            if (battle == null) return;
            chargeFrames = battle.ResolveBasicChargeFrames(chargeFrames);
            intervalFrames = battle.ResolveBasicIntervalFrames(intervalFrames);
            if (form != battle.Transformations.ActiveId) { ClearCharge(); battle.CancelBasicStartupForFormChange(); form = battle.Transformations.ActiveId; }
            if (advanceCooldown && !battle.IsPaused && CooldownFrames > 0) CooldownFrames--;
            if (input.CardUseButtonDown) { ClearCharge(); return; }
            if (battle.IsGameOver || battle.Player.IsDead || battle.Player.IsActionPrevented || (battle.CardUsage.InFlightCard != null && !battle.CanCancelPlayerRecovery))
            { ClearCharge(); return; }
            // カスタム/ポーズは量だけ保持。復帰時の自動解放で発射せず、新しい押下で再開する。
            if (battle.IsPaused || input.CustomScreenButtonDown) { Suspend(); return; }
            if (resumePending && !input.BasicAttackButtonDown) return;
            if (input.BasicAttackButtonDown && !armed)
            {
                bool cancelRecovery = battle.CanCancelPlayerRecovery;
                if ((CooldownFrames > 0 && !cancelRecovery) || !battle.TryPreparePlayerAttack()) { ClearCharge(); return; }
                if (cancelRecovery) CooldownFrames = 0;
                armed = true; resumePending = false;
            }
            if (armed && input.BasicAttackButton) ChargeFrames = System.Math.Min(chargeFrames, ChargeFrames + 1);
            if (input.BasicAttackButtonUp && !input.BasicAttackButton)
            {
                bool fire = armed && CooldownFrames == 0 && battle.CanPlayerBasicAttack;
                bool charged = ChargeFrames >= chargeFrames;
                ClearCharge();
                if (fire) { CooldownFrames = intervalFrames; battle.TryStartPlayerBasicAttack(charged, chargeMultiplier); }
            }
            else if (!input.BasicAttackButton) ClearCharge();
        }
    }
    public enum AttackReservationKind { None, Card, Basic }

    /// <summary>共通1枠。受付した境界から6論理frame目の開始で失効する。</summary>
    public sealed class AttackInputReservation
    {
        public AttackReservationKind Kind { get; private set; }
        public long RequestedFrame { get; private set; }
        public void Request(AttackReservationKind kind, long frame)
        {
            if (kind != AttackReservationKind.Card && kind != AttackReservationKind.Basic) throw new System.ArgumentOutOfRangeException(nameof(kind));
            if (frame < 0) throw new System.ArgumentOutOfRangeException(nameof(frame));
            Kind = kind; RequestedFrame = frame;
        }
        public void Observe(long frame)
        {
            if (frame < 0) throw new System.ArgumentOutOfRangeException(nameof(frame));
            if (Kind == AttackReservationKind.None) return;
            if (frame < RequestedFrame) { Cancel(); return; }
            if (frame - RequestedFrame >= 6) Cancel();
        }
        public void Cancel() { Kind = AttackReservationKind.None; RequestedFrame = 0; }
    }

}
