#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public sealed record WebSocketClientOptions
    {
        public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(5);

        public long MaxMessagePayloadBytes { get; init; } = 1_048_576;

        public int ReceiveBufferSize { get; init; } = 16 * 1024;
    }
}
