#nullable enable
using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CoreIpc.Core.Correlation
{
    public sealed class RequestCorrelationRegistry : IDisposable
    {
        private readonly ConcurrentDictionary<string, PendingRequest> _pending = new();
        private readonly TimeSpan _defaultTimeout;
        private readonly Action<Action>? _completeOnMainThread;
        private readonly Action<string, Exception>? _logError;
        private int _disposed;

        public RequestCorrelationRegistry()
            : this(new CoreIpcOptions())
        {
        }

        public RequestCorrelationRegistry(
            CoreIpcOptions options,
            Action<Action>? completeOnMainThread = null,
            Action<string, Exception>? logError = null)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (options.DefaultRequestTimeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "CoreIpcOptions.DefaultRequestTimeout must be non-negative.");
            }

            _defaultTimeout = options.DefaultRequestTimeout;
            _completeOnMainThread = completeOnMainThread;
            _logError = logError;
        }

        public int PendingRequestCount => _pending.Count;

        public TimeSpan DefaultTimeout => _defaultTimeout;

        public string AllocateCorrelationId() => Guid.NewGuid().ToString("N");

        public Task<IpcResult<JsonElement>> RegisterPending(
            string correlationId,
            CancellationToken cancellationToken = default)
            => RegisterPending(correlationId, _defaultTimeout, cancellationToken);

        public Task<IpcResult<JsonElement>> RegisterPending(
            string correlationId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (correlationId is null) throw new ArgumentNullException(nameof(correlationId));
            if (correlationId.Length == 0)
            {
                throw new ArgumentException(
                    "correlationId must be a non-empty string.", nameof(correlationId));
            }
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout),
                    "timeout must be non-negative or Timeout.InfiniteTimeSpan.");
            }

            var tcs = new TaskCompletionSource<IpcResult<JsonElement>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (Volatile.Read(ref _disposed) != 0)
            {
                tcs.TrySetResult(IpcResult<JsonElement>.Fail(new CoreIpcError.NotConnected()));
                return tcs.Task;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return tcs.Task;
            }

            var pending = new PendingRequest(this, correlationId, tcs, timeout);

            if (!_pending.TryAdd(correlationId, pending))
            {
                throw new InvalidOperationException(
                    $"correlation id '{correlationId}' is already registered.");
            }

            try
            {
                if (cancellationToken.CanBeCanceled)
                {
                    pending.CancellationRegistration = cancellationToken.Register(static state =>
                    {
                        var p = (PendingRequest)state!;
                        p.Owner.OnCancelled(p);
                    }, pending);
                }

                if (timeout != Timeout.InfiniteTimeSpan)
                {
                    pending.TimeoutTimer = new Timer(static state =>
                    {
                        var p = (PendingRequest)state!;
                        p.Owner.OnTimeout(p);
                    }, pending, dueTime: timeout, period: Timeout.InfiniteTimeSpan);
                }
            }
            catch
            {
                _pending.TryRemove(correlationId, out _);
                pending.DisposeRegistrations();
                throw;
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                if (_pending.TryRemove(correlationId, out var p2))
                {
                    p2.DisposeRegistrations();
                    Complete(p2.Tcs, IpcResult<JsonElement>.Fail(new CoreIpcError.NotConnected()));
                }
            }

            return tcs.Task;
        }

        public bool MatchResponse(string correlationId, JsonElement payload)
        {
            if (correlationId is null) return false;
            if (!_pending.TryRemove(correlationId, out var pending)) return false;

            pending.DisposeRegistrations();
            Complete(pending.Tcs, IpcResult<JsonElement>.Ok(payload));
            return true;
        }

        public bool FailPending(string correlationId, CoreIpcError error)
        {
            if (error is null) throw new ArgumentNullException(nameof(error));
            if (correlationId is null) return false;
            if (!_pending.TryRemove(correlationId, out var pending)) return false;

            pending.DisposeRegistrations();
            Complete(pending.Tcs, IpcResult<JsonElement>.Fail(error));
            return true;
        }

        public int FailAllPending(CoreIpcError error)
        {
            if (error is null) throw new ArgumentNullException(nameof(error));

            int count = 0;
            foreach (var key in _pending.Keys)
            {
                if (_pending.TryRemove(key, out var pending))
                {
                    pending.DisposeRegistrations();
                    Complete(pending.Tcs, IpcResult<JsonElement>.Fail(error));
                    count++;
                }
            }
            return count;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            FailAllPending(new CoreIpcError.NotConnected());
        }

        private void OnTimeout(PendingRequest pending)
        {
            if (!_pending.TryRemove(pending.CorrelationId, out var stored)) return;
            stored.DisposeRegistrations();
            Complete(stored.Tcs, IpcResult<JsonElement>.Fail(new CoreIpcError.RequestTimeout(stored.Timeout)));
        }

        private void OnCancelled(PendingRequest pending)
        {
            if (!_pending.TryRemove(pending.CorrelationId, out var stored)) return;
            stored.DisposeRegistrations();

            var tcs = stored.Tcs;
            if (_completeOnMainThread is null)
            {
                tcs.TrySetCanceled();
            }
            else
            {
                _completeOnMainThread(() => tcs.TrySetCanceled());
            }
        }

        private void Complete(
            TaskCompletionSource<IpcResult<JsonElement>> tcs,
            IpcResult<JsonElement> result)
        {
            if (_completeOnMainThread is null)
            {
                tcs.TrySetResult(result);
                return;
            }

            try
            {
                _completeOnMainThread(() => tcs.TrySetResult(result));
            }
            catch (Exception ex)
            {
                _logError?.Invoke(
                    "Failed to post correlation completion to main thread; completing inline. " + ex.Message,
                    ex);
                tcs.TrySetResult(result);
            }
        }

        private sealed class PendingRequest
        {
            public RequestCorrelationRegistry Owner { get; }
            public string CorrelationId { get; }
            public TaskCompletionSource<IpcResult<JsonElement>> Tcs { get; }
            public TimeSpan Timeout { get; }
            public Timer? TimeoutTimer { get; set; }
            public CancellationTokenRegistration CancellationRegistration { get; set; }

            public PendingRequest(
                RequestCorrelationRegistry owner,
                string correlationId,
                TaskCompletionSource<IpcResult<JsonElement>> tcs,
                TimeSpan timeout)
            {
                Owner = owner;
                CorrelationId = correlationId;
                Tcs = tcs;
                Timeout = timeout;
            }

            public void DisposeRegistrations()
            {
                TimeoutTimer?.Dispose();
                TimeoutTimer = null;
                CancellationRegistration.Dispose();
            }
        }
    }
}
