#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Commands
{
    /// <summary>
    /// IPC connection state surfaced through <c>IConnectionStatus</c> and aggregated into
    /// <c>ShellDiagnosticsSnapshot</c>. The state machine is
    /// <c>Initializing → Connecting → Connected → Disconnected → Reconnecting → FailedPermanently</c>;
    /// <c>FailedPermanently</c> is reached when core-ipc-foundation R-3 retries are exhausted.
    /// See design.md §Commands §IConnectionStatus for the full transition contract.
    /// </summary>
    public enum ConnectionStatusCode
    {
        Initializing,
        Connecting,
        Connected,
        Disconnected,
        Reconnecting,
        FailedPermanently,
    }
}
