#nullable enable

namespace VTuberSystemBase.UiToolkitShell.Diagnostics
{
    /// <summary>
    /// Shell-wide diagnostics sink. Implementations multiplex into the Unity Console and the
    /// in-shell diagnostics surface (notification bar / diagnostics panel) but never into
    /// the main output surface (Display 2+). See design.md §Diagnostics for the contract.
    /// </summary>
    public interface IDiagnosticsLogger
    {
        void Log(LogLevel level, LogCategory category, string message, object? context = null);

        LogLevel MinimumLevel { get; set; }
    }

    /// <summary>
    /// Severity ordering: <c>Trace &lt; Debug &lt; Info &lt; Warning &lt; Error</c>.
    /// Entries with <c>level &lt; MinimumLevel</c> are dropped before any sink sees them.
    /// </summary>
    public enum LogLevel
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
    }

    /// <summary>
    /// Categorisation used by all shell-internal log emitters. <c>TabSpec</c> is the default
    /// category for log calls originating from tab-spec code.
    /// </summary>
    public enum LogCategory
    {
        Preload,
        TabSwitch,
        AssetLoad,
        Ipc,
        Connection,
        Skin,
        Lifecycle,
        TabSpec,
    }
}
