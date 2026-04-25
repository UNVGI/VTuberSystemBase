#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.UiToolkitShell.Tests.TestSupport
{
    /// <summary>
    /// In-memory <see cref="IDiagnosticsLogger"/> that records every accepted entry without
    /// going through the Unity Console. Tests use it to assert on the category, level and
    /// message of emitted log lines without needing <c>LogAssert.Expect</c> for every single
    /// entry. <see cref="MinimumLevel"/> defaults to <see cref="LogLevel.Trace"/> so tests
    /// see everything by default.
    /// </summary>
    public sealed class RecordingDiagnosticsLogger : IDiagnosticsLogger
    {
        private readonly List<DiagnosticsLogEntry> _entries = new List<DiagnosticsLogEntry>();

        public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

        public IReadOnlyList<DiagnosticsLogEntry> Entries => _entries;

        public void Log(LogLevel level, LogCategory category, string message, object? context = null)
        {
            if (level < MinimumLevel) return;
            _entries.Add(new DiagnosticsLogEntry(level, category, message, context));
        }
    }
}
