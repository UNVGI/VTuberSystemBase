#nullable enable
using System.Text.Json;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public readonly record struct MessageEnvelope(
        string ProtocolVersion,
        MessageKind Kind,
        string Topic,
        string? CorrelationId,
        long TimestampUnixMs,
        JsonElement Payload);
}
