#nullable enable
using System;
using System.Threading;
using VTuberSystemBase.UiToolkitShell.Diagnostics;
using ICoreIpcBus = VTuberSystemBase.CoreIpc.Abstractions.ICoreIpcBus;
using IpcSubscriptionToken = VTuberSystemBase.CoreIpc.Abstractions.ISubscriptionToken;
using LogLevel = VTuberSystemBase.UiToolkitShell.Diagnostics.LogLevel;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Default <see cref="IUiSubscriptionClient"/> implementation. Wraps the injected
    /// <see cref="ICoreIpcBus"/>'s <c>SubscribeState</c> / <c>SubscribeEvent</c> with a thin
    /// pass-through that builds a <see cref="MessageEnvelope{TPayload}"/>, emits a
    /// <c>Received</c> log via the injected <see cref="IDiagnosticsLogger"/>
    /// (<see cref="LogCategory.Ipc"/>), and protects subscriber callbacks with a
    /// <c>try/catch</c> so a thrown exception is recorded at <see cref="LogLevel.Error"/> but
    /// never propagates back to the bus or to other subscribers (Requirements 5.6, 5.7, 5.8,
    /// 11.5; design.md §Commands §UiSubscriptionClient).
    /// </summary>
    public sealed class UiSubscriptionClient : IUiSubscriptionClient
    {
        private readonly ICoreIpcBus _bus;
        private readonly IDiagnosticsLogger _logger;

        public UiSubscriptionClient(ICoreIpcBus bus, IDiagnosticsLogger logger)
        {
            if (bus is null) throw new ArgumentNullException(nameof(bus));
            if (logger is null) throw new ArgumentNullException(nameof(logger));
            _bus = bus;
            _logger = logger;
        }

        public ISubscriptionToken Subscribe<TPayload>(
            string topic,
            MessageKind kind,
            Action<MessageEnvelope<TPayload>> callback)
        {
            if (callback is null) throw new ArgumentNullException(nameof(callback));
            if (string.IsNullOrEmpty(topic))
                throw new ArgumentException("topic must not be null or empty", nameof(topic));

            var subscription = new Subscription<TPayload>(topic, kind, callback, _logger);

            IpcSubscriptionToken inner = kind switch
            {
                MessageKind.State => _bus.SubscribeState<TPayload>(topic, subscription.Dispatch),
                MessageKind.Event => _bus.SubscribeEvent<TPayload>(topic, subscription.Dispatch),
                MessageKind.Response => throw new NotSupportedException(
                    "Response subscriptions are delivered via RequestAsync; explicit Subscribe(Response) is not supported."),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown MessageKind"),
            };

            subscription.AttachInner(inner);
            return subscription;
        }

        private sealed class Subscription<TPayload> : ISubscriptionToken
        {
            private readonly MessageKind _kind;
            private readonly Action<MessageEnvelope<TPayload>> _callback;
            private readonly IDiagnosticsLogger _logger;
            private IpcSubscriptionToken? _inner;
            private int _disposed;

            public Subscription(
                string topic,
                MessageKind kind,
                Action<MessageEnvelope<TPayload>> callback,
                IDiagnosticsLogger logger)
            {
                Topic = topic;
                _kind = kind;
                _callback = callback;
                _logger = logger;
            }

            public string Topic { get; }

            public bool IsActive => Volatile.Read(ref _disposed) == 0;

            public void AttachInner(IpcSubscriptionToken inner)
            {
                _inner = inner;
                if (Volatile.Read(ref _disposed) == 1)
                {
                    inner.Dispose();
                }
            }

            public void Dispatch(TPayload payload)
            {
                if (!IsActive) return;

                _logger.Log(
                    LogLevel.Info,
                    LogCategory.Ipc,
                    $"Received topic={Topic} kind={_kind}");

                try
                {
                    _callback(new MessageEnvelope<TPayload>(
                        Topic,
                        _kind,
                        correlationId: null,
                        payload,
                        DateTimeOffset.UtcNow));
                }
                catch (Exception ex)
                {
                    _logger.Log(
                        LogLevel.Error,
                        LogCategory.Ipc,
                        $"Subscriber callback threw on topic={Topic} kind={_kind}: {ex.Message}",
                        ex);
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
                _inner?.Dispose();
            }
        }
    }
}
