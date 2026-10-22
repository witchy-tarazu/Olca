using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    public enum CustomCommandKind { Open, Select, Deselect, PreviewCustomization, ExecuteCustomization, CancelCustomization, Undo, Confirm, ReturnToBattle, PreviewTransformation, CancelTransformation }
    public enum CustomCommandRejection { None, InvalidCommand, Busy, BattleUnavailable, UserPaused, GaugeNotFull, ScreenClosed, Confirming, NotConfirming, CardUnavailable, RuleRejected }

    /// <summary>カードIDではなく、この戦闘内の個体ハンドルで対象を指定する不変入力。</summary>
    public sealed class CustomScreenCommand
    {
        public CustomCommandKind Kind { get; }
        public IReadOnlyList<long> Cards { get; }
        public string TransformationId { get; }
        public CustomScreenCommand(CustomCommandKind kind, params long[] cards)
        { Kind = kind; Cards = Array.AsReadOnly((cards ?? Array.Empty<long>()).ToArray()); }
        public CustomScreenCommand(CustomCommandKind kind, string transformationId) : this(kind) { TransformationId = transformationId; }
    }
    public sealed class TransformationObservation
    {
        public string PendingId { get; }
        public string ActiveId { get; }
        public int RemainingPhases { get; }
        public int ActivationFrames { get; }
        public int ReleaseFrames { get; }
        public IReadOnlyList<string> UsedIds { get; }
        public string Canonical { get; }
        internal TransformationObservation(BattleSystem battle, TransformationSelectionState transformation)
        {
            var state = transformation ?? battle?.Transformations; PendingId = state?.PendingId; ActiveId = state?.ActiveId;
            RemainingPhases = state?.RemainingPhases ?? 0; ActivationFrames = state?.ActivationFramesRemaining ?? 0; ReleaseFrames = state?.ReleaseFramesRemaining ?? 0;
            UsedIds = Array.AsReadOnly(state?.UsedIds.ToArray() ?? Array.Empty<string>());
            var attack = battle?.ActivePlayerAttack; var defense = battle?.Player.ActiveResistances;
            Canonical = string.Join(":", Uri.EscapeDataString(PendingId ?? ""), Uri.EscapeDataString(ActiveId ?? ""), RemainingPhases, ActivationFrames, ReleaseFrames,
                string.Join(",", UsedIds.Select(Uri.EscapeDataString)), state?.PresentationEnabled ?? false, state?.ShortPresentation ?? false,
                attack?.Element ?? ElementType.None, attack?.NormalPowerPercent ?? 100, attack?.ChargedPowerPercent ?? 200, attack?.ChargeFrames ?? 0, attack?.IntervalFrames ?? 0,
                defense?.Fire ?? 0, defense?.Water ?? 0, defense?.Wind ?? 0, defense?.Earth ?? 0, battle?.Player.BarrierHp ?? 0);
        }
    }
    public sealed class CustomCardObservation
    {
        public long Handle { get; }
        public string Id { get; }
        public string DefinitionHash { get; }
        public int Power { get; }
        public int Cost { get; }
        public int TuningCount { get; }
        internal CustomCardObservation(long handle, Card card)
        { Handle = handle; Id = card.Id; DefinitionHash = CollectionDisplay.DefinitionHash(card); Power = card.GetFinalPower(); Cost = card.GetFinalCost(); TuningCount = card.TuningCount; }
        public string Canonical => Handle + ":" + Uri.EscapeDataString(Id) + ":" + DefinitionHash + ":" + Power + ":" + Cost + ":" + TuningCount;
    }
    /// <summary>後続操作で変わらない選択・手札・加工履歴の観測値。保存DTOではない。</summary>
    public sealed class CustomScreenObservation
    {
        public bool IsOpen { get; }
        public bool IsConfirming { get; }
        public int UndoCount { get; }
        public int UsedCost { get; }
        public TransformationObservation Transformation { get; }
        public IReadOnlyList<CustomCardObservation> Hand { get; }
        public IReadOnlyList<CustomCardObservation> Selected { get; }
        public IReadOnlyList<CustomCardObservation> Available { get; }
        public string Canonical { get; }
        internal CustomScreenObservation(CustomScreenSystem screen, Func<Card, long> handle, BattleSystem battle, TransformationSelectionState transformation)
        {
            Transformation = new TransformationObservation(battle, transformation);
            IsOpen = screen.IsOpen; IsConfirming = screen.IsConfirming; UndoCount = screen.CustomizationUndoCount; UsedCost = screen.GetTotalUsedCost();
            CustomCardObservation[] Capture(IEnumerable<Card> cards) => cards.Select(c => new CustomCardObservation(handle(c), c)).ToArray();
            Hand = Array.AsReadOnly(Capture(screen.CurrentHand)); Selected = Array.AsReadOnly(Capture(screen.SelectedCards)); Available = Array.AsReadOnly(Capture(screen.AvailableCards));
            string List(IReadOnlyList<CustomCardObservation> values) => string.Join(",", values.Select(c => c.Canonical));
            Canonical = string.Join("/", IsOpen, IsConfirming, UndoCount, UsedCost, List(Hand), List(Selected), List(Available), Transformation.Canonical);
        }
    }
    public sealed class CustomCommandResult
    {
        public long Sequence { get; }
        public bool Accepted => Rejection == CustomCommandRejection.None;
        public CustomCommandRejection Rejection { get; }
        public string Reason { get; }
        public CustomizationPreview Preview { get; }
        public CustomScreenObservation State { get; }
        public IReadOnlyList<string> Events { get; }
        public string Canonical { get; }
        internal CustomCommandResult(long sequence, CustomScreenCommand command, CustomCommandRejection rejection, string reason, CustomizationPreview preview, CustomScreenObservation state)
        {
            Sequence = sequence; Rejection = rejection; Reason = reason; Preview = preview; State = state;
            string output = command.Kind + ":" + Uri.EscapeDataString(command.TransformationId ?? "") + ":" + string.Join(",", command.Cards) + ":" + rejection + ":" + Uri.EscapeDataString(reason ?? "");
            if (preview != null) output += ":" + preview.Operation + ":" + preview.Status + ":" + (preview.ResultCard == null ? "-" : CollectionDisplay.DefinitionHash(preview.ResultCard));
            Events = Array.AsReadOnly(new[] { (Accepted ? "custom-accepted:" : "custom-rejected:") + output });
            Canonical = sequence + "|" + output + "|" + state.Canonical;
        }
    }

    /// <summary>Unity/CLI共通の受付境界。既存Systemに判定・素材消費・Undoを委譲し、時計を進めない。</summary>
    public sealed class CustomScreenCommands
    {
        private readonly CustomScreenSystem screen;
        private readonly Dictionary<Card, long> handles = new Dictionary<Card, long>();
        private readonly Dictionary<long, Card> cards = new Dictionary<long, Card>();
        private long nextHandle, sequence;
        private bool executing;
        private readonly Queue<Action> afterCommand = new Queue<Action>();
        private BattleSystem battle;
        private TransformationSelectionState transformations;
        private IRandomProvider random;
        public long Sequence => sequence;
        // 同期View互換で、確定イベント内に完了した表示を受付処理の後へ送る。時計/スレッドへは送らない。
        public void AfterCurrentCommand(Action action)
        { if (action == null) throw new ArgumentNullException(nameof(action)); if (executing) afterCommand.Enqueue(action); else action(); }
        internal CustomScreenCommands(CustomScreenSystem screen)
        {
            this.screen = screen;
            screen.OnScreenOpened += _ => Refresh(); screen.OnHandChanged += Refresh;
            screen.OnExceeded += _ => Refresh(); screen.OnTuned += _ => Refresh(); screen.OnFused += _ => Refresh();
            screen.OnCustomizationUndone += Refresh;
        }
        internal void AttachTransformations(TransformationSelectionState state) { transformations = state; }
        internal void AttachBattle(BattleSystem battle, IRandomProvider random) { this.battle = battle; this.random = random; }
        private void Refresh()
        {
            foreach (var card in screen.CurrentHand.Concat(screen.SelectedCards).Concat(screen.AvailableCards))
                if (!handles.ContainsKey(card)) { long id = checked(++nextHandle); handles.Add(card, id); cards.Add(id, card); }
        }
        public long HandleOf(Card card) => card != null && handles.TryGetValue(card, out long id) ? id : 0;
        public CustomScreenObservation Observe() { Refresh(); return new CustomScreenObservation(screen, HandleOf, battle, transformations); }

        public CustomCommandResult Execute(CustomScreenCommand command, IRandomProvider openingRandom = null)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (executing) return new CustomCommandResult(sequence, command, CustomCommandRejection.Busy, "コマンド処理中です", null, Observe());
            executing = true;
            try {
                sequence = checked(sequence + 1); Refresh();
                CustomCommandResult Result(CustomCommandRejection code = CustomCommandRejection.None, string reason = null, CustomizationPreview preview = null)
                    => new CustomCommandResult(sequence, command, code, reason, preview, Observe());
                int expected = command.Kind == CustomCommandKind.Select || command.Kind == CustomCommandKind.Deselect ? 1 :
                    command.Kind == CustomCommandKind.PreviewCustomization || command.Kind == CustomCommandKind.ExecuteCustomization ? 2 : 0;
                if ((command.Kind == CustomCommandKind.PreviewTransformation ? string.IsNullOrWhiteSpace(command.TransformationId) : command.TransformationId != null) || !Enum.IsDefined(typeof(CustomCommandKind), command.Kind) || command.Cards.Count != expected || command.Cards.Distinct().Count() != command.Cards.Count)
                    return Result(CustomCommandRejection.InvalidCommand, "操作または対象数が不正です");
                if (battle != null && battle.IsGameOver) return Result(CustomCommandRejection.BattleUnavailable, "戦闘が終了しています");
                if (battle != null && battle.IsUserPaused && command.Kind != CustomCommandKind.ReturnToBattle) return Result(CustomCommandRejection.UserPaused, "ポーズ中は操作できません");
                if (command.Kind == CustomCommandKind.Open) {
                    if (battle == null || (openingRandom ?? random) == null) return Result(CustomCommandRejection.BattleUnavailable, "戦闘と乱数を接続してください");
                    if (screen.IsOpen || battle.Transformations.IsActivating) return Result(CustomCommandRejection.RuleRejected, "既に開いているか変身演出中です");
                    if (!battle.CustomGauge.IsFull) return Result(CustomCommandRejection.GaugeNotFull, "カスタムゲージが満タンではありません");
                    battle.OpenCustomScreen(openingRandom ?? random); return Result();
                }
                if (!screen.IsOpen) return Result(CustomCommandRejection.ScreenClosed, "カスタム画面が閉じています");
                if (command.Kind == CustomCommandKind.ReturnToBattle) {
                    if (!screen.IsConfirming) return Result(CustomCommandRejection.NotConfirming, "先に選択を確定してください");
                    screen.Close(); return Result();
                }
                if (screen.IsConfirming) return Result(CustomCommandRejection.Confirming, "確定済みです");
                var targets = new List<Card>();
                foreach (long id in command.Cards) {
                    if (!cards.TryGetValue(id, out var card) || !(screen.CurrentHand.Contains(card) || screen.SelectedCards.Contains(card)))
                        return Result(CustomCommandRejection.CardUnavailable, "対象個体が現在の手札・選択にありません");
                    targets.Add(card);
                }
                switch (command.Kind) {
                    case CustomCommandKind.PreviewTransformation:
                        if (transformations == null) return Result(CustomCommandRejection.BattleUnavailable, "変身状態を接続してください");
                        return transformations.TryPreview(command.TransformationId) ? Result() : Result(CustomCommandRejection.RuleRejected, "未解放・使用済み・オーラ中など、変身を選べない状態です");
                    case CustomCommandKind.CancelTransformation:
                        if (transformations == null) return Result(CustomCommandRejection.BattleUnavailable, "変身状態を接続してください");
                        return transformations.TryCancelPending() ? Result() : Result(CustomCommandRejection.RuleRejected, "この画面で新しく選んだ変身だけ取り消せます");
                    case CustomCommandKind.Select:
                        return screen.TrySelectCard(targets[0]) ? Result() : Result(CustomCommandRejection.RuleRejected, "手札・選択枚数・コスト条件を満たしません");
                    case CustomCommandKind.Deselect:
                        return screen.TryDeselectCard(targets[0]) ? Result() : Result(CustomCommandRejection.RuleRejected, "最後に選択したカードだけ解除できます");
                    case CustomCommandKind.PreviewCustomization:
                    case CustomCommandKind.ExecuteCustomization:
                        var preview = command.Kind == CustomCommandKind.PreviewCustomization ? screen.PreviewCustomization(targets) : screen.ExecuteCustomization(targets);
                        return preview.CanExecute ? Result(preview: preview) : Result(CustomCommandRejection.RuleRejected, preview.Reason, preview);
                    case CustomCommandKind.CancelCustomization:
                        // 素材の仮選択/プレビューは入力と出力だけ。取消では消費せず、実行済み加工はUndoで戻す。
                        return Result();
                    case CustomCommandKind.Undo:
                        return screen.TryUndoCustomization() ? Result() : Result(CustomCommandRejection.RuleRejected, "取り消せる加工がありません");
                    case CustomCommandKind.Confirm:
                        if (!screen.CanConfirm()) return Result(CustomCommandRejection.RuleRejected, "確定できません");
                        screen.Confirm(); return Result();
                    default: return Result(CustomCommandRejection.InvalidCommand, "操作が不正です");
                }
            } finally { executing = false; while (afterCommand.Count > 0) afterCommand.Dequeue()(); }
        }
    }
}
