using Olca.Core.Interfaces;
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
        private void EditorPauseChanged(UnityEditor.PauseState state) => Observe(state == UnityEditor.PauseState.Paused && Time.timeScale > 0);
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
        private double wallRemainder;
        private Olca.Core.Logic.Integration.LogicalInput previous = new Olca.Core.Logic.Integration.LogicalInput();
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
        public void Observe(bool active)
        {
            long now = readTimestamp();
            if (active)
            {
                double ticks = (now - observed) * (10000000d / System.Diagnostics.Stopwatch.Frequency) + wallRemainder;
                long whole = (long)ticks; wallRemainder = ticks - whole; Clock.Accumulate(whole);
            }
            observed = now;
        }
        // セーブ読込で攻略そのものを置き換える場合だけ使用。通常のポーズには使用しない。
        public void StartNewTimeline()
        {
            Clock = new Olca.Core.Logic.Integration.LogicalFrameClock();
            commands.Clear(); ReleaseInput(); wallRemainder = 0; Observe(false);
        }
        public void ReleaseInput() { inputs.Release(); previous = new Olca.Core.Logic.Integration.LogicalInput(); }
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
                sample.JumpButtonDown || sample.CardUseButtonDown || sample.CustomScreenButtonDown || sample.DashButtonDown || sample.HorizontalDown != 0))
            { inputs.Enqueue(Clock.InputFrame, sample); previous = sample.Copy(); previous.ClearEdges(); }
            Clock.Drain((frame, ms) => {
                while (commands.Count > 0)
                {
                    var iterator = commands.GetEnumerator(); iterator.MoveNext(); var first = iterator.Current; iterator.Dispose();
                    if (first.Key > frame) break;
                    commands.Remove(first.Key); foreach (var action in first.Value) action();
                }
                GameplayPause.AdvanceLogicalFrame();
                tick(frame, ms, inputs.Take(frame, advanceHeldTime == null || advanceHeldTime()));
            }, budget, canAdvance);
        }
    }
}
