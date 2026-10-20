using Olca.Core.Interfaces;
using System.Linq;
using UnityEngine;

namespace Olca.Unity
{
    /// <summary>
    /// UnityEngine.Timeを使用した時間プロバイダー
    /// 整数ベース（ミリ秒）で時間を提供し、浮動小数点の累積誤差を回避
    /// </summary>
    public class UnityTimeProvider : ITimeProvider
    {
        /// <summary>
        /// 前フレームからの経過時間（ミリ秒）
        /// </summary>
        public int DeltaTimeMs => Mathf.RoundToInt(UnityEngine.Time.deltaTime * 1000f);

        /// <summary>
        /// ゲーム開始からの経過時間（ミリ秒）
        /// </summary>
        public long TimeMs => (long)(UnityEngine.Time.time * 1000f);

        /// <summary>
        /// スケールされていない前フレームからの経過時間（ミリ秒）
        /// </summary>
        public int UnscaledDeltaTimeMs => Mathf.RoundToInt(UnityEngine.Time.unscaledDeltaTime * 1000f);

        /// <summary>
        /// 前フレームからの経過時間（秒）- View層での互換性用
        /// </summary>
        public float DeltaTime => UnityEngine.Time.deltaTime;

        /// <summary>
        /// ゲーム開始からの経過時間（秒）- View層での互換性用
        /// </summary>
        public float Time => UnityEngine.Time.time;

        /// <summary>
        /// スケールされていない前フレームからの経過時間（秒）- View層での互換性用
        /// </summary>
        public float UnscaledDeltaTime => UnityEngine.Time.unscaledDeltaTime;
    }
}

