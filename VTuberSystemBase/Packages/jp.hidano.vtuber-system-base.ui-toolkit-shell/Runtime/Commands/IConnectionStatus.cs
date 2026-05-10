#nullable enable
using System;

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// Public surface that exposes the IPC connection state to UI components on the main thread.
    /// Wraps core-ipc-foundation's <c>IConnectionDiagnostics</c> with the
    /// <see cref="ConnectionStatusCode"/> state machine
    /// (<c>Initializing → Connecting → Connected → Disconnected → Reconnecting → FailedPermanently</c>).
    /// See design.md §Commands §IConnectionStatus (Requirements 5.9, 9.3, 9.5, 11.6).
    /// </summary>
    public interface IConnectionStatus
    {
        bool IsConnected { get; }

        ConnectionStatusCode CurrentStatus { get; }

        event Action<ConnectionStatusEvent> OnStatusChanged;
    }
}
