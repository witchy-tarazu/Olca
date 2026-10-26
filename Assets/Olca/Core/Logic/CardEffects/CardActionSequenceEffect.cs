using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Olca.Core.Domain;
using Olca.Core.Interfaces;
using Olca.Core.Logic.Battle;

namespace Olca.Core.Logic.CardEffects
{
    /// <summary>表示コマと正の論理フレーム数。素材そのものや性能値は変更しない。</summary>
    public sealed class ActionDisplayFrame
    {
        public int Index { get; }
        public int Frames { get; }
        public ActionDisplayFrame(int index, int frames)
        {
            if (index < 0 || frames < 1) throw new ArgumentOutOfRangeException(nameof(frames));
            Index = index; Frames = frames;
        }
    }

    /// <summary>複数アクション/カードから共有でき、別設定への差替えもできる表示情報。</summary>
    public sealed class ActionVisualDefinition
    {
        public string MotionKey { get; }
        public string EquipmentKey { get; }
        public string ProjectileKey { get; }
        public IReadOnlyList<ActionDisplayFrame> Startup { get; }
        public IReadOnlyList<ActionDisplayFrame> Execution { get; }
        public IReadOnlyList<ActionDisplayFrame> Recovery { get; }
        public int? EffectMarkerFrame { get; }
        public ActionVisualDefinition(string motionKey, string equipmentKey,
            IEnumerable<ActionDisplayFrame> startup, IEnumerable<ActionDisplayFrame> execution,
            IEnumerable<ActionDisplayFrame> recovery, int? effectMarkerFrame = null, string projectileKey = null)
        {
            if (string.IsNullOrWhiteSpace(motionKey) || equipmentKey != null && string.IsNullOrWhiteSpace(equipmentKey) ||
                projectileKey != null && string.IsNullOrWhiteSpace(projectileKey) || effectMarkerFrame < 0)
                throw new ArgumentException("表示参照と発生マーカーが不正です");
            MotionKey = motionKey; EquipmentKey = equipmentKey; ProjectileKey = projectileKey;
            Startup = Copy(startup); Execution = Copy(execution); Recovery = Copy(recovery); EffectMarkerFrame = effectMarkerFrame;
        }
        private static IReadOnlyList<ActionDisplayFrame> Copy(IEnumerable<ActionDisplayFrame> source)
        {
            var values = (source ?? Array.Empty<ActionDisplayFrame>()).ToArray();
            if (values.Any(v => v == null)) throw new ArgumentException("表示コマが必要です");
            return Array.AsReadOnly(values);
        }
        internal static int Total(IReadOnlyList<ActionDisplayFrame> frames) => checked(frames.Sum(v => v.Frames));
        public int SelectFrame(PlayerAttackPhase phase, long elapsed)
        {
            var frames = phase == PlayerAttackPhase.Startup ? Startup : phase == PlayerAttackPhase.Executing ? Execution : phase == PlayerAttackPhase.Recovery ? Recovery : Array.Empty<ActionDisplayFrame>();
            if (frames.Count == 0) return -1;
            long end = 0;
            foreach (var frame in frames) { end += frame.Frames; if (elapsed < end) return frame.Index; }
            // 効果処理が継続する場合だけ最後の姿勢を保持。表示側が待機時間を増やさない。
            return frames[frames.Count - 1].Index;
        }
    }

