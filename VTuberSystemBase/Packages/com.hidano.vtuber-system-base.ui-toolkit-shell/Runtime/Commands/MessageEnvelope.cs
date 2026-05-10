#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Read-only wrapper passed to <see cref="IUiSubscriptionClient"/> subscribers when an
    /// inbound message is delivered. Mirrors the core-ipc envelope (topic, kind, correlation
    /// id, payload, received-at) without leaking the underlying transport type. See design.md
    /// §UiSubscriptionClient (Requirements 5.6, 5.7, 5.8).
    /// </summary>
    public readonly struct MessageEnvelope<TPayload>
    {
        public MessageEnvelope(
            string topic,
            MessageKind kind,
            string? correlationId,
            TPayload payload,
            DateTimeOffset receivedAt)
        {
            Topic = topic;
            Kind = kind;
            CorrelationId = correlationId;
            Payload = payload;
            ReceivedAt = receivedAt;
        }

        public string Topic { get; }
        public MessageKind Kind { get; }
        public string? CorrelationId { get; }
        public TPayload Payload { get; }
        public DateTimeOffset ReceivedAt { get; }
    }
}
