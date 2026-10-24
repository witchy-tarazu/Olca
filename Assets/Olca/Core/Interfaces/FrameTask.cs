using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Olca.Core.Interfaces
{
    public enum FrameTaskStatus { Pending, Succeeded, Faulted, Canceled }

    // 戦闘の単一所有スレッド専用。完了したStep内で継続し、時計/PlayerLoop/同期Contextを参照しない。
    internal sealed class FramePromise
    {
        internal FrameTaskStatus Status;
        internal object Result;
        private ExceptionDispatchInfo failure;
        private List<Action> continuations = new List<Action>();
        internal void Subscribe(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (Status == FrameTaskStatus.Pending) continuations.Add(action); else action();
        }
        internal bool Complete(object result, Exception exception = null)
        {
            if (Status != FrameTaskStatus.Pending) return false;
            Result = result;
            failure = exception == null ? null : ExceptionDispatchInfo.Capture(exception);
            Status = exception == null ? FrameTaskStatus.Succeeded : exception is OperationCanceledException ? FrameTaskStatus.Canceled : FrameTaskStatus.Faulted;
            var pending = continuations; continuations = null;
            foreach (var action in pending) action();
            return true;
        }
        internal object GetResult()
        {
            if (Status == FrameTaskStatus.Pending) throw new InvalidOperationException("論理フレームの完了前には結果を取得できません");
            failure?.Throw(); return Result;
        }
    }

    [AsyncMethodBuilder(typeof(FrameTaskMethodBuilder))]
    public readonly struct FrameTask
    {
        private readonly FramePromise promise;
        internal FrameTask(FramePromise promise) { this.promise = promise; }
        public FrameTaskStatus Status => promise?.Status ?? FrameTaskStatus.Succeeded;
        public static FrameTask CompletedTask => default;
        public static FrameTask FromCanceled(CancellationToken token) { var source = new FrameCompletionSource(); source.TrySetCanceled(token); return source.Task; }
        public FrameTask Preserve() => this;
        public FrameAwaiter GetAwaiter() => new FrameAwaiter(promise);
        public void Forget()
        {
            var value = this;
            GetAwaiter().OnCompleted(() => { if (value.Status != FrameTaskStatus.Canceled) value.GetAwaiter().GetResult(); });
        }
        public Task AsTask()
        {
            var observer = new TaskCompletionSource<bool>(); var value = this;
            GetAwaiter().OnCompleted(() => { try { value.GetAwaiter().GetResult(); observer.TrySetResult(true); } catch (OperationCanceledException) { observer.TrySetCanceled(); } catch (Exception e) { observer.TrySetException(e); } });
            return observer.Task;
        }
        public FrameTask AttachExternalCancellation(CancellationToken token)
        {
            if (!token.CanBeCanceled || Status != FrameTaskStatus.Pending) return this;
            var source = new FrameCompletionSource(); var value = this;
            var registration = token.Register(() => source.TrySetCanceled(token));
            GetAwaiter().OnCompleted(() => { registration.Dispose(); try { value.GetAwaiter().GetResult(); source.TrySetResult(); } catch (Exception e) { source.TrySetException(e); } });
            return source.Task;
        }
    }
    public readonly struct FrameAwaiter : ICriticalNotifyCompletion
    {
        private readonly FramePromise promise;
        internal FrameAwaiter(FramePromise promise) { this.promise = promise; }
        public bool IsCompleted => promise == null || promise.Status != FrameTaskStatus.Pending;
        public void GetResult() { promise?.GetResult(); }
        public void OnCompleted(Action continuation) { if (promise == null) continuation(); else promise.Subscribe(continuation); }
        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
    }
    [AsyncMethodBuilder(typeof(FrameTaskMethodBuilder<>))]
    public readonly struct FrameTask<T>
    {
        private readonly FramePromise promise;
        internal FrameTask(FramePromise promise) { this.promise = promise; }
        public FrameTaskStatus Status => promise?.Status ?? FrameTaskStatus.Succeeded;
        public FrameTask<T> Preserve() => this;
        public FrameAwaiter<T> GetAwaiter() => new FrameAwaiter<T>(promise);
        public void Forget() { var value = this; GetAwaiter().OnCompleted(() => { if (value.Status != FrameTaskStatus.Canceled) value.GetAwaiter().GetResult(); }); }
        public Task<T> AsTask()
        {
            var observer = new TaskCompletionSource<T>(); var value = this;
            GetAwaiter().OnCompleted(() => { try { observer.TrySetResult(value.GetAwaiter().GetResult()); } catch (OperationCanceledException) { observer.TrySetCanceled(); } catch (Exception e) { observer.TrySetException(e); } });
            return observer.Task;
        }
    }
    public readonly struct FrameAwaiter<T> : ICriticalNotifyCompletion
    {
        private readonly FramePromise promise;
        internal FrameAwaiter(FramePromise promise) { this.promise = promise; }
        public bool IsCompleted => promise == null || promise.Status != FrameTaskStatus.Pending;
        public T GetResult() => promise == null ? default : (T)promise.GetResult();
        public void OnCompleted(Action continuation) { if (promise == null) continuation(); else promise.Subscribe(continuation); }
        public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
    }
    public sealed class FrameCompletionSource
    {
        private readonly FramePromise promise = new FramePromise();
        public FrameTask Task => new FrameTask(promise);
        public bool TrySetResult() => promise.Complete(null);
        public bool TrySetCanceled(CancellationToken token = default) => promise.Complete(null, new OperationCanceledException(token));
        public bool TrySetException(Exception error) => promise.Complete(null, error ?? throw new ArgumentNullException(nameof(error)));
    }
    public struct FrameTaskMethodBuilder
    {
        private FramePromise promise;
        public static FrameTaskMethodBuilder Create() => new FrameTaskMethodBuilder { promise = new FramePromise() };
        public FrameTask Task => new FrameTask(promise);
        public void SetResult() => promise.Complete(null);
        public void SetException(Exception exception) => promise.Complete(null, exception);
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
        public void Start<T>(ref T stateMachine) where T : IAsyncStateMachine => stateMachine.MoveNext();
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state) where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine
        { IAsyncStateMachine boxed = state; awaiter.OnCompleted(boxed.MoveNext); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state) where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine
        { IAsyncStateMachine boxed = state; awaiter.UnsafeOnCompleted(boxed.MoveNext); }
    }
    public struct FrameTaskMethodBuilder<T>
    {
        private FramePromise promise;
        public static FrameTaskMethodBuilder<T> Create() => new FrameTaskMethodBuilder<T> { promise = new FramePromise() };
        public FrameTask<T> Task => new FrameTask<T>(promise);
        public void SetResult(T result) => promise.Complete(result);
        public void SetException(Exception exception) => promise.Complete(null, exception);
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
        public void Start<TState>(ref TState state) where TState : IAsyncStateMachine => state.MoveNext();
        public void AwaitOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state) where TAwaiter : INotifyCompletion where TState : IAsyncStateMachine
        { IAsyncStateMachine boxed = state; awaiter.OnCompleted(boxed.MoveNext); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TState>(ref TAwaiter awaiter, ref TState state) where TAwaiter : ICriticalNotifyCompletion where TState : IAsyncStateMachine
        { IAsyncStateMachine boxed = state; awaiter.UnsafeOnCompleted(boxed.MoveNext); }
    }
}