    /// <summary>既存ICardEffectを実行する一アクション。factoryで実行ごとの可変状態を分離する。</summary>
    public sealed class CardActionDefinition
    {
        public string Id { get; }
        public Func<ICardEffect> CreateEffect { get; }
        public PlayerAttackTiming Timing { get; }
        public ActionVisualDefinition Visual { get; }
        public bool WaitForDerivatives { get; }
        public CardActionDefinition(string id, Func<ICardEffect> createEffect, PlayerAttackTiming timing, ActionVisualDefinition visual, bool waitForDerivatives = false)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("アクションIDが必要です");
            Id = id; WaitForDerivatives = waitForDerivatives; CreateEffect = createEffect ?? throw new ArgumentNullException(nameof(createEffect));
            Timing = timing ?? throw new ArgumentNullException(nameof(timing)); Visual = visual ?? throw new ArgumentNullException(nameof(visual));
            if (ActionVisualDefinition.Total(visual.Startup) != timing.StartupFrames || ActionVisualDefinition.Total(visual.Recovery) != timing.RecoveryFrames)
                throw new ArgumentException("表示コマ配分と既存の前後隙フレーム数が一致しません");
        }
    }

    /// <summary>既存の停止対象時計を読む表示スナップショット。壁時計/描画deltaを使用しない。</summary>
    public sealed class CardActionVisualState
    {
        private readonly Func<long> clock;
        private readonly Action resolved;
        private long phaseStarted;
        internal object ActionOwner { get; }
        public CardActionDefinition Definition { get; }
        public PlayerAttackPhase Phase { get; private set; }
        public long StartedFrame { get; }
        public long ElapsedPhaseFrames => Math.Max(0, clock() - phaseStarted) / 50;
        public int DisplayFrame => Definition.Visual.SelectFrame(Phase, ElapsedPhaseFrames);
        public long? EffectFrame { get; private set; }
        public bool? EffectMarkerMatches => !EffectFrame.HasValue || !Definition.Visual.EffectMarkerFrame.HasValue ? (bool?)null : EffectFrame.Value - StartedFrame == Definition.Visual.EffectMarkerFrame.Value;
        internal CardActionVisualState(CardActionDefinition definition, Func<long> clock, Action resolved, object actionOwner)
        { Definition = definition; ActionOwner = actionOwner; this.clock = clock; this.resolved = resolved; StartedFrame = clock() / 50; SetPhase(PlayerAttackPhase.Startup); }
        internal void SetPhase(PlayerAttackPhase phase) { Phase = phase; phaseStarted = clock(); }
        internal void MarkEffect()
        {
            if (EffectFrame.HasValue) return;
            EffectFrame = clock() / 50; resolved();
        }
    }

    /// <summary>カード効果の既存登録入口へ渡す順次列。新しい時計や派生/飛翔体の寿命を作らない。</summary>
    public sealed class CardActionSequenceEffect : ICardEffect, IAsyncCardEffect
    {
        public IReadOnlyList<CardActionDefinition> Actions { get; }
        public CardActionSequenceEffect(IEnumerable<CardActionDefinition> actions)
        {
            var values = actions?.ToArray() ?? throw new ArgumentNullException(nameof(actions));
            if (values.Length == 0 || values.Any(v => v == null)) throw new ArgumentException("一つ以上のアクションが必要です");
            Actions = Array.AsReadOnly(values);
        }
        internal void ValidateLegacyTiming(Card card, PlayerAttackTiming timing)
        {
            if (card.ExtendRecoveryDuringDerivative)
                throw new InvalidOperationException("アクション列では派生待機を各アクションへ明示してください。カード全体の延長指定は暗黙移行しません");
            int? startup = timing?.StartupFrames ?? card.StartupFrames ?? (card.StartupTime > 0 ? (int?)Math.Ceiling(card.StartupTime * 60d - .00001d) : null);
            int? recovery = timing?.RecoveryFrames ?? card.RecoveryFrames ?? (card.RecoveryTime > 0 ? (int?)Math.Ceiling(card.RecoveryTime * 60d - .00001d) : null);
            if (startup.HasValue && startup != Actions[0].Timing.StartupFrames || recovery.HasValue && recovery != Actions[Actions.Count - 1].Timing.RecoveryFrames)
                throw new InvalidOperationException("既存カードの前後隙とアクション列の端の時間が矛盾します");
            if (card.IsInstantEffect && Actions.Any(a => a.Timing.StartupFrames != 0 || a.Timing.RecoveryFrames != 0))
                throw new InvalidOperationException("即時カードに前後隙を追加できません");
        }
        public void Execute(BattleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            context.TrackAction(ExecuteAsync(context, context.ActionCancellation));
        }
        public async FrameTask ExecuteAsync(BattleContext context, CancellationToken ct)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            ValidateLegacyTiming(context.UsedCard, null);
            if (context.EnemySource == null && context.PlayerActionOwner == null)
                throw new InvalidOperationException("プレイヤーのアクション列は通常の非同期カード使用経路から実行してください");
            var effects = Actions.Select(a => a.CreateEffect()).ToArray();
            if (effects.Any(v => v == null || v is CardActionSequenceEffect)) throw new ArgumentException("各アクションは既存の単体効果が必要です");
            bool resolved = false;
            try {
                for (int i = 0; i < Actions.Count; i++) {
                    ct.ThrowIfCancellationRequested();
                    var definition = Actions[i]; var child = context.ForAction(); child.ActionCancellation = ct;
                    var state = new CardActionVisualState(definition, () => context.Battle.ActionPresentationTimeUnits, () => resolved = true, context.PlayerActionOwner);
                    child.ActionVisual = state;
                    child.ProjectileVisualKey = definition.Visual.ProjectileKey;
                    if (context.EnemySource == null && context.Battle.OwnsPlayerAction(context.PlayerActionOwner)) context.Battle.PlayerActionVisual = state;
                    void Phase(PlayerAttackPhase phase) {
                        state.SetPhase(phase);
                        if (context.PlayerActionOwner != null) context.Battle.SetPlayerActionPhase(context.PlayerActionOwner, phase, definition.Timing.CancelAfterFrames);
                    }
                    try {
                        Phase(PlayerAttackPhase.Startup);
                        await context.Battle.WaitForActionFramesAsync(definition.Timing.StartupFrames, ct);
                        ct.ThrowIfCancellationRequested(); Phase(PlayerAttackPhase.Executing);
                        if (effects[i] is Effects.ModifiableCardEffect modifiable) await modifiable.ExecuteAsync(child, ct, false);
                        else if (effects[i] is IAsyncCardEffect asynchronous) await asynchronous.ExecuteAsync(child, ct);
                        else effects[i].Execute(child);
                        state.MarkEffect();
                        if (definition.WaitForDerivatives) await child.WaitForDerivativesAsync(ct);
                        Phase(PlayerAttackPhase.Recovery);
                        await context.Battle.WaitForActionFramesAsync(definition.Timing.RecoveryFrames, ct);
                    } finally {
                        child.ActionVisual = null;
                        if (ReferenceEquals(context.Battle.PlayerActionVisual, state)) context.Battle.PlayerActionVisual = null;
                    }
                }
            } catch (OperationCanceledException) when (resolved) { /* 発生済みカードは使用済み。残りのアクションを実行しない。 */ }
        }
    }
}
