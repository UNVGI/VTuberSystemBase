#nullable enable
using System.Collections.Generic;
using VTuberSystemBase.UiToolkitShell.Diagnostics;

namespace VTuberSystemBase.CameraSwitcherTab.Tests.TestDoubles
{
    /// <summary>
    /// Test double for <see cref="IDiagnosticsLogger"/>. Captures every log call;
    /// honours <see cref="MinimumLevel"/> by dropping below-threshold entries.
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
            Entries.Add(new Entry { Level = level, Category = category, Message = message, Context = context });
        }
    }
}
