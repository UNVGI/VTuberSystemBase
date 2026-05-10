#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VTuberSystemBase.CoreIpc.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// In-memory <see cref="ICoreIpcBus"/> double used to capture the
    /// <c>PublishState</c> / <c>PublishEvent</c> traffic emitted by the adapter
    /// (CamerasListPublisher, FailureAggregator, etc.) without spinning up a
    /// WebSocket transport.
    /// </summary>
    public sealed class FakeCoreIpcBus : ICoreIpcBus
    {
        public List<PublishedState> PublishedStates { get; } = new();
        public List<PublishedEvent> PublishedEvents { get; } = new();
        public List<RequestRecord> SentRequests { get; } = new();

        public IConnectionDiagnostics Diagnostics => StubDiagnostics.Instance;

        public IpcResult PublishState<TPayload>(string topic, TPayload payload)
        {
            PublishedStates.Add(new PublishedState(topic, typeof(TPayload), payload));
            return IpcResult.Ok();
        }

        public IpcResult PublishEvent<TPayload>(string topic, TPayload payload)
        {
            PublishedEvents.Add(new PublishedEvent(topic, typeof(TPayload), payload));
            return IpcResult.Ok();
        }

        public Task<IpcResult<TResponse>> RequestAsync<TRequest, TResponse>(
            string topic,
            TRequest payload,
            RequestOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            SentRequests.Add(new RequestRecord(topic, typeof(TRequest), payload));
            // Tests that need a non-default response should override this fake.
            return Task.FromResult(IpcResult<TResponse>.Ok(default!));
        }

        public ISubscriptionToken SubscribeState<TPayload>(string topic, Action<TPayload> handler)
            => StubSubscription.Instance;

        public ISubscriptionToken SubscribeEvent<TPayload>(string topic, Action<TPayload> handler)
            => StubSubscription.Instance;

        public ISubscriptionToken RegisterRequestHandler<TRequest, TResponse>(
            string topic,
            Func<TRequest, CancellationToken, Task<TResponse>> handler)
            => StubSubscription.Instance;

        public void Reset()
        {
            PublishedStates.Clear();
            PublishedEvents.Clear();
            SentRequests.Clear();
        }

        public readonly struct PublishedState
        {
            public PublishedState(string topic, Type payloadType, object? payload)
            {
                Topic = topic;
                PayloadType = payloadType;
                Payload = payload;
            }

            public string Topic { get; }
            public Type PayloadType { get; }
            public object? Payload { get; }
        }

        public readonly struct PublishedEvent
        {
            public PublishedEvent(string topic, Type payloadType, object? payload)
            {
                Topic = topic;
                PayloadType = payloadType;
                Payload = payload;
            }

            public string Topic { get; }
            public Type PayloadType { get; }
            public object? Payload { get; }
        }

        public readonly struct RequestRecord
        {
            public RequestRecord(string topic, Type payloadType, object? payload)
            {
                Topic = topic;
                PayloadType = payloadType;
                Payload = payload;
            }

            public string Topic { get; }
            public Type PayloadType { get; }
            public object? Payload { get; }
        }

        private sealed class StubSubscription : ISubscriptionToken
        {
            public static readonly StubSubscription Instance = new();
            public void Dispose() { }
        }

        private sealed class StubDiagnostics : IConnectionDiagnostics
        {
            public static readonly StubDiagnostics Instance = new();
            public ConnectionState CurrentState => ConnectionState.Disconnected;
            public int ReconnectAttemptCount => 0;
            public int PendingRequestCount => 0;
            public int StateSlotCount => 0;
            public int EventQueueCount => 0;
            public int ConnectedClientCount => 0;

            public event Action<ConnectionState, ConnectionState>? ConnectionStateChanged
            {
                add { }
                remove { }
            }

            public DiagnosticsSnapshot TakeSnapshot() => new DiagnosticsSnapshot(
                DateTimeOffset.UtcNow,
                ConnectionState.Disconnected,
                0,
                0,
                0,
                0,
                0);
        }
    }
}
