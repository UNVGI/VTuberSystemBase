#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Diagnostics
{
    /// <summary>
    /// Produces an immutable <see cref="ShellDiagnosticsSnapshot"/> on demand.
    /// <see cref="Capture"/> samples each subsystem's current state on the calling thread
    /// and returns synchronously; it must not block, allocate handles, or mutate state.
    /// Designed so tests can substitute every subsystem with a fake without requiring the
    /// full bootstrapper.
    /// </summary>
    public interface IShellDiagnosticsSnapshotProvider
    {
        ShellDiagnosticsSnapshot Capture();
    }
}
