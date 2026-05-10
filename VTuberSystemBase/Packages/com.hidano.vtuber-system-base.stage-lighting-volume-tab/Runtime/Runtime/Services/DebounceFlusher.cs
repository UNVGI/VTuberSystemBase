#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace VTuberSystemBase.StageLightingVolumeTab.Services
{
    /// <summary>
    /// Coalescing 500 ms (configurable) debounce timer. Each <see cref="Schedule"/>
    /// call cancels any in-flight wait and replaces the pending action with the
    /// latest one. After <see cref="DebounceFlusher(TimeSpan, IClock)"/>'s
    /// <c>interval</c> elapses without further <see cref="Schedule"/> calls, the
    /// most recently registered action runs once.
    /// See design.md §Services §DebounceFlusher (Requirements 4.7, 8.3, 12.8).
    /// </summary>
    public sealed class DebounceFlusher : IDisposable
    {
        private readonly TimeSpan _interval;
        private readonly IClock _clock;
        private readonly object _gate = new object();

        private Func<Task>? _pendingAction;
        private CancellationTokenSource? _pendingCts;
        private bool _disposed;

        public DebounceFlusher(TimeSpan interval, IClock clock)
        {
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
            _interval = interval;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>
        /// Register / replace the action to run after the debounce window. If a
        /// previous Schedule is still waiting, its action is dropped (latest wins).
        /// Calling <see cref="Schedule"/> on a disposed flusher is a no-op.
        /// </summary>
        public void Schedule(Func<Task> flushAction)
        {
            if (flushAction is null) throw new ArgumentNullException(nameof(flushAction));

            CancellationTokenSource? oldCts;
            CancellationTokenSource newCts;
            lock (_gate)
            {
                if (_disposed) return;
                oldCts = _pendingCts;
                _pendingAction = flushAction;
                newCts = new CancellationTokenSource();
                _pendingCts = newCts;
            }

            // Cancel previous outside the lock to avoid potential deadlocks if any
            // listener of the cancellation runs synchronously.
            oldCts?.Cancel();
            oldCts?.Dispose();

            _ = WaitAndFireAsync(newCts);
        }

        private async Task WaitAndFireAsync(CancellationTokenSource cts)
        {
            try
            {
                await _clock.Delay(_interval, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Func<Task>? toRun;
            lock (_gate)
            {
                // The token of THIS waiter must still be the current pending one;
                // otherwise a newer Schedule has superseded us.
                if (_disposed) return;
                if (!ReferenceEquals(_pendingCts, cts)) return;
                toRun = _pendingAction;
                _pendingAction = null;
                _pendingCts = null;
            }

            cts.Dispose();

            if (toRun is null) return;

            try
            {
                await toRun().ConfigureAwait(false);
            }
            catch
            {
                // Caller is responsible for handling/logging exceptions inside
                // their action; the flusher itself swallows them so a single bad
                // flush cannot crash the dispatch loop.
            }
        }

        /// <summary>
        /// Runs any pending action immediately, ignoring the remaining wait. Safe
        /// to call when nothing is pending (no-op). Used on shutdown / Dispose.
        /// </summary>
        public async Task FlushImmediateAsync()
        {
            Func<Task>? toRun;
            CancellationTokenSource? cts;
            lock (_gate)
            {
                toRun = _pendingAction;
                cts = _pendingCts;
                _pendingAction = null;
                _pendingCts = null;
            }

            cts?.Cancel();
            cts?.Dispose();

            if (toRun is null) return;

            await toRun().ConfigureAwait(false);
        }

        public void Dispose()
        {
            CancellationTokenSource? cts;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                cts = _pendingCts;
                _pendingCts = null;
                _pendingAction = null;
            }
            cts?.Cancel();
            cts?.Dispose();
        }
    }
}
