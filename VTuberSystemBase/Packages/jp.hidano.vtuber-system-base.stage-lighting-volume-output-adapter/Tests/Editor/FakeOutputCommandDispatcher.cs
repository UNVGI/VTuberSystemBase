#nullable enable
using System;
using System.Collections.Generic;
using VTuberSystemBase.OutputRendererShell.Abstractions;

namespace VTuberSystemBase.StageLightingVolumeOutputAdapter.Tests.Editor
{
    /// <summary>
    /// In-memory <see cref="IOutputCommandDispatcher"/> double for adapter tests. Tracks
    /// per-topic state / event / request handlers, exposes <c>Emit*</c> methods to drive
    /// the production code under test, and counts active registrations through the
    /// <see cref="RegisteredHandlerCount"/> property required by the production interface.
    ///
    /// Note: this class only implements the dispatcher *receive* side. Outbound publishes
    /// are routed through <c>IAdapterMessageSink</c> and observed by
    /// <c>RecordingMessageSink</c> in the same test (Fact 1: the production
    /// IOutputCommandDispatcher API has no Publish* methods).
    /// </summary>
    internal sealed class FakeOutputCommandDispatcher : IOutputCommandDispatcher
    {
        private readonly Dictionary<string, List<Delegate>> _stateHandlers = new();
        private readonly Dictionary<string, List<Delegate>> _eventHandlers = new();
        private readonly Dictionary<string, List<Delegate>> _requestHandlers = new();

        // Disposal tracking for assertion helpers
        public readonly List<(string Topic, string Kind)> DisposedRegistrations = new();

        private int _liveRegistrationCount;
        private bool _disposed;

        public int RegisteredHandlerCount => _liveRegistrationCount;
        public bool IsDisposed => _disposed;

        public OutputCommandHandlerRegistration RegisterStateHandler<TPayload>(string topic, Action<StateCommand<TPayload>> handler)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return AddHandler(_stateHandlers, topic, handler, "state");
        }

        public OutputCommandHandlerRegistration RegisterEventHandler<TPayload>(string topic, Action<EventCommand<TPayload>> handler)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return AddHandler(_eventHandlers, topic, handler, "event");
        }

        public OutputCommandHandlerRegistration RegisterRequestHandler<TRequest, TResponse>(string topic, Func<RequestCommand<TRequest>, TResponse> handler)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("topic", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return AddHandler(_requestHandlers, topic, handler, "request");
        }

        private OutputCommandHandlerRegistration AddHandler(Dictionary<string, List<Delegate>> table, string topic, Delegate handler, string kind)
        {
            if (!table.TryGetValue(topic, out var list))
            {
                list = new List<Delegate>();
                table[topic] = list;
            }
            list.Add(handler);
            _liveRegistrationCount++;
            bool released = false;
            return new OutputCommandHandlerRegistration(() =>
            {
                if (released) return;
                released = true;
                if (table.TryGetValue(topic, out var ll))
                {
                    ll.Remove(handler);
                    if (ll.Count == 0) table.Remove(topic);
                }
                _liveRegistrationCount--;
                DisposedRegistrations.Add((topic, kind));
            });
        }

        // ----- Test driving methods -----

        public void EmitState<TPayload>(string topic, TPayload payload)
        {
            if (!_stateHandlers.TryGetValue(topic, out var list)) return;
            foreach (var d in new List<Delegate>(list))
            {
                if (d is Action<StateCommand<TPayload>> typed)
                {
                    typed(new StateCommand<TPayload>
                    {
                        Topic = topic,
                        Payload = payload,
                        ReceivedAtTicks = DateTime.UtcNow.Ticks,
                    });
                }
            }
        }

        public void EmitEvent<TPayload>(string topic, TPayload payload)
        {
            if (!_eventHandlers.TryGetValue(topic, out var list)) return;
            foreach (var d in new List<Delegate>(list))
            {
                if (d is Action<EventCommand<TPayload>> typed)
                {
                    typed(new EventCommand<TPayload>
                    {
                        Topic = topic,
                        Payload = payload,
                        ReceivedAtTicks = DateTime.UtcNow.Ticks,
                    });
                }
            }
        }

        public TResponse? InvokeRequest<TRequest, TResponse>(string topic, TRequest payload, string? correlationId = null)
        {
            if (!_requestHandlers.TryGetValue(topic, out var list) || list.Count == 0) return default;
            // Prefer the first registered handler.
            foreach (var d in list)
            {
                if (d is Func<RequestCommand<TRequest>, TResponse> typed)
                {
                    return typed(new RequestCommand<TRequest>
                    {
                        Topic = topic,
                        CorrelationId = correlationId,
                        Payload = payload,
                        ReceivedAtTicks = DateTime.UtcNow.Ticks,
                    });
                }
            }
            return default;
        }

        public bool HasStateHandler(string topic) => _stateHandlers.TryGetValue(topic, out var l) && l.Count > 0;
        public bool HasEventHandler(string topic) => _eventHandlers.TryGetValue(topic, out var l) && l.Count > 0;
        public bool HasRequestHandler(string topic) => _requestHandlers.TryGetValue(topic, out var l) && l.Count > 0;

        public void Dispose()
        {
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new InvalidOperationException("Dispatcher disposed.");
        }
    }
}
