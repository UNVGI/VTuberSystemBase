#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.StageLightingVolumeTab.Tests.TestDoubles
{
    /// <summary>
    /// In-memory <see cref="IDiagnosticsLogger"/> double. Records every log call and
    /// honours <see cref="MinimumLevel"/> filtering so tests can assert observability.
    /// (Task 1.2, Requirement 12.1)
    /// </summary>
    public sealed class FakeDiagnosticsLogger : IDiagnosticsLogger
    {
        public sealed class Entry
        {
            public LogLevel Level { get; init; }
            public LogCategory Category { get; init; }
            public string Message { get; init; } = "";
            public object? Context { get; init; }
        }

        public List<Entry> Entries { get; } = new List<Entry>();

        public LogLevel MinimumLevel { get; set; } = LogLevel.Trace;

        public void Log(LogLevel level, LogCategory category, string message, object? context = null)
        {
            if (level < MinimumLevel) return;
            Entries.Add(new Entry
            {
                Level = level,
                Category = category,
                Message = message,
                Context = context,
            });
        }
    }
}
