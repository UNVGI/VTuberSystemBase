#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.UiToolkitShell.Tests.TestSupport
{
    /// <summary>
    /// Test double for the <see cref="ICoreIpcBus"/> abstraction. Allows tests to control connection
    /// state, send results, inject inbound messages, and match Request/Response correlation IDs without
    /// touching any concrete transport. Intended for use in EditMode tests that exercise the
    /// ui-toolkit-shell facades on top of the core-ipc abstract layer.
    /// </summary>
    public sealed class FakeIpcClient : ICoreIpcBus
    {
        private readonly object syncRoot = new();
        private readonly FakeConnectionDiagnostics diagnostics = new();
        private readonly Dictionary<string, List<ISubscription>> stateSubscriptions = new();
        private readonly Dictionary<string, List<ISubscription>> eventSubscriptions = new();
        private readonly Dictionary<string, IRequestHandlerEntry> requestHandlers = new();
        private readonly Dictionary<string, IPendingRequest> pendingRequests = new();
        private readonly List<string> pendingRequestOrder = new();
        private readonly List<RecordedMessage> sentMessages = new();
        private long correlationCounter;

        public IConnectionDiagnostics Diagnostics => diagnostics;

        public bool IsConnected => diagnostics.CurrentState == ConnectionState.Connected;

        /// <summary>Optional interceptor that lets tests substitute a custom <see cref="IpcResult"/> for outbound sends.</summary>
        public Func<RecordedMessage, IpcResult>? SendInterceptor { get; set; }

        /// <summary>Read-only snapshot of every Publish*/Request* sent through this fake.</summary>
        public IReadOnlyList<RecordedMessage> SentMessages
        {
            get
            {
                lock (syncRoot)
                {
                    return sentMessages.ToArray();
                }
            }
        }

        /// <summary>Sets the connection state and raises <see cref="IConnectionDiagnostics.ConnectionStateChanged"/>.</summary>
        public void SetConnectionState(ConnectionState newState)
        {
            diagnostics.SetState(newState);
        }

        public IpcResult PublishState<TPayload>(string topic, TPayload payload)
            => SendInternal(MessageKind.State, topic, payload, correlationId: null);

        public IpcResult PublishEvent<TPayload>(string topic, TPayload payload)
            => SendInternal(MessageKind.Event, topic, payload, correlationId: null);

        public Task<IpcResult<TResponse>> RequestAsync<TRequest, TResponse>(
            string topic,
            TRequest payload,
            RequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(topic))
            {
                return Task.FromResult(IpcResult<TResponse>.Fail(new CoreIpcError.InvalidTopic(topic ?? string.Empty)));
            }
            if (!IsConnected)
            {
                return Task.FromResult(IpcResult<TResponse>.Fail(new CoreIpcError.NotConnected()));
            }

            var correlationId = Interlocked.Increment(ref correlationCounter).ToString();
            var record = new RecordedMessage(MessageKind.Request, topic, payload, correlationId);
            lock (syncRoot)
            {
                sentMessages.Add(record);
            }

            if (SendInterceptor != null)
            {
                var overridden = SendInterceptor(record);
                if (!overridden.Success)
                {
                    return Task.FromResult(IpcResult<TResponse>.Fail(overridden.Error!));
                }
            }

            IRequestHandlerEntry? handlerEntry;
            lock (syncRoot)
            {
                requestHandlers.TryGetValue(topic, out handlerEntry);
            }
            if (handlerEntry is RequestHandlerEntry<TRequest, TResponse> typed)
            {
                return typed.InvokeAsync(payload, cancellationToken);
            }

            var tcs = new TaskCompletionSource<IpcResult<TResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = new PendingRequest<TResponse>(correlationId, tcs);
            lock (syncRoot)
            {
                pendingRequests[correlationId] = pending;
                pendingRequestOrder.Add(correlationId);
            }
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    lock (syncRoot)
                    {
                        if (!pendingRequests.Remove(correlationId)) return;
                        pendingRequestOrder.Remove(correlationId);
                    }
                    tcs.TrySetResult(IpcResult<TResponse>.Fail(new CoreIpcError.HandlerException("Cancelled")));
                });
            }
            var timeout = options?.Timeout;
            if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(timeout.Value, cancellationToken).ConfigureAwait(false);
                    lock (syncRoot)
                    {
                        if (!pendingRequests.Remove(correlationId)) return;
                        pendingRequestOrder.Remove(correlationId);
                    }
                    tcs.TrySetResult(IpcResult<TResponse>.Fail(new CoreIpcError.RequestTimeout(timeout.Value)));
                });
            }
            return tcs.Task;
        }

        public ISubscriptionToken SubscribeState<TPayload>(string topic, Action<TPayload> handler)
            => SubscribeInternal(stateSubscriptions, topic, handler);

        public ISubscriptionToken SubscribeEvent<TPayload>(string topic, Action<TPayload> handler)
            => SubscribeInternal(eventSubscriptions, topic, handler);

        public ISubscriptionToken RegisterRequestHandler<TRequest, TResponse>(
            string topic,
            Func<TRequest, CancellationToken, Task<TResponse>> handler)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic must not be null or empty", nameof(topic));
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var entry = new RequestHandlerEntry<TRequest, TResponse>(handler);
            lock (syncRoot)
            {
                requestHandlers[topic] = entry;
            }
            return new RequestHandlerToken(this, topic, entry);
        }

        /// <summary>Inject an inbound state message; synchronously invokes all matching state subscribers.</summary>
        public void InjectState<TPayload>(string topic, TPayload payload)
            => DispatchInjection(stateSubscriptions, topic, payload);

        /// <summary>Inject an inbound event message; synchronously invokes all matching event subscribers.</summary>
        public void InjectEvent<TPayload>(string topic, TPayload payload)
            => DispatchInjection(eventSubscriptions, topic, payload);

        /// <summary>Resolve the most recently issued pending request with a successful response.</summary>
        public bool RespondToLastRequest<TResponse>(TResponse response)
        {
            string? lastId;
            lock (syncRoot)
            {
                if (pendingRequestOrder.Count == 0) return false;
                lastId = pendingRequestOrder[pendingRequestOrder.Count - 1];
            }
            return RespondToRequest(lastId, response);
        }

        /// <summary>Resolve a specific pending request (by correlation ID) with a successful response.</summary>
        public bool RespondToRequest<TResponse>(string correlationId, TResponse response)
        {
            IPendingRequest? pending;
            lock (syncRoot)
            {
                if (!pendingRequests.TryGetValue(correlationId, out pending)) return false;
                pendingRequests.Remove(correlationId);
                pendingRequestOrder.Remove(correlationId);
            }
            return pending.TryComplete(response);
        }

        /// <summary>Fail a specific pending request with the given core-ipc error.</summary>
        public bool FailRequest(string correlationId, CoreIpcError error)
        {
            if (error is null) throw new ArgumentNullException(nameof(error));
            IPendingRequest? pending;
            lock (syncRoot)
            {
                if (!pendingRequests.TryGetValue(correlationId, out pending)) return false;
                pendingRequests.Remove(correlationId);
                pendingRequestOrder.Remove(correlationId);
            }
            return pending.TryFail(error);
        }

        /// <summary>Returns the correlation IDs of currently pending requests, in issue order.</summary>
        public IReadOnlyList<string> PendingRequestCorrelationIds
        {
            get
            {
                lock (syncRoot)
                {
                    return pendingRequestOrder.ToArray();
                }
            }
        }

        private IpcResult SendInternal<TPayload>(MessageKind kind, string topic, TPayload payload, string? correlationId)
        {
            if (string.IsNullOrEmpty(topic))
            {
                return IpcResult.Fail(new CoreIpcError.InvalidTopic(topic ?? string.Empty));
            }
            var record = new RecordedMessage(kind, topic, payload, correlationId);
            lock (syncRoot)
            {
                sentMessages.Add(record);
            }
            if (SendInterceptor != null)
            {
                return SendInterceptor(record);
            }
            if (!IsConnected)
            {
                return IpcResult.Fail(new CoreIpcError.NotConnected());
            }
            return IpcResult.Ok();
        }

        private ISubscriptionToken SubscribeInternal<TPayload>(
            Dictionary<string, List<ISubscription>> bucket,
            string topic,
            Action<TPayload> handler)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic must not be null or empty", nameof(topic));
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var subscription = new Subscription<TPayload>(this, bucket, topic, handler);
            lock (syncRoot)
            {
                if (!bucket.TryGetValue(topic, out var list))
                {
                    list = new List<ISubscription>();
                    bucket[topic] = list;
                }
                list.Add(subscription);
            }
            return subscription;
        }

        private void DispatchInjection<TPayload>(
            Dictionary<string, List<ISubscription>> bucket,
            string topic,
            TPayload payload)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic must not be null or empty", nameof(topic));
            ISubscription[] snapshot;
            lock (syncRoot)
            {
                if (!bucket.TryGetValue(topic, out var list)) return;
                snapshot = list.ToArray();
            }
            foreach (var sub in snapshot)
            {
                sub.Deliver(payload);
            }
        }

        private void RemoveSubscription(Dictionary<string, List<ISubscription>> bucket, string topic, ISubscription subscription)
        {
            lock (syncRoot)
            {
                if (!bucket.TryGetValue(topic, out var list)) return;
                list.Remove(subscription);
                if (list.Count == 0) bucket.Remove(topic);
            }
        }

        private void RemoveRequestHandler(string topic, IRequestHandlerEntry expected)
        {
            lock (syncRoot)
            {
                if (requestHandlers.TryGetValue(topic, out var actual) && ReferenceEquals(actual, expected))
                {
                    requestHandlers.Remove(topic);
                }
            }
        }

        private interface ISubscription : ISubscriptionToken
        {
            void Deliver(object? payload);
        }

        private sealed class Subscription<TPayload> : ISubscription
        {
            private readonly FakeIpcClient owner;
            private readonly Dictionary<string, List<ISubscription>> bucket;
            private readonly string topic;
            private readonly Action<TPayload> handler;
            private bool disposed;

            public Subscription(FakeIpcClient owner, Dictionary<string, List<ISubscription>> bucket, string topic, Action<TPayload> handler)
            {
                this.owner = owner;
                this.bucket = bucket;
                this.topic = topic;
                this.handler = handler;
            }

            public void Deliver(object? payload)
            {
                if (disposed) return;
                if (payload is TPayload typed) handler(typed);
                else if (payload is null && !typeof(TPayload).IsValueType) handler(default!);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.RemoveSubscription(bucket, topic, this);
            }
        }

        private interface IRequestHandlerEntry
        {
        }

        private sealed class RequestHandlerEntry<TRequest, TResponse> : IRequestHandlerEntry
        {
            private readonly Func<TRequest, CancellationToken, Task<TResponse>> handler;

            public RequestHandlerEntry(Func<TRequest, CancellationToken, Task<TResponse>> handler)
            {
                this.handler = handler;
            }

            public async Task<IpcResult<TResponse>> InvokeAsync(TRequest payload, CancellationToken cancellationToken)
            {
                try
                {
                    var response = await handler(payload, cancellationToken).ConfigureAwait(false);
                    return IpcResult<TResponse>.Ok(response);
                }
                catch (OperationCanceledException)
                {
                    return IpcResult<TResponse>.Fail(new CoreIpcError.HandlerException("Cancelled"));
                }
                catch (Exception ex)
                {
                    return IpcResult<TResponse>.Fail(new CoreIpcError.HandlerException(ex.Message));
                }
            }
        }

        private sealed class RequestHandlerToken : ISubscriptionToken
        {
            private readonly FakeIpcClient owner;
            private readonly string topic;
            private readonly IRequestHandlerEntry entry;
            private bool disposed;

            public RequestHandlerToken(FakeIpcClient owner, string topic, IRequestHandlerEntry entry)
            {
                this.owner = owner;
                this.topic = topic;
                this.entry = entry;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                owner.RemoveRequestHandler(topic, entry);
            }
        }

        private interface IPendingRequest
        {
            bool TryComplete(object? response);
            bool TryFail(CoreIpcError error);
        }

        private sealed class PendingRequest<TResponse> : IPendingRequest
        {
            private readonly string correlationId;
            private readonly TaskCompletionSource<IpcResult<TResponse>> tcs;

            public PendingRequest(string correlationId, TaskCompletionSource<IpcResult<TResponse>> tcs)
            {
                this.correlationId = correlationId;
                this.tcs = tcs;
            }

            public bool TryComplete(object? response)
            {
                if (response is TResponse typed)
                {
                    return tcs.TrySetResult(IpcResult<TResponse>.Ok(typed));
                }
                if (response is null && !typeof(TResponse).IsValueType)
                {
                    return tcs.TrySetResult(IpcResult<TResponse>.Ok(default!));
                }
                return tcs.TrySetResult(IpcResult<TResponse>.Fail(new CoreIpcError.HandlerException(
                    $"Response type mismatch for correlation {correlationId}: expected {typeof(TResponse).FullName}, got {response?.GetType().FullName ?? "null"}")));
            }

            public bool TryFail(CoreIpcError error)
            {
                return tcs.TrySetResult(IpcResult<TResponse>.Fail(error));
            }
        }

        private sealed class FakeConnectionDiagnostics : IConnectionDiagnostics
        {
            public ConnectionState CurrentState { get; private set; } = ConnectionState.Disconnected;
            public int ReconnectAttemptCount { get; set; }
            public int PendingRequestCount { get; set; }
            public int StateSlotCount { get; set; }
            public int EventQueueCount { get; set; }
            public int ConnectedClientCount { get; set; }

            public event Action<ConnectionState, ConnectionState>? ConnectionStateChanged;

            public void SetState(ConnectionState newState)
            {
                if (newState == CurrentState) return;
                var previous = CurrentState;
                CurrentState = newState;
                ConnectionStateChanged?.Invoke(previous, newState);
            }

            public DiagnosticsSnapshot TakeSnapshot()
            {
                return new DiagnosticsSnapshot(
                    TakenAt: DateTimeOffset.UtcNow,
                    ClientState: CurrentState,
                    ServerConnectedCount: ConnectedClientCount,
                    ReconnectAttemptCount: ReconnectAttemptCount,
                    PendingRequestCount: PendingRequestCount,
                    StateSlotCount: StateSlotCount,
                    EventQueueCount: EventQueueCount);
            }
        }
    }

    /// <summary>Recorded outbound message captured by <see cref="FakeIpcClient"/>.</summary>
    public readonly struct RecordedMessage
    {
        public RecordedMessage(MessageKind kind, string topic, object? payload, string? correlationId)
        {
            Kind = kind;
            Topic = topic;
            Payload = payload;
            CorrelationId = correlationId;
        }

        public MessageKind Kind { get; }
        public string Topic { get; }
        public object? Payload { get; }
        public string? CorrelationId { get; }
    }
}