namespace Olca.Unity
{
    /// <summary>単調時計から経過量を取得する。UnityのmaximumDeltaTimeによる切捨てを受けない。</summary>
    public sealed class UnityLogicalLoop : System.IDisposable
    {
        public UnityLogicalLoop(bool observeEditor = false)
        {
#if UNITY_EDITOR
            if (observeEditor) UnityEditor.EditorApplication.pauseStateChanged += EditorPauseChanged;
#endif
        }
#if UNITY_EDITOR
        private void EditorPauseChanged(UnityEditor.PauseState state)
        { Observe(state == UnityEditor.PauseState.Paused && Time.timeScale > 0); ReleaseInput(); }
#endif
        public void Dispose()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= EditorPauseChanged;
#endif
        }
        public Olca.Core.Logic.Integration.LogicalFrameClock Clock { get; private set; } = new Olca.Core.Logic.Integration.LogicalFrameClock();
        private readonly Olca.Core.Logic.Integration.LogicalInputQueue inputs = new Olca.Core.Logic.Integration.LogicalInputQueue();
        private readonly System.Collections.Generic.SortedDictionary<long, System.Collections.Generic.List<System.Action>> commands = new System.Collections.Generic.SortedDictionary<long, System.Collections.Generic.List<System.Action>>();
        private System.Func<long> readTimestamp = System.Diagnostics.Stopwatch.GetTimestamp;
        private long observed = System.Diagnostics.Stopwatch.GetTimestamp();
        private long wallRemainder;
        private long inputFloor = long.MinValue;
        private int inputGeneration;
        private readonly System.Collections.Generic.List<System.Action> frameInputCommands = new System.Collections.Generic.List<System.Action>();
        public long LateInputEvents { get; private set; }
        private readonly System.Collections.Generic.List<TimestampedLogicalInput> pendingEvents = new System.Collections.Generic.List<TimestampedLogicalInput>();
        private Olca.Core.Logic.Integration.LogicalInput previous = new Olca.Core.Logic.Integration.LogicalInput();
        public void QueueTimestampedCommand(long timestamp, System.Action action)
        {
            if (action == null) throw new System.ArgumentNullException(nameof(action));
            pendingEvents.Add(new TimestampedLogicalInput(timestamp, null, action));
        }
        public void Schedule(System.Action action, int delayFrames = 1)
        {
            long target = Clock.InputFrame + System.Math.Max(0, delayFrames - 1);
            if (!commands.TryGetValue(target, out var list)) commands[target] = list = new System.Collections.Generic.List<System.Action>();
            list.Add(action);
        }
        public void AfterFrames(int frames, System.Action action)
        {
            long target = Clock.Frame + System.Math.Max(1, frames);
            if (!commands.TryGetValue(target, out var list)) commands[target] = list = new System.Collections.Generic.List<System.Action>();
            list.Add(action);
        }
        public void Observe(bool active) => ObserveAt(readTimestamp(), active);
        private void ObserveAt(long now, bool active)
        {
            now = System.Math.Max(now, observed);
            if (active)
            {
                long frequency = System.Diagnostics.Stopwatch.Frequency, elapsed = now - observed;
                long numerator = elapsed % frequency * 10000000L + wallRemainder;
                Clock.Accumulate(elapsed / frequency * 10000000L + numerator / frequency);
                wallRemainder = numerator % frequency;
            }
            observed = now;
        }
        /// <summary>生成時刻までの経過量で入力フレームを確定してから追いつき処理を行う。</summary>
        public void PumpTimestamped(bool active, System.Collections.Generic.IEnumerable<TimestampedLogicalInput> events,
            System.Action<long, int, Olca.Core.Logic.Integration.LogicalInput> tick,
            System.Func<bool> canAdvance = null, System.Func<bool> advanceHeldTime = null)
        {
            long now = readTimestamp();
            if (!active) { ObserveAt(now, false); ReleaseInput(); return; }
            if (events != null) pendingEvents.AddRange(events);
            var ordered = pendingEvents.OrderBy(e => e.Timestamp).ToArray(); pendingEvents.Clear();
            foreach (var entry in ordered)
            {
                if (entry.Timestamp < inputFloor) continue;
                if (entry.Timestamp > now) { pendingEvents.Add(entry); continue; }
                // 既に処理済みの時刻に遅着したイベントは巻き戻さず次の未処理フレームへ置く。
                if (entry.Timestamp < observed) LateInputEvents++;
                ObserveAt(entry.Timestamp, true);
                if (entry.Command == null) inputs.Enqueue(Clock.InputFrame, entry.Input);
                else
                {
                    int generation = inputGeneration;
                    Schedule(() => frameInputCommands.Add(() => { if (generation == inputGeneration) entry.Command(); }));
                }
            }
            ObserveAt(now, true);
            Advance(0, null, tick, canAdvance, advanceHeldTime: advanceHeldTime);
        }
        // セーブ読込で攻略そのものを置き換える場合だけ使用。通常のポーズには使用しない。
        public void StartNewTimeline()
        {
            Clock = new Olca.Core.Logic.Integration.LogicalFrameClock();
            commands.Clear(); ReleaseInput(); wallRemainder = 0; Observe(false);
        }
        public void ReleaseInput() { inputGeneration++; inputs.Release(); pendingEvents.Clear(); inputFloor = readTimestamp(); previous = new Olca.Core.Logic.Integration.LogicalInput(); }
        public void Pump(bool active, Olca.Core.Logic.Integration.LogicalInput sample,
            System.Action<long, int, Olca.Core.Logic.Integration.LogicalInput> tick, System.Func<bool> canAdvance = null, System.Func<bool> advanceHeldTime = null)
        {
            Observe(active);
            if (!active) { ReleaseInput(); return; }
            Advance(0, sample, tick, canAdvance, advanceHeldTime: advanceHeldTime);
        }
        // 記録済みの経過時間と入力を通常Controllerの経路へ再生する入口。
        public void Advance(long elapsedWallTicks, Olca.Core.Logic.Integration.LogicalInput sample,
            System.Action<long, int, Olca.Core.Logic.Integration.LogicalInput> tick, System.Func<bool> canAdvance = null, int budget = 240, System.Func<bool> advanceHeldTime = null)
        {
            Clock.Accumulate(elapsedWallTicks);
            if (sample != null && (sample.Horizontal != previous.Horizontal || sample.Vertical != previous.Vertical ||
                sample.BasicAttackButton != previous.BasicAttackButton || sample.DashButton != previous.DashButton ||
                sample.JumpButtonDown || sample.BasicAttackButtonDown || sample.BasicAttackButtonUp || sample.CardUseButtonDown || sample.CustomScreenButtonDown || sample.DashButtonDown || sample.MenuConfirmButtonDown || sample.MenuDecideButtonDown || sample.HorizontalDown != 0 || sample.TouchHorizontalDown != 0 || sample.CancelTouchTap))
            { inputs.Enqueue(Clock.InputFrame, sample); previous = sample.Copy(); previous.ClearEdges(); }
            var timeline = Clock;
            timeline.Drain((frame, ms) => {
                while (commands.Count > 0)
                {
                    var iterator = commands.GetEnumerator(); iterator.MoveNext(); var first = iterator.Current; iterator.Dispose();
                    if (first.Key > frame) break;
                    commands.Remove(first.Key); foreach (var action in first.Value) { action(); if (!ReferenceEquals(timeline, Clock)) return; }
                }
                var input = inputs.Take(frame, advanceHeldTime == null || advanceHeldTime());
                if (input.HasInputSample) GameplayPause.UpdateInputBarrier(input.AnyKey);
                GameplayPause.AdvanceLogicalFrame();
                var inputCommands = frameInputCommands.ToArray(); frameInputCommands.Clear();
                foreach (var command in inputCommands) { command(); if (!ReferenceEquals(timeline, Clock)) return; }
                tick(frame, ms, input);
            }, budget, () => ReferenceEquals(timeline, Clock) && (canAdvance == null || canAdvance()));
        }
    }
}

namespace Olca.Unity
{
    /// <summary>Stopwatchと同じ時間軸の生成時刻と、その時点の入力状態。</summary>
    public readonly struct TimestampedLogicalInput
    {
        public readonly long Timestamp;
        public readonly Olca.Core.Logic.Integration.LogicalInput Input;
        public readonly System.Action Command;
        public TimestampedLogicalInput(long timestamp, Olca.Core.Logic.Integration.LogicalInput input, System.Action command = null)
        { Timestamp = timestamp; Input = input; Command = command; }
    }
}
