#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.CameraSwitcherOutputAdapter.Tests.Fakes
{
    /// <summary>
    /// In-memory <see cref="IOutputCommandDispatcher"/> double for unit / integration
    /// tests. Records every registered handler so a test can re-invoke them
    /// (<see cref="InvokeStateAt"/> / <see cref="InvokeEventAt"/> / <see cref="InvokeRequestAt"/>)
    /// to drive the adapter, and exposes published State / Event payloads via the
    /// <see cref="PublishedStates"/> and <see cref="PublishedEvents"/> buffers.
    /// </summary>
    /// <remarks>
    /// The fake intentionally does not implement <c>kind</c> deduplication or
    /// coalesce semantics — those are exercised by the production dispatcher. Tests
    /// inspect the raw list to verify ordering / contents.
    /// </remarks>
    public sealed class FakeOutputCommandDispatcher : IOutputCommandDispatcher
    {
        private readonly Dictionary<string, StateHandlerEntry> _stateHandlers = new();
        private readonly Dictionary<string, EventHandlerEntry> _eventHandlers = new();
        private readonly Dictionary<string, RequestHandlerEntry> _requestHandlers = new();
        private bool _disposed;

        public IReadOnlyDictionary<string, StateHandlerEntry> StateHandlers => _stateHandlers;
        public IReadOnlyDictionary<string, EventHandlerEntry> EventHandlers => _eventHandlers;
        public IReadOnlyDictionary<string, RequestHandlerEntry> RequestHandlers => _requestHandlers;

        /// <summary>
        /// Captures the per-Request response returned by the registered handler each
        /// time <see cref="InvokeRequestAt{TRequest, TResponse}"/> is called.
        /// </summary>
        public List<CapturedResponse> CapturedResponses { get; } = new();

        public int RegisteredHandlerCount =>
            _stateHandlers.Count + _eventHandlers.Count + _requestHandlers.Count;

        public OutputCommandHandlerRegistration RegisterStateHandler<TPayload>(string topic, Action<StateCommand<TPayload>> handler)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic is empty", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_stateHandlers.ContainsKey(topic))
                throw new InvalidOperationException($"state handler already registered: {topic}");
            _stateHandlers[topic] = new StateHandlerEntry(typeof(TPayload), env => handler((StateCommand<TPayload>)env));
            return new OutputCommandHandlerRegistration(() => _stateHandlers.Remove(topic));
        }

        public OutputCommandHandlerRegistration RegisterEventHandler<TPayload>(string topic, Action<EventCommand<TPayload>> handler)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic is empty", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_eventHandlers.ContainsKey(topic))
                throw new InvalidOperationException($"event handler already registered: {topic}");
            _eventHandlers[topic] = new EventHandlerEntry(typeof(TPayload), env => handler((EventCommand<TPayload>)env));
            return new OutputCommandHandlerRegistration(() => _eventHandlers.Remove(topic));
        }

        public OutputCommandHandlerRegistration RegisterRequestHandler<TRequest, TResponse>(string topic, Func<RequestCommand<TRequest>, TResponse> handler)
        {
            EnsureNotDisposed();
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic is empty", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (_requestHandlers.ContainsKey(topic))
                throw new InvalidOperationException($"request handler already registered: {topic}");
            _requestHandlers[topic] = new RequestHandlerEntry(typeof(TRequest), typeof(TResponse), env => handler((RequestCommand<TRequest>)env)!);
            return new OutputCommandHandlerRegistration(() => _requestHandlers.Remove(topic));
        }

        public void InvokeStateAt<TPayload>(string topic, TPayload payload, long receivedAtTicks = 0)
        {
            if (!_stateHandlers.TryGetValue(topic, out var entry))
                throw new InvalidOperationException($"no state handler for topic: {topic}");
            var cmd = new StateCommand<TPayload>
            {
                Topic = topic,
                Payload = payload,
                ReceivedAtTicks = receivedAtTicks == 0 ? DateTime.UtcNow.Ticks : receivedAtTicks,
            };
            entry.Invoker(cmd);
        }

        public void InvokeEventAt<TPayload>(string topic, TPayload payload, long receivedAtTicks = 0)
        {
            if (!_eventHandlers.TryGetValue(topic, out var entry))
                throw new InvalidOperationException($"no event handler for topic: {topic}");
            var cmd = new EventCommand<TPayload>
            {
                Topic = topic,
                Payload = payload,
                ReceivedAtTicks = receivedAtTicks == 0 ? DateTime.UtcNow.Ticks : receivedAtTicks,
            };
            entry.Invoker(cmd);
        }

        public TResponse InvokeRequestAt<TRequest, TResponse>(string topic, TRequest payload, string? correlationId = null)
        {
            if (!_requestHandlers.TryGetValue(topic, out var entry))
                throw new InvalidOperationException($"no request handler for topic: {topic}");
            var cmd = new RequestCommand<TRequest>
            {
                Topic = topic,
                CorrelationId = correlationId,
                Payload = payload,
                ReceivedAtTicks = DateTime.UtcNow.Ticks,
            };
            var raw = entry.Invoker(cmd);
            CapturedResponses.Add(new CapturedResponse(topic, correlationId, typeof(TResponse), raw));
            return (TResponse)raw!;
        }

        public void ResetBuffers()
        {
            CapturedResponses.Clear();
        }

        public void Dispose()
        {
            _disposed = true;
            _stateHandlers.Clear();
            _eventHandlers.Clear();
            _requestHandlers.Clear();
        }

        private void EnsureNotDisposed()
        {
            if (_disposed) throw new InvalidOperationException("FakeOutputCommandDispatcher disposed");
        }

        public sealed class StateHandlerEntry
        {
            public StateHandlerEntry(Type payloadType, Action<object> invoker)
            {
                PayloadType = payloadType;
                Invoker = invoker;
            }

            public Type PayloadType { get; }
            public Action<object> Invoker { get; }
        }

        public sealed class EventHandlerEntry
        {
            public EventHandlerEntry(Type payloadType, Action<object> invoker)
            {
                PayloadType = payloadType;
                Invoker = invoker;
            }

            public Type PayloadType { get; }
            public Action<object> Invoker { get; }
        }

        public sealed class RequestHandlerEntry
        {
            public RequestHandlerEntry(Type requestType, Type responseType, Func<object, object> invoker)
            {
                RequestType = requestType;
                ResponseType = responseType;
                Invoker = invoker;
            }

            public Type RequestType { get; }
            public Type ResponseType { get; }
            public Func<object, object> Invoker { get; }
        }

        public readonly struct CapturedResponse
        {
            public CapturedResponse(string topic, string? correlationId, Type responseType, object? payload)
            {
                Topic = topic;
                CorrelationId = correlationId;
                ResponseType = responseType;
                Payload = payload;
            }

            public string Topic { get; }
            public string? CorrelationId { get; }
            public Type ResponseType { get; }
            public object? Payload { get; }
        }
    }
}
