#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Core.Transport.WebSocket
{
    public sealed record WebSocketServerOptions
    {
        public int MaxConcurrentClients { get; init; } = 16;

        public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(30);

        public TimeSpan PongTimeout { get; init; } = TimeSpan.FromSeconds(60);

        public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(5);

        public long MaxMessagePayloadBytes { get; init; } = 1_048_576;

        public int HandshakeMaxRequestBytes { get; init; } = 16 * 1024;

        public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);
    }
}
