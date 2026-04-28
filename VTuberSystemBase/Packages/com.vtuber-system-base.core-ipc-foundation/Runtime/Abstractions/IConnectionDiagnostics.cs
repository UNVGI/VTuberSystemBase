#nullable enable
using System;

namespace VTuberSystemBase.CoreIpc.Abstractions
{
    public interface IConnectionDiagnostics
    {
        ConnectionState CurrentState { get; }

        int ReconnectAttemptCount { get; }

        int PendingRequestCount { get; }

        int StateSlotCount { get; }

        int EventQueueCount { get; }

        int ConnectedClientCount { get; }

        event Action<ConnectionState, ConnectionState> ConnectionStateChanged;

        DiagnosticsSnapshot TakeSnapshot();
    }

    public readonly record struct DiagnosticsSnapshot(
        DateTimeOffset TakenAt,
        ConnectionState ClientState,
        int ServerConnectedCount,
        int ReconnectAttemptCount,
        int PendingRequestCount,
        int StateSlotCount,
        int EventQueueCount);
}
